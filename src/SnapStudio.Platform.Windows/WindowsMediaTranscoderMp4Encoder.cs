using System.Globalization;
using SnapStudio.Core.ScreenRecording;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Streams;

namespace SnapStudio.Platform.Windows;

public sealed class WindowsMediaTranscoderMp4Encoder : IWindowsMp4Encoder
{
    private const string Pcm16Encoding = "Pcm16";
    private const string AudioCodec = "aac";
    private static readonly TimeSpan DefaultFrameDuration = TimeSpan.FromMilliseconds(1000d / 30d);

    public async Task<WindowsMp4EncodingResult> EncodeAsync(
        WindowsMp4EncodingRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Frames.Count == 0)
        {
            return WindowsMp4EncodingResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.OutputUnavailable,
                    "No video frames were captured for MP4 encoding."),
                CreateBaseDiagnostics(request));
        }

        if (!string.Equals(request.PixelFormat, "Bgra32", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.PixelFormat, "B8G8R8A8UIntNormalized", StringComparison.OrdinalIgnoreCase))
        {
            return WindowsMp4EncodingResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.EncoderUnavailable,
                    $"Pixel format '{request.PixelFormat}' is not supported by the MP4 encoder."),
                CreateBaseDiagnostics(request));
        }

        if (request.Width % 2 != 0 || request.Height % 2 != 0)
        {
            return WindowsMp4EncodingResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.EncoderUnavailable,
                    "Odd recording dimensions require scaling or padding before H.264 encoding."),
                CreateBaseDiagnostics(request));
        }

        ScreenRecordingFailure? audioFailure = ValidateAudioRequest(request);
        if (audioFailure is not null)
        {
            return WindowsMp4EncodingResult.Failed(
                audioFailure,
                CreateBaseDiagnostics(request));
        }

        try
        {
            MediaStreamSource mediaStreamSource = CreateMediaStreamSource(request);
            MediaEncodingProfile encodingProfile = CreateEncodingProfile(request);
            StorageFile outputFile = await CreateOutputFileAsync(request.OutputPath, cancellationToken)
                .ConfigureAwait(false);

            using IRandomAccessStream outputStream = await outputFile
                .OpenAsync(FileAccessMode.ReadWrite)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            var transcoder = new MediaTranscoder
            {
                HardwareAccelerationEnabled = true
            };
            PrepareTranscodeResult prepareResult = await transcoder
                .PrepareMediaStreamSourceTranscodeAsync(mediaStreamSource, outputStream, encodingProfile)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            if (!prepareResult.CanTranscode)
            {
                Dictionary<string, string> diagnostics = CreateBaseDiagnostics(request);
                diagnostics["transcodeFailureReason"] = prepareResult.FailureReason.ToString();

                return WindowsMp4EncodingResult.Failed(
                    new ScreenRecordingFailure(
                        ScreenRecordingFailureReason.EncoderUnavailable,
                        $"MediaTranscoder could not prepare H.264/MP4 encoding: {prepareResult.FailureReason}."),
                    diagnostics);
            }

            await prepareResult
                .TranscodeAsync()
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            FileInfo outputInfo = new(request.OutputPath);
            TimeSpan duration = CalculateDuration(request.Frames);
            var output = new ScreenRecordingOutput(
                request.OutputPath,
                duration,
                MakeEven(request.Width),
                MakeEven(request.Height),
                outputInfo.Exists ? outputInfo.Length : null,
                ContainerFormat: "mp4",
                VideoCodec: "h264",
                AudioCodec: request.AudioSession is null ? null : AudioCodec);

            Dictionary<string, string> successDiagnostics = CreateBaseDiagnostics(request);
            successDiagnostics["durationMilliseconds"] = duration.TotalMilliseconds.ToString(CultureInfo.InvariantCulture);
            successDiagnostics["fileSizeBytes"] = (output.FileSizeBytes ?? 0).ToString(CultureInfo.InvariantCulture);
            successDiagnostics["audioCodec"] = output.AudioCodec ?? string.Empty;

            return WindowsMp4EncodingResult.Success(output, successDiagnostics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return WindowsMp4EncodingResult.Failed(
                new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.EncoderUnavailable,
                    "MediaTranscoder failed while encoding the MP4 recording.",
                    exception),
                CreateBaseDiagnostics(request));
        }
    }

    private static MediaStreamSource CreateMediaStreamSource(WindowsMp4EncodingRequest request)
    {
        uint encodedWidth = (uint)MakeEven(request.Width);
        uint encodedHeight = (uint)MakeEven(request.Height);
        VideoEncodingProperties videoProperties = VideoEncodingProperties.CreateUncompressed(
            MediaEncodingSubtypes.Bgra8,
            encodedWidth,
            encodedHeight);
        var videoDescriptor = new VideoStreamDescriptor(videoProperties)
        {
            Label = "video",
            Name = "Video"
        };
        AudioStreamDescriptor? audioDescriptor = CreateAudioStreamDescriptor(request);
        var mediaStreamSource = audioDescriptor is null
            ? new MediaStreamSource(videoDescriptor)
            : new MediaStreamSource(videoDescriptor, audioDescriptor);
        mediaStreamSource.BufferTime = TimeSpan.FromMilliseconds(30);
        mediaStreamSource.Duration = CalculateMediaDuration(request);

        int videoSampleIndex = 0;
        int audioSampleIndex = 0;
        TimeSpan firstVideoTimestamp = request.Frames[0].Timestamp;
        TimeSpan? firstAudioTimestamp = request.AudioSamples?.Count > 0
            ? request.AudioSamples[0].Timestamp
            : null;
        TimeSpan videoFallbackDuration = EstimateFrameDuration(request.Frames);

        mediaStreamSource.Starting += (_, args) =>
        {
            args.Request.SetActualStartPosition(TimeSpan.Zero);
        };
        mediaStreamSource.SampleRequested += (_, args) =>
        {
            MediaStreamSourceSampleRequestDeferral deferral = args.Request.GetDeferral();
            try
            {
                if (args.Request.StreamDescriptor is VideoStreamDescriptor)
                {
                    SetVideoSample(
                        request,
                        args.Request,
                        ref videoSampleIndex,
                        firstVideoTimestamp,
                        videoFallbackDuration);
                    return;
                }

                if (audioDescriptor is not null
                    && args.Request.StreamDescriptor is AudioStreamDescriptor
                    && firstAudioTimestamp is not null)
                {
                    SetAudioSample(
                        request.AudioSamples!,
                        args.Request,
                        ref audioSampleIndex,
                        firstAudioTimestamp.Value);
                    return;
                }

                args.Request.Sample = null;
            }
            finally
            {
                deferral.Complete();
            }
        };

        return mediaStreamSource;
    }

    private static AudioStreamDescriptor? CreateAudioStreamDescriptor(WindowsMp4EncodingRequest request)
    {
        if (request.AudioSession is null)
        {
            return null;
        }

        AudioEncodingProperties audioProperties = AudioEncodingProperties.CreatePcm(
            (uint)request.AudioSession.SampleRate,
            (uint)request.AudioSession.ChannelCount,
            (uint)request.AudioSession.BitsPerSample);
        return new AudioStreamDescriptor(audioProperties)
        {
            Label = "audio",
            Name = "Audio"
        };
    }

    private static void SetVideoSample(
        WindowsMp4EncodingRequest request,
        MediaStreamSourceSampleRequest sampleRequest,
        ref int sampleIndex,
        TimeSpan firstTimestamp,
        TimeSpan fallbackDuration)
    {
        if (sampleIndex >= request.Frames.Count)
        {
            sampleRequest.Sample = null;
            return;
        }

        WindowsMp4BufferedFrame frame = request.Frames[sampleIndex];
        MediaStreamSample sample = MediaStreamSample.CreateFromBuffer(
            CryptographicBuffer.CreateFromByteArray(frame.PixelBytes),
            NormalizeTimestamp(frame.Timestamp - firstTimestamp));
        sample.Duration = ResolveFrameDuration(request.Frames, sampleIndex, fallbackDuration);
        sampleRequest.Sample = sample;
        sampleIndex++;
    }

    private static void SetAudioSample(
        IReadOnlyList<WindowsMp4BufferedAudioSample> audioSamples,
        MediaStreamSourceSampleRequest sampleRequest,
        ref int sampleIndex,
        TimeSpan firstTimestamp)
    {
        if (sampleIndex >= audioSamples.Count)
        {
            sampleRequest.Sample = null;
            return;
        }

        WindowsMp4BufferedAudioSample audioSample = audioSamples[sampleIndex];
        MediaStreamSample sample = MediaStreamSample.CreateFromBuffer(
            CryptographicBuffer.CreateFromByteArray(audioSample.AudioBytes),
            NormalizeTimestamp(audioSample.Timestamp - firstTimestamp));
        sample.Duration = ResolveAudioSampleDuration(audioSample);
        sampleRequest.Sample = sample;
        sampleIndex++;
    }

    private static MediaEncodingProfile CreateEncodingProfile(WindowsMp4EncodingRequest request)
    {
        VideoEncodingProperties bitrateReference = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p).Video;
        MediaEncodingProfile profile = new()
        {
            Container = new ContainerEncodingProperties
            {
                Subtype = MediaEncodingSubtypes.Mpeg4
            },
            Video = new VideoEncodingProperties
            {
                Subtype = MediaEncodingSubtypes.H264,
                Width = (uint)MakeEven(request.Width),
                Height = (uint)MakeEven(request.Height),
                Bitrate = bitrateReference.Bitrate
            }
        };
        profile.Video.FrameRate.Numerator = 30;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;

        if (request.AudioSession is not null)
        {
            profile.Audio = AudioEncodingProperties.CreateAac(
                (uint)request.AudioSession.SampleRate,
                (uint)request.AudioSession.ChannelCount,
                EstimateAudioBitrate(request.AudioSession));
        }

        return profile;
    }

    private static async Task<StorageFile> CreateOutputFileAsync(
        string outputPath,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("The MP4 output directory is missing.");
        }

        StorageFolder outputFolder = await StorageFolder
            .GetFolderFromPathAsync(directory)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        return await outputFolder
            .CreateFileAsync(Path.GetFileName(outputPath), CreationCollisionOption.ReplaceExisting)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
    }

    private static TimeSpan CalculateDuration(IReadOnlyList<WindowsMp4BufferedFrame> frames)
    {
        if (frames.Count < 2)
        {
            return frames.Count == 0 ? TimeSpan.Zero : DefaultFrameDuration;
        }

        TimeSpan duration = frames[^1].Timestamp - frames[0].Timestamp;
        if (duration <= TimeSpan.Zero)
        {
            return TimeSpan.FromTicks(DefaultFrameDuration.Ticks * frames.Count);
        }

        return duration + ResolveFrameDuration(frames, frames.Count - 1, EstimateFrameDuration(frames));
    }

    private static TimeSpan CalculateMediaDuration(WindowsMp4EncodingRequest request)
    {
        TimeSpan videoDuration = CalculateDuration(request.Frames);
        TimeSpan audioDuration = CalculateAudioDuration(request.AudioSamples);

        return audioDuration > videoDuration ? audioDuration : videoDuration;
    }

    private static TimeSpan CalculateAudioDuration(IReadOnlyList<WindowsMp4BufferedAudioSample>? audioSamples)
    {
        if (audioSamples is null || audioSamples.Count == 0)
        {
            return TimeSpan.Zero;
        }

        TimeSpan firstTimestamp = audioSamples[0].Timestamp;
        TimeSpan lastTimestamp = audioSamples[^1].Timestamp;
        TimeSpan duration = lastTimestamp - firstTimestamp + ResolveAudioSampleDuration(audioSamples[^1]);

        if (duration > TimeSpan.Zero)
        {
            return duration;
        }

        long totalTicks = audioSamples
            .Where(sample => sample.Duration > TimeSpan.Zero)
            .Sum(sample => sample.Duration.Ticks);

        return totalTicks > 0 ? TimeSpan.FromTicks(totalTicks) : TimeSpan.Zero;
    }

    private static TimeSpan ResolveAudioSampleDuration(WindowsMp4BufferedAudioSample sample)
    {
        if (sample.Duration > TimeSpan.Zero)
        {
            return sample.Duration;
        }

        int bytesPerSample = Math.Max(1, sample.BitsPerSample / 8);
        int bytesPerSecond = sample.SampleRate * sample.ChannelCount * bytesPerSample;
        if (bytesPerSecond <= 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(sample.AudioBytes.LongLength / (double)bytesPerSecond);
    }

    private static uint EstimateAudioBitrate(ScreenRecordingAudioSourceSession audioSession)
    {
        int channelCount = Math.Max(1, audioSession.ChannelCount);
        return (uint)Math.Clamp(96000 * channelCount, 96000, 192000);
    }

    private static TimeSpan EstimateFrameDuration(IReadOnlyList<WindowsMp4BufferedFrame> frames)
    {
        if (frames.Count < 2)
        {
            return DefaultFrameDuration;
        }

        TimeSpan totalDelta = TimeSpan.Zero;
        int deltaCount = 0;
        for (int index = 1; index < frames.Count; index++)
        {
            TimeSpan delta = frames[index].Timestamp - frames[index - 1].Timestamp;
            if (delta > TimeSpan.Zero)
            {
                totalDelta += delta;
                deltaCount++;
            }
        }

        return deltaCount == 0
            ? DefaultFrameDuration
            : TimeSpan.FromTicks(totalDelta.Ticks / deltaCount);
    }

    private static TimeSpan ResolveFrameDuration(
        IReadOnlyList<WindowsMp4BufferedFrame> frames,
        int index,
        TimeSpan fallbackDuration)
    {
        if (index + 1 < frames.Count)
        {
            TimeSpan nextDuration = frames[index + 1].Timestamp - frames[index].Timestamp;
            if (nextDuration > TimeSpan.Zero)
            {
                return nextDuration;
            }
        }

        return fallbackDuration > TimeSpan.Zero ? fallbackDuration : DefaultFrameDuration;
    }

    private static TimeSpan NormalizeTimestamp(TimeSpan timestamp)
    {
        return timestamp < TimeSpan.Zero ? TimeSpan.Zero : timestamp;
    }

    private static ScreenRecordingFailure? ValidateAudioRequest(WindowsMp4EncodingRequest request)
    {
        bool hasAudioSamples = request.AudioSamples is { Count: > 0 };

        if (request.AudioSession is null)
        {
            return hasAudioSamples
                ? new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.InvalidRequest,
                    "MP4 audio samples require an audio source session.")
                : null;
        }

        if (!hasAudioSamples)
        {
            return new ScreenRecordingFailure(
                ScreenRecordingFailureReason.AudioUnavailable,
                "No audio samples were captured for MP4 muxing.");
        }

        if (!string.Equals(request.AudioSession.Encoding, Pcm16Encoding, StringComparison.OrdinalIgnoreCase)
            || request.AudioSession.BitsPerSample != 16)
        {
            return new ScreenRecordingFailure(
                ScreenRecordingFailureReason.EncoderUnavailable,
                $"Audio format '{request.AudioSession.Encoding}' with {request.AudioSession.BitsPerSample} bits per sample is not supported by the MP4 encoder.");
        }

        foreach (WindowsMp4BufferedAudioSample sample in request.AudioSamples!)
        {
            if (sample.SampleRate != request.AudioSession.SampleRate
                || sample.ChannelCount != request.AudioSession.ChannelCount
                || sample.BitsPerSample != request.AudioSession.BitsPerSample
                || !string.Equals(sample.Encoding, request.AudioSession.Encoding, StringComparison.OrdinalIgnoreCase))
            {
                return new ScreenRecordingFailure(
                    ScreenRecordingFailureReason.InvalidRequest,
                    "An audio sample format does not match the recording audio source session.");
            }
        }

        return null;
    }

    private static int MakeEven(int value)
    {
        return value % 2 == 0 ? value : value + 1;
    }

    private static Dictionary<string, string> CreateBaseDiagnostics(WindowsMp4EncodingRequest request)
    {
        var diagnostics = new Dictionary<string, string>
        {
            ["encoder"] = nameof(WindowsMediaTranscoderMp4Encoder),
            ["outputPath"] = request.OutputPath,
            ["framesReceived"] = request.Frames.Count.ToString(CultureInfo.InvariantCulture),
            ["audioSamplesReceived"] = (request.AudioSamples?.Count ?? 0).ToString(CultureInfo.InvariantCulture),
            ["width"] = request.Width.ToString(CultureInfo.InvariantCulture),
            ["height"] = request.Height.ToString(CultureInfo.InvariantCulture),
            ["encodedWidth"] = MakeEven(request.Width).ToString(CultureInfo.InvariantCulture),
            ["encodedHeight"] = MakeEven(request.Height).ToString(CultureInfo.InvariantCulture),
            ["pixelFormat"] = request.PixelFormat,
            ["startedAtUtc"] = request.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture),
            ["stoppedAtUtc"] = request.StoppedAtUtc.ToString("O", CultureInfo.InvariantCulture)
        };

        if (request.AudioSession is not null)
        {
            diagnostics["audioSampleRate"] = request.AudioSession.SampleRate.ToString(CultureInfo.InvariantCulture);
            diagnostics["audioChannelCount"] = request.AudioSession.ChannelCount.ToString(CultureInfo.InvariantCulture);
            diagnostics["audioBitsPerSample"] = request.AudioSession.BitsPerSample.ToString(CultureInfo.InvariantCulture);
            diagnostics["audioEncoding"] = request.AudioSession.Encoding;
        }

        return diagnostics;
    }
}

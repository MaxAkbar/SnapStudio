namespace SnapStudio.Core.Integration;

/// <summary>Small, local command surface shared by optional launchers.</summary>
public sealed record WorkspaceCommandRequest(int ProtocolVersion, Guid RequestId, string Command)
{
    public const string CaptureRegion = "capture-region";
    public const string EditClipboard = "edit-clipboard";
    public const string OpenLatest = "open-latest";

    public void Validate()
    {
        if (ProtocolVersion != 1 || RequestId == Guid.Empty)
            throw new InvalidDataException("Unsupported workspace command request.");
        if (Command is not (CaptureRegion or EditClipboard or OpenLatest))
            throw new InvalidDataException("Unknown workspace command. Update the SnapStudio companion and try again.");
    }
}

public sealed record WorkspaceCommandResponse(int ProtocolVersion, Guid RequestId, bool Succeeded, string? Error, Guid? DocumentId = null);

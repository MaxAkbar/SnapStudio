[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("x86", "x64", "ARM64")]
    [string]$Platform = "x64",

    [int]$TimeoutSeconds = 45,

    [switch]$SkipBuild,

    [switch]$Launch,

    [switch]$KeepAppOpen,

    [switch]$RestoreSettingsBackup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-CheckedProcess {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    Write-Host ">> $FilePath $($Arguments -join ' ')"
    & $FilePath @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "'$FilePath' failed with exit code $LASTEXITCODE."
    }
}

function Get-RuntimeIdentifier {
    param(
        [Parameter(Mandatory)]
        [string]$Architecture
    )

    switch ($Architecture) {
        "x86" { return "win-x86" }
        "x64" { return "win-x64" }
        "ARM64" { return "win-arm64" }
        default { throw "Unsupported platform '$Architecture'." }
    }
}

function Get-SettingsPath {
    return Join-Path $env:LOCALAPPDATA "SnapStudio\settings.json"
}

function Get-DefaultSettings {
    $appDataRoot = Join-Path $env:LOCALAPPDATA "SnapStudio"

    return [ordered]@{
        schemaVersion = 2
        storageRoot = Join-Path $appDataRoot "Documents"
        captureHotkey = "PrintScreen"
        includeCursorByDefault = $true
        copyCapturesToClipboard = $false
        firstRunCompleted = $true
        featureFlags = [pscustomobject][ordered]@{
            "Capture.WgcStill" = $true
            "Capture.GdiFallback" = $false
            "Capture.Delayed" = $true
            "Capture.IncludeCursor" = $true
            "Editor.BlurTool" = $true
            "Editor.PdfExport" = $true
            "Workspace.PinToScreen" = $true
            "V1.Ocr" = $false
            "V1.ScrollingCapture" = $false
            "V1.ScreenRecording" = $true
            "V2.SmartRedact" = $false
            "V2.PluginSdk" = $false
        }
    }
}

function Set-JsonProperty {
    param(
        [Parameter(Mandatory)]
        [psobject]$Object,

        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [object]$Value
    )

    if ($Object.PSObject.Properties.Name -contains $Name) {
        $Object.$Name = $Value
        return
    }

    $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
}

function Enable-ScreenRecordingFeatureFlag {
    $settingsPath = Get-SettingsPath
    $settingsDirectory = Split-Path -Parent $settingsPath
    New-Item -ItemType Directory -Force -Path $settingsDirectory | Out-Null

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupPath = Join-Path $settingsDirectory "settings.json.recording-smoke.$timestamp.bak"

    if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
        Copy-Item -LiteralPath $settingsPath -Destination $backupPath -Force
        $settings = Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json
    }
    else {
        $settings = [pscustomobject](Get-DefaultSettings)
        $backupPath = ""
    }

    if (-not ($settings.PSObject.Properties.Name -contains "featureFlags") -or $null -eq $settings.featureFlags) {
        Set-JsonProperty -Object $settings -Name "featureFlags" -Value ([pscustomobject]@{})
    }

    Set-JsonProperty -Object $settings.featureFlags -Name "V1.ScreenRecording" -Value $true
    Set-JsonProperty -Object $settings -Name "firstRunCompleted" -Value $true

    $json = $settings | ConvertTo-Json -Depth 8
    Set-Content -LiteralPath $settingsPath -Value $json -Encoding UTF8

    Write-Host "Enabled V1.ScreenRecording in $settingsPath"
    if (-not [string]::IsNullOrWhiteSpace($backupPath)) {
        Write-Host "Settings backup: $backupPath"
    }
}

function Restore-LatestSettingsBackup {
    $settingsPath = Get-SettingsPath
    $settingsDirectory = Split-Path -Parent $settingsPath

    $backup = Get-ChildItem -LiteralPath $settingsDirectory -Filter "settings.json.recording-smoke.*.bak" -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if ($null -eq $backup) {
        throw "No recording smoke settings backup was found under '$settingsDirectory'."
    }

    Copy-Item -LiteralPath $backup.FullName -Destination $settingsPath -Force
    Write-Host "Restored settings from $($backup.FullName)"
}

function Wait-ForWindow {
    param(
        [Parameter(Mandatory)]
        [int]$ProcessId,

        [Parameter(Mandatory)]
        [int]$TimeoutSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $processCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $ProcessId)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            $processCondition)

        if ($null -ne $window) {
            return $window
        }

        Start-Sleep -Milliseconds 250
    }

    throw "SnapStudio window was not found for process id $ProcessId."
}

function Find-ElementByName {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement]$Root,

        [Parameter(Mandatory)]
        [string]$Name
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)

    return $Root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
}

function Assert-ElementByName {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement]$Root,

        [Parameter(Mandatory)]
        [string]$Name
    )

    $element = Find-ElementByName -Root $Root -Name $Name
    if ($null -eq $element) {
        throw "Expected UI Automation element '$Name' was not found."
    }

    Write-Host "Found: $Name"
    return $element
}

function Invoke-Element {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement]$Element
    )

    $pattern = $null
    if ($Element.TryGetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern,
        [ref]$pattern)) {
        $pattern.Invoke()
        return $true
    }

    return $false
}

function Start-SnapStudioAndProbe {
    param(
        [Parameter(Mandatory)]
        [string]$AppExe
    )

    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes

    $process = Start-Process -FilePath $AppExe -PassThru
    try {
        $window = Wait-ForWindow -ProcessId $process.Id -TimeoutSeconds $TimeoutSeconds
        Write-Host "Window: $($window.Current.Name)"

        [void](Assert-ElementByName -Root $window -Name "Recording audio")

        $capture = Assert-ElementByName -Root $window -Name "Capture"
        if (-not (Invoke-Element -Element $capture)) {
            throw "Capture command was not directly invokable."
        }

        Start-Sleep -Milliseconds 750
        [void](Assert-ElementByName -Root $window -Name "Start screen recording")
        Write-Host "Recording UI smoke passed."
    }
    finally {
        if (-not $KeepAppOpen) {
            if (-not $process.HasExited) {
                [void]$process.CloseMainWindow()
                if (-not $process.WaitForExit(5000)) {
                    $process.Kill()
                    $process.WaitForExit()
                }
            }
        }
    }
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtimeIdentifier = Get-RuntimeIdentifier -Architecture $Platform
$appProject = Join-Path $repoRoot "src\SnapStudio.App\SnapStudio.App.csproj"
$targetFramework = "net10.0-windows10.0.26100.0"

if ($RestoreSettingsBackup) {
    Restore-LatestSettingsBackup
    return
}

Enable-ScreenRecordingFeatureFlag

if (-not $SkipBuild) {
    Invoke-CheckedProcess -FilePath "dotnet" -Arguments @(
        "build",
        $appProject,
        "--configuration",
        $Configuration,
        "-p:Platform=$Platform"
    )
}

$appExe = Join-Path $repoRoot "src\SnapStudio.App\bin\$Platform\$Configuration\$targetFramework\$runtimeIdentifier\SnapStudio.App.exe"
if (-not (Test-Path -LiteralPath $appExe -PathType Leaf)) {
    throw "SnapStudio.App.exe was not found at '$appExe'. Build the app first or check -Configuration/-Platform."
}

if ($Launch) {
    Start-SnapStudioAndProbe -AppExe $appExe
}

Write-Host ""
Write-Host "Manual screen recording smoke steps:"
Write-Host "1. Launch SnapStudio if it is not already open."
Write-Host "2. Confirm the Recording audio selector is visible."
Write-Host "3. With Recording audio set to None, choose Capture > Record Screen."
Write-Host "4. Pick a normal window or display in the Windows capture picker."
Write-Host "5. Wait 3 to 5 seconds while changing visible content in the selected target."
Write-Host "6. Choose Capture > Stop Recording."
Write-Host "7. Confirm the success InfoBar reports an MP4 path under the configured storage root's Recordings folder."
Write-Host "8. Play the MP4 and confirm it is non-empty, contains the selected target, and has no audio track."
Write-Host "9. Repeat short recordings with Recording audio set to Microphone and System audio."
Write-Host "10. Confirm %LOCALAPPDATA%\SnapStudio\Logs\snapstudio.jsonl receives SnapStudio.App.ScreenRecording entries with requestedAudioMode."
Write-Host "11. Restore settings later with: .\eng\run-recording-smoke.ps1 -RestoreSettingsBackup"

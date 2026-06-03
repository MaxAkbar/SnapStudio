[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("x86", "x64", "ARM64")]
    [string]$Platform = "x64",

    [int]$TimeoutSeconds = 45,

    [switch]$SkipPackageBuild,

    [switch]$RegisterLayout,

    [switch]$Launch,

    [switch]$KeepAppOpen,

    [switch]$RestoreSettingsBackup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-FullPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    return [System.IO.Path]::GetFullPath($Path)
}

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
            "V1.Ocr" = $true
            "V1.ScrollingCapture" = $false
            "V1.ScreenRecording" = $false
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

function Enable-OcrFeatureFlag {
    $settingsPath = Get-SettingsPath
    $settingsDirectory = Split-Path -Parent $settingsPath
    New-Item -ItemType Directory -Force -Path $settingsDirectory | Out-Null

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupPath = Join-Path $settingsDirectory "settings.json.ocr-smoke.$timestamp.bak"

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

    Set-JsonProperty -Object $settings.featureFlags -Name "V1.Ocr" -Value $true
    Set-JsonProperty -Object $settings -Name "firstRunCompleted" -Value $true

    $json = $settings | ConvertTo-Json -Depth 8
    Set-Content -LiteralPath $settingsPath -Value $json -Encoding UTF8

    Write-Host "Enabled V1.Ocr in $settingsPath"
    if (-not [string]::IsNullOrWhiteSpace($backupPath)) {
        Write-Host "Settings backup: $backupPath"
    }
}

function Restore-LatestSettingsBackup {
    $settingsPath = Get-SettingsPath
    $settingsDirectory = Split-Path -Parent $settingsPath

    $backup = Get-ChildItem -LiteralPath $settingsDirectory -Filter "settings.json.ocr-smoke.*.bak" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $backup) {
        throw "No OCR smoke settings backup was found under '$settingsDirectory'."
    }

    Copy-Item -LiteralPath $backup.FullName -Destination $settingsPath -Force
    Write-Host "Restored settings from $($backup.FullName)"
}

function New-OcrSampleImage {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    Add-Type -AssemblyName System.Drawing

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null

    $bitmap = New-Object System.Drawing.Bitmap 1000, 420
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $titleFont = New-Object System.Drawing.Font -ArgumentList "Segoe UI", 54, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $bodyFont = New-Object System.Drawing.Font -ArgumentList "Segoe UI", 38, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(20, 20, 20))
    $accentBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0, 95, 184))

    try {
        $graphics.Clear([System.Drawing.Color]::White)
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
        $graphics.DrawString("SNAPSTUDIO OCR SMOKE TEST", $titleFont, $brush, 60, 70)
        $graphics.DrawString("Copy Text 2026", $bodyFont, $accentBrush, 60, 175)
        $graphics.DrawString("Local OCR cache: PASS", $bodyFont, $brush, 60, 250)
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $accentBrush.Dispose()
        $brush.Dispose()
        $bodyFont.Dispose()
        $titleFont.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }

    Write-Host "OCR sample image: $Path"
}

function Write-OcrLanguageStatus {
    $winRtError = $null
    try {
        [void][Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
        $languages = @([Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages)
        if ($languages.Count -eq 0) {
            Write-Warning "No Windows OCR recognizer languages were reported. Install an OCR language before smoke testing."
            return
        }

        Write-Host "Windows OCR recognizer languages:"
        foreach ($language in $languages) {
            Write-Host "  $($language.LanguageTag)"
        }
    }
    catch {
        $winRtError = $_.Exception.Message
    }

    try {
        if ($null -eq (Get-Command Get-WindowsCapability -ErrorAction SilentlyContinue)) {
            throw "Get-WindowsCapability is unavailable in this PowerShell host."
        }

        $capabilities = @(Get-WindowsCapability -Online -Name "Language.OCR*" -ErrorAction Stop)
        $installedCapabilities = @($capabilities | Where-Object { $_.State -eq "Installed" })
        if ($installedCapabilities.Count -eq 0) {
            Write-Warning "No installed Windows OCR language capabilities were reported."
            return
        }

        Write-Host "Installed Windows OCR language capabilities:"
        foreach ($capability in $installedCapabilities) {
            Write-Host "  $($capability.Name)"
        }
    }
    catch {
        $details = if ([string]::IsNullOrWhiteSpace($winRtError)) {
            $_.Exception.Message
        }
        else {
            "WinRT query failed: $winRtError; Windows capability query failed: $($_.Exception.Message)"
        }

        Write-Warning "Could not query Windows OCR language availability: $details"
    }
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

function Assert-ElementByName {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement]$Root,

        [Parameter(Mandatory)]
        [string]$Name
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    $element = $Root.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)

    if ($null -eq $element) {
        throw "Expected UI Automation element '$Name' was not found."
    }

    Write-Host "Found: $Name"
}

function Register-PackageLayout {
    param(
        [Parameter(Mandatory)]
        [string]$ManifestPath
    )

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        throw "Package manifest was not found at '$ManifestPath'. Run without -SkipPackageBuild first."
    }

    Write-Host "Registering package layout: $ManifestPath"
    Add-AppxPackage -Register $ManifestPath -ForceApplicationShutdown
}

function Start-PackagedSnapStudio {
    param(
        [Parameter(Mandatory)]
        [string]$ManifestPath
    )

    [xml]$manifest = Get-Content -Raw -LiteralPath $ManifestPath
    $namespaceManager = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
    $namespaceManager.AddNamespace("appx", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
    $identity = $manifest.SelectSingleNode("/appx:Package/appx:Identity", $namespaceManager)
    if ($null -eq $identity) {
        throw "Package manifest does not contain an Identity element."
    }

    $packageName = $identity.GetAttribute("Name")
    $package = Get-AppxPackage -Name $packageName |
        Sort-Object InstallDate -Descending |
        Select-Object -First 1

    if ($null -eq $package) {
        throw "Package '$packageName' is not registered. Use -RegisterLayout first."
    }

    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes

    $existingProcessIds = @(Get-Process -Name "SnapStudio.App" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
    $appUserModelId = "$($package.PackageFamilyName)!App"
    Write-Host "Launching packaged app: $appUserModelId"
    Start-Process "shell:AppsFolder\$appUserModelId"

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $process = $null
    while ([DateTimeOffset]::UtcNow -lt $deadline -and $null -eq $process) {
        $process = Get-Process -Name "SnapStudio.App" -ErrorAction SilentlyContinue |
            Where-Object { $existingProcessIds -notcontains $_.Id } |
            Sort-Object StartTime -Descending |
            Select-Object -First 1

        if ($null -eq $process) {
            Start-Sleep -Milliseconds 250
        }
    }

    if ($null -eq $process) {
        throw "Packaged SnapStudio process did not start."
    }

    try {
        $window = Wait-ForWindow -ProcessId $process.Id -TimeoutSeconds $TimeoutSeconds
        Write-Host "Window: $($window.Current.Name)"
        Assert-ElementByName -Root $window -Name "Copy recognized text"
        Write-Host "OCR UI smoke passed."
    }
    finally {
        if (-not $KeepAppOpen -and -not $process.HasExited) {
            [void]$process.CloseMainWindow()
            if (-not $process.WaitForExit(5000)) {
                $process.Kill()
                $process.WaitForExit()
            }
        }
    }
}

$repoRoot = Get-FullPath (Join-Path $PSScriptRoot "..")
$samplePath = Join-Path $repoRoot "artifacts\ocr-smoke\ocr-sample.png"
$manifestPath = Join-Path $repoRoot "artifacts\msix\layout\$Platform\AppxManifest.xml"

if ($RestoreSettingsBackup) {
    Restore-LatestSettingsBackup
    return
}

Enable-OcrFeatureFlag
New-OcrSampleImage -Path $samplePath
Write-OcrLanguageStatus

if (-not $SkipPackageBuild) {
    Invoke-CheckedProcess -FilePath "powershell" -Arguments @(
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        (Join-Path $PSScriptRoot "package-msix.ps1"),
        "-Platform",
        $Platform,
        "-Configuration",
        $Configuration
    )
}

if ($RegisterLayout) {
    Register-PackageLayout -ManifestPath $manifestPath
}

if ($Launch) {
    Start-PackagedSnapStudio -ManifestPath $manifestPath
}

Write-Host ""
Write-Host "Manual OCR smoke steps:"
Write-Host "1. Launch the packaged app if it is not already open."
Write-Host "2. Use Open and select: $samplePath"
Write-Host "3. Confirm the Copy Text command is visible in the Tools panel."
Write-Host "4. Click Copy Text and paste into Notepad."
Write-Host "5. Confirm pasted text includes: SNAPSTUDIO OCR SMOKE TEST"
Write-Host "6. Run the command again and confirm cached copy status is reported."
Write-Host "7. Restore settings later with: .\eng\run-ocr-smoke.ps1 -RestoreSettingsBackup"

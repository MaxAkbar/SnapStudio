[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("x86", "x64", "ARM64")]
    [string]$Platform = "x64",

    [int]$TimeoutSeconds = 45,

    [switch]$SkipBuild,

    [switch]$KeepAppOpen
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

function Assert-AnyElementByName {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement]$Root,

        [Parameter(Mandatory)]
        [string[]]$Names
    )

    foreach ($name in $Names) {
        $element = Find-ElementByName -Root $Root -Name $name
        if ($null -ne $element) {
            Write-Host "Found: $name"
            return $element
        }
    }

    throw "Expected one of these UI Automation elements was not found: $($Names -join ', ')."
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

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$runtimeIdentifier = Get-RuntimeIdentifier -Architecture $Platform
$appProject = Join-Path $repoRoot "src\SnapStudio.App\SnapStudio.App.csproj"
$targetFramework = "net10.0-windows10.0.26100.0"

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

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$process = Start-Process -FilePath $appExe -PassThru
try {
    $window = Wait-ForWindow -ProcessId $process.Id -TimeoutSeconds $TimeoutSeconds
    Write-Host "Window: $($window.Current.Name)"

    $requiredNames = @(
        "Capture",
        "Open image",
        "Copy current capture",
        "Export",
        "Search capture history",
        "Capture history",
        "Include cursor",
        "Capture delay",
        "Capture hotkey",
        "Right controls scroll area",
        "Remove current capture from history",
        "Move current capture to recycle bin",
        "Selected capture properties",
        "Selected capture width",
        "Selected capture height",
        "Selected capture file size",
        "Add selected annotation tool",
        "Delete selected annotation",
        "Annotation tool",
        "Annotation color",
        "Annotation size",
        "Annotation opacity",
        "Crop to selected annotation",
        "Resize unit",
        "Resize width",
        "Resize height",
        "Lock resize aspect ratio",
        "Resize document"
    )

    foreach ($name in $requiredNames) {
        [void](Assert-ElementByName -Root $window -Name $name)
    }

    [void](Assert-AnyElementByName -Root $window -Names @("Editor canvas", "Settings capture hotkey"))

    if ($null -eq (Find-ElementByName -Root $window -Name "Settings capture hotkey")) {
        $settings = Find-ElementByName -Root $window -Name "Settings"
        if ($null -ne $settings -and (Invoke-Element -Element $settings)) {
            Start-Sleep -Milliseconds 750
        }
        else {
            Write-Warning "Settings command was not directly invokable. First-run/setup controls were not probed."
        }
    }

    if ($null -ne (Find-ElementByName -Root $window -Name "Settings capture hotkey")) {
        [void](Assert-ElementByName -Root $window -Name "Settings capture hotkey")
        [void](Assert-ElementByName -Root $window -Name "Settings include cursor")
        [void](Assert-ElementByName -Root $window -Name "Settings copy captures to clipboard")
        [void](Assert-ElementByName -Root $window -Name "Settings storage location")
        [void](Assert-ElementByName -Root $window -Name "Choose storage location")
        [void](Assert-ElementByName -Root $window -Name "Enable still capture")
        [void](Assert-ElementByName -Root $window -Name "Enable OCR")
        [void](Assert-ElementByName -Root $window -Name "Enable scrolling capture")
        [void](Assert-ElementByName -Root $window -Name "Enable screen recording")
        [void](Assert-AnyElementByName -Root $window -Names @("Finish setup", "Save settings"))
    }

    Write-Host "UI smoke passed."
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

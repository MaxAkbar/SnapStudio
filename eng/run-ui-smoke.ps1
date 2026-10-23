[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("x86", "x64", "ARM64")]
    [string]$Platform = "x64",

    [int]$TimeoutSeconds = 45,

    [string]$AppExecutablePath,

    [switch]$SkipBuild,

    [switch]$CreateTransparentImage,

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

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(5)
    do {
        $element = Find-ElementByName -Root $Root -Name $Name
        if ($null -ne $element) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
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
if (-not [string]::IsNullOrWhiteSpace($AppExecutablePath)) {
    $appExe = [System.IO.Path]::GetFullPath($AppExecutablePath)
}

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
        "New image",
        "Paste image",
        "Select annotations",
        "Pen tool",
        "Fill bucket tool",
        "Copy current capture",
        "Export",
        "Search capture history",
        "Capture history",
        "Annotation layers",
        "Delete selected layer",
        "Resize layers and captures pane",
        "Unpin layers and captures pane",
        "Include cursor",
        "Capture delay",
        "Capture hotkey",
        "Right controls scroll area",
        "Remove current capture from history",
        "Move current capture to recycle bin",
        "Resize right tools pane",
        "Unpin right tools pane",
        "Selected capture properties",
        "Selected capture width",
        "Selected capture height",
        "Selected capture file size",
        "Add selected annotation tool",
        "Delete selected annotation",
        "Annotation tool",
        "Annotation color",
        "Annotation size",
        "Rectangle corner style",
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

    $newImage = Assert-ElementByName -Root $window -Name "New image"
    if (-not (Invoke-Element -Element $newImage)) {
        throw "Unable to invoke the New image button."
    }
    Start-Sleep -Milliseconds 350
    $newImageWidth = Assert-ElementByName -Root $window -Name "New image width"
    $newImageHeight = Assert-ElementByName -Root $window -Name "New image height"
    $createNewImage = Assert-ElementByName -Root $window -Name "Create"
    $widthValue = $newImageWidth.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    $heightValue = $newImageHeight.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    $widthValue.SetValue(20000)
    $heightValue.SetValue(20000)
    Start-Sleep -Milliseconds 250
    if ($createNewImage.Current.IsEnabled) {
        throw "The New image dialog accepted dimensions above the pixel limit."
    }
    $widthValue.SetValue(1280)
    $heightValue.SetValue(720)
    Start-Sleep -Milliseconds 250
    if (-not $createNewImage.Current.IsEnabled) {
        throw "The New image dialog rejected valid dimensions."
    }
    if ($CreateTransparentImage) {
        if (-not (Invoke-Element -Element $createNewImage)) {
            throw "Unable to create the transparent image."
        }
        [void](Assert-ElementByName -Root $window -Name "Untitled image")
        Start-Sleep -Milliseconds 500
        if ($process.HasExited) {
            throw "SnapStudio exited while displaying the transparent image."
        }
    }
    else {
        $cancelNewImage = Assert-ElementByName -Root $window -Name "Cancel"
        if (-not (Invoke-Element -Element $cancelNewImage)) {
            throw "Unable to cancel the New image dialog."
        }
    }
    Start-Sleep -Milliseconds 250

    $unpinToolsPane = Assert-ElementByName -Root $window -Name "Unpin right tools pane"
    if (-not (Invoke-Element -Element $unpinToolsPane)) {
        throw "Unable to invoke the right tools pane unpin button."
    }

    Start-Sleep -Milliseconds 250
    $pinToolsPane = Assert-ElementByName -Root $window -Name "Pin right tools pane"
    if (-not (Invoke-Element -Element $pinToolsPane)) {
        throw "Unable to invoke the right tools pane pin button."
    }

    Start-Sleep -Milliseconds 250
    [void](Assert-ElementByName -Root $window -Name "Unpin right tools pane")

    $leftSplitter = Assert-ElementByName -Root $window -Name "Resize layers and captures pane"
    $leftSplitterPosition = $leftSplitter.Current.BoundingRectangle.Left
    $unpinLeftPane = Assert-ElementByName -Root $window -Name "Unpin layers and captures pane"
    if (-not (Invoke-Element -Element $unpinLeftPane)) {
        throw "Unable to invoke the layers and captures pane unpin button."
    }

    Start-Sleep -Milliseconds 250
    $pinLeftPane = Assert-ElementByName -Root $window -Name "Pin layers and captures pane"
    if (-not (Invoke-Element -Element $pinLeftPane)) {
        throw "Unable to invoke the layers and captures pane pin button."
    }

    Start-Sleep -Milliseconds 250
    [void](Assert-ElementByName -Root $window -Name "Unpin layers and captures pane")
    $restoredLeftSplitter = Assert-ElementByName -Root $window -Name "Resize layers and captures pane"
    if ([Math]::Abs($restoredLeftSplitter.Current.BoundingRectangle.Left - $leftSplitterPosition) -gt 3) {
        throw "The layers and captures pane did not restore its previous width."
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
        [void](Assert-ElementByName -Root $window -Name "Settings storage backend")
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

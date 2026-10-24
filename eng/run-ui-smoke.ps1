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

function Set-ElementExpanded {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement]$Element,

        [Parameter(Mandatory)]
        [bool]$Expanded
    )

    $pattern = $null
    if ($Element.TryGetCurrentPattern(
        [System.Windows.Automation.ExpandCollapsePattern]::Pattern,
        [ref]$pattern)) {
        $targetState = if ($Expanded) {
            [System.Windows.Automation.ExpandCollapseState]::Expanded
        }
        else {
            [System.Windows.Automation.ExpandCollapseState]::Collapsed
        }
        if ($pattern.Current.ExpandCollapseState -ne $targetState) {
            if ($Expanded) { $pattern.Expand() }
            else { $pattern.Collapse() }
        }
        return $true
    }

    # Some WinUI versions expose only the expander header's Invoke pattern.
    if (Invoke-Element -Element $Element) { return $true }
    $buttonCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $header = $Element.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $buttonCondition)
    return $null -ne $header -and (Invoke-Element -Element $header)
}

function Set-SectionExpanded {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement]$Root,

        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [bool]$Expanded
    )

    $section = Assert-ElementByName -Root $Root -Name $Name
    if (-not (Set-ElementExpanded -Element $section -Expanded $Expanded)) {
        throw "Unable to change the '$Name' section expansion state."
    }
    Start-Sleep -Milliseconds 250
}

function Close-ElementFlyout {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement]$Element
    )

    $pattern = $null
    if ($Element.TryGetCurrentPattern(
        [System.Windows.Automation.ExpandCollapsePattern]::Pattern,
        [ref]$pattern)) {
        $pattern.Collapse()
    }
    else {
        # Plain WinUI Button flyouts may not expose an ExpandCollapse pattern.
        $Element.SetFocus()
        [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
    }
    Start-Sleep -Milliseconds 250
}

function Test-ElementVisible {
    param(
        [Parameter(Mandatory)]
        [System.Windows.Automation.AutomationElement]$Root,

        [Parameter(Mandatory)]
        [string]$Name
    )

    $element = Find-ElementByName -Root $Root -Name $Name
    return $null -ne $element -and -not $element.Current.IsOffscreen `
        -and $element.Current.BoundingRectangle.Width -gt 0 `
        -and $element.Current.BoundingRectangle.Height -gt 0
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
Add-Type -AssemblyName System.Windows.Forms

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
        "Capture folders",
        "Add mapped folder",
        "Refresh mapped folders",
        "Manage capture folders",
        "Capture history",
        "Annotation layers",
        "Delete selected layer",
        "Resize layers and captures pane",
        "Unpin layers and captures pane",
        "Resize right tools pane",
        "Unpin right tools pane",
        "Properties",
        "Image details",
        "Capture options",
        "Manage current capture",
        "Focus mode",
        "Add selected annotation tool",
        "Delete selected annotation",
        "Crop to selected annotation"
    )

    foreach ($name in $requiredNames) {
        [void](Assert-ElementByName -Root $window -Name $name)
    }

    # Tool-specific properties are realized only for the selected tool.
    $rectangleTool = Assert-ElementByName -Root $window -Name "Rectangle tool"
    if (-not (Invoke-Element -Element $rectangleTool)) {
        throw "Unable to select the Rectangle tool."
    }
    Start-Sleep -Milliseconds 250
    foreach ($name in @("Annotation color", "Annotation opacity")) {
        [void](Assert-ElementByName -Root $window -Name $name)
    }
    Set-SectionExpanded -Root $window -Name "More tool options" -Expanded $true
    [void](Assert-ElementByName -Root $window -Name "Annotation size")
    [void](Assert-ElementByName -Root $window -Name "Rectangle corner style")
    [void](Assert-ElementByName -Root $window -Name "Annotation preset")
    [void](Assert-ElementByName -Root $window -Name "Annotation tool")
    Set-SectionExpanded -Root $window -Name "More tool options" -Expanded $false

    $textTool = Assert-ElementByName -Root $window -Name "Text tool"
    if (-not (Invoke-Element -Element $textTool)) {
        throw "Unable to select the Text tool."
    }
    Start-Sleep -Milliseconds 250
    [void](Assert-ElementByName -Root $window -Name "Text font")
    [void](Assert-ElementByName -Root $window -Name "Annotation text")
    $rectangleTool = Assert-ElementByName -Root $window -Name "Rectangle tool"
    if (-not (Invoke-Element -Element $rectangleTool)) {
        throw "Unable to restore the Rectangle tool."
    }

    Set-SectionExpanded -Root $window -Name "Capture options" -Expanded $true
    foreach ($name in @("Include cursor", "Capture delay", "Capture hotkey")) {
        [void](Assert-ElementByName -Root $window -Name $name)
    }
    Set-SectionExpanded -Root $window -Name "Image details" -Expanded $true
    foreach ($name in @(
        "Selected capture properties", "Selected capture width", "Selected capture height",
        "Selected capture file size", "Resize unit", "Resize width", "Resize height",
        "Lock resize aspect ratio", "Resize document")) {
        [void](Assert-ElementByName -Root $window -Name $name)
    }
    Set-SectionExpanded -Root $window -Name "Properties" -Expanded $true

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

    $canvasBeforeFocus = Assert-ElementByName -Root $window -Name "Editor canvas"
    $canvasWidthBeforeFocus = $canvasBeforeFocus.Current.BoundingRectangle.Width
    $focusMode = Assert-ElementByName -Root $window -Name "Focus mode"
    if (-not (Invoke-Element -Element $focusMode)) {
        throw "Unable to enter focus mode."
    }
    Start-Sleep -Milliseconds 250
    $exitFocusMode = Assert-ElementByName -Root $window -Name "Exit focus mode"
    $focusedCanvas = Assert-ElementByName -Root $window -Name "Editor canvas"
    [void](Assert-ElementByName -Root $window -Name "Select annotations")
    if ((Test-ElementVisible -Root $window -Name "Unpin layers and captures pane") `
        -or (Test-ElementVisible -Root $window -Name "Unpin right tools pane")) {
        throw "Focus mode left a side pane visible."
    }
    if ($focusedCanvas.Current.BoundingRectangle.Width -le $canvasWidthBeforeFocus + 100) {
        throw "Focus mode did not give the canvas the expected additional space."
    }
    if (-not (Invoke-Element -Element $exitFocusMode)) {
        throw "Unable to exit focus mode."
    }
    Start-Sleep -Milliseconds 250
    foreach ($name in @("Focus mode", "Unpin layers and captures pane", "Unpin right tools pane", "Capture folders", "Annotation layers")) {
        [void](Assert-ElementByName -Root $window -Name $name)
    }
    $restoredFocusSplitter = Assert-ElementByName -Root $window -Name "Resize layers and captures pane"
    if ([Math]::Abs($restoredFocusSplitter.Current.BoundingRectangle.Left - $leftSplitterPosition) -gt 3) {
        throw "Leaving focus mode did not restore the previous library width."
    }

    $manageCapture = Assert-ElementByName -Root $window -Name "Manage current capture"
    if (-not (Set-ElementExpanded -Element $manageCapture -Expanded $true)) {
        throw "Unable to open the capture management flyout."
    }
    foreach ($name in @(
        "Current capture title", "Rename current capture", "Duplicate current capture",
        "Open current capture folder", "Remove current capture from history",
        "Move current capture to recycle bin", "Move current capture to folder")) {
        [void](Assert-ElementByName -Root $window -Name $name)
    }
    Close-ElementFlyout -Element $manageCapture

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

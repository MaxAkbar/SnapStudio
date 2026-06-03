[CmdletBinding()]
param(
    [switch]$RequireStoreEnvironment,

    [string]$ReportPath
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

function Assert-FileContains {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Pattern,

        [Parameter(Mandatory)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description file was not found: '$Path'."
    }

    $content = Get-Content -Raw -LiteralPath $Path
    if ($content -notmatch $Pattern) {
        throw "$Description check failed for '$Path'."
    }

    Write-Host "Verified: $Description"
}

function Add-CheckResult {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [ValidateSet("Pass", "Fail", "Skipped")]
        [string]$Status,

        [Parameter(Mandatory)]
        [string]$Details
    )

    $script:checkResults.Add([pscustomobject]@{
        Name = $Name
        Status = $Status
        Details = $Details
    }) | Out-Null
}

function Invoke-ReadinessCheck {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$PassedDetails,

        [Parameter(Mandatory)]
        [scriptblock]$Check
    )

    try {
        & $Check
        Add-CheckResult -Name $Name -Status "Pass" -Details $PassedDetails
    }
    catch {
        $script:checksFailed = $true
        $failureMessage = $_.Exception.Message
        Add-CheckResult -Name $Name -Status "Fail" -Details $failureMessage
        Write-Host "FAILED: $Name"
        Write-Host $failureMessage
    }
}

function Skip-ReadinessCheck {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$Details
    )

    Add-CheckResult -Name $Name -Status "Skipped" -Details $Details
    Write-Host "Skipped: $Name"
}

function Assert-FeatureFlagDefault {
    param(
        [Parameter(Mandatory)]
        [string]$SettingsPath,

        [Parameter(Mandatory)]
        [string]$FlagName,

        [Parameter(Mandatory)]
        [bool]$ExpectedValue
    )

    $expectedLiteral = if ($ExpectedValue) { "true" } else { "false" }
    $escapedFlagName = [regex]::Escape($FlagName)
    Assert-FileContains `
        -Path $SettingsPath `
        -Pattern "\[`"$escapedFlagName`"\]\s*=\s*$expectedLiteral" `
        -Description "default feature flag $FlagName = $expectedLiteral"
}

function Assert-EnvironmentValue {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [string[]]$RejectedValues = @(),

        [string]$Pattern
    )

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Required Store environment variable '$Name' is not set."
    }

    foreach ($rejectedValue in $RejectedValues) {
        if ([string]::Equals($value, $rejectedValue, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Store environment variable '$Name' still uses placeholder value '$value'."
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($Pattern) -and $value -notmatch $Pattern) {
        throw "Store environment variable '$Name' has an invalid value: '$value'."
    }

    Write-Host "Verified: $Name"
}

function Format-MarkdownTableValue {
    param(
        [Parameter(Mandatory)]
        [string]$Value
    )

    return $Value.Replace("|", "\|").Replace("`r`n", "<br>").Replace("`n", "<br>").Replace("`r", "<br>")
}

function Resolve-ReportPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$RepositoryRoot
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return Get-FullPath $Path
    }

    return Get-FullPath (Join-Path $RepositoryRoot $Path)
}

function Write-ReadinessReport {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [bool]$StoreEnvironmentRequired,

        [Parameter(Mandatory)]
        [bool]$HasFailures
    )

    $generatedAt = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
    $overallStatus = if ($HasFailures) { "Failed" } else { "Passed" }
    $lines = New-Object 'System.Collections.Generic.List[string]'

    $lines.Add("# SnapStudio Release Readiness") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("Generated: $generatedAt") | Out-Null
    $lines.Add("Result: $overallStatus") | Out-Null
    $lines.Add("Store environment required: $StoreEnvironmentRequired") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("| Check | Status | Details |") | Out-Null
    $lines.Add("|---|---|---|") | Out-Null

    foreach ($result in $script:checkResults) {
        $name = Format-MarkdownTableValue $result.Name
        $status = Format-MarkdownTableValue $result.Status
        $details = Format-MarkdownTableValue $result.Details
        $lines.Add("| $name | $status | $details |") | Out-Null
    }

    $reportDirectory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
    }

    Set-Content -LiteralPath $Path -Value $lines -Encoding UTF8
    Write-Host "Wrote release readiness report: $Path"
}

$script:checkResults = New-Object 'System.Collections.Generic.List[object]'
$script:checksFailed = $false

$repoRoot = Get-FullPath (Join-Path $PSScriptRoot "..")
$settingsPath = Join-Path $repoRoot "src\SnapStudio.Core\Settings\ApplicationSettings.cs"
$workflowPath = Join-Path $repoRoot ".github\workflows\ci.yml"
$packagingPath = Join-Path $repoRoot "docs\packaging.md"
$storeSubmissionPath = Join-Path $repoRoot "docs\store-submission.md"
$privacyPath = Join-Path $repoRoot "docs\privacy.md"
$projectPlanPath = Join-Path $repoRoot "docs\project-plan.md"

Invoke-ReadinessCheck `
    -Name "Default V1.Ocr feature flag" `
    -PassedDetails "V1.Ocr defaults to false." `
    -Check { Assert-FeatureFlagDefault -SettingsPath $settingsPath -FlagName "V1.Ocr" -ExpectedValue $false }

Invoke-ReadinessCheck `
    -Name "Default V1.ScrollingCapture feature flag" `
    -PassedDetails "V1.ScrollingCapture defaults to false." `
    -Check { Assert-FeatureFlagDefault -SettingsPath $settingsPath -FlagName "V1.ScrollingCapture" -ExpectedValue $false }

Invoke-ReadinessCheck `
    -Name "Default V1.ScreenRecording feature flag" `
    -PassedDetails "V1.ScreenRecording defaults to false." `
    -Check { Assert-FeatureFlagDefault -SettingsPath $settingsPath -FlagName "V1.ScreenRecording" -ExpectedValue $false }

Invoke-ReadinessCheck `
    -Name "CI release package smoke" `
    -PassedDetails "CI workflow invokes eng\run-release-smoke.ps1." `
    -Check {
        Assert-FileContains `
            -Path $workflowPath `
            -Pattern "run-release-smoke\.ps1" `
            -Description "CI release smoke is configured"
    }

Invoke-ReadinessCheck `
    -Name "Packaging release smoke documentation" `
    -PassedDetails "docs\packaging.md documents the Release Package Smoke." `
    -Check {
        Assert-FileContains `
            -Path $packagingPath `
            -Pattern "Release Package Smoke" `
            -Description "packaging release smoke documentation"
    }

Invoke-ReadinessCheck `
    -Name "Store package placeholder rejection" `
    -PassedDetails "docs\store-submission.md documents -RequireStoreIdentity." `
    -Check {
        Assert-FileContains `
            -Path $storeSubmissionPath `
            -Pattern "-RequireStoreIdentity" `
            -Description "Store package smoke rejects placeholder identity"
    }

Invoke-ReadinessCheck `
    -Name "Store signing decision documentation" `
    -PassedDetails "Store submission docs state that Microsoft signs the package." `
    -Check {
        Assert-FileContains `
            -Path $storeSubmissionPath `
            -Pattern "Microsoft signs the package" `
            -Description "Store signing decision is documented"
    }

Invoke-ReadinessCheck `
    -Name "Local-first privacy disclosure" `
    -PassedDetails "docs\privacy.md discloses that SnapStudio does not upload captures." `
    -Check {
        Assert-FileContains `
            -Path $privacyPath `
            -Pattern "does not upload" `
            -Description "local-first privacy disclosure"
    }

Invoke-ReadinessCheck `
    -Name "URL redaction disclosure" `
    -PassedDetails "docs\privacy.md documents URL redaction." `
    -Check {
        Assert-FileContains `
            -Path $privacyPath `
            -Pattern "URLs" `
            -Description "URL redaction disclosure"
    }

Invoke-ReadinessCheck `
    -Name "Known issue triage levels" `
    -PassedDetails "docs\project-plan.md defines release blocker triage." `
    -Check {
        Assert-FileContains `
            -Path $projectPlanPath `
            -Pattern "Release blocker" `
            -Description "known issue triage levels"
    }

Invoke-ReadinessCheck `
    -Name "Privacy release check" `
    -PassedDetails "docs\project-plan.md records the no-network diagnostics posture." `
    -Check {
        Assert-FileContains `
            -Path $projectPlanPath `
            -Pattern "no network, telemetry, crash-upload, or cloud" `
            -Description "privacy release check"
    }

if ($RequireStoreEnvironment) {
    Invoke-ReadinessCheck `
        -Name "Store package identity name" `
        -PassedDetails "SNAPSTUDIO_MSIX_PACKAGE_IDENTITY_NAME is present and not the local placeholder." `
        -Check {
            Assert-EnvironmentValue `
                -Name "SNAPSTUDIO_MSIX_PACKAGE_IDENTITY_NAME" `
                -RejectedValues @("SnapStudio")
        }

    Invoke-ReadinessCheck `
        -Name "Store package publisher" `
        -PassedDetails "SNAPSTUDIO_MSIX_PACKAGE_PUBLISHER is present and not the local placeholder." `
        -Check {
            Assert-EnvironmentValue `
                -Name "SNAPSTUDIO_MSIX_PACKAGE_PUBLISHER" `
                -RejectedValues @("CN=SnapStudio")
        }

    Invoke-ReadinessCheck `
        -Name "Hosted privacy policy URL" `
        -PassedDetails "SNAPSTUDIO_STORE_PRIVACY_POLICY_URL is present and uses https." `
        -Check {
            Assert-EnvironmentValue `
                -Name "SNAPSTUDIO_STORE_PRIVACY_POLICY_URL" `
                -Pattern "^https://"
        }

    Invoke-ReadinessCheck `
        -Name "Hosted support URL" `
        -PassedDetails "SNAPSTUDIO_STORE_SUPPORT_URL is present and uses https." `
        -Check {
            Assert-EnvironmentValue `
                -Name "SNAPSTUDIO_STORE_SUPPORT_URL" `
                -Pattern "^https://"
        }
}
else {
    Skip-ReadinessCheck `
        -Name "Store environment variables" `
        -Details "Use -RequireStoreEnvironment for a public Store release candidate."
}

if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $resolvedReportPath = Resolve-ReportPath -Path $ReportPath -RepositoryRoot $repoRoot
    Write-ReadinessReport `
        -Path $resolvedReportPath `
        -StoreEnvironmentRequired $RequireStoreEnvironment `
        -HasFailures $script:checksFailed
}

if ($script:checksFailed) {
    throw "Release readiness checks failed."
}

Write-Host ""
Write-Host "Release readiness checks passed."

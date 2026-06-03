[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [ValidateSet("x86", "x64", "ARM64")]
    [string]$Platform = "x64",

    [string]$OutputDirectory,

    [string]$PackageIdentityName = $env:SNAPSTUDIO_MSIX_PACKAGE_IDENTITY_NAME,

    [string]$PackagePublisher = $env:SNAPSTUDIO_MSIX_PACKAGE_PUBLISHER,

    [string]$PackageVersion = $env:SNAPSTUDIO_MSIX_PACKAGE_VERSION,

    [string]$CertificatePath = $env:SNAPSTUDIO_MSIX_CERTIFICATE_PATH,

    [string]$CertificatePassword = $env:SNAPSTUDIO_MSIX_CERTIFICATE_PASSWORD,

    [string]$TimestampUrl = $env:SNAPSTUDIO_MSIX_TIMESTAMP_URL,

    [switch]$SkipPackageBuild,

    [switch]$RequireStoreIdentity,

    [switch]$RequireSignedPackage
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

function Convert-ToFourPartVersion {
    param(
        [Parameter(Mandatory)]
        [string]$Version
    )

    $stableVersion = $Version.Split("-")[0]
    $parts = @($stableVersion.Split("."))

    if ($parts.Count -gt 4) {
        $parts = $parts[0..3]
    }

    while ($parts.Count -lt 4) {
        $parts += "0"
    }

    return $parts -join "."
}

function Assert-FileExists {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found: '$Path'."
    }

    Write-Host "Verified: $Description"
}

function Assert-DirectoryExists {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Description was not found: '$Path'."
    }

    Write-Host "Verified: $Description"
}

function Invoke-PackageBuild {
    param(
        [Parameter(Mandatory)]
        [string]$PackageScript,

        [Parameter(Mandatory)]
        [string]$BuildPlatform,

        [Parameter(Mandatory)]
        [string]$BuildConfiguration,

        [string]$PackageOutputDirectory,

        [string]$ExpectedIdentityName,

        [string]$ExpectedPublisher,

        [string]$ExpectedVersion,

        [string]$SigningCertificatePath,

        [string]$SigningCertificatePassword,

        [string]$SigningTimestampUrl,

        [switch]$SkipBuild
    )

    $arguments = @(
        "-Platform",
        $BuildPlatform,
        "-Configuration",
        $BuildConfiguration
    )
    $displayArguments = @($arguments)

    if (-not [string]::IsNullOrWhiteSpace($PackageOutputDirectory)) {
        $arguments += @("-OutputDirectory", $PackageOutputDirectory)
        $displayArguments += @("-OutputDirectory", $PackageOutputDirectory)
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedIdentityName)) {
        $arguments += @("-PackageIdentityName", $ExpectedIdentityName)
        $displayArguments += @("-PackageIdentityName", $ExpectedIdentityName)
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedPublisher)) {
        $arguments += @("-PackagePublisher", $ExpectedPublisher)
        $displayArguments += @("-PackagePublisher", $ExpectedPublisher)
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion)) {
        $arguments += @("-PackageVersion", $ExpectedVersion)
        $displayArguments += @("-PackageVersion", $ExpectedVersion)
    }

    if (-not [string]::IsNullOrWhiteSpace($SigningCertificatePath)) {
        $arguments += @("-CertificatePath", $SigningCertificatePath)
        $displayArguments += @("-CertificatePath", $SigningCertificatePath)
    }

    if (-not [string]::IsNullOrWhiteSpace($SigningCertificatePassword)) {
        $arguments += @("-CertificatePassword", $SigningCertificatePassword)
        $displayArguments += @("-CertificatePassword", "***")
    }

    if (-not [string]::IsNullOrWhiteSpace($SigningTimestampUrl)) {
        $arguments += @("-TimestampUrl", $SigningTimestampUrl)
        $displayArguments += @("-TimestampUrl", $SigningTimestampUrl)
    }

    if ($SkipBuild) {
        $arguments += "-SkipBuild"
        $displayArguments += "-SkipBuild"
    }

    $processArguments = @(
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        $PackageScript
    ) + $arguments
    $displayProcessArguments = @(
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        $PackageScript
    ) + $displayArguments

    Write-Host ">> powershell $($displayProcessArguments -join ' ')"
    & powershell @processArguments

    if ($LASTEXITCODE -ne 0) {
        throw "'$PackageScript' failed with exit code $LASTEXITCODE."
    }
}

function Get-ManifestIdentity {
    param(
        [Parameter(Mandatory)]
        [xml]$Manifest
    )

    $namespaceManager = New-Object System.Xml.XmlNamespaceManager($Manifest.NameTable)
    $namespaceManager.AddNamespace("appx", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
    $identity = $Manifest.SelectSingleNode("/appx:Package/appx:Identity", $namespaceManager)

    if ($null -eq $identity) {
        throw "Package manifest does not contain an Identity element."
    }

    return [pscustomobject]@{
        Name = $identity.GetAttribute("Name")
        Publisher = $identity.GetAttribute("Publisher")
        Version = $identity.GetAttribute("Version")
        Architecture = $identity.GetAttribute("ProcessorArchitecture")
    }
}

function Assert-ExpectedIdentity {
    param(
        [Parameter(Mandatory)]
        [psobject]$Identity
    )

    if (-not [string]::IsNullOrWhiteSpace($PackageIdentityName) -and $Identity.Name -ne $PackageIdentityName) {
        throw "Package identity name '$($Identity.Name)' did not match expected '$PackageIdentityName'."
    }

    if (-not [string]::IsNullOrWhiteSpace($PackagePublisher) -and $Identity.Publisher -ne $PackagePublisher) {
        throw "Package publisher '$($Identity.Publisher)' did not match expected '$PackagePublisher'."
    }

    if (-not [string]::IsNullOrWhiteSpace($PackageVersion)) {
        $expectedVersion = Convert-ToFourPartVersion -Version $PackageVersion

        if ($Identity.Version -ne $expectedVersion) {
            throw "Package version '$($Identity.Version)' did not match expected '$expectedVersion'."
        }
    }

    if ($RequireStoreIdentity -and ($Identity.Name -eq "SnapStudio" -or $Identity.Publisher -eq "CN=SnapStudio")) {
        throw "Store release smoke requires Partner Center identity values, not the local placeholders."
    }

    Write-Host "Verified: package identity $($Identity.Name), $($Identity.Version), $($Identity.Architecture)"
}

function Assert-RunFullTrustCapability {
    param(
        [Parameter(Mandatory)]
        [xml]$Manifest
    )

    $capability = $Manifest.SelectSingleNode("//*[local-name()='Capability' and @Name='runFullTrust']")

    if ($null -eq $capability) {
        throw "Package manifest does not declare the runFullTrust capability."
    }

    Write-Host "Verified: runFullTrust capability"
}

function Assert-PrivacyPayload {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    Assert-FileExists -Path $Path -Description "bundled privacy notes"
    $content = Get-Content -Raw -LiteralPath $Path

    if ($content -notlike "*# SnapStudio Privacy Disclosure*" -or $content -notlike "*does not upload*") {
        throw "Bundled privacy notes do not contain the expected disclosure baseline."
    }

    Write-Host "Verified: privacy disclosure content"
}

function Assert-SignatureStatus {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $signature = Get-AuthenticodeSignature -FilePath $Path
    Write-Host "Package signature status: $($signature.Status)"

    if ($RequireSignedPackage -and $signature.Status -ne "Valid") {
        throw "A signed package was required, but signature status was '$($signature.Status)'."
    }
}

$repoRoot = Get-FullPath (Join-Path $PSScriptRoot "..")
$packageScript = Join-Path $PSScriptRoot "package-msix.ps1"
$verifyRoot = Join-Path $repoRoot "artifacts\msix\verify\$Platform"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $resolvedOutputDirectory = Join-Path $repoRoot "artifacts\packages"
}
elseif ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $resolvedOutputDirectory = Get-FullPath $OutputDirectory
}
else {
    $resolvedOutputDirectory = Get-FullPath (Join-Path $repoRoot $OutputDirectory)
}

Invoke-PackageBuild `
    -PackageScript $packageScript `
    -BuildPlatform $Platform `
    -BuildConfiguration $Configuration `
    -PackageOutputDirectory $OutputDirectory `
    -ExpectedIdentityName $PackageIdentityName `
    -ExpectedPublisher $PackagePublisher `
    -ExpectedVersion $PackageVersion `
    -SigningCertificatePath $CertificatePath `
    -SigningCertificatePassword $CertificatePassword `
    -SigningTimestampUrl $TimestampUrl `
    -SkipBuild:$SkipPackageBuild

Assert-DirectoryExists -Path $verifyRoot -Description "unpacked MSIX verification directory"
$manifestPath = Join-Path $verifyRoot "AppxManifest.xml"
Assert-FileExists -Path $manifestPath -Description "unpacked package manifest"

[xml]$manifest = Get-Content -Raw -LiteralPath $manifestPath
$identity = Get-ManifestIdentity -Manifest $manifest
Assert-ExpectedIdentity -Identity $identity
Assert-RunFullTrustCapability -Manifest $manifest

$packagePath = Join-Path $resolvedOutputDirectory "$($identity.Name)_$($identity.Version)_$($identity.Architecture).msix"
Assert-FileExists -Path $packagePath -Description "MSIX package"

$requiredPayloadFiles = @(
    "SnapStudio.App.exe",
    "SnapStudio.Core.dll",
    "SnapStudio.Platform.Windows.dll",
    "Assets\StoreLogo.png"
)

foreach ($payloadFile in $requiredPayloadFiles) {
    Assert-FileExists -Path (Join-Path $verifyRoot $payloadFile) -Description "package payload $payloadFile"
}

Assert-PrivacyPayload -Path (Join-Path $verifyRoot "Docs\privacy.md")
Assert-SignatureStatus -Path $packagePath

Write-Host ""
Write-Host "Release smoke passed."
Write-Host "MSIX package: $packagePath"
Write-Host "Verified unpack: $verifyRoot"

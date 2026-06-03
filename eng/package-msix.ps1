[CmdletBinding()]
param(
    [ValidateSet("x86", "x64", "ARM64")]
    [string]$Platform = "x64",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$OutputDirectory,

    [string]$PackageIdentityName = $env:SNAPSTUDIO_MSIX_PACKAGE_IDENTITY_NAME,

    [string]$PackagePublisher = $env:SNAPSTUDIO_MSIX_PACKAGE_PUBLISHER,

    [string]$PackageVersion = $env:SNAPSTUDIO_MSIX_PACKAGE_VERSION,

    [string]$CertificatePath = $env:SNAPSTUDIO_MSIX_CERTIFICATE_PATH,

    [string]$CertificatePassword = $env:SNAPSTUDIO_MSIX_CERTIFICATE_PASSWORD,

    [string]$TimestampUrl = $env:SNAPSTUDIO_MSIX_TIMESTAMP_URL,

    [switch]$SkipBuild
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

function Get-RelativePath {
    param(
        [Parameter(Mandatory)]
        [string]$FromDirectory,

        [Parameter(Mandatory)]
        [string]$ToPath
    )

    $fromFullPath = Get-FullPath $FromDirectory
    $toFullPath = Get-FullPath $ToPath

    if (-not $fromFullPath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $fromFullPath += [System.IO.Path]::DirectorySeparatorChar
    }

    $fromUri = New-Object System.Uri($fromFullPath)
    $toUri = New-Object System.Uri($toFullPath)

    return [System.Uri]::UnescapeDataString(
        $fromUri.MakeRelativeUri($toUri).ToString()
    ).Replace("/", [System.IO.Path]::DirectorySeparatorChar)
}

function Assert-PathUnderRoot {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Root,

        [Parameter(Mandatory)]
        [string]$Purpose
    )

    $fullPath = Get-FullPath $Path
    $fullRoot = Get-FullPath $Root
    $comparisonRoot = $fullRoot

    if (-not $comparisonRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $comparisonRoot += [System.IO.Path]::DirectorySeparatorChar
    }

    if ($fullPath -ne $fullRoot -and -not $fullPath.StartsWith($comparisonRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Purpose must stay under '$fullRoot'. Resolved path: '$fullPath'."
    }

    return $fullPath
}

function Remove-DirectoryIfExists {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$AllowedRoot
    )

    $fullPath = Assert-PathUnderRoot -Path $Path -Root $AllowedRoot -Purpose "Recursive delete target"

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

function Invoke-CheckedProcess {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [string[]]$DisplayArguments = $Arguments
    )

    Write-Host ">> $FilePath $($DisplayArguments -join ' ')"
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
        default { throw "Unsupported MSIX platform '$Architecture'." }
    }
}

function Get-ProjectValue {
    param(
        [Parameter(Mandatory)]
        [xml]$Project,

        [Parameter(Mandatory)]
        [string]$XPath
    )

    $node = $Project.SelectSingleNode($XPath)

    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        throw "Could not read project value '$XPath'."
    }

    return $node.InnerText.Trim()
}

function Get-OptionalProjectValue {
    param(
        [Parameter(Mandatory)]
        [xml]$Project,

        [Parameter(Mandatory)]
        [string]$XPath
    )

    $node = $Project.SelectSingleNode($XPath)

    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        return ""
    }

    return $node.InnerText.Trim()
}

function Get-PackageReferenceVersion {
    param(
        [Parameter(Mandatory)]
        [xml]$Project,

        [Parameter(Mandatory)]
        [string]$PackageName
    )

    $node = $Project.SelectSingleNode("/Project/ItemGroup/PackageReference[@Include='$PackageName']")

    if ($null -eq $node) {
        throw "Could not find PackageReference '$PackageName'."
    }

    $version = $node.GetAttribute("Version")

    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "PackageReference '$PackageName' does not specify a version."
    }

    return $version.Trim()
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

function Get-TargetPlatformVersionFromFramework {
    param(
        [Parameter(Mandatory)]
        [string]$TargetFramework
    )

    if ($TargetFramework -match "windows(?<Version>\d+\.\d+\.\d+\.\d+)") {
        return $Matches["Version"]
    }

    throw "Could not infer the Windows target platform version from '$TargetFramework'."
}

function Resolve-WindowsSdkTool {
    param(
        [Parameter(Mandatory)]
        [string]$ToolName,

        [string]$BuildToolsVersion
    )

    $packageRoot = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windows.sdk.buildtools"
    $preferredArchitecture = if ([Environment]::Is64BitOperatingSystem) { "x64" } else { "x86" }
    $searchRoots = @()

    if (-not [string]::IsNullOrWhiteSpace($BuildToolsVersion)) {
        $versionRoot = Join-Path $packageRoot $BuildToolsVersion

        if (Test-Path -LiteralPath $versionRoot) {
            $searchRoots += $versionRoot
        }
    }

    if (Test-Path -LiteralPath $packageRoot) {
        $searchRoots += $packageRoot
    }

    foreach ($root in $searchRoots) {
        $tool = Get-ChildItem -LiteralPath $root -Recurse -Filter $ToolName -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*\$preferredArchitecture\$ToolName" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1

        if ($null -ne $tool) {
            return $tool.FullName
        }
    }

    throw "Could not find '$ToolName'. Restore Microsoft.Windows.SDK.BuildTools first."
}

function Write-AppxManifest {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$IdentityName,

        [Parameter(Mandatory)]
        [string]$Publisher,

        [Parameter(Mandatory)]
        [string]$Version,

        [Parameter(Mandatory)]
        [string]$Architecture,

        [Parameter(Mandatory)]
        [string]$TargetPlatformMinVersion,

        [Parameter(Mandatory)]
        [string]$TargetPlatformVersion,

        [Parameter(Mandatory)]
        [string]$WindowsAppRuntimeMajorVersion,

        [Parameter(Mandatory)]
        [string]$WindowsAppRuntimeVersion
    )

    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)

    $writer = [System.Xml.XmlWriter]::Create($Path, $settings)

    try {
        $writer.WriteStartDocument()
        $writer.WriteStartElement("Package", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
        $writer.WriteAttributeString("xmlns", "uap", $null, "http://schemas.microsoft.com/appx/manifest/uap/windows10")
        $writer.WriteAttributeString("xmlns", "rescap", $null, "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities")
        $writer.WriteAttributeString("IgnorableNamespaces", "uap rescap")

        $writer.WriteStartElement("Identity")
        $writer.WriteAttributeString("Name", $IdentityName)
        $writer.WriteAttributeString("Publisher", $Publisher)
        $writer.WriteAttributeString("Version", $Version)
        $writer.WriteAttributeString("ProcessorArchitecture", $Architecture)
        $writer.WriteEndElement()

        $writer.WriteStartElement("Properties")
        $writer.WriteElementString("DisplayName", "SnapStudio")
        $writer.WriteElementString("PublisherDisplayName", "SnapStudio")
        $writer.WriteElementString("Logo", "Assets\StoreLogo.png")
        $writer.WriteEndElement()

        $writer.WriteStartElement("Dependencies")
        $writer.WriteStartElement("TargetDeviceFamily")
        $writer.WriteAttributeString("Name", "Windows.Universal")
        $writer.WriteAttributeString("MinVersion", $TargetPlatformMinVersion)
        $writer.WriteAttributeString("MaxVersionTested", $TargetPlatformVersion)
        $writer.WriteEndElement()
        $writer.WriteStartElement("TargetDeviceFamily")
        $writer.WriteAttributeString("Name", "Windows.Desktop")
        $writer.WriteAttributeString("MinVersion", $TargetPlatformMinVersion)
        $writer.WriteAttributeString("MaxVersionTested", $TargetPlatformVersion)
        $writer.WriteEndElement()
        $writer.WriteStartElement("PackageDependency")
        $writer.WriteAttributeString("Name", "Microsoft.WindowsAppRuntime.$WindowsAppRuntimeMajorVersion")
        $writer.WriteAttributeString("MinVersion", $WindowsAppRuntimeVersion)
        $writer.WriteAttributeString("Publisher", "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US")
        $writer.WriteEndElement()
        $writer.WriteEndElement()

        $writer.WriteStartElement("Resources")
        $writer.WriteStartElement("Resource")
        $writer.WriteAttributeString("Language", "EN-US")
        $writer.WriteEndElement()
        $writer.WriteEndElement()

        $writer.WriteStartElement("Applications")
        $writer.WriteStartElement("Application")
        $writer.WriteAttributeString("Id", "App")
        $writer.WriteAttributeString("Executable", "SnapStudio.App.exe")
        $writer.WriteAttributeString("EntryPoint", "Windows.FullTrustApplication")
        $writer.WriteStartElement("uap", "VisualElements", "http://schemas.microsoft.com/appx/manifest/uap/windows10")
        $writer.WriteAttributeString("DisplayName", "SnapStudio")
        $writer.WriteAttributeString("Description", "SnapStudio")
        $writer.WriteAttributeString("BackgroundColor", "transparent")
        $writer.WriteAttributeString("Square150x150Logo", "Assets\Square150x150Logo.png")
        $writer.WriteAttributeString("Square44x44Logo", "Assets\Square44x44Logo.png")
        $writer.WriteStartElement("uap", "DefaultTile", "http://schemas.microsoft.com/appx/manifest/uap/windows10")
        $writer.WriteAttributeString("Wide310x150Logo", "Assets\Wide310x150Logo.png")
        $writer.WriteEndElement()
        $writer.WriteStartElement("uap", "SplashScreen", "http://schemas.microsoft.com/appx/manifest/uap/windows10")
        $writer.WriteAttributeString("Image", "Assets\SplashScreen.png")
        $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteEndElement()

        $writer.WriteStartElement("Capabilities")
        $writer.WriteStartElement("rescap", "Capability", "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities")
        $writer.WriteAttributeString("Name", "runFullTrust")
        $writer.WriteEndElement()
        $writer.WriteEndElement()

        $writer.WriteStartElement("Extensions")
        $writer.WriteStartElement("Extension")
        $writer.WriteAttributeString("Category", "windows.activatableClass.inProcessServer")
        $writer.WriteStartElement("InProcessServer")
        $writer.WriteElementString("Path", "Microsoft.Web.WebView2.Core.dll")
        $writer.WriteStartElement("ActivatableClass")
        $writer.WriteAttributeString("ActivatableClassId", "Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions")
        $writer.WriteAttributeString("ThreadingModel", "both")
        $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteEndElement()
        $writer.WriteEndElement()

        $writer.WriteEndElement()
        $writer.WriteEndDocument()
    }
    finally {
        $writer.Dispose()
    }
}

function Copy-PackagePayloadFromRecipe {
    param(
        [Parameter(Mandatory)]
        [string]$RecipePath,

        [Parameter(Mandatory)]
        [string]$StagingRoot,

        [Parameter(Mandatory)]
        [string]$RuntimePayloadRoot
    )

    [xml]$recipe = Get-Content -Raw -LiteralPath $RecipePath
    $namespaceManager = New-Object System.Xml.XmlNamespaceManager($recipe.NameTable)
    $namespaceManager.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")

    $nodes = @($recipe.SelectNodes("//msb:AppXManifest | //msb:AppxPackagedFile", $namespaceManager))

    if ($nodes.Count -eq 0) {
        throw "No package payload entries were found in '$RecipePath'."
    }

    $seenPackagePaths = @{}
    $copiedCount = 0
    $manifestCopied = $false

    foreach ($node in $nodes) {
        $source = $node.GetAttribute("Include")
        $packagePathNode = $node.SelectSingleNode("msb:PackagePath", $namespaceManager)
        $isManifest = $node.LocalName -eq "AppXManifest"

        if ([string]::IsNullOrWhiteSpace($source) -or $null -eq $packagePathNode -or [string]::IsNullOrWhiteSpace($packagePathNode.InnerText)) {
            throw "Package recipe entry is missing a source or package path."
        }

        $packagePath = $packagePathNode.InnerText.Trim()
        $packagePathKey = $packagePath.ToLowerInvariant()

        if ($seenPackagePaths.ContainsKey($packagePathKey)) {
            throw "Package recipe contains duplicate package path '$packagePath'."
        }

        $seenPackagePaths[$packagePathKey] = $true

        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            if ($isManifest) {
                continue
            }

            if ($packagePath -eq "resources.pri") {
                $fallbackPri = Join-Path $RuntimePayloadRoot "SnapStudio.App.pri"

                if (Test-Path -LiteralPath $fallbackPri -PathType Leaf) {
                    $source = $fallbackPri
                }
            }

            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
                throw "Package source does not exist: '$source'."
            }
        }

        $destination = Assert-PathUnderRoot -Path (Join-Path $StagingRoot $packagePath) -Root $StagingRoot -Purpose "Package payload destination"
        $destinationDirectory = Split-Path -Parent $destination
        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Force
        $copiedCount++

        if ($isManifest) {
            $manifestCopied = $true
        }
    }

    return [pscustomobject]@{
        CopiedCount = $copiedCount
        ManifestCopied = $manifestCopied
    }
}

function Copy-AdditionalRuntimePayload {
    param(
        [Parameter(Mandatory)]
        [string]$SourceRoot,

        [Parameter(Mandatory)]
        [string]$StagingRoot
    )

    $sourceRootFullPath = Get-FullPath $SourceRoot
    $copiedCount = 0

    $files = Get-ChildItem -LiteralPath $sourceRootFullPath -Recurse -File |
        Where-Object {
            $relativePath = Get-RelativePath -FromDirectory $sourceRootFullPath -ToPath $_.FullName
            -not $relativePath.StartsWith("publish$([System.IO.Path]::DirectorySeparatorChar)", [System.StringComparison]::OrdinalIgnoreCase) -and
            $_.Extension -ne ".pdb" -and
            $_.Extension -ne ".appxrecipe" -and
            $_.Extension -ne ".msix" -and
            $_.Extension -ne ".appx" -and
            $_.Extension -ne ".appxbundle" -and
            $_.Extension -ne ".appxupload" -and
            $_.Name -ne "AppxManifest.xml"
        }

    foreach ($file in $files) {
        $relativePath = Get-RelativePath -FromDirectory $sourceRootFullPath -ToPath $file.FullName
        $destination = Assert-PathUnderRoot -Path (Join-Path $StagingRoot $relativePath) -Root $StagingRoot -Purpose "Additional runtime payload destination"

        if (Test-Path -LiteralPath $destination -PathType Leaf) {
            continue
        }

        $destinationDirectory = Split-Path -Parent $destination
        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        $copiedCount++
    }

    return $copiedCount
}

$repoRoot = Get-FullPath (Join-Path $PSScriptRoot "..")
$appProject = Join-Path $repoRoot "src\SnapStudio.App\SnapStudio.App.csproj"
$appProjectDirectory = Split-Path -Parent $appProject
$runtimeIdentifier = Get-RuntimeIdentifier -Architecture $Platform
$artifactsRoot = Join-Path $repoRoot "artifacts"
$msixArtifactsRoot = Join-Path $artifactsRoot "msix"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $artifactsRoot "packages"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}

$OutputDirectory = Get-FullPath $OutputDirectory

[xml]$projectXml = Get-Content -Raw -LiteralPath $appProject
$targetFramework = Get-ProjectValue -Project $projectXml -XPath "/Project/PropertyGroup/TargetFramework"
$targetPlatformMinVersion = Get-ProjectValue -Project $projectXml -XPath "/Project/PropertyGroup/TargetPlatformMinVersion"
$targetPlatformVersion = Get-TargetPlatformVersionFromFramework -TargetFramework $targetFramework
$buildToolsVersion = Get-PackageReferenceVersion -Project $projectXml -PackageName "Microsoft.Windows.SDK.BuildTools"
$windowsAppSdkVersion = Get-PackageReferenceVersion -Project $projectXml -PackageName "Microsoft.WindowsAppSDK"
$windowsAppRuntimeVersion = Convert-ToFourPartVersion -Version $windowsAppSdkVersion
$windowsAppRuntimeMajorVersion = $windowsAppRuntimeVersion.Split(".")[0]
$makeAppx = Resolve-WindowsSdkTool -ToolName "makeappx.exe" -BuildToolsVersion $buildToolsVersion
$signTool = Resolve-WindowsSdkTool -ToolName "signtool.exe" -BuildToolsVersion $buildToolsVersion

if ([string]::IsNullOrWhiteSpace($PackageIdentityName)) {
    $PackageIdentityName = "SnapStudio"
}

if ([string]::IsNullOrWhiteSpace($PackagePublisher)) {
    $PackagePublisher = "CN=SnapStudio"
}

if ([string]::IsNullOrWhiteSpace($PackageVersion)) {
    $projectVersion = Get-OptionalProjectValue -Project $projectXml -XPath "/Project/PropertyGroup/Version"
    $PackageVersion = if ([string]::IsNullOrWhiteSpace($projectVersion)) { "1.0.0.0" } else { Convert-ToFourPartVersion -Version $projectVersion }
}
else {
    $PackageVersion = Convert-ToFourPartVersion -Version $PackageVersion
}

if (-not $SkipBuild) {
    Invoke-CheckedProcess -FilePath "dotnet" -Arguments @(
        "restore",
        $appProject,
        "-p:RuntimeIdentifier=$runtimeIdentifier"
    )

    Invoke-CheckedProcess -FilePath "dotnet" -Arguments @(
        "publish",
        $appProject,
        "--configuration",
        $Configuration,
        "--no-restore",
        "-p:RuntimeIdentifier=$runtimeIdentifier",
        "-p:SelfContained=false",
        "-p:WindowsPackageType=MSIX",
        "-p:GenerateAppxPackageOnBuild=true",
        "-p:AppxPackageSigningEnabled=false",
        "-p:AppxBundle=Never"
    )
}

$packageBuildRoot = Join-Path $appProjectDirectory "bin\$Configuration\$targetFramework\$runtimeIdentifier"
$recipePath = Join-Path $packageBuildRoot "SnapStudio.App.build.appxrecipe"

if (-not (Test-Path -LiteralPath $recipePath -PathType Leaf)) {
    throw "Package recipe was not found at '$recipePath'. Run without -SkipBuild or inspect the publish output."
}

$stagingRoot = Join-Path $msixArtifactsRoot "layout\$Platform"
$verifyRoot = Join-Path $msixArtifactsRoot "verify\$Platform"

Remove-DirectoryIfExists -Path $stagingRoot -AllowedRoot $msixArtifactsRoot
Remove-DirectoryIfExists -Path $verifyRoot -AllowedRoot $msixArtifactsRoot
New-Item -ItemType Directory -Force -Path $stagingRoot, $verifyRoot, $OutputDirectory | Out-Null

$payloadResult = Copy-PackagePayloadFromRecipe -RecipePath $recipePath -StagingRoot $stagingRoot -RuntimePayloadRoot $packageBuildRoot
$additionalPayloadCount = Copy-AdditionalRuntimePayload -SourceRoot $packageBuildRoot -StagingRoot $stagingRoot
$manifestPath = Join-Path $stagingRoot "AppxManifest.xml"

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    Write-AppxManifest `
        -Path $manifestPath `
        -IdentityName $PackageIdentityName `
        -Publisher $PackagePublisher `
        -Version $PackageVersion `
        -Architecture $Platform.ToLowerInvariant() `
        -TargetPlatformMinVersion $targetPlatformMinVersion `
        -TargetPlatformVersion $targetPlatformVersion `
        -WindowsAppRuntimeMajorVersion $windowsAppRuntimeMajorVersion `
        -WindowsAppRuntimeVersion $windowsAppRuntimeVersion
}

[xml]$manifest = Get-Content -Raw -LiteralPath $manifestPath
$manifestNamespace = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$manifestNamespace.AddNamespace("appx", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
$identity = $manifest.SelectSingleNode("/appx:Package/appx:Identity", $manifestNamespace)

if ($null -eq $identity) {
    throw "Package manifest does not contain an Identity element."
}

$packageName = $identity.GetAttribute("Name")
$packageVersion = $identity.GetAttribute("Version")
$packageArchitecture = $identity.GetAttribute("ProcessorArchitecture")
$packagePath = Join-Path $OutputDirectory "$packageName`_$packageVersion`_$packageArchitecture.msix"

Invoke-CheckedProcess -FilePath $makeAppx -Arguments @(
    "pack",
    "/d",
    $stagingRoot,
    "/p",
    $packagePath,
    "/o",
    "/nv"
)

if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    $certificateFullPath = Get-FullPath $CertificatePath

    if (-not (Test-Path -LiteralPath $certificateFullPath -PathType Leaf)) {
        throw "Certificate file does not exist: '$certificateFullPath'."
    }

    $signArguments = @(
        "sign",
        "/fd",
        "SHA256",
        "/f",
        $certificateFullPath
    )
    $displaySignArguments = @(
        "sign",
        "/fd",
        "SHA256",
        "/f",
        $certificateFullPath
    )

    if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
        $signArguments += @("/p", $CertificatePassword)
        $displaySignArguments += @("/p", "***")
    }

    if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
        $signArguments += @("/tr", $TimestampUrl, "/td", "SHA256")
        $displaySignArguments += @("/tr", $TimestampUrl, "/td", "SHA256")
    }

    $signArguments += $packagePath
    $displaySignArguments += $packagePath
    Invoke-CheckedProcess -FilePath $signTool -Arguments $signArguments -DisplayArguments $displaySignArguments
}
else {
    Write-Host "No certificate path was provided. The MSIX package was created unsigned."
}

Invoke-CheckedProcess -FilePath $makeAppx -Arguments @(
    "unpack",
    "/p",
    $packagePath,
    "/d",
    $verifyRoot,
    "/o"
)

Write-Host "MSIX package: $packagePath"
Write-Host "Staged payload: $stagingRoot"
Write-Host "Verified unpack: $verifyRoot"
Write-Host "Recipe payload files: $($payloadResult.CopiedCount)"
Write-Host "Additional runtime files: $additionalPayloadCount"

[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')][string]$Platform = 'x64',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$OutputDirectory,
    [string]$PackagePublisher = 'CN=SnapStudio',
    [string]$PackageVersion = '1.0.0.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repoRoot "artifacts\command-palette\$Platform\$Configuration" }
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
$stagePath = Join-Path $outputPath 'package'
if (Test-Path -LiteralPath $stagePath) {
    throw "The package staging folder already exists: $stagePath. Choose a new OutputDirectory for a clean package."
}
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$makeAppx = Get-ChildItem -LiteralPath $sdkRoot -Directory |
    Where-Object { $_.Name -match '^10\.0\.\d+\.\d+$' } |
    Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64\makeappx.exe' } |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$makeAppx) { throw 'Install a Windows SDK with MakeAppx before packaging the optional companion.' }

$projectPath = Join-Path $repoRoot 'integrations\SnapStudio.CommandPalette\SnapStudio.CommandPalette.csproj'
dotnet publish $projectPath -c $Configuration -p:Platform=$Platform -r "win-$($Platform.ToLowerInvariant())" --self-contained true -o $stagePath
if ($LASTEXITCODE -ne 0) { throw 'The Command Palette companion could not be published.' }

Get-ChildItem -LiteralPath (Join-Path (Split-Path $projectPath -Parent) 'distribution') -File |
    Copy-Item -Destination $stagePath

[xml]$manifest = Get-Content -LiteralPath (Join-Path (Split-Path $projectPath -Parent) 'Package.appxmanifest') -Raw
$manifest.Package.Identity.ProcessorArchitecture = $Platform.ToLowerInvariant()
$manifest.Package.Identity.Publisher = $PackagePublisher
$manifest.Package.Identity.Version = $PackageVersion
$manifestPath = Join-Path $stagePath 'AppxManifest.xml'
$manifest.Save($manifestPath)
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $stagePath 'LICENSE.txt')
$packagePath = Join-Path $outputPath "SnapStudio.CommandPalette-$PackageVersion-$Platform.msix"
& $makeAppx pack /d $stagePath /p $packagePath /o
if ($LASTEXITCODE -ne 0) { throw 'The Command Palette companion could not be packaged.' }
$archivePath = Join-Path $outputPath "SnapStudio.CommandPalette-$PackageVersion-$Platform-development.zip"
Compress-Archive -Path (Join-Path $stagePath '*') -DestinationPath $archivePath -CompressionLevel Optimal
Write-Output "Unsigned optional package: $packagePath"
Write-Output "Optional development ZIP: $archivePath"
Write-Output "Developer-mode registration: Add-AppxPackage -Register '$manifestPath'"

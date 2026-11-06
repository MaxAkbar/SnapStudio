[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$manifestPath = Join-Path $PSScriptRoot 'AppxManifest.xml'
if (!(Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw 'Extract the entire companion ZIP into a permanent folder, then run Install.ps1 from that folder.'
}
$developerMode = Get-ItemPropertyValue -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue
if ($developerMode -ne 1) {
    throw 'This unsigned preview requires Windows Developer Mode. Enable it yourself in Windows Settings, or continue using SnapStudio without the companion. This installer does not change Developer Mode.'
}
if (!(Get-AppxPackage -Name Microsoft.CommandPalette)) {
    throw 'Install PowerToys with Command Palette before registering this optional companion.'
}
Add-AppxPackage -Register $manifestPath
Write-Output 'SnapStudio for Command Palette is registered. Keep this folder in place, open SnapStudio 1.3.0 or newer once, and reload Command Palette extensions.'

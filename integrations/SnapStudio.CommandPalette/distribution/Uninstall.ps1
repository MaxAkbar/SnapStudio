[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Get-AppxPackage -Name SnapStudio.CommandPalette | Remove-AppxPackage
Write-Output 'The optional companion is unregistered. SnapStudio and your captures are unchanged.'

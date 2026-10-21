$ErrorActionPreference = 'Stop'
if (Get-Process -Name Ebira -ErrorAction SilentlyContinue) { throw 'Close Ebira before uninstalling.' }
$programs = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs'))
$target = [IO.Path]::GetFullPath((Join-Path $programs 'Ebira'))
if (($target -ne [IO.Path]::GetFullPath($PSScriptRoot)) -or !($target.StartsWith($programs + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))) { throw 'Run the installed uninstaller from LocalAppData\Programs\Ebira.' }
$menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Ebira.lnk'
if (Test-Path -LiteralPath $menu) { Remove-Item -LiteralPath $menu }
Remove-Item -LiteralPath $target -Recurse -Force
Write-Host 'Ebira removed. Received files and LocalAppData\Ebira recovery/identity data were kept.'

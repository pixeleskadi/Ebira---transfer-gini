$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot
$target = Join-Path $env:LOCALAPPDATA 'Programs\Ebira'
if (!(Test-Path -LiteralPath (Join-Path $source 'Ebira.exe'))) { throw 'Extract the complete Windows package first.' }
if (Get-Process -Name Ebira -ErrorAction SilentlyContinue) { throw 'Close Ebira before installing or updating.' }
New-Item -ItemType Directory -Force -Path $target | Out-Null
foreach ($name in @('Ebira.exe','Install-Ebira.ps1','Install-Ebira.cmd','Uninstall-Ebira.ps1','Uninstall-Ebira.cmd','GETTING-STARTED.md','V1-BASELINE.md','DEVICE-VALIDATION.md','ThirdPartyNotices.txt','DOTNET-LICENSE.txt')) {
    $file = Join-Path $source $name
    if ((Test-Path -LiteralPath $file) -and ([IO.Path]::GetFullPath($file) -ne [IO.Path]::GetFullPath((Join-Path $target $name)))) { Copy-Item -LiteralPath $file -Destination (Join-Path $target $name) -Force }
}
$menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Ebira.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($menu)
$shortcut.TargetPath = Join-Path $target 'Ebira.exe'
$shortcut.WorkingDirectory = $target
$shortcut.Description = 'Ebira local file transfer (development candidate)'
$shortcut.Save()
Write-Host "Installed to $target. Open Ebira from the Start menu."

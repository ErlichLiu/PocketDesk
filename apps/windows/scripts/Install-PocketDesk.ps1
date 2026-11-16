[CmdletBinding()]
param([switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\stable'
$destination = Join-Path $env:LOCALAPPDATA 'Programs\PocketDesk'
$exe = Join-Path $destination 'PocketDesk.exe'
if (-not (Test-Path (Join-Path $source 'PocketDesk.exe'))) { throw 'Run Build-PocketDesk.ps1 first.' }
foreach ($process in @(Get-Process PocketDesk -ErrorAction SilentlyContinue)) {
    # A hidden/elevated path may be unreadable: conservatively refuse updates.
    if (-not $process.Path -or $process.Path -eq $exe) { throw 'Exit PocketDesk safely before updating the installed app.' }
}
$files = @(Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Extension -in @('.exe', '.dll', '.json') })
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($file in $files) {
    $target = Join-Path $destination $file.Name
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $target).Hash) {
        throw "Installed file verification failed: $($file.Name)"
    }
}
$menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'PocketDesk'
New-Item -ItemType Directory -Path $menu -Force | Out-Null
$shell = New-Object -ComObject WScript.Shell
$shortcutPath = Join-Path $menu 'PocketDesk.lnk'
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $destination
$shortcut.Description = 'PocketDesk remote workspace'
$shortcut.Save()
$verified = $shell.CreateShortcut($shortcutPath)
if ($verified.TargetPath -ne $exe) { throw 'Start Menu shortcut verification failed.' }
Write-Output "Installed PocketDesk: $destination"
if (-not $NoLaunch) { Start-Process -FilePath $exe -WorkingDirectory $destination }

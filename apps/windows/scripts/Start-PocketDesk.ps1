param([switch]$Elevated)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = Join-Path $root 'artifacts\stable\PocketDesk.exe'
if (-not (Test-Path $exe)) { throw 'Build first: dotnet build src\PocketDeskApp\PocketDeskApp.csproj -c Release -o artifacts\stable' }
if ($Elevated) { Start-Process -FilePath $exe -Verb RunAs -WorkingDirectory $root }
else { Start-Process -FilePath $exe -WorkingDirectory $root }

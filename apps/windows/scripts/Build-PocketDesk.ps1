$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) { $dotnet = $dotnet.Source }
else { $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw 'Install the .NET 8 SDK first.' }
& $dotnet build (Join-Path $root 'src\PocketDeskApp\PocketDeskApp.csproj') -c Release -o (Join-Path $root 'artifacts\stable') --nologo
if ($LASTEXITCODE -ne 0) { throw 'PocketDesk build failed.' }

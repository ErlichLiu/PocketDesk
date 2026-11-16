$ErrorActionPreference = 'Stop'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' } else { $dotnet = $dotnet.Source }
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    foreach ($project in @('DisplayTests', 'WindowSafetyTests', 'GestureTests', 'MigrationTests')) {
        & $dotnet run --project "tests\$project" -c Release
        if ($LASTEXITCODE -ne 0) { throw "Failed: $project" }
    }
} finally { Pop-Location }

[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$repo = Split-Path (Split-Path $root -Parent) -Parent
$project = Join-Path $root 'src\PocketDeskApp\PocketDeskApp.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = $projectXml.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid project version.' }
$commit = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'Cannot determine source commit.' }
$output = Join-Path $root 'artifacts\packages'
foreach ($path in @((Join-Path $root 'artifacts'), $output)) {
    if ((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Refusing symlink output.'
    }
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$stage = Join-Path $output ('.package-' + [Guid]::NewGuid().ToString('N'))
$name = "PocketDesk-$version-windows-x64-$($commit.Substring(0,8))"
$folder = Join-Path $stage $name
New-Item -ItemType Directory -Path $folder -Force | Out-Null
try {
    & dotnet publish $project -c Release -r win-x64 --self-contained true --nologo `
        -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $folder
    if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
    & python (Join-Path $repo 'tools\package_artifacts.py') prepare $folder windows $version $commit
    if ($LASTEXITCODE -ne 0) { throw 'Package metadata failed.' }
    $nuget = ((& dotnet msbuild $project -nologo -getProperty:NuGetPackageRoot) | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $nuget)) { throw 'Cannot locate runtime license source.' }
    $runtime = Get-Content -LiteralPath (Join-Path $folder 'PocketDesk.runtimeconfig.json') -Raw | ConvertFrom-Json
    $packs = @(
        @{ Id='microsoft.netcore.app.runtime.win-x64'; Framework='Microsoft.NETCore.App'; Name='dotnet-core' },
        @{ Id='microsoft.windowsdesktop.app.runtime.win-x64'; Framework='Microsoft.WindowsDesktop.App'; Name='dotnet-desktop' }
    )
    foreach ($pack in $packs) {
        $framework = @($runtime.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq $pack.Framework })
        if ($framework.Count -ne 1) { throw 'Expected an included self-contained runtime.' }
        $source = Join-Path (Join-Path $nuget $pack.Id) $framework[0].version
        $destination = Join-Path (Join-Path $folder 'licenses') $pack.Name
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        foreach ($file in @(Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\.TXT)?$' })) {
            $targetName = $file.Name.ToUpperInvariant()
            if ($targetName -eq 'LICENSE') { $targetName = 'LICENSE.TXT' }
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $destination $targetName)
        }
        if (-not (Test-Path -LiteralPath (Join-Path $destination 'LICENSE.TXT'))) { throw 'Runtime license missing.' }
    }
    $archive = Join-Path $output "$name.zip"
    Compress-Archive -LiteralPath $folder -DestinationPath $archive -CompressionLevel Optimal -Force
    & python (Join-Path $repo 'tools\package_artifacts.py') verify $archive windows
    if ($LASTEXITCODE -ne 0) { throw 'Package verification failed.' }
    & python (Join-Path $repo 'tools\package_artifacts.py') checksum $archive
    if ($LASTEXITCODE -ne 0) { throw 'Checksum generation failed.' }
    if ($env:GITHUB_OUTPUT) {
        "archive=$archive" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
        "artifact_name=$name" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    }
    Write-Output "Packaged $archive (self-contained, unsigned; never installed/launched)"
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}

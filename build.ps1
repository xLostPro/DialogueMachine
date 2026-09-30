param(
    [ValidateSet('win-x64','win-arm64','linux-x64','linux-arm64','osx-x64','osx-arm64')]
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$projectRoot = $PSScriptRoot
$outputFolder = Join-Path $projectRoot ('dist/' + $(if ($Runtime -eq 'win-x64') { 'Windows' } else { $Runtime }))
# Runtime-specific restores must not rewrite the committed, platform-neutral locks.
dotnet publish (Join-Path $projectRoot 'DialogueMachine.csproj') -c Release -r $Runtime --self-contained true -o $outputFolder --configfile (Join-Path $projectRoot 'NuGet.Config') "-p:NuGetLockFilePath=obj/$Runtime.packages.lock.json"
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $outputFolder
if ($Runtime.StartsWith('osx-')) {
    $bundleFolder = Join-Path $outputFolder 'Dialogue Machine.app/Contents'
    $macosFolder = Join-Path $bundleFolder 'MacOS'
    New-Item -ItemType Directory -Path $macosFolder -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $bundleFolder 'Resources') -Force | Out-Null
    Get-ChildItem -LiteralPath $outputFolder -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $macosFolder }
    Copy-Item -LiteralPath (Join-Path $outputFolder 'Examples') -Destination $macosFolder -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Packaging/Info.plist') -Destination $bundleFolder
}
Write-Host "Build ready: $outputFolder"

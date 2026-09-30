param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'artifacts/release' }
$metadata = & (Join-Path $PSScriptRoot 'Get-ReleaseVersion.ps1')
$packageFolder = Join-Path $projectRoot 'dist/Windows'
$executable = Join-Path $packageFolder 'DialogueMachine.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Build the Windows app before creating a release archive.' }
$builtVersion = (Get-Item -LiteralPath $executable).VersionInfo.ProductVersion.Split('+')[0]
if ($builtVersion -cne $metadata.Version) { throw "The package version '$builtVersion' does not match project version '$($metadata.Version)'. Rebuild before packaging." }
$runtimeConfig = Get-Content -LiteralPath (Join-Path $packageFolder 'DialogueMachine.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $runtimeConfig.runtimeOptions.includedFrameworks) { throw 'The release must include its .NET runtime.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$zipPath = Join-Path $OutputDirectory $metadata.ZipName
$files = @(Get-ChildItem -LiteralPath $packageFolder | Select-Object -ExpandProperty FullName)
Compress-Archive -LiteralPath $files -DestinationPath $zipPath -CompressionLevel Optimal -Force
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
# Use an explicit LF so Linux release jobs can read a checksum produced on Windows.
Set-Content -LiteralPath ($zipPath + '.sha256') -Value ($hash + '  ' + $metadata.ZipName + [char]10) -Encoding ascii -NoNewline
Write-Host "Release ready: $zipPath"

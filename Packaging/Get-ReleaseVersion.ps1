param(
    [string]$Tag = $(if ($env:GITHUB_REF_TYPE -eq 'tag') { $env:GITHUB_REF_NAME } else { '' }),
    [string]$GitHubOutput
)
$ErrorActionPreference = 'Stop'
$projectFile = Join-Path (Split-Path $PSScriptRoot -Parent) 'DialogueMachine.csproj'
[xml]$projectXml = Get-Content -LiteralPath $projectFile -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw "Unsupported project version: $version" }
if ($Tag -and $Tag -cne "v$version") { throw "Release tag '$Tag' must match project version 'v$version'. Update DialogueMachine.csproj before tagging." }
$zipName = "DialogueMachine-$version-win-x64.zip"
if ($GitHubOutput) {
    @("version=$version", "zip-name=$zipName") | Add-Content -LiteralPath $GitHubOutput -Encoding utf8
}
[pscustomobject]@{ Version = $version; ZipName = $zipName }

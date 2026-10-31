# Copies conformance test fixtures into the Godot addon's test directory.
$ErrorActionPreference = "Stop"

$sourceDir = Join-Path $PSScriptRoot "../Conformance"
$targetDir = Join-Path $PSScriptRoot "addons/dialogue_machine/tests/conformance"

if (-not (Test-Path $sourceDir)) {
    throw "Source directory not found: $sourceDir"
}

if (-not (Test-Path $targetDir)) {
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
}

# Remove stale fixtures in target
Get-ChildItem -Path $targetDir -Filter "*.json" | Remove-Item -Force

# Copy all JSON fixtures
Get-ChildItem -Path $sourceDir -Filter "*.json" | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination (Join-Path $targetDir $_.Name) -Force
}

$count = (Get-ChildItem -Path $targetDir -Filter "*.json").Count
Write-Host "Copied $count conformance fixtures to $targetDir"

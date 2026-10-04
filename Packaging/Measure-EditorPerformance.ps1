param(
    [string]$OutputDirectory = 'artifacts/performance',
    [ValidateRange(3,100)][int]$Samples = 5,
    [ValidateRange(1,100)][int]$HistoryEntries = 20,
    [ValidateSet('100','1000','10000')][string[]]$Sizes = @('100','1000','10000'),
    [switch]$Native,
    [switch]$CacheProbe,
    [string]$BaselineRef
)
$ErrorActionPreference = 'Stop'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory, $taskRoot)
$taskSource = $taskRoot
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskRevision = git -C $taskRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Could not identify the source revision.' }
if ($BaselineRef) {
    $taskRevision = git -C $taskRoot rev-parse "$BaselineRef^{commit}"
    if ($LASTEXITCODE -ne 0) { throw 'The baseline must be an existing Git commit.' }
    # An archive avoids changing the user's checkout. Keep it for inspection/reuse;
    # this script never removes directories or switches branches.
    $taskSource = Join-Path ([IO.Path]::GetTempPath()) ('DialogueMachine-performance-' + [guid]::NewGuid().ToString('N'))
    $taskArchive = Join-Path $taskOutput 'baseline-source.zip'
    git -C $taskRoot archive --format=zip --output=$taskArchive $taskRevision
    if ($LASTEXITCODE -ne 0) { throw 'Could not archive the baseline source.' }
    Expand-Archive -LiteralPath $taskArchive -DestinationPath $taskSource
    Copy-Item -LiteralPath (Join-Path $taskRoot 'tests/PerformanceChecks.cs') -Destination (Join-Path $taskSource 'tests/PerformanceChecks.cs')
    $taskProgram = Join-Path $taskSource 'tests/Program.cs'
    $taskText = [IO.File]::ReadAllText($taskProgram)
    if (-not $taskText.Contains('PerformanceChecks.Run(args)')) {
        $taskText = $taskText.Replace('var passed = 0;', "if (args.Contains(`"--performance`")) { PerformanceChecks.Run(args); return; }`nvar passed = 0;")
        [IO.File]::WriteAllText($taskProgram, $taskText)
    }
}
$taskCpu = Get-ItemPropertyValue -LiteralPath 'HKLM:\HARDWARE\DESCRIPTION\System\CentralProcessor\0' -Name ProcessorNameString
$taskHasher = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
try {
    [string[]]$taskFiles = @((Get-ChildItem -LiteralPath (Join-Path $taskSource 'Core') -File -Filter '*.cs').FullName) + @((Get-ChildItem -LiteralPath (Join-Path $taskSource 'UI') -File -Filter '*.cs').FullName) + @('App.axaml', 'App.axaml.cs', 'DialogueMachine.csproj' | ForEach-Object { Join-Path $taskSource $_ })
    [Array]::Sort($taskFiles, [StringComparer]::Ordinal)
    foreach ($taskFile in $taskFiles) {
        $taskRelative = [IO.Path]::GetRelativePath($taskSource, $taskFile).Replace('\','/')
        $taskHasher.AppendData([Text.Encoding]::UTF8.GetBytes($taskRelative + "`n"))
        $taskHasher.AppendData([IO.File]::ReadAllBytes($taskFile))
    }
    $taskSourceHash = [Convert]::ToHexString($taskHasher.GetHashAndReset())
} finally { $taskHasher.Dispose() }
$taskInfo = [ordered]@{ SourceRevision = $taskRevision; ProductSourceSha256 = $taskSourceHash; BaselineRef = $BaselineRef; SourceDirectory = $taskSource; Cpu = $taskCpu.Trim(); Sdk = (dotnet --version); Native = [bool]$Native; CacheProbe = [bool]$CacheProbe; Samples = $Samples; HistoryEntries = $HistoryEntries; Sizes = $Sizes; OutputDirectory = $taskOutput }
$taskInfo | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskOutput 'run.json')
$taskProject = Join-Path $taskSource 'tests/DialogueMachine.Tests.csproj'
dotnet restore $taskProject --configfile (Join-Path $taskSource 'NuGet.Config') --packages (Join-Path $taskRoot '.packages') --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Performance runner restore failed.' }
$taskArguments = @('--performance','--output',$taskOutput,'--samples',"$Samples",'--history',"$HistoryEntries",'--sizes',($Sizes -join ','))
if ($Native) { $taskArguments += '--native' }
if ($CacheProbe) { $taskArguments += '--cache-probe' }
dotnet run --project $taskProject -c Release --no-restore -- @taskArguments | Tee-Object -FilePath (Join-Path $taskOutput 'console.txt')
if ($LASTEXITCODE -ne 0) { throw 'Performance measurements failed; inspect the partial results and console output.' }
Write-Host "Measurements ready: $taskOutput/results.json"

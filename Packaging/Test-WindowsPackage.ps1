param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'artifacts/native' }
$executable = Join-Path $projectRoot 'dist/Windows/DialogueMachine.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Build the Windows app before testing its package.' }
foreach ($kind in @('welcome', 'project')) {
    $folder = Join-Path $OutputDirectory ($kind + '-' + [guid]::NewGuid().ToString('N'))
    $arguments = @('--smoke-test', '--smoke-output', ('"' + $folder + '"'))
    if ($kind -eq 'project') {
        $example = Join-Path $projectRoot 'Examples/lighthouse.dialogue.json'
        $arguments += @('--smoke-project', ('"' + $example + '"'))
    }
    $process = Start-Process -FilePath $executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(20000)) { $process.Kill(); throw "Native $kind startup timed out." }
    if ($process.ExitCode -ne 0) {
        $errorFile = Join-Path $folder 'native-smoke-error.txt'
        if (Test-Path -LiteralPath $errorFile) { Get-Content -LiteralPath $errorFile | Write-Host }
        throw "Native $kind startup failed with exit code $($process.ExitCode)."
    }
    $report = Get-Content -LiteralPath (Join-Path $folder 'native-smoke.json') -Raw | ConvertFrom-Json
    if ($report.Errors -ne 0 -or $report.HasProject -ne ($kind -eq 'project') -or $report.WelcomeVisible -ne ($kind -eq 'welcome') -or -not $report.FileDialogsAvailable) {
        throw "Unexpected native $kind startup state. Inspect $folder."
    }
    if ($kind -eq 'project' -and ($report.Scenes -ne 5 -or $report.CanvasWidth -lt 250 -or $report.EditorWidth -lt 260)) { throw 'The project workspace did not lay out correctly.' }
    Write-Host "PASS native $kind startup"
}

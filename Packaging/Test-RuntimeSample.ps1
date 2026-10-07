param([int]$Port = 5238, [string]$OutputDirectory = 'artifacts/runtime-sample')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sample = Join-Path $projectRoot 'Integration/Sample/bin/Release/net10.0/Sample.dll'
if (-not (Test-Path -LiteralPath $sample -PathType Leaf)) { throw 'Build Integration/Sample/Sample.csproj first.' }
$outputPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$address = "http://127.0.0.1:$Port"
$process = Start-Process -FilePath 'dotnet' -ArgumentList @(('"' + $sample + '"'), '--urls', $address) -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $outputPath 'stdout.txt') -RedirectStandardError (Join-Path $outputPath 'stderr.txt')
try {
    $response = $null
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if ($process.HasExited) { throw 'Sample exited before serving its root page. Inspect runtime-sample logs.' }
        try { $response = Invoke-WebRequest -Uri $address -TimeoutSec 2; break } catch { Start-Sleep -Milliseconds 250 }
    }
    if ($null -eq $response -or $response.StatusCode -ne 200 -or -not $response.Content.Contains('Enter, Ada') -or -not $response.Content.Contains('blazor.web.js')) { throw 'Sample did not render the configured target choices and Blazor client.' }
    $client = Invoke-WebRequest -Uri ($address + '/_framework/blazor.web.js') -TimeoutSec 10
    if ($client.StatusCode -ne 200 -or $client.RawContentLength -lt 10000) { throw 'Sample Blazor interactive client is unavailable.' }
    foreach ($theme in @('Dark','Light','High contrast')) {
        foreach ($scale in @(100,115,130)) {
            $url = $address + '/?theme=' + [Uri]::EscapeDataString($theme) + '&scale=' + $scale
            $page = Invoke-WebRequest -Uri $url -TimeoutSec 10
            if (-not $page.Content.Contains('data-theme="' + $theme + '"') -or -not $page.Content.Contains('font-size: ' + $scale + '%')) { throw "Unexpected sample theme/scale at $theme/$scale." }
            $page.Content | Set-Content -LiteralPath (Join-Path $outputPath ($theme.Replace(' ','-') + '-' + $scale + '.html')) -Encoding utf8
        }
    }
    if ($process.HasExited) { throw 'Sample exited during verification. Inspect runtime-sample logs.' }
    Write-Host 'PASS runnable website sample HTTP/client startup and all nine theme/interface configurations'
} finally {
    if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
}

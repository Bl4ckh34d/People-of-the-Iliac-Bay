param()
. (Join-Path $PSScriptRoot 'TextProtocol.ps1')


$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$dfuRoot = Split-Path $repoRoot -Parent
$launcher = Join-Path $PSScriptRoot 'StartLocalDialogue.ps1'
$powershell = (Get-Command powershell.exe -ErrorAction Stop).Source
$expectedModel = Join-Path $dfuRoot 'local-llm\models\qwen2.5-7b-instruct-q4_k_m-00001-of-00002.gguf'
$testRoot = Join-Path $repoRoot 'work\launcher-selftest'
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

function Get-FreePort {
    $probe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $probe.Start()
    try { return ([System.Net.IPEndPoint]$probe.LocalEndpoint).Port }
    finally { $probe.Stop() }
}

function Start-HealthStub([int]$port, [string]$healthStatus, [string]$modelPath, [string]$propsOverride = '') {
    $props = if ($propsOverride) { $propsOverride } else {
        @{ model_path = $modelPath; default_generation_settings = @{ n_ctx = 4096; params = @{} }; total_slots = 1 } | ConvertTo-Json -Compress -Depth 5
    }
    $job = Start-Job -ArgumentList $port, $healthStatus, $props -ScriptBlock {
        param($listenPort, $status, $properties)
        $ErrorActionPreference = 'Stop'
        $listener = [System.Net.HttpListener]::new()
        $listener.Prefixes.Add("http://127.0.0.1:$listenPort/")
        $listener.Start()
        try {
            $stopping = $false
            while ($listener.IsListening -and -not $stopping) {
                $context = $listener.GetContext()
                if ($context.Request.Url.AbsolutePath -eq '/shutdown') {
                    $context.Response.StatusCode = 200
                    $body = '{}'
                    $stopping = $true
                } elseif ($context.Request.Url.AbsolutePath -eq '/health') {
                    $context.Response.StatusCode = [int]$status
                    $body = if ([int]$status -eq 200) { '{"status":"ok"}' } else { '{"status":"loading"}' }
                } elseif ($context.Request.Url.AbsolutePath -eq '/props') {
                    $context.Response.StatusCode = 200
                    $body = $properties
                } else {
                    $context.Response.StatusCode = 404
                    $body = '{}'
                }
                $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
                $context.Response.ContentType = 'application/json'
                $context.Response.ContentLength64 = $bytes.Length
                $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
                $context.Response.Close()
            }
        } finally { $listener.Stop(); $listener.Close() }
    }
    $until = [DateTime]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 100
        if ($job.State -eq 'Failed') { throw "Health stub failed: $($job.ChildJobs[0].JobStateInfo.Reason)" }
        $client = [System.Net.Sockets.TcpClient]::new()
        try { $client.Connect('127.0.0.1', $port); return $job }
        catch { if ([DateTime]::UtcNow -ge $until) { throw "Health stub did not listen on $port" } }
        finally { $client.Dispose() }
    } while ([DateTime]::UtcNow -lt $until)
    throw "Health stub did not listen on $port"
}

function Invoke-Launcher([int]$port, [string]$name) {
    $out = Join-Path $testRoot "$name.stdout.log"
    $err = Join-Path $testRoot "$name.stderr.log"
    $argumentLine = '-NoProfile -ExecutionPolicy Bypass -File "' + $launcher + '" -Background -Port ' + $port +
        ' -ContextSize 4096 -ExecutionDevice GPU -GpuLayers 99 -ReadinessTimeoutSeconds 2 -LogDirectory "' + $testRoot + '"'
    $process = Start-Process -FilePath $powershell -ArgumentList $argumentLine -WindowStyle Hidden -PassThru -Wait `
        -RedirectStandardOutput $out -RedirectStandardError $err
    return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = (Get-Content $out -Raw -ErrorAction SilentlyContinue) + (Get-Content $err -Raw -ErrorAction SilentlyContinue) }
}

function Stop-HealthStub($job, [int]$port) {
    try { Invoke-WebRequest -Uri "http://127.0.0.1:$port/shutdown" -TimeoutSec 2 -UseBasicParsing | Out-Null } catch { }
    Wait-Job $job -Timeout 5 | Out-Null
    Remove-Job $job -Force -ErrorAction SilentlyContinue
}

$beforeIds = @((Get-Process -Name 'llama-server' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id))
$healthyPort = Get-FreePort
$healthyJob = Start-HealthStub $healthyPort 200 $expectedModel
try {
    $healthy = Invoke-Launcher $healthyPort 'healthy-reuse'
    if ($healthy.ExitCode -eq 0 -or (-not ((([string]$healthy.Output).IndexOf('unrelated process',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) {
        throw "A foreign listener with matching health and props was not rejected: exit=$($healthy.ExitCode); output=$($healthy.Output)"
    }
} finally { Stop-HealthStub $healthyJob $healthyPort }

$foreignPort = Get-FreePort
$foreignJob = Start-HealthStub $foreignPort 200 'C:\unrelated\model.gguf'
try {
    $foreign = Invoke-Launcher $foreignPort 'foreign-model'
    if ($foreign.ExitCode -eq 0 -or (-not ((([string]$foreign.Output).IndexOf('unrelated process',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) {
        throw "An occupied foreign port was not rejected: exit=$($foreign.ExitCode); output=$($foreign.Output)"
    }
} finally { Stop-HealthStub $foreignJob $foreignPort }

$loadingPort = Get-FreePort
$loadingJob = Start-HealthStub $loadingPort 503 $expectedModel
try {
    $loading = Invoke-Launcher $loadingPort 'health-not-ready'
    if ($loading.ExitCode -eq 0 -or (-not ((([string]$loading.Output).IndexOf('unrelated process',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) {
        throw "An unready occupied port was not rejected: exit=$($loading.ExitCode); output=$($loading.Output)"
    }
} finally { Stop-HealthStub $loadingJob $loadingPort }

$malformedPort = Get-FreePort
$malformedJob = Start-HealthStub $malformedPort 200 $expectedModel '{malformed json'
try {
    $malformed = Invoke-Launcher $malformedPort 'malformed-busy-port'
    if ($malformed.ExitCode -eq 0 -or (-not ((([string]$malformed.Output).IndexOf('unrelated process',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) {
        throw "A busy port with malformed endpoint data was not rejected before startup: exit=$($malformed.ExitCode); output=$($malformed.Output)"
    }
} finally { Stop-HealthStub $malformedJob $malformedPort }

$afterIds = @((Get-Process -Name 'llama-server' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id))
if (@(Compare-Object $beforeIds $afterIds).Count -ne 0) { throw 'Launcher self-test changed llama-server process set.' }
Write-Output 'LOCAL_DIALOGUE_LAUNCHER_SELFTEST_PASS: foreign, unready, and malformed busy ports rejected; llama-server process set unchanged.'

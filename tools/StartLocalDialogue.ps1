param(
    [ValidateRange(1024, 16384)] [int]$ContextSize = 4096,
    [ValidateRange(0, 200)] [int]$GpuLayers = 99,
    [ValidateRange(1024, 65535)] [int]$Port = 8080
)

$ErrorActionPreference = 'Stop'
$dfuRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$runtimeRoot = Join-Path $dfuRoot 'local-llm'
$server = Join-Path $runtimeRoot 'bin\llama-server.exe'
$model = Join-Path $runtimeRoot 'models\qwen2.5-7b-instruct-q4_k_m-00001-of-00002.gguf'
$secondShard = Join-Path $runtimeRoot 'models\qwen2.5-7b-instruct-q4_k_m-00002-of-00002.gguf'

if (-not (Test-Path -LiteralPath $server)) { throw "Missing llama.cpp server: $server" }
if (-not (Test-Path -LiteralPath $model)) { throw "Missing Qwen model shard: $model" }
if (-not (Test-Path -LiteralPath $secondShard)) { throw "Missing Qwen model shard: $secondShard" }

& $server -m $model -ngl $GpuLayers -c $ContextSize --host 127.0.0.1 --port $Port -np 1

param(
    [Parameter(Mandatory=$true)] [string]$ManifestPath
)

$ErrorActionPreference = 'Stop'
$lifecycleHelper = Join-Path $PSScriptRoot 'LocalServiceLifecycle.ps1'
if (-not (Test-Path -LiteralPath $lifecycleHelper -PathType Leaf)) { throw "Missing lifecycle helper: $lifecycleHelper" }
. $lifecycleHelper

$manifestPath = [IO.Path]::GetFullPath($ManifestPath)
try {
    $manifest = Read-LocalServiceLifecycleManifest -Path $manifestPath
    Invoke-LocalGameOwnedServiceMonitor -GameProcesses $manifest.GameProcesses -OwnedServices $manifest.OwnedServices | Out-Null
    Remove-Item -LiteralPath $manifestPath -Force -ErrorAction SilentlyContinue
} catch {
    $logPath = "$manifestPath.watcher-error.log"
    $message = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')] $($_.Exception.Message)"
    try { [IO.File]::AppendAllText($logPath, $message + [Environment]::NewLine, [Text.UTF8Encoding]::new($false)) } catch { }
    throw
}

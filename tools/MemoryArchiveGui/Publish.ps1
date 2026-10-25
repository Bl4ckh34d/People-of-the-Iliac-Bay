[CmdletBinding()]
param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'work/MemoryArchiveGui/win-x64'
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)

# Reuse the CLI publisher's work-directory and linked-path checks, and ship its
# unmodified executable alongside the GUI. Neither build step opens any save.
& (Join-Path $PSScriptRoot '../PublishMemoryArchiveTool.ps1') -OutputDirectory $outputRoot
& dotnet publish (Join-Path $PSScriptRoot 'MemoryArchiveGui.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false `
    --output $outputRoot --nologo
if ($LASTEXITCODE -ne 0) { throw 'Guided helper publish failed. Check the .NET 8 SDK and runtime-pack restore output.' }
foreach ($name in @('MemoryArchiveGui.exe', 'MemoryArchiveTool.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $outputRoot $name) -PathType Leaf)) {
        throw "Publish did not produce the required executable: $name"
    }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $outputRoot 'Portable-save-help.md')
Write-Output "Published offline Windows helper and CLI: $outputRoot"
Write-Output 'Distribute both executable files together. Players do not need the .NET SDK or runtime installed.'

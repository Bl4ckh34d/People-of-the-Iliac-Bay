[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference='Stop'
$toolsRoot=[IO.Path]::GetFullPath($PSScriptRoot)
$repoRoot=[IO.Path]::GetFullPath((Join-Path $toolsRoot '..'))
$workRoot=[IO.Path]::GetFullPath((Join-Path $repoRoot 'work'))
if([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory=Join-Path $workRoot 'MemoryArchiveTool/win-x64'
}
$outputRoot=[IO.Path]::GetFullPath($OutputDirectory)
$workPrefix=$workRoot.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
if(-not $outputRoot.StartsWith($workPrefix,[StringComparison]::OrdinalIgnoreCase)) {
    throw "Output must be inside the repository work directory: $workRoot"
}
if(Test-Path -LiteralPath $outputRoot -PathType Leaf) {
    throw "Output path is a file, not a directory: $outputRoot"
}

# Refuse existing junctions/symlinks in the output path. The script never cleans the target.
$current=$outputRoot
while($current -and $current.StartsWith($workRoot,[StringComparison]::OrdinalIgnoreCase)) {
    if(Test-Path -LiteralPath $current) {
        if(([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Linked output paths are unsupported: $current"
        }
    }
    if([string]::Equals($current,$workRoot,[StringComparison]::OrdinalIgnoreCase)) {break}
    $current=[IO.Path]::GetDirectoryName($current)
}

$project=Join-Path $toolsRoot 'MemoryArchiveTool/MemoryArchiveTool.csproj'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
& dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    --output $outputRoot --nologo
if($LASTEXITCODE -ne 0) {
    throw 'Self-contained publish failed. Check the .NET 8 SDK and runtime-pack restore output, then retry.'
}

$executable=Join-Path $outputRoot 'MemoryArchiveTool.exe'
if(-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Publish completed without the expected executable: $executable"
}
Write-Output "Published self-contained win-x64 tool: $executable"

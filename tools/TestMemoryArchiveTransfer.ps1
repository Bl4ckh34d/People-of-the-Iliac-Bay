param([Parameter(Mandatory=$true)][string]$SaveFolder)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$workRoot=[IO.Path]::GetFullPath((Join-Path $repo 'work'))
$testRoot=Join-Path $workRoot ('archive-transfer-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'MemoryArchiveTool/MemoryArchiveTool.csproj') -c Release --nologo | Out-Null
if($LASTEXITCODE -ne 0) {throw 'Archive transfer tool build failed.'}
$tool=Join-Path $PSScriptRoot 'MemoryArchiveTool/bin/Release/net8.0/MemoryArchiveTool.dll'
try {
    $package=Join-Path $testRoot 'save.zip'
    dotnet $tool export $SaveFolder $package
    if($LASTEXITCODE -ne 0) {throw 'Save export failed.'}
    $importRoot=Join-Path $testRoot 'import'
    dotnet $tool import $package $importRoot SAVE0
    if($LASTEXITCODE -ne 0) {throw 'Save import failed.'}
    $original=(Get-FileHash (Join-Path $SaveFolder 'mod_livingcities.txt')).Hash
    $imported=Join-Path $importRoot 'SAVE0/mod_livingcities.txt'
    if((Get-FileHash $imported).Hash -ne $original) {throw 'Imported save content changed.'}
    if(@(Get-ChildItem (Join-Path $importRoot 'PeopleMemory') -Recurse -Filter *.pim).Count -eq 0) {throw 'Export omitted memory ancestry.'}
    dotnet $tool import $package $importRoot SAVE0 2>&1 | Out-Null
    if($LASTEXITCODE -eq 0 -or (Get-FileHash $imported).Hash -ne $original) {throw 'Import overwrote an existing save.'}

    $bad=Join-Path $testRoot 'traversal.zip'
    $zip=[IO.Compression.ZipFile]::Open($bad,[IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry=$zip.CreateEntry('Save/../../escape.txt'); $stream=$entry.Open()
        try {$stream.WriteByte(1)} finally {$stream.Dispose()}
    } finally {$zip.Dispose()}
    dotnet $tool import $bad $importRoot SAVE1 2>&1 | Out-Null
    if($LASTEXITCODE -eq 0 -or (Test-Path (Join-Path $importRoot 'SAVE1')) -or (Test-Path (Join-Path $testRoot 'escape.txt'))) {throw 'Unsafe ZIP entry was accepted.'}

    $corrupt=Join-Path $testRoot 'corrupt.zip'; Copy-Item -LiteralPath $package -Destination $corrupt
    $zip=[IO.Compression.ZipFile]::Open($corrupt,[IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry=$zip.Entries | Where-Object {$_.FullName.EndsWith('.pim')} | Select-Object -First 1
        $name=$entry.FullName; $entry.Delete(); $entry=$zip.CreateEntry($name); $stream=$entry.Open()
        try {$stream.WriteByte(1)} finally {$stream.Dispose()}
    } finally {$zip.Dispose()}
    dotnet $tool import $corrupt $importRoot SAVE2 2>&1 | Out-Null
    if($LASTEXITCODE -eq 0 -or (Test-Path (Join-Path $importRoot 'SAVE2'))) {throw 'Corrupt ancestry was imported.'}
    Write-Output 'LIVING_CITIES_MEMORY_TRANSFER_PASS: real save export/import, identical payload, no overwrite, path traversal rejected, corrupt ancestry rejected.'
} finally {
    $resolved=[IO.Path]::GetFullPath($testRoot)
    if(-not $resolved.StartsWith($workRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw 'Unsafe transfer-test cleanup path.'}
    if(Test-Path -LiteralPath $resolved) {Remove-Item -LiteralPath $resolved -Recurse -Force}
}

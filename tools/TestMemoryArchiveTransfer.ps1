
. (Join-Path $PSScriptRoot 'TextProtocol.ps1')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$workRoot=[IO.Path]::GetFullPath((Join-Path $repo 'work'))
$testRoot=Join-Path $workRoot ('archive-transfer-'+[Guid]::NewGuid().ToString('N'))
if([Diagnostics.Process]::GetProcessesByName('DaggerfallUnity').Count -gt 0) {
    throw 'Close Daggerfall Unity before running the archive transfer test. Test uses temporary fixtures only.'
}
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'MemoryArchiveTool/MemoryArchiveTool.csproj') -c Release --nologo | Out-Null
if($LASTEXITCODE -ne 0) {throw 'Archive transfer tool build failed.'}
$tool=Join-Path $PSScriptRoot 'MemoryArchiveTool/bin/Release/net8.0/MemoryArchiveTool.dll'

function Write-Utf8Field([IO.BinaryWriter]$Writer,[string]$Value) {
    $bytes=[Text.Encoding]::UTF8.GetBytes($Value)
    $Writer.Write([int]$bytes.Length)
    $Writer.Write($bytes)
}

function New-TemporarySaveFixture([string]$Root) {
    $save=Join-Path $Root 'SAVE4'
    $memoryRoot=Join-Path $Root 'PeopleMemory'
    $archiveId='00112233445566778899aabbccddeeff'
    New-Item -ItemType Directory -Path $save -Force | Out-Null

    $rawStream=[IO.MemoryStream]::new()
    $writer=[IO.BinaryWriter]::new($rawStream,[Text.Encoding]::UTF8,$true)
    try {
        $writer.Write([int]0x50494d31)
        Write-Utf8Field $writer ''
        $writer.Write([int]1)
        Write-Utf8Field $writer 'fixture:npc'
        $writer.Write([double]12345.5)
        Write-Utf8Field $writer 'Sundas, Morning Star 2'
        Write-Utf8Field $writer 'The White Muskrat'
        Write-Utf8Field $writer 'I told the player about the silver brooch.'
        Write-Utf8Field $writer 'I remember the silver brooch.'
        Write-Utf8Field $writer ''
        Write-Utf8Field $writer 'native'
        $writer.Flush()
        $uncompressed=$rawStream.ToArray()
    } finally {$writer.Dispose();$rawStream.Dispose()}

    $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($uncompressed)).ToLowerInvariant()
    $chunk=Join-Path $memoryRoot "$archiveId/$($hash.Substring(0,2))/$hash.pim"
    New-Item -ItemType Directory -Path (Split-Path $chunk -Parent) -Force | Out-Null
    $file=[IO.File]::Create($chunk)
    try {
        $gzip=[IO.Compression.GZipStream]::new($file,[IO.Compression.CompressionLevel]::Optimal)
        try {$gzip.Write($uncompressed,0,$uncompressed.Length)} finally {$gzip.Dispose()}
    } finally {$file.Dispose()}

    $checkpoint=@{Version=1;ArchiveId=$archiveId;Head=$hash}
    $state=@{ConversationArchive=$checkpoint;Fixture='portable-memory-transfer-test'}
    $sourceData=Join-Path $save 'mod_peopleoftheiliacbay.txt'
    [IO.File]::WriteAllText($sourceData,(ConvertTo-Json -InputObject $state -Compress -Depth 8),[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $save 'fixture-payload.dat'),'fixture save payload',[Text.UTF8Encoding]::new($false))
    [pscustomobject]@{Save=$save;MemoryRoot=$memoryRoot;ArchiveId=$archiveId;Head=$hash;SourceData=$sourceData;Chunk=$chunk}
}

function Invoke-MemoryTool([string[]]$Arguments) {
    $output=& dotnet $tool @Arguments 2>&1
    [pscustomobject]@{ExitCode=$LASTEXITCODE;Output=($output -join "`n")}
}

function Replace-ZipEntry([string]$Path,[string]$Name,[byte[]]$Bytes) {
    $zip=[IO.Compression.ZipFile]::Open($Path,[IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry=$zip.GetEntry($Name)
        if($entry) {$entry.Delete()}
        if($null -ne $Bytes) {
            $entry=$zip.CreateEntry($Name)
            $stream=$entry.Open()
            try {$stream.Write($Bytes,0,$Bytes.Length)} finally {$stream.Dispose()}
        }
    } finally {$zip.Dispose()}
}

function Copy-Zip([string]$Source,[string]$Target) {Copy-Item -LiteralPath $Source -Destination $Target}

function Assert-FailedImportLeavesNoState([string]$Package,[string]$Slot,[string]$ExpectedText) {
    $destination=Join-Path $testRoot ("reject-"+$Slot)
    $result=Invoke-MemoryTool @('import',$Package,$destination,$Slot)
    if($result.ExitCode -eq 0 -or ([string]$result.Output).IndexOf($ExpectedText,[StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Expected rejection containing '$ExpectedText'; exit=$($result.ExitCode): $($result.Output)"
    }
    if((Test-Path -LiteralPath (Join-Path $destination $Slot)) -or (Test-Path -LiteralPath (Join-Path $destination 'PeopleMemory'))) {
        throw 'Rejected import published a save or memory chunks before validation completed.'
    }
}

try {
    $fixture=New-TemporarySaveFixture (Join-Path $testRoot 'source')
    $package=Join-Path $testRoot 'save.zip'
    $export=Invoke-MemoryTool @('export',$fixture.Save,$package)
    if($export.ExitCode -ne 0) {throw "Export failed: $($export.Output)"}

    $zip=[IO.Compression.ZipFile]::OpenRead($package)
    try {
        $manifestEntry=$zip.GetEntry('manifest.json')
        if(-not $manifestEntry) {throw 'Export omitted manifest.json.'}
        $manifestStream=$manifestEntry.Open()
        try {
            $manifestDocument=[System.Text.Json.JsonDocument]::Parse($manifestStream)
            $manifest=$manifestDocument.RootElement
        } finally {$manifestStream.Dispose()}
        if($manifest.GetProperty('formatVersion').GetInt32() -ne 2) {throw 'Exported manifest has wrong format version.'}
        if($manifest.GetProperty('cueHead').GetString() -ne '' -or
            @($manifest.GetProperty('cueAncestry').EnumerateArray()).Count -ne 0) {
            throw 'Legacy fixture unexpectedly claims a conversation cue ancestry.'
        }
        if($manifest.GetProperty('archiveId').GetString() -ne $fixture.ArchiveId -or
            $manifest.GetProperty('head').GetString() -ne $fixture.Head) {throw 'Manifest checkpoint differs from source save.'}
        $ancestry=@($manifest.GetProperty('ancestry').EnumerateArray())
        if($ancestry.Count -ne 1 -or $ancestry[0].GetString() -ne $fixture.Head) {throw 'Manifest ancestry differs from source checkpoint.'}
        $files=$manifest.GetProperty('files')
        if(@($files.EnumerateArray()).Count -ne 3) {throw 'Manifest must checksum save payload files and memory chunks.'}
        foreach($entry in $files.EnumerateArray()) {
            if([IO.Path]::IsPathRooted($entry.GetProperty('path').GetString())) {throw 'Manifest contains an absolute path.'}
            if($entry.GetProperty('length').GetInt64() -lt 0 -or -not (Test-HexValue $entry.GetProperty('sha256').GetString() 64 -Lowercase)) {
                throw 'Manifest file length or SHA-256 is invalid.'
            }
        }
    } finally {
        if($manifestDocument) {$manifestDocument.Dispose()}
        $zip.Dispose()
    }

    $importRoot=Join-Path $testRoot 'import'
    $import=Invoke-MemoryTool @('import',$package,$importRoot,'SAVE0')
    if($import.ExitCode -ne 0) {throw "Import failed: $($import.Output)"}
    $imported=Join-Path $importRoot 'SAVE0/mod_peopleoftheiliacbay.txt'
    if((Get-FileHash $fixture.SourceData).Hash -ne (Get-FileHash $imported).Hash) {throw 'Imported save payload changed.'}
    $importedChunk=Join-Path $importRoot "PeopleMemory/$($fixture.ArchiveId)/$($fixture.Head.Substring(0,2))/$($fixture.Head).pim"
    if(-not (Test-Path $importedChunk)) {throw 'Import omitted checkpoint ancestry.'}
    $duplicate=Invoke-MemoryTool @('import',$package,$importRoot,'SAVE0')
    if($duplicate.ExitCode -eq 0 -or -not (Test-Path $imported)) {throw 'Import overwrote an occupied save slot.'}

    $tampered=Join-Path $testRoot 'tampered.zip'
    Copy-Zip $package $tampered
    Replace-ZipEntry $tampered 'Save/fixture-payload.dat' ([Text.Encoding]::UTF8.GetBytes('changed after manifest'))
    Assert-FailedImportLeavesNoState $tampered 'SAVE1' 'checksum'

    $unlisted=Join-Path $testRoot 'unlisted.zip'
    Copy-Zip $package $unlisted
    $zip=[IO.Compression.ZipFile]::Open($unlisted,[IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry=$zip.CreateEntry('Save/unlisted.dat');$stream=$entry.Open()
        try {$bytes=[Text.Encoding]::UTF8.GetBytes('unlisted');$stream.Write($bytes,0,$bytes.Length)} finally {$stream.Dispose()}
    } finally {$zip.Dispose()}
    Assert-FailedImportLeavesNoState $unlisted 'SAVE2' 'unlisted'

    $missingChunk=Join-Path $testRoot 'missing-chunk.zip'
    Copy-Zip $package $missingChunk
    Replace-ZipEntry $missingChunk "PeopleMemory/$($fixture.ArchiveId)/$($fixture.Head.Substring(0,2))/$($fixture.Head).pim" $null
    Assert-FailedImportLeavesNoState $missingChunk 'SAVE7' 'missing'

    $collisionRoot=Join-Path $testRoot 'collision-import'
    $collisionChunk=Join-Path $collisionRoot "PeopleMemory/$($fixture.ArchiveId)/$($fixture.Head.Substring(0,2))/$($fixture.Head).pim"
    New-Item -ItemType Directory -Path (Split-Path $collisionChunk -Parent) -Force | Out-Null
    [IO.File]::WriteAllText($collisionChunk,'corrupt existing chunk',[Text.UTF8Encoding]::new($false))
    $collision=Invoke-MemoryTool @('import',$package,$collisionRoot,'SAVE8')
    if($collision.ExitCode -eq 0 -or (Test-Path -LiteralPath (Join-Path $collisionRoot 'SAVE8')) -or
        (Get-Content -Raw $collisionChunk) -ne 'corrupt existing chunk') {
        throw 'Import overwrote a corrupt existing content-addressed chunk or published its save.'
    }

    $legacy=Join-Path $testRoot 'legacy.zip'
    Copy-Zip $package $legacy
    Replace-ZipEntry $legacy 'manifest.json' $null
    $legacyRoot=Join-Path $testRoot 'legacy-import'
    $legacyImport=Invoke-MemoryTool @('import',$legacy,$legacyRoot,'SAVE3')
    if($legacyImport.ExitCode -ne 0 -or -not ([string]$legacyImport.Output).Contains('Legacy package detected: verifying SAVE structure and exact archive ancestry; save-file manifest checks are unavailable')) {
        throw "Legacy import failed: $($legacyImport.Output)"
    }
    if(-not (Test-Path -LiteralPath (Join-Path $legacyRoot 'SAVE3/mod_peopleoftheiliacbay.txt'))) {throw 'Legacy import omitted save payload.'}

    $legacyExtra=Join-Path $testRoot 'legacy-extra.zip'
    Copy-Zip $legacy $legacyExtra
    $zip=[IO.Compression.ZipFile]::Open($legacyExtra,[IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry=$zip.CreateEntry('unexpected.txt');$stream=$entry.Open()
        try {$bytes=[Text.Encoding]::UTF8.GetBytes('extra');$stream.Write($bytes,0,$bytes.Length)} finally {$stream.Dispose()}
    } finally {$zip.Dispose()}
    Assert-FailedImportLeavesNoState $legacyExtra 'SAVE5' 'unexpected'

    $badPath=Join-Path $testRoot 'traversal.zip'
    $zip=[IO.Compression.ZipFile]::Open($badPath,[IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry=$zip.CreateEntry('Save/../../escape.txt');$stream=$entry.Open()
        try {$stream.WriteByte(1)} finally {$stream.Dispose()}
    } finally {$zip.Dispose()}
    Assert-FailedImportLeavesNoState $badPath 'SAVE6' 'unsafe'
    if(Test-Path (Join-Path $testRoot 'escape.txt')) {throw 'Path traversal wrote outside staging.'}

    Write-Output 'PEOPLE_OF_THE_ILIAC_BAY_MEMORY_TRANSFER_PASS: manifest/checkpoint, exact ancestry, payload integrity, no overwrite, legacy compatibility, unlisted files, missing chunks, corrupt collision, traversal and no-write-on-rejection.'
} finally {
    $resolved=[IO.Path]::GetFullPath($testRoot)
    if(-not $resolved.StartsWith($workRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw 'Unsafe transfer-test cleanup path.'}
    if(Test-Path -LiteralPath $resolved) {Remove-Item -LiteralPath $resolved -Recurse -Force}
}


. (Join-Path $PSScriptRoot 'TextProtocol.ps1')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$workRoot=[IO.Path]::GetFullPath((Join-Path $repo 'work'))
$testRoot=[IO.Path]::GetFullPath((Join-Path $workRoot ('published-memory-tool-'+[Guid]::NewGuid().ToString('N'))))
if([Diagnostics.Process]::GetProcessesByName('DaggerfallUnity').Count -gt 0) {
    throw 'Close Daggerfall Unity before testing the published tool. Test uses temporary fixtures only.'
}
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null

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
    $state=@{ConversationArchive=$checkpoint;Fixture='published-tool-test'}
    $sourceData=Join-Path $save 'mod_peopleoftheiliacbay.txt'
    [IO.File]::WriteAllText($sourceData,(ConvertTo-Json -InputObject $state -Compress -Depth 8),[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $save 'fixture-payload.dat'),'fixture save payload',[Text.UTF8Encoding]::new($false))
    [pscustomobject]@{Save=$save;ArchiveId=$archiveId;Head=$hash;SourceData=$sourceData}
}

function Invoke-PublishedTool([string]$Executable,[string[]]$Arguments) {
    $start=[Diagnostics.ProcessStartInfo]::new()
    $start.FileName=$Executable
    $start.WorkingDirectory=$testRoot
    $start.UseShellExecute=$false
    $start.RedirectStandardOutput=$true
    $start.RedirectStandardError=$true
    foreach($argument in $Arguments) {$start.ArgumentList.Add($argument)}
    $start.Environment['PATH']=Join-Path $env:SystemRoot 'System32'
    $start.Environment['DOTNET_ROOT']=Join-Path $testRoot 'missing-runtime'
    $start.Environment['DOTNET_ROOT_X64']=Join-Path $testRoot 'missing-runtime-x64'
    $start.Environment['DOTNET_ROOT_X86']=Join-Path $testRoot 'missing-runtime-x86'
    $process=[Diagnostics.Process]::new()
    $process.StartInfo=$start
    if(-not $process.Start()) {throw 'Could not start the published memory archive tool.'}
    $stdout=$process.StandardOutput.ReadToEnd()
    $stderr=$process.StandardError.ReadToEnd()
    $process.WaitForExit()
    [pscustomobject]@{ExitCode=$process.ExitCode;Output=($stdout+"`n"+$stderr)}
}

try {
    $outsideOutput=Join-Path $env:TEMP ('memory-tool-outside-work-'+[Guid]::NewGuid().ToString('N'))
    try {
        & (Join-Path $PSScriptRoot 'PublishMemoryArchiveTool.ps1') -OutputDirectory $outsideOutput
        throw 'Publisher accepted an output path outside the repository work directory.'
    } catch {
        if((-not ((([string]$_.Exception.Message).IndexOf('Output must be inside the repository work directory',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) {throw}
    }
    if(Test-Path -LiteralPath $outsideOutput) {throw 'Rejected publisher path created an output directory.'}

    $publishDir=Join-Path $testRoot 'published'
    & (Join-Path $PSScriptRoot 'PublishMemoryArchiveTool.ps1') -OutputDirectory $publishDir
    if($LASTEXITCODE -ne 0) {throw 'Self-contained memory archive tool publishing failed.'}
    $executable=Join-Path $publishDir 'MemoryArchiveTool.exe'
    if(-not (Test-Path -LiteralPath $executable -PathType Leaf)) {throw 'Publish did not produce MemoryArchiveTool.exe.'}

    $help=Invoke-PublishedTool $executable @()
    if($help.ExitCode -eq 0 -or (-not ((([string]$help.Output).IndexOf('Usage: export',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) {throw "Published executable did not show command usage: $($help.Output)"}

    $fixture=New-TemporarySaveFixture (Join-Path $testRoot 'source')
    $package=Join-Path $testRoot 'save.zip'
    $export=Invoke-PublishedTool $executable @('export',$fixture.Save,$package)
    if($export.ExitCode -ne 0) {throw "Published executable export failed: $($export.Output)"}
    $zip=[IO.Compression.ZipFile]::OpenRead($package)
    try {
        $manifestEntry=$zip.GetEntry('manifest.json')
        if(-not $manifestEntry) {throw 'Published executable export omitted manifest.json.'}
        $manifestDocument=[System.Text.Json.JsonDocument]::Parse($manifestEntry.Open())
        try {
            $manifest=$manifestDocument.RootElement
            if($manifest.GetProperty('head').GetString() -ne $fixture.Head) {throw 'Published executable manifest has wrong checkpoint.'}
            if(@($manifest.GetProperty('ancestry').EnumerateArray()).Count -ne 1) {throw 'Published executable manifest has wrong ancestry.'}
        } finally {$manifestDocument.Dispose()}
    } finally {$zip.Dispose()}

    $destination=Join-Path $testRoot 'import'
    $import=Invoke-PublishedTool $executable @('import',$package,$destination,'SAVE0')
    if($import.ExitCode -ne 0) {throw "Published executable import failed: $($import.Output)"}
    $imported=Join-Path $destination 'SAVE0/mod_peopleoftheiliacbay.txt'
    if((Get-FileHash $fixture.SourceData).Hash -ne (Get-FileHash $imported).Hash) {throw 'Published executable changed the imported save payload.'}
    $importedChunk=Join-Path $destination "PeopleMemory/$($fixture.ArchiveId)/$($fixture.Head.Substring(0,2))/$($fixture.Head).pim"
    if(-not (Test-Path -LiteralPath $importedChunk)) {throw 'Published executable import omitted memory ancestry.'}

    Write-Output 'PUBLISHED_MEMORY_ARCHIVE_TOOL_PASS: usage, self-contained execution without PATH/DOTNET_ROOT runtime discovery, manifest export, exact ancestry and import from temporary fixtures.'
} finally {
    $resolved=[IO.Path]::GetFullPath($testRoot)
    if(-not $resolved.StartsWith($workRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw 'Unsafe published-tool test cleanup path.'}
    if(Test-Path -LiteralPath $resolved) {Remove-Item -LiteralPath $resolved -Recurse -Force}
}

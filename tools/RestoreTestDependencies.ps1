param([string]$DfuRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))

$ErrorActionPreference = 'Stop'
# Native serializer and Unity dependencies from a pinned official DFU release.
$expected = 'BA65E2BB2887333BBE8FC5A905A09D58AC4CE9875443CABACCBD6698E281BF92'
$uri = 'https://github.com/Interkarma/daggerfall-unity/releases/download/v1.1.1-cve-2025/dfu_linux_64bit-v1.1.1.zip'
$archiveHash = 'BE3C476AE92972F42448757719266AD82C04868EB7B6F3A3C90FCFCF1490849A'
$destination = Join-Path $DfuRoot 'Player/DaggerfallUnity_Data/Managed/FullSerializer - Unity.dll'
if (Test-Path -LiteralPath $destination) {
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expected) {
        throw "Existing serializer differs from the pinned DFU test dependency: $destination"
    }
    $managed = Split-Path $destination -Parent
    if ((Test-Path -LiteralPath (Join-Path $managed 'UnityEngine.dll')) -and
        (Test-Path -LiteralPath (Join-Path $managed 'UnityEngine.CoreModule.dll'))) {
        Write-Output 'Native serializer and Unity test dependencies available.'
        return
    }
}
New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('dfu-test-deps-' + [Guid]::NewGuid().ToString('N') + '.zip')
try {
    Invoke-WebRequest -Uri $uri -OutFile $temporary
    if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $archiveHash) {
        throw 'Downloaded DFU release does not match the pinned SHA-256.'
    }
    $managed = Split-Path $destination -Parent
    $archive = [IO.Compression.ZipFile]::OpenRead($temporary)
    try {
        foreach ($entry in $archive.Entries) {
            if (-not $entry.FullName.Contains('/Managed/')) { continue }
            if ($entry.Name -ne 'FullSerializer - Unity.dll' -and
                -not ($entry.Name.StartsWith('UnityEngine',[StringComparison]::Ordinal) -and
                      $entry.Name.EndsWith('.dll',[StringComparison]::Ordinal))) { continue }
            $target = Join-Path $managed $entry.Name
            # Never overwrite an installed game assembly.
            if (-not (Test-Path -LiteralPath $target)) {
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$target,$false)
            }
        }
    } finally { $archive.Dispose() }
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $expected -or
        -not (Test-Path -LiteralPath (Join-Path $managed 'UnityEngine.CoreModule.dll'))) {
        throw 'Pinned release did not supply the expected native dependencies.'
    }
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
}
Write-Output 'Pinned native test dependencies installed.'

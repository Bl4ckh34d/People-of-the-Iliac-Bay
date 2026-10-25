function Test-LocalOwnershipHash([string]$Value) {
    if ($Value.Length -ne 64) { return $false }
    foreach ($character in $Value.ToCharArray()) {
        if ('0123456789abcdefABCDEF'.IndexOf($character) -lt 0) { return $false }
    }
    return $true
}

function ConvertTo-LocalServiceCreationUtc([object]$Value) {
    if ($null -eq $Value) { throw 'Process creation time is missing.' }
    $creation = $Value
    if ($creation -isnot [DateTime]) {
        try { $creation = [System.Management.ManagementDateTimeConverter]::ToDateTime([string]$Value) }
        catch {
            try { $creation = [DateTime]::Parse([string]$Value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal) }
            catch { throw 'Process creation time could not be parsed.' }
        }
    }
    $utc = $creation.ToUniversalTime()
    # Win32_Process.CreationDate carries microsecond precision; Process.StartTime can carry 100 ns.
    $normalizedTicks = $utc.Ticks - ($utc.Ticks % 10)
    return ([DateTime]::new($normalizedTicks, [DateTimeKind]::Utc)).ToString('o', [Globalization.CultureInfo]::InvariantCulture)
}

function Get-LocalServiceConfigIdentity([object]$Configuration) {
    $json = ConvertTo-Json -InputObject $Configuration -Compress -Depth 10
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-LocalServiceOwnershipFingerprint([object]$Record) {
    return Get-LocalServiceConfigIdentity ([ordered]@{
        schemaVersion = 1
        processId = [int]$Record.ProcessId
        creationTimeUtc = ConvertTo-LocalServiceCreationUtc $Record.CreationTimeUtc
        executablePath = [string]$Record.ExecutablePath
        commandLine = [string]$Record.CommandLine
        configIdentity = ([string]$Record.ConfigIdentity).ToLowerInvariant()
    })
}

function Get-LocalServiceProcessSnapshot([int]$ProcessId) {
    if ($ProcessId -lt 1) { return $null }
    return Get-CimInstance Win32_Process -Filter "ProcessId=$ProcessId" -ErrorAction SilentlyContinue
}

function New-LocalServiceOwnershipRecord {
    param(
        [Parameter(Mandatory=$true)] [ValidateRange(1, 2147483647)] [int]$ProcessId,
        [Parameter(Mandatory=$true)] [string]$ExecutablePath,
        [Parameter(Mandatory=$true)] [ValidateScript({ $_.Length -eq 64 -and @($_.ToCharArray() | Where-Object { '0123456789abcdefABCDEF'.IndexOf($_) -lt 0 }).Count -eq 0 })] [string]$ConfigIdentity
    )

    $process = Get-LocalServiceProcessSnapshot -ProcessId $ProcessId
    if (-not $process) { throw "Could not snapshot newly started local service PID $ProcessId." }
    $expectedPath = [IO.Path]::GetFullPath($ExecutablePath)
    $actualPath = [IO.Path]::GetFullPath([string]$process.ExecutablePath)
    if (-not [string]::Equals($actualPath, $expectedPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "PID $ProcessId executable did not match the launched service path; refusing to record ownership."
    }
    if (-not $process.CommandLine) { throw "Could not capture command line for newly started local service PID $ProcessId." }
    $record = [pscustomobject][ordered]@{
        ProcessId = $ProcessId
        CreationTimeUtc = ConvertTo-LocalServiceCreationUtc $process.CreationDate
        ExecutablePath = $actualPath
        CommandLine = [string]$process.CommandLine
        ConfigIdentity = $ConfigIdentity.ToLowerInvariant()
        OwnershipFingerprint = $null
    }
    $record.OwnershipFingerprint = Get-LocalServiceOwnershipFingerprint $record
    return $record
}

function Read-LocalServiceOwnershipReceipt([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return @() }
    try {
        $receipt = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        if ($receipt.schemaVersion -ne 1 -or $null -eq $receipt.processes) { return @() }
        $records = @($receipt.processes)
        foreach ($record in $records) {
            $names = @($record.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object)
            if (($names -join "`n") -cne (@('CommandLine','ConfigIdentity','CreationTimeUtc','ExecutablePath','OwnershipFingerprint','ProcessId') -join "`n")) { return @() }
            $id = 0
            if (-not [int]::TryParse([string]$record.ProcessId, [ref]$id) -or $id -lt 1) { return @() }
            if ((-not (Test-LocalOwnershipHash ([string]$record.ConfigIdentity))) -or
                (-not (Test-LocalOwnershipHash ([string]$record.OwnershipFingerprint))) -or
                [string]::IsNullOrWhiteSpace([string]$record.CommandLine) -or
                [string]::IsNullOrWhiteSpace([string]$record.ExecutablePath) -or
                [string]::IsNullOrWhiteSpace([string]$record.CreationTimeUtc)) { return @() }
            try { [void][DateTime]::Parse([string]$record.CreationTimeUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind) }
            catch { return @() }
            if (-not [string]::Equals([string]$record.OwnershipFingerprint, (Get-LocalServiceOwnershipFingerprint $record), [StringComparison]::OrdinalIgnoreCase)) { return @() }
        }
        return $records
    } catch { return @() }
}

function Write-LocalServiceOwnershipReceipt([string]$Path, [object]$Record) {
    if (-not $Path) { throw 'Ownership receipt path is required.' }
    $directory = Split-Path -Parent $Path
    if ($directory) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    $records = @(Read-LocalServiceOwnershipReceipt -Path $Path)
    $records += $Record
    $receipt = [ordered]@{ schemaVersion=1; processes=$records }
    $json = ConvertTo-Json -InputObject $receipt -Compress -Depth 6
    $tempPath = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    [IO.File]::WriteAllText($tempPath, $json, [Text.UTF8Encoding]::new($false))
    try {
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            $backupPath = "$Path.$([guid]::NewGuid().ToString('N')).bak"
            try { [IO.File]::Replace($tempPath, $Path, $backupPath) }
            finally { Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue }
        }
        else { [IO.File]::Move($tempPath, $Path) }
    } finally {
        Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
    }
}

function Test-LocalServiceOwnershipRecord([object]$Record) {
    try {
        if ($null -eq $Record -or (-not (Test-LocalOwnershipHash ([string]$Record.ConfigIdentity))) -or
            -not [string]::Equals([string]$Record.OwnershipFingerprint, (Get-LocalServiceOwnershipFingerprint $Record), [StringComparison]::OrdinalIgnoreCase)) { return $false }
        $expectedId = [int]$Record.ProcessId
        $current = Get-LocalServiceProcessSnapshot -ProcessId $expectedId
        if (-not $current -or [int]$current.ProcessId -ne $expectedId) { return $false }
        if ((ConvertTo-LocalServiceCreationUtc $current.CreationDate) -cne (ConvertTo-LocalServiceCreationUtc $Record.CreationTimeUtc)) { return $false }
        if (-not [string]::Equals([IO.Path]::GetFullPath([string]$current.ExecutablePath), [IO.Path]::GetFullPath([string]$Record.ExecutablePath), [StringComparison]::OrdinalIgnoreCase)) { return $false }
        return [string]::Equals([string]$current.CommandLine, [string]$Record.CommandLine, [StringComparison]::Ordinal)
    } catch { return $false }
}

function Stop-LocalServiceIfOwned([object]$Record) {
    if (-not (Test-LocalServiceOwnershipRecord -Record $Record)) {
        return [pscustomobject]@{ Stopped=$false; Reason='current process identity no longer matches the ownership receipt' }
    }
    $process = $null
    try { $process = Get-Process -Id ([int]$Record.ProcessId) -ErrorAction Stop }
    catch { return [pscustomobject]@{ Stopped=$false; Reason='process exited before cleanup' } }
    try {
        if (-not [string]::Equals([IO.Path]::GetFullPath([string]$process.Path), [IO.Path]::GetFullPath([string]$Record.ExecutablePath), [StringComparison]::OrdinalIgnoreCase) -or
            (ConvertTo-LocalServiceCreationUtc $process.StartTime) -cne (ConvertTo-LocalServiceCreationUtc $Record.CreationTimeUtc)) {
            return [pscustomobject]@{ Stopped=$false; Reason='process handle identity no longer matches the ownership receipt' }
        }
        Stop-Process -InputObject $process -Force -ErrorAction Stop
        return [pscustomobject]@{ Stopped=$true; Reason='stopped exact owned process' }
    } catch {
        return [pscustomobject]@{ Stopped=$false; Reason="safe process cleanup failed: $($_.Exception.Message)" }
    }
}

function Stop-LocalServiceOwnershipRecords([object[]]$Records) {
    # Services are added in launch order; reverse order stops the coordinator before its audio server.
    $stoppedCount = 0
    $failedCount = 0
    for ($index = $Records.Count - 1; $index -ge 0; $index--) {
        $record = $Records[$index]
        $result = Stop-LocalServiceIfOwned -Record $record
        if ($result.Stopped) { $stoppedCount++; Write-Verbose "Stopped owned local service PID $($record.ProcessId)." }
        else { $failedCount++; Write-Warning "Left PID $($record.ProcessId) running: $($result.Reason)." }
    }
    return [pscustomobject]@{ StoppedCount=$stoppedCount; FailedCount=$failedCount; ServicesStopped=($failedCount -eq 0) }
}

function Write-LocalServiceLifecycleManifest {
    param(
        [Parameter(Mandatory=$true)] [string]$Path,
        [Parameter(Mandatory=$true)] [object[]]$GameProcesses,
        [Parameter(Mandatory=$true)] [object[]]$OwnedServices
    )
    if ($GameProcesses.Count -eq 0) { throw 'A lifecycle manifest must identify at least one game process.' }
    if ($OwnedServices.Count -eq 0) { throw 'A lifecycle manifest must identify at least one newly owned service.' }
    foreach ($record in @($GameProcesses) + @($OwnedServices)) {
        if ([int]$record.ProcessId -lt 1 -or (-not (Test-LocalOwnershipHash ([string]$record.ConfigIdentity))) -or
            -not [string]::Equals([string]$record.OwnershipFingerprint, (Get-LocalServiceOwnershipFingerprint $record), [StringComparison]::OrdinalIgnoreCase) -or
            [string]::IsNullOrWhiteSpace([string]$record.CommandLine) -or
            [string]::IsNullOrWhiteSpace([string]$record.ExecutablePath) -or
            [string]::IsNullOrWhiteSpace([string]$record.CreationTimeUtc)) {
            throw 'Lifecycle manifest contains an incomplete process identity.'
        }
    }
    $document = [ordered]@{
        schemaVersion = 1
        gameProcesses = @($GameProcesses)
        ownedServices = @($OwnedServices)
    }
    $directory = Split-Path -Parent $Path
    if ($directory) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    $json = ConvertTo-Json -InputObject $document -Compress -Depth 8
    $tempPath = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    [IO.File]::WriteAllText($tempPath, $json, [Text.UTF8Encoding]::new($false))
    try {
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            $backupPath = "$Path.$([guid]::NewGuid().ToString('N')).bak"
            try { [IO.File]::Replace($tempPath, $Path, $backupPath) }
            finally { Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue }
        }
        else { [IO.File]::Move($tempPath, $Path) }
    } finally { Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue }
}

function Read-LocalServiceLifecycleManifest([string]$Path) {
    $document = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    if ($document.schemaVersion -ne 1) { throw 'Unsupported local-service lifecycle manifest schema.' }
    $games = @($document.gameProcesses)
    $services = @($document.ownedServices)
    if ($games.Count -eq 0 -or $services.Count -eq 0) { throw 'Lifecycle manifest is missing game or owned-service identities.' }
    foreach ($record in @($games) + @($services)) {
        $null = ConvertTo-LocalServiceCreationUtc $record.CreationTimeUtc
        [void][IO.Path]::GetFullPath([string]$record.ExecutablePath)
        if ([int]$record.ProcessId -lt 1 -or (-not (Test-LocalOwnershipHash ([string]$record.ConfigIdentity))) -or
            -not [string]::Equals([string]$record.OwnershipFingerprint, (Get-LocalServiceOwnershipFingerprint $record), [StringComparison]::OrdinalIgnoreCase) -or
            [string]::IsNullOrWhiteSpace([string]$record.CommandLine)) {
            throw 'Lifecycle manifest contains an invalid process identity.'
        }
    }
    return [pscustomobject]@{ GameProcesses=$games; OwnedServices=$services }
}

function Invoke-LocalGameOwnedServiceMonitor {
    param(
        [Parameter(Mandatory=$true)] [object[]]$GameProcesses,
        [Parameter(Mandatory=$true)] [object[]]$OwnedServices,
        [switch]$RunOnce,
        [ValidateRange(100, 10000)] [int]$PollMilliseconds = 1000
    )
    do {
        $activeGames = @($GameProcesses | Where-Object { Test-LocalServiceOwnershipRecord -Record $_ })
        if ($activeGames.Count -eq 0) {
            $cleanup = Stop-LocalServiceOwnershipRecords -Records $OwnedServices
            return [pscustomobject]@{ GameProcessesRemaining=0; ServicesStopped=$cleanup.ServicesStopped; ServicesStoppedCount=$cleanup.StoppedCount; ServiceStopFailures=$cleanup.FailedCount }
        }
        if ($RunOnce) { return [pscustomobject]@{ GameProcessesRemaining=$activeGames.Count; ServicesStopped=$false } }
        Start-Sleep -Milliseconds $PollMilliseconds
    } while ($true)
}

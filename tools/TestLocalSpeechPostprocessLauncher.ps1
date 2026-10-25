
. (Join-Path $PSScriptRoot 'TextProtocol.ps1')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$launcher = Join-Path $PSScriptRoot 'StartLocalSpeech.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($launcher, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw "StartLocalSpeech.ps1 parse failed: $($parseErrors[0].Message)" }
$launcherSource = [IO.File]::ReadAllText($launcher)
if ((-not $launcherSource.Contains("Name='python.exe' OR Name='python3.exe' OR Name='pythonw.exe'"))) {
    throw 'Coordinator ownership scan must include windowed Python processes to avoid duplicating an existing bridge-root owner.'
}

foreach ($functionName in @('Test-OptionalPinnedArtifact', 'Get-CommandLineOptionValues', 'Test-CommandLineFlag', 'Get-CommandLineOptionNames', 'Test-CoordinatorLaunchConfiguration', 'Test-CoordinatorReadyReceipt', 'Read-CoordinatorReadyReceipt', 'Get-ReadyConfigIdentity', 'Get-PostprocessCoordinatorArguments', 'Assert-PreparedPresentationHelpers', 'Quote-Arg')) {
    $functionAst = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $functionName }, $false)
    if (-not $functionAst) { throw "Launcher is missing testable helper function: $functionName" }
    Invoke-Expression $functionAst.Extent.Text
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ('speech-postprocess-launcher-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $missing = Test-OptionalPinnedArtifact -Path (Join-Path $temp 'missing.exe') -ExpectedBytes 2 -ExpectedSha256 ('a' * 64) -Description 'test helper'
    if ($missing.Available -or (-not ((([string]$missing.Reason).IndexOf('not installed',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Missing optional helper was not reported as unavailable.' }

    $helperPath = Join-Path $temp 'helper cli.exe'
    [IO.File]::WriteAllBytes($helperPath, [byte[]](1,2,3,4))
    $hash = (Get-FileHash -LiteralPath $helperPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $present = Test-OptionalPinnedArtifact -Path $helperPath -ExpectedBytes 4 -ExpectedSha256 $hash -Description 'test helper'
    if (-not $present.Available -or $present.Path -ne (Resolve-Path -LiteralPath $helperPath).Path) { throw 'Pinned helper failed verification.' }

    $coordinatorSource = Join-Path $temp 'coordinator.py'
    [IO.File]::WriteAllText($coordinatorSource, '# coordinator version one', [Text.UTF8Encoding]::new($false))

    $badHash = Test-OptionalPinnedArtifact -Path $helperPath -ExpectedBytes 4 -ExpectedSha256 ('b' * 64) -Description 'test helper'
    if ($badHash.Available -or (-not ((([string]$badHash.Reason).IndexOf('SHA-256',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Hash-mismatched optional helper was not disabled.' }

    $badLength = Test-OptionalPinnedArtifact -Path $helperPath -ExpectedBytes 5 -ExpectedSha256 $hash -Description 'test helper'
    if ($badLength.Available -or (-not ((([string]$badLength.Reason).IndexOf('length',[StringComparison]::OrdinalIgnoreCase) -ge 0)))) { throw 'Size-mismatched optional helper was not disabled.' }

    $alignCli = Join-Path $temp 'audio cli.exe'
    $alignModel = Join-Path $temp 'align model.gguf'
    $rhubarb = Join-Path $temp 'rhubarb.exe'
    $readyFile = Join-Path $temp 'ready receipt.json'
    $expected = [pscustomobject]@{
        Cli=(Join-Path $temp 'audiocpp_cli.exe'); Model=(Join-Path $temp 'breeze.gguf'); ModelSha256=$hash
        Spec=(Join-Path $temp 'breeze.json'); Backend='cuda'; Root=(Join-Path $temp 'bridge root')
        ServerEndpoint='http://127.0.0.1:8094'; Profiles=(Join-Path $temp 'profiles.json')
        Bindings=(Join-Path $temp 'bindings.json'); VoiceRoot=(Join-Path $temp 'voices')
        AlignerCli=$alignCli; AlignerModel=$alignModel; AlignerModelSha256=$hash; RhubarbCli=$rhubarb
        RhubarbSha256=('e' * 64); PreparePresentation=$true; PerNpcVoices=$true
        ReadyFile=$readyFile; ReadyConfigId=('d' * 64)
    }
    $arguments = @(Get-PostprocessCoordinatorArguments $expected)
    if (($arguments -join ' ') -ne ('--aligner-cli "' + $alignCli + '" --aligner-model "' + $alignModel + '" --aligner-model-sha256 ' + $hash + ' --rhubarb-cli "' + $rhubarb + '" --prepare-presentation-before-playback')) {
        throw 'Launcher did not emit strict prepared-presentation helper arguments.'
    }
    $cmd = 'python.exe -u "C:\tools\coordinator.py" --cli "' + $expected.Cli + '" --model "' + $expected.Model + '" --model-sha256 ' + $hash +
        ' --spec "' + $expected.Spec + '" --backend cuda --root "' + $expected.Root + '" --server-endpoint ' + $expected.ServerEndpoint +
        ' --profiles "' + $expected.Profiles + '" --bindings "' + $expected.Bindings + '" --voice-root "' + $expected.VoiceRoot +
        '" --aligner-cli "' + $alignCli + '" --aligner-model "' + $alignModel + '" --aligner-model-sha256 ' + $hash +
        ' --rhubarb-cli "' + $rhubarb + '" --prepare-presentation-before-playback --per-npc-voices --ready-file "' + $readyFile + '" --ready-config-id ' + $expected.ReadyConfigId + ' --watch'
    if (-not (Test-CoordinatorLaunchConfiguration -CommandLine $cmd -Expected $expected)) { throw 'Matching coordinator arguments were rejected for reuse.' }
    if (Test-CoordinatorLaunchConfiguration -CommandLine ($cmd.Replace(' --prepare-presentation-before-playback','')) -Expected $expected) { throw 'Coordinator without strict prepared-presentation mode was accepted for reuse.' }
    if (Test-CoordinatorLaunchConfiguration -CommandLine ($cmd.Replace(' --per-npc-voices','')) -Expected $expected) { throw 'Coordinator without per-NPC voice mode was accepted for production reuse.' }

    $stale = $cmd.Replace($hash, ('c' * 64))
    if (Test-CoordinatorLaunchConfiguration -CommandLine $stale -Expected $expected) { throw 'Coordinator with stale helper hash was accepted for reuse.' }
    if (Test-CoordinatorLaunchConfiguration -CommandLine 'python.exe -u coordinator.py' -Expected $expected) { throw 'Coordinator without requested arguments was accepted for reuse.' }
    if (Test-CoordinatorLaunchConfiguration -CommandLine ($cmd + ' --aligner-model-sha256 ' + $hash) -Expected $expected) { throw 'Coordinator with duplicate helper arguments was accepted for reuse.' }
    if (Test-CoordinatorLaunchConfiguration -CommandLine ($cmd + ' --await-native-root') -Expected $expected) { throw 'Native-root wait mode was accepted for production reuse.' }
    if (Test-CoordinatorLaunchConfiguration -CommandLine ($cmd.Replace(' --watch','')) -Expected $expected) { throw 'Non-watching coordinator was accepted for production reuse.' }
    if (Test-CoordinatorLaunchConfiguration -CommandLine ($cmd + ' --timeout 1') -Expected $expected) { throw 'Coordinator with unrecognized extra launch options was accepted for reuse.' }

    $disabled = [pscustomobject]@{
        Cli=$expected.Cli; Model=$expected.Model; ModelSha256=$hash; Spec=$expected.Spec; Backend='cuda'; Root=$expected.Root
        ServerEndpoint=$expected.ServerEndpoint; Profiles=$expected.Profiles; Bindings=$expected.Bindings; VoiceRoot=$expected.VoiceRoot
        AlignerCli=$null; AlignerModel=$null; AlignerModelSha256=$null; RhubarbCli=$null; RhubarbSha256=$null
        PreparePresentation=$false; PerNpcVoices=$false; ReadyFile=$readyFile; ReadyConfigId=$expected.ReadyConfigId
    }
    $missingHelperArgumentsRejected = $false
    try { $null = Get-PostprocessCoordinatorArguments $disabled }
    catch { $missingHelperArgumentsRejected = ((([string]$_.Exception.Message).IndexOf('Rhubarb',[StringComparison]::OrdinalIgnoreCase) -ge 0)) }
    if (-not $missingHelperArgumentsRejected) { throw 'Launcher accepted a production configuration without pinned Rhubarb.' }
    $rhubarbOnly = $disabled.PSObject.Copy()
    $rhubarbOnly.RhubarbCli = $rhubarb
    $rhubarbOnly.RhubarbSha256 = ('e' * 64)
    $rhubarbOnly.PreparePresentation = $true
    $rhubarbOnlyArguments = @(Get-PostprocessCoordinatorArguments $rhubarbOnly)
    if (($rhubarbOnlyArguments -join ' ') -ne ('--rhubarb-cli "' + $rhubarb + '" --prepare-presentation-before-playback')) {
        throw 'Launcher did not accept Rhubarb-only playback preparation.'
    }
    Assert-PreparedPresentationHelpers `
        -AlignmentStatus ([pscustomobject]@{ Available=$false; Reason='aligner unavailable' }) `
        -RhubarbStatus ([pscustomobject]@{ Available=$true; Reason=$null })
    $missingHelperStartupRejected = $false
    try {
        Assert-PreparedPresentationHelpers `
            -AlignmentStatus ([pscustomobject]@{ Available=$true; Reason=$null }) `
            -RhubarbStatus ([pscustomobject]@{ Available=$false; Reason='Rhubarb unavailable' })
    } catch { $missingHelperStartupRejected = ($_.Exception.Message.Contains('cannot start') -and $_.Exception.Message.Contains('Rhubarb')) }
    if (-not $missingHelperStartupRejected) { throw 'Missing Rhubarb did not fail production startup preflight.' }
    Assert-PreparedPresentationHelpers `
        -AlignmentStatus ([pscustomobject]@{ Available=$true; Reason=$null }) `
        -RhubarbStatus ([pscustomobject]@{ Available=$true; Reason=$null })
    if (Test-CoordinatorLaunchConfiguration -CommandLine $cmd -Expected $disabled) { throw 'Strict prepared coordinator was accepted for a non-prepared configuration.' }

    $runtimeHelperSource = Join-Path $temp 'align_words.py'
    [IO.File]::WriteAllText($runtimeHelperSource, '# alignment helper version one', [Text.UTF8Encoding]::new($false))
    $identity = Get-ReadyConfigIdentity $expected $coordinatorSource
    if ((-not (Test-HexValue $identity 64 -Lowercase)) -or $identity -cne (Get-ReadyConfigIdentity $expected $coordinatorSource)) { throw 'Readiness config identity is not stable lowercase SHA-256.' }
    $expected.ReadyConfigId = $identity
    $currentCodeCommand = $cmd.Replace('--ready-config-id ' + ('d' * 64), '--ready-config-id ' + $identity)
    if (-not (Test-CoordinatorLaunchConfiguration -CommandLine $currentCodeCommand -Expected $expected)) { throw 'Coordinator with current source identity was rejected for reuse.' }
    $changedIdentity = $expected.PSObject.Copy()
    $changedIdentity.RhubarbCli = Join-Path $temp 'other-rhubarb.exe'
    if ((Get-ReadyConfigIdentity $changedIdentity $coordinatorSource) -ceq $identity) { throw 'Readiness config identity ignored helper-path changes.' }
    $changedVoiceMode = $expected.PSObject.Copy()
    $changedVoiceMode.PerNpcVoices = $false
    if ((Get-ReadyConfigIdentity $changedVoiceMode $coordinatorSource) -ceq $identity) { throw 'Readiness config identity ignored production voice-mode changes.' }
    [IO.File]::WriteAllText($runtimeHelperSource, '# alignment helper version two', [Text.UTF8Encoding]::new($false))
    $changedDependencyExpected = $expected.PSObject.Copy()
    $changedDependencyExpected.ReadyConfigId = Get-ReadyConfigIdentity $changedDependencyExpected $coordinatorSource
    if ($changedDependencyExpected.ReadyConfigId -ceq $identity) { throw 'Readiness config identity ignored imported helper source changes.' }
    if (Test-CoordinatorLaunchConfiguration -CommandLine $currentCodeCommand -Expected $changedDependencyExpected) { throw 'Coordinator loaded with older helper source was accepted for reuse.' }
    [IO.File]::WriteAllText($runtimeHelperSource, '# alignment helper version one', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($coordinatorSource, '# coordinator version two', [Text.UTF8Encoding]::new($false))
    $changedCodeExpected = $expected.PSObject.Copy()
    $changedCodeExpected.ReadyConfigId = Get-ReadyConfigIdentity $changedCodeExpected $coordinatorSource
    if ($changedCodeExpected.ReadyConfigId -ceq $identity) { throw 'Readiness config identity ignored coordinator source changes.' }
    if (Test-CoordinatorLaunchConfiguration -CommandLine $currentCodeCommand -Expected $changedCodeExpected) { throw 'Coordinator launched from older source content was accepted for reuse.' }

    $expected.ReadyConfigId = $identity
    $receiptExpected = $expected.PSObject.Copy()
    $receiptExpected | Add-Member -NotePropertyName ProcessId -NotePropertyValue 1234
    $receipt = [pscustomobject]@{ schemaVersion=1; status='ready'; processId=1234; root=$expected.Root; modelSha256=$hash; serverEndpoint=$expected.ServerEndpoint; configIdentity=$identity }
    if (-not (Test-CoordinatorReadyReceipt -Receipt $receipt -Expected $receiptExpected)) { throw 'Matching coordinator readiness receipt was rejected.' }
    foreach ($mutation in @(
        @{ Name='PID'; Change={ param($value) $value.processId=9999 } },
        @{ Name='root'; Change={ param($value) $value.root=(Join-Path $temp 'wrong-root') } },
        @{ Name='model hash'; Change={ param($value) $value.modelSha256=('e' * 64) } },
        @{ Name='endpoint'; Change={ param($value) $value.serverEndpoint='http://127.0.0.1:8095' } },
        @{ Name='config identity'; Change={ param($value) $value.configIdentity=('f' * 64) } },
        @{ Name='status'; Change={ param($value) $value.status='starting' } },
        @{ Name='schema'; Change={ param($value) $value.schemaVersion=2 } }
    )) {
        $wrongReceipt = [pscustomobject]@{ schemaVersion=1; status='ready'; processId=1234; root=$expected.Root; modelSha256=$hash; serverEndpoint=$expected.ServerEndpoint; configIdentity=$identity }
        & $mutation.Change $wrongReceipt
        if (Test-CoordinatorReadyReceipt -Receipt $wrongReceipt -Expected $receiptExpected) { throw "Coordinator receipt with wrong $($mutation.Name) was accepted." }
    }
    $noConfigExpected = $expected.PSObject.Copy()
    $noConfigExpected.ReadyConfigId = $null
    $noConfigExpected | Add-Member -NotePropertyName ProcessId -NotePropertyValue 1234
    $noConfigReceipt = [pscustomobject]@{ schemaVersion=1; status='ready'; processId=1234; root=$expected.Root; modelSha256=$hash; serverEndpoint=$expected.ServerEndpoint; configIdentity=$null }
    if (-not (Test-CoordinatorReadyReceipt -Receipt $noConfigReceipt -Expected $noConfigExpected)) { throw 'Null config identity was rejected when no config identity is expected.' }
    $extraReceipt = [pscustomobject]@{ schemaVersion=1; status='ready'; processId=1234; root=$expected.Root; modelSha256=$hash; serverEndpoint=$expected.ServerEndpoint; configIdentity=$null; extra='unexpected' }
    if (Test-CoordinatorReadyReceipt -Receipt $extraReceipt -Expected $noConfigExpected) { throw 'Coordinator readiness receipt with unknown schema-1 fields was accepted.' }

    $receiptPath = Join-Path $temp 'coordinator-ready.json'
    [IO.File]::WriteAllText($receiptPath, ($noConfigReceipt | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
    $loadedReceipt = Read-CoordinatorReadyReceipt $receiptPath
    if ($null -eq $loadedReceipt -or -not (Test-CoordinatorReadyReceipt -Receipt $loadedReceipt -Expected $noConfigExpected)) { throw 'Valid coordinator readiness JSON did not load and validate.' }
    [IO.File]::WriteAllText($receiptPath, '{broken', [Text.UTF8Encoding]::new($false))
    if ($null -ne (Read-CoordinatorReadyReceipt $receiptPath)) { throw 'Malformed coordinator readiness JSON was not rejected.' }
} finally {
    Remove-Item -LiteralPath $temp -Recurse -Force
}

Write-Output 'LOCAL_SPEECH_POSTPROCESS_LAUNCHER_SELFTEST_PASS: Rhubarb gates playback, alignment is optional background work, and coordinator reuse binds exact args plus runtime Python hashes.'

function Resolve-LocalDecisionModel {
    param(
        [Parameter(Mandatory = $true)] [string]$RuntimeRoot,
        [ValidateSet('mini', 'nano')] [string]$Profile
    )

    if ([string]::IsNullOrWhiteSpace($RuntimeRoot)) {
        throw [ArgumentException]::new('RuntimeRoot must be a nonempty path.', 'RuntimeRoot')
    }
    $runtimeRootPath = [IO.Path]::GetFullPath($RuntimeRoot)

    if ([string]::IsNullOrEmpty($Profile)) {
        $selectionPath = Join-Path $runtimeRootPath 'active-decision-model.json'
        if (Test-Path -LiteralPath $selectionPath) {
            try {
                $selection = Get-Content -LiteralPath $selectionPath -Raw -ErrorAction Stop |
                    ConvertFrom-Json -ErrorAction Stop
            } catch {
                throw [IO.InvalidDataException]::new("Invalid decision model selection JSON: $selectionPath", $_.Exception)
            }
            if ($selection -isnot [System.Management.Automation.PSCustomObject]) {
                throw [IO.InvalidDataException]::new("Decision model selection must be an object with only a profile field: $selectionPath")
            }
            $propertyNames = @($selection.PSObject.Properties | ForEach-Object { $_.Name })
            if ($propertyNames.Count -ne 1 -or $propertyNames[0] -cne 'profile' -or
                $selection.profile -isnot [string]) {
                throw [IO.InvalidDataException]::new("Decision model selection must contain only a string profile field: $selectionPath")
            }
            if ($selection.profile -cnotin @('mini', 'nano')) {
                throw [IO.InvalidDataException]::new("Unsupported decision model profile '$($selection.profile)' in $selectionPath")
            }
            $Profile = $selection.profile
        } else {
            $Profile = 'mini'
        }
    }

    $registry = @{
        mini = @{
            ModelRelativePath = 'models/d3-mini/vllm-d3-mini-Q4_K_M.gguf'
            ProjectorRelativePath = 'models/d3-mini/mmproj-vllm-d3-mini-Q8_0.gguf'
            ModelHash = 'AB3D4378C268F7F3238A63607AF40C0CB516E4482278B01C2F48E9D0698CE522'
            ProjectorHash = 'BB79BA9F091DF890684388F559F3145649007B5715D03760F99B767DE41D9EDA'
            ModelBytes = 3707523552L
            ProjectorBytes = 366894720L
        }
        nano = @{
            ModelRelativePath = 'models/d3-nano/vllm-d3-nano-Q8_0.gguf'
            ProjectorRelativePath = 'models/d3-nano/mmproj-vllm-d3-nano-Q8_0.gguf'
            ModelHash = '4867D306FD22AEC8C5EBD1851B7F1074F480702655E8000CEEDF0C47C6A6F337'
            ProjectorHash = 'B778993A051D604757DDA94A554BA870BF6E685909C44E7399D71FFB73E0E58F'
            ModelBytes = 2552358464L
            ProjectorBytes = 364664448L
        }
    }
    $entry = $registry[$Profile]
    if ($null -eq $entry) { throw [ArgumentOutOfRangeException]::new('Profile') }

    return [pscustomobject]@{
        Profile = $Profile
        ModelPath = [IO.Path]::GetFullPath((Join-Path $runtimeRootPath $entry.ModelRelativePath))
        ProjectorPath = [IO.Path]::GetFullPath((Join-Path $runtimeRootPath $entry.ProjectorRelativePath))
        ModelHash = $entry.ModelHash
        ProjectorHash = $entry.ProjectorHash
        ModelBytes = [long]$entry.ModelBytes
        ProjectorBytes = [long]$entry.ProjectorBytes
    }
}

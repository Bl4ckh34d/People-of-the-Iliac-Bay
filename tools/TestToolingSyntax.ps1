param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$failures = @()
$scripts = @(Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'tools') -Recurse -Filter '*.ps1')
foreach ($script in $scripts) {
    $tokens = $null
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors)
    foreach ($error in $errors) {
        $failures += "$($script.FullName):$($error.Extent.StartLineNumber): $($error.Message)"
    }
    $forbidden=$ast.FindAll({ param($node)
        ($node -is [System.Management.Automation.Language.BinaryExpressionAst] -and
            $node.Operator.ToString() -in @('Imatch','Inotmatch','Cmatch','Cnotmatch','Ireplace','Creplace','Isplit','Csplit')) -or
        ($node -is [System.Management.Automation.Language.UnaryExpressionAst] -and $node.TokenKind.ToString() -in @('Isplit','Csplit')) -or
        ($node -is [System.Management.Automation.Language.TypeExpressionAst] -and $node.TypeName.FullName -in @('regex','System.Text.RegularExpressions.Regex')) -or
        ($node -is [System.Management.Automation.Language.AttributeAst] -and $node.TypeName.FullName -in @('ValidatePattern','ValidatePatternAttribute'))
    },$true)
    foreach ($node in $forbidden) { $failures += "$($script.FullName):$($node.Extent.StartLineNumber): Regular-expression use is prohibited." }
}
if ($failures.Count) { throw ($failures -join [Environment]::NewLine) }
Write-Output "PASS: $($scripts.Count) PowerShell scripts parse and contain no regular-expression operators or attributes."

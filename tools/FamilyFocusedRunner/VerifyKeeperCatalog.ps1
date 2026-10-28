param(
    [string]$RuntimeAssembly = "$PSScriptRoot/../RuntimeCompile/bin/Debug/net472/RuntimeCompile.dll",
    [string]$HostManaged = "$PSScriptRoot/../../../Player/DaggerfallUnity_Data/Managed"
)
$ErrorActionPreference = 'Stop'
$HostManaged = [IO.Path]::GetFullPath($HostManaged)
$resolver = [System.ResolveEventHandler] {
    param($sender, $eventArgs)
    $name = [Reflection.AssemblyName]::new($eventArgs.Name).Name
    if ($name.EndsWith('.resources')) { return $null }
    $candidate = [IO.Path]::Combine($HostManaged, $name + '.dll')
    if ([IO.File]::Exists($candidate)) { return [Reflection.Assembly]::LoadFrom($candidate) }
    return $null
}
[AppDomain]::CurrentDomain.add_AssemblyResolve($resolver)
try {
    $assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($RuntimeAssembly))
    $method = $assembly.GetType('PeopleOfTheIliacBay.DFU.StreetPopulation').GetMethod(
        'RegisterSurveyedInnCatalog', [Reflection.BindingFlags]'Static,NonPublic')
    $parameters = $method.GetParameters()
    $inventory = [Activator]::CreateInstance($parameters[4].ParameterType)
    $arguments = @($null, 0, [Activator]::CreateInstance($parameters[2].ParameterType),
        [Activator]::CreateInstance($parameters[3].ParameterType), $inventory, [double]0)
    # Execute the actual adapter without a store or native game. A missing source must return before either is accessed.
    $method.Invoke($null, $arguments)
    Write-Output 'PASS missing keeper leaves lodging registration available'
    $inventory.GetType().GetField('KeeperHash').SetValue($inventory, 1)
    $arguments[1] = 1 # The default native location belongs to map 0.
    $rejected = $false
    try { $method.Invoke($null, $arguments) }
    catch {
        if ($_.Exception.InnerException -is [ArgumentException]) { $rejected = $true } else { throw }
    }
    if (!$rejected) { throw 'An actual catalog/location mismatch was accepted.' }
    Write-Output 'PASS actual catalog/location mismatch remains rejected'
}
finally { [AppDomain]::CurrentDomain.remove_AssemblyResolve($resolver) }

# Exact protocol and process-argument helpers. No natural-language interpretation.
function Get-LiteralCount([string]$Text,[string]$Value) {
    if ([string]::IsNullOrEmpty($Value)) { throw 'Empty literal marker.' }
    $count=0; $offset=0
    while ($offset -lt $Text.Length) {
        $found=$Text.IndexOf($Value,$offset,[StringComparison]::Ordinal)
        if ($found -lt 0) { break }
        $count++; $offset=$found+$Value.Length
    }
    return $count
}
function Test-HexValue([string]$Value,[int]$Length,[switch]$Lowercase) {
    if ($Value.Length -ne $Length) { return $false }
    $allowed=if($Lowercase){'0123456789abcdef'}else{'0123456789abcdefABCDEF'}
    foreach($c in $Value.ToCharArray()) { if($allowed.IndexOf($c) -lt 0){return $false} }
    return $true
}
function Test-DecimalValue([string]$Value) {
    if(!$Value.Length){return $false}
    foreach($c in $Value.ToCharArray()){if($c -lt '0' -or $c -gt '9'){return $false}}
    return $true
}
function Test-ProtocolIdentifier([string]$Value,[int]$MaximumLength=128) {
    if(!$Value.Length -or $Value.Length -gt $MaximumLength){return $false}
    $letters='ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'
    if($letters.IndexOf($Value[0]) -lt 0){return $false}
    foreach($c in $Value.ToCharArray()){if(($letters+'._-').IndexOf($c) -lt 0){return $false}}
    return $true
}
function Get-NativeCommandArguments([string]$CommandLine) {
    if([string]::IsNullOrWhiteSpace($CommandLine)){return @()}
    if(-not ('DfuCommandArguments' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DfuCommandArguments {
    [DllImport("shell32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern IntPtr CommandLineToArgvW(string text,out int count);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    public static string[] Parse(string text) {
        int count;var memory=CommandLineToArgvW(text,out count);
        if(memory==IntPtr.Zero)throw new InvalidOperationException("Cannot parse native command line.");
        try {var result=new string[count];for(int i=0;i<count;i++)result[i]=Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory,i*IntPtr.Size));return result;}
        finally {LocalFree(memory);}
    }
}
'@
    }
    return [DfuCommandArguments]::Parse($CommandLine)
}
function Get-NativeOptionValues([string]$CommandLine,[string]$Option) {
    $arguments=@(Get-NativeCommandArguments $CommandLine)
    for($i=0;$i -lt $arguments.Count;$i++){
        if($arguments[$i] -ieq $Option -and $i+1 -lt $arguments.Count){$arguments[$i+1]}
    }
}
function Test-NativeOption([string]$CommandLine,[string]$Option,[string]$Value) {
    $values=@(Get-NativeOptionValues $CommandLine $Option)
    return $values.Count -eq 1 -and $values[0] -ieq $Value
}
function Test-NativeFlag([string]$CommandLine,[string]$Flag) {
    return @(Get-NativeCommandArguments $CommandLine) -icontains $Flag
}

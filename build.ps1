# Builds pa120.exe (console, for testing) and pa120w.exe (windowless, for running at logon)
# using the C# compiler that ships with Windows. No SDK or downloads needed.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /optimize+ /platform:x64 /target:exe     /out:pa120.exe  pa120.cs
if ($LASTEXITCODE) { exit $LASTEXITCODE }
& $csc /nologo /optimize+ /platform:x64 /target:winexe  /out:pa120w.exe pa120.cs
exit $LASTEXITCODE

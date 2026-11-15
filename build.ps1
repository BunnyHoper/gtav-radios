# Compila RadioOnline.dll con el compilador de .NET Framework 4.x (C# 5).
# Uso: .\build.ps1 -Game "E:\Grand Theft Auto V Legacy"
param([string]$Game = 'E:\Grand Theft Auto V Legacy')
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:library /optimize /nowarn:618 "/out:$PSScriptRoot\scripts\RadioOnline.dll" `
  "/r:$Game\ScriptHookVDotNet3.dll" "/r:$Game\scripts\NAudio.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll `
  "$PSScriptRoot\src\RadioOnline.cs"

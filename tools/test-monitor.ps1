param([switch]$Hardware, [switch]$BuildOnly)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$output=Join-Path $root 'bin'
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs=@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll',
    "$framework\WPF\UIAutomationClient.dll","$framework\WPF\UIAutomationTypes.dll","$framework\WPF\WindowsBase.dll",
    (Join-Path $output 'LibreHardwareMonitorLib.dll'))
$exe=Join-Path $output 'MonitorChecks.exe'
$arguments=@('/nologo','/target:exe','/platform:x64','/codepage:65001','/main:MonitorChecks',('/out:'+$exe),
    ('/resource:'+(Join-Path $root 'assets\taskbar-monitor.ico')+',TaskbarMonitor.AppIcon'))
$arguments+=$refs | ForEach-Object { '/reference:'+$_ }
$arguments+=Get-ChildItem -LiteralPath $root -Filter '*.cs' | Select-Object -ExpandProperty FullName
$arguments+=Join-Path $PSScriptRoot 'MonitorChecks.cs'
& (Join-Path $framework 'csc.exe') @arguments
if($LASTEXITCODE -ne 0) { throw 'Check build failed.' }
Copy-Item -LiteralPath (Join-Path $output 'TaskbarMonitor.exe.config') -Destination ($exe+'.config')
if($BuildOnly) { return }
if($Hardware) { & $exe --hardware } else { & $exe }
if($LASTEXITCODE -ne 0) { throw 'Monitor checks failed.' }

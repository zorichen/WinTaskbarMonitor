param([string]$OutputDirectory = 'bin')
$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
$vendor = Join-Path $project 'vendor\lhm-0.9.6'
if (-not (Test-Path (Join-Path $vendor 'LibreHardwareMonitorLib.dll'))) { & (Join-Path $project 'tools\restore-dependencies.ps1') }
$output = Join-Path $project $OutputDirectory
New-Item -ItemType Directory -Path $output -Force | Out-Null
Get-ChildItem -LiteralPath $vendor -Filter '*.dll' | Copy-Item -Destination $output
Copy-Item -LiteralPath (Join-Path $vendor 'LibreHardwareMonitor.exe.config') -Destination (Join-Path $output 'TaskbarMonitor.exe.config')
# Keep the dependency binding redirects, but opt this application into WinForms'
# .NET 4.7+ single-pass and per-monitor ToolStrip scaling.
$configPath = Join-Path $output 'TaskbarMonitor.exe.config'
[xml]$appConfig = Get-Content -LiteralPath $configPath -Raw
$dpiSection = $appConfig.CreateElement('System.Windows.Forms.ApplicationConfigurationSection')
$dpiOption = $appConfig.CreateElement('add')
$dpiOption.SetAttribute('key', 'DpiAwareness')
$dpiOption.SetAttribute('value', 'PerMonitorV2')
[void]$dpiSection.AppendChild($dpiOption)
[void]$appConfig.configuration.AppendChild($dpiSection)
$appConfig.Save($configPath)
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll',"$framework\WPF\UIAutomationClient.dll","$framework\WPF\UIAutomationTypes.dll","$framework\WPF\WindowsBase.dll",(Join-Path $output 'LibreHardwareMonitorLib.dll'))
$arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',('/win32manifest:'+(Join-Path $project 'app.manifest')),('/out:'+(Join-Path $output 'TaskbarMonitor.exe')))
$icon = Join-Path $project 'assets\taskbar-monitor.ico'
$arguments += '/win32icon:' + $icon
$arguments += '/resource:' + $icon + ',TaskbarMonitor.AppIcon'
$arguments += $refs | ForEach-Object { '/reference:'+$_ }
$arguments += Get-ChildItem -LiteralPath $project -Filter '*.cs' | Select-Object -ExpandProperty FullName
& (Join-Path $framework 'csc.exe') @arguments
if($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$startupArguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',
    ('/win32manifest:'+(Join-Path $project 'startup\app.manifest')),('/win32icon:'+$icon),
    ('/out:'+(Join-Path $output 'TaskbarMonitorStartup.exe')), '/reference:System.dll',
    '/reference:System.Core.dll','/reference:System.Web.Extensions.dll','/reference:Microsoft.CSharp.dll',
    (Join-Path $project 'AssemblyInfo.cs'),(Join-Path $project 'AssistantActivation.cs'),(Join-Path $project 'startup\TaskbarMonitorStartup.cs'))
& (Join-Path $framework 'csc.exe') @startupArguments
if($LASTEXITCODE -ne 0) { throw 'Startup launcher build failed.' }
Write-Output (Join-Path $output 'TaskbarMonitor.exe')
Write-Output (Join-Path $output 'TaskbarMonitorStartup.exe')

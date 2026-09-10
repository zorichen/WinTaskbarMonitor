# Run elevated on a Windows 11 test machine. Installs only into a project-owned test directory.
$ErrorActionPreference='Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run as administrator.' }
$root=Split-Path $PSScriptRoot -Parent
$validation=Join-Path $root 'release-validation'
$install=Join-Path $validation 'installed'
# Do not replace the real installation's shared Inno Setup uninstall registration.
$uninstallKey='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{57F571DC-767B-4A3A-BCCE-073197E4B8F5}_is1'
$existingInstall=Get-ItemProperty -LiteralPath $uninstallKey -ErrorAction SilentlyContinue
if ($existingInstall -and $existingInstall.InstallLocation.TrimEnd('\') -ne $install.TrimEnd('\')) {
    throw 'TaskbarMonitor is already installed outside the validation directory. Run this installer test in a disposable Windows VM.'
}
New-Item -ItemType Directory -Path $validation -Force | Out-Null
$resolvedInstall=[IO.Path]::GetFullPath($install)
if (-not $resolvedInstall.StartsWith([IO.Path]::GetFullPath($validation)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Test installation must stay inside validation directory.' }
$devExe=Join-Path $root 'bin\TaskbarMonitor.exe'
$data=Join-Path $env:LOCALAPPDATA 'TaskbarMonitor'
$restoreDev=$false
$report=[ordered]@{}
function Run-AndWait([string]$File,[string[]]$Arguments) {
    $p=Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if(-not $p.WaitForExit(60000)) { throw "Timed out: $File" }
    if($p.ExitCode -ne 0) { throw "Exit code $($p.ExitCode): $File" }
}
function Wait-ForExit([int]$ProcessId) {
    for($i=0;$i -lt 40;$i++) { if(-not (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) { return }; Start-Sleep -Milliseconds 250 }
    throw "Process still running: $ProcessId"
}
try {
    $devProcess=Get-Process -Name TaskbarMonitor -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $devExe } | Select-Object -First 1
    if($devProcess) { $restoreDev=$true; Run-AndWait $devExe @('--quit'); Wait-ForExit $devProcess.Id }
    $setup=Join-Path $root 'dist\TaskbarMonitor-1.0.1-Setup-x64.exe'
    Run-AndWait $setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="'+$install+'"'),'/GROUP="TaskbarMonitor Validation"','/TASKS=""',('/LOG="'+(Join-Path $validation 'install.log')+'"'))
    $exe=Join-Path $install 'TaskbarMonitor.exe'
    if(-not (Test-Path $exe)) { throw 'Installed executable missing.' }
    $report.InstallerPassed=$true
    $report.InstalledVersion=(Get-Item $exe).VersionInfo.FileVersion
    Run-AndWait $exe @('--diagnose')
    $diag=Get-Content (Join-Path $data 'diagnostics.json') -Raw | ConvertFrom-Json
    if($null -eq $diag.Readings.Cpu -or $null -eq $diag.Readings.Memory) { throw 'Load sampling unavailable.' }
    $report.CpuTemperatureAvailable=$null -ne $diag.Readings.CpuTemp
    $report.DiskTemperatureAvailable=$null -ne $diag.Readings.DiskTemp
    $ui=Start-Process -FilePath $exe -ArgumentList '--capture-preview' -WindowStyle Hidden -PassThru
    $status=$null
    for($i=0;$i -lt 30;$i++) {
        Start-Sleep -Milliseconds 500
        if(Test-Path (Join-Path $data 'status.json')) { $status=Get-Content (Join-Path $data 'status.json') -Raw | ConvertFrom-Json; if($status.ProcessId -eq $ui.Id) { break } }
    }
    if($null -eq $status -or $status.ProcessId -ne $ui.Id -or $status.TaskbarHandle -eq 0 -or $status.ActualParent -ne $status.TaskbarHandle) { throw 'Installed taskbar attachment failed.' }
    $report.TaskbarAttachmentPassed=$true
    Copy-Item (Join-Path $data 'widget-preview.png') -Destination (Join-Path $validation 'installed-preview.png')
    Run-AndWait $exe @('--quit'); Wait-ForExit $ui.Id
    Run-AndWait (Join-Path $install 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $validation 'uninstall.log')+'"'))
    for($i=0;$i -lt 20 -and (Test-Path $exe);$i++) { Start-Sleep -Milliseconds 250 }
    if(Test-Path $exe) { throw 'Uninstaller left the application executable.' }
    $report.UninstallerPassed=$true
    $report.DriverPreserved=Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO'
    $report.Timestamp=(Get-Date).ToString('o')
    $report | ConvertTo-Json | Set-Content (Join-Path $validation 'result.json')
} catch {
    $_ | Out-String | Set-Content (Join-Path $validation 'failure.txt')
    throw
} finally {
    if($restoreDev) { Start-Process -FilePath $devExe -WindowStyle Hidden }
}

param([Parameter(Mandatory=$true)][ValidateSet('Prepare','Register','Launch','Remove')][string]$Phase)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$testDir=Join-Path $root 'release-validation\startup'
$launcher=Join-Path $testDir 'TaskbarMonitorStartup.exe'
$registrationFile=Join-Path $testDir 'startup-registration.json'
$marker=Join-Path $testDir 'launched.json'
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
$admin=([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
function Run-Helper([string]$Argument) {
    $p=Start-Process -FilePath $launcher -ArgumentList $Argument -WindowStyle Hidden -PassThru
    if (-not $p.WaitForExit(30000)) { throw 'Startup helper timed out.' }
    if ($p.ExitCode -ne 0) { throw "Startup helper failed with $($p.ExitCode). See LocalAppData\TaskbarMonitor\startup-launcher.log." }
}
if ($Phase -eq 'Prepare') {
    if (Test-Path $registrationFile) { throw 'A previous test registration exists. Remove it before preparing another test.' }
    & (Join-Path $root 'build.ps1') -OutputDirectory 'build-startup-validation'
    & (Join-Path $PSScriptRoot 'test-startup-build.ps1') -BuildDirectory 'build-startup-validation'
    New-Item -ItemType Directory -Path $testDir -Force | Out-Null
    Copy-Item (Join-Path $root 'build-startup-validation\TaskbarMonitorStartup.exe') -Destination $launcher -Force
    # This stand-in never opens hardware or competes with the user's real monitor.
    $fixture=@'
using System;
using System.IO;
using System.Security.Principal;
using System.Web.Script.Serialization;
class Fixture {
    static void Main() {
        using (var identity = WindowsIdentity.GetCurrent()) {
            var value = new { IsAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), Sid = identity.User.Value, Time = DateTime.UtcNow };
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launched.json"), new JavaScriptSerializer().Serialize(value));
        }
    }
}
'@
    $fixturePath=Join-Path $testDir 'Fixture.cs'
    $fixture | Set-Content $fixturePath -Encoding utf8
    & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe /reference:System.Web.Extensions.dll ('/out:'+(Join-Path $testDir 'TaskbarMonitor.exe')) $fixturePath
    if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed.' }
    if (Test-Path $marker) { Remove-Item -LiteralPath $marker }
} elseif ($Phase -eq 'Register') {
    if (-not $admin) { throw 'Register must run elevated.' }
    Run-Helper '--install-startup'
    $registration=Get-Content $registrationFile -Raw | ConvertFrom-Json
    $task=Get-ScheduledTask -TaskName $registration.TaskName
    [xml]$xml=Export-ScheduledTask -TaskName $registration.TaskName
    $ns=New-Object Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace('t',$xml.DocumentElement.NamespaceURI)
    if ($xml.SelectNodes('/t:Task/t:Triggers/*',$ns).Count -ne 0) { throw 'An independent task trigger would bypass Task Manager disabling.' }
    if ($task.Principal.RunLevel -ne 'Highest' -or $task.Principal.LogonType -ne 'Interactive') { throw 'Wrong task security context.' }
    if ($task.Actions.Execute -ne (Join-Path $testDir 'TaskbarMonitor.exe')) { throw 'Task targets another installation.' }
    if (-not (Test-Path -LiteralPath $registration.ShortcutPath)) { throw 'Startup shortcut is missing.' }
    Run-Helper '--install-startup' # Reconfiguration must reuse the same registration.
} elseif ($Phase -eq 'Launch') {
    if ($admin) { throw 'Launch must run as the non-elevated login user.' }
    if (Test-Path $marker) { throw 'Stale launch marker: prepare a new test.' }
    $started=Get-Date
    $p=Start-Process -FilePath $launcher -WindowStyle Hidden -PassThru
    if (-not $p.WaitForExit(30000) -or $p.ExitCode -ne 0) { throw 'Unelevated launcher failed.' }
    for ($i=0;$i -lt 10 -and -not (Test-Path $marker);$i++) { Start-Sleep -Seconds 1 }
    $result=Get-Content $marker -Raw | ConvertFrom-Json
    if (-not $result.IsAdmin -or $result.Sid -ne $identity.User.Value) { throw 'Fixture did not run elevated as the login user.' }
    if (((Get-Date)-$started).TotalSeconds -lt 15) { throw 'Login delay was skipped.' }
} else {
    if (-not $admin) { throw 'Remove must run elevated.' }
    $registration=Get-Content $registrationFile -Raw | ConvertFrom-Json
    Run-Helper '--remove-startup'
    if ((Test-Path -LiteralPath $registration.ShortcutPath) -or (Test-Path $registrationFile) -or (Get-ScheduledTask -TaskName $registration.TaskName -ErrorAction SilentlyContinue)) { throw 'Startup resources were left behind.' }
    Run-Helper '--remove-startup' # Missing resources must also be safe to clean up.
}
"PASS: $Phase" | Set-Content (Join-Path $testDir ($Phase+'.txt'))
"PASS: $Phase"

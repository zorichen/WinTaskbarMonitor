$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'bin\TaskbarMonitor.exe'
Start-Process -FilePath $exe -Verb RunAs -WindowStyle Hidden

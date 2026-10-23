param([string]$InnoCompiler)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'tools\restore-dependencies.ps1') -IncludeDriver
$stage=Join-Path $PSScriptRoot 'build-release'
$dist=Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Path $stage,$dist -Force | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory 'build-release'
Copy-Item (Join-Path $PSScriptRoot 'LICENSE'),(Join-Path $PSScriptRoot 'README.md'),(Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.md') -Destination $stage
Copy-Item (Join-Path $PSScriptRoot 'licenses') -Destination $stage -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'REFERENCES.md'),(Join-Path $PSScriptRoot 'CHANGELOG.md') -Destination $stage
New-Item -ItemType Directory -Path (Join-Path $stage 'docs') -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'docs\2026-10-20-gpu-dpi.md') -Destination (Join-Path $stage 'docs')
Copy-Item (Join-Path $PSScriptRoot 'docs\2026-10-23-heipi-assistant.md') -Destination (Join-Path $stage 'docs')
New-Item -ItemType Directory -Path (Join-Path $stage 'assets') -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'assets\icon-256.png'),(Join-Path $PSScriptRoot 'assets\screenshot.png') -Destination (Join-Path $stage 'assets')
New-Item -ItemType Directory -Path (Join-Path $stage 'drivers') -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'vendor\PawnIO_setup.exe') -Destination (Join-Path $stage 'drivers')
if (-not $InnoCompiler) {
    $candidates=@("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe","${env:ProgramFiles}\Inno Setup 7\ISCC.exe","${env:LOCALAPPDATA}\Programs\Inno Setup 7\ISCC.exe")
    $InnoCompiler=$candidates | Where-Object {Test-Path $_} | Select-Object -First 1
}
if (-not $InnoCompiler -or -not (Test-Path $InnoCompiler)) { throw 'Pass -InnoCompiler with the path to ISCC.exe (Inno Setup 6 or 7).' }
& $InnoCompiler "/DStageDir=$stage" "/DOutputDirPath=$dist" (Join-Path $PSScriptRoot 'installer\TaskbarMonitor.iss')
if($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$portableFiles=Get-ChildItem -LiteralPath $stage | Where-Object Name -ne 'startup-registration.json' | Select-Object -ExpandProperty FullName
$portable=Join-Path $dist 'ZCWinAssistant-1.1.0-Portable-x64.zip'
Compress-Archive -LiteralPath $portableFiles -DestinationPath $portable -Force
$releaseFiles=Get-Item (Join-Path $dist 'ZCWinAssistant-1.1.0-Setup-x64.exe'),$portable
$lines=$releaseFiles | ForEach-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.Name }
$lines | Set-Content -Encoding ascii (Join-Path $dist 'SHA256SUMS.txt')
$releaseFiles + (Get-Item (Join-Path $dist 'SHA256SUMS.txt')) | Select-Object Name,Length

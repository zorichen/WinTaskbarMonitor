$ErrorActionPreference = 'Stop'
$installer = Join-Path $PSScriptRoot 'vendor\PawnIO_setup.exe'
$expectedHash = '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032'
if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $expectedHash) { throw 'PawnIO installer hash mismatch.' }
$signature = Get-AuthenticodeSignature -LiteralPath $installer
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=namazso.eu') { throw 'PawnIO installer signature verification failed.' }
$process = Start-Process -FilePath $installer -ArgumentList '-install','-silent' -Verb RunAs -WindowStyle Hidden -PassThru
$process.WaitForExit()
if ($process.ExitCode -eq 3010) { throw 'PawnIO installed; Windows reports a reboot is required. No restart was performed.' }
if ($process.ExitCode -ne 0) { throw "PawnIO installer exited with code $($process.ExitCode)." }
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO' | Select-Object DisplayName,DisplayVersion,InstallLocation

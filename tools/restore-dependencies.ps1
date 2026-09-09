param([switch]$IncludeDriver)
$ErrorActionPreference='Stop'
[Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
$root=Split-Path $PSScriptRoot -Parent
$vendor=Join-Path $root 'vendor'
New-Item -ItemType Directory -Path $vendor -Force | Out-Null
function Get-VerifiedFile([string]$Url,[string]$Path,[string]$Hash) {
    if (-not (Test-Path -LiteralPath $Path)) { Invoke-WebRequest -UseBasicParsing $Url -OutFile $Path }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Hash) { throw "Checksum mismatch: $Path" }
}
$zip=Join-Path $vendor 'LibreHardwareMonitor-v0.9.6.zip'
Get-VerifiedFile 'https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v0.9.6/LibreHardwareMonitor.zip' $zip '086D9F1B5A99E643EDC2CFAAAC16051685B551E4C5AC0B32A57C58C0E529C001'
Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $vendor 'lhm-0.9.6') -Force
if ($IncludeDriver) {
    $installer=Join-Path $vendor 'PawnIO_setup.exe'
    Get-VerifiedFile 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe' $installer '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032'
    if ((Get-AuthenticodeSignature -LiteralPath $installer).Status -ne 'Valid') { throw 'PawnIO installer signature invalid' }
}

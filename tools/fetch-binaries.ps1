# Downloads the official sing-box and byedpi builds into FsocietyNet\Resources (not in git: 33 MB).
# The sha256 values are the GitHub release digests; the app checks the extracted exe against the hashes
# in Services\Binaries.cs, so a changed version must be updated in both places.
$ErrorActionPreference = 'Stop'
$res = Join-Path $PSScriptRoot '..\FsocietyNet\Resources'
$items = @(
    @{ Url = 'https://github.com/SagerNet/sing-box/releases/download/v1.14.4/sing-box-1.14.4-windows-amd64.zip'
       Sha = '99185d75d314a07dbba4a130c8ad986cbeb629a07927975636b341ca61f991df'; Out = 'sing-box.zip' },
    @{ Url = 'https://github.com/hufrea/byedpi/releases/download/v0.17.3/byedpi-17.3-x86_64-w64.zip'
       Sha = '70d2c94147193cb915f9c6eb5144b8d404dacbcfa90bda2383b6b211afafa456'; Out = 'byedpi.zip' }
)
$ProgressPreference = 'SilentlyContinue'
foreach ($i in $items) {
    $dst = Join-Path $res $i.Out
    if ((Test-Path $dst) -and ((Get-FileHash $dst -Algorithm SHA256).Hash -eq $i.Sha)) { "$($i.Out): ok"; continue }
    Invoke-WebRequest -Uri $i.Url -OutFile "$dst.tmp" -UseBasicParsing
    $h = (Get-FileHash "$dst.tmp" -Algorithm SHA256).Hash
    if ($h -ne $i.Sha) { Remove-Item "$dst.tmp"; throw "$($i.Out): sha256 mismatch ($h)" }
    Move-Item "$dst.tmp" $dst -Force
    "$($i.Out): downloaded"
}

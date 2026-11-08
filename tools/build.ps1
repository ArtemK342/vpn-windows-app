# Builds of the Windows app.
#   .\tools\build.ps1            → out-release\FsocietyNet.exe (one file, needs .NET 8 Desktop Runtime)
#   .\tools\build.ps1 -SelfContained → out-release-sc\FsocietyNet.exe (one file with the runtime, ~110 MB)
#   .\tools\build.ps1 -Preview   → out-preview (no admin prompt) + PNG of every screen in out-preview\render
param([switch]$SelfContained, [switch]$Preview)
$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..'
$proj = Join-Path $root 'FsocietyNet\FsocietyNet.csproj'
& (Join-Path $PSScriptRoot 'fetch-binaries.ps1')

if ($Preview) {
    dotnet build $proj -c Debug -p:Preview=true -o (Join-Path $root 'out-preview')
    $exe = Join-Path $root 'out-preview\FsocietyNet.exe'
    Start-Process $exe -ArgumentList '-render', (Join-Path $root 'out-preview\render') -Wait
    return
}
if ($SelfContained) {
    # The release file: .NET inside, the exe installs itself (Services\Installer.cs). Upload it as FsocietyNet-Setup.exe
    # to the GitHub release and as FsocietyNet-Setup-<version>.exe to /var/www/html/download/files on the site.
    $out = Join-Path $root 'out-release-sc'
    dotnet publish $proj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $out
    Copy-Item (Join-Path $out 'FsocietyNet.exe') (Join-Path $out 'FsocietyNet-Setup.exe') -Force
} else {
    dotnet publish $proj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o (Join-Path $root 'out-release')
}

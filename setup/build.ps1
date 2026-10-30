<#
.SYNOPSIS
    Publishes AcmeDeck as a self-contained single file and builds the installer.

.DESCRIPTION
    1. dotnet publish (win-x64, self-contained, single file) into setup\publish
    2. Optionally downloads win-acme into setup\win-acme so it is bundled
    3. Compiles setup\AcmeDeck.iss with Inno Setup 6

.PARAMETER WinAcmeVersion
    Release tag of win-acme to bundle, e.g. "v2.2.9.1701". When omitted, an
    existing setup\win-acme folder is used, or the setup is built without win-acme.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -WinAcmeVersion v2.2.9.1701
#>
param(
    [string]$WinAcmeVersion
)

$ErrorActionPreference = 'Stop'
$setup = $PSScriptRoot
$root = Split-Path $setup -Parent
$project = Join-Path $root 'src\AcmeDeck\AcmeDeck.csproj'

[xml]$csproj = Get-Content $project
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
Write-Host "AcmeDeck $version" -ForegroundColor Cyan

# 1. Publish
$publish = Join-Path $setup 'publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

# 2. win-acme (optional)
$wacsFolder = Join-Path $setup 'win-acme'
if ($WinAcmeVersion) {
    $numeric = $WinAcmeVersion.TrimStart('v')
    $zipName = "win-acme.v$numeric.x64.pluggable.zip"
    $url = "https://github.com/win-acme/win-acme/releases/download/$WinAcmeVersion/$zipName"
    $zip = Join-Path $env:TEMP $zipName
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
    if (Test-Path $wacsFolder) { Remove-Item $wacsFolder -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $wacsFolder
    Remove-Item $zip
}
if (Test-Path (Join-Path $wacsFolder 'wacs.exe')) {
    $wacsVersion = (Get-Item (Join-Path $wacsFolder 'wacs.exe')).VersionInfo.ProductVersion
    Write-Host "Bundling win-acme $wacsVersion" -ForegroundColor Cyan
} else {
    Write-Host "No setup\win-acme folder: building the setup without win-acme." -ForegroundColor Yellow
}

# 3. Installer
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 (ISCC.exe) not found.' }
& $iscc "/DAppVersion=$version" (Join-Path $setup 'AcmeDeck.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

Get-ChildItem (Join-Path $setup 'output') -Filter "AcmeDeck-Setup-$version.exe" |
    ForEach-Object { Write-Host "Created $($_.FullName) ($([math]::Round($_.Length / 1MB, 1)) MB)" -ForegroundColor Green }

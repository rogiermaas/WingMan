param([string]$SdkRoot = $env:MSFS2024_SDK)
$ErrorActionPreference = 'Stop'
if (-not $SdkRoot) { throw 'Supply -SdkRoot for the MSFS 2024 SDK 1.7.3 installation.' }
$binary = Join-Path $SdkRoot 'SimConnect SDK\lib\SimConnect.dll'
$digest = (Get-FileHash -Algorithm SHA256 -LiteralPath $binary).Hash
if ($digest -ne 'B10DE7ADF4C62E5F66C89DD6D01B64091BBBCEC83411CFE191D6B85FBEE61D15') { throw 'SDK runtime differs from the pinned release; review before publishing.' }
$destination = Join-Path $PSScriptRoot '..\dist\wingman-server\web\runtime\msfs2024-1.7.3'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -LiteralPath $binary,(Join-Path $SdkRoot 'Licenses\MSFS SDK EULA.pdf') -Destination $destination

param([string]$Version = '1.1.6', [switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-wingman.ps1') -Version $Version -SkipPublish:$SkipPublish

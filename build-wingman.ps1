param(
    [string]$Version = '1.1.7',
    [string]$SigningKey = (Join-Path $env:LOCALAPPDATA 'WingManPublisher\update-private.pem'),
    [switch]$SkipPublish
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $goTool = Join-Path $PSScriptRoot 'artifacts\toolchain\go\bin\go.exe'
    if (-not (Test-Path -LiteralPath $goTool)) { $goTool = (Get-Command go -ErrorAction Stop).Source }
    $wixTool = Join-Path $PSScriptRoot 'artifacts\toolchain\wix\wix.exe'
    if (-not (Test-Path -LiteralPath $wixTool)) { throw 'Install WiX 4.0.6 using dotnet tool install wix --version 4.0.6 --tool-path artifacts/toolchain/wix, then add WixToolset.UI.wixext/4.0.6.' }
    if (-not $SkipPublish -and -not (Test-Path -LiteralPath $SigningKey)) { throw 'Publisher signing key is missing. Restore the existing key, or use -SkipPublish to build without publishing an update.' }
    dotnet publish src/EscortPlane2024/EscortPlane2024.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:UseSharedCompilation=false "-p:Version=$Version" -o dist/WingMan --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed' }
    Copy-Item -LiteralPath 'docs\WINGMAN-USAGE.md' -Destination 'dist\WingMan\README.md'
    Copy-Item -LiteralPath 'LICENSE.txt' -Destination 'dist\WingMan\LICENSE.txt'
    & $wixTool build installer/WingMan.wxs -ext WixToolset.UI.wixext/4.0.6 -arch x64 -d "Version=$Version" -d 'PublishDir=dist\WingMan' -o dist/WingMan.msi
    if ($LASTEXITCODE -ne 0) { throw 'MSI build failed' }
    New-Item -ItemType Directory -Force -Path 'dist\wingman-server\web','dist\wingman-server\releases' | Out-Null
    Push-Location relay
    try {
        & $goTool test ./...
        if ($LASTEXITCODE -ne 0) { throw 'Relay tests failed' }
        & $goTool build -trimpath -ldflags '-s -w' -o ../artifacts/wingman-relay.exe .
        if ($LASTEXITCODE -ne 0) { throw 'Publisher build failed' }
        $savedOS = $env:GOOS; $savedArch = $env:GOARCH; $savedCgo = $env:CGO_ENABLED
        try {
            $env:GOOS = 'linux'; $env:GOARCH = 'amd64'; $env:CGO_ENABLED = '0'
            & $goTool build -trimpath -ldflags '-s -w' -o ../dist/wingman-server/wingman-relay .
            if ($LASTEXITCODE -ne 0) { throw 'Linux build failed' }
        } finally { $env:GOOS = $savedOS; $env:GOARCH = $savedArch; $env:CGO_ENABLED = $savedCgo }
    } finally { Pop-Location }
    if (-not $SkipPublish) {
        & './artifacts/wingman-relay.exe' publish -version $Version -source dist/WingMan -out dist/wingman-server/releases -key $SigningKey
        if ($LASTEXITCODE -ne 0) { throw 'Signed publication failed (use a new version if the archive already exists)' }
    }
    Copy-Item -LiteralPath 'dist\WingMan.msi','deploy\index.html' -Destination 'dist\wingman-server\web'
    $webPage = Get-Content -LiteralPath 'dist\wingman-server\web\index.html' -Raw
    $webPage = $webPage -replace '(<strong id="current-version">)[^<]*(</strong>)', ('${1}' + $Version + '${2}')
    [System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'dist\wingman-server\web\index.html'), $webPage, [System.Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath 'assets\branding\wingman-logo-concept.png','assets\branding\WingMan.ico' -Destination 'dist\wingman-server\web'
    Copy-Item -LiteralPath 'LICENSE.txt' -Destination 'dist\wingman-server\web'
    Copy-Item -LiteralPath 'deploy\wingman.service','deploy\apache-wingman.inc.conf','docs\WINGMAN-DEPLOYMENT.md' -Destination 'dist\wingman-server'
    Copy-Item -LiteralPath 'licenses\GORILLA-LICENSE.txt','licenses\GO-LICENSE.txt' -Destination 'dist\wingman-server'
    python tools/package-source.py
    if ($LASTEXITCODE -ne 0) { throw 'Source packaging failed' }
    python tools/package-server.py
    if ($LASTEXITCODE -ne 0) { throw 'Server packaging failed' }
    Get-FileHash -Algorithm SHA256 -LiteralPath 'dist\WingMan.msi','dist\WingMan\WingMan.exe','dist\wingman-server\wingman-relay' | Format-Table -AutoSize
} finally { Pop-Location }

@echo off
setlocal
set GOOS=linux
set GOARCH=amd64
set CGO_ENABLED=0
pushd "%~dp0..\relay"
"..\artifacts\toolchain\go\bin\go.exe" build -trimpath -ldflags "-s -w" -o ..\dist\wingman-server\wingman-relay .
set build_result=%ERRORLEVEL%
popd
exit /b %build_result%

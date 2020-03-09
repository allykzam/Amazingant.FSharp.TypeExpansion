@echo off
cls

.paket\paket.bootstrapper.exe 5.242.2
if errorlevel 1 (
  exit /b %errorlevel%
)

.paket\paket.exe restore
if errorlevel 1 (
  exit /b %errorlevel%
)

dotnet restore

packages\build\FAKE\tools\FAKE.exe build.fsx %*

@echo off
cls

rem .paket\paket.bootstrapper.exe
.paket\paket.exe update
if errorlevel 1 (
  exit /b %errorlevel%
)

.paket\paket.exe restore
if errorlevel 1 (
  exit /b %errorlevel%
)

IF NOT EXIST build.fsx (
  .paket\paket.exe update
  packages\FAKE\tools\FAKE.exe init.fsx
)
"packages\FAKE\tools\FAKE.exe" build.fsx %*
pause

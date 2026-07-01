@echo off
setlocal EnableExtensions

set "SRC=%~dp0nvmessagebus_shim.cpp"
set "OUTDIR=%~1"
if "%OUTDIR%"=="" set "OUTDIR=%~dp0..\..\bin\Release"
if not exist "%OUTDIR%" mkdir "%OUTDIR%"
set "OUT=%OUTDIR%\NvMessageBusShim.dll"

set "GPP="
where g++ >nul 2>&1 && set "GPP=g++"

if not defined GPP if exist "%LOCALAPPDATA%\Microsoft\WinGet\Packages\BrechtSanders.WinLibs.POSIX.UCRT_Microsoft.Winget.Source_8wekyb3d8bbwe\mingw64\bin\g++.exe" (
    set "GPP=DISABLED"
)

if not defined GPP (
    for /f "delims=" %%G in ('where /r "%ProgramFiles%" g++.exe 2^>nul') do (
        if not defined GPP set "GPP=%%G"
    )
)

if defined GPP goto :compile

echo WARNING: g++ not found in PATH; trying prebuilt NvMessageBusShim.dll
if exist "%~dp0prebuilt\NvMessageBusShim.dll" (
    copy /Y "%~dp0prebuilt\NvMessageBusShim.dll" "%OUT%" >nul
    echo Copied prebuilt to %OUT%
    exit /b 0
)
if exist "%OUT%" (
    echo Using existing %OUT%
    exit /b 0
)
echo ERROR: install MinGW g++ or add tools\NvMessageBusShim\prebuilt\NvMessageBusShim.dll
exit /b 1

:compile
"%GPP%" -shared -O2 -s -o "%OUT%" "%SRC%" -static-libgcc -static-libstdc++ -lkernel32
if errorlevel 1 (
    if exist "%~dp0prebuilt\NvMessageBusShim.dll" (
        copy /Y "%~dp0prebuilt\NvMessageBusShim.dll" "%OUT%" >nul
        echo g++ failed; copied prebuilt to %OUT%
        exit /b 0
    )
    exit /b 1
)
copy /Y "%OUT%" "%~dp0prebuilt\NvMessageBusShim.dll" >nul 2>&1
echo Built %OUT%
exit /b 0


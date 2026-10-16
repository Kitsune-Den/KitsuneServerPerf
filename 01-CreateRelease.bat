@echo off
setlocal
pushd "%~dp0"
dotnet build KitsuneServerPerf.sln -c Release || goto :fail
call :zip KitsuneServerPerf || goto :fail
call :zip KitsuneServerPerf_UI || goto :fail
popd
exit /b 0

rem Zips one mod folder as <folder>-v<version>.zip, version read from its ModInfo.xml.
rem Windows' bsdtar writes forward-slash paths. Windows PowerShell's Compress-Archive and
rem ZipFile write backslashes, which Linux unzip turns into files named "Folder\file".
:zip
set VER=
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "([xml](Get-Content '%~1\ModInfo.xml')).xml.Version.value"`) do set VER=%%v
if "%VER%"=="" exit /b 1
if exist "%~1-v%VER%.zip" del "%~1-v%VER%.zip"
"%SystemRoot%\System32\tar.exe" -a -cf "%~1-v%VER%.zip" "%~1" || exit /b 1
echo Release zip: %CD%\%~1-v%VER%.zip
exit /b 0

:fail
popd
exit /b 1

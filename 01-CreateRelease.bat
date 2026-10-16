@echo off
setlocal
pushd "%~dp0"
dotnet build -c Release || goto :fail
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "([xml](Get-Content 'KitsuneServerPerf\ModInfo.xml')).xml.Version.value"`) do set VER=%%v
if "%VER%"=="" goto :fail
rem Windows' bsdtar writes forward-slash paths. Windows PowerShell's Compress-Archive and
rem ZipFile write backslashes, which Linux unzip turns into files named "Folder\file".
if exist "KitsuneServerPerf-v%VER%.zip" del "KitsuneServerPerf-v%VER%.zip"
"%SystemRoot%\System32\tar.exe" -a -cf "KitsuneServerPerf-v%VER%.zip" KitsuneServerPerf || goto :fail
echo Release zip: %CD%\KitsuneServerPerf-v%VER%.zip
popd
exit /b 0
:fail
popd
exit /b 1

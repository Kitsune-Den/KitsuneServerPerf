@echo off
setlocal
pushd "%~dp0"
dotnet build -c Release || goto :fail
for /f "tokens=3 delims=<>= " %%v in ('findstr /c:"Version value" KitsuneServerPerf\ModInfo.xml') do set VER=%%~v
powershell -NoProfile -Command "Compress-Archive -Force -Path 'KitsuneServerPerf' -DestinationPath 'KitsuneServerPerf-v%VER%.zip'" || goto :fail
echo.
echo Release zip: %CD%\KitsuneServerPerf-v%VER%.zip
popd
exit /b 0
:fail
popd
exit /b 1

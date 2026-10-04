@echo off
setlocal
set "ARCANA_POWERSHELL=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"
if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" set "ARCANA_POWERSHELL=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"
"%ARCANA_POWERSHELL%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\auto-compile.ps1"
set "ARCANA_RESULT=%ERRORLEVEL%"
if not "%ARCANA_RESULT%"=="0" echo No se ha sustituido el juego. Revisa el registro indicado.
pause
exit /b %ARCANA_RESULT%

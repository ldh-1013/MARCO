@echo off
rem MARCO local multi-instance QA run.
rem Launches Builds\Windows\MARCO.exe N times (default 3), each writing its OWN log file
rem (Builds\Windows\Player_P0.log, Player_P1.log, ...). Without -logFile every instance writes
rem the same Player.log and the lines overwrite each other.
rem   P0 = host  : press H in the main menu
rem   P1, P2     : press J (join localhost), then R in the lobby on every window
rem Usage: Run3P_QA.bat        -> 3 instances (P0..P2)
rem        Run3P_QA.bat 2      -> 2 instances (e.g. when the editor is the host)
setlocal
set "EXE=%~dp0Builds\Windows\MARCO.exe"
set "LOGDIR=%~dp0Builds\Windows"
set "COUNT=%~1"
if "%COUNT%"=="" set "COUNT=3"
if not exist "%EXE%" (
  echo MARCO.exe not found: "%EXE%"
  echo Build first: Tools/MARCO/Build Windows
  pause
  exit /b 1
)
set /a LAST=%COUNT%-1
for /L %%P in (0,1,%LAST%) do (
  echo Starting P%%P  log: "%LOGDIR%\Player_P%%P.log"
  start "MARCO P%%P" "%EXE%" -logFile "%LOGDIR%\Player_P%%P.log" -screen-fullscreen 0 -screen-width 1280 -screen-height 720
  timeout /t 2 /nobreak >nul
)
endlocal

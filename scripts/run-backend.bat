@echo off
REM AiDesk 백엔드 실행 스크립트 (Windows)
REM 포트 8080에서 ASP.NET Core API 서버 실행

setlocal enabledelayedexpansion

cd /d "%~dp0\.."
set ROOT_DIR=%cd%

echo [backend] checking port 8080...

REM 포트 확인 및 점유 프로세스 종료 시도
for /f "tokens=5" %%a in ('netstat -ano ^| find ":8080" ^| find "LISTENING"') do (
    echo [backend] port 8080 is in use, terminating process %%a
    taskkill /pid %%a /f >nul 2>&1
)

echo [backend] starting AiDeskApi
cd /d %ROOT_DIR%\AiDeskApi
dotnet run

pause

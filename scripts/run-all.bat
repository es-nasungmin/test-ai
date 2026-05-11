@echo off
REM AiDesk 전체 실행 스크립트 (Windows)
REM 백엔드(8080)와 프론트엔드(5173)를 별도 터미널에서 자동 실행

setlocal enabledelayedexpansion

cd /d "%~dp0\.."
set ROOT_DIR=%cd%

echo [all] starting backend...
start "AiDeskApi Backend" cmd /k "cd /d %ROOT_DIR%\AiDeskApi && dotnet run"

REM 백엔드 준비 대기
echo [all] waiting for backend to become ready...
set elapsed=0
:wait_backend
if %elapsed% geq 60 (
    echo [all] backend readiness timeout after 60s
    exit /b 1
)

curl -s http://localhost:8080/api/knowledgebase/platforms >nul 2>&1
if errorlevel 1 (
    timeout /t 1 /nobreak >nul
    set /a elapsed=!elapsed!+1
    goto wait_backend
)

echo [all] backend is ready

echo [all] starting frontend...
start "AiDeskClient Frontend" cmd /k "cd /d %ROOT_DIR%\AiDeskClient && npm run dev -- --host"

echo [all] open: http://localhost:5173
echo [all] backend running on: http://localhost:8080

pause

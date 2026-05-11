@echo off
REM AiDesk 프론트엔드 실행 스크립트 (Windows)
REM 포트 5173에서 Vue 3 + Vite 개발 서버 실행

setlocal enabledelayedexpansion

cd /d "%~dp0\.."
set ROOT_DIR=%cd%

echo [frontend] checking port 5173...

REM 포트 확인 및 점유 프로세스 종료 시도
for /f "tokens=5" %%a in ('netstat -ano ^| find ":5173" ^| find "LISTENING"') do (
    echo [frontend] port 5173 is in use, terminating process %%a
    taskkill /pid %%a /f >nul 2>&1
)

echo [frontend] starting AiDeskClient
cd /d %ROOT_DIR%\AiDeskClient
npm run dev -- --host

pause

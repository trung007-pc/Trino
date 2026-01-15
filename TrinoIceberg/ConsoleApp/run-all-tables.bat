@echo off
REM Script để chạy 15 instances của ConsoleApp đồng thời
REM Mỗi instance insert vào 1 bảng riêng biệt

echo ===================================================================
echo PARALLEL PERFORMANCE TEST - 15 TABLES
echo ===================================================================
echo Starting 15 console app instances...
echo Each instance will insert 100,000 records with batch size 1,000
echo ===================================================================
echo.

REM Lấy đường dẫn đến executable
set APP_PATH=bin\Release\net9.0\ConsoleApp.exe
set RECORDS=100000
set BATCH_SIZE=1000

REM Check if app exists
if not exist "%APP_PATH%" (
    echo ERROR: ConsoleApp.exe not found at %APP_PATH%
    echo Please build the project first using: dotnet build -c Release
    pause
    exit /b 1
)

REM Tạo thư mục logs
if not exist "logs" mkdir logs

REM Lấy timestamp cho session này
set TIMESTAMP=%date:~-4%%date:~3,2%%date:~0,2%_%time:~0,2%%time:~3,2%%time:~6,2%
set TIMESTAMP=%TIMESTAMP: =0%

echo Session ID: %TIMESTAMP%
echo Log directory: logs\%TIMESTAMP%
mkdir logs\%TIMESTAMP%
echo.

REM Start 15 instances
echo Starting instances...
start "Table-tonghopkcb" cmd /c "%APP_PATH% tonghopkcb %RECORDS% %BATCH_SIZE% > logs\%TIMESTAMP%\tonghopkcb.log 2>&1"
start "Table-tonghopkcb1" cmd /c "%APP_PATH% tonghopkcb1 %RECORDS% %BATCH_SIZE% > logs\%TIMESTAMP%\tonghopkcb1.log 2>&1"
start "Table-tonghopkcb2" cmd /c "%APP_PATH% tonghopkcb2 %RECORDS% %BATCH_SIZE% > logs\%TIMESTAMP%\tonghopkcb2.log 2>&1"
start "Table-tonghopkcb3" cmd /c "%APP_PATH% tonghopkcb3 %RECORDS% %BATCH_SIZE% > logs\%TIMESTAMP%\tonghopkcb3.log 2>&1"
start "Table-tonghopkcb4" cmd /c "%APP_PATH% tonghopkcb4 %RECORDS% %BATCH_SIZE% > logs\%TIMESTAMP%\tonghopkcb4.log 2>&1"
start "Table-tonghopkcb5" cmd /c "%APP_PATH% tonghopkcb5 %RECORDS% %BATCH_SIZE% > logs\%TIMESTAMP%\tonghopkcb5.log 2>&1"
start "Table-tonghopkcb6" cmd /c "%APP_PATH% tonghopkcb6 %RECORDS% %BATCH_SIZE% > logs\%TIMESTAMP%\tonghopkcb6.log 2>&1"
start "Table-tonghopkcb7" cmd /c "%APP_PATH% tonghopkcb7 %RECORDS% %BATCH_SIZE% > logs\%TIMESTAMP%\tonghopkcb7.log 2>&1"
start "Table-tonghopkcb8" cmd /c "%APP_PATH% tonghopkcb8 %RECORDS% %BATCH_SIZE% > logs\%TIMESTAMP%\tonghopkcb8.log 2>&1"
start "Table-tonghopkcb9" cmd /c "%APP_PATH% tonghopkcb9 %RECORDS% %BATCH_SIZE% > logs\%TIMESTAMP%\tonghopkcb9.log 2>&1"
start "Table-tonghopkcb10" cmd /c "%APP_PATH% tonghopkcb10 %RECORDS% %BATCH_SIZE% > logs\%TIMESTAMP%\tonghopkcb10.log 2>&1"
start "Table-tonghopkcb11" cmd /c "%APP_PATH% tonghopkcb11 %RECORDS% %BATCH_SIZE% > logs\%TIMESTAMP%\tonghopkcb11.log 2>&1"
start "Table-tonghopkcb12" cmd /c "%APP_PATH% tonghopkcb12 %RECORDS% %BATCH_SIZE% > logs\%TIMESTAMP%\tonghopkcb12.log 2>&1"
start "Table-tonghopkcb13" cmd /c "%APP_PATH% tonghopkcb13 %RECORDS% %BATCH_SIZE% > logs\%TIMESTAMP%\tonghopkcb13.log 2>&1"
start "Table-tonghopkcb14" cmd /c "%APP_PATH% tonghopkcb14 %RECORDS% %BATCH_SIZE% > logs\%TIMESTAMP%\tonghopkcb14.log 2>&1"
start "Table-tonghopkcb15" cmd /c "%APP_PATH% tonghopkcb15 %RECORDS% %BATCH_SIZE% > logs\%TIMESTAMP%\tonghopkcb15.log 2>&1"

echo.
echo ===================================================================
echo All 15 instances have been started!
echo ===================================================================
echo Logs are being written to: logs\%TIMESTAMP%\
echo.
echo You can monitor the logs by opening the log files in the logs folder.
echo Each table has its own log file (tonghopkcb.log, tonghopkcb1.log, etc.)
echo.
echo Press any key to exit this script (processes will continue running)...
pause > nul

@echo off
set IMAGE_NAME=vinagroupteam/trino-api
set VERSION=%1

if "%VERSION%"=="" (
    echo Usage: build.bat [version]
    echo Example: build.bat r3
    exit /b 1
)

dotnet publish -c Release
docker build . -f Dockerfile -t %IMAGE_NAME%:%VERSION%
docker push %IMAGE_NAME%:%VERSION%
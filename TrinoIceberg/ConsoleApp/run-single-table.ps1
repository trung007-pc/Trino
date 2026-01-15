# Script đơn giản để chạy 1 table
# Usage: .\run-single-table.ps1 -TableName tonghopkcb1 -Records 100000 -BatchSize 1000

param(
    [Parameter(Mandatory=$true)]
    [string]$TableName,
    
    [Parameter(Mandatory=$false)]
    [int]$Records = 100000,
    
    [Parameter(Mandatory=$false)]
    [int]$BatchSize = 1000
)

$appPath = "bin\Release\net9.0\ConsoleApp.exe"

# Check if app exists
if (-not (Test-Path $appPath)) {
    Write-Host "ERROR: ConsoleApp.exe not found at $appPath" -ForegroundColor Red
    Write-Host "Please build the project first using: dotnet build -c Release" -ForegroundColor Red
    exit 1
}

Write-Host "Running insert for table: $TableName" -ForegroundColor Cyan
Write-Host "Records: $Records" -ForegroundColor Yellow
Write-Host "Batch Size: $BatchSize" -ForegroundColor Yellow
Write-Host ""

# Run the application
& $appPath $TableName $Records $BatchSize

# PowerShell script để chạy 15 instances của ConsoleApp đồng thời
# Mỗi instance insert vào 1 bảng riêng biệt

Write-Host "===================================================================" -ForegroundColor Cyan
Write-Host "PARALLEL PERFORMANCE TEST - 15 TABLES" -ForegroundColor Cyan
Write-Host "===================================================================" -ForegroundColor Cyan
Write-Host "Starting 15 console app instances..." -ForegroundColor Yellow
Write-Host "Each instance will insert 100,000 records with batch size 1,000" -ForegroundColor Yellow
Write-Host "===================================================================" -ForegroundColor Cyan
Write-Host ""

# Configuration
$appPath = "bin\Release\net9.0\ConsoleApp.exe"
$records = 100000
$batchSize = 1000

# Check if app exists
if (-not (Test-Path $appPath)) {
    Write-Host "ERROR: ConsoleApp.exe not found at $appPath" -ForegroundColor Red
    Write-Host "Please build the project first using: dotnet build -c Release" -ForegroundColor Red
    exit 1
}

# Create logs directory
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$logDir = "logs\$timestamp"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

Write-Host "Session ID: $timestamp" -ForegroundColor Green
Write-Host "Log directory: $logDir" -ForegroundColor Green
Write-Host ""

# Table names
$tables = @(
    "tonghopkcb",
    "tonghopkcb1", "tonghopkcb2", "tonghopkcb3", "tonghopkcb4", "tonghopkcb5",
    "tonghopkcb6", "tonghopkcb7", "tonghopkcb8", "tonghopkcb9", "tonghopkcb10",
    "tonghopkcb11", "tonghopkcb12", "tonghopkcb13", "tonghopkcb14", "tonghopkcb15"
)

# Start all processes
$jobs = @()
$startTime = Get-Date

foreach ($table in $tables) {
    $logFile = "$logDir\$table.log"
    
    Write-Host "Starting: $table -> $logFile" -ForegroundColor Yellow
    
    # Start process và lưu vào jobs array
    $process = Start-Process -FilePath $appPath `
        -ArgumentList $table, $records, $batchSize `
        -RedirectStandardOutput $logFile `
        -RedirectStandardError "$logDir\$table.error.log" `
        -PassThru `
        -NoNewWindow
    
    $jobs += @{
        Table = $table
        Process = $process
        LogFile = $logFile
    }
    
    Start-Sleep -Milliseconds 100
}

Write-Host ""
Write-Host "===================================================================" -ForegroundColor Cyan
Write-Host "All 15 instances have been started!" -ForegroundColor Green
Write-Host "===================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Monitoring progress..." -ForegroundColor Yellow
Write-Host ""

# Monitor processes
$completedCount = 0
while ($completedCount -lt $jobs.Count) {
    $completedCount = 0
    
    foreach ($job in $jobs) {
        if ($job.Process.HasExited) {
            $completedCount++
        }
    }
    
    $runningCount = $jobs.Count - $completedCount
    Write-Host "`r[$(Get-Date -Format 'HH:mm:ss')] Running: $runningCount | Completed: $completedCount / $($jobs.Count)" -NoNewline -ForegroundColor Cyan
    
    Start-Sleep -Seconds 2
}

$endTime = Get-Date
$duration = $endTime - $startTime

Write-Host ""
Write-Host ""
Write-Host "===================================================================" -ForegroundColor Cyan
Write-Host "ALL PROCESSES COMPLETED!" -ForegroundColor Green
Write-Host "===================================================================" -ForegroundColor Cyan
Write-Host "Total Duration: $($duration.ToString('hh\:mm\:ss'))" -ForegroundColor Green
Write-Host ""

# Check results
Write-Host "Results:" -ForegroundColor Yellow
Write-Host ""

foreach ($job in $jobs) {
    $exitCode = $job.Process.ExitCode
    $status = if ($exitCode -eq 0) { "SUCCESS" } else { "FAILED" }
    $color = if ($exitCode -eq 0) { "Green" } else { "Red" }
    
    Write-Host "  $($job.Table.PadRight(15)) : $status (Exit Code: $exitCode)" -ForegroundColor $color
}

Write-Host ""
Write-Host "Logs directory: $logDir" -ForegroundColor Cyan
Write-Host ""
Write-Host "Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

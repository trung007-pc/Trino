# ConsoleApp - Performance Test Tool

Console application để test hiệu suất insert data vào Trino/Iceberg database. Hỗ trợ chạy đồng thời nhiều instances để test insert parallel vào nhiều bảng khác nhau.

## Features

- ✅ Insert data vào các bảng: `tonghopkcb`, `tonghopkcb1` - `tonghopkcb15`
- ✅ Configurable: số records, batch size
- ✅ Chi tiết log cho từng batch
- ✅ Metrics: throughput, avg/min/max batch time
- ✅ Hỗ trợ chạy 15 instances đồng thời

## Build

```bash
dotnet build -c Release
```

## Configuration

Chỉnh sửa `appsettings.json`:

```json
{
  "Trino": {
    "Host": "192.168.100.17",
    "Port": 8000,
    "Catalog": "iceberg",
    "Schema": "v1",
    "User": "admin",
    "EnableSsl": false
  }
}
```

## Usage

### 1. Chạy single table

```bash
# Windows CMD
bin\Release\net9.0\ConsoleApp.exe tonghopkcb1 100000 1000

# PowerShell
.\bin\Release\net9.0\ConsoleApp.exe tonghopkcb1 100000 1000

# Hoặc dùng script PowerShell
.\run-single-table.ps1 -TableName tonghopkcb1 -Records 100000 -BatchSize 1000
```

**Parameters:**
- `tableName`: Tên bảng (tonghopkcb, tonghopkcb1-tonghopkcb15)
- `recordsCount`: Tổng số records (default: 100,000)
- `batchSize`: Batch size (default: 1,000)

### 2. Chạy 15 tables đồng thời

#### Option A: Batch Script (.bat)

```bash
run-all-tables.bat
```

#### Option B: PowerShell Script (.ps1) - RECOMMENDED

```powershell
.\run-all-tables.ps1
```

**PowerShell script có thêm:**
- ✅ Monitor progress real-time
- ✅ Hiển thị kết quả từng table
- ✅ Tính tổng thời gian
- ✅ Kiểm tra exit code

### 3. Xem logs

Logs được lưu vào thư mục `logs\<timestamp>\`:

```
logs/
  20260115_143022/
    tonghopkcb.log
    tonghopkcb1.log
    tonghopkcb2.log
    ...
    tonghopkcb15.log
    tonghopkcb1.error.log (nếu có lỗi)
```

## Output Example

```
═══════════════════════════════════════════════════════════
PERFORMANCE TEST - SINGLE TABLE INSERT
═══════════════════════════════════════════════════════════
Table Name: tonghopkcb1
Records Count: 100,000
Batch Size: 1,000
Total Batches: 100
Trino: 192.168.100.17:8000 / iceberg.v1
═══════════════════════════════════════════════════════════
[tonghopkcb1] Starting insert of 100,000 records...
[tonghopkcb1] Generating 100,000 fake records...
[tonghopkcb1] Data generation completed
[tonghopkcb1] [ 10/100] Progress: 10.0% | Last 10 avg: 250ms | Overall avg: 255ms
[tonghopkcb1] [ 20/100] Progress: 20.0% | Last 10 avg: 248ms | Overall avg: 252ms
...
[tonghopkcb1] [100/100] Progress: 100.0% | Last 10 avg: 245ms | Overall avg: 250ms
═══════════════════════════════════════════════════════════
COMPLETED SUCCESSFULLY
═══════════════════════════════════════════════════════════
Total Time: 25.50s
Total Records: 100,000
Throughput: 3,922 records/sec
Avg Batch Time: 250.25ms
Min Batch Time: 230ms
Max Batch Time: 280ms
═══════════════════════════════════════════════════════════
```

## Monitoring Multiple Instances

Khi chạy 15 instances đồng thời:

### Real-time monitoring (PowerShell)

```powershell
# PowerShell script sẽ tự động monitor
.\run-all-tables.ps1
```

### Manual monitoring

```powershell
# Xem tất cả log files
Get-ChildItem logs\<timestamp>\*.log | ForEach-Object { 
    Write-Host $_.Name -ForegroundColor Yellow
    Get-Content $_ -Tail 5
    Write-Host ""
}

# Theo dõi 1 log file cụ thể
Get-Content logs\<timestamp>\tonghopkcb1.log -Wait

# Grep progress từ tất cả logs
Select-String "Progress:" logs\<timestamp>\*.log
```

## Performance Tips

1. **Batch Size**: 
   - Nhỏ (100-500): Nhiều batch, overhead cao nhưng memory thấp
   - Trung bình (1000-2000): Balanced
   - Lớn (5000-10000): Ít batch, throughput cao nhưng memory cao

2. **Parallel Instances**:
   - Test với 1-2 tables trước
   - Monitor database load
   - Tăng dần số lượng instances

3. **Database Tuning**:
   - Kiểm tra Trino worker resources
   - Monitor network bandwidth
   - Xem Iceberg commit overhead

## Troubleshooting

### Build lỗi

```bash
# Clean và rebuild
dotnet clean
dotnet restore
dotnet build -c Release
```

### Connection timeout

- Kiểm tra `appsettings.json`
- Ping Trino server: `ping 192.168.100.17`
- Test connection: `curl http://192.168.100.17:8000/v1/info`

### Memory issues

- Giảm batch size
- Giảm số lượng parallel instances
- Tăng heap size cho .NET: `set DOTNET_GCHeapCount=4`

## Architecture

```
ConsoleApp/
├── Program.cs              # Entry point, parse args, setup logging
├── Run.cs                  # TableInserter class, insert logic
├── appsettings.json        # Configuration
├── run-all-tables.bat      # Batch script cho Windows
├── run-all-tables.ps1      # PowerShell script (recommended)
└── run-single-table.ps1    # PowerShell script cho single table
```

## Dependencies

- `Entityframework.Trino.Iceberg`: DbContext và models
- `Microsoft.Extensions.Configuration`: Read appsettings.json
- `Microsoft.Extensions.Logging`: Logging

## Notes

- Mỗi instance tạo data với seed khác nhau dựa trên table name
- Test run ID format: `PERF_yyyyMMdd_HHmmss`
- MA_LK format: `{testRunId}_{tableName}_{index:D8}`

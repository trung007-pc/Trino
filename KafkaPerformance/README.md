# Kafka Performance Testing Solution

Solution gồm 2 console applications để test performance Kafka với PostgreSQL.

## Cấu Trúc

```
KafkaPerformance/
├── KafkaPerformance.Shared/          # Shared library
│   ├── Entities/ApiRequest.cs        # Entity model
│   └── Data/AppDbContext.cs          # EF Core DbContext
├── KafkaPerformance.Publisher/       # Publisher app
│   ├── Program.cs
│   └── appsettings.json
└── KafkaPerformance.Consumer/        # Consumer app
    ├── Program.cs
    └── appsettings.json
```

## Entity: ApiRequest

```csharp
- Id: Guid (Primary Key)
- MaCSKCB: string (255 chars)
- Status: string (50 chars) - "Pending" | "Sent" | "Processed"
- CreatedAt: DateTime (UTC)
```

## Database Setup

### 1. Tạo Database và Table

```sql
CREATE DATABASE kafka_performance;

\c kafka_performance

CREATE TABLE api_requests (
    id UUID PRIMARY KEY,
    ma_cskcb VARCHAR(255) NOT NULL,
    status VARCHAR(50) NOT NULL,
    created_at TIMESTAMP NOT NULL
);

-- Index cho performance
CREATE INDEX idx_api_requests_status ON api_requests(status);
CREATE INDEX idx_api_requests_created_at ON api_requests(created_at);
```

### 2. Insert Test Data

```sql
-- Insert 1000 pending requests
INSERT INTO api_requests (id, ma_cskcb, status, created_at)
SELECT 
    gen_random_uuid(),
    'CSKCB_' || LPAD(generate_series::text, 5, '0'),
    'Pending',
    NOW()
FROM generate_series(1, 1000);
```

## Cấu Hình

### appsettings.json (Publisher và Consumer)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "User ID=postgres;Password=12345678;Host=192.168.100.16;Port=5432;Database=kafka_performance;"
  },
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "Topic": "api-requests"
  }
}
```

### Kafka Configuration

- **Publisher**:
  - Batch Size: 100 records
  - Poll Interval: 5000ms (5 giây)
  - Compression: Snappy

- **Consumer**:
  - Group ID: api-request-consumer-group
  - Auto Offset Reset: Earliest
  - Manual Commit: true

## Cách Chạy

### 1. Start Kafka (Docker)

```bash
cd "E:\Apache Iceberg"
docker-compose -f docker-compose-kafa.yml up -d
```

### 2. Build Solution

```bash
cd KafkaPerformance
dotnet build
```

### 3. Chạy Publisher

```bash
cd KafkaPerformance.Publisher
dotnet run
```

**Publisher sẽ:**
- Đọc records có status = "Pending" từ database
- Gửi message lên Kafka topic "api-requests"
- Update status thành "Sent"
- Lặp lại mỗi 5 giây

### 4. Chạy Consumer (Terminal khác)

```bash
cd KafkaPerformance.Consumer
dotnet run
```

**Consumer sẽ:**
- Lắng nghe messages từ Kafka topic
- Update status thành "Processed" trong database
- Commit offset sau khi xử lý thành công

## Flow

```
Database (Pending)
       ↓
   Publisher → Kafka Topic → Consumer
       ↓                          ↓
Database (Sent)          Database (Processed)
```

## Test Performance

### Tạo nhiều records để test

```sql
-- Insert 100,000 pending requests
INSERT INTO api_requests (id, ma_cskcb, status, created_at)
SELECT 
    gen_random_uuid(),
    'CSKCB_' || LPAD(generate_series::text, 6, '0'),
    'Pending',
    NOW()
FROM generate_series(1, 100000);
```

### Kiểm tra status

```sql
-- Đếm theo status
SELECT status, COUNT(*) 
FROM api_requests 
GROUP BY status;

-- Xem records mới nhất
SELECT * FROM api_requests 
ORDER BY created_at DESC 
LIMIT 10;
```

## Monitoring

### Publisher Output

```
[10:30:15] Found 100 pending requests
  ✓ Sent: abc123... | MaCSKCB: CSKCB_00001 | Partition: 0
  ✓ Sent: def456... | MaCSKCB: CSKCB_00002 | Partition: 1
[10:30:16] Batch completed: 100 sent | Total: 100
```

### Consumer Output

```
[10:30:16] Received message:
  Partition: 0
  Offset: 1234
  Key: abc123...
  ID: abc123...
  MaCSKCB: CSKCB_00001
  ✓ Status updated to 'Processed' | Total: 1
```

## Dependencies

- .NET 8.0
- Confluent.Kafka 2.3.0
- Entity Framework Core 8.0.0
- Npgsql.EntityFrameworkCore.PostgreSQL 8.0.0
- System.Text.Json 8.0.0

## Troubleshooting

### Kafka không connect được

```bash
# Kiểm tra Kafka đang chạy
docker ps | grep kafka

# Kiểm tra logs
docker logs kafka
```

### Database connection error

```bash
# Test connection
psql -h 192.168.100.16 -U postgres -d kafka_performance
```

### Messages không được consume

```bash
# Check consumer group
docker exec -it kafka kafka-consumer-groups --bootstrap-server localhost:9092 --describe --group api-request-consumer-group
```

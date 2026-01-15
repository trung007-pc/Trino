# 🚀 Streaming Architecture - Giải Thích Đơn Giản

## 🎯 Mục Tiêu
**Nhận dữ liệu từ Kafka và ghi liên tục vào S3 với custom logic**

---

## 🏗️ Kiến Trúc (6 Thành Phần)

```
Kafka → Flink → Iceberg → S3
          ↓
        Trino (đọc)
          ↓
      PostgreSQL (metadata)
```

---

## 📦 Từng Thành Phần Làm Gì?

### 1. **Kafka (Port 9092)** - Hàng Đợi
- **Vai trò:** Nhận và giữ messages (events) từ nhiều nguồn
- **Tại sao cần:** Tách biệt người gửi và người nhận, tránh mất data
- **Ví dụ:** Website gửi 1000 orders/giây → Kafka lưu tạm → Flink xử lý từ từ

```
Producer → Topic "orders" → Flink consumer
```

### 2. **Flink (Port 8082)** - Bộ Xử Lý
- **Vai trò:** Đọc từ Kafka, xử lý logic, ghi vào S3
- **Đây là nơi BẠN VIẾT CODE:** Filter, transform, validate, enrich data
- **Chạy 24/7** liên tục

```java
// Ví dụ đơn giản
Kafka → Flink:
  .filter(order -> order.amount > 0)        // Lọc
  .map(order -> addCustomerInfo(order))     // Thêm thông tin
  .sinkTo(Iceberg)                          // Ghi vào S3
```

### 3. **Iceberg** - Quản Lý Bảng
- **Vai trò:** Tổ chức data thành bảng (như database table)
- **Không phải storage**, chỉ là cách sắp xếp files trên S3
- **Lợi ích:** 
  - ACID transactions (không bị corrupt data)
  - Time travel (xem data ở quá khứ)
  - Schema evolution (thêm/xóa cột dễ dàng)

```
S3 Bucket:
└── orders/
    ├── metadata/     ← Iceberg quản lý cấu trúc bảng
    └── data/         ← Files Parquet thực tế
        └── date=2024-01-13/
            └── part-001.parquet
```

### 4. **S3/MinIO (Port 9000)** - Kho Lưu Trữ
- **Vai trò:** Lưu files Parquet (data cuối cùng)
- **Tại sao:** Rẻ, không giới hạn dung lượng, bền vững
- **Flink ghi vào đây** qua Iceberg

### 5. **Trino (Port 8081)** - Công Cụ Query
- **Vai trò:** Cho phép query data bằng SQL (chỉ đọc)
- **Không can thiệp** vào việc ghi của Flink
- **Dùng cho:** Báo cáo, analytics, BI tools

```sql
SELECT * FROM orders WHERE date = '2024-01-13';
```

### 6. **PostgreSQL (Port 5432)** - Sổ Sách
- **Vai trò:** Lưu metadata (bảng nào, ở đâu, schema gì)
- **Cả Flink và Trino đều hỏi:** "Bảng orders ở đâu?" → PostgreSQL trả lời: "Ở S3 path này"

---

## 🌊 Luồng Dữ Liệu (1 Event)

```
1. Website → Kafka
   Event: {order_id: 123, amount: 99}

2. Kafka → Flink (consume)
   Flink nhận message

3. Flink xử lý (CUSTOM LOGIC Ở ĐÂY)
   - Validate: amount > 0 ✅
   - Enrich: Thêm customer_tier = "gold"
   - Transform: Format data

4. Flink → Iceberg → S3
   Ghi vào: s3://bucket/orders/data/date=2024-01-13/part-001.parquet

5. Iceberg update metadata
   PostgreSQL: "Bảng orders có thêm file mới"

6. User query qua Trino
   SELECT * FROM orders → Trino đọc từ S3 → Return results
```

**Thời gian:** Từ Kafka → S3 < 1 phút (real-time)

---

## 🎯 Nơi Viết Custom Logic

### ✅ **Trong Flink Job** (Đây là chỗ quan trọng nhất)

```java
// Flink Job (Java/Scala)
public class OrderStreamingJob {
    public static void main(String[] args) {
        
        // 1. Đọc từ Kafka
        DataStream<Order> orders = env
            .fromSource(kafkaSource, "orders");
        
        // 2. VIẾT LOGIC CỦA BẠN Ở ĐÂY
        orders
            .filter(order -> order.getAmount() > 0)           // Rule 1: Lọc invalid
            .map(order -> {
                // Rule 2: Thêm thông tin customer
                String tier = lookupCustomerTier(order.getCustomerId());
                order.setTier(tier);
                return order;
            })
            .keyBy(Order::getRegion)                          // Group theo region
            .window(TumblingProcessingTimeWindows.of(Minutes(5)))
            .aggregate(new MyAggregator())                     // Rule 3: Tổng hợp
            
            // 3. Ghi vào Iceberg (tự động vào S3)
            .sinkTo(icebergSink);
    }
}
```

### ✅ **Partition Strategy** (Quyết định cách lưu vào S3)

```sql
-- Khi tạo bảng Iceberg
CREATE TABLE orders (
    order_id BIGINT,
    amount DECIMAL,
    region VARCHAR,
    order_date DATE
)
PARTITIONED BY (
    days(order_date),    -- Chia theo ngày
    region               -- Chia theo vùng
);
```

**Kết quả trên S3:**
```
s3://bucket/orders/data/
├── date=2024-01-13/
│   ├── region=US/
│   │   └── part-001.parquet
│   └── region=EU/
│       └── part-002.parquet
└── date=2024-01-14/
    └── ...
```

---

## 🔒 Đảm Bảo Không Mất/Trùng Data

### **Exactly-Once Guarantee**

```
Flink checkpoint cơ chế:
1. Flink ghi data vào S3
2. Flink commit offset vào Kafka
3. Cả 2 bước thành công → OK
4. Nếu 1 trong 2 fail → Rollback hết → Retry

→ Mỗi message chỉ được ghi đúng 1 lần
```

---

## 💰 Tại Sao Dùng Kiến Trúc Này?

| Yêu cầu | Giải pháp |
|---------|-----------|
| Ghi data real-time từ Kafka | ✅ Flink consume 24/7 |
| Custom logic khi ghi | ✅ Viết trong Flink job |
| Lưu vào S3 | ✅ Iceberg + S3 |
| Không mất data | ✅ Exactly-once |
| Query dễ dàng | ✅ Trino SQL |
| Scale được | ✅ Mỗi layer scale độc lập |
| Chi phí thấp | ✅ S3 rẻ, chỉ trả khi dùng |

---

## 🚀 Các Bước Tiếp Theo

### 1. **Chạy infrastructure**
```bash
docker-compose -f docker-compose-streaming.yml up -d
```

### 2. **Tạo Kafka topic**
```bash
docker exec kafka kafka-topics.sh --create --topic orders --bootstrap-server localhost:9092
```

### 3. **Viết Flink Job** (Java/Scala)
- Tạo project Maven/Gradle
- Thêm dependencies: Flink, Kafka connector, Iceberg connector
- Viết custom logic
- Build JAR file

### 4. **Deploy Flink Job**
```bash
docker exec flink-jobmanager flink run /path/to/job.jar
```

### 5. **Test**
- Gửi message vào Kafka
- Check Flink UI: http://localhost:8082
- Query Trino: `SELECT * FROM orders`

---

## 📝 Tóm Tắt 3 Câu

1. **Kafka nhận data** từ nhiều nguồn (website, mobile, IoT)
2. **Flink xử lý real-time** (filter, transform, validate) và **ghi vào S3** qua Iceberg
3. **Trino query** data từ S3 cho analytics

**Custom logic → Viết trong Flink job**  
**Data cuối cùng → Lưu trong S3 (qua Iceberg)**

---

## ❓ So Sánh Với Kiến Trúc Cũ (Trino Only)

| | Cũ (Trino only) | Mới (Kafka+Flink+Trino) |
|---|-----------------|-------------------------|
| **Dùng cho** | Query/Read | Real-time write + Query |
| **Data source** | Có sẵn trong S3 | Stream từ Kafka |
| **Latency** | N/A (không ghi) | < 1 phút |
| **Custom logic** | SQL only | Java/Scala (mạnh hơn) |
| **Use case** | Analytics | Event processing + Analytics |

---

**Câu hỏi?** Hỏi về phần nào muốn giải thích thêm!

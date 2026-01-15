# So sánh PostgreSQL vs Trino + Iceberg + S3

> **Bản chất:** PostgreSQL và Trino + Iceberg + S3 đều là database (cơ sở dữ liệu), nhưng có sự **khác biệt căn bản về kiến trúc lưu trữ**:
> - **PostgreSQL:** Lưu trữ tập trung (coupled storage & compute)
> - **Trino + Iceberg + S3:** Lưu trữ phân tán (decoupled storage & compute)


## 1. PostgreSQL: Hệ quản trị CSDL quan hệ nguyên khối (Monolithic)

PostgreSQL là một hệ quản trị cơ sở dữ liệu (RDBMS) truyền thống, nơi mọi thành phần được đóng gói trong một phần mềm duy nhất.

### Bản chất
Là một **thực thể hợp nhất**. Khi bạn cài đặt PostgreSQL, nó tự quản lý từ việc lưu trữ file trên ổ cứng, quản lý bộ nhớ RAM đến việc thực thi các câu lệnh SQL.

### Kiến trúc
**Compute** (Tính toán) và **Storage** (Lưu trữ) gắn liền với nhau. Nếu bạn muốn thêm ổ cứng, thường bạn phải đặt chúng trong cùng một máy chủ chứa CPU xử lý.

### Giới hạn lưu trữ
- **Kích thước bảng tối đa:** 10 TB
---

## 2. Trino + Iceberg + S3: Hệ CSDL phân tán (Decoupled Database)

Đây không phải là một phần mềm duy nhất mà là sự kết hợp của 3 lớp công nghệ để tạo thành một "Database khổng lồ".

### Bản chất
Là một **kiến trúc tách rời** giữa tính toán và lưu trữ:

- **S3 (Storage):** Đóng vai trò là "ổ cứng" (Lưu trữ vật lý)
- **Iceberg (Table Format):** Đóng vai trò là "bộ quản lý bảng" (Định nghĩa cấu trúc, hàng, cột và đảm bảo tính toàn vẹn dữ liệu)
- **Trino (Query Engine):** Đóng vai trò là "bộ não" (Nhận lệnh SQL và xử lý dữ liệu)

### Kiến trúc
Bạn có thể có **100 máy chủ Trino** để xử lý nhưng chỉ cần **một kho lưu trữ S3 duy nhất**.

```
┌─────────────────────────────────────────────────────────┐
│                    👤 User / Application                │
│                   "SELECT * FROM customers"             │
└────────────────────────┬────────────────────────────────┘
                         │ SQL Query
                         ▼
┌────────────────────────────────────────────────────────┐
│         🧠 TRINO (Query Engine - Bộ não)               │
│  ┌─────────┐  ┌─────────┐  ┌─────────┐  ┌─────────┐  │
│  │Worker 1 │  │Worker 2 │  │Worker 3 │  │Worker N │  │
│  └─────────┘  └─────────┘  └─────────┘  └─────────┘  │
│    "Xử lý SQL, phân tích dữ liệu, tính toán"          │
└────────────────────────┬───────────────────────────────┘
                         │ Đọc Metadata
                         ▼
┌────────────────────────────────────────────────────────┐
│      📋 ICEBERG (Table Format - Sổ quản lý)            │
│  ┌──────────────────────────────────────────────────┐ │
│  │ Metadata (cũng lưu trên S3):                     │ │
│  │ - Schema (cột: id, name, email...)               │ │
│  │ - Partitions (chia theo ngày, tháng)             │ │
│  │ - Snapshots (phiên bản dữ liệu)                  │ │
│  │ - File locations (file1.parquet, file2.parquet) │ │
│  └──────────────────────────────────────────────────┘ │
│    "Iceberg = Quyển sổ chỉ mục (không lưu data)"     │
└────────────────────────┬───────────────────────────────┘
                         │ Đọc Data Files
                         ▼
┌────────────────────────────────────────────────────────┐
│          🗄️ S3 (Storage - Kho chứa vật lý)            │
│  ┌────────────────┐  ┌────────────────┐              │
│  │ METADATA/      │  │ DATA/          │              │
│  │ - metadata.json│  │ - file1.parquet│              │
│  │ - snap-1.avro  │  │ - file2.parquet│              │
│  └────────────────┘  │ - fileN.parquet│              │
│                      └────────────────┘              │
│    "Cả Metadata lẫn Data đều trên S3"                 │
└────────────────────────────────────────────────────────┘
```

#### Giải thích cấu trúc thư mục trên S3:

##### 📂 METADATA/ (Thư mục siêu dữ liệu)
Chứa các file **mô tả cấu trúc và lịch sử** của bảng:
- **`metadata.json`**: File gốc chứa thông tin table (schema, partition spec, sort order)
- **`snap-1.avro`, `snap-2.avro`**: Các snapshot (ảnh chụp phiên bản dữ liệu)
- **`manifest-list.avro`**: Danh sách các manifest files
- **`manifest-*.avro`**: Liệt kê chi tiết các data files trong mỗi snapshot

**Vai trò:** Giống như "mục lục sách" - giúp Trino biết:
- Bảng có những cột gì?
- Dữ liệu được chia partition như thế nào?
- File nào thuộc snapshot nào?
- Cần đọc file nào để trả lời query?

##### 📂 DATA/ (Thư mục dữ liệu)
Chứa các file **dữ liệu thực tế** của bảng:
- **`file1.parquet`, `file2.parquet`**: Các file Parquet chứa rows thực tế
- Thường được tổ chức theo partition: 
  - `DATA/year=2024/month=01/file1.parquet`
  - `DATA/year=2024/month=02/file2.parquet`

**Vai trò:** Giống như "nội dung sách" - chứa dữ liệu thô mà user cần query.

##### 🔄 Quy trình hoạt động:
1. **Trino nhận query** → Đọc `METADATA/` để biết phải đọc file nào
2. **Iceberg lọc files** → Chỉ chọn data files cần thiết (partition pruning)
3. **Workers đọc song song** → Mỗi worker đọc một vài files từ `DATA/`
4. **Trả kết quả** → Gộp lại và trả về cho user

**Ví dụ:**
```sql
-- Query: SELECT * FROM orders WHERE date = '2024-01-15'
-- 
-- Bước 1: Trino đọc METADATA/metadata.json
--         → Biết table có partition theo date
-- 
-- Bước 2: Iceberg đọc METADATA/manifest-*.avro
--         → Tìm file: DATA/date=2024-01-15/orders.parquet
-- 
-- Bước 3: Worker chỉ đọc 1 file thay vì scan toàn bộ DATA/
--         → Nhanh hơn 1000x
```

---
## Bảng so sánh nhanh

| Tiêu chí | PostgreSQL | Trino + Iceberg + S3 |
|----------|------------|----------------------|
| **Kiến trúc** | Monolithic (Compute + Storage gắn liền) | Decoupled (Compute và Storage tách rời) |
| **Giới hạn lưu trữ** | ~10 TB / bảng | Petabytes (gần như vô hạn) |
| **Khả năng mở rộng** | Vertical scaling (nâng cấp máy chủ) | Horizontal scaling (thêm workers) |
| **Use case chính** | OLTP (giao dịch thời gian thực) | OLAP (phân tích dữ liệu lớn) |
| **Schema Evolution** | Cần lock bảng, rewrite data | Chỉ sửa metadata (1 giây) |
| **Time Travel** | Cần extension (phức tạp) | Built-in (snapshots) |
| **Chi phí lưu trữ** | Cao (SSD/HDD gắn máy chủ) | Thấp (S3: ~$0.023/GB/tháng) |
| **Hiệu năng OLTP** | ⭐⭐⭐⭐⭐ Rất cao | ⭐⭐ Thấp (không tối ưu cho transactions) |
| **Hiệu năng OLAP** | ⭐⭐ Giới hạn bởi CPU/RAM 1 máy | ⭐⭐⭐⭐⭐ Xử lý song song hàng trăm nodes |

---

## 3. Tham khảo thêm: "Xử lý song song hàng trăm nodes" nghĩa là gì?

### Node là gì?
**Node** = một máy chủ/server riêng biệt chạy Trino Worker. Mỗi node có CPU, RAM riêng để xử lý dữ liệu.

### Xử lý song song (Parallel Processing)
Khi bạn chạy một câu query lớn, Trino tự động:

1. **Chia nhỏ công việc** thành nhiều phần (tasks)
2. **Phân phối** các tasks này cho hàng trăm workers
3. **Mỗi worker xử lý đồng thời** phần dữ liệu của mình
4. **Gộp kết quả** lại cuối cùng

### Ví dụ cụ thể:

```
Query: SELECT * FROM orders WHERE year = 2024
Data: 1 tỷ records, 100 files parquet

┌──────────────────────────────────────┐
│    Trino Coordinator (Chỉ huy)      │
│  "Chia query thành 100 tasks"       │
└────────┬─────────────────────────────┘
         │
    ┌────┴────┬────────┬────────┬─────┐
    │         │        │        │     │
    ▼         ▼        ▼        ▼     ▼
┌────────┐ ┌────────┐ ┌────────┐ ... ┌────────┐
│Worker 1│ │Worker 2│ │Worker 3│     │Worker N│
│Read    │ │Read    │ │Read    │     │Read    │
│file1   │ │file2   │ │file3   │     │file100 │
│        │ │        │ │        │     │        │
│Process │ │Process │ │Process │     │Process │
│10M rows│ │10M rows│ │10M rows│     │10M rows│
└────────┘ └────────┘ └────────┘     └────────┘
    │         │        │        │     │
    └────┬────┴────────┴────────┴─────┘
         ▼
   ┌───────────┐
   │  Result   │ ← Kết quả gộp lại
   └───────────┘

Thời gian: ~30 giây (với 100 workers)
```

### So sánh PostgreSQL:
```
PostgreSQL: 1 máy chủ duy nhất
├─ CPU: 16 cores xử lý tuần tự
└─ Thời gian: ~50 phút (với 1 tỷ records)

Trino: 100 workers (100 máy chủ)
├─ Mỗi worker: 16 cores
├─ Tổng: 1,600 cores xử lý ĐỒNG THỜI
└─ Thời gian: ~30 giây
```

### Lợi ích chính:
- **Tốc độ:** Query 1 tỷ records trong vài giây thay vì vài giờ
- **Scalability:** Cần nhanh hơn? → Thêm workers (horizontal scaling)
- **Hiệu quả:** Mỗi worker chỉ đọc phần data cần thiết từ S3

Đó là lý do Trino + Iceberg phù hợp cho **Big Data Analytics** (OLAP), còn PostgreSQL phù hợp cho **Transactional Systems** (OLTP).

---

## 4. Tham khảo thêm: Cơ chế hoạt động của Metadata & Partition

### 4.1. Partition có phải là mặc định không?

**KHÔNG.** Bạn phải thiết lập khi tạo bảng:

```sql
-- ❌ Không partition (mặc định)
CREATE TABLE orders (
    id INT,
    customer_name VARCHAR,
    order_date DATE
) WITH (format = 'PARQUET');

-- ✅ CÓ partition (do bạn chỉ định)
CREATE TABLE orders (
    id INT,
    customer_name VARCHAR,
    order_date DATE
) 
PARTITIONED BY (order_date)  ← Bạn phải khai báo
WITH (format = 'PARQUET');
```

**Nếu không khai báo `PARTITIONED BY`:**
- Iceberg sẽ lưu tất cả data vào 1 thư mục flat
- Không có cấu trúc `date=2024-01-15/`
- Query phải scan toàn bộ data (chậm)

---

### 4.2. Iceberg có đọc tất cả manifest-*.avro không?

**KHÔNG.** Iceberg đọc thông minh qua 4 bước:

```
Bước 1: Đọc metadata.json (1 file duy nhất)
        ↓
        Biết snapshot hiện tại là: snap-5.avro

Bước 2: Đọc snap-5.avro (1 file snapshot)
        ↓
        Trong này có field: "manifest_list": "manifest-list-snap5.avro"

Bước 3: Đọc manifest-list-snap5.avro (1 file list)
        ↓
        Trong này liệt kê: [manifest-001.avro, manifest-045.avro]
        (chỉ liệt kê những manifest có partition phù hợp)

Bước 4: Đọc manifest-001.avro, manifest-045.avro
        ↓
        Lấy ra danh sách data files
```

**Ví dụ thực tế:**
- Bạn có 1000 manifest files trong S3
- Query: `SELECT * FROM orders WHERE date = '2024-01-15'`
- Iceberg chỉ đọc **2-3 manifest files** (những file chứa partition date=2024-01-15)
- Bỏ qua 997 manifest files còn lại

**→ Tối ưu I/O, tiết kiệm chi phí S3**

---

### 4.3. Làm sao Iceberg tìm ra đường dẫn file từ manifest?

Trong **manifest-001.avro** có cấu trúc như này:

```json
{
  "schema": {...},
  "data_files": [
    {
      "file_path": "s3://bucket/warehouse/orders/data/order_date=2024-01-15/00001-1-abc123.parquet",
      "file_format": "PARQUET",
      "partition": {
        "order_date": "2024-01-15"  ← Iceberg lưu partition value
      },
      "record_count": 150000,
      "file_size_in_bytes": 45000000,
      "column_stats": {
        "id": {"min": 1, "max": 150000},
        "total_amount": {"min": 10.5, "max": 9999.99}
      }
    },
    {
      "file_path": "s3://bucket/warehouse/orders/data/order_date=2024-01-16/00002-1-def456.parquet",
      "partition": {
        "order_date": "2024-01-16"
      },
      "record_count": 180000,
      "file_size_in_bytes": 52000000
    }
  ]
}
```

**Quy trình lọc file:**

```
Query: SELECT * FROM orders WHERE date = '2024-01-15'

Bước 1: Iceberg đọc manifest → thấy có field "partition.order_date"

Bước 2: Lọc chỉ giữ lại entries có order_date = "2024-01-15"
        ↓
        [
          {
            "file_path": "s3://.../order_date=2024-01-15/00001-1-abc123.parquet",
            "record_count": 150000
          }
        ]

Bước 3: Trả về danh sách file_path cho Trino Workers

Bước 4: Workers đọc đúng file đó (150,000 rows)
        thay vì scan 1 tỷ rows
```

**Tóm lại:**
- Đường dẫn file đầy đủ (bao gồm partition path) được **lưu trực tiếp trong manifest**
- Iceberg không phải "đoán" hay "tìm kiếm"
- Nó chỉ cần **đọc và lọc** theo partition value
- **Partition Pruning** = Bỏ qua files không liên quan → Query nhanh hơn 100-1000x

---

## 5. Tham khảo thêm: Cơ chế Snapshot trong Iceberg

### 5.1. Snapshot là gì?
**Snapshot** = một "ảnh chụp" trạng thái của bảng tại một thời điểm cụ thể. Mỗi khi bạn INSERT/UPDATE/DELETE dữ liệu, Iceberg tạo một snapshot mới.

### 5.2. Cấu trúc cụ thể:

```
s3://bucket/warehouse/orders/
├── metadata/
│   ├── metadata.json                    ← File gốc (luôn trỏ đến snapshot mới nhất)
│   ├── snap-1234567890.avro            ← Snapshot 1 (lúc 10:00 AM)
│   ├── snap-1234567891.avro            ← Snapshot 2 (lúc 11:00 AM)
│   ├── snap-1234567892.avro            ← Snapshot 3 (lúc 12:00 PM)
│   ├── manifest-list-snap1.avro
│   ├── manifest-list-snap2.avro
│   ├── manifest-001.avro
│   ├── manifest-002.avro
│   └── ...
└── data/
    ├── file1.parquet  ← Được tạo lúc 10:00 AM
    ├── file2.parquet  ← Được tạo lúc 11:00 AM
    ├── file3.parquet  ← Được tạo lúc 12:00 PM
    └── ...
```

### 5.3. Nội dung của snapshot file:

**snap-1234567891.avro** (Snapshot 2):
```json
{
  "snapshot_id": 1234567891,
  "timestamp_ms": 1642161600000,
  "operation": "append",  // hoặc "overwrite", "delete"
  "manifest_list": "s3://.../manifest-list-snap2.avro",
  "summary": {
    "total-records": 500000,
    "total-files-size": 150000000,
    "total-data-files": 3
  }
}
```

### 5.4. Khi nào tạo snapshot mới?

```sql
-- 10:00 AM: INSERT đầu tiên
INSERT INTO orders VALUES (1, 'John', '2024-01-01');
→ Tạo snap-1234567890.avro
→ Tạo file1.parquet

-- 11:00 AM: INSERT thêm
INSERT INTO orders VALUES (2, 'Jane', '2024-01-02');
→ Tạo snap-1234567891.avro (snapshot mới)
→ Tạo file2.parquet (file mới)
→ file1.parquet VẪN TỒN TẠI

-- 12:00 PM: DELETE
DELETE FROM orders WHERE id = 1;
→ Tạo snap-1234567892.avro (snapshot mới)
→ KHÔNG xóa file1.parquet vật lý
→ Chỉ đánh dấu trong metadata: "file1.parquet không còn trong snapshot này"
```

### 5.5. Cơ chế hoạt động chi tiết:

#### Query mặc định (luôn đọc snapshot mới nhất)
```sql
SELECT * FROM orders;
```

**Quy trình:**
```
1. Trino đọc metadata.json
   → "current_snapshot_id": 1234567892

2. Trino đọc snap-1234567892.avro
   → "manifest_list": "manifest-list-snap2.avro"

3. Trino đọc manifest-list-snap2.avro
   → Liệt kê: [manifest-002.avro, manifest-003.avro]
   (KHÔNG có manifest-001.avro vì file1.parquet đã bị DELETE)

4. Trino đọc manifest-002.avro, manifest-003.avro
   → file_paths: [file2.parquet, file3.parquet]

5. Workers đọc: file2.parquet + file3.parquet
   → Kết quả: 2 rows (id=2, id=3)
```

#### Time Travel (đọc snapshot cũ)
```sql
SELECT * FROM orders FOR SYSTEM_TIME AS OF TIMESTAMP '2024-01-01 11:00:00';
```

**Quy trình:**
```
1. Trino tìm snapshot gần timestamp 11:00
   → Tìm thấy snap-1234567891.avro

2. Trino đọc snap-1234567891.avro
   → "manifest_list": "manifest-list-snap1.avro"

3. Trino đọc manifest-list-snap1.avro
   → Liệt kê: [manifest-001.avro, manifest-002.avro]

4. Trino đọc manifest files
   → file_paths: [file1.parquet, file2.parquet]

5. Workers đọc: file1.parquet + file2.parquet
   → Kết quả: 3 rows (bao gồm cả id=1 đã bị DELETE)
```

### 5.6. Tại sao không xóa file vật lý ngay?

**Lý do:**
1. **Time Travel:** Có thể query dữ liệu quá khứ
2. **Rollback:** Có thể quay lại phiên bản cũ nếu sai
3. **Concurrent Queries:** Query đang chạy có thể đang đọc file cũ

**Khi nào xóa?**
- Chạy lệnh **VACUUM** / **EXPIRE_SNAPSHOTS**:
```sql
-- Xóa snapshots cũ hơn 7 ngày
CALL iceberg.system.expire_snapshots('orders', TIMESTAMP '2024-01-01 00:00:00');
```
- Lúc này mới xóa vật lý: `file1.parquet`, `snap-1234567890.avro`, `manifest-001.avro`

### 5.7. So sánh với PostgreSQL:

| Hành động | PostgreSQL | Iceberg |
|-----------|-----------|---------|
| **INSERT** | Ghi trực tiếp vào table file | Tạo file parquet mới + snapshot mới |
| **DELETE** | Xóa row khỏi table file ngay | Tạo snapshot mới, đánh dấu file cũ (không xóa vật lý) |
| **Time Travel** | Cần WAL + extension phức tạp | Built-in, chỉ cần chỉ định snapshot_id |
| **Rollback** | Khó, cần backup | Đơn giản: `SET CURRENT SNAPSHOT = snapshot_id` |

### 5.8. Tóm lại:
- **Snapshot** = phiên bản của bảng tại một thời điểm
- **Mỗi lần thay đổi** (INSERT/UPDATE/DELETE) = tạo snapshot mới
- **File vật lý không bị xóa ngay**, chỉ "ẩn" khỏi snapshot hiện tại
- **Time Travel** = đọc snapshot cũ
- **VACUUM** = xóa snapshot + file cũ để tiết kiệm storage


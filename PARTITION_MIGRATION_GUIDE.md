# LÀM RÕ VẤN ĐỀ: Thêm Partition Về Sau & Multi-Partition

## 🔴 VẤN ĐỀ 1: Thêm Partition Về Sau - Data Cũ CÓ Tự Động Tổ Chức Lại?

### **Tình huống:**
```sql
-- Bước 1: Tạo table KHÔNG có partition
CREATE TABLE customersc1 (
    Id VARCHAR,
    Name VARCHAR,
    Email VARCHAR,
    OrderDate DATE,
    Status VARCHAR
)
WITH (format = 'PARQUET');  -- ❌ KHÔNG có partitioning

-- Bước 2: Insert 10,000 records
INSERT INTO customersc1 VALUES (...);  -- 10,000 rows

-- Cấu trúc trên S3:
-- s3://bucket/customersc1/data/
--   ├── file1.parquet (1000 rows - nhiều ngày lẫn lộn)
--   ├── file2.parquet (1000 rows - nhiều ngày lẫn lộn)
--   └── ... (10 files - TẤT CẢ flat, không có partition)
```

### **Bước 3: Muốn thêm partition về sau**

```sql
-- ❌ TRINO/ICEBERG KHÔNG HỖ TRỢ ALTER TABLE ADD PARTITION trực tiếp
-- Lệnh này SẼ LỖI:
ALTER TABLE customersc1 
SET PROPERTIES (partitioning = ARRAY['OrderDate']);  -- ❌ KHÔNG HOẠT ĐỘNG
```

### **❌ KẾT QUẢ: Data cũ VẪN LƯU FLAT, không tự động tổ chức lại**

Sau khi thêm partition spec:
- **Data CŨ** (10,000 rows) → VẪN lưu flat trong `data/` (KHÔNG có partition folders)
- **Data MỚI** (insert sau khi thêm partition) → Lưu vào partition folders

```
s3://bucket/customersc1/data/
  ├── file1.parquet              ← Data CŨ (flat, 1000 rows nhiều ngày)
  ├── file2.parquet              ← Data CŨ (flat, 1000 rows nhiều ngày)
  ├── ...
  ├── OrderDate=2024-01-20/      ← Data MỚI (partitioned)
  │   └── file11.parquet
  └── OrderDate=2024-01-21/      ← Data MỚI (partitioned)
      └── file12.parquet
```

**⚠️ HẬU QUẢ:**
- Query `WHERE OrderDate = '2024-01-15'` → Phải scan **TẤT CẢ data cũ** (file1-10) + partition mới
- Partition pruning KHÔNG hoạt động với data cũ
- Performance KHÔNG được cải thiện cho data cũ

---

## ✅ GIẢI PHÁP: Migrate Data Cũ

### **Cách 1: Rewrite Data (CTAS - Create Table As Select)**

```sql
-- Bước 1: Tạo table MỚI với partition
CREATE TABLE customersc1_partitioned (
    Id VARCHAR,
    Name VARCHAR,
    Email VARCHAR,
    OrderDate DATE,
    Status VARCHAR
)
WITH (
    format = 'PARQUET',
    partitioning = ARRAY['OrderDate']  -- ✅ CÓ partition
);

-- Bước 2: Copy toàn bộ data từ table cũ sang table mới
INSERT INTO customersc1_partitioned
SELECT * FROM customersc1;

-- → Iceberg SẼ TỰ ĐỘNG tổ chức data theo partition:
-- s3://bucket/customersc1_partitioned/data/
--   ├── OrderDate=2024-01-15/ (1000 rows)
--   ├── OrderDate=2024-01-16/ (1500 rows)
--   └── OrderDate=2024-01-17/ (2000 rows)

-- Bước 3: Đổi tên table (optional)
DROP TABLE customersc1;
ALTER TABLE customersc1_partitioned RENAME TO customersc1;
```

**✅ KẾT QUẢ:** TẤT CẢ data đã được tổ chức lại theo partition!

---

### **Cách 2: Iceberg Table Evolution (Advanced)**

Iceberg hỗ trợ **partition evolution** nhưng KHÔNG tự động migrate data:

```sql
-- Thêm partition spec mới (Iceberg v2 feature)
ALTER TABLE customersc1 
SET TBLPROPERTIES (
    'write.format.default' = 'parquet',
    'write.partition-spec' = 'day(OrderDate)'  -- Partition mới
);

-- Data CŨ vẫn flat, chỉ data MỚI có partition
-- Cần chạy REWRITE để reorganize:

-- Spark SQL (Trino KHÔNG hỗ trợ REWRITE):
CALL iceberg.system.rewrite_data_files(
    table => 'customersc1',
    strategy => 'sort',
    sort_order => 'OrderDate'
);
```

**⚠️ LƯU Ý:** Trino **KHÔNG HỖ TRỢ** `CALL rewrite_data_files` → Cần dùng **Spark** hoặc **Flink**

---

## 🔵 VẤN ĐỀ 2: Có Đánh NHIỀU Partitions Được Không?

### **✅ CÓ - Multi-Column Partitioning (Composite Partitioning)**

Iceberg hỗ trợ partition theo **NHIỀU cột**:

```sql
-- Partition theo 2 cột: OrderDate + Status
CREATE TABLE customers_multi_partition (
    Id VARCHAR,
    Name VARCHAR,
    Email VARCHAR,
    OrderDate DATE,
    Status VARCHAR
)
WITH (
    format = 'PARQUET',
    partitioning = ARRAY['OrderDate', 'Status']  -- ✅ 2 partitions
);
```

**Cấu trúc trên S3:**
```
customers_multi_partition/data/
  ├── OrderDate=2024-01-15/
  │   ├── Status=Active/
  │   │   └── file1.parquet (100 customers)
  │   ├── Status=Pending/
  │   │   └── file2.parquet (50 customers)
  │   └── Status=Inactive/
  │       └── file3.parquet (20 customers)
  ├── OrderDate=2024-01-16/
  │   ├── Status=Active/
  │   │   └── file4.parquet (150 customers)
  │   └── Status=Pending/
  │       └── file5.parquet (30 customers)
  └── OrderDate=2024-01-17/
      └── Status=Active/
          └── file6.parquet (200 customers)
```

---

### **Query Performance với Multi-Partition:**

```sql
-- ✅ Query theo PARTITION KEY đầu tiên (OrderDate) → NHANH
SELECT * FROM customers_multi_partition
WHERE OrderDate = DATE '2024-01-15';
-- → Scan 3 folders: Active/, Pending/, Inactive/ (170 customers)

-- ✅ Query theo CẢ 2 PARTITION KEYS → CỰC NHANH
SELECT * FROM customers_multi_partition
WHERE OrderDate = DATE '2024-01-15' 
  AND Status = 'Active';
-- → Scan 1 folder: OrderDate=2024-01-15/Status=Active/ (100 customers)

-- ⚠️ Query CHỈ theo partition key THỨ 2 (Status) → VẪN CHẬM
SELECT * FROM customers_multi_partition
WHERE Status = 'Active';
-- → Phải scan TẤT CẢ folders OrderDate=*/Status=Active/ (450 customers)
```

---

### **Best Practices cho Multi-Partition:**

1. **Thứ tự partition quan trọng:**
   - Partition đầu tiên = Cột query NHIỀU NHẤT
   - Partition thứ 2 = Cột query thứ yếu

2. **Ví dụ đúng:**
   ```sql
   -- ✅ TỐT: Partition theo Time + Region
   PARTITIONING = ARRAY['OrderDate', 'Region']
   
   -- Query: WHERE OrderDate = '2024-01-15' AND Region = 'US'
   -- → Scan 1 folder duy nhất
   ```

3. **Ví dụ SAI:**
   ```sql
   -- ❌ TỆ: Partition theo GUID + Date
   PARTITIONING = ARRAY['Id', 'OrderDate']
   
   -- → Tạo TRIỆU folders (partition explosion)
   ```

---

## 📊 BẢNG SO SÁNH:

| Tình huống | Có tự động reorganize? | Giải pháp |
|------------|------------------------|-----------|
| **Thêm partition về sau** | ❌ KHÔNG | CTAS (tạo table mới) |
| **Data cũ flat + data mới partitioned** | ⚠️ Data cũ vẫn flat | Rewrite với Spark |
| **Multi-partition (2+ cột)** | ✅ CÓ hỗ trợ | Cẩn thận thứ tự partition |
| **Partition theo GUID** | ❌ KHÔNG NÊN | Partition explosion |

---

## 🎯 KẾT LUẬN:

### **Câu hỏi 1:** Thêm partition về sau, data cũ có tự động vào đúng thư mục?
**→ ❌ KHÔNG.** Data cũ vẫn lưu flat. Cần dùng CTAS hoặc Spark REWRITE.

### **Câu hỏi 2:** Đánh nhiều partition được không?
**→ ✅ ĐƯỢC.** Nhưng cần chú ý:
- Thứ tự partition quan trọng (cột query nhiều nhất đặt trước)
- Tránh partition theo high-cardinality column (GUID, Email...)
- Best practice: **Partition theo Time + Low-cardinality column**

---

## 🚀 KHUYẾN NGHỊ:

**Từ đầu hãy thiết kế partition đúng:**
```sql
-- ✅ TỐT: Partition theo ngày (cardinality thấp, query nhiều)
PARTITIONING = ARRAY['OrderDate']

-- ✅ TUYỆT VỜI: Multi-partition với thứ tự hợp lý
PARTITIONING = ARRAY['OrderDate', 'Region']

-- ❌ TỆ: Partition sau khi đã có data → Phải migrate
-- ❌ TỆ: Partition theo GUID → Partition explosion
```

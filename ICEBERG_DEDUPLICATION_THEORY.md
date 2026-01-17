# LÝ THUYẾT: XỬ LÝ TRÙNG LẶP TRONG TRINO/ICEBERG

## 1. CƠ CHẾ INSERT TRONG ICEBERG

### 1.1. Iceberg CHO PHÉP INSERT TRÙNG 100%
```sql
-- Iceberg KHÔNG TỰ ĐỘNG kiểm tra trùng lặp
INSERT INTO tonghopkcb (id, ma_lk, ho_ten) VALUES 
  ('123e4567-e89b-12d3-a456-426614174000', 'TEST001', 'Nguyen Van A');

-- Insert lại CÙNG ID → Iceberg VẪN CHO PHÉP
INSERT INTO tonghopkcb (id, ma_lk, ho_ten) VALUES 
  ('123e4567-e89b-12d3-a456-426614174000', 'TEST001', 'Nguyen Van A');

-- Kết quả: 2 bản ghi GIỐNG HỆT NHAU trong bảng
```

**Lý do:**
- Iceberg là **Append-Only** format (giống Parquet)
- KHÔNG có Primary Key constraint như RDBMS
- Mỗi INSERT tạo ra **Parquet file mới** chứa data
- Metadata (snapshot) chỉ track files, KHÔNG validate duplicate

---

## 2. KHI SELECT - KHÔNG TỰ ĐỘNG GỘP

### 2.1. Kết quả SELECT với dữ liệu trùng

```sql
-- Sau 2 lần insert trên
SELECT * FROM tonghopkcb WHERE id = '123e4567-e89b-12d3-a456-426614174000';

-- KẾT QUẢ: 2 DÒNG GIỐNG NHAU
+--------------------------------------+---------+--------------+
| id                                   | ma_lk   | ho_ten       |
+--------------------------------------+---------+--------------+
| 123e4567-e89b-12d3-a456-426614174000 | TEST001 | Nguyen Van A |
| 123e4567-e89b-12d3-a456-426614174000 | TEST001 | Nguyen Van A |
+--------------------------------------+---------+--------------+
```

**Iceberg KHÔNG tự động deduplicate vì:**
- Mỗi Parquet file là immutable (read-only)
- Query engine (Trino) đọc TẤT CẢ các files
- Không có logic tự động DISTINCT

---

## 3. CẤU TRÚC LƯU TRỮ - NGUYÊN NHÂN TRÙNG

### 3.1. Cấu trúc Iceberg Table

```
tonghopkcb/
├── metadata/
│   ├── v1.metadata.json          # Snapshot 1: file1.parquet
│   ├── v2.metadata.json          # Snapshot 2: file1.parquet + file2.parquet
│   └── v3.metadata.json          # Snapshot 3: file1 + file2 + file3
└── data/
    ├── file1.parquet             # Insert lần 1: 1000 records
    ├── file2.parquet             # Insert lần 2: 1000 records (có thể trùng)
    └── file3.parquet             # Insert lần 3: 500 records (có thể trùng)
```

### 3.2. Snapshot Mechanism

```json
// v3.metadata.json
{
  "snapshot-id": 3,
  "timestamp-ms": 1705392000000,
  "manifest-list": "s3://bucket/metadata/snap-3-manifests.avro",
  "summary": {
    "total-data-files": 3,           // 3 files
    "total-records": 2500,           // TỔNG records (có thể trùng)
    "total-files-size": 45678900
  }
}
```

**Khi SELECT:**
1. Trino đọc metadata snapshot mới nhất (v3)
2. Trino quét TẤT CẢ 3 files: file1 + file2 + file3
3. Trả về 2500 records (KHÔNG loại bỏ trùng)

---

## 4. CÁC TRƯỜNG HỢP TRÙNG LẶP

### 4.1. Trùng Hoàn Toàn (Duplicate Rows)
```sql
-- Cùng ID + cùng tất cả cột
INSERT INTO tonghopkcb VALUES ('id1', 'TEST', 'Name', 1000);
INSERT INTO tonghopkcb VALUES ('id1', 'TEST', 'Name', 1000);

-- KẾT QUẢ: 2 dòng giống hệt nhau
```

### 4.2. Trùng Khóa Chính (Duplicate Keys)
```sql
-- Cùng ID nhưng khác dữ liệu
INSERT INTO tonghopkcb VALUES ('id1', 'TEST', 'Name A', 1000);
INSERT INTO tonghopkcb VALUES ('id1', 'TEST', 'Name B', 2000); -- Khác name, amount

-- KẾT QUẢ: 2 dòng cùng ID nhưng data khác
-- Đây là vấn đề DATA INCONSISTENCY nghiêm trọng
```

### 4.3. Trùng Do Retry/Network Issue
```bash
# Application retry insert do network timeout
INSERT ... # Thành công nhưng client không nhận response
INSERT ... # Retry → Duplicate
```

---

## 5. OPTIMIZE - COMPACTION VÀ DEDUPLICATION

### 5.1. Lệnh OPTIMIZE trong Trino

```sql
-- ❌ Trino KHÔNG HỖ TRỢ OPTIMIZE command
OPTIMIZE TABLE tonghopkcb;  -- ERROR: Syntax error

-- ✅ Trino chỉ hỗ trợ via Spark/Flink
```

### 5.2. Sử dụng Spark để OPTIMIZE

```python
# PySpark - Compaction only (KHÔNG deduplicate)
spark.sql("""
    CALL iceberg.system.rewrite_data_files(
        table => 'iceberg.v1.tonghopkcb',
        strategy => 'binpack',
        options => map('target-file-size-bytes', '134217728') -- 128MB
    )
""")

# KẾT QUẢ:
# - Gộp file1.parquet + file2.parquet + file3.parquet → file_consolidated.parquet
# - VẪN GIỮ NGUYÊN duplicate records
# - CHỈ giảm số lượng files (small file problem)
```

**Compaction CHỈ làm:**
- ✅ Gộp nhiều small files → ít files lớn hơn
- ✅ Tăng query performance (ít file scan hơn)
- ❌ KHÔNG loại bỏ duplicate records

### 5.3. VÍ DỤ CỤ THỂ: OPTIMIZE KHÔNG DEDUPLICATE

**TRƯỚC OPTIMIZE:**
```
data/
├── file1.parquet (500 KB)
│   ├── Record A (id=1, name='Test')
│   ├── Record B (id=2, name='Demo')
│   └── Record C (id=1, name='Test')  ← TRÙNG với Record A
│
├── file2.parquet (600 KB)
│   ├── Record D (id=3, name='Hello')
│   └── Record E (id=1, name='Test')  ← TRÙNG với Record A
│
└── file3.parquet (400 KB)
    ├── Record F (id=4, name='World')
    └── Record G (id=2, name='Demo')  ← TRÙNG với Record B

Total: 3 files, 7 records (có 4 records trùng)
```

**SAU OPTIMIZE:**
```
data/
└── file_consolidated.parquet (1.5 MB)
    ├── Record A (id=1, name='Test')
    ├── Record B (id=2, name='Demo')
    ├── Record C (id=1, name='Test')  ← VẪN TRÙNG
    ├── Record D (id=3, name='Hello')
    ├── Record E (id=1, name='Test')  ← VẪN TRÙNG
    ├── Record F (id=4, name='World')
    └── Record G (id=2, name='Demo')  ← VẪN TRÙNG

Total: 1 file, 7 records (VẪN CÓ 4 records trùng)
```

**KẾT QUẢ QUERY:**
```sql
-- Query sau OPTIMIZE
SELECT * FROM tonghopkcb WHERE id = 1;

-- Kết quả: VẪN TRẢ VỀ 3 RECORDS TRÙNG
+----+------+
| id | name |
+----+------+
|  1 | Test |
|  1 | Test |
|  1 | Test |
+----+------+
```

**KẾT LUẬN:**
- ✅ OPTIMIZE gộp 3 files → 1 file
- ✅ Query nhanh hơn (scan 1 file thay vì 3)
- ❌ Duplicate records VẪN TỒN TẠI
- ❌ Logic business KHÔNG thay đổi

---

## 6. XỬ LÝ TRÙNG LẶP - CÁC CÁCH TIẾP CẬN

### 6.1. PHÒNG TRÁNH - Application Level

```csharp
// Cách 1: Check exist trước khi insert
public async Task<bool> InsertIfNotExistsAsync(TongHopKcb record)
{
    var exists = await _context.TongHopKcbs
        .Where(t => t.Id == record.Id)
        .AnyAsync();
    
    if (!exists)
    {
        await _context.TongHopKcbs.AddAsync(record);
        return true;
    }
    return false; // Đã tồn tại
}

// Cách 2: Idempotency Key (cho retry safety)
public class TongHopKcb
{
    public Guid Id { get; set; }
    public string IdempotencyKey { get; set; } // Request ID duy nhất
    // ...
}
```

### 6.2. MERGE INTO - Upsert (Trino hỗ trợ)

```sql
-- ✅ Trino hỗ trợ MERGE INTO cho Iceberg
MERGE INTO tonghopkcb AS target
USING (
    SELECT 
        UUID '123e4567-e89b-12d3-a456-426614174000' as id,
        'TEST001' as ma_lk,
        'Nguyen Van A' as ho_ten
) AS source
ON target.id = source.id
WHEN MATCHED THEN 
    UPDATE SET 
        ma_lk = source.ma_lk,
        ho_ten = source.ho_ten
WHEN NOT MATCHED THEN
    INSERT (id, ma_lk, ho_ten) 
    VALUES (source.id, source.ma_lk, source.ho_ten);

-- KẾT QUẢ:
-- - Nếu id tồn tại → UPDATE (ghi đè)
-- - Nếu id chưa có → INSERT
-- - KHÔNG tạo duplicate
```

**Lưu ý MERGE INTO:**
- ✅ Đảm bảo unique constraint
- ⚠️ Performance CHẬM (phải scan toàn bảng để check MATCHED)
- ⚠️ Với 100k inserts → 100k lần scan → RẤT CHẬM

### 6.3. DEDUPLICATION - Manual Query

```sql
-- Cách 1: DISTINCT trong SELECT
SELECT DISTINCT id, ma_lk, ho_ten
FROM tonghopkcb;

-- Cách 2: GROUP BY với aggregation
SELECT 
    id,
    MAX(ma_lk) as ma_lk,
    MAX(ho_ten) as ho_ten,
    COUNT(*) as duplicate_count
FROM tonghopkcb
GROUP BY id
HAVING COUNT(*) > 1;  -- Chỉ hiển thị records trùng

-- Cách 3: ROW_NUMBER() - giữ bản ghi mới nhất
SELECT *
FROM (
    SELECT 
        *,
        ROW_NUMBER() OVER (PARTITION BY id ORDER BY created_at DESC) as rn
    FROM tonghopkcb
)
WHERE rn = 1;  -- Chỉ lấy bản ghi mới nhất mỗi ID
```

### 6.4. CREATE TABLE AS SELECT (CTAS) - Tạo bảng mới

```sql
-- Tạo bảng mới KHÔNG có duplicate
CREATE TABLE tonghopkcb_dedup AS
SELECT DISTINCT *
FROM tonghopkcb;

-- Hoặc giữ bản ghi mới nhất
CREATE TABLE tonghopkcb_dedup AS
SELECT *
FROM (
    SELECT 
        *,
        ROW_NUMBER() OVER (PARTITION BY id ORDER BY created_at DESC) as rn
    FROM tonghopkcb
)
WHERE rn = 1;

-- Sau đó: DROP bảng cũ, RENAME bảng mới
DROP TABLE tonghopkcb;
ALTER TABLE tonghopkcb_dedup RENAME TO tonghopkcb;
```

---

## 7. DELETE DUPLICATES - Spark/Flink Required

```python
# Spark - Delete duplicates giữ lại 1 bản ghi
from pyspark.sql.window import Window
from pyspark.sql.functions import row_number, col

# 1. Đọc table
df = spark.read.format("iceberg").load("iceberg.v1.tonghopkcb")

# 2. Đánh số thứ tự (giữ bản ghi mới nhất)
window = Window.partitionBy("id").orderBy(col("created_at").desc())
df_with_rn = df.withColumn("rn", row_number().over(window))

# 3. Filter giữ bản ghi đầu tiên
df_dedup = df_with_rn.filter(col("rn") == 1).drop("rn")

# 4. Overwrite bảng
df_dedup.writeTo("iceberg.v1.tonghopkcb") \
    .overwritePartitions() \
    .create()
```

**⚠️ Trino KHÔNG hỗ trợ DELETE trùng lặp trực tiếp**

---

## 8. COPY-ON-WRITE vs MERGE-ON-READ

### 8.1. Copy-on-Write (Iceberg default)

```
INSERT → Tạo Parquet file mới
UPDATE → Đọc file cũ + Ghi file mới (rewrite)
DELETE → Đọc file cũ + Ghi file mới (không có deleted rows)

✅ Read nhanh (1 lần scan)
❌ Write chậm (phải rewrite file)
```

### 8.2. Merge-on-Read (Iceberg v2+)

```
INSERT → Tạo Parquet file mới
UPDATE → Ghi "delete file" + "insert file" 
DELETE → Chỉ ghi "delete file"

✅ Write nhanh (append only)
❌ Read chậm (phải merge delete files)
❌ Cần OPTIMIZE định kỳ
```

**Iceberg dùng Copy-on-Write → Duplicate vẫn tồn tại trong files**

---

## 9. KẾT LUẬN - BEST PRACTICES

### 9.1. PHÒNG TRÁNH TRÙNG (Recommended)

```csharp
// ✅ Check exist trước insert (cho single record)
if (!await ExistsAsync(id)) {
    await InsertAsync(record);
}

// ✅ Sử dụng MERGE INTO (cho upsert)
await MergeIntoAsync(records);

// ✅ Idempotency key (cho retry safety)
record.RequestId = Guid.NewGuid(); // Unique per request
```

### 9.2. XỬ LÝ TRÙNG (Periodic cleanup)

```sql
-- 1. Định kỳ check duplicate count
SELECT id, COUNT(*) as count
FROM tonghopkcb
GROUP BY id
HAVING COUNT(*) > 1;

-- 2. Nếu có trùng → Dùng Spark để dedup
-- (Không thể dùng Trino DELETE)

-- 3. Hoặc CTAS tạo bảng mới
CREATE TABLE tonghopkcb_v2 AS
SELECT DISTINCT * FROM tonghopkcb;
```

### 9.3. OPTIMIZE (Compaction)

```sql
-- Trino không hỗ trợ, dùng Spark
CALL iceberg.system.rewrite_data_files(
    table => 'iceberg.v1.tonghopkcb'
);

-- Hoặc dùng scheduling job (Airflow, dbt)
-- Chạy mỗi ngày để gộp small files
```

---

## 10. TESTCASE CẦN KIỂM TRA

### Test 1: Insert Duplicate - Verify Allowed
- Insert cùng ID 2 lần
- SELECT count → Expect: 2 records

### Test 2: MERGE INTO - Verify Upsert
- MERGE cùng ID 2 lần
- SELECT count → Expect: 1 record (updated)

### Test 3: Deduplication Query
- Insert 1000 records với 100 IDs trùng (10x mỗi ID)
- SELECT DISTINCT → Expect: 100 unique records

### Test 4: Compaction Simulation
- Insert 100 batches (100 files)
- Verify file count → Expect: 100 files
- (Sau OPTIMIZE → Expect: ~1-2 files nếu dùng Spark)

### Test 5: Performance Impact
- So sánh query time: 1 file vs 100 files
- Expect: 100 files chậm hơn (more file scans)

---

## REFERENCES

- [Iceberg Table Format](https://iceberg.apache.org/spec/)
- [Trino Iceberg Connector](https://trino.io/docs/current/connector/iceberg.html)
- [Iceberg MERGE INTO](https://iceberg.apache.org/docs/latest/spark-writes/#merge-into)
- [File Compaction](https://iceberg.apache.org/docs/latest/maintenance/#compact-data-files)

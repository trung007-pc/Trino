-- =========================================================
-- CREATE CUSTOMERS TABLE WITH PARTITION
-- =========================================================
-- Partition theo OrderDate để tối ưu query theo thời gian
-- Lý do: Thường query khách hàng theo ngày đặt hàng gần đây
-- =========================================================

-- Drop table nếu tồn tại (OPTIONAL - để test lại từ đầu)
-- DROP TABLE IF EXISTS iceberg.v4.customers_partitioned;

-- Tạo bảng với partition theo OrderDate
CREATE TABLE IF NOT EXISTS iceberg.v4.customers_partitioned (
    Id VARCHAR,              -- GUID: a1b2c3d4-e5f6-7890-abcd-ef1234567890
    Name VARCHAR,            -- Tên khách hàng
    Email VARCHAR,           -- Email khách hàng
    OrderDate DATE,          -- Ngày đặt hàng (PARTITION KEY)
    Status VARCHAR           -- Trạng thái: 'Active', 'Inactive', 'Pending'
)
WITH (
    format = 'PARQUET',
    partitioning = ARRAY['OrderDate']  -- ✅ PARTITION THEO OrderDate
);

-- Verify table created
SHOW TABLES IN iceberg.v4;

-- Xem cấu trúc bảng
DESCRIBE iceberg.v4.customers_partitioned;

-- =========================================================
-- CẤU TRÚC TRÊN S3 SAU KHI INSERT:
-- =========================================================
-- s3://bucket/warehouse/customers_partitioned/data/
--   ├── OrderDate=2024-01-15/
--   │   ├── file1.parquet (100 customers của ngày 15/01)
--   │   └── file2.parquet (50 customers của ngày 15/01)
--   ├── OrderDate=2024-01-16/
--   │   └── file3.parquet (200 customers của ngày 16/01)
--   └── OrderDate=2024-01-17/
--       └── file4.parquet (150 customers của ngày 17/01)
-- 
-- ✅ Query: WHERE OrderDate = '2024-01-15'
--    → CHỈ scan 2 files (file1.parquet, file2.parquet)
--    → Bỏ qua file3.parquet, file4.parquet
--    → NHANH HƠN 100-1000x
-- =========================================================

-- =========================================================
-- TEST INSERT EXAMPLES:
-- =========================================================

-- Insert đơn lẻ
INSERT INTO iceberg.v4.customers_partitioned (Id, Name, Email, OrderDate, Status)
VALUES (
    'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
    'John Doe',
    'john@example.com',
    DATE '2024-01-15',
    'Active'
);

-- Insert nhiều records cùng partition (cùng ngày)
INSERT INTO iceberg.v4.customers_partitioned (Id, Name, Email, OrderDate, Status)
VALUES 
    ('b2c3d4e5-f6a7-8901-bcde-f12345678901', 'Jane Smith', 'jane@example.com', DATE '2024-01-15', 'Active'),
    ('c3d4e5f6-a7b8-9012-cdef-123456789012', 'Bob Wilson', 'bob@example.com', DATE '2024-01-15', 'Pending');

-- Insert vào partitions khác nhau (nhiều ngày)
INSERT INTO iceberg.v4.customers_partitioned (Id, Name, Email, OrderDate, Status)
VALUES 
    ('d4e5f6a7-b8c9-0123-def1-234567890123', 'Alice Brown', 'alice@example.com', DATE '2024-01-16', 'Active'),
    ('e5f6a7b8-c9d0-1234-ef12-345678901234', 'Charlie Davis', 'charlie@example.com', DATE '2024-01-17', 'Inactive');

-- =========================================================
-- TEST QUERY WITH PARTITION PRUNING:
-- =========================================================

-- ✅ Query theo partition key (NHANH - chỉ scan 1 partition)
SELECT * FROM iceberg.v4.customers_partitioned 
WHERE OrderDate = DATE '2024-01-15';

-- ✅ Query theo range (scan nhiều partitions)
SELECT * FROM iceberg.v4.customers_partitioned 
WHERE OrderDate BETWEEN DATE '2024-01-15' AND DATE '2024-01-17';

-- ❌ Query theo non-partition key (CHẬM - scan TẤT CẢ partitions)
SELECT * FROM iceberg.v4.customers_partitioned 
WHERE Id = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890';

-- ✅ Kết hợp partition + non-partition key (NHANH - lọc partition trước)
SELECT * FROM iceberg.v4.customers_partitioned 
WHERE OrderDate = DATE '2024-01-15' 
  AND Status = 'Active';

-- =========================================================
-- AGGREGATE QUERIES:
-- =========================================================

-- Đếm số khách hàng theo ngày (mỗi partition)
SELECT OrderDate, COUNT(*) as CustomerCount
FROM iceberg.v4.customers_partitioned
GROUP BY OrderDate
ORDER BY OrderDate;

-- Đếm số khách hàng theo Status trong 1 ngày cụ thể
SELECT Status, COUNT(*) as Count
FROM iceberg.v4.customers_partitioned
WHERE OrderDate = DATE '2024-01-15'
GROUP BY Status;

-- =========================================================
-- MULTI-PARTITION EXAMPLE (NÂNG CAO)
-- =========================================================
-- Partition theo NHIỀU cột để tối ưu query phức tạp
-- Thứ tự partition: Cột query NHIỀU NHẤT đặt TRƯỚC
-- =========================================================

-- Drop table nếu tồn tại
-- DROP TABLE IF EXISTS iceberg.v4.customers_multi_partition;

-- Tạo table với 2 partitions: OrderDate + Status
CREATE TABLE IF NOT EXISTS iceberg.v4.customers_multi_partition (
    Id VARCHAR,
    Name VARCHAR,
    Email VARCHAR,
    OrderDate DATE,        -- PARTITION 1 (query nhiều nhất)
    Status VARCHAR,        -- PARTITION 2 (query thứ yếu)
    Amount DECIMAL(18,2)
)
WITH (
    format = 'PARQUET',
    partitioning = ARRAY['OrderDate', 'Status']  -- ✅ 2 PARTITIONS
);

-- Cấu trúc trên S3:
-- s3://bucket/customers_multi_partition/data/
--   ├── OrderDate=2024-01-15/
--   │   ├── Status=Active/
--   │   │   └── file1.parquet (100 customers)
--   │   ├── Status=Pending/
--   │   │   └── file2.parquet (50 customers)
--   │   └── Status=Inactive/
--   │       └── file3.parquet (20 customers)
--   └── OrderDate=2024-01-16/
--       ├── Status=Active/
--       │   └── file4.parquet (150 customers)
--       └── Status=Pending/
--           └── file5.parquet (30 customers)

-- Test Insert vào multi-partition
INSERT INTO iceberg.v4.customers_multi_partition 
VALUES 
    ('a1', 'John', 'john@test.com', DATE '2024-01-15', 'Active', 100.50),
    ('a2', 'Jane', 'jane@test.com', DATE '2024-01-15', 'Pending', 200.75),
    ('a3', 'Bob', 'bob@test.com', DATE '2024-01-16', 'Active', 150.00);

-- ✅ Query theo CẢ 2 partitions (CỰC NHANH - scan 1 folder)
SELECT * FROM iceberg.v4.customers_multi_partition
WHERE OrderDate = DATE '2024-01-15' AND Status = 'Active';
-- → Scan: OrderDate=2024-01-15/Status=Active/ (100 customers)

-- ✅ Query theo partition đầu tiên (NHANH - scan nhiều folders)
SELECT * FROM iceberg.v4.customers_multi_partition
WHERE OrderDate = DATE '2024-01-15';
-- → Scan: Active/, Pending/, Inactive/ (170 customers)

-- ⚠️ Query theo partition thứ 2 (CHẬM - scan toàn bộ dates)
SELECT * FROM iceberg.v4.customers_multi_partition
WHERE Status = 'Active';
-- → Scan: TẤT CẢ OrderDate=*/Status=Active/ (250 customers)

-- 💡 KẾT LUẬN: Đặt cột query NHIỀU NHẤT ở partition ĐẦU TIÊN!

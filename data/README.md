# Test Data - CSV Files

## Files

- `customers_2024_01.csv` - ID từ 1-5
- `customers_2024_02.csv` - ID từ 6-10
- `customers_2024_03.csv` - ID từ 11-15

## Cách dùng

### Bước 1: Upload lên MinIO S3

**Option A: Qua MinIO Console (Dễ nhất)**
1. Mở browser: http://192.168.100.17:9000
2. Login: `admin` / `12345678`
3. Vào bucket `tn3`
4. Tạo folder `uploads/`
5. Upload 3 file CSV

**Option B: Qua AWS CLI**
```powershell
# Install AWS CLI nếu chưa có
# winget install Amazon.AWSCLI

# Config
aws configure
# AWS Access Key: admin
# AWS Secret Key: 12345678
# Region: us-east-1

# Upload
aws s3 cp customers_2024_01.csv s3://tn3/uploads/ --endpoint-url http://192.168.100.17:9000
aws s3 cp customers_2024_02.csv s3://tn3/uploads/ --endpoint-url http://192.168.100.17:9000
aws s3 cp customers_2024_03.csv s3://tn3/uploads/ --endpoint-url http://192.168.100.17:9000
```

### Bước 2: Tạo Iceberg table từ CSV

```sql
-- Connect to Trino
docker exec -it trino trino

-- Create table from CSV files
CREATE TABLE iceberg.testdb.all_customers
WITH (
    format = 'PARQUET'
)
AS
SELECT 
    CAST(id AS BIGINT) as id,
    name,
    email
FROM (
    SELECT * FROM TABLE(
        read_csv('s3://tn3/uploads/customers_2024_01.csv',
                 columns => ARRAY['id', 'name', 'email'],
                 header => true)
    )
    UNION ALL
    SELECT * FROM TABLE(
        read_csv('s3://tn3/uploads/customers_2024_02.csv',
                 columns => ARRAY['id', 'name', 'email'],
                 header => true)
    )
    UNION ALL
    SELECT * FROM TABLE(
        read_csv('s3://tn3/uploads/customers_2024_03.csv',
                 columns => ARRAY['id', 'name', 'email'],
                 header => true)
    )
);
```

### Bước 3: Query data

```sql
-- Query tất cả
SELECT * FROM iceberg.testdb.all_customers;

-- Query với filter
SELECT * FROM iceberg.testdb.all_customers WHERE id < 8;

-- Count
SELECT COUNT(*) FROM iceberg.testdb.all_customers;

-- Xem file path (hidden column)
SELECT id, name, email, "$path" 
FROM iceberg.testdb.all_customers 
WHERE id = 1;
```

## Kết quả mong đợi

- ✅ 3 file CSV → Trino đọc
- ✅ Data được convert sang Parquet
- ✅ Lưu trên S3 tại: `s3://tn3/iceberg-warehouse/testdb/all_customers/`
- ✅ Query được 15 records (1-15)

## Notes

**Tại sao dùng CSV thay vì Excel (.xlsx)?**
- CSV đơn giản hơn
- Trino hỗ trợ tốt hơn
- Excel .xlsx cần connector phức tạp hơn

**Muốn test với Excel thật:**
1. Mở file CSV bằng Excel
2. Save As → Excel Workbook (.xlsx)
3. Nhưng vẫn cần convert về CSV khi load vào Trino

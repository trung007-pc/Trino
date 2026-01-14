# So sánh PostgreSQL vs Trino + Iceberg + S3

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
---


```
### Giải thích: Tại sao có 2 thư mục METADATA/ và DATA/ trên S3?

#### 📋 Thư mục METADATA/ - "Quyển sổ chỉ mục"

**Chứa gì?**
```
s3://bucket/warehouse/customers/metadata/
├── v1.metadata.json        (Schema hiện tại: cột gì, kiểu gì)
├── v2.metadata.json        (Schema version 2)
├── snap-001.avro           (Snapshot 1: danh sách file data)
├── snap-002.avro           (Snapshot 2: sau khi INSERT thêm)
└── manifest-list-xxx.avro  (Danh sách các manifest files)
```

**Kích thước:** Rất nhỏ (KB đến vài MB)

**Nội dung ví dụ:**
```json
{
  "schema": [
    {"id": 1, "name": "id", "type": "bigint"},
    {"id": 2, "name": "name", "type": "string"},
    {"id": 3, "name": "email", "type": "string"}
  ],
  "partition-spec": ["day(created_at)"],
  "current-snapshot-id": 12345,
  "snapshots": [
    {
      "snapshot-id": 12345,
      "manifest-list": "s3://bucket/.../manifest-list-001.avro"
    }
  ]
}
```

**Vai trò:**
- ✅ Lưu cấu trúc bảng (schema)
- ✅ Danh sách file data nằm ở đâu
- ✅ Lịch sử snapshots (time travel)
- ✅ Partition information

---

#### 🗄️ Thư mục DATA/ - "Kho hàng thực tế"

**Chứa gì?**
```
s3://bucket/warehouse/customers/data/
├── part-00001.parquet  (100GB - Dữ liệu tháng 1)
├── part-00002.parquet  (100GB - Dữ liệu tháng 2)
├── part-00003.parquet  (100GB - Dữ liệu tháng 3)
└── ...
└── part-99999.parquet  (100GB - Dữ liệu tháng N)
```

**Kích thước:** RẤT LỚN (GB, TB, PB)

**Nội dung:** Dữ liệu thực tế của bảng (hàng triệu/tỷ dòng)

**Format:** Parquet (compressed, columnar format)

---

#### 🎯 Tại sao phải tách biệt?

| Lý do | Giải thích |
|-------|------------|
| **1. Hiệu năng** | Trino chỉ đọc METADATA nhỏ (vài KB) để biết cần đọc file DATA nào, thay vì scan toàn bộ TB data |
| **2. Time Travel** | METADATA lưu snapshots → Query data của 1 tuần trước chỉ cần đổi snapshot-id |
| **3. Schema Evolution** | Thêm cột mới? → Chỉ sửa METADATA (1 giây), không động vào DATA (TB) |
| **4. Partition Pruning** | METADATA nói: "Dữ liệu ngày 1/1 ở file1.parquet" → Trino chỉ đọc file đó, bỏ qua 999 file khác |

---

#### 🔄 Flow Query cụ thể

```sql
SELECT * FROM customers WHERE created_at = '2024-01-01'
```

**Bước 1:** Trino đọc `METADATA/v1.metadata.json` (5KB)
```
→ Biết được: Bảng có 3 cột (id, name, email)
→ Partition theo day(created_at)
→ Snapshot hiện tại: 12345
```

**Bước 2:** Trino đọc `METADATA/snap-12345.avro` (100KB)
```
→ Có 1000 file data
→ File cho ngày 2024-01-01: part-00001.parquet
```

**Bước 3:** Trino chỉ đọc `DATA/part-00001.parquet` (100GB)
```
→ Bỏ qua 999 file khác (tiết kiệm 99.9TB I/O!)
```

---

### 💡 So sánh với PostgreSQL

| PostgreSQL | Iceberg trên S3 |
|------------|-----------------|
| Data và Metadata gộp chung trong 1 file `.pg` | METADATA (KB) và DATA (TB) tách rời |
| Đổi schema = Lock bảng, rewrite data | Đổi schema = Sửa METADATA (1 giây) |
| Time travel = Cần extension (pgaudit) | Time travel = Built-in (snapshots) |
| Partition pruning = Cần index | Partition pruning = Tự động (metadata) |

---

Luồng hoạt động:
1. User gửi SQL → Trino
2. Trino đọc Iceberg metadata từ S3: "Bảng customers có những file nào?"
3. Metadata trả về: "1000 file parquet ở s3://bucket/data/..."
4. Trino đọc 1000 file song song từ S3 bằng nhiều workers
5. Trino xử lý và trả kết quả về cho User
```

### Giới hạn lưu trữ
**Gần như vô hạn:** Có thể lên tới hàng trăm **Petabytes** ($1024 \times 1024$ GB) hoặc **Exabytes**. Giới hạn duy nhất là khả năng chi trả cho dung lượng lưu trữ trên Cloud của bạn.

## Chốt lại

✅ **Dữ liệu dưới 1 TB + cần giao dịch nhanh:** Chọn PostgreSQL.

✅ **Dữ liệu trên 10 TB + chỉ dùng để phân tích/báo cáo:** Chọn Trino + Iceberg + S3.

---

## PostgreSQL có vai trò gì trong Trino + Iceberg + S3?

### Câu trả lời ngắn gọn: **CÓ 3 trường hợp**

#### 1️⃣ Trường hợp 1: KHÔNG CẦN PostgreSQL (Pure Iceberg)
```
User → Trino → Iceberg (metadata trên S3) → S3 (data)
```
✅ Iceberg tự quản lý metadata trên S3 (không cần PostgreSQL)
✅ Setup đơn giản nhất
✅ Phù hợp: Data lake thuần túy

---

#### 2️⃣ Trường hợp 2: PostgreSQL làm Hive Metastore Backend (Optional)
```
User → Trino → Hive Metastore (dùng PostgreSQL lưu metadata) → S3 (data)
```

**PostgreSQL vai trò:** Lưu metadata của Iceberg thay vì lưu trên S3

**Tại sao dùng?**
- ✅ Query metadata nhanh hơn (PostgreSQL nhanh hơn đọc file JSON trên S3)
- ✅ Quản lý transactions metadata tốt hơn
- ✅ Nhiều công cụ cần Hive Metastore (Spark, Flink)

**Lưu ý:**
- PostgreSQL chỉ lưu **metadata** (schema, partition info)
- **Data thực tế** vẫn trên S3
- PostgreSQL này rất nhỏ (< 1GB), không chứa data analytics

---

#### 3️⃣ Trường hợp 3: Hybrid Architecture (OLTP + OLAP)
```
┌──────────────┐           ┌──────────────────────┐
│  PostgreSQL  │───ETL────→│ Trino + Iceberg + S3 │
│  (OLTP)      │ (nightly) │ (OLAP)               │
└──────────────┘           └──────────────────────┘
     ↓                              ↓
 App/Web                      Analytics/BI
```

**PostgreSQL vai trò:** Lưu data OLTP (giao dịch thời gian thực)

**Flow:**
1. User mua hàng → Ghi vào **PostgreSQL** (nhanh)
2. Mỗi đêm → ETL copy data sang **Iceberg S3**
3. Analytics team query **Trino** (data lịch sử, không ảnh hưởng PostgreSQL)

**Lợi ích:**
- PostgreSQL xử lý transactions (INSERT/UPDATE/DELETE)
- Trino xử lý analytics (SUM, AVG trên TB data)
- Tách biệt workload → hiệu năng tốt hơn

---

### Tóm lại: PostgreSQL trong Trino ecosystem

| Vai trò | Bắt buộc? | Mục đích |
|---------|-----------|----------|
| **Không dùng** | ❌ Không | Pure Iceberg trên S3 |
| **Metastore backend** | ⚠️ Optional | Lưu metadata thay S3 (< 1GB) |
| **OLTP database** | ⚠️ Optional | Hybrid: OLTP (PostgreSQL) + OLAP (Trino) |

💡 **Kết luận:** Trino + Iceberg + S3 hoạt động **HOÀN TOÀN ĐỘC LẬP** không cần PostgreSQL!

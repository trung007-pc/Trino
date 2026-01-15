# Kafka - Hướng dẫn căn bản (So sánh với RabbitMQ)

## Mục lục
- [Producer & Consumer](#producer--consumer)
- [Topic](#topic)
- [Partition (Phân vùng)](#partition-phân-vùng)
- [Partition Key](#partition-key)
- [Offset](#offset)
- [So sánh Kafka vs RabbitMQ](#so-sánh-kafka-vs-rabbitmq)

---

## **Producer & Consumer**

### Producer (Nhà sản xuất)
- **Vai trò:** Gửi message vào Kafka
- **Tương đương RabbitMQ:** Publisher
- **Đặc điểm:**
  - Chỉ định topic để gửi message
  - Có thể chỉ định partition key để kiểm soát phân phối
  - Không cần biết consumer là ai

### Consumer (Người tiêu thụ)
- **Vai trò:** Đọc message từ Kafka
- **Tương đương RabbitMQ:** Consumer
- **Đặc điểm:**
  - Tự quản lý vị trí đọc (offset)
  - Có thể thuộc về một consumer group
  - Có thể đọc lại message cũ (rewind)

**Ví dụ:**
```
Producer → [Topic: orders] → Consumer
```

---

## **Topic**

### Khái niệm
- **Topic** = kênh/danh mục chứa các message cùng loại
- **Tương đương RabbitMQ:** Kết hợp giữa Exchange và Queue
- Message được tổ chức theo topic để dễ quản lý

### Đặc điểm
- Một Kafka cluster có thể có nhiều topic
- Mỗi topic có tên duy nhất
- Topic được lưu trữ lâu dài (theo cấu hình retention)

**Ví dụ:**
```
Topic: orders      → Message về đơn hàng
Topic: users       → Message về người dùng  
Topic: payments    → Message về thanh toán
```

---

## **Partition (Phân vùng)**

### Khái niệm
- Mỗi topic được chia thành **nhiều partition**
- Mỗi partition là một hàng đợi độc lập, có thứ tự
- **Khác biệt lớn:** RabbitMQ không có khái niệm partition
- **Mặc định:** Khi tạo topic mới sẽ có **ít nhất 1 partition** (có thể config nhiều hơn)

### 🎯 **Cách hiểu đơn giản:**
```
1 Topic có nhiều Partition
→ Mỗi Partition chứa nhiều Message
→ Nhiều Partition = Nhiều Consumer xử lý SONG SONG

Ví dụ:
Topic: orders
├── Partition 0: [msg1, msg4, msg7, msg10, ...] → Consumer 1
├── Partition 1: [msg2, msg5, msg8, msg11, ...] → Consumer 2
└── Partition 2: [msg3, msg6, msg9, msg12, ...] → Consumer 3

→ 3 consumers xử lý cùng lúc, KHÔNG chờ nhau
```

**⚠️ Lưu ý quan trọng:**
- **1 Partition = Tối đa 1 Consumer** (trong cùng consumer group)
- Số Partition KHÔNG ảnh hưởng số lượng message (chỉ ảnh hưởng tốc độ xử lý)
- Tốt nhất: Số Partitions = Số Consumers để tối ưu

### Tại sao cần Partition?

#### 1. **Tăng hiệu suất (Parallelism)**
```
Topic: orders (3 partitions)
├── Partition 0 → Consumer 1
├── Partition 1 → Consumer 2
└── Partition 2 → Consumer 3

→ 3 consumer xử lý song song = Nhanh gấp 3 lần
```

#### 2. **Mở rộng dễ dàng (Scalability)**
- Thêm partition = thêm khả năng xử lý
- Phân tán trên nhiều server

#### 3. **Đảm bảo thứ tự (Ordering)**
- Thứ tự được đảm bảo **trong cùng partition**
- Giữa các partition không có đảm bảo thứ tự

### Quy tắc: 1 Partition = 1 Consumer (trong cùng group)

```
✅ ĐÚNG:
3 partitions → 3 consumers (lý tưởng, tối ưu)
3 partitions → 2 consumers (consumer 1 xử lý 2 partitions, consumer 2 xử lý 1 partition)
3 partitions → 1 consumer  (1 consumer xử lý cả 3 partitions, chậm)

❌ LÃNG PHÍ:
3 partitions → 5 consumers (2 consumer sẽ idle, không làm gì)

💡 KẾT LUẬN:
Số consumers <= Số partitions
Tốt nhất: Số consumers = Số partitions
```

### Cách message được phân bổ vào Partition

Kafka có **3 cách** phân bổ message vào partition:

#### **1️⃣ CÓ Partition Key (Khuyên dùng khi cần đảm bảo thứ tự)**

```csharp
// Producer gửi message với partition key
producer.Send(new Message {
    Topic = "orders",
    Key = "user_123",        // ← Partition Key
    Value = "order data..."
});

// Kafka tính: hash("user_123") % số_partition = partition_number
// → Cùng key → Luôn vào CÙNG partition → Đảm bảo thứ tự
```

**Ví dụ với 3 partitions:**
```
Message {key: "user_123"} → hash % 3 = 1 → Partition 1
Message {key: "user_123"} → hash % 3 = 1 → Partition 1  ← Cùng partition
Message {key: "user_456"} → hash % 3 = 2 → Partition 2
Message {key: "user_789"} → hash % 3 = 0 → Partition 0
Message {key: "user_123"} → hash % 3 = 1 → Partition 1  ← Vẫn cùng partition
```

#### **2️⃣ KHÔNG có Partition Key (Round-robin - Xoay vòng)**

```csharp
// Producer gửi message KHÔNG có key
producer.Send(new Message {
    Topic = "orders",
    Key = null,              // ← KHÔNG có key
    Value = "order data..."
});

// → Kafka phân bổ đều theo round-robin (xoay vòng)
// → Phân tán đều, tăng throughput
```

**Ví dụ với 3 partitions:**
```
Message 1 (no key) → Partition 0  ← Round-robin
Message 2 (no key) → Partition 1
Message 3 (no key) → Partition 2
Message 4 (no key) → Partition 0  ← Lặp lại
Message 5 (no key) → Partition 1
Message 6 (no key) → Partition 2
```

#### **3️⃣ CHỈ ĐỊNH Partition cụ thể (Ít dùng)**

```csharp
// Producer chỉ định partition cụ thể
producer.Send(new Message {
    Topic = "orders",
    Partition = 2,           // ← Chỉ định partition số 2
    Value = "order data..."
});

// → Message luôn vào Partition 2
// → Không khuyên dùng (mất tính linh hoạt)
```

#### **So sánh 3 cách:**

| Cách | Khi nào dùng | Ưu điểm | Nhược điểm |
|------|--------------|---------|------------|
| **Có Key** | Cần đảm bảo thứ tự theo entity | Đảm bảo thứ tự, dự đoán được | Có thể bị hotspot nếu key không đều |
| **Không Key** | Không quan trọng thứ tự | Phân bổ đều, throughput cao | Không đảm bảo thứ tự |
| **Chỉ định Partition** | Hiếm khi cần | Kiểm soát hoàn toàn | Mất tính tự động, khó scale |

#### **Ví dụ thực tế:**

**E-commerce - CÓ KEY:**
```csharp
// ✅ Đảm bảo orders của cùng user đúng thứ tự
producer.Send(new Message {
    Topic = "orders",
    Key = userId,           // user_123
    Value = orderData
});
// → Tất cả orders của user_123 vào cùng partition
// → Xử lý đúng thứ tự: order1 → order2 → order3
```

**Logging - KHÔNG KEY:**
```csharp
// ✅ Phân bổ đều, tăng throughput
producer.Send(new Message {
    Topic = "logs",
    Key = null,             // Không cần key
    Value = logData
});
// → Log phân bổ đều các partition
// → Xử lý nhanh, không quan trọng thứ tự
```

---

## **Partition Key**

### Khái niệm
- **Partition Key** = Khóa để xác định message vào partition nào
- **Tương đương RabbitMQ:** Routing Key (nhưng khác mục đích)

### Cách hoạt động
```
Producer gửi message:
{
  "partitionKey": "user_123",
  "data": {...}
}

Kafka tính toán:
partition_number = hash("user_123") % số_partition

→ Message vào partition cố định
```

### Khi nào dùng Partition Key?

#### ✅ **CÓ Partition Key**
**Use case:** Đảm bảo thứ tự xử lý cho từng entity

```
// Tất cả giao dịch của user 123 vào cùng partition
{userId: "123", action: "login"}     → Partition 1
{userId: "123", action: "buy"}       → Partition 1
{userId: "123", action: "logout"}    → Partition 1

→ Đảm bảo xử lý đúng thứ tự: login → buy → logout
```

**Ví dụ thực tế:**
- **E-commerce:** Partition key = userId → đảm bảo orders của cùng user đúng thứ tự
- **Banking:** Partition key = accountId → đảm bảo transactions đúng thứ tự
- **IoT:** Partition key = deviceId → đảm bảo events từ cùng thiết bị đúng thứ tự

#### ❌ **KHÔNG Partition Key**
**Use case:** Không cần đảm bảo thứ tự, chỉ cần xử lý nhanh

```
Message 1 → Partition 0 (random/round-robin)
Message 2 → Partition 2
Message 3 → Partition 1

→ Phân bổ đều, tăng throughput
```

**Ví dụ thực tế:**
- **Logging:** Không quan trọng log nào xử lý trước
- **Metrics:** Chỉ cần thu thập dữ liệu
- **Notifications:** Email/SMS không cần thứ tự

---

## **Offset**

### Khái niệm
- **Offset** = Số thứ tự của message trong partition (0, 1, 2, 3...)
- Mỗi partition có offset riêng, bắt đầu từ 0
- **Khác biệt lớn:** RabbitMQ xóa message sau khi đọc, Kafka giữ lại

### Cách hoạt động

```
Partition 0:
┌────┬────┬────┬────┬────┬────┐
│ 0  │ 1  │ 2  │ 3  │ 4  │ 5  │
└────┴────┴────┴────┴────┴────┘
       ↑              ↑
   offset=1      offset=4
   (message)    (consumer đang đọc)
```

### Offset được lưu ở đâu?

**🗄️ Kafka lưu offset trong topic nội bộ:**
```
Topic đặc biệt: __consumer_offsets
→ Lưu offset của TẤT CẢ consumer groups
→ Được Kafka tự động quản lý
```

**Cấu trúc dữ liệu:**
```json
{
  "group_id": "order-processing-group",
  "topic": "orders",
  "partition": 0,
  "offset": 1523,
  "timestamp": "2026-01-13T10:30:00Z"
}
```

### Consumer tự quản lý Offset

**Consumer Group A:**
```
Đọc đến offset 10 → Xử lý → Commit offset 10
→ Ghi vào __consumer_offsets
Crash → Khởi động lại → Đọc offset từ __consumer_offsets
→ Tiếp tục từ offset 10
```

**Consumer Group B:**
```
Cùng topic, nhưng offset riêng = 50
→ Hai group độc lập, có thể xử lý cùng data
→ Mỗi group có record riêng trong __consumer_offsets
```

### Flow khi Consumer restart:
```
Consumer crash tại offset 73
         ↓
Restart & kết nối Kafka
         ↓
Yêu cầu offset từ __consumer_offsets
         ↓
Kafka trả về: offset = 73
         ↓
Tiếp tục đọc từ offset 74
→ KHÔNG mất message
```

### Lợi ích của Offset

#### 1. **Replay message (Đọc lại)**
```
Consumer đã đọc đến offset 100
Phát hiện lỗi logic → Reset về offset 50
→ Xử lý lại 50 message
```

#### 2. **Fault tolerance (Chịu lỗi)**
```
Consumer crash tại offset 73
→ Khởi động lại
→ Tiếp tục từ offset 73 (không mất message)
```

#### 3. **Multiple consumer groups**
```
Topic: orders

Consumer Group: analytics  (offset=1000)
Consumer Group: billing    (offset=500)
Consumer Group: shipping   (offset=800)

→ Mỗi group xử lý cùng data theo nhu cầu riêng
```

### Commit Offset

**1️⃣ Auto Commit (Tự động):**
```csharp
var config = new ConsumerConfig {
    GroupId = "my-group",
    EnableAutoCommit = true,           // ← Tự động commit
    AutoCommitIntervalMs = 5000        // ← Mỗi 5 giây
};

// Kafka tự động commit offset mỗi 5 giây
// → Ghi vào __consumer_offsets
```

**⚠️ Rủi ro của Auto Commit:**
```
T=0s: Đọc message offset 100
T=2s: Xử lý message...
T=3s: Consumer crash (chưa xử lý xong)
T=5s: (Lẽ ra commit nhưng đã crash)

→ Restart: Đọc lại từ offset 95 (commit lần trước)
→ Xử lý lại message 95-100 (duplicate)
```

**2️⃣ Manual Commit (Thủ công - Khuyên dùng):**
```csharp
var config = new ConsumerConfig {
    GroupId = "my-group",
    EnableAutoCommit = false           // ← Tắt auto commit
};

while (true) {
    var message = consumer.Consume();  // Đọc message
    
    ProcessMessage(message);           // Xử lý xong
    
    consumer.Commit(message);          // ← Manual commit
    // → Ghi offset vào __consumer_offsets
}
```

**✅ An toàn hơn:**
```
Đọc message offset 100
→ Xử lý xong
→ Commit offset 100 → Ghi vào __consumer_offsets
→ Nếu crash → Restart từ offset 101 (không duplicate)
```

---

## **Message Retention & Storage**

### 🎯 **Message sau khi đọc có mất không?**

**KHÔNG! Message KHÔNG mất sau khi consumer đọc**

Đây là **KHÁC BIỆT LỚN** giữa Kafka và RabbitMQ:

```
RabbitMQ:
Consumer đọc → ACK → Message BỊ XÓA ngay
→ Không đọc lại được

Kafka:
Consumer đọc → Commit offset → Message VẪN CÒN
→ Có thể đọc lại nhiều lần
→ Chỉ bị xóa khi HẾT hạn lưu trữ (retention)
```

### Kafka lưu trữ message như thế nào?

**Lưu trữ trên Disk (Ổ cứng):**
```
Kafka lưu message vào file trên disk:
/var/lib/kafka/data/
├── orders-0/           ← Partition 0 của topic orders
│   ├── 00000000000000000000.log  ← File chứa message
│   ├── 00000000000000000000.index
│   └── 00000000000000000000.timeindex
└── orders-1/           ← Partition 1 của topic orders
    ├── 00000000000000000000.log
    └── ...
```

**Message được lưu cho đến khi:**
- Hết thời gian lưu trữ (retention time)
- Hoặc hết dung lượng cho phép (retention size)

### Retention Policy (Chính sách lưu trữ)

#### **1️⃣ Retention Time (Thời gian lưu trữ)**

```properties
# Mặc định: 7 ngày (168 giờ)
retention.ms = 604800000

# Các config phổ biến:
retention.ms = 86400000      # 1 ngày
retention.ms = 604800000     # 7 ngày (mặc định)
retention.ms = 2592000000    # 30 ngày
retention.ms = -1            # VÔ THỜI HẠN (lưu mãi mãi)
```

**Cách hoạt động:**
```
Timeline:
Day 0: Message được ghi vào Kafka
Day 1-7: Message vẫn còn, có thể đọc lại
Day 8: Message BỊ XÓA (hết retention 7 ngày)
```

#### **2️⃣ Retention Size (Dung lượng lưu trữ)**

```properties
# Giới hạn theo dung lượng partition
retention.bytes = 1073741824  # 1GB per partition

# Không giới hạn
retention.bytes = -1
```

**Cách hoạt động:**
```
Partition size: 0MB → 500MB → 900MB → 1GB
                                      ↑
                               Đạt giới hạn
                                      ↓
                      Xóa message cũ nhất để giữ < 1GB
```

### Consumer đọc message nhiều lần

#### **Ví dụ 1: Nhiều Consumer Group**

```
Topic: orders (retention = 7 ngày)
├── Message 1 (Day 0)
├── Message 2 (Day 0)
└── Message 3 (Day 0)

Consumer Group A (Day 0): 
→ Đọc message 1, 2, 3 → Commit offset 3

Consumer Group B (Day 2):
→ Đọc message 1, 2, 3 → Commit offset 3
→ Message vẫn còn vì chưa hết 7 ngày

Consumer Group C (Day 5):
→ Đọc message 1, 2, 3 → Commit offset 3
→ Message vẫn còn

Day 8: Message 1, 2, 3 bị xóa (hết retention)
```

#### **Ví dụ 2: Replay (Đọc lại)**

```csharp
// Consumer đã đọc đến offset 1000
currentOffset = consumer.Position();  // 1000

// Phát hiện lỗi logic, cần xử lý lại
consumer.Seek(new TopicPartitionOffset(
    topic: "orders",
    partition: 0,
    offset: 500  // ← Quay lại offset 500
));

// Đọc lại từ offset 500 → 1000
// (Nếu message còn trong retention period)
```

### Config retention trong thực tế

#### **Short-term (Ngắn hạn)**
```properties
# Use case: Logs, metrics, temporary events
retention.ms = 86400000        # 1 ngày
retention.bytes = 1073741824   # 1GB
```

#### **Medium-term (Trung hạn)**
```properties
# Use case: Orders, transactions, user events
retention.ms = 2592000000      # 30 ngày
retention.bytes = -1           # Không giới hạn size
```

#### **Long-term / Event Sourcing**
```properties
# Use case: Audit trail, compliance, event store
retention.ms = -1              # VÔ THỜI HẠN
retention.bytes = -1           # Không giới hạn size

# Hoặc compaction (giữ message mới nhất của mỗi key)
cleanup.policy = compact
```

### Log Compaction (Nén)

Thay vì xóa message cũ, Kafka có thể **giữ message mới nhất** của mỗi key:

```
Before compaction:
Key: user_123, Value: {name: "John", age: 25}     ← offset 0
Key: user_456, Value: {name: "Jane", age: 30}     ← offset 1
Key: user_123, Value: {name: "John", age: 26}     ← offset 2
Key: user_123, Value: {name: "Johnny", age: 26}   ← offset 3

After compaction:
Key: user_456, Value: {name: "Jane", age: 30}     ← Giữ lại
Key: user_123, Value: {name: "Johnny", age: 26}   ← Giữ lại (mới nhất)

→ Message cũ của user_123 bị xóa
→ Message mới nhất được giữ MÃI MÃI
```

### Ví dụ thực tế: E-commerce

```
Topic: orders (retention = 30 ngày)

Day 1: 
- Order #1001 → Kafka
- Consumer "order-processing" xử lý → Commit offset
- Consumer "analytics" xử lý → Commit offset  
- Consumer "billing" xử lý → Commit offset
→ Order #1001 VẪN CÒN trong Kafka

Day 5:
- Cần phân tích lại data
→ Tạo consumer mới "data-migration"
→ Đọc từ offset 0 (từ đầu)
→ Lấy lại order #1001 (vì còn trong 30 ngày)

Day 15:
- Bug phát hiện trong order-processing
→ Reset offset về Day 1
→ Xử lý lại TẤT CẢ orders (vì còn trong 30 ngày)

Day 31:
→ Order #1001 BỊ XÓA (hết retention 30 ngày)
```

---

## **So sánh Kafka vs RabbitMQ**

### Bảng so sánh nhanh

| **Khái niệm** | **Kafka** | **RabbitMQ** | **Ghi chú** |
|---------------|-----------|--------------|-------------|
| **Producer** | Producer | Publisher | Tương đương |
| **Consumer** | Consumer | Consumer | Tương đương |
| **Topic** | Topic | Exchange + Queue | Kafka đơn giản hơn |
| **Partition** | Có (mỗi topic nhiều partition) | Không có | Lợi thế lớn của Kafka |
| **Partition Key** | Có | Routing Key | Mục đích khác nhau |
| **Offset** | Consumer tự quản lý (__consumer_offsets) | Broker quản lý | Kafka linh hoạt hơn |
| **Message sau khi đọc** | VẪN CÒN (theo retention) | BỊ XÓA ngay | Khác biệt lớn |
| **Lưu trữ** | Trên DISK | Trên RAM (hoặc disk) | Kafka bền vững hơn |
| **Message retention** | 7 ngày mặc định (config được) | Xóa sau khi ACK | Kafka như log store |
| **Replay** | ✅ Có thể đọc lại | ❌ Không thể | Kafka mạnh hơn |
| **Nhiều consumer** | ✅ Nhiều group đọc cùng data | ❌ 1 message = 1 consumer | Kafka linh hoạt hơn |
| **Throughput** | Rất cao (millions/sec) | Trung bình | Kafka cho big data |
| **Latency** | Cao hơn (ms) | Thấp hơn (µs) | RabbitMQ nhanh hơn |

### Khi nào dùng Kafka?

✅ **Phù hợp:**
- **High throughput:** Xử lý hàng triệu message/giây
- **Event sourcing:** Lưu trữ lịch sử sự kiện
- **Stream processing:** Xử lý luồng dữ liệu real-time
- **Log aggregation:** Thu thập log từ nhiều nguồn
- **Replay events:** Cần đọc lại dữ liệu cũ

**Ví dụ:**
- Uber: Tracking vị trí xe real-time
- Netflix: Thu thập viewing metrics
- LinkedIn: Activity stream

### Khi nào dùng RabbitMQ?

✅ **Phù hợp:**
- **Low latency:** Cần phản hồi tức thì
- **Complex routing:** Routing logic phức tạp
- **Request/Reply:** Pattern RPC
- **Priority queue:** Ưu tiên message
- **Dead letter queue:** Xử lý message lỗi

**Ví dụ:**
- Task queue cho background jobs
- Microservices communication
- Email/SMS notifications

---

## **Ví dụ thực tế**

### Hệ thống E-commerce

```
Topic: orders (3 partitions)

Producer (Website):
  order_123 → {userId: "user_1", amount: 100} → Partition 0
  order_124 → {userId: "user_2", amount: 200} → Partition 1
  order_125 → {userId: "user_1", amount: 150} → Partition 0

Consumer Group: order-processing
  Consumer 1 → Partition 0 → Xử lý order_123, order_125
  Consumer 2 → Partition 1 → Xử lý order_124
  Consumer 3 → Partition 2 → Idle

Consumer Group: analytics
  Consumer 1 → Partition 0, 1, 2 → Phân tích tất cả orders
```

### Banking System

```
Topic: transactions (10 partitions)

Partition Key: accountId
- Đảm bảo tất cả giao dịch của cùng tài khoản vào 1 partition
- Đảm bảo thứ tự: deposit → withdraw → transfer

Offset:
- Consumer xử lý transaction, commit offset
- Nếu fail → rollback, đọc lại từ offset cũ
- Đảm bảo exactly-once processing
```

---

## **Best Practices**

### 1. Số lượng Partition
```
Số partition = Số consumer tối đa cần

Ví dụ:
- Throughput cần: 1M messages/sec
- 1 consumer xử lý: 100K messages/sec
→ Cần 10 partitions (và 10 consumers)
```

### 2. Chọn Partition Key
```
✅ TỐT: userId, orderId, deviceId
→ Đảm bảo thứ tự cho từng entity

❌ TỆ: timestamp, random
→ Phân bổ không đều, hotspot
```

### 3. Commit Offset
```csharp
// ❌ TỆ: Auto commit trước khi xử lý
EnableAutoCommit = true;
var msg = consumer.Consume();
ProcessMessage(msg); // Nếu fail, message bị mất

// ✅ TỐT: Manual commit sau khi xử lý
EnableAutoCommit = false;
var msg = consumer.Consume();
ProcessMessage(msg);
consumer.Commit(); // Đảm bảo đã xử lý xong
```

### 4. Retention Policy
```
// Short-term: 1-7 ngày
retention.ms = 604800000 (7 days)

// Long-term: Vô thời hạn (event sourcing)
retention.ms = -1 (infinite)
```

---

## **Tổng kết**

### Các khái niệm quan trọng

1. **Producer/Consumer:** Giống RabbitMQ, vai trò gửi/nhận message
2. **Topic:** Kênh chứa message, như queue nhưng mạnh hơn
3. **Partition:** Chia topic thành nhiều luồng song song → tăng performance
4. **Partition Key:** Đảm bảo message cùng key vào cùng partition → đúng thứ tự
5. **Offset:** Vị trí message, consumer tự quản lý → có thể replay

### Ưu điểm Kafka
- 🚀 **Throughput cực cao**
- 💾 **Lưu trữ dài hạn**
- 🔄 **Replay events**
- 📊 **Stream processing**
- 🔧 **Horizontal scaling**

### Nhược điểm Kafka
- 🐢 **Latency cao hơn RabbitMQ**
- 🔧 **Setup phức tạp hơn**
- 📚 **Learning curve cao hơn**

---

## **Tài liệu tham khảo**

- [Apache Kafka Documentation](https://kafka.apache.org/documentation/)
- [Confluent Kafka Guide](https://docs.confluent.io/)
- [Kafka vs RabbitMQ](https://www.cloudamqp.com/blog/when-to-use-rabbitmq-or-apache-kafka.html)

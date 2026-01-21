ETL dữ liệu XML theo chuẩn 3176, 4750, 130 để nạp vào CSDL Khám Chữa Bệnh Hà Nội (CSDL KCB HN). Phục vụ mục đích dashboard.

# Context diagram
```mermaid
graph TD;
    A[HIS/EMR] -->|Gửi XML| B[Dataflow];
    A -->|Gửi XML| C[CSDL KCB HN];
    B -->|Gửi XML| C;
```

# Dataflow diagram

```mermaid
graph TD;

    %% ==========================================
    %% TẦNG NGUỒN (SOURCE SYSTEM)
    %% ==========================================
    subgraph Source ["Hệ thống nguồn"]
        A[HIS/EMR] -->|PA1: Xml 3176| B[Dataflow]
    end

    %% ==========================================
    %% TẦNG TIẾP NHẬN & LƯU TRỮ THÔ (BRONZE)
    %% ==========================================
    subgraph Bronze ["Tầng Bronze (Lưu trữ thô)"]
        A -->|PA2: Xml 3176| D
        D[TiepNhanXml3176]
        D -->|Lưu file XML gốc| E[S3]
        D -->|Lưu bản ghi đối soát| I["Table đối soát<br>(Postgres-lưu tạm)"]
        D -->|Publish task| F[Queue Xử lý XML]
        
        %% --- PHẦN CHỈNH SỬA THEO YÊU CẦU ---
        I -->|Đọc data| W["Worker đối soát"]
        W -->|Sync data| Q[IceBerg]
        %% ----------------------------------
        O["TrangThaiDoiSoat<br/>(Kiểm tra thành công/thất bại<br/>hồ sơ)"]
        O -->|Move file thành công/thất bại| E
    end

    %% ==========================================
    %% TẦNG XỬ LÝ & LƯU TRỮ CHUẨN HÓA (SILVER)
    %% ==========================================
    subgraph Silver ["Tầng Silver (Chuẩn hóa)"]
        F -->|Consume task| G["Worker ETL xử lý XML<br/>(validate, verify,<br> enrich)"]
        E -->|Get file XML| G
        G -->|Publish data 15 bảng| Y[15 Queue Insert data]
        Y -->|Consume| H["Worker insert data"]
        
        %% Queue Đối soát
        N[Queue Đối soát]
        
        %% Publish event cập nhật trạng thái
        G -->|Publish event đã xử lý XML, metadata của Hồ sơ| N
        H -->|Publish event đã insert Thành công/thất bại số bản ghi| N
        
        %% Consume từ Queue Đối soát
        N -->|Consume| M["XuLyTrungLap<br/>(Xử lý trùng lặp bản ghi)"]
        N -->|Consume| O
        O -->|Publish event Hồ sơ thành công/thất bại| N
        H -->|Insert/Update| Z["Iceberg (DB Silver)"]
    end

    %% ==========================================
    %% TẦNG TỔNG HỢP & BÁO CÁO (GOLD)
    %% ==========================================
    subgraph Gold ["Tầng Gold (Báo cáo)"]
        Z -->|Tổng hợp dữ liệu| J[DBT]
        J -->|Insert/Update| K["Postgre (DB Gold)"]
        K -->|Đọc DB hiển thị báo cáo| L["Metabase"] 
    end

    %% Ghi chú các luồng chính
    B -->|XML 3176| D
    
    %% Định nghĩa màu sắc để dễ phân biệt
    style Source fill:#f9f,stroke:#333,stroke-width:2px
    style Bronze fill:#e1f5fe,stroke:#01579b
    style Silver fill:#fff3e0,stroke:#e65100
    style Gold fill:#e8f5e9,stroke:#2e7d32
    style M fill:#fff9c4,stroke:#fbc02d,stroke-dasharray: 5 5
    style W fill:#bbf,stroke:#333,stroke-width:2px
```

# Mô hình cài đặt
## Cài đặt Iceberg, Trino, DBT
```mermaid
graph TD;
    subgraph "Load Balancing Layer"
        LB[HAProxy / Nginx]
    end

    subgraph "Storage Layer (MinIO Distributed - Iceberg)"
        M1[MinIO Node 1]
        M2[MinIO Node 2]
        M3[MinIO Node 3]
        M4[MinIO Node 4]
    end

    subgraph "Metadata Layer"
        HMS[Hive Metastore / Rest Catalog]
        DB_META[(PostgreSQL - Metadata DB)]
    end

    subgraph "Query Layer (Trino)"
        TC[Trino Coordinator]
        TW1[Trino Worker 1]
        TW2[Trino Worker 2]
    end

    subgraph "Transformation Layer (dbt)"
        DBT[dbt Core / dbt Project]
    end

    subgraph "Target Layer (PostgreSQL Cluster)"
        PG_M[(PostgreSQL Master)]
        PG_S[(PostgreSQL Slave)]
    end

    %% Flow dữ liệu lưu trữ
    LB --> M1 & M2 & M3 & M4
    
    %% Trino tương tác Metadata & Storage
    TC & TW1 & TW2 --> HMS
    HMS --> DB_META
    TC & TW1 & TW2 --> LB

    %% dbt Điều phối
    DBT -- "1. Query Iceberg via SQL" --> TC
    DBT -- "2. Write Aggregated Data" --> PG_M
    
    %% Replication
    PG_M -- "Streaming Replication" --> PG_S
```

## Cài đặt .NET service
```mermaid
graph TD
    subgraph K8s_Cluster ["Kubernetes Cluster"]
        direction TB
        
        %% Ingress Layer
        subgraph Ingress_Layer ["External Access"]
            LB[Load Balancer / Ingress Controller]
        end

        %% Workloads với nhiều Pods
        subgraph Workloads
            
            %% Service TiepNhan
            subgraph SVC_TN ["Service: TiepNhanXml"]
                TN_SVC((ClusterIP))
                subgraph Pods_TN ["Pods Replicas"]
                    TN1[Pod 1]
                    TN2[Pod 2]
                    TNn[Pod n...]
                end
            end

            %% Service ETL
            subgraph SVC_ETL ["Service: EtlXml"]
                ETL_SVC((ClusterIP))
                subgraph Pods_ETL ["Pods Replicas"]
                    ETL1[Pod 1]
                    ETL2[Pod 2]
                end
            end

            %% Service KCB
            subgraph SVC_KCB ["Service: KcbService"]
                KCB_SVC((ClusterIP))
                subgraph Pods_KCB ["Pods Replicas"]
                    KCB1[Pod 1]
                    KCB2[Pod 2]
                end
            end
        end
    end

    %% Kết nối Load Balancing
    LB --> TN_SVC
    TN_SVC --> TN1
    TN_SVC --> TN2
    TN_SVC --> TNn

    %% Logic luồng dữ liệu
    TN1 & TN2 & TNn -->|Publish| Kafka[(Kafka Cluster)]
    Kafka <-->|Consume/Produce| ETL1 & ETL2
    
    %% ETL gọi sang KCB qua Load Balancer nội bộ (Service)
    ETL1 & ETL2 -->|Call API| KCB_SVC
    KCB_SVC --> KCB1
    KCB_SVC --> KCB2

    %% Styling
    style TN_SVC fill:#f9f,stroke:#333,stroke-width:2px
    style ETL_SVC fill:#f9f,stroke:#333,stroke-width:2px
    style KCB_SVC fill:#f9f,stroke:#333,stroke-width:2px
```
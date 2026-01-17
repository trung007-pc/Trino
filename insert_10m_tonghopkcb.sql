-- =========================================================
-- GENERATE 10 MILLION RECORDS CHO TỪNG BẢNG TONGHOPKCB
-- =========================================================
-- Insert 10M records vào mỗi bảng: tonghopkcb -> tonghopkcb15
-- Sử dụng CROSS JOIN với UNNEST: 100 x 100 x 1000 = 10M
-- =========================================================

-- =========================================================
-- BẢNG 1: tonghopkcb (10M records - 69 cột)
-- =========================================================
INSERT INTO iceberg.v4.tonghopkcb
SELECT 
    CAST(UUID() AS UUID) AS id,
    CAST(UUID() AS UUID) AS tenantid,
    CONCAT('LK', LPAD(CAST((a * 10000 + b * 100 + c) AS VARCHAR), 10, '0')) AS ma_lk,
    (a * 10000 + b * 100 + c) AS stt,
    CONCAT('BN', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 1000000) AS VARCHAR), 8, '0')) AS ma_bn,
    CONCAT('Nguyen Van ', CHR(65 + MOD((a * 10000 + b * 100 + c), 26))) AS ho_ten,
    LPAD(CAST((a * 10000 + b * 100 + c) AS VARCHAR), 12, '0') AS so_cccd,
    CAST(TIMESTAMP '1960-01-01 00:00:00' + INTERVAL '1' DAY * MOD((a * 10000 + b * 100 + c), 20000) AS TIMESTAMP(6)) AS ngay_sinh,
    CAST(MOD((a * 10000 + b * 100 + c), 2) + 1 AS INTEGER) AS gioi_tinh,
    CASE MOD((a * 10000 + b * 100 + c), 4) WHEN 0 THEN 'O' WHEN 1 THEN 'A' WHEN 2 THEN 'B' ELSE 'AB' END AS nhom_mau,
    'VN' AS ma_quoctich,
    CONCAT('DT', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 54) AS VARCHAR), 2, '0')) AS ma_dantoc,
    CONCAT('NN', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 20) AS VARCHAR), 2, '0')) AS ma_nghe_nghiep,
    CONCAT(CAST((a * 10000 + b * 100 + c) AS VARCHAR), ' Nguyen Trai, Hanoi') AS dia_chi,
    LPAD(CAST(MOD((a * 10000 + b * 100 + c), 63) + 1 AS VARCHAR), 2, '0') AS matinh_cu_tru,
    LPAD(CAST(MOD((a * 10000 + b * 100 + c), 999) + 1 AS VARCHAR), 3, '0') AS mahuyen_cu_tru,
    LPAD(CAST(MOD((a * 10000 + b * 100 + c), 9999) + 1 AS VARCHAR), 5, '0') AS maxa_cu_tru,
    CONCAT('09', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 100000000) AS VARCHAR), 8, '0')) AS dien_thoai,
    CONCAT('BHYT', LPAD(CAST((a * 10000 + b * 100 + c) AS VARCHAR), 13, '0')) AS ma_the_bhyt,
    CONCAT('DK', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 10000) AS VARCHAR), 5, '0')) AS ma_dkbd,
    CAST(DATE '2024-01-01' + INTERVAL '1' DAY * MOD((a * 10000 + b * 100 + c), 365) AS VARCHAR) AS gt_the_tu,
    CAST(DATE '2025-01-01' + INTERVAL '1' DAY * MOD((a * 10000 + b * 100 + c), 365) AS VARCHAR) AS gt_the_den,
    CAST(TIMESTAMP '2024-01-01 00:00:00' + INTERVAL '1' DAY * MOD((a * 10000 + b * 100 + c), 365) AS TIMESTAMP(6)) AS ngay_mien_cct,
    CONCAT('Ly do VV ', CAST(MOD((a * 10000 + b * 100 + c), 10) AS VARCHAR)) AS ly_do_vv,
    'Kham benh' AS ly_do_vnt,
    CONCAT('VNT', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 100) AS VARCHAR), 2, '0')) AS ma_ly_do_vnt,
    CONCAT('Chuan doan vao benh ', CAST(MOD((a * 10000 + b * 100 + c), 50) AS VARCHAR)) AS chan_doan_vao,
    CONCAT('Chuan doan ra benh ', CAST(MOD((a * 10000 + b * 100 + c), 50) AS VARCHAR)) AS chan_doan_rv,
    CONCAT('ICD', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 9999) AS VARCHAR), 4, '0')) AS ma_benh_chinh,
    CONCAT('ICD', LPAD(CAST(MOD((a * 10000 + b * 100 + c + 1), 9999) AS VARCHAR), 4, '0')) AS ma_benh_kt,
    NULL AS ma_benh_yhct,
    CONCAT('PTTT', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 1000) AS VARCHAR), 4, '0')) AS ma_pttt_qt,
    CONCAT('DT', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 10) AS VARCHAR), 2, '0')) AS ma_doituong_kcb,
    CONCAT('CSKCB', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 1000) AS VARCHAR), 5, '0')) AS ma_noi_di,
    CONCAT('CSKCB', LPAD(CAST(MOD((a * 10000 + b * 100 + c + 1), 1000) AS VARCHAR), 5, '0')) AS ma_noi_den,
    CAST(MOD((a * 10000 + b * 100 + c), 5) AS INTEGER) AS ma_tai_nan,
    CAST(TIMESTAMP '2024-01-01 00:00:00' + INTERVAL '1' DAY * MOD((a * 10000 + b * 100 + c), 365) AS TIMESTAMP(6)) AS ngay_vao,
    CAST(TIMESTAMP '2024-01-01 00:00:00' + INTERVAL '1' DAY * MOD((a * 10000 + b * 100 + c), 365) AS TIMESTAMP(6)) AS ngay_vao_noi_tru,
    CAST(TIMESTAMP '2024-01-01 00:00:00' + INTERVAL '1' DAY * (MOD((a * 10000 + b * 100 + c), 365) + MOD((a * 10000 + b * 100 + c), 30)) AS TIMESTAMP(6)) AS ngay_ra,
    NULL AS giay_chuyen_tuyen,
    CAST(MOD((a * 10000 + b * 100 + c), 30) + 1 AS INTEGER) AS so_ngay_dtri,
    CONCAT('PP', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 10) AS VARCHAR), 2, '0')) AS pp_dieu_tri,
    CAST(MOD((a * 10000 + b * 100 + c), 5) + 1 AS INTEGER) AS ket_qua_dtri,
    CAST(MOD((a * 10000 + b * 100 + c), 4) + 1 AS INTEGER) AS ma_loai_rv,
    NULL AS ghi_chu,
    CAST(TIMESTAMP '2024-01-01 00:00:00' + INTERVAL '1' DAY * MOD((a * 10000 + b * 100 + c), 365) AS TIMESTAMP(6)) AS ngay_ttoan,
    CAST(10000 + MOD((a * 10000 + b * 100 + c), 990000) AS DECIMAL(18,2)) AS t_thuoc,
    CAST(5000 + MOD((a * 10000 + b * 100 + c), 495000) AS DECIMAL(18,2)) AS t_vtyt,
    CAST(50000 + MOD((a * 10000 + b * 100 + c), 9950000) AS DECIMAL(18,2)) AS t_tongchi_bv,
    CAST(30000 + MOD((a * 10000 + b * 100 + c), 5970000) AS DECIMAL(18,2)) AS t_tongchi_bh,
    CAST(20000 + MOD((a * 10000 + b * 100 + c), 3980000) AS DECIMAL(18,2)) AS t_bntt,
    CAST(0 AS DECIMAL(18,2)) AS t_bncct,
    CAST(30000 + MOD((a * 10000 + b * 100 + c), 5970000) AS DECIMAL(18,2)) AS t_bhtt,
    CAST(0 AS DECIMAL(18,2)) AS t_nguonkhac,
    CAST(0 AS DECIMAL(18,2)) AS t_bhtt_gdv,
    CAST(2024 AS INTEGER) AS nam_qt,
    CAST(MOD((a * 10000 + b * 100 + c), 12) + 1 AS INTEGER) AS thang_qt,
    CONCAT('KCB', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 5) AS VARCHAR), 2, '0')) AS ma_loai_kcb,
    CONCAT('K', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 50) AS VARCHAR), 3, '0')) AS ma_khoa,
    CONCAT('CSKCB', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 1000) AS VARCHAR), 5, '0')) AS ma_cskcb,
    CONCAT('KV', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 10) AS VARCHAR), 2, '0')) AS ma_khuvuc,
    CAST(30 + MOD((a * 10000 + b * 100 + c), 100) AS VARCHAR) AS can_nang,
    CAST(3 + MOD((a * 10000 + b * 100 + c), 5) AS VARCHAR) AS can_nang_con,
    NULL AS nam_nam_lien_tuc,
    CAST(DATE '2024-01-01' + INTERVAL '1' DAY * MOD((a * 10000 + b * 100 + c), 365) AS VARCHAR) AS ngay_tai_kham,
    CONCAT('HSBA', LPAD(CAST((a * 10000 + b * 100 + c) AS VARCHAR), 10, '0')) AS ma_hsba,
    CONCAT('TTDV', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 100) AS VARCHAR), 3, '0')) AS ma_ttdv,
    NULL AS du_phong,
    CAST(UUID() AS UUID) AS apirequestid
FROM 
    UNNEST(SEQUENCE(1, 100)) AS t1(a),
    UNNEST(SEQUENCE(1, 100)) AS t2(b),
    UNNEST(SEQUENCE(1, 1000)) AS t3(c);

-- Verify
SELECT COUNT(*) FROM iceberg.v4.tonghopkcb;

-- =========================================================
-- BẢNG 2: tonghopkcb1 (10M records)
-- =========================================================
INSERT INTO iceberg.v4.tonghopkcb1
SELECT 
    CAST(UUID() AS UUID) AS Id,
    CAST(UUID() AS UUID) AS TenantId,
    CONCAT('LK', LPAD(CAST((a * 10000 + b * 100 + c) AS VARCHAR), 10, '0')) AS ma_lk,
    (a * 10000 + b * 100 + c) AS stt,
    CONCAT('BN', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 1000000) AS VARCHAR), 8, '0')) AS ma_bn,
    CONCAT('Nguyen Van ', CHR(65 + MOD((a * 10000 + b * 100 + c), 26))) AS ho_ten,
    LPAD(CAST((a * 10000 + b * 100 + c) AS VARCHAR), 12, '0') AS so_cccd,
    TIMESTAMP '1960-01-01 00:00:00' + INTERVAL '1' DAY * MOD((a * 10000 + b * 100 + c), 20000) AS ngay_sinh,
    MOD((a * 10000 + b * 100 + c), 2) + 1 AS gioi_tinh,
    CASE MOD((a * 10000 + b * 100 + c), 4) WHEN 0 THEN 'O' WHEN 1 THEN 'A' WHEN 2 THEN 'B' ELSE 'AB' END AS nhom_mau,
    'VN' AS ma_quoctich,
    CONCAT('DT', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 54) AS VARCHAR), 2, '0')) AS ma_dantoc,
    CONCAT('NN', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 20) AS VARCHAR), 2, '0')) AS ma_nghe_nghiep,
    CONCAT(CAST((a * 10000 + b * 100 + c) AS VARCHAR), ' Nguyen Trai, Hanoi') AS dia_chi,
    LPAD(CAST(MOD((a * 10000 + b * 100 + c), 63) + 1 AS VARCHAR), 2, '0') AS matinh_cu_tru,
    LPAD(CAST(MOD((a * 10000 + b * 100 + c), 999) + 1 AS VARCHAR), 3, '0') AS mahuyen_cu_tru,
    LPAD(CAST(MOD((a * 10000 + b * 100 + c), 9999) + 1 AS VARCHAR), 5, '0') AS maxa_cu_tru,
    CONCAT('09', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 100000000) AS VARCHAR), 8, '0')) AS dien_thoai,
    CONCAT('BHYT', LPAD(CAST((a * 10000 + b * 100 + c) AS VARCHAR), 13, '0')) AS ma_the_bhyt,
    CONCAT('DK', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 10000) AS VARCHAR), 5, '0')) AS ma_dkbd,
    CAST(DATE '2024-01-01' + INTERVAL '1' DAY * MOD((a * 10000 + b * 100 + c), 365) AS VARCHAR) AS gt_the_tu,
    CAST(DATE '2025-01-01' + INTERVAL '1' DAY * MOD((a * 10000 + b * 100 + c), 365) AS VARCHAR) AS gt_the_den,
    TIMESTAMP '2024-01-01 00:00:00' + INTERVAL '1' DAY * MOD((a * 10000 + b * 100 + c), 365) AS ngay_mien_cct,
    CONCAT('Ly do VV ', CAST(MOD((a * 10000 + b * 100 + c), 10) AS VARCHAR)) AS ly_do_vv,
    'Kham benh' AS ly_do_vnt,
    CONCAT('VNT', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 100) AS VARCHAR), 2, '0')) AS ma_ly_do_vnt,
    CONCAT('Chuan doan vao benh ', CAST(MOD((a * 10000 + b * 100 + c), 50) AS VARCHAR)) AS chan_doan_vao,
    CONCAT('Chuan doan ra benh ', CAST(MOD((a * 10000 + b * 100 + c), 50) AS VARCHAR)) AS chan_doan_rv,
    CONCAT('ICD', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 9999) AS VARCHAR), 4, '0')) AS ma_benh_chinh,
    CONCAT('ICD', LPAD(CAST(MOD((a * 10000 + b * 100 + c + 1), 9999) AS VARCHAR), 4, '0')) AS ma_benh_kt,
    NULL AS ma_benh_yhct,
    CONCAT('PTTT', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 1000) AS VARCHAR), 4, '0')) AS ma_pttt_qt,
    CAST(DATE '2024-01-01' + INTERVAL '1' DAY * MOD((a * 10000 + b * 100 + c), 365) AS VARCHAR) AS ngay_tai_kham,
    CONCAT('HSBA', LPAD(CAST((a * 10000 + b * 100 + c) AS VARCHAR), 10, '0')) AS ma_hsba,
    CONCAT('TTDV', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 100) AS VARCHAR), 3, '0')) AS ma_ttdv,
    NULL AS du_phong,
    CAST(UUID() AS UUID) AS ApiRequestId
FROM 
    UNNEST(SEQUENCE(1, 100)) AS t1(a),
    UNNEST(SEQUENCE(1, 100)) AS t2(b),
    UNNEST(SEQUENCE(1, 1000)) AS t3(c);

SELECT COUNT(*) FROM iceberg.v4.tonghopkcb1;

-- =========================================================
-- LẶP LẠI CHO CÁC BẢNG CÒN LẠI (tonghopkcb2 -> tonghopkcb15)
-- =========================================================
-- Copy pattern trên và thay đổi tên bảng

-- tonghopkcb2
INSERT INTO iceberg.v4.tonghopkcb2
SELECT CAST(UUID() AS UUID), CAST(UUID() AS UUID), 
    CONCAT('LK', LPAD(CAST((a * 10000 + b * 100 + c) AS VARCHAR), 10, '0')),
    (a * 10000 + b * 100 + c), CONCAT('BN', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 1000000) AS VARCHAR), 8, '0')),
    CONCAT('Nguyen Van ', CHR(65 + MOD((a * 10000 + b * 100 + c), 26))), LPAD(CAST((a * 10000 + b * 100 + c) AS VARCHAR), 12, '0'),
    TIMESTAMP '1960-01-01 00:00:00' + INTERVAL '1' DAY * MOD((a * 10000 + b * 100 + c), 20000),
    MOD((a * 10000 + b * 100 + c), 2) + 1, CASE MOD((a * 10000 + b * 100 + c), 4) WHEN 0 THEN 'O' WHEN 1 THEN 'A' WHEN 2 THEN 'B' ELSE 'AB' END,
    'VN', CONCAT('DT', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 54) AS VARCHAR), 2, '0')),
    CONCAT('NN', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 20) AS VARCHAR), 2, '0')),
    CONCAT(CAST((a * 10000 + b * 100 + c) AS VARCHAR), ' Nguyen Trai, Hanoi'),
    LPAD(CAST(MOD((a * 10000 + b * 100 + c), 63) + 1 AS VARCHAR), 2, '0'), LPAD(CAST(MOD((a * 10000 + b * 100 + c), 999) + 1 AS VARCHAR), 3, '0'),
    LPAD(CAST(MOD((a * 10000 + b * 100 + c), 9999) + 1 AS VARCHAR), 5, '0'), CONCAT('09', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 100000000) AS VARCHAR), 8, '0')),
    CONCAT('BHYT', LPAD(CAST((a * 10000 + b * 100 + c) AS VARCHAR), 13, '0')), CONCAT('DK', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 10000) AS VARCHAR), 5, '0')),
    CAST(DATE '2024-01-01' + INTERVAL '1' DAY * MOD((a * 10000 + b * 100 + c), 365) AS VARCHAR),
    CAST(DATE '2025-01-01' + INTERVAL '1' DAY * MOD((a * 10000 + b * 100 + c), 365) AS VARCHAR),
    TIMESTAMP '2024-01-01 00:00:00' + INTERVAL '1' DAY * MOD((a * 10000 + b * 100 + c), 365),
    CONCAT('Ly do VV ', CAST(MOD((a * 10000 + b * 100 + c), 10) AS VARCHAR)), 'Kham benh',
    CONCAT('VNT', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 100) AS VARCHAR), 2, '0')),
    CONCAT('Chuan doan vao benh ', CAST(MOD((a * 10000 + b * 100 + c), 50) AS VARCHAR)),
    CONCAT('Chuan doan ra benh ', CAST(MOD((a * 10000 + b * 100 + c), 50) AS VARCHAR)),
    CONCAT('ICD', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 9999) AS VARCHAR), 4, '0')),
    CONCAT('ICD', LPAD(CAST(MOD((a * 10000 + b * 100 + c + 1), 9999) AS VARCHAR), 4, '0')), NULL,
    CONCAT('PTTT', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 1000) AS VARCHAR), 4, '0')),
    CAST(DATE '2024-01-01' + INTERVAL '1' DAY * MOD((a * 10000 + b * 100 + c), 365) AS VARCHAR),
    CONCAT('HSBA', LPAD(CAST((a * 10000 + b * 100 + c) AS VARCHAR), 10, '0')),
    CONCAT('TTDV', LPAD(CAST(MOD((a * 10000 + b * 100 + c), 100) AS VARCHAR), 3, '0')), NULL, CAST(UUID() AS UUID)
FROM UNNEST(SEQUENCE(1, 100)) AS t1(a), UNNEST(SEQUENCE(1, 100)) AS t2(b), UNNEST(SEQUENCE(1, 1000)) AS t3(c);

-- =========================================================
-- ⚠️ LƯU Ý: File này chỉ có mẫu cho 3 bảng đầu
-- Copy pattern và thay tên bảng cho các bảng còn lại:
-- tonghopkcb3, tonghopkcb4, ..., tonghopkcb15
-- =========================================================

-- =========================================================
-- KIỂM TRA TỔNG SỐ RECORDS
-- =========================================================
SELECT 'tonghopkcb' AS table_name, COUNT(*) AS record_count FROM iceberg.v4.tonghopkcb
UNION ALL
SELECT 'tonghopkcb1', COUNT(*) FROM iceberg.v4.tonghopkcb1
UNION ALL
SELECT 'tonghopkcb2', COUNT(*) FROM iceberg.v4.tonghopkcb2;
-- Thêm các bảng còn lại...

-- Expected: Mỗi bảng 10,000,000 records
-- Total: 160,000,000 records (16 bảng x 10M)

-- =========================================================
-- ƯỚC TÍNH:
-- - Thời gian: ~2-3 phút/bảng = 30-50 phút tổng
-- - Disk: ~8-16 GB (16 bảng x 500MB-1GB)
-- - Mỗi bảng: 10,000,000 records
-- =========================================================

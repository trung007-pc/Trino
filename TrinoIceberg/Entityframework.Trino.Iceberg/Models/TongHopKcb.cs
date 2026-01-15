using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace kcb.KcbService.Entities.TongHopKcbs;

public class TongHopKcb
{
    public Guid Id { get; set; }
    public virtual Guid? TenantId { get; set; }

    
    public virtual string? MA_LK { get; set; }

    public virtual long STT { get; set; }

    
    public virtual string? MA_BN { get; set; }

    
    public virtual string? HO_TEN { get; set; }

    
    public virtual string? SO_CCCD { get; set; }

    public virtual DateTime? NGAY_SINH { get; set; }

    public virtual int? GIOI_TINH { get; set; }

    
    public virtual string? NHOM_MAU { get; set; }

    
    public virtual string? MA_QUOCTICH { get; set; }

    
    public virtual string? MA_DANTOC { get; set; }

    
    public virtual string? MA_NGHE_NGHIEP { get; set; }

    
    public virtual string? DIA_CHI { get; set; }

    
    public virtual string? MATINH_CU_TRU { get; set; }

    
    public virtual string? MAHUYEN_CU_TRU { get; set; }

    
    public virtual string? MAXA_CU_TRU { get; set; }

    
    public virtual string? DIEN_THOAI { get; set; }

    
    public virtual string? MA_THE_BHYT { get; set; }

    
    public virtual string? MA_DKBD { get; set; }

    
    public virtual string? GT_THE_TU { get; set; }

    
    public virtual string? GT_THE_DEN { get; set; }

    public virtual DateTime? NGAY_MIEN_CCT { get; set; }

    
    public virtual string? LY_DO_VV { get; set; }

    
    public virtual string? LY_DO_VNT { get; set; }

    
    public virtual string? MA_LY_DO_VNT { get; set; }

    
    public virtual string? CHAN_DOAN_VAO { get; set; }

    
    public virtual string? CHAN_DOAN_RV { get; set; }

    
    public virtual string? MA_BENH_CHINH { get; set; }

    
    public virtual string? MA_BENH_KT { get; set; }

    
    public virtual string? MA_BENH_YHCT { get; set; }

    
    public virtual string? MA_PTTT_QT { get; set; }

    
    public virtual string? MA_DOITUONG_KCB { get; set; }

    
    public virtual string? MA_NOI_DI { get; set; }

    
    public virtual string? MA_NOI_DEN { get; set; }

    public virtual int? MA_TAI_NAN { get; set; }

    public virtual DateTime? NGAY_VAO { get; set; }

    public virtual DateTime? NGAY_VAO_NOI_TRU { get; set; }

    public virtual DateTime? NGAY_RA { get; set; }

    
    public virtual string? GIAY_CHUYEN_TUYEN { get; set; }

    public virtual int? SO_NGAY_DTRI { get; set; }

    
    public virtual string? PP_DIEU_TRI { get; set; }

    public virtual int? KET_QUA_DTRI { get; set; }

    public virtual int? MA_LOAI_RV { get; set; }

    
    public virtual string? GHI_CHU { get; set; }

    public virtual DateTime? NGAY_TTOAN { get; set; }

    public virtual decimal? T_THUOC { get; set; }

    public virtual decimal? T_VTYT { get; set; }

    public virtual decimal? T_TONGCHI_BV { get; set; }

    public virtual decimal? T_TONGCHI_BH { get; set; }

    public virtual decimal? T_BNTT { get; set; }

    public virtual decimal? T_BNCCT { get; set; }

    public virtual decimal? T_BHTT { get; set; }

    public virtual decimal? T_NGUONKHAC { get; set; }

    public virtual decimal? T_BHTT_GDV { get; set; }

    public virtual int? NAM_QT { get; set; }

    public virtual int? THANG_QT { get; set; }

    
    public virtual string? MA_LOAI_KCB { get; set; }

    
    public virtual string? MA_KHOA { get; set; }

    
    public virtual string? MA_CSKCB { get; set; }

    
    public virtual string? MA_KHUVUC { get; set; }

    
    public virtual string? CAN_NANG { get; set; }

    
    public virtual string? CAN_NANG_CON { get; set; }

    
    public virtual string? NAM_NAM_LIEN_TUC { get; set; }

    
    public virtual string? NGAY_TAI_KHAM { get; set; }

    
    public virtual string? MA_HSBA { get; set; }

    
    public virtual string? MA_TTDV { get; set; }

    
    public virtual string? DU_PHONG { get; set; }

    public virtual Guid? ApiRequestId { get; set; }
}
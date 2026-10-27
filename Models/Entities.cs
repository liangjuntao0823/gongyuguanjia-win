using System;
using System.Collections.Generic;
using System.IO;

namespace ApartmentManagerWin.Models;

// ============================================================
// 数据实体（对齐 Mac 版 SwiftData 模型字段，SQLite 持久化）
// v0.1：首版 Windows 移植
// ============================================================

/// 房源（1/2 座 · 单间/复式 · 上下架/出租状态 · 媒体 · 云端同步标识）
public class House
{
    public long Id { get; set; }
    public string RoomNo { get; set; } = "";          // 房号（"2-"前缀=2座，否则1座）
    public int Tower { get; set; }                    // 1 / 2
    public string Landlord { get; set; } = "";        // 房东（康/业主/…）
    public string Layout { get; set; } = "";          // 户型（单间下层/单间上层/复式…）
    public int Price { get; set; }                    // 租金（整数，与两端一致）
    public string Status { get; set; } = "待租";       // 待租/已租/已上架/未上架（展示态）
    public bool IsRented { get; set; }
    public bool IsListed { get; set; }                // 上架
    public string Remark { get; set; } = "";
    public string DoorPassword { get; set; } = "";
    public string LeaseStart { get; set; } = "";      // 租期
    public string LeaseEnd { get; set; } = "";
    public string MediaRoomKey { get; set; } = "";    // 媒体目录 key（如 "1-2325"）

    // 软删/回收站
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    // 云端同步
    public string CloudStatus { get; set; } = "local"; // local / cloud / both
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public DateTime LastUpdatedAt { get; set; } = DateTime.Now;
    public bool HasMedia { get; set; }                // 有实拍图（v1.2.4 统计口径）

    public string DisplayRoomNo => RoomNo;
}

/// 房源媒体（图片/视频）
public class MediaItem
{
    public long Id { get; set; }
    public string RoomKey { get; set; } = "";
    public string FileName { get; set; } = "";        // roomKey-TAG-uid-N.ext（与两端一致）
    public string Kind { get; set; } = "image";       // image / video
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// 收租房源（经营侧：交租日/押金/预存/租期/月度收租/水电计划）
public class Property
{
    public long Id { get; set; }
    public string RoomNumber { get; set; } = "";
    public string Landlord { get; set; } = "";
    public string UnitType { get; set; } = "";
    public string Type { get; set; } = "管理";         // 管理/托管/包租
    public int Rent { get; set; }
    public int Deposit { get; set; }
    public int Prepayment { get; set; }
    public string LeaseStart { get; set; } = "";
    public string LeaseEnd { get; set; } = "";
    public string LeaseDuration { get; set; } = "";
    public int RentDueDay { get; set; } = 1;
    public string Notes { get; set; } = "";
    public decimal WaterMeterBase { get; set; }
    public decimal ElectricMeterBase { get; set; }

    public bool IsTerminated { get; set; }
    public DateTime? TerminationDate { get; set; }
    public string TerminationReason { get; set; } = "";
    public string DepositRefundStatus { get; set; } = "";
    public string RoomDamageStatus { get; set; } = "";
    public string UtilitySettlementStatus { get; set; } = "";
    public string TerminationPerson { get; set; } = "";
    public string TerminationPhone { get; set; } = "";

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// 月度收租记录
public class RentMonthRecord
{
    public long Id { get; set; }
    public long PropertyId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    public bool IsPaid { get; set; }
    public DateTime? PaidDate { get; set; }
    public string Note { get; set; } = "";
}

/// 水电结算计划记录
public class UtilitySettlementRecord
{
    public long Id { get; set; }
    public long PropertyId { get; set; }
    public int Year { get; set; }
    public int Period { get; set; }
    public decimal WaterAmount { get; set; }
    public decimal ElectricAmount { get; set; }
    public decimal WaterUsage { get; set; }
    public decimal ElectricUsage { get; set; }
    public bool IsSettled { get; set; }
    public DateTime? SettledDate { get; set; }
}

/// 包租合同
public class PayoutRecord
{
    public long Id { get; set; }
    public string RoomNumber { get; set; } = "";
    public string Manager { get; set; } = "";         // 包租人
    public string UnitType { get; set; } = "";
    public string PaymentMethod { get; set; } = "月付"; // 月付/季付/半年付/年付
    public int AnnualRent { get; set; }
    public int? MonthlyRent { get; set; }
    public int Deposit { get; set; }
    public int RentFreeDays { get; set; }
    public DateTime LeaseStartDate { get; set; }
    public DateTime LeaseEndDate { get; set; }
    public string LeaseDuration { get; set; } = "";
    public int RentDueDay { get; set; } = 1;
    public decimal WaterMeterBase { get; set; }
    public decimal ElectricMeterBase { get; set; }
    public string Notes { get; set; } = "";

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// 包租打租记录
public class MonthlyPayout
{
    public long Id { get; set; }
    public long PayoutId { get; set; }
    public int Month { get; set; }
    public decimal Amount { get; set; }
    public bool IsPaid { get; set; }
    public DateTime? PaidDate { get; set; }
}

/// 包租水电结算
public class MonthlyUtilityRecord
{
    public long Id { get; set; }
    public long PayoutId { get; set; }
    public DateTime Date { get; set; }
    public decimal WaterAmount { get; set; }
    public decimal ElectricAmount { get; set; }
    public bool IsSettled { get; set; }
}

/// 成交（收租成交/中介费）
public class DealRecord
{
    public long Id { get; set; }
    public DateTime Date { get; set; }
    public string RoomNumber { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Type { get; set; } = "收租成交";     // 收租成交/出租成交/…
    public decimal Amount { get; set; }               // 成交金额
    public decimal AgentFee { get; set; }             // 中介费
    public string Note { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public class MiscIncome
{
    public long Id { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string Note { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class MiscExpense
{
    public long Id { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string Note { get; set; } = "";
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

/// 待办
public class TodoItem
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Note { get; set; } = "";
    public bool IsCompleted { get; set; }
    public DateTime? DueDate { get; set; }
    public bool HasReminder { get; set; }
    public int SortOrder { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// 水电结算历史（工具）
public class UtilitySettlementHistory
{
    public long Id { get; set; }
    public DateTime Date { get; set; }
    public string RoomNumber { get; set; } = "";
    public decimal WaterAmount { get; set; }
    public decimal ElectricAmount { get; set; }
    public decimal WaterUsage { get; set; }
    public decimal ElectricUsage { get; set; }
    public string Note { get; set; } = "";
}

/// 操作日志
public class OperationLog
{
    public long Id { get; set; }
    public DateTime Time { get; set; }
    public string Category { get; set; } = "";
    public string Action { get; set; } = "";
    public string Result { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Room { get; set; } = "";
}

/// 分享记录（选房链接访问统计，云端 do_shares）
public class ShareRecord
{
    public long Id { get; set; }
    public string RoomNo { get; set; } = "";
    public string Mode { get; set; } = "";            // 脱敏/完整
    public DateTime Time { get; set; }
    public string Link { get; set; } = "";
}

/// 设置项（key-value）
public class SettingItem
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// 云配置（COS + 云函数）
public class CosConfig
{
    public string Username { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string Region { get; set; } = "";
    public string SecretId { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string CustomDomain { get; set; } = "";
    public string FunctionUrl { get; set; } = "";
    public bool IsValid => !string.IsNullOrEmpty(Bucket) && !string.IsNullOrEmpty(SecretId) && !string.IsNullOrEmpty(SecretKey);

    public const string SaveKey = "cosConfig";
    public static CosConfig Load() => Settings.Load<CosConfig>(SaveKey);
    public void Save() => Settings.Save(SaveKey, this);
}

/// 远程配置（云端公告/更新日志/最低版本/功能开关/数值参数）
public class RemoteConfig
{
    public string Notice { get; set; } = "";
    public string Changelog { get; set; } = "";
    public string MinVersion { get; set; } = "";
    public Dictionary<string, bool> Flags { get; set; } = new();
    public Dictionary<string, double> Numbers { get; set; } = new();
    public string AdminPasswordHash { get; set; } = "";
}

/// 设置辅助（JSON 序列化到 settings 表）
public static class Settings
{
    private static string? _filePath;
    public static string FilePath => _filePath ??= Path.Combine(DataRoot.Directory, "settings.json");

    public static T Load<T>(string key) where T : new()
    {
        try
        {
            var dict = ReadAll();
            if (dict.TryGetValue(key, out var json))
                return System.Text.Json.JsonSerializer.Deserialize<T>(json) ?? new T();
        }
        catch { }
        return new T();
    }

    public static void Save<T>(string key, T value)
    {
        var dict = ReadAll();
        dict[key] = System.Text.Json.JsonSerializer.Serialize(value);
        WriteAll(dict);
    }

    public static string Get(string key, string def = "")
    {
        var dict = ReadAll();
        return dict.TryGetValue(key, out var v) ? v : def;
    }

    public static void Set(string key, string value)
    {
        var dict = ReadAll();
        dict[key] = value;
        WriteAll(dict);
    }

    private static Dictionary<string, string> ReadAll()
    {
        try
        {
            if (File.Exists(FilePath))
                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    private static void WriteAll(Dictionary<string, string> dict)
    {
        Directory.CreateDirectory(DataRoot.Directory);
        File.WriteAllText(FilePath, System.Text.Json.JsonSerializer.Serialize(dict));
    }
}

/// 数据根目录（Windows：%LOCALAPPDATA%/公寓管家Win）
public static class DataRoot
{
    public static string Directory
    {
        get
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(baseDir, "公寓管家Win");
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string DbPath => Path.Combine(Directory, "apartment.db");
    public static string MediaDirectory
    {
        get
        {
            var dir = Path.Combine(Directory, "media");
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }
    }
    public static string BackupsDirectory
    {
        get
        {
            var dir = Path.Combine(Directory, "备份");
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }
    }
    public static string ShareCacheDirectory
    {
        get
        {
            var dir = Path.Combine(Directory, "ShareCache");
            System.IO.Directory.CreateDirectory(dir);
            return dir;
        }
    }
    /// 房源媒体目录（按房号 key：media/<roomKey>/）
    public static string RoomMediaDirectory(string roomKey)
    {
        var dir = Path.Combine(MediaDirectory, roomKey);
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }
    /// 本地账本镜像（对齐 iOS v1.72：media/<房号>/info.json）
    public static string LedgerPath(string roomKey) => Path.Combine(RoomMediaDirectory(roomKey), "info.json");

    public static long FolderSize(string dir)
    {
        if (!System.IO.Directory.Exists(dir)) return 0;
        long total = 0;
        try
        {
            foreach (var f in System.IO.Directory.EnumerateFiles(dir, "*", System.IO.SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
        }
        catch { }
        return total;
    }
}

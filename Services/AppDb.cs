using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using ApartmentManagerWin.Models;

namespace ApartmentManagerWin.Services;

// ============================================================
// SQLite 数据层（单库 apartment.db）
// 所有写操作后广播 DataChanged 事件，视图按需刷新（懒加载）
// —— 规避 Mac 版 SwiftData 软删缓存导致的「删除无效/回收站空」bug
// ============================================================

public static class AppDb
{
    public static event Action? DataChanged;
    public static event Action? HousesChanged;

    private static readonly string ConnStr = $"Data Source={DataRoot.DbPath}";

    static AppDb()
    {
        Initialize();
    }

    public static SqliteConnection Open()
    {
        var conn = new SqliteConnection(ConnStr);
        conn.Open();
        return conn;
    }

    public static void NotifyDataChanged() => DataChanged?.Invoke();
    public static void NotifyHousesChanged() { HousesChanged?.Invoke(); DataChanged?.Invoke(); }

    // ---------- 建表 ----------
    public static void Initialize()
    {
        using var conn = Open();
        Exec(conn, """
        CREATE TABLE IF NOT EXISTS houses (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            room_no TEXT, tower INTEGER DEFAULT 1, landlord TEXT, layout TEXT,
            price INTEGER DEFAULT 0, status TEXT DEFAULT '待租', is_rented INTEGER DEFAULT 0,
            is_listed INTEGER DEFAULT 1, remark TEXT, door_password TEXT,
            lease_start TEXT, lease_end TEXT, media_room_key TEXT,
            is_deleted INTEGER DEFAULT 0, deleted_at TEXT,
            cloud_status TEXT DEFAULT 'local', updated_at TEXT, last_updated_at TEXT, has_media INTEGER DEFAULT 0
        );
        CREATE TABLE IF NOT EXISTS media_items (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            room_key TEXT, file_name TEXT, kind TEXT DEFAULT 'image',
            size INTEGER DEFAULT 0, created_at TEXT
        );
        CREATE TABLE IF NOT EXISTS properties (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            room_number TEXT, landlord TEXT, unit_type TEXT, type TEXT DEFAULT '管理',
            rent INTEGER DEFAULT 0, deposit INTEGER DEFAULT 0, prepayment INTEGER DEFAULT 0,
            lease_start TEXT, lease_end TEXT, lease_duration TEXT, rent_due_day INTEGER DEFAULT 1,
            notes TEXT, water_meter_base REAL DEFAULT 0, electric_meter_base REAL DEFAULT 0,
            is_terminated INTEGER DEFAULT 0, termination_date TEXT, termination_reason TEXT,
            deposit_refund_status TEXT, room_damage_status TEXT, utility_settlement_status TEXT,
            termination_person TEXT, termination_phone TEXT,
            is_deleted INTEGER DEFAULT 0, deleted_at TEXT, updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS rent_month_records (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            property_id INTEGER, year INTEGER, month INTEGER,
            amount REAL DEFAULT 0, is_paid INTEGER DEFAULT 0, paid_date TEXT, note TEXT
        );
        CREATE TABLE IF NOT EXISTS utility_settlement_records (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            property_id INTEGER, year INTEGER, period INTEGER,
            water_amount REAL DEFAULT 0, electric_amount REAL DEFAULT 0,
            water_usage REAL DEFAULT 0, electric_usage REAL DEFAULT 0,
            is_settled INTEGER DEFAULT 0, settled_date TEXT
        );
        CREATE TABLE IF NOT EXISTS payouts (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            room_number TEXT, manager TEXT, unit_type TEXT, payment_method TEXT DEFAULT '月付',
            annual_rent INTEGER DEFAULT 0, monthly_rent INTEGER, deposit INTEGER DEFAULT 0,
            rent_free_days INTEGER DEFAULT 0, lease_start_date TEXT, lease_end_date TEXT,
            lease_duration TEXT, rent_due_day INTEGER DEFAULT 1,
            water_meter_base REAL DEFAULT 0, electric_meter_base REAL DEFAULT 0, notes TEXT,
            is_deleted INTEGER DEFAULT 0, deleted_at TEXT, updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS monthly_payouts (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            payout_id INTEGER, month INTEGER, amount REAL DEFAULT 0,
            is_paid INTEGER DEFAULT 0, paid_date TEXT
        );
        CREATE TABLE IF NOT EXISTS monthly_utility_records (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            payout_id INTEGER, date TEXT, water_amount REAL DEFAULT 0,
            electric_amount REAL DEFAULT 0, is_settled INTEGER DEFAULT 0
        );
        CREATE TABLE IF NOT EXISTS deals (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            date TEXT, room_number TEXT, customer TEXT, type TEXT DEFAULT '收租成交',
            amount REAL DEFAULT 0, agent_fee REAL DEFAULT 0, note TEXT,
            is_deleted INTEGER DEFAULT 0, deleted_at TEXT, updated_at TEXT
        );
        CREATE TABLE IF NOT EXISTS misc_incomes (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            date TEXT, amount REAL DEFAULT 0, note TEXT,
            is_deleted INTEGER DEFAULT 0, deleted_at TEXT
        );
        CREATE TABLE IF NOT EXISTS misc_expenses (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            date TEXT, amount REAL DEFAULT 0, note TEXT,
            is_deleted INTEGER DEFAULT 0, deleted_at TEXT
        );
        CREATE TABLE IF NOT EXISTS todos (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            title TEXT, note TEXT, is_completed INTEGER DEFAULT 0,
            due_date TEXT, has_reminder INTEGER DEFAULT 0, sort_order INTEGER DEFAULT 0,
            is_deleted INTEGER DEFAULT 0, deleted_at TEXT, created_at TEXT
        );
        CREATE TABLE IF NOT EXISTS utility_histories (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            date TEXT, room_number TEXT, water_amount REAL DEFAULT 0,
            electric_amount REAL DEFAULT 0, water_usage REAL DEFAULT 0,
            electric_usage REAL DEFAULT 0, note TEXT
        );
        CREATE TABLE IF NOT EXISTS operation_logs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            time TEXT, category TEXT, action TEXT, result TEXT, detail TEXT, room TEXT
        );
        CREATE TABLE IF NOT EXISTS share_records (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            room_no TEXT, mode TEXT, time TEXT, link TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_media_room ON media_items(room_key);
        CREATE INDEX IF NOT EXISTS idx_rent_prop ON rent_month_records(property_id);
        CREATE INDEX IF NOT EXISTS idx_util_prop ON utility_settlement_records(property_id);
        CREATE INDEX IF NOT EXISTS idx_payout_month ON monthly_payouts(payout_id);
        """);
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long LastInsertId(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    // ============================================================
    // 房源 + 媒体
    // ============================================================

    public static List<House> GetHouses(bool includeDeleted = false)
    {
        var list = new List<House>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = includeDeleted
            ? "SELECT * FROM houses ORDER BY tower, room_no"
            : "SELECT * FROM houses WHERE is_deleted = 0 ORDER BY tower, room_no";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadHouse(r));
        return list;
    }

    private static House ReadHouse(SqliteDataReader r)
    {
        var h = new House
        {
            Id = r.GetInt64(0),
            RoomNo = r.GetString(1),
            Tower = r.GetInt32(2),
            Landlord = r.GetString(3),
            Layout = r.GetString(4),
            Price = r.GetInt32(5),
            Status = r.GetString(6),
            IsRented = r.GetInt32(7) == 1,
            IsListed = r.GetInt32(8) == 1,
            Remark = r.GetString(9),
            DoorPassword = r.GetString(10),
            LeaseStart = r.GetString(11),
            LeaseEnd = r.GetString(12),
            MediaRoomKey = r.GetString(13),
            IsDeleted = r.GetInt32(14) == 1,
            DeletedAt = ParseDate(r, 15),
            CloudStatus = r.GetString(16),
            UpdatedAt = ParseDate(r, 17) ?? DateTime.Now,
            LastUpdatedAt = ParseDate(r, 18) ?? DateTime.Now,
            HasMedia = r.GetInt32(19) == 1,
        };
        return h;
    }

    private static DateTime? ParseDate(SqliteDataReader r, int i)
    {
        if (r.IsDBNull(i)) return null;
        var s = r.GetString(i);
        return DateTime.TryParse(s, out var d) ? d : null;
    }

    public static long SaveHouse(House h)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        h.UpdatedAt = DateTime.Now;
        h.LastUpdatedAt = h.UpdatedAt;
        if (h.Id == 0)
        {
            cmd.CommandText = """
                INSERT INTO houses (room_no,tower,landlord,layout,price,status,is_rented,is_listed,remark,door_password,
                    lease_start,lease_end,media_room_key,is_deleted,deleted_at,cloud_status,updated_at,last_updated_at,has_media)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19)
                """;
        }
        else
        {
            cmd.CommandText = """
                UPDATE houses SET room_no=$1,tower=$2,landlord=$3,layout=$4,price=$5,status=$6,is_rented=$7,is_listed=$8,
                    remark=$9,door_password=$10,lease_start=$11,lease_end=$12,media_room_key=$13,is_deleted=$14,
                    deleted_at=$15,cloud_status=$16,updated_at=$17,last_updated_at=$18,has_media=$19 WHERE id=$20
                """;
            cmd.Parameters.AddWithValue("$20", h.Id);
        }
        cmd.Parameters.AddWithValue("$1", h.RoomNo);
        cmd.Parameters.AddWithValue("$2", h.Tower);
        cmd.Parameters.AddWithValue("$3", h.Landlord);
        cmd.Parameters.AddWithValue("$4", h.Layout);
        cmd.Parameters.AddWithValue("$5", h.Price);
        cmd.Parameters.AddWithValue("$6", h.Status);
        cmd.Parameters.AddWithValue("$7", h.IsRented ? 1 : 0);
        cmd.Parameters.AddWithValue("$8", h.IsListed ? 1 : 0);
        cmd.Parameters.AddWithValue("$9", h.Remark);
        cmd.Parameters.AddWithValue("$10", h.DoorPassword);
        cmd.Parameters.AddWithValue("$11", h.LeaseStart);
        cmd.Parameters.AddWithValue("$12", h.LeaseEnd);
        cmd.Parameters.AddWithValue("$13", h.MediaRoomKey);
        cmd.Parameters.AddWithValue("$14", h.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$15", h.DeletedAt?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$16", h.CloudStatus);
        cmd.Parameters.AddWithValue("$17", h.UpdatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$18", h.LastUpdatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$19", h.HasMedia ? 1 : 0);
        cmd.ExecuteNonQuery();
        if (h.Id == 0) h.Id = LastInsertId(conn);
        return h.Id;
    }

    /// 软删（进回收站）
    public static void SoftDeleteHouse(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE houses SET is_deleted=1, deleted_at=$1, updated_at=$2 WHERE id=$3";
        cmd.Parameters.AddWithValue("$1", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("$2", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("$3", id);
        cmd.ExecuteNonQuery();
    }

    public static void RestoreHouse(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE houses SET is_deleted=0, deleted_at=NULL, updated_at=$1 WHERE id=$2";
        cmd.Parameters.AddWithValue("$1", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("$2", id);
        cmd.ExecuteNonQuery();
    }

    /// 彻底删除（回收站）
    public static void PermanentDeleteHouse(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM houses WHERE id=$1";
        cmd.Parameters.AddWithValue("$1", id);
        cmd.ExecuteNonQuery();
    }

    // ---------- 媒体 ----------
    public static List<MediaItem> GetMedia(string roomKey)
    {
        var list = new List<MediaItem>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,room_key,file_name,kind,size,created_at FROM media_items WHERE room_key=$1 ORDER BY id";
        cmd.Parameters.AddWithValue("$1", roomKey);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new MediaItem
            {
                Id = r.GetInt64(0),
                RoomKey = r.GetString(1),
                FileName = r.GetString(2),
                Kind = r.GetString(3),
                Size = r.GetInt64(4),
                CreatedAt = DateTime.TryParse(r.GetString(5), out var d) ? d : DateTime.Now,
            });
        }
        return list;
    }

    public static void AddMedia(MediaItem m)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO media_items (room_key,file_name,kind,size,created_at) VALUES ($1,$2,$3,$4,$5)";
        cmd.Parameters.AddWithValue("$1", m.RoomKey);
        cmd.Parameters.AddWithValue("$2", m.FileName);
        cmd.Parameters.AddWithValue("$3", m.Kind);
        cmd.Parameters.AddWithValue("$4", m.Size);
        cmd.Parameters.AddWithValue("$5", DateTime.Now.ToString("o"));
        cmd.ExecuteNonQuery();
        UpdateHouseHasMedia(m.RoomKey);
    }

    public static void DeleteMedia(string roomKey, string fileName)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM media_items WHERE room_key=$1 AND file_name=$2";
        cmd.Parameters.AddWithValue("$1", roomKey);
        cmd.Parameters.AddWithValue("$2", fileName);
        cmd.ExecuteNonQuery();
        UpdateHouseHasMedia(roomKey);
    }

    public static void UpdateHouseHasMedia(string roomKey)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE houses SET has_media=(SELECT COUNT(*) FROM media_items WHERE room_key=$1) WHERE media_room_key=$1";
        cmd.Parameters.AddWithValue("$1", roomKey);
        cmd.ExecuteNonQuery();
    }

    public static void UpdateHouseHasMediaForAll()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE houses SET has_media = COALESCE((SELECT COUNT(*) FROM media_items mi WHERE mi.room_key = houses.media_room_key), 0)
            """;
        cmd.ExecuteNonQuery();
    }

    // ============================================================
    // 收租
    // ============================================================

    public static List<Property> GetProperties(bool includeDeleted = false)
    {
        var list = new List<Property>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = includeDeleted
            ? "SELECT * FROM properties ORDER BY is_terminated, rent_due_day, room_number"
            : "SELECT * FROM properties WHERE is_deleted = 0 ORDER BY is_terminated, rent_due_day, room_number";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadProperty(r));
        return list;
    }

    private static Property ReadProperty(SqliteDataReader r)
    {
        return new Property
        {
            Id = r.GetInt64(0), RoomNumber = r.GetString(1), Landlord = r.GetString(2),
            UnitType = r.GetString(3), Type = r.GetString(4), Rent = r.GetInt32(5),
            Deposit = r.GetInt32(6), Prepayment = r.GetInt32(7), LeaseStart = r.GetString(8),
            LeaseEnd = r.GetString(9), LeaseDuration = r.GetString(10), RentDueDay = r.GetInt32(11),
            Notes = r.GetString(12), WaterMeterBase = r.GetDecimal(13), ElectricMeterBase = r.GetDecimal(14),
            IsTerminated = r.GetInt32(15) == 1, TerminationDate = ParseDate(r, 16),
            TerminationReason = r.GetString(17), DepositRefundStatus = r.GetString(18),
            RoomDamageStatus = r.GetString(19), UtilitySettlementStatus = r.GetString(20),
            TerminationPerson = r.GetString(21), TerminationPhone = r.GetString(22),
            IsDeleted = r.GetInt32(23) == 1, DeletedAt = ParseDate(r, 24),
            UpdatedAt = ParseDate(r, 25) ?? DateTime.Now,
        };
    }

    public static long SaveProperty(Property p)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        p.UpdatedAt = DateTime.Now;
        if (p.Id == 0)
        {
            cmd.CommandText = """
                INSERT INTO properties (room_number,landlord,unit_type,type,rent,deposit,prepayment,lease_start,lease_end,
                    lease_duration,rent_due_day,notes,water_meter_base,electric_meter_base,is_terminated,termination_date,
                    termination_reason,deposit_refund_status,room_damage_status,utility_settlement_status,
                    termination_person,termination_phone,is_deleted,deleted_at,updated_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21,$22,$23,$24,$25)
                """;
        }
        else
        {
            cmd.CommandText = """
                UPDATE properties SET room_number=$1,landlord=$2,unit_type=$3,type=$4,rent=$5,deposit=$6,prepayment=$7,
                    lease_start=$8,lease_end=$9,lease_duration=$10,rent_due_day=$11,notes=$12,water_meter_base=$13,
                    electric_meter_base=$14,is_terminated=$15,termination_date=$16,termination_reason=$17,
                    deposit_refund_status=$18,room_damage_status=$19,utility_settlement_status=$20,
                    termination_person=$21,termination_phone=$22,is_deleted=$23,deleted_at=$24,updated_at=$25 WHERE id=$26
                """;
            cmd.Parameters.AddWithValue("$26", p.Id);
        }
        AddParams(cmd, 1, p.RoomNumber, p.Landlord, p.UnitType, p.Type, p.Rent, p.Deposit, p.Prepayment,
            p.LeaseStart, p.LeaseEnd, p.LeaseDuration, p.RentDueDay, p.Notes, p.WaterMeterBase, p.ElectricMeterBase,
            p.IsTerminated ? 1 : 0, p.TerminationDate?.ToString("o") ?? (object)DBNull.Value, p.TerminationReason,
            p.DepositRefundStatus, p.RoomDamageStatus, p.UtilitySettlementStatus, p.TerminationPerson,
            p.TerminationPhone, p.IsDeleted ? 1 : 0, p.DeletedAt?.ToString("o") ?? (object)DBNull.Value, p.UpdatedAt.ToString("o"));
        cmd.ExecuteNonQuery();
        if (p.Id == 0) p.Id = LastInsertId(conn);
        return p.Id;
    }

    private static void AddParams(SqliteCommand cmd, int start, params object?[] values)
    {
        for (int i = 0; i < values.Length; i++)
            cmd.Parameters.AddWithValue("$" + (start + i), values[i] ?? (object)DBNull.Value);
    }

    public static void SoftDeleteProperty(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE properties SET is_deleted=1, deleted_at=$1 WHERE id=$2";
        cmd.Parameters.AddWithValue("$1", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("$2", id);
        cmd.ExecuteNonQuery();
    }

    public static void RestoreProperty(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE properties SET is_deleted=0, deleted_at=NULL WHERE id=$1";
        cmd.Parameters.AddWithValue("$1", id);
        cmd.ExecuteNonQuery();
    }

    public static void PermanentDeleteProperty(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM properties WHERE id=$1";
        cmd.Parameters.AddWithValue("$1", id);
        cmd.ExecuteNonQuery();
    }

    // ---------- 月度收租 / 水电计划 ----------
    public static List<RentMonthRecord> GetRentRecords(long propertyId)
    {
        var list = new List<RentMonthRecord>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,property_id,year,month,amount,is_paid,paid_date,note FROM rent_month_records WHERE property_id=$1 ORDER BY year,month";
        cmd.Parameters.AddWithValue("$1", propertyId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new RentMonthRecord { Id = r.GetInt64(0), PropertyId = r.GetInt64(1), Year = r.GetInt32(2), Month = r.GetInt32(3), Amount = r.GetDecimal(4), IsPaid = r.GetInt32(5) == 1, PaidDate = ParseDate(r, 6), Note = r.GetString(7) });
        return list;
    }

    public static void SaveRentRecord(RentMonthRecord rec)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        if (rec.Id == 0)
        {
            cmd.CommandText = "INSERT INTO rent_month_records (property_id,year,month,amount,is_paid,paid_date,note) VALUES ($1,$2,$3,$4,$5,$6,$7)";
        }
        else
        {
            cmd.CommandText = "UPDATE rent_month_records SET property_id=$1,year=$2,month=$3,amount=$4,is_paid=$5,paid_date=$6,note=$7 WHERE id=$8";
            cmd.Parameters.AddWithValue("$8", rec.Id);
        }
        cmd.Parameters.AddWithValue("$1", rec.PropertyId);
        cmd.Parameters.AddWithValue("$2", rec.Year);
        cmd.Parameters.AddWithValue("$3", rec.Month);
        cmd.Parameters.AddWithValue("$4", rec.Amount);
        cmd.Parameters.AddWithValue("$5", rec.IsPaid ? 1 : 0);
        cmd.Parameters.AddWithValue("$6", rec.PaidDate?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$7", rec.Note);
        cmd.ExecuteNonQuery();
    }

    public static void DeleteRentRecords(long propertyId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM rent_month_records WHERE property_id=$1";
        cmd.Parameters.AddWithValue("$1", propertyId);
        cmd.ExecuteNonQuery();
    }

    public static List<UtilitySettlementRecord> GetUtilityRecords(long propertyId)
    {
        var list = new List<UtilitySettlementRecord>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,property_id,year,period,water_amount,electric_amount,water_usage,electric_usage,is_settled,settled_date FROM utility_settlement_records WHERE property_id=$1 ORDER BY year,period";
        cmd.Parameters.AddWithValue("$1", propertyId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new UtilitySettlementRecord { Id = r.GetInt64(0), PropertyId = r.GetInt64(1), Year = r.GetInt32(2), Period = r.GetInt32(3), WaterAmount = r.GetDecimal(4), ElectricAmount = r.GetDecimal(5), WaterUsage = r.GetDecimal(6), ElectricUsage = r.GetDecimal(7), IsSettled = r.GetInt32(8) == 1, SettledDate = ParseDate(r, 9) });
        return list;
    }

    public static void SaveUtilityRecord(UtilitySettlementRecord rec)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        if (rec.Id == 0)
            cmd.CommandText = "INSERT INTO utility_settlement_records (property_id,year,period,water_amount,electric_amount,water_usage,electric_usage,is_settled,settled_date) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9)";
        else
        {
            cmd.CommandText = "UPDATE utility_settlement_records SET property_id=$1,year=$2,period=$3,water_amount=$4,electric_amount=$5,water_usage=$6,electric_usage=$7,is_settled=$8,settled_date=$9 WHERE id=$10";
            cmd.Parameters.AddWithValue("$10", rec.Id);
        }
        cmd.Parameters.AddWithValue("$1", rec.PropertyId);
        cmd.Parameters.AddWithValue("$2", rec.Year);
        cmd.Parameters.AddWithValue("$3", rec.Period);
        cmd.Parameters.AddWithValue("$4", rec.WaterAmount);
        cmd.Parameters.AddWithValue("$5", rec.ElectricAmount);
        cmd.Parameters.AddWithValue("$6", rec.WaterUsage);
        cmd.Parameters.AddWithValue("$7", rec.ElectricUsage);
        cmd.Parameters.AddWithValue("$8", rec.IsSettled ? 1 : 0);
        cmd.Parameters.AddWithValue("$9", rec.SettledDate?.ToString("o") ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public static void DeleteUtilityRecords(long propertyId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM utility_settlement_records WHERE property_id=$1";
        cmd.Parameters.AddWithValue("$1", propertyId);
        cmd.ExecuteNonQuery();
    }

    // ============================================================
    // 包租
    // ============================================================

    public static List<PayoutRecord> GetPayouts(bool includeDeleted = false)
    {
        var list = new List<PayoutRecord>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = includeDeleted
            ? "SELECT * FROM payouts ORDER BY rent_due_day, room_number"
            : "SELECT * FROM payouts WHERE is_deleted = 0 ORDER BY rent_due_day, room_number";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadPayout(r));
        return list;
    }

    private static PayoutRecord ReadPayout(SqliteDataReader r)
    {
        return new PayoutRecord
        {
            Id = r.GetInt64(0), RoomNumber = r.GetString(1), Manager = r.GetString(2), UnitType = r.GetString(3),
            PaymentMethod = r.GetString(4), AnnualRent = r.GetInt32(5), MonthlyRent = r.IsDBNull(6) ? null : r.GetInt32(6),
            Deposit = r.GetInt32(7), RentFreeDays = r.GetInt32(8),
            LeaseStartDate = DateTime.TryParse(r.GetString(9), out var a) ? a : DateTime.Now,
            LeaseEndDate = DateTime.TryParse(r.GetString(10), out var b) ? b : DateTime.Now,
            LeaseDuration = r.GetString(11), RentDueDay = r.GetInt32(12),
            WaterMeterBase = r.GetDecimal(13), ElectricMeterBase = r.GetDecimal(14), Notes = r.GetString(15),
            IsDeleted = r.GetInt32(16) == 1, DeletedAt = ParseDate(r, 17), UpdatedAt = ParseDate(r, 18) ?? DateTime.Now,
        };
    }

    public static long SavePayout(PayoutRecord p)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        p.UpdatedAt = DateTime.Now;
        if (p.Id == 0)
        {
            cmd.CommandText = """
                INSERT INTO payouts (room_number,manager,unit_type,payment_method,annual_rent,monthly_rent,deposit,
                    rent_free_days,lease_start_date,lease_end_date,lease_duration,rent_due_day,water_meter_base,
                    electric_meter_base,notes,is_deleted,deleted_at,updated_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18)
                """;
        }
        else
        {
            cmd.CommandText = """
                UPDATE payouts SET room_number=$1,manager=$2,unit_type=$3,payment_method=$4,annual_rent=$5,
                    monthly_rent=$6,deposit=$7,rent_free_days=$8,lease_start_date=$9,lease_end_date=$10,
                    lease_duration=$11,rent_due_day=$12,water_meter_base=$13,electric_meter_base=$14,notes=$15,
                    is_deleted=$16,deleted_at=$17,updated_at=$18 WHERE id=$19
                """;
            cmd.Parameters.AddWithValue("$19", p.Id);
        }
        cmd.Parameters.AddWithValue("$1", p.RoomNumber);
        cmd.Parameters.AddWithValue("$2", p.Manager);
        cmd.Parameters.AddWithValue("$3", p.UnitType);
        cmd.Parameters.AddWithValue("$4", p.PaymentMethod);
        cmd.Parameters.AddWithValue("$5", p.AnnualRent);
        cmd.Parameters.AddWithValue("$6", (object?)p.MonthlyRent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$7", p.Deposit);
        cmd.Parameters.AddWithValue("$8", p.RentFreeDays);
        cmd.Parameters.AddWithValue("$9", p.LeaseStartDate.ToString("o"));
        cmd.Parameters.AddWithValue("$10", p.LeaseEndDate.ToString("o"));
        cmd.Parameters.AddWithValue("$11", p.LeaseDuration);
        cmd.Parameters.AddWithValue("$12", p.RentDueDay);
        cmd.Parameters.AddWithValue("$13", p.WaterMeterBase);
        cmd.Parameters.AddWithValue("$14", p.ElectricMeterBase);
        cmd.Parameters.AddWithValue("$15", p.Notes);
        cmd.Parameters.AddWithValue("$16", p.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$17", p.DeletedAt?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$18", p.UpdatedAt.ToString("o"));
        cmd.ExecuteNonQuery();
        if (p.Id == 0) p.Id = LastInsertId(conn);
        return p.Id;
    }

    public static void SoftDeletePayout(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE payouts SET is_deleted=1, deleted_at=$1 WHERE id=$2";
        cmd.Parameters.AddWithValue("$1", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("$2", id);
        cmd.ExecuteNonQuery();
    }

    public static void RestorePayout(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE payouts SET is_deleted=0, deleted_at=NULL WHERE id=$1";
        cmd.Parameters.AddWithValue("$1", id);
        cmd.ExecuteNonQuery();
    }

    public static void PermanentDeletePayout(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM payouts WHERE id=$1";
        cmd.Parameters.AddWithValue("$1", id);
        cmd.ExecuteNonQuery();
    }

    public static List<MonthlyPayout> GetMonthlyPayouts(long payoutId)
    {
        var list = new List<MonthlyPayout>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,payout_id,month,amount,is_paid,paid_date FROM monthly_payouts WHERE payout_id=$1 ORDER BY month";
        cmd.Parameters.AddWithValue("$1", payoutId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new MonthlyPayout { Id = r.GetInt64(0), PayoutId = r.GetInt64(1), Month = r.GetInt32(2), Amount = r.GetDecimal(3), IsPaid = r.GetInt32(4) == 1, PaidDate = ParseDate(r, 5) });
        return list;
    }

    public static void SaveMonthlyPayout(MonthlyPayout rec)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        if (rec.Id == 0)
            cmd.CommandText = "INSERT INTO monthly_payouts (payout_id,month,amount,is_paid,paid_date) VALUES ($1,$2,$3,$4,$5)";
        else
        {
            cmd.CommandText = "UPDATE monthly_payouts SET payout_id=$1,month=$2,amount=$3,is_paid=$4,paid_date=$5 WHERE id=$6";
            cmd.Parameters.AddWithValue("$6", rec.Id);
        }
        cmd.Parameters.AddWithValue("$1", rec.PayoutId);
        cmd.Parameters.AddWithValue("$2", rec.Month);
        cmd.Parameters.AddWithValue("$3", rec.Amount);
        cmd.Parameters.AddWithValue("$4", rec.IsPaid ? 1 : 0);
        cmd.Parameters.AddWithValue("$5", rec.PaidDate?.ToString("o") ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public static List<MonthlyUtilityRecord> GetMonthlyUtilityRecords(long payoutId)
    {
        var list = new List<MonthlyUtilityRecord>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,payout_id,date,water_amount,electric_amount,is_settled FROM monthly_utility_records WHERE payout_id=$1 ORDER BY date";
        cmd.Parameters.AddWithValue("$1", payoutId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new MonthlyUtilityRecord { Id = r.GetInt64(0), PayoutId = r.GetInt64(1), Date = DateTime.TryParse(r.GetString(2), out var d) ? d : DateTime.Now, WaterAmount = r.GetDecimal(3), ElectricAmount = r.GetDecimal(4), IsSettled = r.GetInt32(5) == 1 });
        return list;
    }

    public static void SaveMonthlyUtilityRecord(MonthlyUtilityRecord rec)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        if (rec.Id == 0)
            cmd.CommandText = "INSERT INTO monthly_utility_records (payout_id,date,water_amount,electric_amount,is_settled) VALUES ($1,$2,$3,$4,$5)";
        else
        {
            cmd.CommandText = "UPDATE monthly_utility_records SET payout_id=$1,date=$2,water_amount=$3,electric_amount=$4,is_settled=$5 WHERE id=$6";
            cmd.Parameters.AddWithValue("$6", rec.Id);
        }
        cmd.Parameters.AddWithValue("$1", rec.PayoutId);
        cmd.Parameters.AddWithValue("$2", rec.Date.ToString("o"));
        cmd.Parameters.AddWithValue("$3", rec.WaterAmount);
        cmd.Parameters.AddWithValue("$4", rec.ElectricAmount);
        cmd.Parameters.AddWithValue("$5", rec.IsSettled ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    // ============================================================
    // 成交 / 杂项
    // ============================================================

    public static List<DealRecord> GetDeals(bool includeDeleted = false)
    {
        var list = new List<DealRecord>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = includeDeleted
            ? "SELECT * FROM deals ORDER BY date DESC"
            : "SELECT * FROM deals WHERE is_deleted = 0 ORDER BY date DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new DealRecord { Id = r.GetInt64(0), Date = DateTime.TryParse(r.GetString(1), out var d) ? d : DateTime.Now, RoomNumber = r.GetString(2), Customer = r.GetString(3), Type = r.GetString(4), Amount = r.GetDecimal(5), AgentFee = r.GetDecimal(6), Note = r.GetString(7), IsDeleted = r.GetInt32(8) == 1, DeletedAt = ParseDate(r, 9), UpdatedAt = ParseDate(r, 10) ?? DateTime.Now });
        return list;
    }

    public static long SaveDeal(DealRecord d)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        d.UpdatedAt = DateTime.Now;
        if (d.Id == 0)
            cmd.CommandText = "INSERT INTO deals (date,room_number,customer,type,amount,agent_fee,note,is_deleted,deleted_at,updated_at) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)";
        else
        {
            cmd.CommandText = "UPDATE deals SET date=$1,room_number=$2,customer=$3,type=$4,amount=$5,agent_fee=$6,note=$7,is_deleted=$8,deleted_at=$9,updated_at=$10 WHERE id=$11";
            cmd.Parameters.AddWithValue("$11", d.Id);
        }
        cmd.Parameters.AddWithValue("$1", d.Date.ToString("o"));
        cmd.Parameters.AddWithValue("$2", d.RoomNumber);
        cmd.Parameters.AddWithValue("$3", d.Customer);
        cmd.Parameters.AddWithValue("$4", d.Type);
        cmd.Parameters.AddWithValue("$5", d.Amount);
        cmd.Parameters.AddWithValue("$6", d.AgentFee);
        cmd.Parameters.AddWithValue("$7", d.Note);
        cmd.Parameters.AddWithValue("$8", d.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$9", d.DeletedAt?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$10", d.UpdatedAt.ToString("o"));
        cmd.ExecuteNonQuery();
        if (d.Id == 0) d.Id = LastInsertId(conn);
        return d.Id;
    }

    public static void SoftDeleteDeal(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE deals SET is_deleted=1, deleted_at=$1 WHERE id=$2";
        cmd.Parameters.AddWithValue("$1", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("$2", id);
        cmd.ExecuteNonQuery();
    }

    public static void RestoreDeal(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE deals SET is_deleted=0, deleted_at=NULL WHERE id=$1";
        cmd.Parameters.AddWithValue("$1", id);
        cmd.ExecuteNonQuery();
    }

    public static void PermanentDeleteDeal(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM deals WHERE id=$1";
        cmd.Parameters.AddWithValue("$1", id);
        cmd.ExecuteNonQuery();
    }

    // ---------- 杂项收支 ----------
    public static List<MiscIncome> GetMiscIncomes(bool includeDeleted = false)
    {
        var list = new List<MiscIncome>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = includeDeleted ? "SELECT * FROM misc_incomes ORDER BY date DESC" : "SELECT * FROM misc_incomes WHERE is_deleted=0 ORDER BY date DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new MiscIncome { Id = r.GetInt64(0), Date = DateTime.TryParse(r.GetString(1), out var d) ? d : DateTime.Now, Amount = r.GetDecimal(2), Note = r.GetString(3), IsDeleted = r.GetInt32(4) == 1, DeletedAt = ParseDate(r, 5) });
        return list;
    }

    public static void SaveMiscIncome(MiscIncome m)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        if (m.Id == 0) cmd.CommandText = "INSERT INTO misc_incomes (date,amount,note,is_deleted,deleted_at) VALUES ($1,$2,$3,$4,$5)";
        else { cmd.CommandText = "UPDATE misc_incomes SET date=$1,amount=$2,note=$3,is_deleted=$4,deleted_at=$5 WHERE id=$6"; cmd.Parameters.AddWithValue("$6", m.Id); }
        cmd.Parameters.AddWithValue("$1", m.Date.ToString("o"));
        cmd.Parameters.AddWithValue("$2", m.Amount);
        cmd.Parameters.AddWithValue("$3", m.Note);
        cmd.Parameters.AddWithValue("$4", m.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$5", m.DeletedAt?.ToString("o") ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public static List<MiscExpense> GetMiscExpenses(bool includeDeleted = false)
    {
        var list = new List<MiscExpense>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = includeDeleted ? "SELECT * FROM misc_expenses ORDER BY date DESC" : "SELECT * FROM misc_expenses WHERE is_deleted=0 ORDER BY date DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new MiscExpense { Id = r.GetInt64(0), Date = DateTime.TryParse(r.GetString(1), out var d) ? d : DateTime.Now, Amount = r.GetDecimal(2), Note = r.GetString(3), IsDeleted = r.GetInt32(4) == 1, DeletedAt = ParseDate(r, 5) });
        return list;
    }

    public static void SaveMiscExpense(MiscExpense m)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        if (m.Id == 0) cmd.CommandText = "INSERT INTO misc_expenses (date,amount,note,is_deleted,deleted_at) VALUES ($1,$2,$3,$4,$5)";
        else { cmd.CommandText = "UPDATE misc_expenses SET date=$1,amount=$2,note=$3,is_deleted=$4,deleted_at=$5 WHERE id=$6"; cmd.Parameters.AddWithValue("$6", m.Id); }
        cmd.Parameters.AddWithValue("$1", m.Date.ToString("o"));
        cmd.Parameters.AddWithValue("$2", m.Amount);
        cmd.Parameters.AddWithValue("$3", m.Note);
        cmd.Parameters.AddWithValue("$4", m.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$5", m.DeletedAt?.ToString("o") ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    // ============================================================
    // 待办 / 水电历史 / 日志 / 分享记录
    // ============================================================

    public static List<TodoItem> GetTodos(bool includeDeleted = false)
    {
        var list = new List<TodoItem>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = includeDeleted
            ? "SELECT * FROM todos ORDER BY is_completed, sort_order, created_at DESC"
            : "SELECT * FROM todos WHERE is_deleted=0 ORDER BY is_completed, sort_order, created_at DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new TodoItem { Id = r.GetInt64(0), Title = r.GetString(1), Note = r.GetString(2), IsCompleted = r.GetInt32(3) == 1, DueDate = ParseDate(r, 4), HasReminder = r.GetInt32(5) == 1, SortOrder = r.GetInt32(6), IsDeleted = r.GetInt32(7) == 1, DeletedAt = ParseDate(r, 8), CreatedAt = ParseDate(r, 9) ?? DateTime.Now });
        return list;
    }

    public static long SaveTodo(TodoItem t)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        if (t.Id == 0) cmd.CommandText = "INSERT INTO todos (title,note,is_completed,due_date,has_reminder,sort_order,is_deleted,deleted_at,created_at) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9)";
        else { cmd.CommandText = "UPDATE todos SET title=$1,note=$2,is_completed=$3,due_date=$4,has_reminder=$5,sort_order=$6,is_deleted=$7,deleted_at=$8 WHERE id=$9"; cmd.Parameters.AddWithValue("$9", t.Id); }
        cmd.Parameters.AddWithValue("$1", t.Title);
        cmd.Parameters.AddWithValue("$2", t.Note);
        cmd.Parameters.AddWithValue("$3", t.IsCompleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$4", t.DueDate?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$5", t.HasReminder ? 1 : 0);
        cmd.Parameters.AddWithValue("$6", t.SortOrder);
        cmd.Parameters.AddWithValue("$7", t.IsDeleted ? 1 : 0);
        cmd.Parameters.AddWithValue("$8", t.DeletedAt?.ToString("o") ?? (object)DBNull.Value);
        cmd.ExecuteNonQuery();
        if (t.Id == 0) t.Id = LastInsertId(conn);
        return t.Id;
    }

    public static void SoftDeleteTodo(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE todos SET is_deleted=1, deleted_at=$1 WHERE id=$2";
        cmd.Parameters.AddWithValue("$1", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("$2", id);
        cmd.ExecuteNonQuery();
    }

    public static void RestoreTodo(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE todos SET is_deleted=0, deleted_at=NULL WHERE id=$1";
        cmd.Parameters.AddWithValue("$1", id);
        cmd.ExecuteNonQuery();
    }

    public static void PermanentDeleteTodo(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM todos WHERE id=$1";
        cmd.Parameters.AddWithValue("$1", id);
        cmd.ExecuteNonQuery();
    }

    public static List<UtilitySettlementHistory> GetUtilityHistories()
    {
        var list = new List<UtilitySettlementHistory>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,date,room_number,water_amount,electric_amount,water_usage,electric_usage,note FROM utility_histories ORDER BY date DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new UtilitySettlementHistory { Id = r.GetInt64(0), Date = DateTime.TryParse(r.GetString(1), out var d) ? d : DateTime.Now, RoomNumber = r.GetString(2), WaterAmount = r.GetDecimal(3), ElectricAmount = r.GetDecimal(4), WaterUsage = r.GetDecimal(5), ElectricUsage = r.GetDecimal(6), Note = r.GetString(7) });
        return list;
    }

    public static void AddUtilityHistory(UtilitySettlementHistory h)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO utility_histories (date,room_number,water_amount,electric_amount,water_usage,electric_usage,note) VALUES ($1,$2,$3,$4,$5,$6,$7)";
        cmd.Parameters.AddWithValue("$1", h.Date.ToString("o"));
        cmd.Parameters.AddWithValue("$2", h.RoomNumber);
        cmd.Parameters.AddWithValue("$3", h.WaterAmount);
        cmd.Parameters.AddWithValue("$4", h.ElectricAmount);
        cmd.Parameters.AddWithValue("$5", h.WaterUsage);
        cmd.Parameters.AddWithValue("$6", h.ElectricUsage);
        cmd.Parameters.AddWithValue("$7", h.Note);
        cmd.ExecuteNonQuery();
    }

    public static void AddLog(string category, string action, string result, string detail, string room = "")
    {
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO operation_logs (time,category,action,result,detail,room) VALUES ($1,$2,$3,$4,$5,$6)";
            cmd.Parameters.AddWithValue("$1", DateTime.Now.ToString("o"));
            cmd.Parameters.AddWithValue("$2", category);
            cmd.Parameters.AddWithValue("$3", action);
            cmd.Parameters.AddWithValue("$4", result);
            cmd.Parameters.AddWithValue("$5", detail);
            cmd.Parameters.AddWithValue("$6", room);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    public static List<OperationLog> GetLogs()
    {
        var list = new List<OperationLog>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,time,category,action,result,detail,room FROM operation_logs ORDER BY time DESC LIMIT 500";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new OperationLog { Id = r.GetInt64(0), Time = DateTime.TryParse(r.GetString(1), out var d) ? d : DateTime.Now, Category = r.GetString(2), Action = r.GetString(3), Result = r.GetString(4), Detail = r.GetString(5), Room = r.GetString(6) });
        return list;
    }

    public static void DeleteLogs(List<long> ids)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM operation_logs WHERE id IN ({string.Join(",", ids)})";
        cmd.ExecuteNonQuery();
    }

    public static void AddShareRecord(string roomNo, string mode, string link)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO share_records (room_no,mode,time,link) VALUES ($1,$2,$3,$4)";
        cmd.Parameters.AddWithValue("$1", roomNo);
        cmd.Parameters.AddWithValue("$2", mode);
        cmd.Parameters.AddWithValue("$3", DateTime.Now.ToString("o"));
        cmd.Parameters.AddWithValue("$4", link);
        cmd.ExecuteNonQuery();
    }

    public static List<ShareRecord> GetShareRecords()
    {
        var list = new List<ShareRecord>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,room_no,mode,time,link FROM share_records ORDER BY time DESC LIMIT 300";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ShareRecord { Id = r.GetInt64(0), RoomNo = r.GetString(1), Mode = r.GetString(2), Time = DateTime.TryParse(r.GetString(3), out var d) ? d : DateTime.Now, Link = r.GetString(4) });
        return list;
    }

    // 通用
    public static long Count<T>(string table) where T : class
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE is_deleted=0";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public static long SumDecimal(string table, string column)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COALESCE(SUM({column}),0) FROM {table} WHERE is_deleted=0";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
}

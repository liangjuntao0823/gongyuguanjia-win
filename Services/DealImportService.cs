using System;
using System.Linq;
using ClosedXML.Excel;
using ApartmentManagerWin.Models;
using ApartmentManagerWin.Services;

namespace ApartmentManagerWin.Services;

/// <summary>成交 Excel 导入（对齐 Mac/iOS 导入模板）</summary>
public static class DealImportService
{
    public static int Import(string path)
    {
        int count = 0;
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.First();
        var rows = ws.RangeUsed()?.RowsUsed() ?? Enumerable.Empty<IXLRangeRow>();
        foreach (var row in rows.Skip(1)) // 跳过表头
        {
            var dateStr = row.Cell(1).GetString();
            var d = new DealRecord
            {
                Date = DateTime.TryParse(dateStr, out var dt) ? dt : DateTime.Now,
                RoomNumber = row.Cell(2).GetString(),
                Customer = row.Cell(3).GetString(),
                Type = row.Cell(4).GetString(),
                Amount = row.Cell(5).GetValue<decimal>(),
                AgentFee = row.Cell(6).GetValue<decimal>(),
                Note = row.Cell(7).GetString(),
            };
            if (string.IsNullOrEmpty(d.RoomNumber) && string.IsNullOrEmpty(d.Customer)) continue;
            AppDb.SaveDeal(d);
            count++;
        }
        AppDb.AddLog("成交", "导入Excel", "成功", $"导入 {count} 条");
        AppDb.NotifyDataChanged();
        return count;
    }
}

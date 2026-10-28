using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using ApartmentManagerWin.Models;

namespace ApartmentManagerWin.Services;

// ============================================================
// 导出服务（Excel / 图片 / 二维码 / 分享卡片）
// ============================================================

public static class ExportService
{
    // ---------- Excel 导出（成交/包租/收租） ----------

    public static string ExportDealsExcel(IEnumerable<DealRecord> deals, int? year, int? month)
    {
        var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("成交记录");
        var headers = new[] { "日期", "房号", "客户", "类型", "成交金额", "中介费", "备注" };
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
        }
        int row = 2;
        foreach (var d in deals)
        {
            ws.Cell(row, 1).Value = d.Date.ToString("yyyy-MM-dd");
            ws.Cell(row, 2).Value = d.RoomNumber;
            ws.Cell(row, 3).Value = d.Customer;
            ws.Cell(row, 4).Value = d.Type;
            ws.Cell(row, 5).Value = (double)d.Amount;
            ws.Cell(row, 6).Value = (double)d.AgentFee;
            ws.Cell(row, 7).Value = d.Note;
            row++;
        }
        ws.Columns().AdjustToContents();
        var path = Path.Combine(DataRoot.BackupsDirectory, $"成交记录导出-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx");
        Directory.CreateDirectory(DataRoot.BackupsDirectory);
        wb.SaveAs(path);
        AppDb.AddLog("成交", "导出Excel", "成功", $"{year}-{month} 共{deals.Count()}条");
        return path;
    }

    public static string ExportPayoutsExcel(IEnumerable<PayoutRecord> payouts)
    {
        var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("包租合同");
        var headers = new[] { "房号", "包租人", "户型", "付款方式", "年租金", "月租金", "押金", "免租期", "租期开始", "租期结束", "交租日", "备注" };
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
        }
        int row = 2;
        foreach (var p in payouts)
        {
            ws.Cell(row, 1).Value = p.RoomNumber;
            ws.Cell(row, 2).Value = p.Manager;
            ws.Cell(row, 3).Value = p.UnitType;
            ws.Cell(row, 4).Value = p.PaymentMethod;
            ws.Cell(row, 5).Value = p.AnnualRent;
            ws.Cell(row, 6).Value = (double)(p.MonthlyRent ?? 0);
            ws.Cell(row, 7).Value = p.Deposit;
            ws.Cell(row, 8).Value = p.RentFreeDays;
            ws.Cell(row, 9).Value = p.LeaseStartDate.ToString("yyyy-MM-dd");
            ws.Cell(row, 10).Value = p.LeaseEndDate.ToString("yyyy-MM-dd");
            ws.Cell(row, 11).Value = p.RentDueDay;
            ws.Cell(row, 12).Value = p.Notes;
            row++;
        }
        ws.Columns().AdjustToContents();
        var path = Path.Combine(DataRoot.BackupsDirectory, $"包租合同导出-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx");
        wb.SaveAs(path);
        AppDb.AddLog("包租", "导出Excel", "成功", $"共{payouts.Count()}条");
        return path;
    }

    public static string ExportPropertiesExcel(IEnumerable<Property> props)
    {
        var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("收租房源");
        var headers = new[] { "房号", "房东", "户型", "类型", "租金", "押金", "预存", "交租日", "租期开始", "租期结束", "备注", "状态" };
        for (int i = 0; i < headers.Length; i++)
        {
            ws.Cell(1, i + 1).Value = headers[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
            ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
        }
        int row = 2;
        foreach (var p in props)
        {
            ws.Cell(row, 1).Value = p.RoomNumber;
            ws.Cell(row, 2).Value = p.Landlord;
            ws.Cell(row, 3).Value = p.UnitType;
            ws.Cell(row, 4).Value = p.Type;
            ws.Cell(row, 5).Value = p.Rent;
            ws.Cell(row, 6).Value = p.Deposit;
            ws.Cell(row, 7).Value = p.Prepayment;
            ws.Cell(row, 8).Value = p.RentDueDay;
            ws.Cell(row, 9).Value = p.LeaseStart;
            ws.Cell(row, 10).Value = p.LeaseEnd;
            ws.Cell(row, 11).Value = p.Notes;
            ws.Cell(row, 12).Value = p.IsTerminated ? "已退租" : "在租";
            row++;
        }
        ws.Columns().AdjustToContents();
        var path = Path.Combine(DataRoot.BackupsDirectory, $"收租房源导出-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx");
        wb.SaveAs(path);
        AppDb.AddLog("收租", "导出Excel", "成功", $"共{props.Count()}条");
        return path;
    }

    // ---------- 二维码 ----------

    public static byte[] QrCodePng(string text, int size = 300)
    {
        using var gen = new QRCoder.QRCodeGenerator();
        var data = gen.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
        using var qr = new QRCoder.PngByteQRCode(data);
        return qr.GetGraphic(20);
    }

    // ---------- 分享卡片（脱敏/完整） ----------

    public static byte[] RenderShareCard(House h, string link, bool masked)
    {
        // 极简卡片：房号 + 租金 + 状态（脱敏版隐藏房东/门牌细节）
        var lines = new List<string>
        {
            $"米兰公馆 {h.Tower}座",
            $"房号：{h.RoomNo}",
            $"户型：{h.Layout}",
            $"租金：{h.Price} 元/月",
        };
        if (!masked) lines.Add($"房东：{h.Landlord}");
        lines.Add("");
        lines.Add("点击链接查看详情");
        return RenderTextCard(string.Join("\n", lines), link);
    }

    private static byte[] RenderTextCard(string text, string link)
    {
        // 用 SkiaSharp 绘制白底卡片（含二维码）
        var w = 800; var h = 1000;
        using var bmp = new SkiaSharp.SKBitmap(w, h);
        using var canvas = new SkiaSharp.SKCanvas(bmp);
        canvas.Clear(SkiaSharp.SKColors.White);
        using var paint = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Black, IsAntialias = true };
        using var font = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.FromFamilyName("Microsoft YaHei"), 42);
        var y = 80f;
        foreach (var line in text.Split('\n'))
        {
            canvas.DrawText(line, 60, y, SkiaSharp.SKTextAlign.Left, font, paint);
            y += 66;
        }
        // 二维码
        try
        {
            var qr = QrCodePng(link, 200);
            var img = SkiaSharp.SKImage.FromEncodedData(qr);
            canvas.DrawImage(img, new SkiaSharp.SKRect(250, y + 40, 550, y + 340));
        }
        catch { }
        using var image = SkiaSharp.SKImage.FromBitmap(bmp);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    // ---------- 导出图片（列表截图式：把记录渲染为图片） ----------

    public static byte[] RenderRecordsImage(string title, List<(string left, string right)> rows)
    {
        var w = 900; var h = 120 + rows.Count * 64;
        using var bmp = new SkiaSharp.SKBitmap(w, h);
        using var canvas = new SkiaSharp.SKCanvas(bmp);
        canvas.Clear(SkiaSharp.SKColors.White);
        using var paint = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Black, IsAntialias = true };
        using var font = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.FromFamilyName("Microsoft YaHei"), 40);
        canvas.DrawText(title, 40, 70, SkiaSharp.SKTextAlign.Left, font, paint);
        using var linePaint = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(230, 230, 230), StrokeWidth = 1 };
        var y = 110f;
        foreach (var (l, r) in rows)
        {
            canvas.DrawLine(40, y - 8, w - 40, y - 8, linePaint);
            using var lfont = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.FromFamilyName("Microsoft YaHei"), 32);
            canvas.DrawText(l, 40, y + 24, SkiaSharp.SKTextAlign.Left, lfont, paint);
            using var rfont = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.FromFamilyName("Microsoft YaHei"), 32);
            canvas.DrawText(r, w - 40, y + 24, SkiaSharp.SKTextAlign.Right, rfont, paint);
            y += 64;
        }
        using var image = SkiaSharp.SKImage.FromBitmap(bmp);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    public static string SavePng(byte[] data, string name)
    {
        var dir = DataRoot.BackupsDirectory;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        File.WriteAllBytes(path, data);
        return path;
    }

    // ---------- 房源媒体缩略图 ----------

    public static byte[]? MakeThumbnail(string filePath, int size = 400)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            using var src = SkiaSharp.SKImage.FromEncodedData(filePath);
            if (src == null) return null;
            var w = src.Width; var h = src.Height;
            if (w <= 0 || h <= 0) return null;
            var scale = Math.Min((float)size / w, (float)size / h);
            var nw = Math.Max(1, (int)(w * scale)); var nh = Math.Max(1, (int)(h * scale));
            using var bmp = new SkiaSharp.SKBitmap(nw, nh);
            src.ScalePixels(bmp.PeekPixels(), new SkiaSharp.SKSamplingOptions(SkiaSharp.SKFilterMode.Linear, SkiaSharp.SKMipmapMode.Linear));
            using var image = SkiaSharp.SKImage.FromBitmap(bmp);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 85);
            return data.ToArray();
        }
        catch { return null; }
    }

    /// 视频缩略图：提取首帧（无 ffmpeg 时返回 null，UI 显示占位）
    public static byte[]? VideoFirstFrame(string filePath, int size = 400)
    {
        try
        {
            // 首帧提取依赖系统 ffmpeg（可用则用）
            var ffmpeg = FindFfmpeg();
            if (ffmpeg == null) return null;
            var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jpg");
            var psi = new System.Diagnostics.ProcessStartInfo(ffmpeg)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Arguments = $"-y -i \"{filePath}\" -vframes 1 -vf \"scale={size}:-1\" \"{tmp}\""
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p != null) p.WaitForExit(8000);
            if (File.Exists(tmp))
            {
                var data = File.ReadAllBytes(tmp);
                try { File.Delete(tmp); } catch { }
                return data;
            }
            return null;
        }
        catch { return null; }
    }

    private static string? FindFfmpeg()
    {
        foreach (var name in new[] { "ffmpeg", "ffmpeg.exe" })
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(name) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, Arguments = "-version" };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p != null) { p.WaitForExit(2000); if (p.ExitCode == 0) return name; }
            }
            catch { }
        }
        return null;
    }

    /// v1.0：通用 Excel 导出（列头 + 行数据）
    public static string ExportGenericExcel(string sheetName, string[] headers, List<object?[]> rows)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "公寓管家导出");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{sheetName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);
        for (int c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        for (int r = 0; r < rows.Count; r++)
            for (int c = 0; c < rows[r].Length; c++)
                ws.Cell(r + 2, c + 1).Value = rows[r][c]?.ToString() ?? "";
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
        return path;
    }
}

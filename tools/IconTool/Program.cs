using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

// ─── 编码初始化 ───
try
{
    if (OperatingSystem.IsWindows())
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var ansiCodePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
        var ansiEncoding = Encoding.GetEncoding(ansiCodePage);
        Console.OutputEncoding = ansiEncoding;
        Console.InputEncoding = ansiEncoding;
    }
    else
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
    }
}
catch { /* Best-effort */ }

// ─── 参数解析 ───
if (args.Length == 0)
{
    return RunAutoProcess();
}

var command = args[0].ToLowerInvariant();

return command switch
{
    "check" => RunCheck(args),
    "transparent" => RunTransparent(args),
    "crop" => RunCrop(args),
    "help" or "--help" or "-h" or "/?" => PrintUsageAndReturn(),
    _ => Error($"未知命令: {command}。使用 'IconTool help' 查看帮助。")
};

// ═══════════════════════════════════════════════════════════════════
// 帮助信息
// ═══════════════════════════════════════════════════════════════════

static int PrintUsageAndReturn() { PrintUsage(); return 0; }

static void PrintUsage()
{
    Console.WriteLine("""
    IconTool — PNG/ICO 透明度检测与透明化工具

    用法:
      IconTool                                 自动处理当前目录所有 PNG/JPG
      IconTool check <文件路径>                检测 PNG/ICO 是否包含透明通道
      IconTool transparent <文件路径> [选项]    将图片背景透明化并输出为 PNG
      IconTool crop <文件路径> [选项]           居中裁剪缩放到指定尺寸

    无参数自动模式:
      扫描当前目录的 *.png / *.jpg / *.jpeg 文件，自动执行以下流程：
      1. 检测是否已透明 → 已透明则跳过透明化
      2. 采样四周像素判断背景色 → 仅 white/black 才处理
      3. 去除背景色生成透明 PNG（覆盖原文件）
      4. 居中裁剪缩放到 512x512
      5. 生成同名 .ico 图标文件（16/32/48/256 四尺寸）
      6. 源文件（含已有 .ico）备份到按时间命名的子目录
      7. 生成详细处理日志

    check 命令:
      IconTool check logo.png                  检测 logo.png 的透明度
      IconTool check app.ico                   检测 ICO 中所有嵌入图像的透明度

    transparent 命令:
      IconTool transparent photo.png           去除白色背景，输出 photo_transparent.png
      IconTool transparent photo.jpg -o out.png        指定输出文件名
      IconTool transparent photo.png -c "#FF0000"      去除红色背景
      IconTool transparent photo.png -c white          去除白色背景(默认)
      IconTool transparent photo.png -t 30             设置颜色容差(默认 30)
      IconTool transparent icon.png -c black --flood   连通填充模式去黑色背景

    transparent 选项:
      -o, --output <路径>      输出文件路径 (默认: 原文件名_transparent.png)
      -c, --color <颜色>       要去除的背景色 (默认: white)
                               支持: white, black, #RRGGBB, #RRGGBBAA
      -t, --threshold <值>     颜色匹配容差 0-255 (默认: 30)
      --flood                  连通填充模式：仅从边缘可达的背景色区域被移除
                               适合圆角图标（避免删除内部深色内容）
                               自动模式默认使用此模式

    crop 命令:
      IconTool crop photo.png                  居中裁剪缩放到 512x512
      IconTool crop photo.png -s 256           裁剪到 256x256
      IconTool crop photo.png -o out.png       指定输出文件路径

    crop 选项:
      -s, --size <值>          目标正方形边长 (默认: 512)
      -o, --output <路径>      输出文件路径 (默认: 原文件名_cropped.png)

    示例:
      IconTool                                         自动处理当前目录
      IconTool check "C:\icons\app.ico"
      IconTool transparent banner.jpg -c white -t 50 -o banner_clean.png
      IconTool crop logo.png -s 512 -o logo_512.png
    """);
}

// ═══════════════════════════════════════════════════════════════════
// check 命令：检测透明度
// ═══════════════════════════════════════════════════════════════════

static int RunCheck(string[] args)
{
    if (args.Length < 2)
        return Error("用法: IconTool check <文件路径>");

    var filePath = args[1];
    if (!File.Exists(filePath))
        return Error($"文件不存在: {filePath}");

    var ext = Path.GetExtension(filePath).ToLowerInvariant();

    return ext switch
    {
        ".ico" => CheckIco(filePath),
        ".png" => CheckPng(filePath),
        _ => Error($"不支持的文件格式: {ext}。仅支持 .png 和 .ico。")
    };
}

static int CheckPng(string filePath)
{
    Console.WriteLine($"── 检测 PNG: {Path.GetFileName(filePath)} ──");
    Console.WriteLine();

    try
    {
        using var image = Image.Load<Rgba32>(filePath);
        var report = AnalyzeTransparency(image);
        PrintTransparencyReport(report, image.Width, image.Height);
        return 0;
    }
    catch (Exception ex)
    {
        return Error($"读取 PNG 失败: {ex.Message}");
    }
}

static int CheckIco(string filePath)
{
    Console.WriteLine($"── 检测 ICO: {Path.GetFileName(filePath)} ──");
    Console.WriteLine();

    try
    {
        var icoBytes = File.ReadAllBytes(filePath);

        // ICO 格式解析: ICONDIR (6 bytes) + N * ICONDIRENTRY (16 bytes each)
        if (icoBytes.Length < 6)
            return Error("ICO 文件太小，不是有效的 ICO 格式。");

        var reserved = BitConverter.ToUInt16(icoBytes, 0);
        var type = BitConverter.ToUInt16(icoBytes, 2);
        var count = BitConverter.ToUInt16(icoBytes, 4);

        if (reserved != 0 || type != 1)
            return Error("不是有效的 ICO 文件（头部标志不正确）。");

        Console.WriteLine($"ICO 包含 {count} 张嵌入图像");
        Console.WriteLine();

        for (int i = 0; i < count; i++)
        {
            var entryOffset = 6 + i * 16;
            if (entryOffset + 16 > icoBytes.Length)
            {
                Console.WriteLine($"  [图像 {i + 1}] 目录项超出文件范围，跳过。");
                continue;
            }

            var w = icoBytes[entryOffset] == 0 ? 256 : icoBytes[entryOffset];
            var h = icoBytes[entryOffset + 1] == 0 ? 256 : icoBytes[entryOffset + 1];
            var dataSize = BitConverter.ToInt32(icoBytes, entryOffset + 8);
            var dataOffset = BitConverter.ToInt32(icoBytes, entryOffset + 12);

            Console.WriteLine($"  [图像 {i + 1}] {w}x{h}, 数据大小={dataSize} 字节");

            if (dataOffset + dataSize > icoBytes.Length)
            {
                Console.WriteLine("    ⚠ 数据偏移超出文件范围，跳过。");
                Console.WriteLine();
                continue;
            }

            var imageData = new byte[dataSize];
            Array.Copy(icoBytes, dataOffset, imageData, 0, dataSize);

            try
            {
                using var ms = new MemoryStream(imageData);
                using var image = Image.Load<Rgba32>(ms);
                var report = AnalyzeTransparency(image);
                PrintTransparencyReport(report, image.Width, image.Height, indent: "    ");
            }
            catch
            {
                // 可能是 BMP 格式的嵌入图像，尝试直接分析位图数据
                Console.WriteLine("    ⚠ 无法解码嵌入图像（可能是旧式 BMP 格式），跳过透明度分析。");
            }

            Console.WriteLine();
        }

        return 0;
    }
    catch (Exception ex)
    {
        return Error($"读取 ICO 失败: {ex.Message}");
    }
}

// ═══════════════════════════════════════════════════════════════════
// transparent 命令：透明化处理
// ═══════════════════════════════════════════════════════════════════

static int RunTransparent(string[] args)
{
    if (args.Length < 2)
        return Error("用法: IconTool transparent <文件路径> [选项]");

    var filePath = args[1];
    if (!File.Exists(filePath))
        return Error($"文件不存在: {filePath}");

    // 解析选项
    string? outputPath = null;
    string colorName = "white";
    int threshold = 30;
    bool useFloodFill = false;

    for (int i = 2; i < args.Length; i++)
    {
        var arg = args[i].ToLowerInvariant();
        switch (arg)
        {
            case "-o" or "--output":
                if (i + 1 >= args.Length) return Error("-o/--output 需要一个参数。");
                outputPath = args[++i];
                break;
            case "-c" or "--color":
                if (i + 1 >= args.Length) return Error("-c/--color 需要一个参数。");
                colorName = args[++i];
                break;
            case "-t" or "--threshold":
                if (i + 1 >= args.Length) return Error("-t/--threshold 需要一个参数。");
                if (!int.TryParse(args[++i], out threshold) || threshold < 0 || threshold > 255)
                    return Error("阈值必须为 0-255 之间的整数。");
                break;
            case "--flood":
                useFloodFill = true;
                break;
            default:
                return Error($"未知选项: {args[i]}");
        }
    }

    // 确定输出路径
    if (string.IsNullOrEmpty(outputPath))
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";
        var name = Path.GetFileNameWithoutExtension(filePath);
        outputPath = Path.Combine(dir, $"{name}_transparent.png");
    }

    // 解析目标颜色
    if (!TryParseColor(colorName, out var targetColor))
        return Error($"无法解析颜色: {colorName}。支持 white, black, #RRGGBB, #RRGGBBAA。");

    Console.WriteLine($"── 透明化处理 ──");
    Console.WriteLine($"  输入: {filePath}");
    Console.WriteLine($"  去除背景色: {colorName} (R={targetColor.R}, G={targetColor.G}, B={targetColor.B})");
    Console.WriteLine($"  颜色容差: {threshold}");
    Console.WriteLine($"  模式: {(useFloodFill ? "连通填充（仅从边缘可达区域）" : "全局匹配")}");
    Console.WriteLine($"  输出: {outputPath}");
    Console.WriteLine();

    try
    {
        using var image = Image.Load<Rgba32>(filePath);
        Console.WriteLine($"  图像尺寸: {image.Width}x{image.Height}");

        int removedCount;
        int totalPixels = image.Width * image.Height;

        if (useFloodFill)
        {
            removedCount = FloodFillTransparent(image, targetColor, threshold);
        }
        else
        {
            removedCount = 0;
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        ref var pixel = ref row[x];
                        if (IsColorMatch(pixel, targetColor, threshold))
                        {
                            pixel = new Rgba32(0, 0, 0, 0);
                            removedCount++;
                        }
                        else if (pixel.A == 255)
                        {
                            var distance = ColorDistance(pixel, targetColor);
                            if (distance < threshold * 2)
                            {
                                var alpha = (byte)Math.Clamp((int)(255.0 * distance / (threshold * 2)), 0, 255);
                                pixel = new Rgba32(pixel.R, pixel.G, pixel.B, alpha);
                                if (alpha == 0) removedCount++;
                            }
                        }
                    }
                }
            });
        }

        double ratio = Math.Round((double)removedCount / totalPixels * 100, 2);
        Console.WriteLine($"  已透明化像素: {removedCount}/{totalPixels} ({ratio}%)");

        // 确保输出目录存在
        var outDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(outDir))
            Directory.CreateDirectory(outDir);

        var rgbaEncoder = new PngEncoder { ColorType = PngColorType.RgbWithAlpha };
        image.SaveAsPng(outputPath, rgbaEncoder);
        var fileSize = new FileInfo(outputPath).Length;
        Console.WriteLine($"  输出文件大小: {FormatFileSize(fileSize)}");
        Console.WriteLine();

        // 输出后自动做一次透明度检测
        Console.WriteLine("── 输出文件透明度检测 ──");
        Console.WriteLine();
        var report = AnalyzeTransparency(image);
        PrintTransparencyReport(report, image.Width, image.Height);

        Console.WriteLine();
        Console.WriteLine("✓ 透明化处理完成。");

        return 0;
    }
    catch (Exception ex)
    {
        return Error($"处理失败: {ex.Message}");
    }
}

// ═══════════════════════════════════════════════════════════════════
// 辅助函数
// ═══════════════════════════════════════════════════════════════════

static TransparencyReport AnalyzeTransparency(Image<Rgba32> image)
{
    int w = image.Width;
    int h = image.Height;
    int totalPixels = w * h;
    int transparentCount = 0;
    int semiTransparentCount = 0;

    // 统计透明像素
    image.ProcessPixelRows(accessor =>
    {
        for (int y = 0; y < accessor.Height; y++)
        {
            var row = accessor.GetRowSpan(y);
            for (int x = 0; x < row.Length; x++)
            {
                if (row[x].A == 0) transparentCount++;
                else if (row[x].A < 255) semiTransparentCount++;
            }
        }
    });

    // 角点采样
    var cornerPoints = new (int X, int Y)[]
    {
        (0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)
    };

    // 近角点采样（向内偏移 min(20, 10% 边长)
    int offset = Math.Min(20, Math.Min(w, h) / 10);
    var nearCornerPoints = new (int X, int Y)[]
    {
        (offset, offset), (w - 1 - offset, offset),
        (offset, h - 1 - offset), (w - 1 - offset, h - 1 - offset)
    };

    // 中心采样（用于判断主体是否不透明）
    var centerPoints = new (int X, int Y)[]
    {
        (w / 2, h / 2),
        (w / 4, h / 4), (w * 3 / 4, h / 4),
        (w / 4, h * 3 / 4), (w * 3 / 4, h * 3 / 4)
    };

    static (int, int, byte, byte, byte, byte) SamplePixel(Image<Rgba32> img, int x, int y)
    {
        var px = img[x, y];
        return (x, y, px.A, px.R, px.G, px.B);
    }

    var cornerSamples = new (int X, int Y, byte A, byte R, byte G, byte B)[8];
    for (int i = 0; i < 4; i++)
        cornerSamples[i] = SamplePixel(image, cornerPoints[i].X, cornerPoints[i].Y);
    for (int i = 0; i < 4; i++)
        cornerSamples[4 + i] = SamplePixel(image, nearCornerPoints[i].X, nearCornerPoints[i].Y);

    var centerSamples = new (int X, int Y, byte A, byte R, byte G, byte B)[centerPoints.Length];
    for (int i = 0; i < centerPoints.Length; i++)
        centerSamples[i] = SamplePixel(image, centerPoints[i].X, centerPoints[i].Y);

    bool cornersTransparent = cornerSamples[0].A == 0 && cornerSamples[1].A == 0
                            && cornerSamples[2].A == 0 && cornerSamples[3].A == 0;
    bool nearCornersTransparent = cornerSamples[4].A == 0 && cornerSamples[5].A == 0
                                && cornerSamples[6].A == 0 && cornerSamples[7].A == 0;

    return new TransparencyReport(
        totalPixels, transparentCount, semiTransparentCount,
        cornersTransparent, nearCornersTransparent,
        cornerSamples, centerSamples
    );
}

static void PrintTransparencyReport(TransparencyReport r, int width, int height, string indent = "")
{
    double transRatio = Math.Round((double)r.TransparentPixels / r.TotalPixels * 100, 2);
    double semiRatio = Math.Round((double)r.SemiTransparentPixels / r.TotalPixels * 100, 2);
    bool hasAnyTransparency = r.TransparentPixels + r.SemiTransparentPixels > 0;

    Console.WriteLine($"{indent}尺寸: {width}x{height}");
    Console.WriteLine($"{indent}总像素: {r.TotalPixels:N0}");
    Console.WriteLine($"{indent}完全透明像素 (A=0): {r.TransparentPixels:N0} ({transRatio}%)");
    Console.WriteLine($"{indent}半透明像素 (0<A<255): {r.SemiTransparentPixels:N0} ({semiRatio}%)");
    Console.WriteLine($"{indent}包含透明通道: {(hasAnyTransparency ? "是" : "否")}");
    Console.WriteLine();

    // 角点信息
    Console.WriteLine($"{indent}■ 角点检测:");
    string[] cornerLabels = ["左上", "右上", "左下", "右下", "左上(内)", "右上(内)", "左下(内)", "右下(内)"];
    for (int i = 0; i < r.CornerSamples.Length; i++)
    {
        var s = r.CornerSamples[i];
        var status = s.A == 0 ? "透明" : s.A == 255 ? "不透明" : $"半透明(A={s.A})";
        Console.WriteLine($"{indent}  {cornerLabels[i],-8} ({s.X,4},{s.Y,4}) A={s.A,3} RGB=({s.R},{s.G},{s.B}) → {status}");
    }
    Console.WriteLine();

    // 中心采样
    Console.WriteLine($"{indent}■ 主体区域采样:");
    foreach (var s in r.CenterSamples)
    {
        var status = s.A == 255 ? "不透明" : s.A == 0 ? "透明" : $"半透明(A={s.A})";
        Console.WriteLine($"{indent}  ({s.X,4},{s.Y,4}) A={s.A,3} RGB=({s.R},{s.G},{s.B}) → {status}");
    }
    Console.WriteLine();

    // 综合判定
    Console.WriteLine($"{indent}■ 判定结果:");
    if (!hasAnyTransparency)
    {
        Console.WriteLine($"{indent}  ✗ 不透明图片 — 没有任何透明像素。");
    }
    else if (r.CornersTransparent && r.NearCornersTransparent)
    {
        bool centerOpaque = true;
        foreach (var s in r.CenterSamples)
            if (s.A < 255) { centerOpaque = false; break; }

        if (centerOpaque)
            Console.WriteLine($"{indent}  ✓ 圆角透明图标 — 四角透明且主体不透明。");
        else
            Console.WriteLine($"{indent}  △ 含透明通道 — 四角透明，但主体区域存在透明/半透明像素。");
    }
    else
    {
        Console.WriteLine($"{indent}  △ 含透明通道 — 但四角不全是透明 (可能不是圆角图标)。");
    }
}

static bool TryParseColor(string value, out Rgba32 color)
{
    color = default;

    switch (value.ToLowerInvariant())
    {
        case "white":
            color = new Rgba32(255, 255, 255, 255);
            return true;
        case "black":
            color = new Rgba32(0, 0, 0, 255);
            return true;
    }

    // #RRGGBB or #RRGGBBAA
    if (value.StartsWith('#') && (value.Length == 7 || value.Length == 9))
    {
        try
        {
            var hex = value[1..];
            byte r = byte.Parse(hex[0..2], NumberStyles.HexNumber);
            byte g = byte.Parse(hex[2..4], NumberStyles.HexNumber);
            byte b = byte.Parse(hex[4..6], NumberStyles.HexNumber);
            byte a = hex.Length == 8 ? byte.Parse(hex[6..8], NumberStyles.HexNumber) : (byte)255;
            color = new Rgba32(r, g, b, a);
            return true;
        }
        catch { return false; }
    }

    return false;
}

static bool IsColorMatch(Rgba32 pixel, Rgba32 target, int threshold)
{
    return Math.Abs(pixel.R - target.R) <= threshold
        && Math.Abs(pixel.G - target.G) <= threshold
        && Math.Abs(pixel.B - target.B) <= threshold;
}

static double ColorDistance(Rgba32 a, Rgba32 b)
{
    int dr = a.R - b.R;
    int dg = a.G - b.G;
    int db = a.B - b.B;
    return Math.Sqrt(dr * dr + dg * dg + db * db);
}

/// <summary>
/// 使用四向边缘扫描线 + 交集策略移除背景。
/// 水平方向（从左或从右）和垂直方向（从上或从下）各自独立扫描，
/// 只有同时被水平和垂直方向标记的像素才算背景。
/// 这确保只有真正在角落/边缘浅层的背景被移除，图标内部的深色内容不受影响。
/// </summary>
static int FloodFillTransparent(Image<Rgba32> image, Rgba32 bgColor, int threshold)
{
    int w = image.Width, h = image.Height;
    var verticalBg = new bool[w, h];   // 从上或从下可达
    var horizontalBg = new bool[w, h]; // 从左或从右可达

    // 从上往下扫（每列）
    for (int x = 0; x < w; x++)
    {
        for (int y = 0; y < h; y++)
        {
            if (IsColorMatch(image[x, y], bgColor, threshold))
                verticalBg[x, y] = true;
            else
                break;
        }
    }

    // 从下往上扫（每列）
    for (int x = 0; x < w; x++)
    {
        for (int y = h - 1; y >= 0; y--)
        {
            if (IsColorMatch(image[x, y], bgColor, threshold))
                verticalBg[x, y] = true;
            else
                break;
        }
    }

    // 从左往右扫（每行）
    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            if (IsColorMatch(image[x, y], bgColor, threshold))
                horizontalBg[x, y] = true;
            else
                break;
        }
    }

    // 从右往左扫（每行）
    for (int y = 0; y < h; y++)
    {
        for (int x = w - 1; x >= 0; x--)
        {
            if (IsColorMatch(image[x, y], bgColor, threshold))
                horizontalBg[x, y] = true;
            else
                break;
        }
    }

    // 仅水平和垂直方向交集处才是真正的背景
    var isBackground = new bool[w, h];
    int removedCount = 0;
    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            if (verticalBg[x, y] && horizontalBg[x, y])
            {
                isBackground[x, y] = true;
                image[x, y] = new Rgba32(0, 0, 0, 0);
                removedCount++;
            }
        }
    }

    // 对背景-内容交界处做半透明渐变（抗锯齿）
    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            if (isBackground[x, y]) continue;
            var pixel = image[x, y];
            if (pixel.A == 0) continue;

            bool adjacentToBackground = false;
            for (int dy = -1; dy <= 1 && !adjacentToBackground; dy++)
                for (int dx = -1; dx <= 1 && !adjacentToBackground; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && nx < w && ny >= 0 && ny < h && isBackground[nx, ny])
                        adjacentToBackground = true;
                }

            if (adjacentToBackground)
            {
                var distance = ColorDistance(pixel, bgColor);
                if (distance < threshold * 3)
                {
                    var alpha = (byte)Math.Clamp((int)(255.0 * distance / (threshold * 3)), 0, 255);
                    image[x, y] = new Rgba32(pixel.R, pixel.G, pixel.B, alpha);
                    if (alpha == 0) removedCount++;
                }
            }
        }
    }

    return removedCount;
}

static string FormatFileSize(long bytes)
{
    if (bytes < 1024) return $"{bytes} B";
    if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
    return $"{bytes / (1024.0 * 1024):F1} MB";
}

static int Error(string message)
{
    Console.Error.WriteLine($"错误: {message}");
    return 1;
}

// ═══════════════════════════════════════════════════════════════════
// crop 命令：居中裁剪缩放
// ═══════════════════════════════════════════════════════════════════

static int RunCrop(string[] args)
{
    if (args.Length < 2)
        return Error("用法: IconTool crop <文件路径> [选项]");

    var filePath = args[1];
    if (!File.Exists(filePath))
        return Error($"文件不存在: {filePath}");

    // 解析选项
    string? outputPath = null;
    int targetSize = 512;

    for (int i = 2; i < args.Length; i++)
    {
        var arg = args[i].ToLowerInvariant();
        switch (arg)
        {
            case "-o" or "--output":
                if (i + 1 >= args.Length) return Error("-o/--output 需要一个参数。");
                outputPath = args[++i];
                break;
            case "-s" or "--size":
                if (i + 1 >= args.Length) return Error("-s/--size 需要一个参数。");
                if (!int.TryParse(args[++i], out targetSize) || targetSize < 1 || targetSize > 4096)
                    return Error("目标尺寸必须为 1-4096 之间的整数。");
                break;
            default:
                return Error($"未知选项: {args[i]}");
        }
    }

    // 确定输出路径
    if (string.IsNullOrEmpty(outputPath))
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".";
        var name = Path.GetFileNameWithoutExtension(filePath);
        outputPath = Path.Combine(dir, $"{name}_cropped.png");
    }

    Console.WriteLine($"── 居中裁剪缩放 ──");
    Console.WriteLine($"  输入: {filePath}");
    Console.WriteLine($"  目标尺寸: {targetSize}x{targetSize}");
    Console.WriteLine($"  输出: {outputPath}");
    Console.WriteLine();

    try
    {
        using var image = Image.Load<Rgba32>(filePath);
        Console.WriteLine($"  原始尺寸: {image.Width}x{image.Height}");

        using var cropped = CenterCropAndResize(image, targetSize);
        Console.WriteLine($"  裁剪后尺寸: {cropped.Width}x{cropped.Height}");

        // 确保输出目录存在
        var outDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(outDir))
            Directory.CreateDirectory(outDir);

        var rgbaEncoder = new PngEncoder { ColorType = PngColorType.RgbWithAlpha };
        cropped.SaveAsPng(outputPath, rgbaEncoder);
        var fileSize = new FileInfo(outputPath).Length;
        Console.WriteLine($"  输出文件大小: {FormatFileSize(fileSize)}");
        Console.WriteLine();
        Console.WriteLine("✓ 裁剪完成。");

        return 0;
    }
    catch (Exception ex)
    {
        return Error($"处理失败: {ex.Message}");
    }
}

/// <summary>
/// 居中裁剪并缩放到目标正方形尺寸。
/// 对于非正方形图像，先取短边为正方形边长居中裁剪，再缩放到 targetSize。
/// 对于已是正方形的图像，直接缩放。
/// </summary>
static Image<Rgba32> CenterCropAndResize(Image<Rgba32> source, int targetSize)
{
    int w = source.Width, h = source.Height;
    int side = Math.Min(w, h);

    // 居中裁剪为正方形
    int cropX = (w - side) / 2;
    int cropY = (h - side) / 2;

    var result = source.Clone(ctx =>
    {
        if (w != h)
            ctx.Crop(new Rectangle(cropX, cropY, side, side));
        ctx.Resize(targetSize, targetSize, KnownResamplers.Lanczos3);
    });

    return result;
}

// ═══════════════════════════════════════════════════════════════════
// 自动处理模式（无参数）
// ═══════════════════════════════════════════════════════════════════

static int RunAutoProcess()
{
    var currentDir = Directory.GetCurrentDirectory();
    Console.WriteLine($"══ IconTool 自动处理模式 ══");
    Console.WriteLine($"工作目录: {currentDir}");
    Console.WriteLine();

    // 收集 png/jpg/jpeg 文件
    var extensions = new[] { "*.png", "*.jpg", "*.jpeg" };
    var files = new List<string>();
    foreach (var ext in extensions)
        files.AddRange(Directory.GetFiles(currentDir, ext, SearchOption.TopDirectoryOnly));

    if (files.Count == 0)
    {
        Console.WriteLine("当前目录没有找到 PNG/JPG/JPEG 文件。");
        return 0;
    }

    Console.WriteLine($"找到 {files.Count} 个图片文件。");
    Console.WriteLine();

    // 创建按时间命名的备份目录
    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var backupDir = Path.Combine(currentDir, $"backup_{timestamp}");
    Directory.CreateDirectory(backupDir);

    // 日志
    var logPath = Path.Combine(backupDir, "处理日志.txt");
    var logLines = new List<string>
    {
        $"IconTool 自动处理日志",
        $"处理时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
        $"工作目录: {currentDir}",
        $"备份目录: {backupDir}",
        $"待处理文件数: {files.Count}",
        new string('─', 60)
    };

    int successCount = 0, skipCount = 0, failCount = 0;

    foreach (var filePath in files)
    {
        var fileName = Path.GetFileName(filePath);
        var nameNoExt = Path.GetFileNameWithoutExtension(filePath);
        Console.WriteLine($"── 处理: {fileName} ──");
        logLines.Add("");
        logLines.Add($"[文件] {fileName}");

        try
        {
            using var image = Image.Load<Rgba32>(filePath);
            logLines.Add($"  尺寸: {image.Width}x{image.Height}");

            // 1. 检查是否已透明
            var report = AnalyzeTransparency(image);
            bool alreadyTransparent = report.TransparentPixels + report.SemiTransparentPixels > 0;

            if (alreadyTransparent && report.CornersTransparent)
            {
                Console.WriteLine($"  已是透明图片（透明像素 {report.TransparentPixels}），跳过透明化。");
                logLines.Add($"  结果: 跳过透明化 — 已是透明图片（透明像素={report.TransparentPixels}, 占比={Math.Round((double)report.TransparentPixels / report.TotalPixels * 100, 2)}%）");

                // 备份源文件
                var backupPathSkip = Path.Combine(backupDir, fileName);
                File.Copy(filePath, backupPathSkip, overwrite: true);
                logLines.Add($"  备份源文件: {fileName}");

                // 居中裁剪缩放到 512x512
                using var croppedSkip = CenterCropAndResize(image, 512);
                Console.WriteLine($"  裁剪缩放: {image.Width}x{image.Height} → {croppedSkip.Width}x{croppedSkip.Height}");
                logLines.Add($"  裁剪缩放: {image.Width}x{image.Height} → {croppedSkip.Width}x{croppedSkip.Height}");

                // 保存裁剪后的 PNG
                var extSkip = Path.GetExtension(filePath).ToLowerInvariant();
                string outputPngPathSkip;
                if (extSkip is ".jpg" or ".jpeg")
                {
                    outputPngPathSkip = Path.Combine(currentDir, nameNoExt + ".png");
                    File.Delete(filePath);
                    logLines.Add($"  源文件为 JPG，转换为 PNG: {nameNoExt}.png");
                }
                else
                {
                    outputPngPathSkip = filePath;
                }

                var rgbaEncoderSkip = new PngEncoder { ColorType = PngColorType.RgbWithAlpha };
                croppedSkip.SaveAsPng(outputPngPathSkip, rgbaEncoderSkip);
                var pngFileSizeSkip = new FileInfo(outputPngPathSkip).Length;
                Console.WriteLine($"  输出: {Path.GetFileName(outputPngPathSkip)} ({FormatFileSize(pngFileSizeSkip)})");
                logLines.Add($"  输出文件: {Path.GetFileName(outputPngPathSkip)} ({FormatFileSize(pngFileSizeSkip)})");

                // 生成 ICO
                GenerateIcoForFile(outputPngPathSkip, croppedSkip, backupDir, logLines, currentDir);

                logLines.Add($"  结果: 成功（跳过透明化，执行裁剪）");
                successCount++;
                Console.WriteLine();
                continue;
            }

            // 2. 采样四周像素判断背景色
            var borderColor = DetectBorderColor(image);
            if (borderColor is null)
            {
                Console.WriteLine($"  四周颜色不一致，不适合自动透明化，跳过。");
                logLines.Add($"  结果: 跳过 — 四周像素颜色不一致，无法确定单一背景色");
                skipCount++;
                Console.WriteLine();
                continue;
            }

            var (bgColor, bgName) = borderColor.Value;
            Console.WriteLine($"  检测到背景色: {bgName} (R={bgColor.R}, G={bgColor.G}, B={bgColor.B})");
            logLines.Add($"  检测到背景色: {bgName} (R={bgColor.R}, G={bgColor.G}, B={bgColor.B})");

            // 3. 备份源文件
            var backupPath = Path.Combine(backupDir, fileName);
            File.Copy(filePath, backupPath, overwrite: true);
            logLines.Add($"  备份源文件: {fileName} → {Path.GetFileName(backupPath)}");

            // 4. 透明化处理 — 使用 Flood Fill（从四角开始连通填充）
            // 只移除从图像四角可达的、颜色匹配的连通背景区域，
            // 不会误伤图标内部的深色像素
            // Flood Fill 容差需要比全局模式小得多（3），避免沿暗色边缘泄漏进图标内部
            int threshold = 3;
            int removedCount = FloodFillTransparent(image, bgColor, threshold);
            int totalPixels = image.Width * image.Height;

            double ratio = Math.Round((double)removedCount / totalPixels * 100, 2);
            Console.WriteLine($"  透明化: {removedCount}/{totalPixels} 像素 ({ratio}%)");
            logLines.Add($"  透明化像素: {removedCount}/{totalPixels} ({ratio}%)");

            // 5. 居中裁剪缩放到 512x512
            using var cropped = CenterCropAndResize(image, 512);
            Console.WriteLine($"  裁剪缩放: {image.Width}x{image.Height} → {cropped.Width}x{cropped.Height}");
            logLines.Add($"  裁剪缩放: {image.Width}x{image.Height} → {cropped.Width}x{cropped.Height}");

            // 6. 覆盖写入为 PNG（如果源是 jpg 则写为同名 .png）
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            string outputPngPath;
            if (ext is ".jpg" or ".jpeg")
            {
                outputPngPath = Path.Combine(currentDir, nameNoExt + ".png");
                // 删除原 jpg 文件（已备份）
                File.Delete(filePath);
                logLines.Add($"  源文件为 JPG，转换为 PNG: {nameNoExt}.png（原 JPG 已备份并删除）");
            }
            else
            {
                outputPngPath = filePath;
            }

            var rgbaEncoder = new PngEncoder { ColorType = PngColorType.RgbWithAlpha };
            cropped.SaveAsPng(outputPngPath, rgbaEncoder);
            var pngFileSize = new FileInfo(outputPngPath).Length;
            Console.WriteLine($"  输出: {Path.GetFileName(outputPngPath)} ({FormatFileSize(pngFileSize)})");
            logLines.Add($"  输出文件: {Path.GetFileName(outputPngPath)} ({FormatFileSize(pngFileSize)})");

            // 7. 生成 ICO
            GenerateIcoForFile(outputPngPath, cropped, backupDir, logLines, currentDir);

            logLines.Add($"  结果: 成功");
            successCount++;
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  处理失败: {ex.Message}");
            logLines.Add($"  结果: 失败 — {ex.Message}");
            failCount++;
            Console.WriteLine();
        }
    }

    // 写入汇总
    logLines.Add("");
    logLines.Add(new string('═', 60));
    logLines.Add($"汇总: 共 {files.Count} 个文件, 成功 {successCount}, 跳过 {skipCount}, 失败 {failCount}");

    File.WriteAllLines(logPath, logLines, Encoding.UTF8);

    Console.WriteLine(new string('═', 60));
    Console.WriteLine($"处理完成: 成功={successCount}, 跳过={skipCount}, 失败={failCount}");
    Console.WriteLine($"备份目录: {backupDir}");
    Console.WriteLine($"处理日志: {logPath}");

    return failCount > 0 ? 1 : 0;
}

/// <summary>
/// 采样图像四个角区域的像素，判断是否为单一背景色（白/黑）。
/// 改为角区域采样而非整条边，适配圆角矩形图标（图标内容延伸到边缘中段）。
/// 返回 null 表示四角颜色不一致、不适合自动处理。
/// </summary>
static (Rgba32 Color, string Name)? DetectBorderColor(Image<Rgba32> image)
{
    int w = image.Width, h = image.Height;

    // 角区域大小：图像短边的 5%，对于紧贴边缘的圆角矩形图标足够覆盖角落背景
    // 同时不至于深入到圆角弧线内侧的图标内容
    int regionSize = Math.Max(4, Math.Min(w, h) * 5 / 100);
    int step = Math.Max(1, regionSize / 10); // 每个角区域约 10x10=100 个采样点

    var cornerPixels = new List<Rgba32>();

    // 采样四个角区域（矩形块而非单条边线）
    // 左上角
    for (int y = 0; y < regionSize; y += step)
        for (int x = 0; x < regionSize; x += step)
            cornerPixels.Add(image[x, y]);

    // 右上角
    for (int y = 0; y < regionSize; y += step)
        for (int x = w - regionSize; x < w; x += step)
            cornerPixels.Add(image[x, y]);

    // 左下角
    for (int y = h - regionSize; y < h; y += step)
        for (int x = 0; x < regionSize; x += step)
            cornerPixels.Add(image[x, y]);

    // 右下角
    for (int y = h - regionSize; y < h; y += step)
        for (int x = w - regionSize; x < w; x += step)
            cornerPixels.Add(image[x, y]);

    if (cornerPixels.Count == 0) return null;

    // 统计：看是否绝大多数接近白色或黑色
    int whiteCount = 0, blackCount = 0;
    int otherCount = 0;
    int tolerance = 30;

    foreach (var px in cornerPixels)
    {
        if (px.A < 128) continue; // 已透明的忽略
        if (px.R >= (255 - tolerance) && px.G >= (255 - tolerance) && px.B >= (255 - tolerance))
            whiteCount++;
        else if (px.R <= tolerance && px.G <= tolerance && px.B <= tolerance)
            blackCount++;
        else
            otherCount++;
    }

    double totalOpaque = cornerPixels.Count;
    double whiteRatio = whiteCount / totalOpaque;
    double blackRatio = blackCount / totalOpaque;

    // >=80% 的角区域像素为同一颜色才认为可处理
    if (whiteRatio >= 0.80)
        return (new Rgba32(255, 255, 255, 255), "white");
    if (blackRatio >= 0.80)
        return (new Rgba32(0, 0, 0, 255), "black");

    return null;
}

/// <summary>
/// 用透明化后的图像生成同名 ICO（16/32/48/256），备份已有 ICO。
/// </summary>
static void GenerateIcoForFile(string pngPath, Image<Rgba32> sourceImage,
    string backupDir, List<string> logLines, string workDir)
{
    var nameNoExt = Path.GetFileNameWithoutExtension(pngPath);
    var icoPath = Path.Combine(workDir, nameNoExt + ".ico");

    // 如果已有 ICO，先备份
    if (File.Exists(icoPath))
    {
        var backupIcoPath = Path.Combine(backupDir, nameNoExt + ".ico");
        File.Copy(icoPath, backupIcoPath, overwrite: true);
        Console.WriteLine($"  备份已有 ICO: {nameNoExt}.ico");
        logLines.Add($"  备份已有 ICO: {nameNoExt}.ico → {Path.GetFileName(backupIcoPath)}");
    }

    try
    {
        // 补齐为正方形
        using var square = PadToSquare(sourceImage);

        var sizes = new[] { 16, 32, 48, 256 };
        var icoImages = new List<(int Size, byte[] PngBytes)>(sizes.Length);

        foreach (var size in sizes)
        {
            using var resized = square.Clone(ctx => ctx.Resize(size, size, KnownResamplers.NearestNeighbor));
            icoImages.Add((size, EncodePng(resized)));
        }

        // 写 ICO
        var tempIco = icoPath + ".tmp";
        using (var fs = new FileStream(tempIco, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var bw = new BinaryWriter(fs))
        {
            // ICONDIR
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)icoImages.Count);

            var offset = 6 + 16 * icoImages.Count;
            foreach (var (size, png) in icoImages)
            {
                bw.Write((byte)(size >= 256 ? 0 : size));
                bw.Write((byte)(size >= 256 ? 0 : size));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)png.Length);
                bw.Write((uint)offset);
                offset += png.Length;
            }

            foreach (var (_, png) in icoImages)
            {
                bw.Write(png);
            }

            bw.Flush();
            fs.Flush(true);
        }

        File.Move(tempIco, icoPath, overwrite: true);
        var icoSize = new FileInfo(icoPath).Length;
        Console.WriteLine($"  生成 ICO: {nameNoExt}.ico ({FormatFileSize(icoSize)}, 尺寸: {string.Join("/", sizes)})");
        logLines.Add($"  生成 ICO: {nameNoExt}.ico ({FormatFileSize(icoSize)}, 尺寸: {string.Join("/", sizes)})");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  生成 ICO 失败: {ex.Message}");
        logLines.Add($"  生成 ICO: 失败 — {ex.Message}");
    }
}

static Image<Rgba32> PadToSquare(Image<Rgba32> source)
{
    var size = Math.Max(source.Width, source.Height);
    if (source.Width == size && source.Height == size)
        return source.Clone();

    var dest = new Image<Rgba32>(size, size);
    var offsetX = (size - source.Width) / 2;
    var offsetY = (size - source.Height) / 2;
    dest.Mutate(ctx => ctx.DrawImage(source, new Point(offsetX, offsetY), 1f));
    return dest;
}

static byte[] EncodePng(Image<Rgba32> image)
{
    using var ms = new MemoryStream();
    var rgbaEncoder = new PngEncoder { ColorType = PngColorType.RgbWithAlpha };
    image.SaveAsPng(ms, rgbaEncoder);
    return ms.ToArray();
}

// ═══════════════════════════════════════════════════════════════════
// 类型声明（必须在顶级语句之后）
// ═══════════════════════════════════════════════════════════════════

record TransparencyReport(
    int TotalPixels,
    int TransparentPixels,
    int SemiTransparentPixels,
    bool CornersTransparent,
    bool NearCornersTransparent,
    (int X, int Y, byte A, byte R, byte G, byte B)[] CornerSamples,
    (int X, int Y, byte A, byte R, byte G, byte B)[] CenterSamples
);

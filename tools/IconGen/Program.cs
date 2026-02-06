using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: IconGen <input.png> <output.ico>");
    return 2;
}

try
{
    if (OperatingSystem.IsWindows())
    {
        // MSBuild's Exec task commonly decodes child process stdout using the current Windows ANSI
        // code page (e.g., CP936 on zh-CN). If we emit UTF-8 here, Chinese paths can appear garbled.
        // .NET uses UTF-8 for Encoding.Default, so we explicitly select the ANSI code page.
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
catch
{
    // Best-effort; console may not support changing encodings.
}

var inputPng = args[0];
var outputIco = args[1];

try
{
    var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputIco));
    if (!string.IsNullOrWhiteSpace(outputDir))
    {
        Directory.CreateDirectory(outputDir);
    }
}
catch
{
    // Best-effort; if this fails we'll error on write anyway.
}

if (!File.Exists(inputPng))
{
    Console.Error.WriteLine($"Input PNG not found: {inputPng}");
    return 3;
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputIco)) ?? ".");

var pngBytes = File.ReadAllBytes(inputPng);
if (pngBytes.Length < 24)
{
    Console.Error.WriteLine("Input is too small to be a valid PNG.");
    return 4;
}

static uint ReadBeUInt32(byte[] bytes, int offset)
{
    return (uint)(bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3]);
}

var width = ReadBeUInt32(pngBytes, 16);
var height = ReadBeUInt32(pngBytes, 20);

static string Sha256Hex(byte[] bytes)
{
    var hash = SHA256.HashData(bytes);
    return Convert.ToHexString(hash);
}

Console.WriteLine($"[IconGen] Input: {inputPng} ({width}x{height}), sha256={Sha256Hex(pngBytes)}");

// Build a square canvas to preserve aspect ratio and keep transparent padding.
static Image<Rgba32> PadToSquare(Image<Rgba32> source)
{
    var size = Math.Max(source.Width, source.Height);
    if (source.Width == size && source.Height == size)
    {
        return source.Clone();
    }

    var dest = new Image<Rgba32>(size, size);

    var offsetX = (size - source.Width) / 2;
    var offsetY = (size - source.Height) / 2;
    dest.Mutate(ctx => ctx.DrawImage(source, new Point(offsetX, offsetY), 1f));
    return dest;
}

static byte[] EncodePng(Image<Rgba32> image)
{
    using var ms = new MemoryStream();
    image.SaveAsPng(ms);
    return ms.ToArray();
}

var sizes = new[] { 16, 32, 48, 256 };
var icoImages = new List<(int Size, byte[] PngBytes)>(sizes.Length);

using (var src = Image.Load<Rgba32>(inputPng))
using (var square = PadToSquare(src))
{
    foreach (var size in sizes)
    {
        using var resized = square.Clone(ctx => ctx.Resize(size, size, KnownResamplers.NearestNeighbor));
        icoImages.Add((size, EncodePng(resized)));
    }
}

var tempOutput = outputIco + ".tmp";

// ICO: ICONDIR (6 bytes) + Nx ICONDIRENTRY (16 bytes each) + PNG data
using (var fs = new FileStream(tempOutput, FileMode.Create, FileAccess.Write, FileShare.None))
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

try
{
    File.Move(tempOutput, outputIco, overwrite: true);
}
catch (IOException ioEx)
{
    Console.Error.WriteLine($"[IconGen] Failed to replace output ICO (file may be locked): {outputIco}");
    Console.Error.WriteLine(ioEx.Message);
    try { File.Delete(tempOutput); } catch { }
    return 10;
}

Console.WriteLine($"[IconGen] Output: {outputIco} bytes={new FileInfo(outputIco).Length}");

return 0;

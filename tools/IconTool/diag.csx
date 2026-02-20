using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using var img = Image.Load<Rgba32>(args[0]);
// Sample along row 512 (middle) from left edge - find first non-black pixel
int firstNonBlackX = -1;
for (int x = 0; x < img.Width; x++) {
    var p = img[x, 512];
    if (p.R > 3 || p.G > 3 || p.B > 3) { firstNonBlackX = x; break; }
}
// Sample along column 512 from top - find first non-black pixel
int firstNonBlackY = -1;
for (int y = 0; y < img.Height; y++) {
    var p = img[512, y];
    if (p.R > 3 || p.G > 3 || p.B > 3) { firstNonBlackY = y; break; }
}
// Find icon boundary from right and bottom too
int lastNonBlackX = -1;
for (int x = img.Width - 1; x >= 0; x--) {
    var p = img[x, 512];
    if (p.R > 3 || p.G > 3 || p.B > 3) { lastNonBlackX = x; break; }
}
int lastNonBlackY = -1;
for (int y = img.Height - 1; y >= 0; y--) {
    var p = img[512, y];
    if (p.R > 3 || p.G > 3 || p.B > 3) { lastNonBlackY = y; break; }
}
// Count total pixels with R<=3 && G<=3 && B<=3 (near-black)
int blackCount = 0;
for (int y = 0; y < img.Height; y++)
    for (int x = 0; x < img.Width; x++) {
        var p = img[x, y];
        if (p.R <= 3 && p.G <= 3 && p.B <= 3) blackCount++;
    }
Console.WriteLine($"Image: {img.Width}x{img.Height}");
Console.WriteLine($"Row 512: first non-black at x={firstNonBlackX}, last at x={lastNonBlackX}");
Console.WriteLine($"Col 512: first non-black at y={firstNonBlackY}, last at y={lastNonBlackY}");
Console.WriteLine($"Icon rect (approx): ({firstNonBlackX},{firstNonBlackY}) to ({lastNonBlackX},{lastNonBlackY})");
Console.WriteLine($"Icon width: {lastNonBlackX - firstNonBlackX + 1}, height: {lastNonBlackY - firstNonBlackY + 1}");
Console.WriteLine($"Total near-black pixels (R,G,B<=3): {blackCount} / {img.Width*img.Height} = {100.0*blackCount/(img.Width*img.Height):F2}%");

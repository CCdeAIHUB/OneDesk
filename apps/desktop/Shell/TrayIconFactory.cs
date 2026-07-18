using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace OneDesk.Desktop.Shell;

internal static class TrayIconFactory
{
    public static WindowIcon CreateOneDeskIcon()
    {
        const int size = 32;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var offset = (y * size + x) * 4;
                var dx = x - 15.5;
                var dy = y - 15.5;
                var inside = dx * dx + dy * dy <= 15 * 15;
                var tile = (x is >= 8 and <= 13 || x is >= 18 and <= 23) &&
                    (y is >= 8 and <= 13 || y is >= 18 and <= 23);
                if (tile)
                {
                    SetPixel(pixels, offset, 255, 255, 255, 255);
                }
                else if (inside)
                {
                    SetPixel(pixels, offset, 14, 165, 233, 255);
                }
            }
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(size, size),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using (var frame = bitmap.Lock())
        {
            Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
        }
        return new WindowIcon(bitmap);
    }

    private static void SetPixel(byte[] pixels, int offset, byte red, byte green, byte blue, byte alpha)
    {
        pixels[offset] = blue;
        pixels[offset + 1] = green;
        pixels[offset + 2] = red;
        pixels[offset + 3] = alpha;
    }
}

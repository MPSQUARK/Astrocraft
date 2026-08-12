using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AstroCraft.Client.UI;

[SupportedOSPlatform("windows")]
public static class CriticScreenshot
{
    private const uint PwRenderFullContent = 0x00000002;

    public static bool TryCapture(string windowTitleSubstring, string outputPath)
    {
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                string title = process.MainWindowTitle;
                if (string.IsNullOrWhiteSpace(title)
                    || !title.Contains(windowTitleSubstring, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                IntPtr handle = process.MainWindowHandle;
                if (handle == IntPtr.Zero)
                {
                    continue;
                }

                return TryCaptureClientArea(handle, outputPath);
            }
            catch (InvalidOperationException)
            {
            }
            catch (ExternalException)
            {
            }
        }

        return false;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    public static bool TryCaptureClientArea(IntPtr handle, string outputPath)
    {
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        if (!GetClientRect(handle, out Rect clientRect))
        {
            return false;
        }

        int clientWidth = clientRect.Right - clientRect.Left;
        int clientHeight = clientRect.Bottom - clientRect.Top;
        if (clientWidth <= 0 || clientHeight <= 0)
        {
            return false;
        }

        if (!GetWindowRect(handle, out Rect windowRect))
        {
            return false;
        }

        int windowWidth = windowRect.Right - windowRect.Left;
        int windowHeight = windowRect.Bottom - windowRect.Top;
        if (windowWidth <= 0 || windowHeight <= 0)
        {
            return false;
        }

        using Bitmap windowBitmap = new(windowWidth, windowHeight);
        DwmFlush();
        Thread.Sleep(16);
        using (Graphics graphics = Graphics.FromImage(windowBitmap))
        {
            IntPtr deviceContext = graphics.GetHdc();
            PrintWindow(handle, deviceContext, PwRenderFullContent);
            graphics.ReleaseHdc(deviceContext);
        }

        int borderX = System.Math.Max(0, (windowWidth - clientWidth) / 2);
        int titleBarHeight = System.Math.Max(0, windowHeight - clientHeight - borderX);
        Rectangle crop = new(borderX, titleBarHeight, clientWidth, clientHeight);
        crop.Intersect(new Rectangle(0, 0, windowWidth, windowHeight));

        using Bitmap clientBitmap = windowBitmap.Clone(crop, windowBitmap.PixelFormat);
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        clientBitmap.Save(outputPath, ImageFormat.Png);
        return true;
    }

    public static bool TrySaveBgra32(ReadOnlySpan<byte> bgraPixels, int width, int height, string outputPath)
    {
        if (width <= 0 || height <= 0 || bgraPixels.Length < width * height * 4)
        {
            return false;
        }

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using Bitmap bitmap = new(width, height, PixelFormat.Format32bppArgb);
        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            int rowBytes = width * 4;
            unsafe
            {
                for (int y = 0; y < height; y++)
                {
                    bgraPixels.Slice(y * rowBytes, rowBytes).CopyTo(
                        new Span<byte>((void*)(data.Scan0 + y * stride), rowBytes));
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        bitmap.Save(outputPath, ImageFormat.Png);
        return true;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

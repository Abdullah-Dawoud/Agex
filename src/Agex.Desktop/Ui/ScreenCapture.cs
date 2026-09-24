using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Agex.Desktop.Ui;

/// <summary>
/// Screenshots of the main screen for the live Computer view. Frames stay in
/// memory: nothing is written to disk (except macOS's own capture tool, whose
/// temporary file is deleted at once) and nothing is sent anywhere.
/// Windows uses GDI; macOS uses the system screencapture tool (needs the
/// Screen Recording permission); Linux has no capture yet.
/// </summary>
public static class ScreenCapture
{
    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static string UnsupportedReason => OperatingSystem.IsLinux()
        ? "Live screenshots are not available on Linux yet."
        : "Live screenshots are not available on this system.";

    /// <summary>The main screen, or null when it cannot be captured.</summary>
    public static Bitmap? Capture(string tempFolder)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return CaptureWindows();
            if (OperatingSystem.IsMacOS()) return CaptureMac(tempFolder);
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception) { }
        return null;
    }

    /// <summary>Title of the window in front, where the system tells it.</summary>
    public static string? ForegroundWindowTitle()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var handle = GetForegroundWindow();
        if (handle == IntPtr.Zero) return null;
        var buffer = new char[512];
        var length = GetWindowText(handle, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : null;
    }

    private static Bitmap? CaptureMac(string tempFolder)
    {
        Directory.CreateDirectory(tempFolder);
        var file = Path.Combine(tempFolder, $"screen-{Guid.NewGuid():N}.png");
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/sbin/screencapture") { ArgumentList = { "-x", "-C", "-t", "png", file }, UseShellExecute = false });
            if (process is null || !process.WaitForExit(5000) || !File.Exists(file)) return null;
            using var stream = File.OpenRead(file);
            return new Bitmap(stream);
        }
        finally
        {
            try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static Bitmap? CaptureWindows()
    {
        var width = GetSystemMetrics(0);
        var height = GetSystemMetrics(1);
        if (width <= 0 || height <= 0) return null;
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, width, height);
        var old = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, width, height, screen, 0, 0, 0x00CC0020 | 0x40000000)) return null;
            var info = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = width, biHeight = -height, biPlanes = 1, biBitCount = 32 };
            var pixels = new byte[width * height * 4];
            SelectObject(memory, old);
            if (GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0) == 0) return null;
            for (var index = 3; index < pixels.Length; index += 4) pixels[index] = 255;
            var result = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var frame = result.Lock()) Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
            return result;
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, char[] text, int length);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, int rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}

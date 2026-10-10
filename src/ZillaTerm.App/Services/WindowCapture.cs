using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ZillaTerm.App.Services;

/// <summary>Fenêtre de Bureau à distance ouverte sur le poste (Connexion Bureau à distance, mstsc), pour la capturer.</summary>
public sealed record RemoteDesktopWindow(IntPtr Handle, string Title);

/// <summary>
/// Capture d'une fenêtre en PNG avec PrintWindow (contenu rendu par DirectX compris), même cachée par une autre fenêtre ;
/// pour le journal d'astreinte.
/// </summary>
public static class WindowCapture
{
    private const uint PwRenderFullContent = 0x2;

    // Classe de la fenêtre principale de Connexion Bureau à distance (mstsc), qui ouvre les sessions PSM.
    private const string MstscWindowClass = "TscShellContainerClass";

    /// <summary>Fenêtres visibles de Connexion Bureau à distance (sessions PSM, ouvertes avec mstsc).</summary>
    public static IReadOnlyList<RemoteDesktopWindow> RemoteDesktopWindows()
    {
        var windows = new List<RemoteDesktopWindow>();
        EnumWindows((handle, _) =>
        {
            if (IsWindowVisible(handle) && !IsIconic(handle) && ClassName(handle) == MstscWindowClass)
            {
                windows.Add(new RemoteDesktopWindow(handle, Title(handle)));
            }

            return true;
        }, IntPtr.Zero);
        return windows;
    }

    /// <summary>Image PNG de la fenêtre, ou null si elle n'existe plus ou ne peut pas être dessinée.</summary>
    public static byte[]? CapturePng(IntPtr handle)
    {
        if (!IsWindow(handle) || !GetWindowRect(handle, out var rect))
        {
            return null;
        }

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0 || width > 16384 || height > 16384)
        {
            return null;
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            IntPtr dc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(handle, dc, PwRenderFullContent))
                {
                    return null;
                }
            }
            finally
            {
                graphics.ReleaseHdc(dc);
            }
        }

        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        return png.ToArray();
    }

    /// <summary>
    /// Image PNG d'une fenêtre enfant (contrôle Bureau à distance dans un onglet) : sa partie de la fenêtre principale,
    /// telle qu'elle est composée à l'écran. Null si elle n'est pas visible.
    /// </summary>
    public static byte[]? CaptureChildPng(IntPtr child)
    {
        IntPtr root = GetAncestor(child, GaRoot);
        if (root == IntPtr.Zero || !IsWindowVisible(child) || !GetWindowRect(child, out var area) || !GetWindowRect(root, out var window))
        {
            return null;
        }

        var whole = CapturePng(root);
        if (whole is null)
        {
            return null;
        }

        using var source = new Bitmap(new MemoryStream(whole));
        var crop = Rectangle.Intersect(
            new Rectangle(area.Left - window.Left, area.Top - window.Top, area.Right - area.Left, area.Bottom - area.Top),
            new Rectangle(0, 0, source.Width, source.Height));
        if (crop.Width <= 0 || crop.Height <= 0)
        {
            return null;
        }

        using var part = source.Clone(crop, source.PixelFormat);
        using var png = new MemoryStream();
        part.Save(png, ImageFormat.Png);
        return png.ToArray();
    }

    private const uint GaRoot = 2;

    private static string ClassName(IntPtr handle)
    {
        var name = new StringBuilder(256);
        return GetClassName(handle, name, name.Capacity) > 0 ? name.ToString() : "";
    }

    private static string Title(IntPtr handle)
    {
        var title = new StringBuilder(512);
        return GetWindowText(handle, title, title.Capacity) > 0 ? title.ToString() : "";
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, StringBuilder name, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int capacity);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr handle, IntPtr dc, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr handle, uint flags);
}

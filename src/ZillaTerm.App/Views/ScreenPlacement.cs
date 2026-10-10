using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ZillaTerm.App.Views;

/// <summary>
/// Place une fenêtre détachée à l'endroit où l'onglet a été lâché, sur n'importe quel écran. La position est donnée en
/// pixels de l'écran visé, une fois la fenêtre créée : convertie avec l'échelle de la fenêtre principale, elle serait
/// fausse sur un écran d'une autre échelle (100 % d'un côté, 150 % de l'autre).
/// </summary>
internal static class ScreenPlacement
{
    /// <param name="devicePoint">Point lâché, en pixels de l'écran.</param>
    /// <param name="dx">Décalage horizontal, en pixels indépendants (mis à l'échelle de l'écran visé).</param>
    /// <param name="dy">Décalage vertical, en pixels indépendants.</param>
    public static void OpenAt(Window window, Point devicePoint, double dx, double dy)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.SourceInitialized += (_, _) =>
        {
            var monitor = MonitorFromPoint(new NativePoint((int)devicePoint.X, (int)devicePoint.Y), MonitorDefaultToNearest);
            double scale = GetDpiForMonitor(monitor, MonitorDpiEffective, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
            SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero,
                (int)Math.Round(devicePoint.X + dx * scale), (int)Math.Round(devicePoint.Y + dy * scale), 0, 0,
                NoSize | NoZOrder | NoActivate);
        };
    }

    private const uint MonitorDefaultToNearest = 2;
    private const int MonitorDpiEffective = 0;
    private const uint NoSize = 0x0001;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}

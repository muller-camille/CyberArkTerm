using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ZillaTerm.Core.Diagnostics;

namespace ZillaTerm.App.Services;

/// <summary>
/// Signaux d'une alerte du suivi de fichier : bouton de la barre des tâches qui clignote, et notification Windows.
/// La notification ne contient jamais la ligne du journal (elle peut s'afficher sur l'écran verrouillé et reste dans
/// le centre de notifications) : seulement le nombre de lignes et le nom du fichier.
/// </summary>
internal static class TailAlerts
{
    private const uint FlashAll = 0x3;
    private const uint FlashUntilForeground = 0xC;

    private static System.Windows.Forms.NotifyIcon? _icon;
    private static Window? _target;
    private static Action? _onClick;

    /// <summary>Fait clignoter le bouton de la fenêtre dans la barre des tâches jusqu'à ce qu'elle passe au premier plan.</summary>
    public static void Flash(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = handle,
            Flags = FlashAll | FlashUntilForeground,
        };
        FlashWindowEx(ref info);
    }

    /// <summary>Notification Windows ; un clic ramène la fenêtre et appelle <paramref name="onClick"/>.</summary>
    public static void Notify(Window window, string title, string text, Action? onClick)
    {
        try
        {
            _icon ??= CreateIcon();
            _target = window;
            _onClick = onClick;
            _icon.Visible = true;
            _icon.ShowBalloonTip(8000, title, text, System.Windows.Forms.ToolTipIcon.Warning);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ExternalException or IOException)
        {
            DebugLog.Write("tail", "notification impossible", ex);
        }
    }

    /// <summary>Fenêtre fermée : un clic sur une ancienne notification ne la vise plus.</summary>
    public static void Forget(Window window)
    {
        if (ReferenceEquals(_target, window))
        {
            _target = null;
            _onClick = null;
        }
    }

    /// <summary>Retire l'icône de la zone de notification (plus aucune fenêtre de suivi).</summary>
    public static void Hide()
    {
        _target = null;
        _onClick = null;
        if (_icon is { } icon)
        {
            _icon = null;
            icon.Visible = false;
            icon.Dispose();
        }
    }

    private static System.Windows.Forms.NotifyIcon CreateIcon()
    {
        var icon = new System.Windows.Forms.NotifyIcon { Text = "ZillaTerm — tail -f" };
        try
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/ZillaTerm.ico"))?.Stream;
            icon.Icon = stream is null
                ? System.Drawing.SystemIcons.Warning
                : new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            icon.Icon = System.Drawing.SystemIcons.Warning;
        }

        icon.BalloonTipClicked += (_, _) => ShowTarget();
        icon.MouseClick += (_, _) => ShowTarget();
        return icon;
    }

    private static void ShowTarget()
    {
        if (_target is not { IsLoaded: true } window)
        {
            return;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        _onClick?.Invoke();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);
}

using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CyberArkTerm.App.Services.Rdp;

/// <summary>
/// Emplacement de la session dans l'onglet : fenêtre enfant du thread de l'interface, que WPF place, affiche et
/// masque sans jamais attendre un autre thread. Le thread de chaque connexion (<see cref="RdpThread"/>) y crée la
/// fenêtre du contrôle Bureau à distance, l'adapte à sa taille, et l'en retire avant de libérer le contrôle.
/// </summary>
internal sealed class RdpSlot : HwndHost
{
    private const int WsChild = 0x40000000;
    private const int WsClipChildren = 0x02000000;
    private const int WsClipSiblings = 0x04000000;

    private SlotWindow? _window;

    /// <summary>La taille de l'emplacement a changé (thread de l'interface).</summary>
    public event Action? PixelSizeChanged;

    /// <summary>Fenêtre de l'emplacement (parent des fenêtres du contrôle), ou zéro tant qu'elle n'existe pas.</summary>
    public IntPtr SlotHandle => _window?.Handle ?? IntPtr.Zero;

    /// <summary>Taille de l'emplacement, en pixels de l'écran.</summary>
    public (int Width, int Height) PixelSize =>
        _window is { Handle: var handle } && handle != IntPtr.Zero && GetClientRect(handle, out var rect)
            ? (rect.Right - rect.Left, rect.Bottom - rect.Top)
            : (0, 0);

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _window = new SlotWindow(this, hwndParent.Handle);
        return new HandleRef(this, _window.Handle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _window?.DestroyHandle();
        _window = null;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hdc, ref Rect rect, IntPtr brush);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int index);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>Fenêtre noire du thread de l'interface ; signale ses changements de taille.</summary>
    private sealed class SlotWindow : System.Windows.Forms.NativeWindow
    {
        private const int WmSize = 0x0005;
        private const int WmEraseBackground = 0x0014;
        private const int BlackBrush = 4;

        private readonly RdpSlot _owner;

        public SlotWindow(RdpSlot owner, IntPtr parent)
        {
            _owner = owner;
            CreateHandle(new System.Windows.Forms.CreateParams
            {
                Parent = parent,
                Style = WsChild | WsClipChildren | WsClipSiblings,
            });
        }

        protected override void WndProc(ref System.Windows.Forms.Message m)
        {
            if (m.Msg == WmEraseBackground && GetClientRect(m.HWnd, out var rect))
            {
                FillRect(m.WParam, ref rect, GetStockObject(BlackBrush));
                m.Result = 1;
                return;
            }

            base.WndProc(ref m);
            if (m.Msg == WmSize)
            {
                _owner.PixelSizeChanged?.Invoke();
            }
        }
    }
}

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CyberArkTerm.App.Services.Rdp;
using CyberArkTerm.Core.Rdp;
using Xunit.Abstractions;

namespace CyberArkTerm.App.Tests;

/// <summary>
/// Expérience (non fusionnée) : les fenêtres d'une application distante peuvent-elles être affichées dans l'onglet ?
/// Mesures seulement : fenêtres créées par le contrôle, déplacement, rattachement à l'emplacement de l'onglet,
/// dérive de position, rendu (captures) et saisie clavier.
/// </summary>
public class RemoteAppHostingSpike(ITestOutputHelper output)
{
    private static readonly string Shots = Environment.GetEnvironmentVariable("SPIKE_SHOTS") ?? Path.GetTempPath();

    [Fact]
    [Trait("Category", "RdpSpike")]
    public async Task RemoteAppWindowsInTheTab()
    {
        var user = Environment.GetEnvironmentVariable("RDP_TEST_USER");
        var password = Environment.GetEnvironmentVariable("RDP_TEST_PASSWORD");
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
        {
            return;
        }

        Directory.CreateDirectory(Shots);
        var settings = RdpConnectionSettings.FromRdpFile(Encoding.Unicode.GetBytes(
            $"full address:s:127.0.0.2:3389\r\nusername:s:{Environment.MachineName}\\{user}\r\nauthentication level:i:0\r\nenablecredsspsupport:i:1\r\n" +
            "remoteapplicationmode:i:1\r\ndisableremoteappcapscheck:i:1\r\n" +
            "remoteapplicationprogram:s:C:\\Windows\\System32\\notepad.exe\r\nremoteapplicationname:s:PSM-RDP\r\n"));
        var request = new RdpConnectionRequest(settings, password);

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var session = new RdpSession("spike", _ => Task.FromResult(request));
            var window = new Window { Left = 40, Top = 40, Width = 1000, Height = 700, ShowInTaskbar = false, Content = session.Host, Title = "Spike host" };
            window.Show();
            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, async () =>
            {
                try
                {
                    await Scenario(session, window);
                    done.TrySetResult();
                }
                catch (Exception e)
                {
                    done.TrySetException(e);
                }
                finally
                {
                    session.Dispose();
                    try
                    {
                        await session.Closed.WaitAsync(TimeSpan.FromSeconds(30));
                    }
                    finally
                    {
                        window.Close();
                        window.Dispatcher.InvokeShutdown();
                    }
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromMinutes(4));
    }

    private async Task Scenario(RdpSession session, Window window)
    {
        await session.ConnectAsync();
        for (int i = 0; i < 180 && session.State == RdpSessionState.Connecting; i++)
        {
            await Task.Delay(500);
        }

        output.WriteLine($"État : {session.State}, {session.Error}");
        Assert.Equal(RdpSessionState.Connected, session.State);
        IntPtr main = IntPtr.Zero;
        for (int i = 0; i < 40 && main == IntPtr.Zero; i++)
        {
            await Task.Delay(500);
            main = Ours().Where(h => IsWindowVisible(h) && Title(h).Contains("Notepad", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(h => Area(h)).FirstOrDefault();
        }

        Assert.NotEqual(IntPtr.Zero, main);
        output.WriteLine($"Fenêtre principale : {Describe(main)}");
        output.WriteLine($"Événements : {string.Join(" | ", session.RemoteAppEvents)}");
        int connectionThread = await session.InvokeOnControlAsync(_ => GetCurrentThreadId());
        output.WriteLine($"Thread de la connexion : {connectionThread}");
        var host = new System.Windows.Interop.WindowInteropHelper(window).Handle;

        // Rattachement au conteneur du contrôle (thread de la connexion), lui-même dans l'emplacement de l'onglet.
        session.Host.Visibility = Visibility.Visible;
        session.Host.UpdateLayout();
        await Task.Delay(300);
        var slot = session.Host.SlotHandle;
        var container = GetWindow(slot, 5);
        output.WriteLine($"Conteneur : {Describe(container)}");
        await session.InvokeOnControlAsync(_ =>
        {
            long style = GetWindowLongPtr(main, GwlStyle).ToInt64();
            SetParent(main, container);
            SetWindowLongPtr(main, GwlStyle, new IntPtr((style & ~WsPopup) | WsChild));
            GetClientRect(container, out var c);
            SetWindowPos(main, new IntPtr(0), 0, 0, c.Width, c.Height, SwpFrameChanged | SwpShowWindow);
            return 0;
        });
        await Track(main, "B conteneur", 2000);
        ShotScreen(Screen(slot), "3-container");
        await RightClick(main, host, "E1 conteneur");
        await TypeAndCopy(main, host, "K conteneur", "bonjour", setFocus: false);
        await session.InvokeOnControlAsync(_ => SetFocus(main));
        await TypeAndCopy(main, host, "K conteneur SetFocus", "monde", setFocus: false);

        // Boutons du titre dessiné par le serveur : réduire, puis agrandir.
        GetWindowRect(main, out var r);
        await ClickAt(host, r.Right - 115, r.Top + 15, "M réduire");
        await Track(main, "M réduite", 2000);
        output.WriteLine($"M : réduite {IsIconic(main)}");
        await session.InvokeOnControlAsync(_ => ShowWindow(main, 9));
        await Track(main, "M restaurée", 2000);
        output.WriteLine($"M : réduite {IsIconic(main)}");
        GetWindowRect(main, out r);
        await ClickAt(host, r.Right - 69, r.Top + 15, "X agrandir");
        await Track(main, "X agrandie", 2000);
        await session.InvokeOnControlAsync(_ =>
        {
            GetClientRect(container, out var c);
            SetWindowPos(main, IntPtr.Zero, 0, 0, c.Width, c.Height, SwpNoZOrder | SwpNoActivate);
            return 0;
        });
        await Track(main, "X réajustée", 2000);
        await RightClick(main, host, "E2 après agrandissement");
        ShotScreen(Screen(slot), "4-after-maximize");

        output.WriteLine($"Événements : {string.Join(" | ", session.RemoteAppEvents)}");
        output.WriteLine("Fenêtres après :");
        foreach (var h in Ours().Where(IsWindowVisible))
        {
            output.WriteLine("  " + Describe(h));
        }

        session.Disconnect();
        await Task.Delay(3000);
        output.WriteLine($"Fin : {session.State}");
    }

    private async Task ClickAt(IntPtr host, int x, int y, string step)
    {
        SetForegroundWindow(host);
        await Task.Delay(300);
        SetCursorPos(x, y);
        await Task.Delay(200);
        output.WriteLine($"{step} : clic en ({x},{y}) sur {Describe(WindowFromPoint(new Point32 { X = x, Y = y }))}");
        Click(0x0002, 0x0004);
        await Task.Delay(1500);
    }

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr h);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr h, int cmd);

    /// <summary>
    /// Clic droit au centre de <paramref name="main"/> (vraie souris) : nouvelles fenêtres de l'application distante et
    /// écart entre le menu contextuel et le point cliqué ; puis Échap.
    /// </summary>
    private async Task RightClick(IntPtr main, IntPtr host, string step)
    {
        var before = Ours().ToHashSet();
        SetForegroundWindow(host);
        await Task.Delay(300);
        GetWindowRect(main, out var r);
        int x = r.Left + (r.Width / 2), y = r.Top + (r.Height / 2);
        SetCursorPos(x, y);
        await Task.Delay(200);
        output.WriteLine($"{step} : clic droit en ({x},{y}), fenêtre sous la souris {Describe(WindowFromPoint(new Point32 { X = x, Y = y }))}");
        Click(0x0008, 0x0010);
        await Task.Delay(2000);
        var added = Ours().Where(h => !before.Contains(h) && IsWindowVisible(h)).ToList();
        if (added.Count == 0)
        {
            output.WriteLine($"{step} : aucune nouvelle fenêtre");
        }

        foreach (var h in added)
        {
            GetWindowRect(h, out var p);
            output.WriteLine($"{step} : nouvelle fenêtre {Describe(h)}, écart au clic ({p.Left - x},{p.Top - y})");
            Shot(h, $"menu-{step.Split(' ')[0]}");
        }

        Key(0x1B);
        await Task.Delay(800);
    }

    private async Task TypeAndCopy(IntPtr main, IntPtr host, string step, string text, bool setFocus)
    {
        SetForegroundWindow(host);
        await Task.Delay(300);
        GetWindowRect(main, out var r);
        SetCursorPos(r.Left + (r.Width / 2), r.Top + (r.Height / 2));
        await Task.Delay(200);
        Click(0x0002, 0x0004);
        await Task.Delay(500);
        if (setFocus)
        {
            output.WriteLine($"{step} : SetFocus → précédent {SetFocus(main)}");
        }

        int railThread = GetWindowThreadProcessId(main, out _);
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        GetGUIThreadInfo(railThread, ref info);
        output.WriteLine($"{step} : premier plan {GetForegroundWindow()}, actif {info.Active}, focus {info.Focus} (fenêtre {main})");
        var inputs = new List<Input>();
        foreach (var c in text)
        {
            inputs.Add(new Input { Type = 1, Keyboard = new KeyboardInput { Scan = c, Flags = 4 } });
            inputs.Add(new Input { Type = 1, Keyboard = new KeyboardInput { Scan = c, Flags = 4 | 2 } });
        }

        foreach (ushort vk in new ushort[] { 'A', 'C' })
        {
            inputs.Add(new Input { Type = 1, Keyboard = new KeyboardInput { Vk = 0x11 } });
            inputs.Add(new Input { Type = 1, Keyboard = new KeyboardInput { Vk = vk } });
            inputs.Add(new Input { Type = 1, Keyboard = new KeyboardInput { Vk = vk, Flags = 2 } });
            inputs.Add(new Input { Type = 1, Keyboard = new KeyboardInput { Vk = 0x11, Flags = 2 } });
        }

        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
        await Task.Delay(2500);
        string clip;
        try
        {
            clip = Clipboard.GetText();
        }
        catch (Exception e)
        {
            clip = "(" + e.GetType().Name + ")";
        }

        output.WriteLine($"{step} : presse-papiers « {clip.Replace("\r", "\\r").Replace("\n", "\\n")} » (attendu : contient « {text} »)");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Active;
        public IntPtr Focus;
        public IntPtr Capture;
        public IntPtr MenuOwner;
        public IntPtr MoveSize;
        public IntPtr Caret;
        public Rect CaretRect;
    }

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(int thread, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr h);

    private static void Click(uint down, uint up)
    {
        var inputs = new[]
        {
            new Input { Type = 0, Mouse = new MouseInput { Flags = down } },
            new Input { Type = 0, Mouse = new MouseInput { Flags = up } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static void Key(ushort vk)
    {
        var inputs = new[]
        {
            new Input { Type = 1, Keyboard = new KeyboardInput { Vk = vk } },
            new Input { Type = 1, Keyboard = new KeyboardInput { Vk = vk, Flags = 2 } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint Data;
        public uint Flags;
        public uint Time;
        public IntPtr Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr Extra;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct Input
    {
        [FieldOffset(0)]
        public uint Type;

        [FieldOffset(8)]
        public MouseInput Mouse;

        [FieldOffset(8)]
        public KeyboardInput Keyboard;
    }

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr h);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point32 point);

    /// <summary>Position de la fenêtre toutes les 500 ms : stable, ou corrigée par le serveur ?</summary>
    private async Task Track(IntPtr h, string step, int ms)
    {
        for (int t = 0; t <= ms; t += 500)
        {
            GetWindowRect(h, out var r);
            var parent = GetParent(h);
            var rel = new Rect { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
            if (parent != IntPtr.Zero)
            {
                MapWindowPoints(IntPtr.Zero, parent, ref rel, 2);
            }

            output.WriteLine($"{step} t={t} écran {r} relatif {rel} visible {IsWindowVisible(h)} titre « {Title(h)} »");
            await Task.Delay(500);
        }
    }

    private void Shot(IntPtr h, string name)
    {
        GetWindowRect(h, out var r);
        ShotScreen(r, name);
    }

    private void ShotScreen(Rect r, string name)
    {
        if (r.Width <= 0 || r.Height <= 0)
        {
            output.WriteLine($"Capture {name} : vide");
            return;
        }

        using var bitmap = new System.Drawing.Bitmap(r.Width, r.Height);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(r.Left, r.Top, 0, 0, bitmap.Size);
        }

        int black = 0, white = 0, other = 0;
        for (int y = 0; y < bitmap.Height; y += 4)
        {
            for (int x = 0; x < bitmap.Width; x += 4)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.R < 16 && p.G < 16 && p.B < 16)
                {
                    black++;
                }
                else if (p.R > 240 && p.G > 240 && p.B > 240)
                {
                    white++;
                }
                else
                {
                    other++;
                }
            }
        }

        bitmap.Save(Path.Combine(Shots, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        output.WriteLine($"Capture {name} {r} : noir {black}, blanc {white}, autre {other}");
    }

    private static Rect Screen(IntPtr h)
    {
        GetWindowRect(h, out var r);
        return r;
    }

    private static List<IntPtr> Ours()
    {
        int pid = Environment.ProcessId;
        var list = new List<IntPtr>();
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out int p);
            if (p == pid)
            {
                list.Add(h);
            }

            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static long Area(IntPtr h)
    {
        GetWindowRect(h, out var r);
        return (long)r.Width * r.Height;
    }

    private static string Title(IntPtr h)
    {
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string Describe(IntPtr h)
    {
        var cls = new StringBuilder(256);
        GetClassName(h, cls, cls.Capacity);
        GetWindowRect(h, out var r);
        int thread = GetWindowThreadProcessId(h, out _);
        return $"{h} classe {cls} titre « {Title(h)} » {r} style 0x{GetWindowLongPtr(h, GwlStyle).ToInt64():X} ex 0x{GetWindowLongPtr(h, GwlExStyle).ToInt64():X} " +
               $"thread {thread} (nous {GetCurrentThreadId()}) propriétaire {GetWindow(h, GwOwner)} parent {GetParent(h)}";
    }

    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsPopup = 0x80000000;
    private const long WsChild = 0x40000000;
    private const uint GwOwner = 4;
    private const uint SwpNoZOrder = 0x4;
    private const uint SwpNoActivate = 0x10;
    private const uint SwpFrameChanged = 0x20;
    private const uint SwpShowWindow = 0x40;
    private const uint WmKeyDown = 0x100;
    private const uint WmKeyUp = 0x101;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;

        public override readonly string ToString() => $"({Left},{Top} {Width}x{Height})";
    }

    private delegate bool EnumProc(IntPtr h, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc proc, IntPtr param);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr h, out int pid);

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr h, StringBuilder text, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr h, StringBuilder text, int max);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr h, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr h, out Rect rect);

    [DllImport("user32.dll")]
    private static extern int MapWindowPoints(IntPtr from, IntPtr to, ref Rect rect, int count);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr h);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr h);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr h, uint cmd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}

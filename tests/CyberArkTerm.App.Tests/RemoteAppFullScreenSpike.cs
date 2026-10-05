using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CyberArkTerm.App.Services.Rdp;
using CyberArkTerm.Core.Diagnostics;
using CyberArkTerm.Core.Rdp;
using Xunit.Abstractions;

namespace CyberArkTerm.App.Tests;

/// <summary>
/// Expérience (non fusionnée) : la fenêtre de l'application rattachée à l'onglet est-elle aussi déplacée sur le
/// serveur ? Chaque clic reçu s'affiche dans son titre (numéro, position dans la fenêtre) ; un carré rouge est dessiné
/// en haut à gauche de la fenêtre. Plusieurs façons de faire suivre le serveur sont essayées.
/// </summary>
public class RemoteAppFullScreenSpike(ITestOutputHelper output)
{
    private const string Script = """
        Add-Type -AssemblyName System.Windows.Forms
        $script:n = 0
        $script:main = New-Object System.Windows.Forms.Form
        $main.Text = 'Spike main'; $main.Width = 640; $main.Height = 420; $main.StartPosition = 'CenterScreen'
        $panel = New-Object System.Windows.Forms.Panel
        $panel.BackColor = [System.Drawing.Color]::Red; $panel.Left = 0; $panel.Top = 0; $panel.Width = 120; $panel.Height = 80
        $main.Controls.Add($panel)
        $handler = { param($s, $e) $script:n++; $p = $script:main.PointToClient([System.Windows.Forms.Control]::MousePosition); $script:main.Text = "clic $($script:n) $($p.X),$($p.Y)" }
        $main.Add_MouseDown($handler)
        $panel.Add_MouseDown($handler)
        [System.Windows.Forms.Application]::Run($main)
        """;

    [Fact]
    [Trait("Category", "RdpSpike")]
    public async Task DockedWindowFollowsOnTheServer()
    {
        var user = Environment.GetEnvironmentVariable("RDP_TEST_USER");
        var password = Environment.GetEnvironmentVariable("RDP_TEST_PASSWORD");
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
        {
            return;
        }

        File.WriteAllText(@"C:\Users\Public\spike.ps1", Script);
        var log = Path.Combine(Path.GetTempPath(), "spike-debug.log");
        File.Delete(log);
        DebugLog.Start(log, "spike");
        var settings = RdpConnectionSettings.FromRdpFile(Encoding.Unicode.GetBytes(
            $"full address:s:127.0.0.2:3389\r\nusername:s:{Environment.MachineName}\\{user}\r\nauthentication level:i:0\r\nenablecredsspsupport:i:1\r\n" +
            "remoteapplicationmode:i:1\r\ndisableremoteappcapscheck:i:1\r\n" +
            "remoteapplicationprogram:s:C:\\Windows\\System32\\conhost.exe\r\nremoteapplicationname:s:PSM-RDP\r\n" +
            "remoteapplicationcmdline:s:--headless C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\\Users\\Public\\spike.ps1\r\n"));
        var request = new RdpConnectionRequest(settings, password);

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var session = new RdpSession("spike", _ => Task.FromResult(request)) { RemoteAppInTab = true };
            var window = new Window { Left = 120, Top = 90, Width = 860, Height = 600, ShowInTaskbar = false, Content = session.Host };
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
                    RemoteAppDock.Paused = false;
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
        try
        {
            await done.Task.WaitAsync(TimeSpan.FromMinutes(5));
        }
        finally
        {
            RemoteAppDock.Paused = false;
            DebugLog.Stop();
            output.WriteLine("---- journal ----");
            foreach (var line in File.ReadAllLines(log).Where(l => l.Contains(" rdp ", StringComparison.Ordinal)))
            {
                output.WriteLine(line);
            }
        }
    }

    private async Task Scenario(RdpSession session, Window window)
    {
        await session.ConnectAsync();
        for (int i = 0; i < 180 && session.State == RdpSessionState.Connecting; i++)
        {
            await Task.Delay(500);
        }

        output.WriteLine($"État : {session.State}, {session.Error}");
        var host = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        var slot = session.Host.SlotHandle;
        IntPtr main = IntPtr.Zero;
        for (int i = 0; i < 120 && main == IntPtr.Zero; i++)
        {
            await Task.Delay(500);
            main = Win32Input.Descendants(slot).FirstOrDefault(h => Win32Input.ClassName(h) == "RAIL_WINDOW" && Win32Input.Title(h).StartsWith("Spike", StringComparison.Ordinal));
        }

        if (main == IntPtr.Zero)
        {
            output.WriteLine("Fenêtre introuvable dans l'onglet");
            return;
        }

        await Task.Delay(2000);
        var container = GetParent(main);
        output.WriteLine($"Onglet {Win32Input.ScreenBounds(slot)}, conteneur {Win32Input.ScreenBounds(container)}, fenêtre {Win32Input.ScreenBounds(main)}");
        await Probe(host, main, "V0 rattachée (version actuelle)");

        // V4 : déplacement au clavier demandé au serveur (SC_MOVE) : le contrôle fait le déplacement localement,
        // puis envoie la position finale au serveur ; Entrée termine aussitôt (position finale : l'onglet).
        RemoteAppDock.Paused = true;
        await MoveSize(session, main, 0xF010, "V4 SC_MOVE");
        await Probe(host, main, "V4 SC_MOVE");
        await MoveSize(session, main, 0xF000, "V5 SC_SIZE");
        await Probe(host, main, "V5 SC_SIZE");
        RemoteAppDock.Paused = false;
        await Task.Delay(1500);
        await Probe(host, main, "V6 rattachement repris");

        session.Disconnect();
        await Task.Delay(3000);
    }

    /// <summary>Commande système envoyée à la fenêtre, puis Entrée si une boucle de déplacement locale a démarré.</summary>
    private async Task MoveSize(RdpSession session, IntPtr main, int command, string step)
    {
        GetCursorPos(out var cursor);
        output.WriteLine($"{step} : curseur avant {cursor.X},{cursor.Y}");
        PostMessage(main, 0x0112, new IntPtr(command), IntPtr.Zero);
        int thread = GetWindowThreadProcessId(main, out _);
        bool inLoop = false;
        for (int i = 0; i < 30 && !inLoop; i++)
        {
            await Task.Delay(100);
            var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
            if (GetGUIThreadInfo(thread, ref info) && (info.Flags & 0x2) != 0)
            {
                inLoop = true;
                GetCursorPos(out cursor);
                output.WriteLine($"{step} : boucle locale après {(i + 1) * 100} ms, fenêtre 0x{info.MoveSize.ToInt64():X}, curseur {cursor.X},{cursor.Y}, fenêtre {Win32Input.ScreenBounds(main)}");
            }
        }

        if (!inLoop)
        {
            output.WriteLine($"{step} : pas de boucle locale ; fenêtre {Win32Input.ScreenBounds(main)}");
        }
        else
        {
            PostMessage(main, 0x0100, new IntPtr(0x0D), new IntPtr(0x001C0001));
            PostMessage(main, 0x0101, new IntPtr(0x0D), new IntPtr(unchecked((int)0xC01C0001)));
        }

        await Task.Delay(2500);
        var info2 = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        GetGUIThreadInfo(thread, ref info2);
        GetCursorPos(out cursor);
        output.WriteLine($"{step} : après, boucle {(info2.Flags & 0x2) != 0}, curseur {cursor.X},{cursor.Y}, fenêtre {Win32Input.ScreenBounds(main)}");
    }

    /// <summary>Où est dessiné le carré rouge, et où arrivent deux clics (près du coin haut gauche et au centre) ?</summary>
    private async Task Probe(IntPtr host, IntPtr main, string step)
    {
        var b = Win32Input.ScreenBounds(main);
        output.WriteLine($"{step} : fenêtre {b}, carré rouge {RedBox(b)}");
        await ClickAndRead(host, main, (b.Left + 60, b.Top + 60), $"{step} clic (60,60)");
        await ClickAndRead(host, main, (b.Left + (b.Width / 2), b.Top + (b.Height / 2)), $"{step} clic centre");
        await ClickAndRead(host, main, (b.Left + b.Width - 40, b.Top + b.Height - 40), $"{step} clic bas droite");
    }


    private async Task ClickAndRead(IntPtr host, IntPtr main, (int X, int Y) point, string step)
    {
        var before = Win32Input.Title(main);
        Win32Input.BringToFront(GetParent(main) == IntPtr.Zero ? main : host);
        await Task.Delay(300);
        Win32Input.Click(point);
        string after = before;
        for (int i = 0; i < 12 && after == before; i++)
        {
            await Task.Delay(250);
            after = Win32Input.Title(main);
        }

        output.WriteLine($"  {step} en {point} : « {before} » -> « {after} »");
    }

    /// <summary>Rectangle (écran) des pixels rouges dans la fenêtre, ou « aucun ».</summary>
    private static string RedBox(Win32Input.Bounds b)
    {
        using var bitmap = new Bitmap(b.Width, b.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(b.Left, b.Top, 0, 0, new System.Drawing.Size(b.Width, b.Height), CopyPixelOperation.SourceCopy);
        }

        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (int y = 0; y < b.Height; y += 2)
        {
            for (int x = 0; x < b.Width; x += 2)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.R > 200 && c.G < 60 && c.B < 60)
                {
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return right < 0 ? "aucun" : $"{b.Left + left},{b.Top + top} -> {b.Left + right},{b.Top + bottom} (dans la fenêtre {left},{top})";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(int thread, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr window, out int process);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr h);

    [DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr h, out Rect r);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam);
}

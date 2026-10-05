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

        // V1 : déplacement encadré de WM_ENTERSIZEMOVE / WM_EXITSIZEMOVE (comme un déplacement à la souris).
        RemoteAppDock.Paused = true;
        await session.InvokeOnControlAsync(_ =>
        {
            GetClientRect(container, out var r);
            SendMessage(main, 0x0231, IntPtr.Zero, IntPtr.Zero);
            SetWindowPos(main, IntPtr.Zero, 0, 0, r.Right - 1, r.Bottom - 1, 0x0004 | 0x0010);
            SetWindowPos(main, IntPtr.Zero, 0, 0, r.Right, r.Bottom, 0x0004 | 0x0010);
            SendMessage(main, 0x0232, IntPtr.Zero, IntPtr.Zero);
            return 0;
        });
        await Task.Delay(2000);
        await Probe(host, main, "V1 ENTERSIZEMOVE/EXITSIZEMOVE");

        // V2 : de nouveau fenêtre de premier niveau, placée sur l'onglet, puis rattachée.
        var tab = Win32Input.ScreenBounds(container);
        await session.InvokeOnControlAsync(_ =>
        {
            long style = GetWindowLongPtr(main, -16).ToInt64();
            SetParent(main, IntPtr.Zero);
            SetWindowLongPtr(main, -16, new IntPtr((style & ~0x40000000L) | 0x80000000L));
            SendMessage(main, 0x0231, IntPtr.Zero, IntPtr.Zero);
            SetWindowPos(main, new IntPtr(-1), tab.Left, tab.Top, tab.Width, tab.Height, 0x0010 | 0x0020);
            SendMessage(main, 0x0232, IntPtr.Zero, IntPtr.Zero);
            return 0;
        });
        await Task.Delay(2000);
        output.WriteLine($"V2 premier niveau : fenêtre {Win32Input.ScreenBounds(main)} parent 0x{GetParent(main).ToInt64():X}");
        await Probe(host, main, "V2 premier niveau sur l'onglet");
        await session.InvokeOnControlAsync(_ =>
        {
            long style = GetWindowLongPtr(main, -16).ToInt64();
            SetWindowPos(main, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            SetParent(main, container);
            SetWindowLongPtr(main, -16, new IntPtr((style & ~0x80000000L) | 0x40000000L));
            GetClientRect(container, out var r);
            SetWindowPos(main, IntPtr.Zero, 0, 0, r.Right, r.Bottom, 0x0010 | 0x0020);
            return 0;
        });
        await Task.Delay(2000);
        await Probe(host, main, "V2 rattachée ensuite");

        // V3 : agrandie sur le serveur (commande système).
        PostMessage(main, 0x0112, new IntPtr(0xF030), IntPtr.Zero);
        await Task.Delay(2500);
        await Probe(host, main, "V3 SC_MAXIMIZE");
        RemoteAppDock.Paused = false;

        session.Disconnect();
        await Task.Delay(3000);
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
            g.CopyFromScreen(b.Left, b.Top, 0, 0, new System.Drawing.Size(b.Width, b.Height), CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
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

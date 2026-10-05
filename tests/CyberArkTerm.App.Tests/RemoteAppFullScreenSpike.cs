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
/// Expérience (non fusionnée) : comme le composant RDP du PSM, l'application distante est un client Bureau à distance
/// ouvert en plein écran (ici mstsc vers ce même poste), affiché dans l'onglet, puis remis en fenêtre
/// (Ctrl+Alt+Attn) et de nouveau en plein écran. Où vont les fenêtres, et les clics ?
/// </summary>
public class RemoteAppFullScreenSpike(ITestOutputHelper output)
{
    private const long WsMaximize = 0x01000000;

    [Fact]
    [Trait("Category", "RdpSpike")]
    public async Task FullScreenRdpClientInTheTab()
    {
        var user = Environment.GetEnvironmentVariable("RDP_TEST_USER");
        var password = Environment.GetEnvironmentVariable("RDP_TEST_PASSWORD");
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
        {
            return;
        }

        // Réglages par défaut de mstsc dans la session distante : plein écran, ni NLA ni avertissement de certificat
        // (il s'arrête à l'écran d'ouverture de session du poste imbriqué, ce qui suffit).
        File.WriteAllText(@"C:\Users\Public\spike-default.rdp",
            "screen mode id:i:2\r\nauthentication level:i:0\r\nenablecredsspsupport:i:0\r\nprompt for credentials:i:0\r\n" +
            "displayconnectionbar:i:1\r\npinconnectionbar:i:1\r\ndesktopwidth:i:1024\r\ndesktopheight:i:768\r\n", Encoding.Unicode);
        var log = Path.Combine(Path.GetTempPath(), "spike-debug.log");
        File.Delete(log);
        DebugLog.Start(log, "spike");
        var settings = RdpConnectionSettings.FromRdpFile(Encoding.Unicode.GetBytes(
            $"full address:s:127.0.0.2:3389\r\nusername:s:{Environment.MachineName}\\{user}\r\nauthentication level:i:0\r\nenablecredsspsupport:i:1\r\n" +
            "remoteapplicationmode:i:1\r\ndisableremoteappcapscheck:i:1\r\n" +
            "remoteapplicationprogram:s:C:\\Windows\\System32\\cmd.exe\r\nremoteapplicationname:s:PSM-RDP\r\n" +
            "remoteapplicationcmdline:s:/c copy /y C:\\Users\\Public\\spike-default.rdp \"%USERPROFILE%\\Documents\\Default.rdp\" " +
            "& start \"\" C:\\Windows\\System32\\mstsc.exe /v:127.0.0.3 /f\r\n"));
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
            RemoteAppDock.Legacy = false;
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
        // A : comportement de la version publiée.
        RemoteAppDock.Legacy = true;
        await session.ConnectAsync();
        for (int i = 0; i < 180 && session.State == RdpSessionState.Connecting; i++)
        {
            await Task.Delay(500);
        }

        output.WriteLine($"État : {session.State}, {session.Error}");
        var host = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        var slot = session.Host.SlotHandle;
        output.WriteLine($"Onglet {Win32Input.ScreenBounds(slot)}, écran {System.Windows.Forms.SystemInformation.VirtualScreen}");
        IntPtr full = IntPtr.Zero;
        for (int i = 0; i < 60 && full == IntPtr.Zero; i++)
        {
            await Task.Delay(500);
            full = FullScreenWindow(slot);
        }

        await Task.Delay(3000);
        Dump("A1 plein écran, version publiée", slot);
        if (full == IntPtr.Zero)
        {
            output.WriteLine("Pas de fenêtre plein écran");
            return;
        }

        // Retour en fenêtre : clic dans la fenêtre plein écran (où qu'elle soit), puis Ctrl+Alt+Attn.
        await ToggleFullScreen(host, full);
        Dump("A2 après Ctrl+Alt+Attn", slot);
        await RightClickTitle(host, slot, "A2");

        // B : nouvelle version (plein écran dans l'onglet, fenêtre visible affichée).
        RemoteAppDock.Legacy = false;
        await Task.Delay(1500);
        Dump("B0 nouvelle version", slot);
        var docked = Docked(slot);
        if (docked != IntPtr.Zero)
        {
            await ToggleFullScreen(host, docked);
            Dump("B1 après Ctrl+Alt+Attn (plein écran)", slot);
            await RightClick(host, Docked(slot), "B1 centre");
            var current = Docked(slot) is var d && d != IntPtr.Zero ? d : FullScreenWindow(slot);
            if (current != IntPtr.Zero)
            {
                await ToggleFullScreen(host, current);
                Dump("B2 après Ctrl+Alt+Attn (fenêtre)", slot);
                await RightClickTitle(host, slot, "B2");
            }
        }

        session.Disconnect();
        await Task.Delay(3000);
    }

    /// <summary>Clic gauche au centre de la fenêtre (le serveur lui donne le clavier), puis Ctrl+Alt+Attn.</summary>
    private static async Task ToggleFullScreen(IntPtr host, IntPtr window)
    {
        Win32Input.BringToFront(IsTopLevel(window) ? window : host);
        await Task.Delay(300);
        var b = Win32Input.ScreenBounds(window);
        Win32Input.Click((b.Left + (b.Width / 2), b.Top + (b.Height / 2)));
        await Task.Delay(1000);
        Win32Input.Keys((0x11, 0x1D, false), (0x12, 0x38, false), (0x03, 0x46, true));
        await Task.Delay(5000);
    }

    /// <summary>Clic droit sur la barre de titre (dessinée par le serveur) de la fenêtre de l'onglet : menu système ?</summary>
    private async Task RightClickTitle(IntPtr host, IntPtr slot, string step)
    {
        var docked = Docked(slot);
        if (docked == IntPtr.Zero)
        {
            output.WriteLine($"{step} : aucune fenêtre dans l'onglet");
            return;
        }

        var b = Win32Input.ScreenBounds(docked);
        await RightClickAt(host, (b.Left + (b.Width / 3), b.Top + 10), $"{step} titre");
    }

    private async Task RightClick(IntPtr host, IntPtr window, string step)
    {
        if (window == IntPtr.Zero)
        {
            output.WriteLine($"{step} : aucune fenêtre dans l'onglet");
            return;
        }

        var b = Win32Input.ScreenBounds(window);
        await RightClickAt(host, (b.Left + (b.Width / 3), b.Top + (b.Height / 3)), step);
    }

    /// <summary>Clic droit : où s'ouvre un menu (fenêtre de l'application) par rapport au point cliqué ?</summary>
    private async Task RightClickAt(IntPtr host, (int X, int Y) point, string step)
    {
        var before = Win32Input.ProcessWindows().ToHashSet();
        Win32Input.BringToFront(host);
        await Task.Delay(300);
        Win32Input.Click(point, right: true);
        IntPtr menu = IntPtr.Zero;
        for (int i = 0; i < 12 && menu == IntPtr.Zero; i++)
        {
            await Task.Delay(250);
            menu = Win32Input.ProcessWindows().FirstOrDefault(h => !before.Contains(h) && Win32Input.IsWindowVisible(h)
                                                                   && Win32Input.ClassName(h) == "RAIL_WINDOW");
        }

        if (menu == IntPtr.Zero)
        {
            output.WriteLine($"{step} : clic droit en {point}, aucun menu");
            return;
        }

        var m = Win32Input.ScreenBounds(menu);
        output.WriteLine($"{step} : clic droit en {point}, menu {Describe(menu)} (écart {m.Left - point.X},{m.Top - point.Y})");
        Win32Input.Keys((0x1B, 0x01, false));
        await Task.Delay(800);
    }

    private static IntPtr Docked(IntPtr slot) =>
        Win32Input.Descendants(slot).FirstOrDefault(h => Win32Input.ClassName(h) == "RAIL_WINDOW" && Win32Input.IsWindowVisible(h));

    /// <summary>Fenêtre de l'application (dans l'onglet ou non) qui couvre tout l'écran.</summary>
    private static IntPtr FullScreenWindow(IntPtr slot)
    {
        var screen = System.Windows.Forms.SystemInformation.VirtualScreen;
        return Win32Input.ProcessWindows().Concat(Win32Input.Descendants(slot))
            .FirstOrDefault(h => Win32Input.ClassName(h) == "RAIL_WINDOW" && Win32Input.IsWindowVisible(h)
                                 && Win32Input.ScreenBounds(h) is var b && b.Width >= screen.Width && b.Height >= screen.Height);
    }

    private static bool IsTopLevel(IntPtr window) => GetParent(window) == IntPtr.Zero;

    private void Dump(string title, IntPtr slot)
    {
        output.WriteLine(title + " :");
        foreach (var h in Win32Input.ProcessWindows().Where(h => Win32Input.ClassName(h) == "RAIL_WINDOW" && Win32Input.IsWindowVisible(h)))
        {
            output.WriteLine("  " + Describe(h));
        }

        foreach (var h in Win32Input.Descendants(slot).Where(h => Win32Input.ClassName(h) == "RAIL_WINDOW"))
        {
            output.WriteLine("  onglet " + Describe(h));
        }
    }

    private static string Describe(IntPtr h)
    {
        long style = GetWindowLongPtr(h, -16).ToInt64();
        long ex = GetWindowLongPtr(h, -20).ToInt64();
        return $"0x{h.ToInt64():X} « {Win32Input.Title(h)} » {Win32Input.ScreenBounds(h)} style 0x{style:X} ex 0x{ex:X} " +
               $"agrandie {(style & WsMaximize) != 0} visible {Win32Input.IsWindowVisible(h)} parent 0x{GetParent(h).ToInt64():X}";
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr h);
}

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
/// Expérience (non fusionnée) : application distante ouverte agrandie (comme le client RDP du PSM), affichée dans
/// l'onglet. Le serveur ne déplace pas une fenêtre agrandie : les clics tombent-ils à côté ? Et comment la rétablir ?
/// </summary>
public class RemoteAppMaximizedSpike(ITestOutputHelper output)
{
    private const long WsMaximize = 0x01000000;

    [Fact]
    [Trait("Category", "RdpSpike")]
    public async Task MaximizedRemoteAppInTheTab()
    {
        var user = Environment.GetEnvironmentVariable("RDP_TEST_USER");
        var password = Environment.GetEnvironmentVariable("RDP_TEST_PASSWORD");
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
        {
            return;
        }

        var log = Path.Combine(Path.GetTempPath(), "spike-debug.log");
        File.Delete(log);
        DebugLog.Start(log, "spike");
        var settings = RdpConnectionSettings.FromRdpFile(Encoding.Unicode.GetBytes(
            $"full address:s:127.0.0.2:3389\r\nusername:s:{Environment.MachineName}\\{user}\r\nauthentication level:i:0\r\nenablecredsspsupport:i:1\r\n" +
            "remoteapplicationmode:i:1\r\ndisableremoteappcapscheck:i:1\r\n" +
            "remoteapplicationprogram:s:C:\\Windows\\System32\\cmd.exe\r\nremoteapplicationname:s:PSM-RDP\r\n" +
            "remoteapplicationcmdline:s:/c start \"\" /max C:\\Windows\\System32\\notepad.exe\r\n"));
        var request = new RdpConnectionRequest(settings, password);

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var session = new RdpSession("spike", _ => Task.FromResult(request)) { RemoteAppInTab = true };
            var window = new Window { Left = 200, Top = 150, Width = 1000, Height = 700, ShowInTaskbar = false, Content = session.Host };
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
            await done.Task.WaitAsync(TimeSpan.FromMinutes(4));
        }
        finally
        {
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
        for (int i = 0; i < 40 && !session.RemoteAppShown; i++)
        {
            await Task.Delay(500);
        }

        await Task.Delay(2000);
        output.WriteLine($"Dans l'onglet : {session.RemoteAppShown}");
        Dump("Fenêtres");
        var host = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        var slot = session.Host.SlotHandle;
        var app = Win32Input.Descendants(slot).FirstOrDefault(h => Win32Input.ClassName(h) == "RAIL_WINDOW");
        if (app == IntPtr.Zero)
        {
            output.WriteLine("Aucune fenêtre dans l'onglet");
            return;
        }

        output.WriteLine($"Dans l'onglet : {Describe(app)} ; onglet {Win32Input.ScreenBounds(slot)}");
        await RightClick(host, app, "1 agrandie dans l'onglet");

        // R1 : commande système « restaurer » envoyée à la fenêtre (le contrôle la transmet-il au serveur ?).
        PostMessage(app, 0x0112, new IntPtr(0xF120), IntPtr.Zero);
        await Task.Delay(2500);
        output.WriteLine($"Après SC_RESTORE : {Describe(app)}");
        await RightClick(host, app, "2 après SC_RESTORE");

        // R2 : ShowWindow(SW_RESTORE) depuis le thread de la connexion.
        await session.InvokeOnControlAsync(_ => ShowWindow(app, 9));
        await Task.Delay(2500);
        output.WriteLine($"Après SW_RESTORE : {Describe(app)}");
        await RightClick(host, app, "3 après SW_RESTORE");

        // R3 : double-clic sur la barre de titre dessinée par le serveur (comme l'utilisateur).
        var b = Win32Input.ScreenBounds(app);
        Win32Input.BringToFront(host);
        await Task.Delay(300);
        Win32Input.Click((b.Left + (b.Width / 2), b.Top + 12));
        Win32Input.Click((b.Left + (b.Width / 2), b.Top + 12));
        await Task.Delay(2500);
        output.WriteLine($"Après double-clic sur le titre : {Describe(app)}");
        await RightClick(host, app, "4 après double-clic");
        Dump("Fenêtres à la fin");

        session.Disconnect();
        await Task.Delay(3000);
    }

    /// <summary>Clic droit dans la fenêtre : où s'ouvre le menu contextuel par rapport au point cliqué ?</summary>
    private async Task RightClick(IntPtr host, IntPtr app, string step)
    {
        var before = Win32Input.ProcessWindows().ToHashSet();
        var b = Win32Input.ScreenBounds(app);
        (int X, int Y) point = (b.Left + (b.Width / 3), b.Top + (b.Height / 3));
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
        output.WriteLine($"{step} : clic droit en {point}, menu {m} (écart {m.Left - point.X},{m.Top - point.Y})");
        // Fermer le menu : clic gauche ailleurs dans la fenêtre.
        Win32Input.Click((b.Left + (b.Width / 2), b.Top + b.Height - 20));
        await Task.Delay(800);
    }

    private void Dump(string title)
    {
        output.WriteLine(title + " :");
        foreach (var h in Win32Input.ProcessWindows().Where(h => Win32Input.ClassName(h) == "RAIL_WINDOW"))
        {
            output.WriteLine("  " + Describe(h));
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr h, int cmd);
}

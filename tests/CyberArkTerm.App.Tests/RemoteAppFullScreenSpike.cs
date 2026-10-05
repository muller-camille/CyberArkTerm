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
/// Expérience (non fusionnée) : une application distante qui, comme un client Bureau à distance, a une fenêtre
/// principale, affiche une boîte « connexion » puis une fenêtre plein écran (modale : la principale est désactivée),
/// fermée par Échap. Chaque clic reçu s'affiche dans le titre de la fenêtre : la souris arrive-t-elle ?
/// </summary>
public class RemoteAppFullScreenSpike(ITestOutputHelper output)
{
    private const string Script = """
        Add-Type -AssemblyName System.Windows.Forms
        $script:main = New-Object System.Windows.Forms.Form
        $main.Text = 'Spike main'; $main.Width = 640; $main.Height = 420; $main.StartPosition = 'CenterScreen'
        $main.Add_MouseDown({ param($s, $e) $script:main.Text = "clic $($e.X),$($e.Y)" })
        $script:timer = New-Object System.Windows.Forms.Timer
        $timer.Interval = 15000
        $timer.Add_Tick({
          $script:timer.Stop()
          $script:dlg = New-Object System.Windows.Forms.Form
          $dlg.Text = 'Spike connecting'; $dlg.FormBorderStyle = 'FixedDialog'; $dlg.MinimizeBox = $false; $dlg.MaximizeBox = $false
          $dlg.Width = 400; $dlg.Height = 150; $dlg.StartPosition = 'CenterParent'; $dlg.ShowInTaskbar = $false
          $script:t2 = New-Object System.Windows.Forms.Timer; $t2.Interval = 3000
          $t2.Add_Tick({ $script:t2.Stop(); $script:dlg.Close() })
          $t2.Start()
          [void]$dlg.ShowDialog($script:main)
          $script:full = New-Object System.Windows.Forms.Form
          $full.Text = 'Spike full'; $full.FormBorderStyle = 'None'; $full.MinimizeBox = $false; $full.MaximizeBox = $false
          $full.ShowInTaskbar = $false; $full.KeyPreview = $true; $full.StartPosition = 'Manual'
          $full.Bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
          $full.Add_MouseDown({ param($s, $e) $script:full.Text = "clic $($e.X),$($e.Y)" })
          $full.Add_KeyDown({ param($s, $e) if ($e.KeyCode -eq 'Escape') { $script:full.Close() } })
          [void]$full.ShowDialog($script:main)
          $script:timer.Start()
        })
        $main.Add_Shown({ $script:timer.Start() })
        [System.Windows.Forms.Application]::Run($main)
        """;

    private const long WsDisabled = 0x08000000;

    [Fact]
    [Trait("Category", "RdpSpike")]
    public async Task FullScreenWindowInTheTab()
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
        // conhost --headless : PowerShell sans fenêtre de console.
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
        output.WriteLine($"Onglet {Win32Input.ScreenBounds(slot)}");
        var main = await Find("Spike main", 60);
        Dump("A0 fenêtre principale", slot);
        await ClickAndRead(host, main, "A0 principale");

        for (int cycle = 0; cycle < 2; cycle++)
        {
            var step = cycle == 0 ? "A" : "B";
            var full = await Find("Spike full", 60);
            await Task.Delay(1500);
            Dump($"{step}1 plein écran", slot);
            await ClickAndRead(host, full, $"{step}1 plein écran");
            Win32Input.Keys((0x1B, 0x01, false));
            await Task.Delay(3000);
            Dump($"{step}2 après Échap", slot);
            await ClickAndRead(host, main, $"{step}2 principale");
            await Task.Delay(1500);
            await ClickAndRead(host, main, $"{step}2 principale (2e clic)");
            // B : nouvelle version.
            RemoteAppDock.Legacy = false;
        }

        session.Disconnect();
        await Task.Delay(3000);
    }

    /// <summary>Fenêtre de l'application (dans l'onglet ou non) dont le titre commence par <paramref name="title"/>, ou par « clic ».</summary>
    private async Task<IntPtr> Find(string title, int seconds)
    {
        for (int i = 0; i < seconds * 2; i++)
        {
            var found = AllWindows().FirstOrDefault(h => Win32Input.IsWindowVisible(h) && Win32Input.Title(h).StartsWith(title, StringComparison.Ordinal));
            if (found != IntPtr.Zero)
            {
                return found;
            }

            await Task.Delay(500);
        }

        output.WriteLine($"« {title} » introuvable");
        return IntPtr.Zero;
    }

    /// <summary>Clic gauche au milieu de la fenêtre : le titre (mis à jour par le serveur) dit si le clic est arrivé, et où.</summary>
    private async Task ClickAndRead(IntPtr host, IntPtr window, string step)
    {
        if (window == IntPtr.Zero || !Win32Input.IsWindowVisible(window))
        {
            output.WriteLine($"{step} : fenêtre absente ou masquée");
            return;
        }

        var before = Win32Input.Title(window);
        var b = Win32Input.ScreenBounds(window);
        (int X, int Y) point = (b.Left + (b.Width / 2), b.Top + (b.Height / 2));
        Win32Input.BringToFront(GetParent(window) == IntPtr.Zero ? window : host);
        await Task.Delay(300);
        Win32Input.Click(point);
        string after = before;
        for (int i = 0; i < 12 && after == before; i++)
        {
            await Task.Delay(250);
            after = Win32Input.Title(window);
        }

        output.WriteLine($"{step} : clic en {point} (fenêtre {b}, {(IsDisabled(window) ? "désactivée" : "active")}) : titre « {before} » -> « {after} »");
    }

    private IEnumerable<IntPtr> AllWindows() =>
        Win32Input.ProcessWindows().Concat(Win32Input.ProcessWindows().SelectMany(Win32Input.Descendants))
            .Where(h => Win32Input.ClassName(h) == "RAIL_WINDOW").Distinct();

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

    private static bool IsDisabled(IntPtr h) => (GetWindowLongPtr(h, -16).ToInt64() & WsDisabled) != 0;

    private static string Describe(IntPtr h)
    {
        long style = GetWindowLongPtr(h, -16).ToInt64();
        long ex = GetWindowLongPtr(h, -20).ToInt64();
        return $"0x{h.ToInt64():X} « {Win32Input.Title(h)} » {Win32Input.ScreenBounds(h)} style 0x{style:X} ex 0x{ex:X} " +
               $"visible {Win32Input.IsWindowVisible(h)} parent 0x{GetParent(h).ToInt64():X}";
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr h);
}

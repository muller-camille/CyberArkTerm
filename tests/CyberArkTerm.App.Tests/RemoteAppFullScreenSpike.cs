using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using CyberArkTerm.App.Services.Rdp;
using CyberArkTerm.Core.Diagnostics;
using CyberArkTerm.Core.Rdp;
using Xunit.Abstractions;

namespace CyberArkTerm.App.Tests;

/// <summary>
/// Expérience (non fusionnée) : comment terminer le déplacement local (envoi de la place de l'onglet) sans que le
/// contrôle envoie un clic à l'application ? Fenêtre plein écran dans l'onglet, onglet redimensionné, fin par Échap,
/// WM_CANCELMODE puis Entrée ; l'application écrit dans son titre chaque clic reçu (numéroté).
/// </summary>
public class RemoteAppFullScreenSpike(ITestOutputHelper output)
{
    private const string Script = """
        Add-Type -AssemblyName System.Windows.Forms
        $script:n = 0
        $script:enter = 0
        function Show-Click($form) {
          $script:n++
          $p = $form.PointToClient([System.Windows.Forms.Control]::MousePosition)
          $form.Text = "clic $($script:n) $($p.X),$($p.Y) e$($script:enter)"
        }
        $script:main = New-Object System.Windows.Forms.Form
        $main.Text = 'CAT main'; $main.Width = 640; $main.Height = 420; $main.StartPosition = 'CenterScreen'; $main.KeyPreview = $true
        $main.Add_MouseDown({ Show-Click $script:main })
        $main.Add_KeyDown({ param($s, $e)
          if ($e.KeyCode -eq 'Return') { $script:enter++ }
          if ($e.KeyCode -eq 'F11') {
            $script:full = New-Object System.Windows.Forms.Form
            $full.Text = 'CAT full'; $full.FormBorderStyle = 'None'; $full.ShowInTaskbar = $false; $full.KeyPreview = $true
            $full.StartPosition = 'Manual'; $full.Bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
            $full.Add_MouseDown({ Show-Click $script:full })
            $full.Add_KeyDown({ param($s, $e)
              if ($e.KeyCode -eq 'Return') { $script:enter++ }
              if ($e.KeyCode -eq 'Escape') { $script:full.Close() }
            })
            [void]$full.ShowDialog($script:main)
          }
        })
        [System.Windows.Forms.Application]::Run($main)
        """;

    [Fact]
    [Trait("Category", "RdpSpike")]
    public async Task EndingTheLocalMove()
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
            RemoteAppDock.EndMode = 0;
            RemoteAppDock.Park = true;
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
        var main = await Docked(slot, IntPtr.Zero);
        Win32Input.BringToFront(host);
        await Task.Delay(3000);
        await Click(host, main, slot, "principale", 60, 60);

        // Chaque mode : nouvelle fenêtre plein écran (en 0,0 sur le serveur, sous le pointeur), puis onglet agrandi ;
        // pointeur laissé au centre de l'onglet (sur l'application) pendant l'envoi de la place.
        foreach (var (mode, name) in new[] { (1, "Échap"), (2, "WM_CANCELMODE"), (0, "Entrée") })
        {
            RemoteAppDock.EndMode = mode;
            RemoteAppDock.Park = false;
            await Click(host, main, slot, $"{name} principale (clavier)", 60, 60);
            var tab = Win32Input.ScreenBounds(slot);
            Win32Input.MoveCursor(tab.Center);
            Win32Input.BringToFront(host);
            await Task.Delay(300);
            Win32Input.Keys((0x7A, 0x57));
            var full = await Docked(slot, main);
            await Task.Delay(3500);
            output.WriteLine($"{name} : plein écran placé, titre « {Win32Input.Title(full)} »");
            await Click(host, full, slot, $"{name} placé", 60, 60);
            tab = Win32Input.ScreenBounds(slot);
            await Click(host, full, slot, $"{name} placé", tab.Width - 15, tab.Height - 15);
            window.Width += 40;
            window.Height += 30;
            Win32Input.MoveCursor(Win32Input.ScreenBounds(slot).Center);
            Win32Input.BringToFront(host);
            await Task.Delay(3500);
            output.WriteLine($"{name} : onglet agrandi {Win32Input.ScreenBounds(slot)}, titre « {Win32Input.Title(full)} »");
            tab = Win32Input.ScreenBounds(slot);
            await Click(host, full, slot, $"{name} agrandi", tab.Width - 15, tab.Height - 15);
            Win32Input.Keys((0x1B, 0x01));
            for (int i = 0; i < 20 && Win32Input.IsWindowVisible(full); i++)
            {
                await Task.Delay(250);
            }

            await Task.Delay(2000);
        }

        session.Disconnect();
        await Task.Delay(3000);
    }

    private static async Task<IntPtr> Docked(IntPtr slot, IntPtr except)
    {
        for (int i = 0; i < 120; i++)
        {
            var window = Win32Input.Descendants(slot).FirstOrDefault(h => Win32Input.ClassName(h) == "RAIL_WINDOW" && Win32Input.IsWindowVisible(h)
                                                                          && h != except && Win32Input.Title(h).Length > 0);
            if (window != IntPtr.Zero)
            {
                return window;
            }

            await Task.Delay(250);
        }

        return IntPtr.Zero;
    }

    /// <summary>Clic à (60,60) dans l'onglet : reçu où (fenêtre du serveur à la place de l'onglet) et avec quel numéro ?</summary>
    private async Task Click(IntPtr host, IntPtr window, IntPtr slot, string step, int x, int y)
    {
        var tab = Win32Input.ScreenBounds(slot);
        (int X, int Y) point = (tab.Left + x, tab.Top + y);
        var before = Win32Input.Title(window);
        Win32Input.BringToFront(host);
        await Task.Delay(200);
        Win32Input.Click(point);
        string after = before;
        for (int i = 0; i < 8 && after == before; i++)
        {
            await Task.Delay(250);
            after = Win32Input.Title(window);
        }

        var m = Regex.Match(after, @"clic (\d+) (-?\d+),(-?\d+)");
        output.WriteLine($"  clic {step} en {point} : « {before} » -> « {after} »" + (m.Success ? $" (n° {m.Groups[1].Value} en {m.Groups[2].Value},{m.Groups[3].Value})" : ""));
    }
}

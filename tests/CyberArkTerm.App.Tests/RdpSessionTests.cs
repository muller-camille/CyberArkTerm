using System.Text;
using System.Windows;
using System.Windows.Threading;
using CyberArkTerm.App.Services.Rdp;
using CyberArkTerm.Core.Rdp;
using Xunit.Abstractions;

namespace CyberArkTerm.App.Tests;

public class RdpSessionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1001, 700, 1000, 700)]
    [InlineData(50, 50, 200, 200)]
    [InlineData(9000, 9000, 8192, 8192)]
    public void DesktopSizeStaysInRangeWithEvenWidth(int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.Equal((expectedWidth, expectedHeight), RdpSession.DesktopSize((width, height)));
    }

    [Theory]
    [InlineData(96, 100u, 100u)]
    [InlineData(120, 125u, 140u)]
    [InlineData(144, 150u, 140u)]
    [InlineData(192, 200u, 180u)]
    public void ScaleFactorsFollowScreenDpi(int dpi, uint desktop, uint device)
    {
        Assert.Equal((desktop, device), RdpSession.ScaleFactors(dpi));
    }

    /// <summary>
    /// Contrôle Bureau à distance réel (Windows) : création, réglages d'une connexion directe puis d'un fichier PSM,
    /// connexion vers un port fermé, et réception de l'événement de déconnexion avec son explication.
    /// </summary>
    [Fact]
    public async Task RealControlConnectsAndReportsTheFailure()
    {
        if (!OperatingSystem.IsWindows() || !RdpClientHost.IsAvailable)
        {
            Assert.True(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is null, "Contrôle Bureau à distance absent du poste de CI");
            output.WriteLine("Contrôle Bureau à distance absent : test ignoré.");
            return;
        }

        var direct = new RdpConnectionRequest(RdpConnectionSettings.Direct("127.0.0.1", 1, @"TEST\user"), "not-a-password");
        var psm = new RdpConnectionRequest(RdpConnectionSettings.FromRdpFile(Encoding.Unicode.GetBytes(
            "full address:s:127.0.0.1:1\r\nusername:s:jdoe\r\nalternate shell:s:psm /u admin /a srv01 /c PSM-RDP\r\n" +
            "authentication level:i:0\r\nenablecredsspsupport:i:0\r\nkeyboardhook:i:1\r\ndisable wallpaper:i:1\r\n")), null);

        foreach (var request in new[] { direct, psm })
        {
            var (state, controlFailed, reason, error) = await RunOnStaThread(request);
            output.WriteLine($"{request.Settings.Server}:{request.Settings.Port} → {state}, raison {reason}, {error}");
            Assert.False(controlFailed, error);
            Assert.Equal(RdpSessionState.Failed, state);
            // Code > 3 : échec de connexion signalé par l'événement OnDisconnected (pas une fin de session normale).
            Assert.True(reason > 3, $"raison {reason}");
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    private static Task<(RdpSessionState State, bool ControlFailed, int? Reason, string? Error)> RunOnStaThread(RdpConnectionRequest request)
    {
        var result = new TaskCompletionSource<(RdpSessionState, bool, int?, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var session = new RdpSession("test", _ => Task.FromResult(request));
            var window = new Window
            {
                Width = 900,
                Height = 650,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 0,
                Top = 0,
                Content = session.Host,
            };
            void Finish(RdpSessionState state)
            {
                result.TrySetResult((state, session.ControlFailed, session.DisconnectReason, session.Error));
                window.Dispatcher.BeginInvoke(() =>
                {
                    session.Dispose();
                    window.Close();
                    window.Dispatcher.InvokeShutdown();
                });
            }

            session.StateChanged += () =>
            {
                if (session.State is RdpSessionState.Failed or RdpSessionState.Ended)
                {
                    Finish(session.State);
                }
            };
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
            timeout.Tick += (_, _) => Finish(session.State);
            window.Show();
            timeout.Start();
            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, async () => await session.ConnectAsync());
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return result.Task;
    }
}

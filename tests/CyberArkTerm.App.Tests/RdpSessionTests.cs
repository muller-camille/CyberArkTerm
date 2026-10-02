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

    /// <summary>
    /// Vraie session sur ce poste (127.0.0.2) avec le compte de test créé par le workflow « rdp-integration » :
    /// ouverture de session dans l'onglet, puis déconnexion. Ignoré sans RDP_TEST_USER / RDP_TEST_PASSWORD.
    /// </summary>
    [Fact]
    [Trait("Category", "RdpIntegration")]
    public async Task DesktopSessionOpensOnARealServer()
    {
        if (IntegrationAccount() is not { } account)
        {
            return;
        }

        var request = new RdpConnectionRequest(RdpConnectionSettings.FromRdpFile(Encoding.Unicode.GetBytes(
            $"full address:s:127.0.0.2:3389\r\nusername:s:{account.User}\r\nauthentication level:i:0\r\nenablecredsspsupport:i:1\r\n")),
            account.Password);

        await RunOnStaAsync(request, async session =>
        {
            await session.ConnectAsync();
            var state = await WaitForAsync(session, s => s != RdpSessionState.Connecting, TimeSpan.FromSeconds(90));
            output.WriteLine($"Bureau : {state}, raison {session.DisconnectReason}, {session.Error}");
            Assert.Equal(RdpSessionState.Connected, state);
            Assert.True(session.ShowsDesktop);

            session.Disconnect();
            Assert.Equal(RdpSessionState.Ended, await WaitForAsync(session, s => s is RdpSessionState.Ended or RdpSessionState.Failed, TimeSpan.FromSeconds(30)));
        });
    }

    /// <summary>
    /// Application distante (RemoteApp) sur ce poste : le Bloc-notes est lancé dans une nouvelle session, l'onglet
    /// n'affiche pas de bureau, et Déconnecter ferme la session.
    /// </summary>
    [Fact]
    [Trait("Category", "RdpIntegration")]
    public async Task RemoteAppStartsOnARealServer()
    {
        if (IntegrationAccount() is not { } account)
        {
            return;
        }

        var request = new RdpConnectionRequest(RdpConnectionSettings.FromRdpFile(Encoding.Unicode.GetBytes(
            $"full address:s:127.0.0.2:3389\r\nusername:s:{account.User}\r\nauthentication level:i:0\r\nenablecredsspsupport:i:1\r\n" +
            "remoteapplicationmode:i:1\r\nremoteapplicationprogram:s:C:\\Windows\\System32\\notepad.exe\r\n" +
            "remoteapplicationname:s:Bloc-notes\r\ndisableremoteappcapscheck:i:1\r\nalternate shell:s:rdpinit.exe\r\n")),
            account.Password);
        int ourSession = System.Diagnostics.Process.GetCurrentProcess().SessionId;

        await RunOnStaAsync(request, async session =>
        {
            await session.ConnectAsync();
            var state = await WaitForAsync(session, s => s != RdpSessionState.Connecting, TimeSpan.FromSeconds(90));
            output.WriteLine($"Application distante : {state}, raison {session.DisconnectReason}, {session.Error}");
            Assert.Equal(RdpSessionState.Connected, state);
            Assert.True(session.IsRemoteApp);
            Assert.False(session.ShowsDesktop);
            Assert.Equal("Bloc-notes", session.RemoteAppName);

            // Le Bloc-notes tourne dans la session ouverte pour l'application (pas dans celle des tests).
            bool started = false;
            for (int i = 0; i < 60 && !started; i++)
            {
                started = System.Diagnostics.Process.GetProcessesByName("notepad").Any(p => p.SessionId != ourSession);
                if (!started)
                {
                    await Task.Delay(500);
                }
            }

            output.WriteLine($"Bloc-notes dans une autre session : {started} ; état {session.State}, {session.Error}");
            Assert.True(started, "Bloc-notes non lancé dans la session distante");
            Assert.Equal(RdpSessionState.Connected, session.State);

            session.Disconnect();
            var end = await WaitForAsync(session, s => s is RdpSessionState.Ended or RdpSessionState.Failed, TimeSpan.FromSeconds(30));
            output.WriteLine($"Fin : {end}, raison {session.DisconnectReason}, {session.Error}");
            Assert.Equal(RdpSessionState.Ended, end);
        });
    }

    /// <summary>Compte de test fourni par le workflow d'intégration, ou null (tests ignorés).</summary>
    private (string User, string Password)? IntegrationAccount()
    {
        var user = Environment.GetEnvironmentVariable("RDP_TEST_USER");
        var password = Environment.GetEnvironmentVariable("RDP_TEST_PASSWORD");
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password) || !RdpClientHost.IsAvailable)
        {
            output.WriteLine("Pas de compte RDP de test : test d'intégration ignoré.");
            return null;
        }

        return ($"{Environment.MachineName}\\{user}", password);
    }

    /// <summary>Exécute <paramref name="scenario"/> sur un thread STA, la session étant affichée dans une fenêtre.</summary>
    private static async Task RunOnStaAsync(RdpConnectionRequest request, Func<RdpSession, Task> scenario)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var session = new RdpSession("test", _ => Task.FromResult(request));
            var window = new Window { Width = 900, Height = 650, ShowInTaskbar = false, ShowActivated = false, Content = session.Host };
            window.Show();
            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, async () =>
            {
                try
                {
                    await scenario(session);
                    done.TrySetResult();
                }
                catch (Exception e)
                {
                    done.TrySetException(e);
                }
                finally
                {
                    session.Dispose();
                    window.Close();
                    window.Dispatcher.InvokeShutdown();
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromMinutes(4));
    }

    /// <summary>Attend (sur le thread de la session) un état qui vérifie <paramref name="until"/>.</summary>
    private static async Task<RdpSessionState> WaitForAsync(RdpSession session, Func<RdpSessionState, bool> until, TimeSpan timeout)
    {
        var reached = new TaskCompletionSource<RdpSessionState>();
        void Check()
        {
            if (until(session.State))
            {
                reached.TrySetResult(session.State);
            }
        }

        session.StateChanged += Check;
        try
        {
            Check();
            return await reached.Task.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            throw new Xunit.Sdk.XunitException($"Délai dépassé : état {session.State}, raison {session.DisconnectReason}, {session.Error}");
        }
        finally
        {
            session.StateChanged -= Check;
        }
    }

    /// <summary>Fermer l'onglet pendant que la connexion se prépare (appel au PVWA) ne doit pas lever d'erreur.</summary>
    [Fact]
    public async Task ClosingWhilePreparingDoesNotThrow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var done = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var session = new RdpSession("test", async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("jamais atteint");
            });
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    var connect = session.ConnectAsync();
                    session.Dispose();
                    await connect;
                    done.TrySetResult(null);
                }
                catch (Exception e)
                {
                    done.TrySetResult(e);
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        var error = await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Null(error);
    }

    /// <summary>
    /// « Déconnecter » pendant une connexion qui ne répond pas (adresse non routable) l'abandonne au lieu de ne rien faire.
    /// </summary>
    [Fact]
    public async Task DisconnectAbandonsAPendingConnection()
    {
        if (!OperatingSystem.IsWindows() || !RdpClientHost.IsAvailable)
        {
            Assert.True(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is null, "Contrôle Bureau à distance absent du poste de CI");
            return;
        }

        var request = new RdpConnectionRequest(RdpConnectionSettings.Direct("10.255.255.1", 3389, @"TEST\user"), null);
        var (state, abandoned) = await RunOnStaThread(request, disconnectAfter: TimeSpan.FromSeconds(2));
        output.WriteLine($"→ {state}, abandonnée pendant la connexion : {abandoned}");
        Assert.NotEqual(RdpSessionState.Connecting, state);
        if (abandoned)
        {
            Assert.Equal(RdpSessionState.Ended, state);
        }
    }

    private static async Task<(RdpSessionState State, bool Abandoned)> RunOnStaThread(RdpConnectionRequest request, TimeSpan disconnectAfter)
    {
        var result = new TaskCompletionSource<(RdpSessionState, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var session = new RdpSession("test", _ => Task.FromResult(request));
            var window = new Window { Width = 900, Height = 650, ShowInTaskbar = false, ShowActivated = false, Content = session.Host };
            bool abandoned = false;
            void Finish()
            {
                result.TrySetResult((session.State, abandoned));
                window.Dispatcher.BeginInvoke(() =>
                {
                    session.Dispose();
                    window.Close();
                    window.Dispatcher.InvokeShutdown();
                });
            }

            var disconnect = new DispatcherTimer { Interval = disconnectAfter };
            disconnect.Tick += (_, _) =>
            {
                disconnect.Stop();
                if (session.State == RdpSessionState.Connecting && session.HasControl)
                {
                    abandoned = true;
                    session.Disconnect();
                }

                // Laisse le temps à un éventuel événement de déconnexion d'arriver.
                var settle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                settle.Tick += (_, _) =>
                {
                    settle.Stop();
                    Finish();
                };
                settle.Start();
            };
            window.Show();
            disconnect.Start();
            window.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, async () => await session.ConnectAsync());
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        return await result.Task.WaitAsync(TimeSpan.FromSeconds(60));
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

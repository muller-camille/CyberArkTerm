using System.Runtime.InteropServices;
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
        Assert.Equal((expectedWidth, expectedHeight), RdpConnection.DesktopSize((width, height)));
    }

    [Theory]
    [InlineData(96, 100u, 100u)]
    [InlineData(120, 125u, 140u)]
    [InlineData(144, 150u, 140u)]
    [InlineData(192, 200u, 180u)]
    public void ScaleFactorsFollowScreenDpi(int dpi, uint desktop, uint device)
    {
        Assert.Equal((desktop, device), RdpConnection.ScaleFactors(dpi));
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
            // Les codes de Windows accompagnent l'explication.
            Assert.Contains(reason!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), error);
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
            output.WriteLine($"Événements : {string.Join(" | ", session.RemoteAppEvents)}");
            output.WriteLine(OtherSessions(ourSession));
            Assert.True(started, "Bloc-notes non lancé dans la session distante");
            Assert.Equal(RdpSessionState.Connected, session.State);

            session.Disconnect();
            var end = await WaitForAsync(session, s => s is RdpSessionState.Ended or RdpSessionState.Failed, TimeSpan.FromSeconds(30));
            output.WriteLine($"Fin : {end}, raison {session.DisconnectReason}, {session.Error}");
            Assert.Equal(RdpSessionState.Ended, end);
        });
    }

    /// <summary>
    /// Fichier d'application distante PSM ouvert comme un bureau (option des composants PSM en RemoteApp) : la session
    /// s'affiche dans l'onglet, sans mode RemoteApp, et démarre le programme publié de l'application, pas celui du
    /// fichier (« PSM@… »). Un serveur PSM (hôte de session Bureau à distance) le lance ; le poste de CI, un Windows
    /// Server sans ce rôle, ouvre à la place son bureau habituel (constaté) : le lancement n'est donc qu'affiché.
    /// </summary>
    [Fact]
    [Trait("Category", "RdpIntegration")]
    public async Task RemoteAppFileOpensAsDesktopOnARealServer()
    {
        if (IntegrationAccount() is not { } account)
        {
            return;
        }

        var settings = PsmRemoteAppFile(account.User).RemoteAppAsDesktop();
        Assert.NotNull(settings);
        var request = new RdpConnectionRequest(settings, account.Password);
        int ourSession = System.Diagnostics.Process.GetCurrentProcess().SessionId;

        await RunOnStaAsync(request, async session =>
        {
            await session.ConnectAsync();
            var state = await WaitForAsync(session, s => s != RdpSessionState.Connecting, TimeSpan.FromSeconds(90));
            output.WriteLine($"Application distante en bureau : {state}, raison {session.DisconnectReason}, {session.Error}");
            Assert.Equal(RdpSessionState.Connected, state);
            Assert.True(session.ShowsDesktop);
            Assert.False(session.IsRemoteApp);
            Assert.True(session.DesktopFromRemoteApp);

            var startProgram = await session.InvokeOnControlAsync(ocx =>
                Dispatch.Get(Dispatch.First(ocx, "SecuredSettings3", "SecuredSettings2")!, "StartProgram") as string);
            Assert.Equal(@"C:\Windows\System32\notepad.exe", startProgram);

            await Task.Delay(TimeSpan.FromSeconds(5));
            output.WriteLine($"Programme de démarrage lancé par ce serveur : " +
                             $"{System.Diagnostics.Process.GetProcessesByName("notepad").Any(p => p.SessionId != ourSession)}");
            output.WriteLine(OtherSessions(ourSession));

            session.Disconnect();
            Assert.Equal(RdpSessionState.Ended, await WaitForAsync(session, s => s is RdpSessionState.Ended or RdpSessionState.Failed, TimeSpan.FromSeconds(30)));
        });
    }

    /// <summary>
    /// « Ouvrir en fenêtres séparées » sur une application distante ouverte comme un bureau : nouvelle connexion en
    /// application distante, telle que le fichier la demande (le Bloc-notes est lancé dans la session ouverte).
    /// </summary>
    [Fact]
    [Trait("Category", "RdpIntegration")]
    public async Task RemoteAppOpenedAsDesktopCanOpenInSeparateWindows()
    {
        if (IntegrationAccount() is not { } account)
        {
            return;
        }

        var settings = PsmRemoteAppFile(account.User).RemoteAppAsDesktop();
        Assert.NotNull(settings);
        var request = new RdpConnectionRequest(settings, account.Password);
        int ourSession = System.Diagnostics.Process.GetCurrentProcess().SessionId;

        await RunOnStaAsync(request, async session =>
        {
            await session.OpenRemoteAppWindowsAsync();
            var state = await WaitForAsync(session, s => s != RdpSessionState.Connecting, TimeSpan.FromSeconds(90));
            output.WriteLine($"Fenêtres séparées : {state}, raison {session.DisconnectReason}, {session.Error}");
            Assert.Equal(RdpSessionState.Connected, state);
            Assert.True(session.IsRemoteApp);
            Assert.False(session.DesktopFromRemoteApp);
            Assert.False(session.ShowsDesktop);
            Assert.Equal("PSM-RDP", session.RemoteAppName);

            bool started = false;
            for (int i = 0; i < 60 && !started; i++)
            {
                started = System.Diagnostics.Process.GetProcessesByName("notepad").Any(p => p.SessionId != ourSession);
                if (!started)
                {
                    await Task.Delay(500);
                }
            }

            output.WriteLine($"Bloc-notes dans une autre session : {started} ; événements {string.Join(" | ", session.RemoteAppEvents)}");
            Assert.True(started, "Bloc-notes non lancé dans la session distante");

            session.Disconnect();
            Assert.Equal(RdpSessionState.Ended, await WaitForAsync(session, s => s is RdpSessionState.Ended or RdpSessionState.Failed, TimeSpan.FromSeconds(30)));
        });
    }

    /// <summary>
    /// Application distante ouverte comme un bureau que le serveur ferme aussitôt la session ouverte (comme un PSM qui
    /// n'accepte que l'application distante) : l'onglet la rouvre de lui-même en fenêtres séparées. Le serveur de CI
    /// accepte le bureau : le refus est simulé en fermant la session de test côté serveur.
    /// </summary>
    [Fact]
    [Trait("Category", "RdpIntegration")]
    public async Task RemoteAppRefusedAsDesktopReopensInSeparateWindows()
    {
        if (IntegrationAccount() is not { } account)
        {
            return;
        }

        var settings = PsmRemoteAppFile(account.User).RemoteAppAsDesktop();
        Assert.NotNull(settings);
        var request = new RdpConnectionRequest(settings, account.Password);
        var user = account.User.Split('\\')[^1];

        await RunOnStaAsync(request, async session =>
        {
            int refused = 0;
            session.DesktopRefused += () => refused++;
            await session.ConnectAsync();
            Assert.Equal(RdpSessionState.Connected, await WaitForAsync(session, s => s != RdpSessionState.Connecting, TimeSpan.FromSeconds(90)));
            Assert.True(session.DesktopFromRemoteApp);

            var closed = LogoffActiveSessions(user);
            output.WriteLine($"Sessions de {user} fermées côté serveur : {string.Join(", ", closed)}");
            Assert.NotEmpty(closed);

            // Fin de session (le refus est signalé juste après), puis nouvelle connexion en fenêtres séparées.
            var state = await WaitForAsync(session, s => refused > 0 && s != RdpSessionState.Connecting, TimeSpan.FromSeconds(90));
            output.WriteLine($"Après le refus : {state}, raison {session.DisconnectReason}, {session.Error}");
            Assert.Equal(1, refused);
            Assert.Equal(RdpSessionState.Connected, state);
            Assert.True(session.IsRemoteApp);
            Assert.True(session.RemoteAppFallback);
            Assert.False(session.DesktopFromRemoteApp);

            session.Disconnect();
            Assert.Equal(RdpSessionState.Ended, await WaitForAsync(session, s => s is RdpSessionState.Ended or RdpSessionState.Failed, TimeSpan.FromSeconds(30)));
            Assert.Equal(1, refused);
            Assert.False(session.RemoteAppFallback);
        });
    }

    /// <summary>Ferme (côté serveur) les sessions actives de <paramref name="user"/> sur ce poste ; renvoie leurs numéros.</summary>
    private static List<int> LogoffActiveSessions(string user)
    {
        const int WtsActive = 0;
        const int WtsUserName = 5;
        var closed = new List<int>();
        if (!WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var sessions, out int count))
        {
            throw new System.ComponentModel.Win32Exception();
        }

        try
        {
            int size = Marshal.SizeOf<WtsSessionInfo>();
            for (int i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<WtsSessionInfo>(sessions + (i * size));
                if (info.State != WtsActive || !WTSQuerySessionInformation(IntPtr.Zero, info.SessionId, WtsUserName, out var name, out _))
                {
                    continue;
                }

                try
                {
                    if (string.Equals(Marshal.PtrToStringUni(name), user, StringComparison.OrdinalIgnoreCase)
                        && WTSLogoffSession(IntPtr.Zero, info.SessionId, false))
                    {
                        closed.Add(info.SessionId);
                    }
                }
                finally
                {
                    WTSFreeMemory(name);
                }
            }
        }
        finally
        {
            WTSFreeMemory(sessions);
        }

        return closed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WtsSessionInfo
    {
        public int SessionId;
        public IntPtr WinStationName;
        public int State;
    }

    [DllImport("wtsapi32.dll", EntryPoint = "WTSEnumerateSessionsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSEnumerateSessions(IntPtr server, int reserved, int version, out IntPtr sessions, out int count);

    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSLogoffSession(IntPtr server, int sessionId, [MarshalAs(UnmanagedType.Bool)] bool wait);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    /// <summary>Fichier RemoteApp structuré comme celui du PVWA, avec le Bloc-notes comme application publiée.</summary>
    private static RdpConnectionSettings PsmRemoteAppFile(string user) => RdpConnectionSettings.FromRdpFile(Encoding.Unicode.GetBytes(
        $"full address:s:127.0.0.2:3389\r\nusername:s:{user}\r\nauthentication level:i:0\r\nenablecredsspsupport:i:1\r\n" +
        "alternate shell:s:PSM@0123abcd\r\nremoteapplicationmode:i:1\r\ndisableremoteappcapscheck:i:1\r\n" +
        "remoteapplicationprogram:s:C:\\Windows\\System32\\notepad.exe\r\nremoteapplicationname:s:PSM-RDP\r\n"));

    /// <summary>
    /// Application distante inconnue du serveur : la session se termine avec une explication, au lieu de rester
    /// ouverte sans rien afficher. (Le serveur annonce les applications distantes : pas de « disableremoteappcapscheck ».)
    /// </summary>
    [Fact]
    [Trait("Category", "RdpIntegration")]
    public async Task UnknownRemoteAppIsReported()
    {
        if (IntegrationAccount() is not { } account)
        {
            return;
        }

        var request = new RdpConnectionRequest(RdpConnectionSettings.FromRdpFile(Encoding.Unicode.GetBytes(
            $"full address:s:127.0.0.2:3389\r\nusername:s:{account.User}\r\nauthentication level:i:0\r\nenablecredsspsupport:i:1\r\n" +
            "remoteapplicationmode:i:1\r\nremoteapplicationprogram:s:||CyberArkTermInexistant\r\nremoteapplicationname:s:Inexistante\r\n")),
            account.Password);

        await RunOnStaAsync(request, async session =>
        {
            await session.ConnectAsync();
            var state = await WaitForAsync(session, s => s is RdpSessionState.Ended or RdpSessionState.Failed, TimeSpan.FromSeconds(120));
            output.WriteLine($"Application inconnue : {state}, raison {session.DisconnectReason}, {session.Error} ; " +
                             $"événements {string.Join(" | ", session.RemoteAppEvents)}");
            Assert.Equal(RdpSessionState.Failed, state);
            Assert.Contains("Inexistante", session.Error);
        });
    }

    private static string OtherSessions(int ourSession) => string.Join(Environment.NewLine,
        System.Diagnostics.Process.GetProcesses().Where(p => p.SessionId != ourSession && p.SessionId != 0)
            .GroupBy(p => p.SessionId).Select(g => $"session {g.Key} : {string.Join(", ", g.Select(p => p.ProcessName).Order())}"));

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
                    await CloseWindowAsync(session, window);
                }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromMinutes(4));
    }

    /// <summary>
    /// Ferme la session puis la fenêtre, une fois la fenêtre du contrôle sortie de l'onglet (elle appartient au thread
    /// de la connexion : la fenêtre principale ne doit pas être détruite avec elle).
    /// </summary>
    private static async Task CloseWindowAsync(RdpSession session, Window window)
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

    /// <summary>
    /// Le contrôle vit sur son propre thread : bloqué plusieurs secondes (connexion vers une adresse qui ne répond pas),
    /// il ne fige pas l'interface, qui le signale (« ne répond pas ») puis le voit répondre de nouveau.
    /// </summary>
    [Fact]
    public async Task InterfaceStaysResponsiveWhileTheControlIsBlocked()
    {
        if (!OperatingSystem.IsWindows() || !RdpClientHost.IsAvailable)
        {
            Assert.True(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is null, "Contrôle Bureau à distance absent du poste de CI");
            return;
        }

        var request = new RdpConnectionRequest(RdpConnectionSettings.Direct("10.255.255.1", 3389, @"TEST\user"), null);
        await RunOnStaAsync(request, async session =>
        {
            await session.ConnectAsync();
            Assert.True(session.HasControl, session.Error);
            int controlThread = await session.InvokeOnControlAsync(_ => Environment.CurrentManagedThreadId);
            Assert.NotEqual(Environment.CurrentManagedThreadId, controlThread);

            bool sawNotResponding = false;
            session.StateChanged += () => sawNotResponding |= session.IsNotResponding;
            int ticks = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) => ticks++;
            timer.Start();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var blocked = session.InvokeOnControlAsync(_ =>
            {
                Thread.Sleep(TimeSpan.FromSeconds(8));
                return true;
            });
            await Task.Delay(TimeSpan.FromSeconds(7));
            output.WriteLine($"Interface : {ticks} tops de 100 ms en {watch.ElapsedMilliseconds} ms, « ne répond pas » : {sawNotResponding}");
            Assert.True(ticks >= 35, $"{ticks} tops seulement : l'interface a été bloquée");
            Assert.True(sawNotResponding);

            Assert.True(await blocked);
            for (int i = 0; i < 40 && session.IsNotResponding; i++)
            {
                await Task.Delay(100);
            }

            timer.Stop();
            Assert.False(session.IsNotResponding);
        });
    }

    /// <summary>Fermer une session dont le contrôle est bloqué rend la main aussitôt ; il quitte l'onglet dès qu'il répond.</summary>
    [Fact]
    public async Task ClosingDoesNotWaitForABlockedControl()
    {
        if (!OperatingSystem.IsWindows() || !RdpClientHost.IsAvailable)
        {
            Assert.True(Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is null, "Contrôle Bureau à distance absent du poste de CI");
            return;
        }

        var request = new RdpConnectionRequest(RdpConnectionSettings.Direct("10.255.255.1", 3389, @"TEST\user"), null);
        await RunOnStaAsync(request, async session =>
        {
            await session.ConnectAsync();
            Assert.True(session.HasControl, session.Error);
            _ = session.InvokeOnControlAsync(_ =>
            {
                Thread.Sleep(TimeSpan.FromSeconds(4));
                return true;
            });
            await Task.Delay(200);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            session.Dispose();
            long disposeMs = watch.ElapsedMilliseconds;
            Assert.False(session.Closed.IsCompleted);
            await session.Closed.WaitAsync(TimeSpan.FromSeconds(30));
            output.WriteLine($"Fermeture : {disposeMs} ms pour rendre la main, fenêtre retirée après {watch.ElapsedMilliseconds} ms");
            Assert.True(disposeMs < 500, $"{disposeMs} ms");
        });
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
                window.Dispatcher.BeginInvoke(async () => await CloseWindowAsync(session, window));
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
                window.Dispatcher.BeginInvoke(async () => await CloseWindowAsync(session, window));
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

using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ZillaTerm.Core.Ftp;
using ZillaTerm.Core.Localization;
using ZillaTerm.Core.Ssh;

namespace ZillaTerm.Core.Tests.Ftp;

/// <summary>
/// Connexion FTP / FTPS contre un faux serveur : jamais de repli en clair sans accord, TLS exigé en FTPES, certificat
/// refusé avant tout envoi de l'identifiant, et pas de commande injectée par un nom de fichier.
/// </summary>
public sealed class FtpFileBrowserTests
{
    /// <summary>Faux serveur FTP : réponses minimales, TLS (AUTH TLS) seulement si un certificat est donné.</summary>
    private sealed class FakeFtp : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly X509Certificate2? _certificate;
        private readonly CancellationTokenSource _stop = new();

        public FakeFtp(X509Certificate2? certificate = null)
        {
            _certificate = certificate;
            _listener.Start();
            _ = AcceptAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        /// <summary>Commandes reçues, toutes connexions confondues (en clair ou après TLS).</summary>
        public ConcurrentQueue<string> Commands { get; } = new();

        public int Connections;

        /// <summary>AUTH TLS refusé alors que le serveur a un certificat (intermédiaire qui retire TLS).</summary>
        public bool TlsRemoved { get; set; }

        public bool Received(string verb) =>
            Commands.Any(c => c.StartsWith(verb + " ", StringComparison.OrdinalIgnoreCase) || c.Equals(verb, StringComparison.OrdinalIgnoreCase));

        private async Task AcceptAsync()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch (Exception)
                {
                    return;
                }

                Interlocked.Increment(ref Connections);
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using var owned = client;
            Stream stream = client.GetStream();
            try
            {
                await WriteAsync(stream, "220 fake FTP");
                while (await ReadLineAsync(stream) is { } line)
                {
                    Commands.Enqueue(line);
                    var verb = line.Split(' ')[0].ToUpperInvariant();
                    switch (verb)
                    {
                        case "AUTH" when _certificate is null || TlsRemoved:
                            await WriteAsync(stream, "500 AUTH not understood.");
                            break;
                        case "AUTH":
                            await WriteAsync(stream, "234 Proceed with negotiation.");
                            var ssl = new SslStream(stream);
                            await ssl.AuthenticateAsServerAsync(_certificate);
                            stream = ssl;
                            break;
                        case "USER":
                            await WriteAsync(stream, "331 Please specify the password.");
                            break;
                        case "PASS":
                            await WriteAsync(stream, "230 Login successful.");
                            break;
                        case "FEAT":
                            await WriteAsync(stream, "211-Features:\r\n SIZE\r\n MDTM\r\n211 End");
                            break;
                        case "PWD":
                            await WriteAsync(stream, "257 \"/home/fake\" is the current directory");
                            break;
                        case "SYST":
                            await WriteAsync(stream, "215 UNIX Type: L8");
                            break;
                        case "MKD":
                            await WriteAsync(stream, "257 \"" + line[4..] + "\" created");
                            break;
                        case "SIZE":
                            await WriteAsync(stream, line.Contains("taken", StringComparison.Ordinal) ? "213 12" : "550 No such file.");
                            break;
                        case "CWD":
                            await WriteAsync(stream, line[4..] == "/home/fake" ? "250 OK" : "550 Failed to change directory.");
                            break;
                        case "RNFR":
                            await WriteAsync(stream, "350 Ready for RNTO.");
                            break;
                        case "QUIT":
                            await WriteAsync(stream, "221 Goodbye.");
                            return;
                        default:
                            await WriteAsync(stream, "200 OK");
                            break;
                    }
                }
            }
            catch (Exception)
            {
                // Client parti (certificat refusé, fin du test).
            }
        }

        private static async Task WriteAsync(Stream stream, string reply)
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes(reply + "\r\n"));
            await stream.FlushAsync();
        }

        private static async Task<string?> ReadLineAsync(Stream stream)
        {
            var line = new List<byte>();
            var one = new byte[1];
            while (true)
            {
                if (await stream.ReadAsync(one) == 0)
                {
                    return null;
                }

                if (one[0] == '\n')
                {
                    return Encoding.UTF8.GetString(line.ToArray()).TrimEnd('\r');
                }

                line.Add(one[0]);
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }

    private static X509Certificate2 SelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=ftp.fake.test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Clé exportée puis relue : SslStream côté serveur (Windows) refuse une clé éphémère.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }

    private static FtpConnection Connection(FakeFtp server, FtpSecurity security, Func<bool>? allowCleartext = null,
        Func<FtpCertificate, bool>? trust = null) => new()
    {
        Host = "127.0.0.1",
        Port = server.Port,
        UserName = "alice",
        Password = () => "secret-pw",
        Security = security,
        AllowCleartext = allowCleartext ?? (() => throw new InvalidOperationException("question inattendue")),
        TrustCertificate = trust ?? (_ => throw new InvalidOperationException("question inattendue")),
    };

    [Fact]
    public async Task ServerWithoutTlsIsRefusedWhenTheUserDeclinesCleartext()
    {
        using var server = new FakeFtp();
        int asked = 0;

        var error = await Assert.ThrowsAsync<FtpRefusedException>(() =>
            FtpFileBrowser.ConnectAsync(Connection(server, FtpSecurity.Opportunistic, () => { asked++; return false; }), CancellationToken.None));

        Assert.Equal(CoreStrings.FtpCleartextDeclined, error.Message);
        Assert.Equal(1, asked);
        Assert.True(server.Received("AUTH"));
        // Ni l'identifiant ni le mot de passe ne sont partis en clair.
        Assert.False(server.Received("USER"));
        Assert.False(server.Received("PASS"));
    }

    [Fact]
    public async Task ExplicitTlsNeverFallsBackToCleartext()
    {
        using var server = new FakeFtp();

        var error = await Assert.ThrowsAsync<FtpRefusedException>(() =>
            FtpFileBrowser.ConnectAsync(Connection(server, FtpSecurity.Explicit), CancellationToken.None));

        Assert.Equal(CoreStrings.FtpTlsRequired, error.Message);
        Assert.False(server.Received("USER"));
        Assert.False(server.Received("PASS"));
        Assert.Equal(1, server.Connections);
    }

    [Fact]
    public async Task CleartextAcceptedByTheUserConnectsUnencrypted()
    {
        using var server = new FakeFtp();
        int asked = 0;

        using var browser = await FtpFileBrowser.ConnectAsync(
            Connection(server, FtpSecurity.Opportunistic, () => { asked++; return true; }), CancellationToken.None);

        Assert.Equal(1, asked);
        Assert.False(browser.IsEncrypted);
        Assert.Equal(TransferProtocol.Ftp, browser.UploadProtocol);
        Assert.False(browser.ChoosesUploadProtocol);
        Assert.Equal("/home/fake", browser.HomeDirectory);
        Assert.Contains("PASS secret-pw", server.Commands);
    }

    [Fact]
    public async Task ControlCharactersInAPathAreNeverSentToTheServer()
    {
        using var server = new FakeFtp();
        using var browser = await FtpFileBrowser.ConnectAsync(
            Connection(server, FtpSecurity.Opportunistic, () => true), CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => browser.CreateDirectoryAsync("/home/fake/a\r\nDELE b", CancellationToken.None));

        Assert.False(server.Received("MKD"));
        Assert.False(server.Received("DELE"));
        await browser.CreateDirectoryAsync("/home/fake/ok", CancellationToken.None);
        Assert.Contains("MKD /home/fake/ok", server.Commands);
    }

    [Fact]
    public async Task RenameNeverReplacesAnExistingFile()
    {
        using var server = new FakeFtp();
        using var browser = await FtpFileBrowser.ConnectAsync(
            Connection(server, FtpSecurity.Opportunistic, () => true), CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => browser.RenameAsync(FileEntry("/home/fake/a.txt"), "/home/fake/taken.txt", CancellationToken.None));
        Assert.False(server.Received("RNFR"));
        Assert.False(server.Received("RNTO"));

        await browser.RenameAsync(FileEntry("/home/fake/a.txt"), "/home/fake/b.txt", CancellationToken.None);
        Assert.Contains("RNFR /home/fake/a.txt", server.Commands);
        Assert.Contains("RNTO /home/fake/b.txt", server.Commands);
    }

    private static RemoteEntry FileEntry(string path) => new(RemotePath.Name(path), path, false, false, 1, default, "");

    [Fact]
    public async Task UntrustedCertificateDeclinedBeforeTheLogin()
    {
        using var certificate = SelfSigned();
        using var server = new FakeFtp(certificate);
        FtpCertificate? shown = null;

        var error = await Assert.ThrowsAsync<FtpRefusedException>(() =>
            FtpFileBrowser.ConnectAsync(Connection(server, FtpSecurity.Explicit, trust: c => { shown = c; return false; }), CancellationToken.None));

        Assert.Equal(CoreStrings.FtpCertificateDeclined, error.Message);
        Assert.NotNull(shown);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(certificate.RawData)), shown.Sha256);
        Assert.Contains("ftp.fake.test", shown.Subject);
        Assert.False(string.IsNullOrWhiteSpace(shown.Problem));
        Assert.False(server.Received("USER"));
        Assert.False(server.Received("PASS"));
    }

    [Fact]
    public async Task TrustedCertificateGivesAnEncryptedSession()
    {
        using var certificate = SelfSigned();
        using var server = new FakeFtp(certificate);
        int asked = 0;

        using var browser = await FtpFileBrowser.ConnectAsync(
            Connection(server, FtpSecurity.Opportunistic, trust: _ => { asked++; return true; }), CancellationToken.None);

        Assert.Equal(1, asked);
        Assert.True(browser.IsEncrypted);
        Assert.Equal(TransferProtocol.Ftps, browser.UploadProtocol);
        Assert.Equal("/home/fake", browser.HomeDirectory);
        // Les transferts aussi sont chiffrés.
        Assert.Contains("PROT P", server.Commands);
    }

    /// <summary>
    /// Serveur vu une fois avec TLS : mémorisé ; s'il se présente ensuite sans TLS (AUTH TLS effacé), ce n'est plus la
    /// simple question « en clair » mais l'alerte de retrait, et sans accord ni l'identifiant ni le mot de passe ne
    /// partent. Le retrait confirmé est oublié : la question habituelle revient.
    /// </summary>
    [Fact]
    public async Task ServerSeenWithTlsIsFlaggedWhenTlsDisappears()
    {
        using var certificate = SelfSigned();
        using var server = new FakeFtp(certificate);
        var settings = new AppSettings();
        int cleartextAsked = 0, removalAsked = 0;
        bool acceptRemoval = false;
        FtpConnection Make() => new()
        {
            Host = "127.0.0.1",
            Port = server.Port,
            UserName = "alice",
            Password = () => "secret-pw",
            Security = FtpSecurity.Opportunistic,
            TrustCertificate = _ => true,
            AllowCleartext = () =>
            {
                cleartextAsked++;
                return true;
            },
            TlsSeenBefore = () => FtpTlsMemory.Seen(settings, "127.0.0.1", server.Port),
            AllowTlsRemoved = () =>
            {
                removalAsked++;
                return acceptRemoval;
            },
        };

        using (var first = await FtpFileBrowser.ConnectAsync(Make(), CancellationToken.None))
        {
            Assert.True(first.IsEncrypted);
        }

        Assert.False(FtpTlsMemory.Seen(settings, "127.0.0.1", server.Port));
        Assert.True(FtpTlsMemory.Remember(settings, "127.0.0.1", server.Port));
        Assert.False(FtpTlsMemory.Remember(settings, "127.0.0.1", server.Port));
        Assert.True(FtpTlsMemory.Seen(settings, "127.0.0.1", server.Port));

        server.TlsRemoved = true;
        server.Commands.Clear();
        var error = await Assert.ThrowsAsync<FtpRefusedException>(() => FtpFileBrowser.ConnectAsync(Make(), CancellationToken.None));
        Assert.Equal(CoreStrings.FtpTlsRemoved, error.Message);
        Assert.Equal((0, 1), (cleartextAsked, removalAsked));
        Assert.False(server.Received("USER"));
        Assert.False(server.Received("PASS"));

        acceptRemoval = true;
        using (var accepted = await FtpFileBrowser.ConnectAsync(Make(), CancellationToken.None))
        {
            Assert.False(accepted.IsEncrypted);
        }

        Assert.Equal((0, 2), (cleartextAsked, removalAsked));
        FtpTlsMemory.Forget(settings, "127.0.0.1", server.Port);
        using var later = await FtpFileBrowser.ConnectAsync(Make(), CancellationToken.None);
        Assert.Equal((1, 2), (cleartextAsked, removalAsked));
    }

    /// <summary>Un certificat FTPS épinglé vaut mémoire de TLS (réglages d'avant cette mémoire) ; l'oubli le retire aussi.</summary>
    [Fact]
    public void PinnedCertificateCountsAsTlsSeen()
    {
        var settings = new AppSettings();
        KnownHosts.Remember(settings.KnownHosts, "ftps://FTP01.corp.local", 21, "X.509", "AB12");
        settings.KnownHosts["psmp.corp.local:22"] = "ssh-ed25519 SHA256:AAAA";

        Assert.True(FtpTlsMemory.Seen(settings, "ftp01.corp.local", 21));
        Assert.False(FtpTlsMemory.Seen(settings, "ftp01.corp.local", 2121));

        FtpTlsMemory.Forget(settings, "ftp01.corp.local", 21);
        Assert.False(FtpTlsMemory.Seen(settings, "ftp01.corp.local", 21));
        Assert.Equal(["psmp.corp.local:22"], settings.KnownHosts.Keys);
    }

    [Theory]
    [InlineData(640, 0x1A0)]
    [InlineData(755, 0x1ED)]
    [InlineData(644, 0x1A4)]
    [InlineData(1777, 0x3FF)]
    [InlineData(4755, 0x9ED)]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(658, 0)]
    public void ModeOfReadsTheOctalDigits(int chmod, int expected) =>
        Assert.Equal(expected, FtpFileBrowser.ModeOf(chmod));
}

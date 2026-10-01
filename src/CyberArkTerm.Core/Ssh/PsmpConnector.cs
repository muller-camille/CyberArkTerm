using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace CyberArkTerm.Core.Ssh;

/// <summary>Interactions avec l'utilisateur, appelées depuis les threads de SSH.NET (à marshaler vers l'UI).</summary>
public interface IPsmpInteraction
{
    /// <summary>Vérifie la clé d'hôte du PSMP ; renvoie vrai pour poursuivre la connexion.</summary>
    bool CheckHostKey(string host, int port, string algorithm, string sha256Fingerprint);

    /// <summary>Question posée par le PSMP (mot de passe, code MFA...) ; null si l'utilisateur annule.</summary>
    string? Prompt(string instruction, string prompt, bool echo);
}

/// <summary>
/// Ouvre les connexions SSH, SFTP et SCP vers une cible via le PSM for SSH
/// (identifiant <c>coffre@compte[#domaine]@cible</c>). Chaque connexion est une session PSMP distincte.
/// </summary>
public sealed class PsmpConnector
{
    private readonly IPsmpInteraction _ui;
    private readonly PrivateKeyFile? _key;
    private readonly Dictionary<string, string> _cachedAnswers = new(StringComparer.Ordinal);
    private readonly object _cacheLock = new();

    /// <param name="key">Clé SSH « MFA caching » fournie par le PVWA, si disponible.</param>
    public PsmpConnector(string host, int port, string login, IPsmpInteraction ui, PrivateKeyFile? key = null)
    {
        PsmpTarget.Validate(host, "l'adresse du PSMP");
        Host = host;
        Port = port;
        Login = login;
        _ui = ui;
        _key = key;
    }

    public string Host { get; }

    public int Port { get; }

    public string Login { get; }

    /// <summary>Bannière d'authentification envoyée par le PSMP (avertissement d'enregistrement...).</summary>
    public string? Banner { get; private set; }

    public Task<SshClient> ConnectShellAsync(CancellationToken ct) => ConnectAsync(info => new SshClient(info), ct);

    public Task<SftpClient> ConnectSftpAsync(CancellationToken ct) => ConnectAsync(info => new SftpClient(info), ct);

    /// <summary>
    /// SCP : les chemins sont passés à une commande « scp -t » exécutée par le shell de la cible ;
    /// ils sont donc protégés entre apostrophes (ShellQuote) pour empêcher toute injection de commande.
    /// </summary>
    public Task<ScpClient> ConnectScpAsync(CancellationToken ct) =>
        ConnectAsync(info => new ScpClient(info, RemotePathTransformation.ShellQuote), ct);

    private async Task<T> ConnectAsync<T>(Func<ConnectionInfo, T> create, CancellationToken ct)
        where T : BaseClient
    {
        // 1er essai : clé MFA + keyboard-interactive. Ensuite, selon les méthodes que le serveur
        // annonce dans son refus : keyboard-interactive à nouveau (mauvais mot de passe) ou « password ».
        bool usePassword = false;
        for (int attempt = 0; ; attempt++)
        {
            bool cancelled = false;
            var methods = new List<AuthenticationMethod>();
            if (_key is not null && attempt == 0)
            {
                methods.Add(new PrivateKeyAuthenticationMethod(Login, _key));
            }

            if (usePassword)
            {
                var password = _ui.Prompt($"Connexion à {Host}", $"Mot de passe pour {Login} :", echo: false)
                    ?? throw new OperationCanceledException("Authentification annulée.");
                methods.Add(new PasswordAuthenticationMethod(Login, password));
            }
            else
            {
                var interactive = new KeyboardInteractiveAuthenticationMethod(Login);
                interactive.AuthenticationPrompt += (_, e) =>
                {
                    foreach (var prompt in e.Prompts)
                    {
                        var answer = Answer(e.Instruction, prompt);
                        if (answer is null)
                        {
                            cancelled = true;
                            answer = "";
                        }

                        prompt.Response = answer;
                    }
                };
                methods.Add(interactive);
            }

            var info = new ConnectionInfo(Host, Port, Login, [.. methods])
            {
                Timeout = TimeSpan.FromSeconds(30),
                Encoding = Encoding.UTF8,
            };
            info.AuthenticationBanner += (_, e) => Banner = e.BannerMessage;
            if (!Curve25519Supported.Value)
            {
                // Système sans Curve25519 (anciens Windows, Wine) : on laisse le serveur choisir ECDH NIST ou DH.
                foreach (var name in info.KeyExchangeAlgorithms.Keys.Where(k => k.Contains("25519", StringComparison.Ordinal)).ToList())
                {
                    info.KeyExchangeAlgorithms.Remove(name);
                }
            }

            var client = create(info);
            client.HostKeyReceived += (_, e) =>
                e.CanTrust = _ui.CheckHostKey(Host, Port, e.HostKeyName, e.FingerPrintSHA256);
            try
            {
                await client.ConnectAsync(ct).ConfigureAwait(false);
                return client;
            }
            catch (SshAuthenticationException ex) when (!cancelled && attempt < 3)
            {
                client.Dispose();
                ClearCache();
                var allowed = ex.Message;
                usePassword = !allowed.Contains("keyboard-interactive", StringComparison.OrdinalIgnoreCase)
                              && allowed.Contains("password", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                client.Dispose();
                if (cancelled)
                {
                    throw new OperationCanceledException("Authentification annulée.");
                }

                throw;
            }
        }
    }

    /// <summary>
    /// Le mot de passe est réutilisé pour les connexions suivantes de la même session (SFTP, SCP)
    /// tant qu'il est accepté ; les codes à usage unique (MFA) sont toujours redemandés.
    /// </summary>
    private string? Answer(string instruction, AuthenticationPrompt prompt)
    {
        bool cacheable = !prompt.IsEchoed && IsPasswordPrompt(prompt.Request);
        lock (_cacheLock)
        {
            if (cacheable && _cachedAnswers.TryGetValue(prompt.Request, out var cached))
            {
                return cached;
            }
        }

        var answer = _ui.Prompt(instruction, prompt.Request, prompt.IsEchoed);
        if (answer is not null && cacheable)
        {
            lock (_cacheLock)
            {
                _cachedAnswers[prompt.Request] = answer;
            }
        }

        return answer;
    }

    private void ClearCache()
    {
        lock (_cacheLock)
        {
            _cachedAnswers.Clear();
        }
    }

    private static readonly Lazy<bool> Curve25519Supported = new(() =>
    {
        try
        {
            using var ecdh = System.Security.Cryptography.ECDiffieHellman.Create(
                System.Security.Cryptography.ECCurve.CreateFromFriendlyName("Curve25519"));
            ecdh.ExportParameters(includePrivateParameters: false);
            return true;
        }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or PlatformNotSupportedException or NotSupportedException)
        {
            return false;
        }
    });

    internal static bool IsPasswordPrompt(string request) =>
        request.Contains("password", StringComparison.OrdinalIgnoreCase)
        || request.Contains("mot de passe", StringComparison.OrdinalIgnoreCase);
}

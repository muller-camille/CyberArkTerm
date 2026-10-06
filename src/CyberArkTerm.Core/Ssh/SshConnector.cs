using System.Globalization;
using System.Text;
using CyberArkTerm.Core.Diagnostics;
using CyberArkTerm.Core.Localization;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace CyberArkTerm.Core.Ssh;

/// <summary>Interactions avec l'utilisateur, appelées depuis les threads de SSH.NET (à marshaler vers l'UI).</summary>
public interface ISshInteraction
{
    /// <summary>Vérifie la clé d'hôte du serveur ; renvoie vrai pour poursuivre la connexion.</summary>
    bool CheckHostKey(string host, int port, string algorithm, string sha256Fingerprint);

    /// <summary>Question posée par le serveur (mot de passe, code MFA...) ; null si l'utilisateur annule.</summary>
    string? Prompt(string instruction, string prompt, bool echo);
}

/// <summary>
/// Ouvre les connexions SSH, SFTP et SCP : vers une cible via le PSM for SSH (identifiant
/// <c>coffre@compte[#domaine]@cible</c>, chaque connexion est une session PSMP distincte), ou directement vers un
/// serveur (accès d'urgence avec un mot de passe venant d'un coffre KeePass).
/// </summary>
public sealed class SshConnector
{
    private readonly ISshInteraction _ui;
    private readonly PrivateKeyFile? _key;
    private readonly Func<string?>? _password;
    private readonly Dictionary<string, string> _cachedAnswers = new(StringComparer.Ordinal);
    private readonly object _cacheLock = new();

    /// <param name="key">Clé SSH « MFA caching » fournie par le PVWA, si disponible.</param>
    /// <param name="password">
    /// Mot de passe connu (coffre KeePass), lu au moment de chaque connexion ; s'il est refusé, il est demandé.
    /// </param>
    public SshConnector(string host, int port, string login, ISshInteraction ui, PrivateKeyFile? key = null,
        Func<string?>? password = null, string? addressWhat = null)
    {
        PsmpTarget.Validate(host, addressWhat ?? CoreStrings.PsmpAddressWhat);
        Host = host;
        Port = port;
        Login = login;
        _ui = ui;
        _key = key;
        _password = password;
    }

    public string Host { get; }

    public int Port { get; }

    public string Login { get; }

    /// <summary>Bannière d'authentification envoyée par le serveur (avertissement d'enregistrement...).</summary>
    public string? Banner { get; private set; }

    public Task<SshClient> ConnectShellAsync(CancellationToken ct) => ConnectAsync(info => new SshClient(info), ct);

    public Task<SftpClient> ConnectSftpAsync(CancellationToken ct) => ConnectAsync(info => new SftpClient(info), ct);

    /// <summary>
    /// SCP : les chemins sont passés à une commande « scp -t » exécutée par le shell de la cible ;
    /// ils sont donc protégés entre apostrophes (ShellQuote) pour empêcher toute injection de commande.
    /// </summary>
    public Task<ScpClient> ConnectScpAsync(CancellationToken ct) =>
        ConnectAsync(info => new ScpClient(info, RemotePathTransformation.ShellQuote), ct, "SCP");

    /// <param name="purpose">Nom de la connexion dans le journal (par défaut, son type).</param>
    private async Task<T> ConnectAsync<T>(Func<ConnectionInfo, T> create, CancellationToken ct, string? purpose = null)
        where T : BaseClient
    {
        purpose ??= typeof(T).Name;
        // 1er essai : clé MFA (ou mot de passe connu) + keyboard-interactive. Ensuite, selon les méthodes que le
        // serveur annonce dans son refus : keyboard-interactive à nouveau (mauvais mot de passe) ou « password ».
        bool usePassword = false;
        for (int attempt = 0; ; attempt++)
        {
            bool cancelled = false;
            var known = attempt == 0 ? _password?.Invoke() : null;
            var methods = new List<AuthenticationMethod>();
            if (_key is not null && attempt == 0)
            {
                methods.Add(new PrivateKeyAuthenticationMethod(Login, _key));
            }

            if (known is not null)
            {
                methods.Add(new PasswordAuthenticationMethod(Login, known));
            }

            if (usePassword)
            {
                var password = _ui.Prompt(
                        string.Format(CultureInfo.CurrentCulture, CoreStrings.PasswordPromptTitle, Host),
                        string.Format(CultureInfo.CurrentCulture, CoreStrings.PasswordPrompt, Login),
                        echo: false)
                    ?? throw new OperationCanceledException(CoreStrings.AuthenticationCancelled);
                methods.Add(new PasswordAuthenticationMethod(Login, password));
            }
            else
            {
                var interactive = new KeyboardInteractiveAuthenticationMethod(Login);
                interactive.AuthenticationPrompt += (_, e) =>
                {
                    foreach (var prompt in e.Prompts)
                    {
                        // La question seulement, jamais la réponse.
                        DebugLog.Write("ssh", $"{Host} : question du serveur « {prompt.Request.Trim()} » (saisie affichée {prompt.IsEchoed})");
                        var answer = known is not null && !prompt.IsEchoed && IsPasswordPrompt(prompt.Request)
                            ? known
                            : Answer(e.Instruction, prompt);
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
            {
                e.CanTrust = _ui.CheckHostKey(Host, Port, e.HostKeyName, e.FingerPrintSHA256);
                DebugLog.Write("ssh", $"{Host}:{Port} : clé d'hôte {e.HostKeyName} SHA256:{e.FingerPrintSHA256}, acceptée {e.CanTrust}");
            };
            DebugLog.Write("ssh", $"{Host}:{Port} : connexion {purpose}, essai {attempt + 1}, méthodes {string.Join(", ", methods.Select(m => m.Name))}");
            try
            {
                await client.ConnectAsync(ct).ConfigureAwait(false);
                var c = client.ConnectionInfo;
                DebugLog.Write("ssh", $"{Host}:{Port} : {purpose} connecté, serveur « {c.ServerVersion} », échange de clés {c.CurrentKeyExchangeAlgorithm}, "
                    + $"chiffrement {c.CurrentServerEncryption}/{c.CurrentClientEncryption}, compression {c.CurrentServerCompressionAlgorithm}");
                return client;
            }
            catch (SshAuthenticationException ex) when (!cancelled && attempt < 3)
            {
                DebugLog.Write("ssh", $"{Host}:{Port} : authentification refusée : {ex.Message}");
                client.Dispose();
                ClearCache();
                var allowed = ex.Message;
                usePassword = !allowed.Contains("keyboard-interactive", StringComparison.OrdinalIgnoreCase)
                              && allowed.Contains("password", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                DebugLog.Write("ssh", $"{Host}:{Port} : échec de la connexion {purpose}", ex);
                client.Dispose();
                if (cancelled)
                {
                    throw new OperationCanceledException(CoreStrings.AuthenticationCancelled);
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

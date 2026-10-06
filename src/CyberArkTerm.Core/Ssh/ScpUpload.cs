using System.Diagnostics;
using System.Globalization;
using System.Text;
using CyberArkTerm.Core.Diagnostics;
using CyberArkTerm.Core.Localization;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace CyberArkTerm.Core.Ssh;

/// <summary>Étape d'un envoi SCP.</summary>
internal enum ScpStep
{
    /// <summary>Commande « scp -t » envoyée, en attente de son accord.</summary>
    Command,

    /// <summary>Fichier annoncé (droits, taille, nom), en attente de l'accord.</summary>
    Header,

    /// <summary>Contenu envoyé, en attente de la confirmation finale.</summary>
    End,
}

/// <summary>
/// Le serveur a refusé une étape de l'envoi SCP : par un message de scp (<see cref="FromScp"/>), en fermant le canal ou
/// en ne répondant pas. <see cref="Exception.Message"/> est le texte à montrer, avec ce que le serveur a renvoyé.
/// </summary>
internal sealed class ScpStepException(ScpStep step, string message, bool fromScp) : SshException(message)
{
    public ScpStep Step { get; } = step;

    /// <summary>Refus expliqué par scp lui-même (droits, dossier absent…) : la connexion reste utilisable.</summary>
    public bool FromScp { get; } = fromScp;
}

/// <summary>
/// Envoi d'un fichier par SCP : la commande « scp -t » du serveur reçoit l'annonce du fichier (droits, taille, nom), puis
/// son contenu, et répond à chaque étape par un octet (0 : accepté ; 1 ou 2 suivi d'un message : refusé). Même commande
/// et même annonce que la bibliothèque SSH (« scp -t -d 'dossier' », « C0644 taille nom »), mais chaque étape est notée
/// dans le journal de débogage, avec ce que le serveur renvoie quand il ferme le canal (sortie d'erreur, code de sortie,
/// signal) : c'est là qu'un PSMP explique un refus.
/// </summary>
internal static class ScpUpload
{
    /// <summary>Attente maximale de la réponse à la commande scp (commande refusée sans que le canal soit fermé).</summary>
    internal static TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(60);

    private const int BufferSize = 32 * 1024;

    // Sortie d'erreur gardée au plus dans le journal, et dans le message affiché.
    private const int MaxLoggedError = 4000;
    private const int MaxShownError = 300;

    /// <summary>Envoie <paramref name="source"/> (<paramref name="length"/> octets) vers <paramref name="remotePath"/>.</summary>
    /// <param name="uploaded">Octets envoyés jusque-là.</param>
    /// <exception cref="ScpStepException">Étape refusée par le serveur.</exception>
    public static void Run(SshClient client, Stream source, long length, string remotePath, Action<long> uploaded)
    {
        int slash = remotePath.LastIndexOf('/');
        var directory = slash <= 0 ? "/" : remotePath[..slash];
        var name = remotePath[(slash + 1)..];
        var commandText = "scp -t -d " + RemotePathTransformation.ShellQuote.Transform(directory);
        var clock = Stopwatch.StartNew();
        void Log(string text) => DebugLog.Write("files", $"SCP {name} [{clock.ElapsedMilliseconds} ms] : {text}");

        using var command = client.CreateCommand(commandText);
        Log($"commande « {commandText} »");
        var run = command.ExecuteAsync();
        _ = run.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        var step = new Step(command, run, Log, length);

        using (var input = command.CreateInputStream())
        {
            step.Expect(ScpStep.Command);

            var header = string.Create(CultureInfo.InvariantCulture, $"C0644 {length} {name}\n");
            input.Write(Encoding.UTF8.GetBytes(header));
            Log($"annonce « {header.TrimEnd('\n')} » envoyée");
            step.Expect(ScpStep.Header);

            var buffer = new byte[BufferSize];
            long sent = 0;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (run.IsCompleted)
                {
                    // Canal fermé par le serveur : inutile de lire le reste du fichier, la réponse dira pourquoi.
                    Log($"canal fermé par le serveur après {sent} octets de contenu");
                    break;
                }

                input.Write(buffer, 0, read);
                sent += read;
                uploaded(sent);
            }

            if (sent == 0)
            {
                uploaded(0);
            }

            Log($"contenu envoyé : {sent} octets");
            input.WriteByte(0);
            step.Expect(ScpStep.End);
        }

        // Fin de l'entrée (EOF) envoyée ; le canal est fermé sans attendre le code de sortie.
        Log("envoi terminé");
    }

    /// <summary>Réponses du serveur aux étapes d'un envoi.</summary>
    private sealed class Step(SshCommand command, Task run, Action<string> log, long length)
    {
        /// <summary>Lit la réponse à une étape : 0 accepté, sinon <see cref="ScpStepException"/>.</summary>
        public void Expect(ScpStep step)
        {
            int code = step == ScpStep.Command ? ReadFirstByte() : command.OutputStream.ReadByte();
            if (code == 0)
            {
                log($"{Name(step)} acceptée");
                return;
            }

            if (code == -2)
            {
                log($"{Name(step)} : aucune réponse en {CommandTimeout.TotalSeconds:0} s");
                throw new ScpStepException(step, string.Format(CultureInfo.CurrentCulture, CoreStrings.ScpNoAnswer, CommandTimeout.TotalSeconds),
                    fromScp: false);
            }

            if (code is 1 or 2)
            {
                // Refus expliqué par scp (« scp: /dossier: Permission denied »…).
                var message = ReadLine(command.OutputStream);
                log($"{Name(step)} refusée par scp ({(code == 2 ? "erreur fatale" : "erreur")}) : « {message} » ; {Collect().ToLog()}");
                throw new ScpStepException(step, message, fromScp: true);
            }

            if (code == -1)
            {
                // Canal fermé par le serveur : sa sortie d'erreur, son code de sortie et son signal disent pourquoi.
                var closed = Collect();
                log($"{Name(step)} : canal fermé par le serveur sans réponse ; {closed.ToLog()}");
                var shown = closed.ToShown();
                throw new ScpStepException(step, step switch
                {
                    ScpStep.Command => string.Format(CultureInfo.CurrentCulture, CoreStrings.ScpRefusedCommand, shown),
                    ScpStep.Header => string.Format(CultureInfo.CurrentCulture, CoreStrings.ScpRefusedHeader, RemotePath.FormatSize(length), shown),
                    _ => string.Format(CultureInfo.CurrentCulture, CoreStrings.ScpClosedByServer, shown),
                }, fromScp: false);
            }

            var rest = ReadLine(command.OutputStream);
            log($"{Name(step)} : réponse inattendue 0x{code:x2} « {rest} » ; {Collect().ToLog()}");
            throw new ScpStepException(step, string.Format(CultureInfo.CurrentCulture, CoreStrings.ScpUnexpectedAnswer, $"0x{code:x2} {rest}"),
                fromScp: false);
        }

        /// <summary>Première réponse, attendue au plus <see cref="CommandTimeout"/> ; -2 si elle n'arrive pas.</summary>
        private int ReadFirstByte()
        {
            var read = Task.Run(command.OutputStream.ReadByte);
            if (read.Wait(CommandTimeout))
            {
                return read.Result;
            }

            // Le canal est fermé ensuite : la lecture en attente se termine alors.
            _ = read.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            return -2;
        }

        /// <summary>Ce que le serveur a renvoyé jusque-là : sortie d'erreur (lue sans attendre plus de 0,5 s), code, signal.</summary>
        private ServerEnd Collect()
        {
            var bytes = new List<byte>();
            var stream = command.ExtendedOutputStream;
            // Canal fermé : le flux est clos, la lecture s'arrête d'elle-même ; sinon, elle n'est pas attendue.
            var read = Task.Run(() =>
            {
                var buffer = new byte[4096];
                int n;
                while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    lock (bytes)
                    {
                        if (bytes.Count < MaxLoggedError * 4)
                        {
                            bytes.AddRange(buffer.AsSpan(0, n).ToArray());
                        }
                    }
                }
            });
            if (run.IsCompleted)
            {
                read.Wait(TimeSpan.FromMilliseconds(500));
            }
            else
            {
                read.Wait(TimeSpan.FromMilliseconds(100));
            }

            _ = read.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            string error;
            lock (bytes)
            {
                error = Encoding.UTF8.GetString([.. bytes]);
            }

            return new ServerEnd(error, command.ExitStatus, command.ExitSignal);
        }

        private static string Name(ScpStep step) => step switch
        {
            ScpStep.Command => "commande",
            ScpStep.Header => "annonce du fichier",
            _ => "fin de l'envoi",
        };
    }

    /// <summary>Ce que le serveur a renvoyé en fermant le canal.</summary>
    private sealed record ServerEnd(string Error, int? ExitStatus, string? ExitSignal)
    {
        public string ToLog()
        {
            var parts = new List<string>();
            var error = Clean(Error, MaxLoggedError);
            parts.Add(error.Length > 0 ? $"sortie d'erreur « {error} »" : "sortie d'erreur vide");
            parts.Add(ExitStatus is { } status ? $"code de sortie {status}" : "pas de code de sortie");
            if (ExitSignal is { Length: > 0 } signal)
            {
                parts.Add($"signal {signal}");
            }

            return string.Join(", ", parts);
        }

        public string ToShown()
        {
            var parts = new List<string>();
            var error = Clean(Error, MaxShownError);
            if (error.Length > 0)
            {
                parts.Add(string.Format(CultureInfo.CurrentCulture, CoreStrings.ScpServerSaid, error));
            }

            if (ExitStatus is { } status)
            {
                parts.Add(string.Format(CultureInfo.CurrentCulture, CoreStrings.ScpExitStatus, status));
            }

            if (ExitSignal is { Length: > 0 } signal)
            {
                parts.Add(string.Format(CultureInfo.CurrentCulture, CoreStrings.ScpExitSignal, signal));
            }

            return parts.Count > 0 ? string.Join(", ", parts) : CoreStrings.ScpNoExplanation;
        }
    }

    /// <summary>Ligne terminée par LF (message de scp), sans caractères de contrôle.</summary>
    private static string ReadLine(Stream stream)
    {
        var bytes = new List<byte>();
        int b;
        while ((b = stream.ReadByte()) is not (-1 or '\n') && bytes.Count < MaxLoggedError)
        {
            bytes.Add((byte)b);
        }

        return Clean(Encoding.UTF8.GetString([.. bytes]), MaxLoggedError);
    }

    /// <summary>Texte sur une ligne, sans caractères de contrôle, raccourci à <paramref name="max"/> caractères.</summary>
    internal static string Clean(string text, int max)
    {
        var builder = new StringBuilder();
        foreach (var c in text.Trim())
        {
            if (builder.Length >= max)
            {
                builder.Append('…');
                break;
            }

            builder.Append(c switch
            {
                '\n' => " | ",
                '\r' or '\t' => " ",
                _ when char.IsControl(c) => "?",
                _ => c.ToString(),
            });
        }

        return builder.ToString();
    }
}

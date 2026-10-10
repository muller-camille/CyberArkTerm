namespace ZillaTerm.Core.Ssh;

/// <summary>
/// Mots de passe donnés au PSMP (ou au serveur), gardés en mémoire pour ne pas les redemander : ceux d'une session pour
/// ses connexions suivantes (SFTP, SCP du même onglet), ou ceux d'un groupe de sessions ouvertes ensemble quand
/// l'utilisateur a choisi de les réutiliser. Les codes à usage unique ne sont jamais gardés. Toutes les réponses sont
/// oubliées au verrouillage de Windows et à la déconnexion (<see cref="ForgetAll"/>).
/// </summary>
public sealed class SshAnswerCache
{
    private static readonly List<WeakReference<SshAnswerCache>> Instances = [];

    private readonly Dictionary<string, string> _answers = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private int _opening;

    public SshAnswerCache()
    {
        lock (Instances)
        {
            Instances.RemoveAll(w => !w.TryGetTarget(out _));
            Instances.Add(new WeakReference<SshAnswerCache>(this));
        }
    }

    /// <summary>
    /// Groupe : une question à la fois, pour que la réponse donnée à la première session serve aux suivantes au lieu de
    /// N fenêtres ouvertes en même temps.
    /// </summary>
    internal SemaphoreSlim Gate { get; } = new(1, 1);

    public bool TryGet(string prompt, out string answer)
    {
        lock (_lock)
        {
            return _answers.TryGetValue(prompt, out answer!);
        }
    }

    public void Set(string prompt, string answer)
    {
        lock (_lock)
        {
            _answers[prompt] = answer;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _answers.Clear();
        }
    }

    /// <summary>Une session du groupe commence à s'ouvrir.</summary>
    internal void Join() => Interlocked.Increment(ref _opening);

    /// <summary>Une session du groupe a fini de s'authentifier (ou a échoué) : la dernière fait oublier les réponses.</summary>
    internal void Leave()
    {
        if (Interlocked.Decrement(ref _opening) <= 0)
        {
            Clear();
        }
    }

    /// <summary>Oublie les réponses de toutes les sessions (Windows verrouillé, déconnexion de CyberArk).</summary>
    public static void ForgetAll()
    {
        lock (Instances)
        {
            foreach (var reference in Instances)
            {
                if (reference.TryGetTarget(out var cache))
                {
                    cache.Clear();
                }
            }
        }
    }
}

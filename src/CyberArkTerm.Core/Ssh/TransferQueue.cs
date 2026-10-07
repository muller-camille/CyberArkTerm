using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CyberArkTerm.Core.Ssh;

public enum TransferState
{
    Pending,
    Running,
    Done,
    Failed,
    Cancelled,
}

/// <summary>
/// Élément de la file d'attente des transferts : un envoi (fichiers et dossiers déposés ensemble, vers un dossier
/// du serveur fixé au moment du dépôt) ou un téléchargement (vers un emplacement choisi au moment de la demande).
/// </summary>
public sealed class TransferItem(bool upload, string label, string destination, Func<TransferItem, CancellationToken, Task> run)
    : INotifyPropertyChanged
{
    private TransferState _state;
    private double _percent;
    private string? _currentFile;
    private bool _verifying;
    private bool _packing;
    private string? _error;
    private int _fileCount;
    private TransferProtocol? _currentProtocol;
    private object? _owner;
    private Func<TransferItem, CancellationToken, Task>? _run = run;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Upload { get; } = upload;

    /// <summary>Ce qui est transféré : nom du fichier ou du dossier, ou nombre d'éléments.</summary>
    public string Label { get; } = label;

    /// <summary>Dossier du serveur (envoi) ou emplacement sur ce poste (téléchargement).</summary>
    public string Destination { get; } = destination;

    /// <summary>
    /// Session à laquelle appartient le transfert (annulé à sa fermeture). Oubliée à la fin de la série : un élément
    /// gardé dans la file pour son résultat ne retient pas en mémoire une session fermée.
    /// </summary>
    public object? Owner
    {
        get => _owner;
        init => _owner = value;
    }

    /// <summary>Serveur de la session (« utilisateur@serveur ») : nommé dans la file, qui peut mêler plusieurs serveurs.</summary>
    public string? Server { get; init; }

    /// <summary>Où va le transfert : « serveur:dossier » pour un envoi, le dossier de ce poste pour un téléchargement.</summary>
    public string Target => Upload && !string.IsNullOrEmpty(Server) ? $"{Server}:{Destination}" : Destination;

    /// <summary>Trajet complet (infobulle de la file) : d'où vient l'élément et où il va.</summary>
    public string Route => Upload || string.IsNullOrEmpty(Server) ? $"{Label} → {Target}" : $"{Server}:{Label} → {Destination}";

    /// <summary>Protocole d'un envoi (SCP ou SFTP), celui des Paramètres.</summary>
    public string? Protocol { get; init; }

    /// <summary>Protocole de l'envoi du fichier en cours : l'autre que <see cref="Protocol"/> si le serveur l'a refusé.</summary>
    public TransferProtocol? CurrentProtocol
    {
        get => _currentProtocol;
        private set => Set(ref _currentProtocol, value);
    }

    /// <summary>
    /// Protocole pour le bilan et l'historique : <see cref="Protocol"/>, ou ceux réellement utilisés quand le serveur en
    /// a refusé un (« SCP (SFTP refusé) »).
    /// </summary>
    public string? ProtocolUsed => TransferProtocols.Describe(Protocol, Checks);

    /// <summary>Noms déposés, pour signaler un écrasement par un autre envoi en attente vers le même dossier.</summary>
    public IReadOnlyList<string> Names { get; init; } = [];

    /// <summary>Nombre de fichiers à transférer (0 tant qu'il n'est pas connu).</summary>
    public int FileCount
    {
        get => _fileCount;
        set => Set(ref _fileCount, value);
    }

    /// <summary>Vérification de chaque fichier traité, y compris en échec ou interrompu.</summary>
    public List<TransferCheck> Checks { get; } = [];

    public TransferState State
    {
        get => _state;
        internal set
        {
            Set(ref _state, value);
            OnPropertyChanged(nameof(HasChecks));
        }
    }

    /// <summary>Au moins un fichier traité, avec sa vérification (détail affichable).</summary>
    public bool HasChecks => Checks.Count > 0;

    /// <summary>Avancement du fichier en cours (transfert ou vérification), en pourcentage.</summary>
    public double Percent
    {
        get => _percent;
        private set => Set(ref _percent, value);
    }

    public string? CurrentFile
    {
        get => _currentFile;
        private set => Set(ref _currentFile, value);
    }

    /// <summary>Relecture du fichier en cours pour la vérification SHA-256.</summary>
    public bool Verifying
    {
        get => _verifying;
        private set => Set(ref _verifying, value);
    }

    /// <summary>Création de l'archive .tar.gz de l'envoi.</summary>
    public bool Packing
    {
        get => _packing;
        private set => Set(ref _packing, value);
    }

    /// <summary>Envoi en archive .tar.gz : commande à lancer sur le serveur pour l'extraire.</summary>
    public string? ExtractCommand { get; set; }

    public string? Error
    {
        get => _error;
        internal set => Set(ref _error, value);
    }

    /// <summary>Annulation demandée, l'élément s'arrête.</summary>
    public bool CancelRequested => Cancellation.IsCancellationRequested;

    internal CancellationTokenSource Cancellation { get; } = new();

    internal Func<TransferItem, CancellationToken, Task>? Run => _run;

    /// <summary>Élément terminé, gardé pour son résultat : plus de session ni de connexion retenues.</summary>
    internal void Release()
    {
        _owner = null;
        _run = null;
    }

    public bool IsFinished => State is TransferState.Done or TransferState.Failed or TransferState.Cancelled;

    /// <summary>Met à jour l'avancement (à appeler depuis le fil de l'interface, par un <see cref="Progress{T}"/>).</summary>
    public void Report(TransferProgress progress)
    {
        CurrentFile = progress.FileName;
        if (progress.Protocol is { } protocol)
        {
            CurrentProtocol = protocol;
        }

        Verifying = progress.Verifying;
        Packing = progress.Packing;
        Percent = progress.Total > 0 ? Math.Min(100, 100.0 * progress.Transferred / progress.Total) : 0;
    }

    internal void RequestCancel()
    {
        Cancellation.Cancel();
        OnPropertyChanged(nameof(CancelRequested));
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            OnPropertyChanged(name);
        }
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// File d'attente des transferts : un élément à la fois, dans l'ordre des demandes. Un élément en attente peut être
/// retiré, l'élément en cours annulé ; une erreur n'arrête pas la file. La fin d'une série est signalée par
/// <see cref="Drained"/> ; ses éléments restent ensuite dans <see cref="Items"/> avec leur résultat (les
/// <see cref="KeptFinished"/> derniers), jusqu'à <see cref="ClearFinished"/>. À utiliser depuis le fil de l'interface.
/// </summary>
/// <param name="describe">Message lisible d'une erreur de transfert.</param>
public sealed class TransferQueue(Func<Exception, string> describe)
{
    /// <summary>Éléments terminés gardés pour leur résultat ; au-delà, les plus anciens sont retirés.</summary>
    public const int KeptFinished = 50;

    private readonly List<TransferItem> _series = [];
    private bool _active;
    private Task _runner = Task.CompletedTask;

    /// <summary>Éléments en attente, en cours, et terminés (avec leur résultat) de cette série et des précédentes.</summary>
    public ObservableCollection<TransferItem> Items { get; } = [];

    public TransferItem? Current { get; private set; }

    public int PendingCount => Items.Count(i => i.State == TransferState.Pending);

    /// <summary>Éléments en cours ou en attente.</summary>
    public int ActiveCount => Items.Count(i => i.State is TransferState.Pending or TransferState.Running);

    /// <summary>Un élément s'est terminé (réussi, en échec ou annulé).</summary>
    public event Action<TransferItem>? ItemFinished;

    /// <summary>Plus rien en attente : bilan de la série (ses éléments restent dans <see cref="Items"/>).</summary>
    public event Action<IReadOnlyList<TransferItem>>? Drained;

    /// <summary>La file a changé (ajout, début, fin, annulation).</summary>
    public event Action? Changed;

    public void Enqueue(TransferItem item)
    {
        Items.Add(item);
        _series.Add(item);
        Trim();
        Changed?.Invoke();
        if (!_active)
        {
            _active = true;
            _runner = RunAsync();
        }
    }

    /// <summary>Retire un élément en attente, ou arrête l'élément en cours.</summary>
    public void Cancel(TransferItem item)
    {
        switch (item.State)
        {
            case TransferState.Pending:
                item.State = TransferState.Cancelled;
                ItemFinished?.Invoke(item);
                Changed?.Invoke();
                break;
            case TransferState.Running when !item.CancelRequested:
                item.RequestCancel();
                Changed?.Invoke();
                break;
        }
    }

    public void CancelAll() => CancelWhere(_ => true);

    /// <summary>Annule les éléments en attente ou en cours qui répondent au critère (ceux d'une session fermée...).</summary>
    public void CancelWhere(Func<TransferItem, bool> predicate)
    {
        // Les éléments en attente d'abord : l'arrêt de l'élément en cours ne doit pas laisser la file en démarrer un.
        foreach (var item in Items.Where(i => i.State == TransferState.Pending && predicate(i)).ToList())
        {
            Cancel(item);
        }

        foreach (var item in Items.Where(i => i.State == TransferState.Running && predicate(i)).ToList())
        {
            Cancel(item);
        }
    }

    /// <summary>Retire de la liste les éléments terminés (réussis, en échec ou annulés).</summary>
    public void ClearFinished()
    {
        foreach (var item in Items.Where(i => i.IsFinished).ToList())
        {
            Items.Remove(item);
        }

        Changed?.Invoke();
    }

    /// <summary>Garde au plus <see cref="KeptFinished"/> éléments terminés (le bilan d'une série ne dépend pas de la liste).</summary>
    private void Trim()
    {
        var old = Items.Where(i => i.IsFinished).ToList();
        foreach (var item in old.Take(Math.Max(0, old.Count - KeptFinished)))
        {
            Items.Remove(item);
        }
    }

    /// <summary>Attend la fin de la série, au plus <paramref name="timeout"/>.</summary>
    public Task WaitIdleAsync(TimeSpan timeout) => Task.WhenAny(_runner, Task.Delay(timeout));

    /// <summary>
    /// Attend que l'élément en cours, s'il répond au critère, se soit arrêté (après une annulation, pour que le fichier
    /// interrompu soit supprimé avant la fermeture de la connexion), au plus <paramref name="timeout"/>.
    /// </summary>
    public Task WhenStoppedAsync(Func<TransferItem, bool> predicate, TimeSpan timeout)
    {
        if (Current is not { } current || !predicate(current))
        {
            return Task.CompletedTask;
        }

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFinished(TransferItem item)
        {
            if (ReferenceEquals(item, current))
            {
                ItemFinished -= OnFinished;
                stopped.TrySetResult();
            }
        }

        ItemFinished += OnFinished;
        return Task.WhenAny(stopped.Task, Task.Delay(timeout));
    }

    private async Task RunAsync()
    {
        while (Items.FirstOrDefault(i => i.State == TransferState.Pending) is { } item)
        {
            Current = item;
            item.State = TransferState.Running;
            Changed?.Invoke();
            try
            {
                await item.Run!(item, item.Cancellation.Token);
                item.State = TransferState.Done;
            }
            catch (OperationCanceledException) when (item.CancelRequested)
            {
                item.State = TransferState.Cancelled;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                item.Error = describe(e);
                item.State = TransferState.Failed;
            }

            Current = null;
            ItemFinished?.Invoke(item);
            Changed?.Invoke();
        }

        var run = _series.ToList();
        _series.Clear();
        _active = false;
        Drained?.Invoke(run);
        foreach (var item in run)
        {
            item.Release();
        }

        Trim();
        Changed?.Invoke();
    }
}

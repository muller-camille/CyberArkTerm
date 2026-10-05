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
    private string? _error;
    private int _fileCount;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool Upload { get; } = upload;

    /// <summary>Ce qui est transféré : nom du fichier ou du dossier, ou nombre d'éléments.</summary>
    public string Label { get; } = label;

    /// <summary>Dossier du serveur (envoi) ou emplacement sur ce poste (téléchargement).</summary>
    public string Destination { get; } = destination;

    /// <summary>Session à laquelle appartient le transfert (annulé à sa fermeture).</summary>
    public object? Owner { get; init; }

    /// <summary>Protocole d'un envoi (SCP ou SFTP).</summary>
    public string? Protocol { get; init; }

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
        internal set => Set(ref _state, value);
    }

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

    public string? Error
    {
        get => _error;
        internal set => Set(ref _error, value);
    }

    /// <summary>Annulation demandée, l'élément s'arrête.</summary>
    public bool CancelRequested => Cancellation.IsCancellationRequested;

    internal CancellationTokenSource Cancellation { get; } = new();

    internal Func<TransferItem, CancellationToken, Task> Run { get; } = run;

    /// <summary>Met à jour l'avancement (à appeler depuis le fil de l'interface, par un <see cref="Progress{T}"/>).</summary>
    public void Report(TransferProgress progress)
    {
        CurrentFile = progress.FileName;
        Verifying = progress.Verifying;
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
/// retiré, l'élément en cours annulé ; une erreur n'arrête pas la file. Les éléments restent dans
/// <see cref="Items"/> (avec leur résultat) jusqu'à la fin de la série, signalée par <see cref="Drained"/>.
/// À utiliser depuis le fil de l'interface.
/// </summary>
/// <param name="describe">Message lisible d'une erreur de transfert.</param>
public sealed class TransferQueue(Func<Exception, string> describe)
{
    private bool _active;
    private Task _runner = Task.CompletedTask;

    /// <summary>Éléments de la série en cours : terminés, en cours et en attente.</summary>
    public ObservableCollection<TransferItem> Items { get; } = [];

    public TransferItem? Current { get; private set; }

    public int PendingCount => Items.Count(i => i.State == TransferState.Pending);

    /// <summary>Éléments en cours ou en attente.</summary>
    public int ActiveCount => Items.Count(i => i.State is TransferState.Pending or TransferState.Running);

    /// <summary>Un élément s'est terminé (réussi, en échec ou annulé).</summary>
    public event Action<TransferItem>? ItemFinished;

    /// <summary>Plus rien en attente : bilan de la série (les éléments sont retirés de <see cref="Items"/>).</summary>
    public event Action<IReadOnlyList<TransferItem>>? Drained;

    /// <summary>La file a changé (ajout, début, fin, annulation).</summary>
    public event Action? Changed;

    public void Enqueue(TransferItem item)
    {
        Items.Add(item);
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
                await item.Run(item, item.Cancellation.Token);
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

        var run = Items.ToList();
        Items.Clear();
        _active = false;
        Drained?.Invoke(run);
        Changed?.Invoke();
    }
}

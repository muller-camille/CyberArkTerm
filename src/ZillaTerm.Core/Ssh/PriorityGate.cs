namespace ZillaTerm.Core.Ssh;

/// <summary>
/// Verrou asynchrone à deux niveaux pour la connexion SFTP : une opération à la fois, et les demandes interactives
/// (navigation, suppression, droits, éditeur, glisser vers l'Explorateur) passent avant les transferts de la file
/// d'attente qui attendent. L'opération en cours n'est jamais interrompue : une demande interactive passe entre deux
/// fichiers d'un transfert.
/// </summary>
internal sealed class PriorityGate
{
    private readonly object _lock = new();
    private readonly LinkedList<TaskCompletionSource> _interactive = new();
    private readonly LinkedList<TaskCompletionSource> _background = new();
    private bool _held;

    /// <summary>Attend son tour ; le résultat libère le verrou quand il est disposé.</summary>
    /// <param name="background">Transfert de la file d'attente : passe après toutes les demandes interactives.</param>
    public async Task<IDisposable> EnterAsync(bool background, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LinkedListNode<TaskCompletionSource> node;
        lock (_lock)
        {
            if (!_held)
            {
                _held = true;
                return new Releaser(this);
            }

            node = (background ? _background : _interactive).AddLast(new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        await using (ct.Register(() => Abandon(node)))
        {
            // Annulé pendant l'attente : OperationCanceledException, le verrou n'a pas été donné.
            await node.Value.Task.ConfigureAwait(false);
        }

        return new Releaser(this);
    }

    private void Abandon(LinkedListNode<TaskCompletionSource> node)
    {
        lock (_lock)
        {
            // Déjà retiré : le verrou vient d'être donné à ce demandeur, il le rendra lui-même.
            if (node.List is null)
            {
                return;
            }

            node.List.Remove(node);
        }

        node.Value.TrySetCanceled();
    }

    private void Release()
    {
        TaskCompletionSource next;
        lock (_lock)
        {
            var queue = _interactive.Count > 0 ? _interactive : _background.Count > 0 ? _background : null;
            if (queue is null)
            {
                _held = false;
                return;
            }

            next = queue.First!.Value;
            queue.RemoveFirst();
        }

        next.TrySetResult();
    }

    private sealed class Releaser(PriorityGate gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}

using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.Core.Tests.Ssh;

public sealed class TransferQueueTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    /// <summary>Une demande interactive passe avant les transferts en attente, jamais pendant l'opération en cours.</summary>
    [Fact]
    public async Task InteractiveRequestsGoBeforeWaitingTransfers()
    {
        var gate = new PriorityGate();
        var order = new List<string>();
        var first = await gate.EnterAsync(background: true, default);

        var background = Take("transfert", background: true);
        var interactive = Take("navigation", background: false);
        Assert.False(interactive.IsCompleted);

        first.Dispose();
        await Task.WhenAll(background, interactive).WaitAsync(Wait);
        Assert.Equal(["navigation", "transfert"], order);

        async Task Take(string name, bool background)
        {
            using (await gate.EnterAsync(background, default))
            {
                lock (order)
                {
                    order.Add(name);
                }

                await Task.Delay(20);
            }
        }
    }

    [Fact]
    public async Task CancelledWaiterDoesNotGetTheGate()
    {
        var gate = new PriorityGate();
        var held = await gate.EnterAsync(background: false, default);
        using var cts = new CancellationTokenSource();
        var waiting = gate.EnterAsync(background: true, cts.Token);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        held.Dispose();

        // Le verrou est libre : rien ne le garde pour le demandeur abandonné.
        using var next = await gate.EnterAsync(background: true, default).WaitAsync(Wait);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.EnterAsync(false, new CancellationToken(true)));
    }

    /// <summary>
    /// Un élément à la fois, dans l'ordre ; un élément en attente retiré n'est jamais lancé ; une erreur n'arrête pas la
    /// file ; le bilan de la série contient chaque élément avec son état.
    /// </summary>
    [Fact]
    public async Task RunsOneAtATimeAndKeepsGoingAfterAnError()
    {
        var queue = new TransferQueue(e => "échec : " + e.Message);
        var started = new List<string>();
        var release = new TaskCompletionSource();
        IReadOnlyList<TransferItem>? run = null;
        var drained = new TaskCompletionSource();
        queue.Drained += r =>
        {
            run = r;
            drained.SetResult();
        };

        TransferItem Item(string name, Func<Task> work) => new(true, name, "/srv", async (_, _) =>
        {
            started.Add(name);
            await work();
        });

        var a = Item("a", () => release.Task);
        var b = Item("b", () => throw new IOException("disque plein"));
        var c = Item("c", () => Task.CompletedTask);
        var d = Item("d", () => Task.CompletedTask);
        queue.Enqueue(a);
        queue.Enqueue(b);
        queue.Enqueue(c);
        queue.Enqueue(d);

        Assert.Equal((TransferState.Running, 3, 4), (a.State, queue.PendingCount, queue.ActiveCount));
        queue.Cancel(c);
        Assert.Equal(TransferState.Cancelled, c.State);
        release.SetResult();
        await drained.Task.WaitAsync(Wait);

        Assert.Equal(["a", "b", "d"], started);
        Assert.Equal([TransferState.Done, TransferState.Failed, TransferState.Cancelled, TransferState.Done], run!.Select(i => i.State));
        Assert.Equal("échec : disque plein", b.Error);
        // Les résultats restent affichés jusqu'à ce qu'on les efface.
        Assert.Equal([a, b, c, d], queue.Items);
        queue.ClearFinished();
        Assert.Empty(queue.Items);
    }

    /// <summary>
    /// Annuler l'élément en cours arrête son travail ; annuler ceux d'une session ne touche pas les autres ; une nouvelle
    /// demande après la fin d'une série relance la file.
    /// </summary>
    [Fact]
    public async Task CancelsTheRunningItemAndOnlyTheChosenOnes()
    {
        var queue = new TransferQueue(e => e.Message);
        var sessionA = new object();
        var sessionB = new object();
        var drained = new List<IReadOnlyList<TransferItem>>();
        queue.Drained += drained.Add;

        TransferItem Endless(object owner) => new(false, "x", @"C:\Temp", (_, ct) => Task.Delay(Timeout.Infinite, ct)) { Owner = owner };
        var running = Endless(sessionA);
        var otherSession = new TransferItem(false, "y", @"C:\Temp", (_, _) => Task.CompletedTask) { Owner = sessionB };
        var sameSession = Endless(sessionA);
        queue.Enqueue(running);
        queue.Enqueue(otherSession);
        queue.Enqueue(sameSession);

        queue.CancelWhere(i => i.Owner == sessionA);
        await queue.WaitIdleAsync(Wait);

        Assert.Equal((TransferState.Cancelled, TransferState.Done, TransferState.Cancelled), (running.State, otherSession.State, sameSession.State));
        Assert.True(running.CancelRequested);
        Assert.Single(drained);

        var later = new TransferItem(true, "z", "/srv", (_, _) => Task.CompletedTask);
        queue.Enqueue(later);
        await queue.WaitIdleAsync(Wait);
        Assert.Equal(TransferState.Done, later.State);
        Assert.Equal(2, drained.Count);
        // Le bilan de la deuxième série ne reprend pas la première, dont les éléments ont oublié leur session.
        Assert.Equal([later], drained[1]);
        Assert.Null(running.Owner);
        Assert.Equal(4, queue.Items.Count);
    }

    /// <summary>Les éléments terminés gardés sont limités : les plus anciens partent les premiers.</summary>
    [Fact]
    public async Task KeepsOnlyTheLatestFinishedItems()
    {
        var queue = new TransferQueue(e => e.Message);
        var items = Enumerable.Range(0, TransferQueue.KeptFinished + 5)
            .Select(i => new TransferItem(true, $"f{i}", "/srv", (_, _) => Task.CompletedTask))
            .ToList();
        foreach (var item in items)
        {
            queue.Enqueue(item);
            await queue.WaitIdleAsync(Wait);
        }

        Assert.Equal(TransferQueue.KeptFinished, queue.Items.Count);
        Assert.Equal(items[^1], queue.Items[^1]);
        Assert.DoesNotContain(items[0], queue.Items);
    }

    [Fact]
    public void ReportsProgressOfTheCurrentFile()
    {
        var item = new TransferItem(true, "deploy", "/opt/app", (_, _) => Task.CompletedTask);
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.Report(new TransferProgress("a.bin", 50, 200));
        item.Report(new TransferProgress("a.bin", 300, 200, Verifying: true));

        Assert.Equal(("a.bin", 100.0, true), (item.CurrentFile, item.Percent, item.Verifying));
        Assert.Contains(nameof(TransferItem.Percent), changed);
    }

    /// <summary>Le protocole du fichier en cours suit l'envoi ; la vérification (sans protocole) ne l'efface pas.</summary>
    [Fact]
    public void FollowsTheProtocolOfTheCurrentUpload()
    {
        var item = new TransferItem(true, "deploy", "/opt/app", (_, _) => Task.CompletedTask) { Protocol = "SFTP" };
        Assert.Null(item.CurrentProtocol);

        item.Report(new TransferProgress("a.bin", 0, 200, Protocol: TransferProtocol.Sftp));
        item.Report(new TransferProgress("a.bin", 0, 200, Protocol: TransferProtocol.Scp));
        item.Report(new TransferProgress("a.bin", 10, 200, Verifying: true));

        Assert.Equal(TransferProtocol.Scp, item.CurrentProtocol);
    }

    /// <summary>
    /// Bilan et historique : le protocole des Paramètres tant que le serveur n'en refuse aucun, sinon ceux réellement
    /// utilisés et celui qui a été refusé.
    /// </summary>
    [Fact]
    public void DescribesTheProtocolsReallyUsed()
    {
        using var _ = UiCulture.Use("fr-FR");
        TransferCheck Sent(string name, TransferProtocol used, TransferProtocol? refused = null) =>
            new(name, name, "/opt/" + name, 1, [1], 1, [1]) { Upload = true, Protocol = used, Refused = refused };

        var item = new TransferItem(true, "deploy", "/opt", (_, _) => Task.CompletedTask) { Protocol = "SFTP" };
        Assert.Equal("SFTP", item.ProtocolUsed);
        item.Checks.Add(Sent("a", TransferProtocol.Sftp));
        Assert.Equal("SFTP", item.ProtocolUsed);

        item.Checks.Add(Sent("b", TransferProtocol.Scp, refused: TransferProtocol.Sftp));
        Assert.Equal("SFTP + SCP (SFTP refusé)", item.ProtocolUsed);

        Assert.Equal("SCP (SFTP refusé)", TransferProtocols.Describe("SFTP", [Sent("c", TransferProtocol.Scp, TransferProtocol.Sftp)]));
        using (UiCulture.Use("en-US"))
        {
            Assert.Equal("SFTP (SCP refused)", TransferProtocols.Describe("SCP", [Sent("d", TransferProtocol.Sftp, TransferProtocol.Scp)]));
        }
    }
}

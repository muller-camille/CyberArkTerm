using System.Windows.Forms;
using CyberArkTerm.Core.Diagnostics;

namespace CyberArkTerm.App.Services.Rdp;

/// <summary>
/// Thread d'interface dédié à une connexion Bureau à distance : le contrôle ActiveX, sa fenêtre et ses événements y
/// vivent, avec leur propre boucle de messages. Un contrôle qui bloque (connexion ou libération qui tarde, dialogue,
/// serveur qui ne répond plus) ne fige que ce thread, pas l'interface de l'application.
/// Le thread de l'interface ne l'attend jamais de façon bloquante : il lui confie des actions et en reçoit le résultat
/// de façon asynchrone.
/// </summary>
internal sealed class RdpThread
{
    private readonly Thread _thread;
    private readonly WindowsFormsSynchronizationContext _context;

    private RdpThread(Thread thread, WindowsFormsSynchronizationContext context)
    {
        _thread = thread;
        _context = context;
    }

    public int ManagedThreadId => _thread.ManagedThreadId;

    /// <summary>Démarre le thread (STA, boucle de messages WinForms) et attend qu'il soit prêt à recevoir des actions.</summary>
    public static RdpThread Start(string name)
    {
        WindowsFormsSynchronizationContext? context = null;
        Exception? failure = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                // Rien ici ne touche aux fenêtres du thread de l'interface : celui-ci peut attendre sans risque.
                context = new WindowsFormsSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(context);
                // Exception dans une fenêtre de ce thread : notée, sans le dialogue d'erreur de WinForms.
                Application.ThreadException += (_, e) => DebugLog.Write("rdp", "Exception sur le thread Bureau à distance", e.Exception);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                failure = e;
            }
            finally
            {
                ready.Set();
            }

            if (failure is null)
            {
                Application.Run();
                DebugLog.Write("rdp", $"Thread Bureau à distance {Environment.CurrentManagedThreadId} terminé");
            }
        })
        {
            IsBackground = true,
            Name = name,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        if (failure is not null)
        {
            throw failure;
        }

        return new RdpThread(thread, context!);
    }

    /// <summary>
    /// Exécute <paramref name="action"/> sur ce thread, sans attendre ; une exception est notée et ignorée.
    /// Faux si le thread est déjà terminé (l'action n'est pas exécutée).
    /// </summary>
    public bool Post(Action action) => TryPost(_ => Run(action));

    /// <summary>Exécute <paramref name="action"/> sur ce thread ; la tâche se termine avec elle (ou avec son exception).</summary>
    public Task InvokeAsync(Action action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryPost(_ =>
            {
                try
                {
                    action();
                    done.TrySetResult();
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    done.TrySetException(e);
                }
            }))
        {
            done.TrySetException(new ObjectDisposedException(nameof(RdpThread)));
        }

        return done.Task;
    }

    /// <summary>Exécute <paramref name="func"/> sur ce thread et en renvoie le résultat.</summary>
    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryPost(_ =>
            {
                try
                {
                    done.TrySetResult(func());
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    done.TrySetException(e);
                }
            }))
        {
            done.TrySetException(new ObjectDisposedException(nameof(RdpThread)));
        }

        return done.Task;
    }

    /// <summary>Termine la boucle de messages une fois les actions déjà confiées exécutées ; le thread s'arrête.</summary>
    public void Exit() => Post(Application.ExitThread);

    /// <summary>Confie <paramref name="callback"/> à la boucle de messages ; faux si le thread est déjà terminé.</summary>
    private bool TryPost(SendOrPostCallback callback)
    {
        if (!_thread.IsAlive)
        {
            return false;
        }

        try
        {
            _context.Post(callback, null);
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }
    }

    private static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            DebugLog.Write("rdp", "Erreur sur le thread Bureau à distance", e);
        }
    }
}

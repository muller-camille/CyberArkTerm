using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace CyberArkTerm.App.Services;

/// <summary>
/// Presse-papiers pour un mot de passe : copié directement depuis un tableau de caractères (sans chaîne .NET), marqué
/// pour être exclu de l'historique (Win+V), de la synchronisation entre appareils et des outils de surveillance du
/// presse-papiers, puis effacé après un délai s'il contient toujours ce mot de passe.
/// </summary>
internal sealed class SecureClipboard : IDisposable
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    /// <summary>Nouvel essai quand le presse-papiers est occupé au moment de l'effacer.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly DispatcherTimer _timer;
    private readonly TimeSpan _delay;
    private readonly nint _window;
    private uint _sequence;
    private bool _disposed;

    /// <param name="window">Fenêtre propriétaire du presse-papiers (requise par Windows pour y écrire).</param>
    /// <param name="cleared">Appelé quand le mot de passe est effacé à la fin du délai.</param>
    public SecureClipboard(nint window, TimeSpan delay, Action cleared)
    {
        _window = window;
        _delay = delay;
        _timer = new DispatcherTimer { Interval = delay };
        _timer.Tick += (_, _) =>
        {
            if (Clear())
            {
                cleared();
            }
        };
    }

    public TimeSpan Delay => _delay;

    /// <summary>Copie <paramref name="secret"/> ; faux si une autre application garde le presse-papiers ouvert.</summary>
    public bool Copy(char[] secret)
    {
        if (!Open())
        {
            return false;
        }

        try
        {
            if (!EmptyClipboard())
            {
                return false;
            }

            var text = GlobalAlloc(GmemMoveable, (nuint)((secret.Length + 1) * sizeof(char)));
            if (text == 0)
            {
                throw new Win32Exception();
            }

            var pointer = GlobalLock(text);
            if (pointer == 0)
            {
                GlobalFree(text);
                throw new Win32Exception();
            }

            Marshal.Copy(secret, 0, pointer, secret.Length);
            Marshal.WriteInt16(pointer, secret.Length * sizeof(char), 0);
            GlobalUnlock(text);
            if (SetClipboardData(CfUnicodeText, text) == 0)
            {
                // Mémoire non prise par Windows : effacée et libérée ici.
                ZeroAndFree(text, secret.Length + 1);
                throw new Win32Exception();
            }

            // Formats reconnus par Windows et par les outils de gestion du presse-papiers.
            SetDword("ExcludeClipboardContentFromMonitorProcessing", 0);
            SetDword("CanIncludeInClipboardHistory", 0);
            SetDword("CanUploadToCloudClipboard", 0);
        }
        finally
        {
            CloseClipboard();
        }

        _sequence = GetClipboardSequenceNumber();
        _timer.Stop();
        _timer.Interval = _delay;
        _timer.Start();
        return true;
    }

    /// <summary>
    /// Vide le presse-papiers s'il contient encore le mot de passe copié ; vrai s'il a été vidé. Occupé par une autre
    /// application (gestionnaire de presse-papiers, bureau à distance) : nouvel essai une seconde plus tard, tant
    /// qu'il contient ce mot de passe.
    /// </summary>
    public bool Clear() => Clear(attempts: 10);

    /// <summary>À la fermeture : plus d'essai possible ensuite, on insiste davantage.</summary>
    public void Dispose()
    {
        _disposed = true;
        Clear(attempts: 100);
    }

    private bool Clear(int attempts)
    {
        _timer.Stop();
        if (_sequence == 0 || GetClipboardSequenceNumber() != _sequence)
        {
            // Rien de copié, ou l'utilisateur a copié autre chose entre-temps : on n'y touche pas.
            _sequence = 0;
            return false;
        }

        bool emptied = false;
        if (Open(attempts))
        {
            try
            {
                emptied = EmptyClipboard();
            }
            finally
            {
                CloseClipboard();
            }
        }

        if (emptied)
        {
            _sequence = 0;
        }
        else if (!_disposed)
        {
            // Le mot de passe y est encore : le minuteur réessaiera.
            _timer.Interval = RetryDelay;
            _timer.Start();
        }

        return emptied;
    }

    private bool Open(int attempts = 10)
    {
        // Une autre application peut garder le presse-papiers ouvert un court instant.
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if (OpenClipboard(_window))
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }

    private static void SetDword(string format, int value)
    {
        uint id = RegisterClipboardFormat(format);
        if (id == 0)
        {
            return;
        }

        var data = GlobalAlloc(GmemMoveable, sizeof(int));
        if (data == 0)
        {
            return;
        }

        var pointer = GlobalLock(data);
        if (pointer == 0)
        {
            GlobalFree(data);
            return;
        }

        Marshal.WriteInt32(pointer, value);
        GlobalUnlock(data);
        if (SetClipboardData(id, data) == 0)
        {
            GlobalFree(data);
        }
    }

    private static void ZeroAndFree(nint memory, int chars)
    {
        var pointer = GlobalLock(memory);
        if (pointer != 0)
        {
            for (int i = 0; i < chars; i++)
            {
                Marshal.WriteInt16(pointer, i * sizeof(char), 0);
            }

            GlobalUnlock(memory);
        }

        GlobalFree(memory);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(nint owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string format);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalFree(nint memory);
}

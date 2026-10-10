using System.Text;
using ZillaTerm.Core.Terminal;

namespace ZillaTerm.Core.Interventions;

/// <summary>
/// Texte d'un terminal pour le journal d'intervention : chaque ligne complète, une fois quittée (les morceaux d'une ligne
/// coupée par le retour automatique sont réunis), sur l'écran principal seulement (pas vim, less ou top). La commande
/// que ZillaTerm tape pour suivre le dossier, effacée de l'écran, n'y figure pas non plus : les lignes qui suivent sa
/// marque attendent, et sont oubliées si l'effacement arrive.
/// </summary>
/// <remarks>À utiliser sur le fil de l'émulateur (celui de l'interface), comme lui.</remarks>
public sealed class InterventionTerminalTranscript : IDisposable
{
    private readonly TerminalEmulator _emulator;
    private readonly Action<string> _line;
    private readonly StringBuilder _wrapped = new();
    private readonly List<string> _held = [];

    /// <param name="line">Reçoit chaque ligne complète.</param>
    public InterventionTerminalTranscript(TerminalEmulator emulator, Action<string> line)
    {
        _emulator = emulator;
        _line = line;
        emulator.LineLeft += OnLineLeft;
        emulator.EraseMarkerReceived += OnErased;
    }

    public void Dispose()
    {
        _emulator.LineLeft -= OnLineLeft;
        _emulator.EraseMarkerReceived -= OnErased;
        Release();
    }

    private void OnLineLeft(long number, string text, bool wrap)
    {
        _wrapped.Append(text);
        if (wrap)
        {
            return;
        }

        string full = _wrapped.ToString();
        _wrapped.Clear();
        if (_emulator.EraseMark is long mark && number >= mark)
        {
            _held.Add(full);
            return;
        }

        // Marque retirée sans effacement (shell qui n'a pas exécuté la commande) : les lignes retenues étaient réelles.
        Release();
        _line(full);
    }

    private void OnErased() => _held.Clear();

    private void Release()
    {
        foreach (var held in _held)
        {
            _line(held);
        }

        _held.Clear();
    }
}

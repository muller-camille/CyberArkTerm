namespace CyberArkTerm.App.Tests;

/// <summary>
/// Tests qui créent des fenêtres WPF ou utilisent le presse-papiers : exécutés l'un après l'autre, car
/// <see cref="DialogTests"/> crée l'objet <c>Application</c> (unique dans le processus) qui porte le thème.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfCollection
{
    public const string Name = "WPF";
}

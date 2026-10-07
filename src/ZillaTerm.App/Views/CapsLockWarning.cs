using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

namespace ZillaTerm.App.Views;

/// <summary>Affiche un avertissement tant que Verr. Maj est activé pendant la saisie d'un mot de passe.</summary>
internal static class CapsLockWarning
{
    public static void Attach(TextBlock warning, params PasswordBox[] boxes)
    {
        void Update()
        {
            bool show = boxes.Any(b => b.IsKeyboardFocused) && Keyboard.IsKeyToggled(Key.CapsLock);
            var visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (warning.Visibility != visibility)
            {
                warning.Visibility = visibility;
                if (show && UIElementAutomationPeer.CreatePeerForElement(warning) is { } peer)
                {
                    peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                }
            }
        }

        foreach (var box in boxes)
        {
            box.GotKeyboardFocus += (_, _) => Update();
            box.LostKeyboardFocus += (_, _) => Update();
            // Après la touche : l'état de Verr. Maj est alors à jour.
            box.PreviewKeyUp += (_, _) => Update();
        }
    }
}

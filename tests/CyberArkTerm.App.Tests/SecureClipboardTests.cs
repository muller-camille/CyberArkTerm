using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Interop;
using CyberArkTerm.App.Services;

namespace CyberArkTerm.App.Tests;

public sealed class SecureClipboardTests
{
    /// <summary>
    /// Mot de passe fictif : copié avec les formats qui l'excluent de l'historique et de la synchronisation, effacé
    /// ensuite ; si l'utilisateur a copié autre chose entre-temps, son contenu n'est pas touché.
    /// </summary>
    [Fact]
    public void CopiesWithExclusionFormatsThenClearsOnlyItsOwnContent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunOnSta(() =>
        {
            using var window = new HwndSource(new HwndSourceParameters("cyberarkterm-clipboard-test") { Width = 1, Height = 1 });
            var clipboard = new SecureClipboard(window.Handle, TimeSpan.FromMinutes(5), () => { });
            char[] secret = "fictif-Pa55!é".ToCharArray();

            Assert.True(clipboard.Copy(secret));
            Assert.Equal("fictif-Pa55!é", Clipboard.GetText());
            var data = Clipboard.GetDataObject()!;
            Assert.True(data.GetDataPresent("CanIncludeInClipboardHistory"));
            Assert.True(data.GetDataPresent("CanUploadToCloudClipboard"));
            Assert.True(data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing"));

            Assert.True(clipboard.Clear());
            Assert.False(Clipboard.ContainsText());

            Assert.True(clipboard.Copy(secret));
            Clipboard.SetText("copié par l'utilisateur");
            Assert.False(clipboard.Clear());
            Assert.Equal("copié par l'utilisateur", Clipboard.GetText());
            Clipboard.Clear();
        });
    }

    private static void RunOnSta(Action test)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}

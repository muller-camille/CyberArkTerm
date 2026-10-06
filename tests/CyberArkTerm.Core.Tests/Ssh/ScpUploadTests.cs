using CyberArkTerm.Core.Ssh;

namespace CyberArkTerm.Core.Tests.Ssh;

public sealed class ScpUploadTests
{
    /// <summary>
    /// Sortie d'erreur du serveur reprise dans le journal et le bilan : sur une ligne, sans caractères de contrôle
    /// (séquences d'échappement du terminal comprises), raccourcie.
    /// </summary>
    [Fact]
    public void ServerOutputIsCleanedForTheLogAndTheSummary()
    {
        Assert.Equal("PSMP: refused | code 12", ScpUpload.Clean("  PSMP: refused\r\ncode 12\n", 100).Replace("  ", " "));
        Assert.Equal("?[31mred?[0m", ScpUpload.Clean("\u001b[31mred\u001b[0m", 100));
        Assert.Equal("abcde…", ScpUpload.Clean("abcdefghij", 5));
        Assert.Equal("", ScpUpload.Clean(" \n ", 100));
    }
}

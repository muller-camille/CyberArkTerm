using ZillaTerm.Core.Ssh;

namespace ZillaTerm.Core.Tests.Ssh;

public class SftpLongNameTests
{
    [Theory]
    // OpenSSH : droits, liens, propriétaire, groupe, taille, date, nom.
    [InlineData("-rw-r--r--    1 oracle   dba          1024 Oct  9 14:31 app.log", 1024, "oracle", "dba")]
    [InlineData("drwxr-xr-x    2 root     root         4096 Jan  1  2025 bin", 4096, "root", "root")]
    [InlineData("lrwxrwxrwx    1 www-data www-data       11 Oct  9 14:31 current -> release-42", 11, "www-data", "www-data")]
    // Sans nom pour l'UID ou le GID, le serveur écrit le numéro.
    [InlineData("-rw-------    1 1001     1001            0 Oct  9 14:31 vide", 0, "1001", "1001")]
    // Marque d'ACL après les droits, bits spéciaux, nom de fichier avec des espaces.
    [InlineData("-rwsr-x---+   1 svc_app  app_grp      2048 Oct  9 14:31 mon fichier.txt", 2048, "svc_app", "app_grp")]
    [InlineData("drwxrwxrwt   12 root     root          480 Oct  9 14:31 tmp", 480, "root", "root")]
    public void ReadsOwnerAndGroup(string longName, long size, string owner, string group)
    {
        Assert.Equal((owner, group), SftpLongName.Parse(longName, size));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("app.log")]
    // Groupe avec une espace : les champs sont décalés, la taille n'est pas au cinquième.
    [InlineData("-rw-r--r--    1 jdoe     Domain Users 1024 Oct  9 14:31 app.log")]
    // Taille différente de celle des attributs.
    [InlineData("-rw-r--r--    1 oracle   dba          9999 Oct  9 14:31 app.log")]
    // Pas au format de ls -l.
    [InlineData("app.log 1 oracle dba 1024 Oct 9 14:31")]
    [InlineData("-rw-r--r--    x oracle   dba          1024 Oct  9 14:31 app.log")]
    // Nom réduit à rien une fois retirés les caractères invisibles.
    [InlineData("-rw-r--r--    1 ‮       dba          1024 Oct  9 14:31 app.log")]
    public void KeepsTheNumbersWhenTheLineIsNotAsExpected(string? longName)
    {
        Assert.Null(SftpLongName.Parse(longName, 1024));
    }

    [Fact]
    public void RemovesInvisibleCharactersFromNames()
    {
        Assert.Equal(("rootevil", "dba"), SftpLongName.Parse("-rw-r--r--    1 root‮evil dba 5 Oct  9 14:31 a", 5));
    }
}

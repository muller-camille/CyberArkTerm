using System.Text;
using CyberArkTerm.Core.KeePass;

namespace CyberArkTerm.Core.Tests.KeePass;

public class KeePassDatabaseTests
{
    /// <summary>Un champ en double ou sans nom (écrit par un autre outil) ne doit pas rendre tout le coffre illisible.</summary>
    [Fact]
    public void DuplicateOrUnnamedFieldsDoNotBreakTheVault()
    {
        const string Xml = """
            <KeePassFile><Meta /><Root><Group><UUID>AAAAAAAAAAAAAAAAAAAAAA==</UUID><Name>Racine</Name>
            <Entry><UUID>AQEBAQEBAQEBAQEBAQEBAQ==</UUID>
            <String><Key>Title</Key><Value>srv-lnx01</Value></String>
            <String><Key>Port</Key><Value>22</Value></String>
            <String><Key>Port</Key><Value>2222</Value></String>
            <String><Value>sans nom</Value></String>
            </Entry></Group></Root></KeePassFile>
            """;
        using var db = new KeePassDatabase();
        db.LoadXml(Encoding.UTF8.GetBytes(Xml), new ChaCha20(new byte[32], new byte[12]));

        var entry = Assert.Single(db.Entries);
        Assert.Equal("srv-lnx01", entry.Title);
        Assert.Equal("22", Assert.Single(entry.CustomFields).Value);
    }
}

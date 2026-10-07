namespace ZillaTerm.Core.Tests;

public sealed class PsmpRoutingTests
{
    private static AppSettings With(string defaultPsmp, params string[] others) => new()
    {
        PsmpAddress = defaultPsmp,
        PsmpServers = others.Select(a => new PsmpServer { Address = a }).ToList(),
    };

    /// <summary>Chaque serveur passe par le PSMP de son domaine.</summary>
    [Fact]
    public void ServerGoesThroughThePsmpOfItsDomain()
    {
        var settings = With("", "psmp.xxx.xx.com", "psmp.zzz.xx.com");

        Assert.Equal("psmp.xxx.xx.com", PsmpRouting.Resolve(settings, "blabla.xxx.xx.com")?.Host);
        Assert.Equal("psmp.zzz.xx.com", PsmpRouting.Resolve(settings, "blabla.zzz.xx.com")?.Host);
        Assert.Equal("zzz.xx.com", PsmpRouting.Resolve(settings, "Blabla.ZZZ.xx.com.")?.Domain);
    }

    /// <summary>Sans PSMP pour son sous-domaine, un serveur passe par celui du domaine au-dessus ; le plus proche l'emporte.</summary>
    [Fact]
    public void ServerOfASubdomainUsesTheClosestPsmpAbove()
    {
        var settings = With("", "psmp.xxx.ss.com");
        Assert.Equal("psmp.xxx.ss.com", PsmpRouting.Resolve(settings, "blabla.zzz.xxx.ss.com")?.Host);

        settings.PsmpServers.Add(new PsmpServer { Address = "psmp.zzz.xxx.ss.com" });
        Assert.Equal("psmp.zzz.xxx.ss.com", PsmpRouting.Resolve(settings, "blabla.zzz.xxx.ss.com")?.Host);
        Assert.Equal("psmp.xxx.ss.com", PsmpRouting.Resolve(settings, "blabla.yyy.xxx.ss.com")?.Host);
    }

    /// <summary>Le PSMP par défaut sert les serveurs de son domaine et ceux qu'aucun PSMP ne couvre.</summary>
    [Fact]
    public void DefaultPsmpServesItsDomainAndEverythingElse()
    {
        var settings = With("psmp.corp.com", "psmp.paris.corp.com");
        settings.PsmpPort = 2222;

        Assert.Equal(new PsmpEndpoint("psmp.paris.corp.com", 22, "paris.corp.com"), PsmpRouting.Resolve(settings, "srv1.paris.corp.com"));
        Assert.Equal(new PsmpEndpoint("psmp.corp.com", 2222, "corp.com"), PsmpRouting.Resolve(settings, "srv2.lyon.corp.com"));
        Assert.Equal(new PsmpEndpoint("psmp.corp.com", 2222, null), PsmpRouting.Resolve(settings, "srv3.other.org"));
        Assert.Equal(new PsmpEndpoint("psmp.corp.com", 2222, null), PsmpRouting.Resolve(settings, "10.1.2.3"));
        Assert.Equal(new PsmpEndpoint("psmp.corp.com", 2222, null), PsmpRouting.Resolve(settings, "srv4"));
        Assert.Equal(new PsmpEndpoint("psmp.corp.com", 2222, null), PsmpRouting.Resolve(settings, null));
    }

    /// <summary>Sans PSMP par défaut : le seul PSMP de la liste sert de repli ; avec plusieurs, aucun n'est choisi au hasard.</summary>
    [Fact]
    public void WithoutDefault_FallsBackOnlyToASinglePsmp()
    {
        var single = With("", "psmp.xxx.xx.com");
        Assert.Equal(new PsmpEndpoint("psmp.xxx.xx.com", 22, null), PsmpRouting.Resolve(single, "srv.other.org"));

        var several = With("", "psmp.xxx.xx.com", "psmp.zzz.xx.com");
        Assert.Null(PsmpRouting.Resolve(several, "srv.other.org"));
        Assert.Null(PsmpRouting.Resolve(several, "10.0.0.1"));
        Assert.True(PsmpRouting.Any(several));
        Assert.False(PsmpRouting.Any(With("", " ")));
    }

    /// <summary>Un domaine saisi remplace celui de l'adresse : un PSMP peut servir un autre domaine que le sien.</summary>
    [Fact]
    public void ExplicitDomainReplacesTheOneOfTheAddress()
    {
        var settings = With("");
        settings.PsmpServers.Add(new PsmpServer { Address = "psmp01.infra.corp.com", Port = 2022, Domain = "*.DMZ.corp.com" });
        settings.PsmpServers.Add(new PsmpServer { Address = "10.0.0.5", Domain = "lab.corp.com" });

        Assert.Equal(new PsmpEndpoint("psmp01.infra.corp.com", 2022, "dmz.corp.com"), PsmpRouting.Resolve(settings, "web.dmz.corp.com"));
        Assert.Equal(new PsmpEndpoint("10.0.0.5", 22, "lab.corp.com"), PsmpRouting.Resolve(settings, "db.lab.corp.com"));
        Assert.Null(PsmpRouting.Resolve(settings, "srv.infra.corp.com"));
    }

    /// <summary>Un nom qui contient le domaine sans en faire partie ne passe pas par son PSMP.</summary>
    [Fact]
    public void DomainMustBeAWholeSuffix()
    {
        var settings = With("", "psmp.corp.com", "psmp.other.org");
        Assert.Null(PsmpRouting.Resolve(settings, "srv.evilcorp.com"));
        Assert.Equal("psmp.corp.com", PsmpRouting.Resolve(settings, "corp.com")?.Host);
    }

    [Theory]
    [InlineData("psmp.paris.corp.com", "paris.corp.com")]
    [InlineData("PSMP.Corp.com.", "corp.com")]
    [InlineData("psmp", "")]
    [InlineData("10.1.2.3", "")]
    [InlineData("", "")]
    public void DomainOf(string address, string expected) => Assert.Equal(expected, PsmpRouting.DomainOf(address));

    [Theory]
    [InlineData("corp.com", true)]
    [InlineData("*.corp.com", true)]
    [InlineData(".corp.com", true)]
    [InlineData("my_lab.corp-1.com", true)]
    [InlineData("corp..com", false)]
    [InlineData("corp.com/x", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("", false)]
    [InlineData("srv corp.com", false)]
    public void IsValidDomain(string domain, bool expected) => Assert.Equal(expected, PsmpRouting.IsValidDomain(domain));

    /// <summary>La liste des PSMP est enregistrée ; une entrée nulle d'un fichier modifié à la main est ignorée.</summary>
    [Fact]
    public void PsmpServersRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zillaterm-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "settings.json");
            With("psmp.corp.com", "psmp.paris.corp.com").Save(path);
            Assert.Equal("psmp.paris.corp.com", Assert.Single(AppSettings.Load(path).PsmpServers).Address);

            File.WriteAllText(path, """{ "PsmpServers": [ null, { "Address": "psmp.lyon.corp.com", "Domain": null } ] }""");
            var loaded = Assert.Single(AppSettings.Load(path).PsmpServers);
            Assert.Equal("", loaded.Domain);
            Assert.Equal("psmp.lyon.corp.com", PsmpRouting.Resolve(AppSettings.Load(path), "srv.lyon.corp.com")?.Host);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}

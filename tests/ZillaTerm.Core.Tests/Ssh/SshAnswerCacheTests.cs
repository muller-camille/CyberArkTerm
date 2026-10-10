using ZillaTerm.Core.Ssh;

namespace ZillaTerm.Core.Tests.Ssh;

/// <summary>Mots de passe gardés pour les connexions suivantes : partagés dans un groupe, oubliés au bon moment.</summary>
public sealed class SshAnswerCacheTests
{
    [Fact]
    public void GroupForgetsOnceEverySessionIsAuthenticated()
    {
        var group = new SshAnswerCache();
        group.Join();
        group.Join();
        group.Set("Password:", "secret");

        group.Leave();
        Assert.True(group.TryGet("Password:", out var answer));
        Assert.Equal("secret", answer);

        group.Leave();
        Assert.False(group.TryGet("Password:", out _));
    }

    [Fact]
    public void ForgetAllClearsEveryCache()
    {
        var first = new SshAnswerCache();
        var second = new SshAnswerCache();
        first.Set("Password:", "a");
        second.Set("Mot de passe :", "b");

        SshAnswerCache.ForgetAll();

        Assert.False(first.TryGet("Password:", out _));
        Assert.False(second.TryGet("Mot de passe :", out _));
    }
}

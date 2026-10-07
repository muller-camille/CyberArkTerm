namespace CyberArkTerm.Core.Tests;

public class SafeProfilesTests
{
    [Fact]
    public void ProfilesGrowFromReadOnlyToFull()
    {
        var all = new SafePermissions().All().Select(p => p.Name).ToList();

        Assert.Equal(22, all.Count);
        Assert.True(SafeProfiles.Rights(SafeProfile.ReadOnly).IsProperSubsetOf(SafeProfiles.Rights(SafeProfile.AccountUser)));
        Assert.True(SafeProfiles.Rights(SafeProfile.AccountUser).IsProperSubsetOf(SafeProfiles.Rights(SafeProfile.AccountManager)));
        Assert.True(SafeProfiles.Rights(SafeProfile.AccountManager).IsProperSubsetOf(SafeProfiles.Rights(SafeProfile.Full)));
        Assert.True(SafeProfiles.Rights(SafeProfile.Full).SetEquals(all));
        // Gérer le safe ou ses membres, et accéder sans validation : seulement le profil complet.
        Assert.DoesNotContain(nameof(SafePermissions.ManageSafeMembers), SafeProfiles.Rights(SafeProfile.AccountManager));
        Assert.DoesNotContain(nameof(SafePermissions.AccessWithoutConfirmation), SafeProfiles.Rights(SafeProfile.AccountManager));
    }

    [Fact]
    public void MatchFindsTheProfileOrCustom()
    {
        foreach (var profile in SafeProfiles.Choices)
        {
            Assert.Equal(profile, SafeProfiles.Match(SafeProfiles.Rights(profile)));
        }

        Assert.Equal(SafeProfile.Custom, SafeProfiles.Match([nameof(SafePermissions.ListAccounts), nameof(SafePermissions.UseAccounts)]));
        Assert.Equal(SafeProfile.Custom, SafeProfiles.Match([]));
    }

    [Fact]
    public void SensitiveAddedListsOnlyNewSensitiveRights()
    {
        string[] before = [nameof(SafePermissions.ListAccounts), nameof(SafePermissions.DeleteAccounts)];
        string[] after = [.. before, nameof(SafePermissions.ManageSafeMembers), nameof(SafePermissions.AddAccounts)];

        Assert.Equal([nameof(SafePermissions.ManageSafeMembers)], SafeProfiles.SensitiveAdded(before, after));
        Assert.Empty(SafeProfiles.SensitiveAdded(after, before));
    }
}

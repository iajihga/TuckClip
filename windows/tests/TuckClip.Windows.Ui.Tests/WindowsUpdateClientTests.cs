using TuckClip.Windows.Services;

namespace TuckClip.Windows.Ui.Tests;

[TestClass]
public sealed class WindowsUpdateClientTests
{
    [TestMethod]
    public void UpdateSourceIsTheOfficialHttpsRepository()
    {
        Assert.AreEqual(
            "https://github.com/mzopedia/TuckClip",
            VelopackUpdateClient.RepositoryUrl);
    }

    [TestMethod]
    public void StableUpgradeIsEligible()
    {
        Assert.IsTrue(VelopackUpdateClient.IsEligibleUpdate(
            isDowngrade: false,
            targetIsPrerelease: false));
    }

    [TestMethod]
    public void DowngradesAndPrereleasesAreRejected()
    {
        Assert.IsFalse(VelopackUpdateClient.IsEligibleUpdate(
            isDowngrade: true,
            targetIsPrerelease: false));
        Assert.IsFalse(VelopackUpdateClient.IsEligibleUpdate(
            isDowngrade: false,
            targetIsPrerelease: true));
        Assert.IsFalse(VelopackUpdateClient.IsEligibleUpdate(
            isDowngrade: true,
            targetIsPrerelease: true));
    }
}

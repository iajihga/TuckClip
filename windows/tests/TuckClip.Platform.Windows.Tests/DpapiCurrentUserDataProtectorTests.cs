using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TuckClip.Platform.Windows.Security;

namespace TuckClip.Platform.Windows.Tests;

[TestClass]
public sealed class DpapiCurrentUserDataProtectorTests
{
    [TestMethod]
    public void ProtectIsExplicitlyWindowsOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var protector = new DpapiCurrentUserDataProtector();
        Assert.ThrowsExactly<PlatformNotSupportedException>(
            () => protector.Protect([1, 2, 3], "history-test"));
    }

    [TestMethod]
    public void ProtectAndUnprotectRoundTripsForCurrentWindowsUser()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var protector = new DpapiCurrentUserDataProtector();
        var plaintext = Encoding.UTF8.GetBytes("private clipboard payload");

        var protectedData = protector.Protect(plaintext, "history-test");
        var roundTripped = protector.Unprotect(protectedData, "history-test");

        CollectionAssert.AreEqual(plaintext, roundTripped);
        Assert.AreNotEqual(Convert.ToBase64String(plaintext), Convert.ToBase64String(protectedData));
    }

    [TestMethod]
    public void UnprotectWithDifferentPurposeFailsClosedOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var protector = new DpapiCurrentUserDataProtector();
        var protectedData = protector.Protect([4, 5, 6], "history-metadata");

        Assert.ThrowsExactly<CryptographicException>(
            () => protector.Unprotect(protectedData, "history-image"));
    }
}

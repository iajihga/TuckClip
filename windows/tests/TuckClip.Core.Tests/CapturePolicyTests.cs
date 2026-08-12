using System.Security.Cryptography;
using System.Text;

namespace TuckClip.Core.Tests;

[TestClass]
public sealed class CapturePolicyTests
{
    [TestMethod]
    public void Normalize_PrefersFilesOverImageAndText()
    {
        var decision = CapturePolicy.Normalize(
            new ClipboardCapture
            {
                PlainText = "fallback",
                ImageData = [1, 2, 3],
                FilePaths = [@"c:/Temp/example.txt"],
            },
            new AppSettings());

        Assert.AreEqual(ClipKind.Files, decision.Kind);
        CollectionAssert.AreEqual(new[] { @"C:\Temp\example.txt" }, decision.Capture!.FilePaths.ToArray());
        Assert.IsNull(decision.Capture.ImageData);
        Assert.IsNull(decision.Capture.PlainText);
    }

    [TestMethod]
    public void Normalize_PrefersImageOverText()
    {
        var decision = CapturePolicy.Normalize(
            new ClipboardCapture { PlainText = "fallback", ImageData = [1, 2, 3] },
            new AppSettings());

        Assert.AreEqual(ClipKind.Image, decision.Kind);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, decision.Capture!.ImageData!);
        Assert.IsNull(decision.Capture.PlainText);
    }

    [TestMethod]
    [DataRow("https://example.com/path?q=1")]
    [DataRow("http://localhost:8080")]
    [DataRow("ftp://files.example.com/archive")]
    [DataRow("mailto:hello@example.com")]
    public void Normalize_ClassifiesSupportedStandaloneUrls(string value)
    {
        var decision = CapturePolicy.Normalize(new ClipboardCapture { PlainText = value }, new AppSettings());
        Assert.AreEqual(ClipKind.Link, decision.Kind);
    }

    [TestMethod]
    [DataRow("visit https://example.com")]
    [DataRow("https://example.com trailing")]
    [DataRow("www.example.com")]
    [DataRow("file:///tmp/example")]
    public void Normalize_DoesNotOverclassifyTextAsLink(string value)
    {
        var decision = CapturePolicy.Normalize(new ClipboardCapture { PlainText = value }, new AppSettings());
        Assert.AreEqual(ClipKind.Text, decision.Kind);
    }

    [TestMethod]
    public void Normalize_RejectsTextAboveUtf8Limit()
    {
        var text = new string('你', (CapturePolicy.MaximumTextUtf8Bytes / 3) + 1);
        var decision = CapturePolicy.Normalize(new ClipboardCapture { PlainText = text }, new AppSettings());
        Assert.AreEqual(CaptureRejectionReason.Oversized, decision.RejectionReason);
    }

    [TestMethod]
    public void Normalize_AcceptsTextAtUtf8Limit()
    {
        var text = new string('a', CapturePolicy.MaximumTextUtf8Bytes);
        var decision = CapturePolicy.Normalize(new ClipboardCapture { PlainText = text }, new AppSettings());
        Assert.IsTrue(decision.IsAccepted);
    }

    [TestMethod]
    public void Normalize_RejectsImageAboveLimit()
    {
        var bytes = new byte[CapturePolicy.MaximumBinaryOrFileListBytes + 1];
        var decision = CapturePolicy.Normalize(new ClipboardCapture { ImageData = bytes }, new AppSettings());
        Assert.AreEqual(CaptureRejectionReason.Oversized, decision.RejectionReason);
    }

    [TestMethod]
    [DataRow("-----BEGIN PRIVATE KEY-----\nabc")]
    [DataRow("-----BEGIN RSA PRIVATE KEY-----\nabc")]
    [DataRow("-----begin openssh private key-----\nabc")]
    [DataRow("-----BEGIN PGP PRIVATE KEY BLOCK-----\nabc")]
    public void Normalize_RejectsHighConfidencePrivateKeyHeaders(string value)
    {
        var decision = CapturePolicy.Normalize(new ClipboardCapture { PlainText = value }, new AppSettings());
        Assert.AreEqual(CaptureRejectionReason.HighConfidencePrivateKey, decision.RejectionReason);
    }

    [TestMethod]
    public void Normalize_DoesNotRejectPublicKey()
    {
        const string value = "-----BEGIN PUBLIC KEY-----\nabc";
        var decision = CapturePolicy.Normalize(new ClipboardCapture { PlainText = value }, new AppSettings());
        Assert.IsTrue(decision.IsAccepted);
    }

    [TestMethod]
    public void Normalize_AllowsPrivateKeyWhenExplicitlyConfigured()
    {
        const string value = "-----BEGIN PRIVATE KEY-----\nabc";
        var settings = new AppSettings { FilterHighConfidencePrivateKeys = false };
        var decision = CapturePolicy.Normalize(new ClipboardCapture { PlainText = value }, settings);
        Assert.IsTrue(decision.IsAccepted);
    }

    [TestMethod]
    public void Normalize_RejectsTuckClipGeneratedWrite()
    {
        var decision = CapturePolicy.Normalize(
            new ClipboardCapture { PlainText = "text", IsTuckClipGenerated = true },
            new AppSettings());
        Assert.AreEqual(CaptureRejectionReason.InternalWrite, decision.RejectionReason);
    }

    [TestMethod]
    [DataRow(true, false, false)]
    [DataRow(false, true, false)]
    [DataRow(false, false, true)]
    public void Normalize_RejectsPrivateTransientAndPasswordManagerMarkers(
        bool isPrivate,
        bool isTransient,
        bool passwordManager)
    {
        var decision = CapturePolicy.Normalize(
            new ClipboardCapture
            {
                PlainText = "secret",
                IsPrivate = isPrivate,
                IsTransient = isTransient,
                IsPasswordManagerContent = passwordManager,
            },
            new AppSettings());
        Assert.IsFalse(decision.IsAccepted);
    }

    [TestMethod]
    [DataRow(true, false, false)]
    [DataRow(false, false, true)]
    [DataRow(false, true, false)]
    public void Normalize_RejectsAnyWindowsNoHistoryOrNoCloudMarker(
        bool canInclude,
        bool canUpload,
        bool excludeMonitor)
    {
        var decision = CapturePolicy.Normalize(
            new ClipboardCapture
            {
                PlainText = "secret",
                CanIncludeInClipboardHistory = canInclude,
                CanUploadToCloudClipboard = canUpload,
                ExcludeFromMonitorProcessing = excludeMonitor,
            },
            new AppSettings());
        Assert.AreEqual(CaptureRejectionReason.PlatformPolicy, decision.RejectionReason);
    }

    [TestMethod]
    public void Fingerprint_UsesContractBytesAndLowercaseSha256()
    {
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("text\0hello")));
        var actual = ClipFingerprint.Compute(ClipKind.Text, "hello", Array.Empty<string>(), null);
        Assert.AreEqual(expected, actual);
        StringAssert.Matches(actual, new System.Text.RegularExpressions.Regex("^[0-9a-f]{64}$"));
    }

    [TestMethod]
    public void Fingerprint_IncludesKind()
    {
        var text = ClipFingerprint.Compute(ClipKind.Text, "https://example.com", Array.Empty<string>(), null);
        var link = ClipFingerprint.Compute(ClipKind.Link, "https://example.com", Array.Empty<string>(), null);
        Assert.AreNotEqual(text, link);
    }

    [TestMethod]
    public void FileFingerprint_NormalizesWindowsCaseAndSeparatorsWithoutChangingDisplayPath()
    {
        var firstDecision = CapturePolicy.Normalize(
            new ClipboardCapture { FilePaths = [@"c:/Users/Alice/Report.pdf"] },
            new AppSettings());
        var secondDecision = CapturePolicy.Normalize(
            new ClipboardCapture { FilePaths = [@"C:\USERS\ALICE\REPORT.PDF"] },
            new AppSettings());
        var first = ClipItemFactory.Create(firstDecision);
        var second = ClipItemFactory.Create(secondDecision);

        Assert.AreEqual(first.Fingerprint, second.Fingerprint);
        Assert.AreEqual(@"C:\Users\Alice\Report.pdf", first.FilePaths[0]);
    }

    [TestMethod]
    public void AppSettings_RejectsInvalidLimits()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new AppSettings { MaximumItemCount = 0 }.Validate());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new AppSettings { RetentionDays = -1 }.Validate());
    }
}

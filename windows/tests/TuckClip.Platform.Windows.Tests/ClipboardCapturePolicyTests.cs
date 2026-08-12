using Microsoft.VisualStudio.TestTools.UnitTesting;
using TuckClip.Platform.Windows.Clipboard;

namespace TuckClip.Platform.Windows.Tests;

[TestClass]
public sealed class ClipboardCapturePolicyTests
{
    [TestMethod]
    public void EvaluateRejectsTuckClipGeneratedWrites()
    {
        var formats = new SpyFormatReader();
        formats.AddUnreadable(WindowsClipboardFormats.TuckClipInternalWrite);

        var decision = new ClipboardCapturePolicy().Evaluate(formats, "TuckClip.exe");

        Assert.IsFalse(decision.ShouldCapture);
        Assert.AreEqual(ClipboardCaptureExclusionReason.InternalWrite, decision.ExclusionReason);
    }

    [TestMethod]
    [DataRow(WindowsClipboardFormats.ExcludeFromMonitorProcessing, ClipboardCaptureExclusionReason.MonitorProcessingExcluded)]
    [DataRow(WindowsClipboardFormats.CanIncludeInClipboardHistory, ClipboardCaptureExclusionReason.ClipboardHistoryDisabled)]
    [DataRow(WindowsClipboardFormats.CanUploadToCloudClipboard, ClipboardCaptureExclusionReason.CloudClipboardDisabled)]
    public void EvaluateRespectsWindowsPrivacyMarkers(
        string formatName,
        ClipboardCaptureExclusionReason expectedReason)
    {
        var formats = new SpyFormatReader();
        formats.Add(formatName, 0);

        var decision = new ClipboardCapturePolicy().Evaluate(formats, "notepad.exe");

        Assert.IsFalse(decision.ShouldCapture);
        Assert.AreEqual(expectedReason, decision.ExclusionReason);
    }

    [TestMethod]
    public void EvaluateExcludeFromMonitorProcessingNeedsOnlyMarkerPresence()
    {
        var formats = new SpyFormatReader();
        formats.AddUnreadable(WindowsClipboardFormats.ExcludeFromMonitorProcessing);

        var decision = new ClipboardCapturePolicy().Evaluate(formats, null);

        Assert.AreEqual(
            ClipboardCaptureExclusionReason.MonitorProcessingExcluded,
            decision.ExclusionReason);
    }

    [TestMethod]
    [DataRow(WindowsClipboardFormats.CanIncludeInClipboardHistory, ClipboardCaptureExclusionReason.ClipboardHistoryDisabled)]
    [DataRow(WindowsClipboardFormats.CanUploadToCloudClipboard, ClipboardCaptureExclusionReason.CloudClipboardDisabled)]
    public void EvaluateFailsClosedWhenPrivacyMarkerCannotBeRead(
        string formatName,
        ClipboardCaptureExclusionReason expectedReason)
    {
        var formats = new SpyFormatReader();
        formats.AddUnreadable(formatName);

        var decision = new ClipboardCapturePolicy().Evaluate(formats, null);

        Assert.IsFalse(decision.ShouldCapture);
        Assert.AreEqual(expectedReason, decision.ExclusionReason);
    }

    [TestMethod]
    public void EvaluateAllowsExplicitOptInMarkers()
    {
        var formats = new SpyFormatReader();
        formats.Add(WindowsClipboardFormats.CanIncludeInClipboardHistory, 1);
        formats.Add(WindowsClipboardFormats.CanUploadToCloudClipboard, 1);

        var decision = new ClipboardCapturePolicy().Evaluate(formats, "notepad.exe");

        Assert.IsTrue(decision.ShouldCapture);
        Assert.AreEqual(ClipboardCaptureExclusionReason.None, decision.ExclusionReason);
    }

    [TestMethod]
    public void EvaluateExcludesKnownPasswordManagersRegardlessOfPathAndExtension()
    {
        var decision = new ClipboardCapturePolicy().Evaluate(
            new SpyFormatReader(),
            @"C:\Program Files\Bitwarden.exe");

        Assert.IsFalse(decision.ShouldCapture);
        Assert.AreEqual(ClipboardCaptureExclusionReason.PasswordManager, decision.ExclusionReason);
    }

    [TestMethod]
    public void EvaluateExcludesConfiguredProcesses()
    {
        var policy = new ClipboardCapturePolicy(["PrivateEditor.exe"]);

        var decision = policy.Evaluate(new SpyFormatReader(), "privateeditor");

        Assert.IsFalse(decision.ShouldCapture);
        Assert.AreEqual(ClipboardCaptureExclusionReason.ConfiguredProcess, decision.ExclusionReason);
    }

    private sealed class SpyFormatReader : IClipboardFormatReader
    {
        private readonly Dictionary<string, int?> _formats = new(StringComparer.Ordinal);

        public void Add(string name, int value) => _formats.Add(name, value);

        public void AddUnreadable(string name) => _formats.Add(name, null);

        public bool Contains(string formatName) => _formats.ContainsKey(formatName);

        public bool TryReadInt32(string formatName, out int value)
        {
            if (_formats.TryGetValue(formatName, out var storedValue) && storedValue.HasValue)
            {
                value = storedValue.Value;
                return true;
            }

            value = default;
            return false;
        }
    }
}

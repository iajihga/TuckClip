using Microsoft.VisualStudio.TestTools.UnitTesting;
using TuckClip.Platform.Windows.Interop;
using TuckClip.Platform.Windows.Paste;

namespace TuckClip.Platform.Windows.Tests;

[TestClass]
public sealed class WindowsNativeApiPasteCommitTests
{
    private const uint ExpectedSequence = 41;
    private static readonly PasteTargetWindow Target = new((nint)42, 7, 8);
    private static readonly string[] SuccessfulCalls =
        ["identity:42", "modifier", "sequence", "foreground", "input"];
    private static readonly string[] IdentityCalls = ["identity:42"];
    private static readonly string[] ModifierGuardCalls = ["identity:42", "modifier"];
    private static readonly string[] SequenceGuardCalls = ["identity:42", "modifier", "sequence"];
    private static readonly string[] ForegroundGuardCalls =
        ["identity:42", "modifier", "sequence", "foreground"];

    [TestMethod]
    public void TryCommitPasteSendsInputOnlyAfterEveryFinalGuardPasses()
    {
        var native = new SpyPasteCommitNativeOperations();
        var api = new WindowsNativeApi(native);

        var result = api.TryCommitPaste(Target, ExpectedSequence);

        Assert.AreEqual(PasteCommitResult.Pasted, result);
        CollectionAssert.AreEqual(SuccessfulCalls, native.Calls);
        Assert.AreEqual(1, native.SendInputCalls);
    }

    [TestMethod]
    public void TryCommitPasteRejectsUnavailableTargetBeforeCallingNativeGuards()
    {
        var native = new SpyPasteCommitNativeOperations();
        var api = new WindowsNativeApi(native);

        var result = api.TryCommitPaste(PasteTargetWindow.Unavailable, ExpectedSequence);

        Assert.AreEqual(PasteCommitResult.TargetWindowUnavailable, result);
        Assert.IsEmpty(native.Calls);
        Assert.AreEqual(0, native.SendInputCalls);
    }

    [TestMethod]
    [DataRow(9, 8)]
    [DataRow(7, 9)]
    public void TryCommitPasteRejectsReusedHandleWhenThreadOrProcessChanged(
        int actualThreadId,
        int actualProcessId)
    {
        var native = new SpyPasteCommitNativeOperations
        {
            ActualThreadId = (uint)actualThreadId,
            ActualProcessId = (uint)actualProcessId,
        };
        var api = new WindowsNativeApi(native);

        var result = api.TryCommitPaste(Target, ExpectedSequence);

        Assert.AreEqual(PasteCommitResult.TargetWindowUnavailable, result);
        CollectionAssert.AreEqual(IdentityCalls, native.Calls);
        Assert.AreEqual(0, native.SendInputCalls);
    }

    [TestMethod]
    public void TryCommitPasteRejectsPressedModifierBeforeReadingClipboardOrForeground()
    {
        var native = new SpyPasteCommitNativeOperations { ModifierKeyDown = true };
        var api = new WindowsNativeApi(native);

        var result = api.TryCommitPaste(Target, ExpectedSequence);

        Assert.AreEqual(PasteCommitResult.ModifierKeysPressed, result);
        CollectionAssert.AreEqual(ModifierGuardCalls, native.Calls);
        Assert.AreEqual(0, native.SendInputCalls);
    }

    [TestMethod]
    public void TryCommitPasteRejectsChangedClipboardBeforeReadingForeground()
    {
        var native = new SpyPasteCommitNativeOperations { SequenceNumber = ExpectedSequence + 1 };
        var api = new WindowsNativeApi(native);

        var result = api.TryCommitPaste(Target, ExpectedSequence);

        Assert.AreEqual(PasteCommitResult.ClipboardChanged, result);
        CollectionAssert.AreEqual(SequenceGuardCalls, native.Calls);
        Assert.AreEqual(0, native.SendInputCalls);
    }

    [TestMethod]
    public void TryCommitPasteRejectsFinalForegroundChangeWithoutSendingInput()
    {
        var native = new SpyPasteCommitNativeOperations { ForegroundWindow = (nint)99 };
        var api = new WindowsNativeApi(native);

        var result = api.TryCommitPaste(Target, ExpectedSequence);

        Assert.AreEqual(PasteCommitResult.TargetWindowUnavailable, result);
        CollectionAssert.AreEqual(ForegroundGuardCalls, native.Calls);
        Assert.AreEqual(0, native.SendInputCalls);
    }

    [TestMethod]
    public void TryCommitPasteReportsRejectedInputAfterEveryGuardPassed()
    {
        var native = new SpyPasteCommitNativeOperations { SendInputResult = false };
        var api = new WindowsNativeApi(native);

        var result = api.TryCommitPaste(Target, ExpectedSequence);

        Assert.AreEqual(PasteCommitResult.InputRejected, result);
        CollectionAssert.AreEqual(SuccessfulCalls, native.Calls);
        Assert.AreEqual(1, native.SendInputCalls);
    }

    private sealed class SpyPasteCommitNativeOperations : IPasteCommitNativeOperations
    {
        public List<string> Calls { get; } = [];

        public uint ActualThreadId { get; init; } = Target.ThreadId;

        public uint ActualProcessId { get; init; } = Target.ProcessId;

        public bool ModifierKeyDown { get; init; }

        public uint SequenceNumber { get; init; } = ExpectedSequence;

        public nint ForegroundWindow { get; init; } = Target.Handle;

        public bool SendInputResult { get; init; } = true;

        public int SendInputCalls { get; private set; }

        public void EnsureSupported()
        {
        }

        public uint GetWindowThreadProcessId(nint windowHandle, out uint processIdentifier)
        {
            Calls.Add($"identity:{windowHandle}");
            processIdentifier = ActualProcessId;
            return ActualThreadId;
        }

        public bool IsModifierKeyDown()
        {
            Calls.Add("modifier");
            return ModifierKeyDown;
        }

        public uint GetClipboardSequenceNumber()
        {
            Calls.Add("sequence");
            return SequenceNumber;
        }

        public nint GetForegroundWindow()
        {
            Calls.Add("foreground");
            return ForegroundWindow;
        }

        public bool SendPasteShortcut()
        {
            Calls.Add("input");
            SendInputCalls++;
            return SendInputResult;
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using TuckClip.Platform.Windows.Paste;

namespace TuckClip.Platform.Windows.Tests;

[TestClass]
public sealed class PasteServiceTests
{
    private static readonly nint TargetWindow = 42;
    private static readonly nint PanelWindow = 99;
    private static readonly PasteTargetWindow Target = new(TargetWindow, 7, 8);
    private static readonly string[] SuccessfulCallOrder =
        ["write", "hide", "identity", "activate", "identity", "foreground", "commit-boundary", "commit"];
    private static readonly string[] CopyAndHideCallOrder = ["write", "hide"];
    private static readonly string[] CopyOnlyCallOrder = ["write"];

    [TestMethod]
    public async Task PasteAsyncPerformsTheTransactionInTheRequiredOrder()
    {
        var calls = new List<string>();
        var panel = new SpyPanelBoundary(calls);
        var foreground = new SpyForegroundWindow(calls, PanelWindow)
        {
            ActivateResult = true,
            MoveTargetToForegroundOnActivate = true,
        };
        var committer = new SpyPasteCommitter(calls, PasteCommitResult.Pasted);
        var service = CreateService(panel, foreground, committer);
        using var session = new PastePanelSession(Target);

        var result = await service.PasteAsync(session, new SpyClipboardWrite(calls, 17));

        Assert.AreEqual(PasteResult.Pasted, result);
        CollectionAssert.AreEqual(SuccessfulCallOrder, calls);
        Assert.AreEqual(1, committer.CommitCount);
    }

    [TestMethod]
    public async Task PasteAsyncFocusOrUipiFailureDegradesToCopyOnly()
    {
        var calls = new List<string>();
        var panel = new SpyPanelBoundary(calls);
        var foreground = new SpyForegroundWindow(calls, PanelWindow)
        {
            ActivateResult = false,
        };
        var committer = new SpyPasteCommitter(calls, PasteCommitResult.Pasted);
        var service = CreateService(panel, foreground, committer, confirmationAttempts: 2);
        using var session = new PastePanelSession(Target);

        var result = await service.PasteAsync(session, new SpyClipboardWrite(calls, 23));

        Assert.AreEqual(PasteResultKind.CopiedOnly, result.Kind);
        Assert.AreEqual(PasteFailureReason.TargetWindowUnavailable, result.FailureReason);
        Assert.IsTrue(result.WasCopied);
        Assert.AreEqual(0, committer.CommitCount);
    }

    [TestMethod]
    public async Task PasteAsyncExternalClipboardOverwriteNeverPostsInput()
    {
        var calls = new List<string>();
        var panel = new SpyPanelBoundary(calls);
        var foreground = new SpyForegroundWindow(calls, TargetWindow);
        var committer = new SpyPasteCommitter(calls, PasteCommitResult.ClipboardChanged);
        var service = CreateService(panel, foreground, committer);
        using var session = new PastePanelSession(Target);

        var result = await service.PasteAsync(session, new SpyClipboardWrite(calls, 31));

        Assert.AreEqual(PasteResultKind.CopiedOnly, result.Kind);
        Assert.AreEqual(PasteFailureReason.ClipboardChanged, result.FailureReason);
        Assert.AreEqual(1, committer.CommitCount);
    }

    [TestMethod]
    public async Task PasteAsyncSessionCancellationAfterCopyNeverRestoresFocusOrPostsInput()
    {
        var calls = new List<string>();
        using var session = new PastePanelSession(Target);
        var panel = new SpyPanelBoundary(calls)
        {
            AfterHide = session.Cancel,
        };
        var foreground = new SpyForegroundWindow(calls, PanelWindow);
        var committer = new SpyPasteCommitter(calls, PasteCommitResult.Pasted);
        var service = CreateService(panel, foreground, committer);

        var result = await service.PasteAsync(session, new SpyClipboardWrite(calls, 47));

        Assert.AreEqual(PasteResultKind.CopiedOnly, result.Kind);
        Assert.AreEqual(PasteFailureReason.SessionCancelled, result.FailureReason);
        CollectionAssert.AreEqual(CopyAndHideCallOrder, calls);
        Assert.AreEqual(0, committer.CommitCount);
    }

    [TestMethod]
    public async Task PasteAsyncStaleSessionCannotHideTheNewPanel()
    {
        var calls = new List<string>();
        var panel = new SpyPanelBoundary(calls)
        {
            IsCurrentResult = false,
        };
        var foreground = new SpyForegroundWindow(calls, PanelWindow);
        var committer = new SpyPasteCommitter(calls, PasteCommitResult.Pasted);
        var service = CreateService(panel, foreground, committer);
        using var session = new PastePanelSession(Target);

        var result = await service.PasteAsync(session, new SpyClipboardWrite(calls, 51));

        Assert.AreEqual(PasteFailureReason.StalePanelSession, result.FailureReason);
        CollectionAssert.AreEqual(CopyOnlyCallOrder, calls);
        Assert.AreEqual(0, committer.CommitCount);
    }

    [TestMethod]
    public async Task PasteAsyncSessionSupersededAtCommitStillCannotPostInput()
    {
        var calls = new List<string>();
        var panel = new SpyPanelBoundary(calls)
        {
            BeforeCommit = boundary => boundary.IsCurrentResult = false,
        };
        var foreground = new SpyForegroundWindow(calls, TargetWindow);
        var committer = new SpyPasteCommitter(calls, PasteCommitResult.Pasted);
        var service = CreateService(panel, foreground, committer);
        using var session = new PastePanelSession(Target);

        var result = await service.PasteAsync(session, new SpyClipboardWrite(calls, 61));

        Assert.AreEqual(PasteFailureReason.StalePanelSession, result.FailureReason);
        Assert.AreEqual(0, committer.CommitCount);
    }

    [TestMethod]
    public async Task PasteAsyncSendInputRejectionDegradesToCopyOnly()
    {
        var calls = new List<string>();
        var panel = new SpyPanelBoundary(calls);
        var foreground = new SpyForegroundWindow(calls, TargetWindow);
        var committer = new SpyPasteCommitter(calls, PasteCommitResult.InputRejected);
        var service = CreateService(panel, foreground, committer);
        using var session = new PastePanelSession(Target);

        var result = await service.PasteAsync(session, new SpyClipboardWrite(calls, 71));

        Assert.AreEqual(PasteResultKind.CopiedOnly, result.Kind);
        Assert.AreEqual(PasteFailureReason.InputRejected, result.FailureReason);
        Assert.AreEqual(1, committer.CommitCount);
    }

    [TestMethod]
    public async Task PasteAsyncReusedTargetWindowNeverActivatesOrPostsInput()
    {
        var calls = new List<string>();
        var panel = new SpyPanelBoundary(calls);
        var foreground = new SpyForegroundWindow(calls, PanelWindow)
        {
            SameWindowResult = false,
        };
        var committer = new SpyPasteCommitter(calls, PasteCommitResult.Pasted);
        var service = CreateService(panel, foreground, committer);
        using var session = new PastePanelSession(Target);

        var result = await service.PasteAsync(session, new SpyClipboardWrite(calls, 79));

        Assert.AreEqual(PasteFailureReason.TargetWindowUnavailable, result.FailureReason);
        Assert.AreEqual(0, committer.CommitCount);
        Assert.IsFalse(calls.Contains("activate", StringComparer.Ordinal));
    }

    [TestMethod]
    public async Task PasteAsyncFocusChangeAtFinalCommitNeverPostsInput()
    {
        var calls = new List<string>();
        var panel = new SpyPanelBoundary(calls);
        var foreground = new SpyForegroundWindow(calls, TargetWindow);
        var committer = new SpyPasteCommitter(calls, PasteCommitResult.TargetWindowUnavailable);
        var service = CreateService(panel, foreground, committer);
        using var session = new PastePanelSession(Target);

        var result = await service.PasteAsync(session, new SpyClipboardWrite(calls, 83));

        Assert.AreEqual(PasteFailureReason.TargetWindowUnavailable, result.FailureReason);
        Assert.AreEqual(1, committer.CommitCount);
    }

    [TestMethod]
    public async Task PasteAsyncWriteOwnershipConflictKeepsPanelAndCancelsPaste()
    {
        var calls = new List<string>();
        var panel = new SpyPanelBoundary(calls);
        var foreground = new SpyForegroundWindow(calls, TargetWindow);
        var committer = new SpyPasteCommitter(calls, PasteCommitResult.Pasted);
        var service = CreateService(panel, foreground, committer);
        using var session = new PastePanelSession(Target);

        var result = await service.PasteAsync(session, new ConflictingClipboardWrite(calls));

        Assert.AreEqual(PasteResultKind.Cancelled, result.Kind);
        Assert.AreEqual(PasteFailureReason.ClipboardChanged, result.FailureReason);
        CollectionAssert.AreEqual(CopyOnlyCallOrder, calls);
        Assert.AreEqual(0, committer.CommitCount);
    }

    private static PasteService CreateService(
        SpyPanelBoundary panel,
        SpyForegroundWindow foreground,
        SpyPasteCommitter committer,
        int confirmationAttempts = 1) =>
        new(
            panel,
            foreground,
            committer,
            new ImmediateDelay(),
            new PasteRetryOptions
            {
                ForegroundConfirmationAttempts = confirmationAttempts,
                ForegroundConfirmationInterval = TimeSpan.Zero,
                InputSettlementDelay = TimeSpan.Zero,
            });

    private sealed class SpyClipboardWrite(List<string> calls, uint sequenceNumber) : IClipboardWriteOperation
    {
        public ValueTask<ClipboardWriteReceipt> WriteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add("write");
            return ValueTask.FromResult(new ClipboardWriteReceipt(sequenceNumber));
        }
    }

    private sealed class ConflictingClipboardWrite(List<string> calls) : IClipboardWriteOperation
    {
        public ValueTask<ClipboardWriteReceipt> WriteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add("write");
            throw new ClipboardWriteConflictException("The clipboard changed.");
        }
    }

    private sealed class SpyPanelBoundary(List<string> calls) : IPanelSessionBoundary
    {
        public bool IsCurrentResult { get; set; } = true;

        public bool HideResult { get; set; } = true;

        public Action? AfterHide { get; init; }

        public Action<SpyPanelBoundary>? BeforeCommit { get; init; }

        public bool IsCurrent(PastePanelSession session) => IsCurrentResult;

        public ValueTask<bool> HideAsync(PastePanelSession session, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add("hide");
            AfterHide?.Invoke();
            return ValueTask.FromResult(HideResult);
        }

        public PasteCommitResult CommitIfCurrent(
            PastePanelSession session,
            Func<PasteCommitResult> commit)
        {
            calls.Add("commit-boundary");
            BeforeCommit?.Invoke(this);
            return IsCurrentResult ? commit() : PasteCommitResult.StaleSession;
        }
    }

    private sealed class SpyForegroundWindow(List<string> calls, nint currentWindow) : IForegroundWindowController
    {
        private nint _currentWindow = currentWindow;

        public bool ActivateResult { get; init; } = true;

        public bool MoveTargetToForegroundOnActivate { get; init; }

        public bool SameWindowResult { get; init; } = true;

        public nint GetForegroundWindow()
        {
            calls.Add("foreground");
            return _currentWindow;
        }

        public bool TryActivate(nint targetWindow)
        {
            calls.Add("activate");
            if (ActivateResult && MoveTargetToForegroundOnActivate)
            {
                _currentWindow = targetWindow;
            }

            return ActivateResult;
        }

        public bool IsSameWindow(PasteTargetWindow targetWindow)
        {
            calls.Add("identity");
            return SameWindowResult;
        }
    }

    private sealed class SpyPasteCommitter(
        List<string> calls,
        PasteCommitResult result) : IPasteCommitter
    {
        public int CommitCount { get; private set; }

        public PasteCommitResult TryCommitPaste(
            PasteTargetWindow targetWindow,
            uint expectedSequenceNumber)
        {
            calls.Add("commit");
            CommitCount++;
            return result;
        }
    }

    private sealed class ImmediateDelay : IPasteDelay
    {
        public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }
}

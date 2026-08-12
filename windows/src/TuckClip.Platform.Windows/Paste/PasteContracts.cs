namespace TuckClip.Platform.Windows.Paste;

public readonly record struct ClipboardWriteReceipt(uint SequenceNumber);

public sealed class ClipboardWriteConflictException : IOException
{
    public ClipboardWriteConflictException(string message)
        : base(message)
    {
    }

    public ClipboardWriteConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public readonly record struct PasteTargetWindow(nint Handle, uint ThreadId, uint ProcessId)
{
    public bool IsAvailable => Handle != 0 && ThreadId != 0 && ProcessId != 0;

    public static PasteTargetWindow Unavailable { get; } = new(nint.Zero, 0, 0);
}

public interface IClipboardWriteOperation
{
    ValueTask<ClipboardWriteReceipt> WriteAsync(CancellationToken cancellationToken);
}

public interface IPanelSessionBoundary
{
    bool IsCurrent(PastePanelSession session);

    ValueTask<bool> HideAsync(PastePanelSession session, CancellationToken cancellationToken);

    PasteCommitResult CommitIfCurrent(
        PastePanelSession session,
        Func<PasteCommitResult> commit);
}

public interface IClipboardSequenceReader
{
    uint GetSequenceNumber();
}

public interface IForegroundWindowController
{
    nint GetForegroundWindow();

    bool TryActivate(nint targetWindow);

    bool IsSameWindow(PasteTargetWindow targetWindow);
}

public interface IPasteCommitter
{
    PasteCommitResult TryCommitPaste(PasteTargetWindow targetWindow, uint expectedSequenceNumber);
}

public interface IPasteDelay
{
    ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public enum PasteResultKind
{
    Pasted,
    CopiedOnly,
    Cancelled,
}

public enum PasteFailureReason
{
    None,
    SessionCancelled,
    RequestCancelled,
    StalePanelSession,
    TargetWindowUnavailable,
    ClipboardChanged,
    ModifierKeysPressed,
    InputRejected,
}

public enum PasteCommitResult
{
    Pasted,
    StaleSession,
    RequestCancelled,
    TargetWindowUnavailable,
    ClipboardChanged,
    ModifierKeysPressed,
    InputRejected,
}

public readonly record struct PasteResult(
    PasteResultKind Kind,
    PasteFailureReason FailureReason)
{
    public bool WasCopied => Kind is PasteResultKind.Pasted or PasteResultKind.CopiedOnly;

    public bool WasPasted => Kind is PasteResultKind.Pasted;

    public static PasteResult Pasted { get; } = new(PasteResultKind.Pasted, PasteFailureReason.None);

    public static PasteResult CopiedOnly(PasteFailureReason reason) =>
        new(PasteResultKind.CopiedOnly, reason);

    public static PasteResult Cancelled(PasteFailureReason reason) =>
        new(PasteResultKind.Cancelled, reason);
}

public sealed record PasteRetryOptions
{
    public int ForegroundConfirmationAttempts { get; init; } = 8;

    public TimeSpan ForegroundConfirmationInterval { get; init; } = TimeSpan.FromMilliseconds(35);

    public TimeSpan InputSettlementDelay { get; init; } = TimeSpan.FromMilliseconds(60);
}

public sealed class SystemPasteDelay : IPasteDelay
{
    public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay <= TimeSpan.Zero
            ? ValueTask.CompletedTask
            : new ValueTask(Task.Delay(delay, cancellationToken));
}

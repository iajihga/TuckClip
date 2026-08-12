namespace TuckClip.Platform.Windows.Paste;

public sealed class PasteService
{
    private readonly IPanelSessionBoundary _panelBoundary;
    private readonly IForegroundWindowController _foregroundWindow;
    private readonly IPasteCommitter _pasteCommitter;
    private readonly IPasteDelay _delay;
    private readonly PasteRetryOptions _options;

    public PasteService(
        IPanelSessionBoundary panelBoundary,
        IForegroundWindowController foregroundWindow,
        IPasteCommitter pasteCommitter,
        IPasteDelay? delay = null,
        PasteRetryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(panelBoundary);
        ArgumentNullException.ThrowIfNull(foregroundWindow);
        ArgumentNullException.ThrowIfNull(pasteCommitter);

        _panelBoundary = panelBoundary;
        _foregroundWindow = foregroundWindow;
        _pasteCommitter = pasteCommitter;
        _delay = delay ?? new SystemPasteDelay();
        _options = options ?? new PasteRetryOptions();

        if (_options.ForegroundConfirmationAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "At least one foreground confirmation attempt is required.");
        }

        if (_options.ForegroundConfirmationInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The foreground confirmation interval cannot be negative.");
        }

        if (_options.InputSettlementDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The input settlement delay cannot be negative.");
        }
    }

    public async ValueTask<PasteResult> PasteAsync(
        PastePanelSession session,
        IClipboardWriteOperation clipboardWrite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clipboardWrite);

        var clipboardWasWritten = false;
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            session.CancellationToken);
        var transactionToken = linkedCancellation.Token;

        try
        {
            transactionToken.ThrowIfCancellationRequested();

            // Copying is the durable first phase. Everything after this point is
            // best-effort automatic paste and must fail closed to copy-only.
            var receipt = await clipboardWrite.WriteAsync(transactionToken).ConfigureAwait(false);
            clipboardWasWritten = true;

            if (!_panelBoundary.IsCurrent(session))
            {
                return PasteResult.CopiedOnly(PasteFailureReason.StalePanelSession);
            }

            if (!await _panelBoundary.HideAsync(session, transactionToken).ConfigureAwait(false))
            {
                return PasteResult.CopiedOnly(PasteFailureReason.StalePanelSession);
            }

            transactionToken.ThrowIfCancellationRequested();
            if (!_panelBoundary.IsCurrent(session))
            {
                return PasteResult.CopiedOnly(PasteFailureReason.StalePanelSession);
            }

            if (!session.TargetWindow.IsAvailable ||
                !_foregroundWindow.IsSameWindow(session.TargetWindow))
            {
                return PasteResult.CopiedOnly(PasteFailureReason.TargetWindowUnavailable);
            }

            _ = _foregroundWindow.TryActivate(session.TargetWindow.Handle);
            if (!await ConfirmForegroundAsync(session, transactionToken).ConfigureAwait(false))
            {
                return PasteResult.CopiedOnly(PasteFailureReason.TargetWindowUnavailable);
            }

            if (_options.InputSettlementDelay > TimeSpan.Zero)
            {
                await _delay.DelayAsync(_options.InputSettlementDelay, transactionToken).ConfigureAwait(false);
            }

            transactionToken.ThrowIfCancellationRequested();
            var commitResult = _panelBoundary.CommitIfCurrent(
                session,
                () => transactionToken.IsCancellationRequested
                    ? PasteCommitResult.RequestCancelled
                    : _pasteCommitter.TryCommitPaste(session.TargetWindow, receipt.SequenceNumber));
            return commitResult switch
            {
                PasteCommitResult.Pasted => PasteResult.Pasted,
                PasteCommitResult.StaleSession =>
                    PasteResult.CopiedOnly(PasteFailureReason.StalePanelSession),
                PasteCommitResult.RequestCancelled =>
                    PasteResult.CopiedOnly(PasteFailureReason.RequestCancelled),
                PasteCommitResult.TargetWindowUnavailable =>
                    PasteResult.CopiedOnly(PasteFailureReason.TargetWindowUnavailable),
                PasteCommitResult.ClipboardChanged =>
                    PasteResult.CopiedOnly(PasteFailureReason.ClipboardChanged),
                PasteCommitResult.ModifierKeysPressed =>
                    PasteResult.CopiedOnly(PasteFailureReason.ModifierKeysPressed),
                _ => PasteResult.CopiedOnly(PasteFailureReason.InputRejected),
            };
        }
        catch (ClipboardWriteConflictException)
        {
            return PasteResult.Cancelled(PasteFailureReason.ClipboardChanged);
        }
        catch (OperationCanceledException) when (transactionToken.IsCancellationRequested)
        {
            var reason = session.IsCancellationRequested
                ? PasteFailureReason.SessionCancelled
                : PasteFailureReason.RequestCancelled;
            return clipboardWasWritten
                ? PasteResult.CopiedOnly(reason)
                : PasteResult.Cancelled(reason);
        }
    }

    private async ValueTask<bool> ConfirmForegroundAsync(
        PastePanelSession session,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < _options.ForegroundConfirmationAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_panelBoundary.IsCurrent(session))
            {
                return false;
            }

            if (!_foregroundWindow.IsSameWindow(session.TargetWindow))
            {
                return false;
            }

            if (_foregroundWindow.GetForegroundWindow() == session.TargetWindow.Handle)
            {
                return true;
            }

            if (attempt + 1 < _options.ForegroundConfirmationAttempts)
            {
                await _delay.DelayAsync(
                    _options.ForegroundConfirmationInterval,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return false;
    }
}

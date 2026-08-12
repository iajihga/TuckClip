namespace TuckClip.Platform.Windows.Paste;

public sealed class PastePanelSession : IDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly CancellationToken _token;
    private bool _isDisposed;

    public PastePanelSession(PasteTargetWindow targetWindow)
    {
        Id = Guid.NewGuid();
        TargetWindow = targetWindow;
        _token = _cancellation.Token;
    }

    public Guid Id { get; }

    public PasteTargetWindow TargetWindow { get; }

    public CancellationToken CancellationToken => _token;

    public bool IsCancellationRequested => _token.IsCancellationRequested;

    public void Cancel()
    {
        lock (_gate)
        {
            if (!_isDisposed)
            {
                _cancellation.Cancel();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _cancellation.Cancel();
            _cancellation.Dispose();
            _isDisposed = true;
        }
    }
}

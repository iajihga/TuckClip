namespace TuckClip.Platform.Windows.Interop;

public sealed class GlobalHotKeySwitcher : IDisposable
{
    private readonly object _gate = new();
    private readonly IWindowsNativeApi _nativeApi;
    private readonly nint _windowHandle;
    private readonly int _firstIdentifier;
    private readonly int _secondIdentifier;
    private GlobalHotKeyRegistration? _activeRegistration;
    private GlobalHotKey? _current;
    private int? _activeIdentifier;
    private bool _hasStagedChange;
    private bool _disposed;

    public GlobalHotKeySwitcher(
        IWindowsNativeApi nativeApi,
        nint windowHandle,
        int firstIdentifier,
        int secondIdentifier)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        if (firstIdentifier == secondIdentifier)
        {
            throw new ArgumentException("Hot key identifiers must be distinct.", nameof(secondIdentifier));
        }

        _nativeApi = nativeApi;
        _windowHandle = windowHandle;
        _firstIdentifier = firstIdentifier;
        _secondIdentifier = secondIdentifier;
    }

    public GlobalHotKey? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public GlobalHotKeyChange Stage(GlobalHotKey hotKey)
    {
        var normalized = hotKey.Validate();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_hasStagedChange)
            {
                throw new InvalidOperationException("A global hot key change is already pending.");
            }
            if (_current == normalized)
            {
                return GlobalHotKeyChange.NoOp;
            }

            var identifier = _activeIdentifier == _firstIdentifier
                ? _secondIdentifier
                : _firstIdentifier;
            var registration = GlobalHotKeyRegistration.Register(
                _nativeApi,
                _windowHandle,
                identifier,
                normalized.RegistrationModifiers,
                normalized.VirtualKey);
            _hasStagedChange = true;
            return new GlobalHotKeyChange(this, registration, identifier, normalized);
        }
    }

    public bool IsActiveIdentifier(int identifier)
    {
        lock (_gate)
        {
            return !_disposed && _activeIdentifier == identifier;
        }
    }

    public void Dispose()
    {
        GlobalHotKeyRegistration? registration;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            registration = _activeRegistration;
            _activeRegistration = null;
            _activeIdentifier = null;
            _current = null;
        }
        registration?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Commit(
        GlobalHotKeyRegistration registration,
        int identifier,
        GlobalHotKey hotKey)
    {
        GlobalHotKeyRegistration? previous;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_hasStagedChange)
            {
                throw new InvalidOperationException("The global hot key change is no longer pending.");
            }

            previous = _activeRegistration;
            _activeRegistration = registration;
            _activeIdentifier = identifier;
            _current = hotKey;
            _hasStagedChange = false;
        }
        previous?.Dispose();
    }

    private void Cancel(GlobalHotKeyRegistration registration)
    {
        lock (_gate)
        {
            _hasStagedChange = false;
        }
        registration.Dispose();
    }

    public sealed class GlobalHotKeyChange : IDisposable
    {
        internal static GlobalHotKeyChange NoOp { get; } = new();

        private GlobalHotKeySwitcher? _owner;
        private GlobalHotKeyRegistration? _registration;
        private readonly int _identifier;
        private readonly GlobalHotKey _hotKey;
        private bool _completed;

        private GlobalHotKeyChange()
        {
            _completed = true;
        }

        internal GlobalHotKeyChange(
            GlobalHotKeySwitcher owner,
            GlobalHotKeyRegistration registration,
            int identifier,
            GlobalHotKey hotKey)
        {
            _owner = owner;
            _registration = registration;
            _identifier = identifier;
            _hotKey = hotKey;
        }

        public void Commit()
        {
            if (_completed)
            {
                return;
            }

            var owner = _owner ?? throw new ObjectDisposedException(nameof(GlobalHotKeyChange));
            var registration = _registration ?? throw new ObjectDisposedException(nameof(GlobalHotKeyChange));
            owner.Commit(registration, _identifier, _hotKey);
            _registration = null;
            _owner = null;
            _completed = true;
        }

        public void Dispose()
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            var owner = _owner;
            var registration = _registration;
            _owner = null;
            _registration = null;
            if (owner is not null && registration is not null)
            {
                owner.Cancel(registration);
            }
        }
    }
}

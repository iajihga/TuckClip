using System.ComponentModel;
using System.Diagnostics;
using TuckClip.Platform.Windows.Interop;

namespace TuckClip.Platform.Windows.Clipboard;

public sealed record ClipboardSource(uint ProcessId, string ProcessName);

public interface IClipboardOwnerResolver
{
    ClipboardSource? TryGetCurrentSource();
}

public interface IProcessNameResolver
{
    string? TryGetProcessName(uint processId);
}

public sealed class ClipboardOwnerResolver : IClipboardOwnerResolver
{
    private readonly IWindowsNativeApi _nativeApi;
    private readonly IProcessNameResolver _processNames;

    public ClipboardOwnerResolver(
        IWindowsNativeApi nativeApi,
        IProcessNameResolver processNames)
    {
        ArgumentNullException.ThrowIfNull(nativeApi);
        ArgumentNullException.ThrowIfNull(processNames);
        _nativeApi = nativeApi;
        _processNames = processNames;
    }

    public ClipboardSource? TryGetCurrentSource()
    {
        var owner = _nativeApi.GetClipboardOwner();
        if (owner == 0 || _nativeApi.GetWindowThreadProcessId(owner, out var processId) == 0 || processId == 0)
        {
            return null;
        }

        var processName = _processNames.TryGetProcessName(processId);
        return new ClipboardSource(processId, processName?.Trim() ?? string.Empty);
    }
}

public sealed class SystemProcessNameResolver : IProcessNameResolver
{
    public string? TryGetProcessName(uint processId)
    {
        if (processId > int.MaxValue)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}

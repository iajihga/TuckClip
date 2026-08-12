using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.InteropServices;
using TuckClip.Platform.Windows.Clipboard;
using TuckClip.Platform.Windows.Interop;

namespace TuckClip.Platform.Windows.Tests;

[TestClass]
public sealed class InteropRegistrationTests
{
    [TestMethod]
    public void NativeInputMatchesWindowsInputStructureSize()
    {
        var expectedSize = IntPtr.Size == 8 ? 40 : 28;

        Assert.AreEqual(expectedSize, Marshal.SizeOf<NativeInput>());
    }

    [TestMethod]
    public void ClipboardListenerRegistrationUnregistersExactlyOnce()
    {
        var native = new SpyNativeApi();

        var registration = ClipboardListenerRegistration.Register(native, 101);
        registration.Dispose();
        registration.Dispose();

        Assert.AreEqual(1, native.AddListenerCalls);
        Assert.AreEqual(1, native.RemoveListenerCalls);
        Assert.AreEqual((nint)101, native.LastListenerWindow);
    }

    [TestMethod]
    public void GlobalHotKeyRegistrationUnregistersExactlyOnce()
    {
        var native = new SpyNativeApi();

        var registration = GlobalHotKeyRegistration.Register(
            native,
            202,
            7,
            HotKeyModifiers.Control | HotKeyModifiers.Shift | HotKeyModifiers.NoRepeat,
            0x56);
        registration.Dispose();
        registration.Dispose();

        Assert.AreEqual(1, native.RegisterHotKeyCalls);
        Assert.AreEqual(1, native.UnregisterHotKeyCalls);
        Assert.AreEqual(7, native.LastHotKeyIdentifier);
        Assert.AreEqual((uint)0x56, native.LastVirtualKey);
    }

    [TestMethod]
    public void GlobalHotKeySwitcherCommitsNewRegistrationBeforeRemovingOldOne()
    {
        var native = new SpyNativeApi();
        using var switcher = new GlobalHotKeySwitcher(native, 202, 11, 12);
        using (var initial = switcher.Stage(GlobalHotKey.Default))
        {
            initial.Commit();
        }

        var replacement = new GlobalHotKey(
            0x58,
            HotKeyModifiers.Control | HotKeyModifiers.Shift);
        using var change = switcher.Stage(replacement);

        Assert.AreEqual(GlobalHotKey.Default, switcher.Current);
        Assert.IsTrue(switcher.IsActiveIdentifier(11));
        Assert.HasCount(2, native.ActiveHotKeyIdentifiers);
        Assert.IsTrue(native.ActiveHotKeyIdentifiers.SetEquals([11, 12]));

        change.Commit();

        Assert.AreEqual(replacement, switcher.Current);
        Assert.IsTrue(switcher.IsActiveIdentifier(12));
        Assert.HasCount(1, native.ActiveHotKeyIdentifiers);
        Assert.Contains(12, native.ActiveHotKeyIdentifiers);
    }

    [TestMethod]
    public void GlobalHotKeySwitcherRegistrationFailureKeepsOldShortcutActive()
    {
        var native = new SpyNativeApi();
        using var switcher = new GlobalHotKeySwitcher(native, 202, 21, 22);
        using (var initial = switcher.Stage(GlobalHotKey.Default))
        {
            initial.Commit();
        }
        native.FailNextHotKeyRegistration = true;

        Assert.ThrowsExactly<System.ComponentModel.Win32Exception>(() => switcher.Stage(
            new GlobalHotKey(0x58, HotKeyModifiers.Control | HotKeyModifiers.Shift)));

        Assert.AreEqual(GlobalHotKey.Default, switcher.Current);
        Assert.IsTrue(switcher.IsActiveIdentifier(21));
        Assert.HasCount(1, native.ActiveHotKeyIdentifiers);
        Assert.Contains(21, native.ActiveHotKeyIdentifiers);
    }

    [TestMethod]
    public void DisposingStagedGlobalHotKeyChangeRollsBackWithoutGap()
    {
        var native = new SpyNativeApi();
        using var switcher = new GlobalHotKeySwitcher(native, 202, 31, 32);
        using (var initial = switcher.Stage(GlobalHotKey.Default))
        {
            initial.Commit();
        }

        using (switcher.Stage(new GlobalHotKey(
            0x42,
            HotKeyModifiers.Alt | HotKeyModifiers.Shift)))
        {
        }

        Assert.AreEqual(GlobalHotKey.Default, switcher.Current);
        Assert.IsTrue(switcher.IsActiveIdentifier(31));
        Assert.HasCount(1, native.ActiveHotKeyIdentifiers);
        Assert.Contains(31, native.ActiveHotKeyIdentifiers);
    }

    [TestMethod]
    public void ClipboardOwnerResolverMapsOwnerWindowToProcessSource()
    {
        var native = new SpyNativeApi
        {
            ClipboardOwner = 303,
            OwnerProcessId = 404,
        };
        var processNames = new SpyProcessNameResolver("notepad");
        var resolver = new ClipboardOwnerResolver(native, processNames);

        var source = resolver.TryGetCurrentSource();

        Assert.IsNotNull(source);
        Assert.AreEqual((uint)404, source.ProcessId);
        Assert.AreEqual("notepad", source.ProcessName);
    }

    [TestMethod]
    public void ClipboardOwnerResolverReturnsNullWhenThereIsNoOwner()
    {
        var native = new SpyNativeApi();
        var resolver = new ClipboardOwnerResolver(native, new SpyProcessNameResolver("unused"));

        Assert.IsNull(resolver.TryGetCurrentSource());
    }

    [TestMethod]
    public void ClipboardOwnerResolverPreservesOwnerIdentityWhenNameCannotBeResolved()
    {
        var native = new SpyNativeApi
        {
            ClipboardOwner = 303,
            OwnerProcessId = 404,
        };
        var resolver = new ClipboardOwnerResolver(native, new SpyProcessNameResolver(null));

        var source = resolver.TryGetCurrentSource();

        Assert.IsNotNull(source);
        Assert.AreEqual((uint)404, source.ProcessId);
        Assert.AreEqual(string.Empty, source.ProcessName);
    }

    private sealed class SpyProcessNameResolver(string? name) : IProcessNameResolver
    {
        public string? TryGetProcessName(uint processId) => name;
    }

    private sealed class SpyNativeApi : IWindowsNativeApi
    {
        public int AddListenerCalls { get; private set; }

        public int RemoveListenerCalls { get; private set; }

        public int RegisterHotKeyCalls { get; private set; }

        public int UnregisterHotKeyCalls { get; private set; }

        public nint LastListenerWindow { get; private set; }

        public int LastHotKeyIdentifier { get; private set; }

        public uint LastVirtualKey { get; private set; }

        public bool FailNextHotKeyRegistration { get; set; }

        public HashSet<int> ActiveHotKeyIdentifiers { get; } = [];

        public nint ClipboardOwner { get; init; }

        public uint OwnerProcessId { get; init; }

        public int LastError => 5;

        public bool AddClipboardFormatListener(nint windowHandle)
        {
            AddListenerCalls++;
            LastListenerWindow = windowHandle;
            return true;
        }

        public bool RemoveClipboardFormatListener(nint windowHandle)
        {
            RemoveListenerCalls++;
            LastListenerWindow = windowHandle;
            return true;
        }

        public bool RegisterHotKey(nint windowHandle, int identifier, uint modifiers, uint virtualKey)
        {
            RegisterHotKeyCalls++;
            LastHotKeyIdentifier = identifier;
            LastVirtualKey = virtualKey;
            if (FailNextHotKeyRegistration)
            {
                FailNextHotKeyRegistration = false;
                return false;
            }
            ActiveHotKeyIdentifiers.Add(identifier);
            return true;
        }

        public bool UnregisterHotKey(nint windowHandle, int identifier)
        {
            UnregisterHotKeyCalls++;
            LastHotKeyIdentifier = identifier;
            ActiveHotKeyIdentifiers.Remove(identifier);
            return true;
        }

        public uint GetClipboardSequenceNumber() => 0;

        public nint GetForegroundWindow() => 0;

        public bool SetForegroundWindow(nint windowHandle) => false;

        public nint GetClipboardOwner() => ClipboardOwner;

        public uint GetWindowThreadProcessId(nint windowHandle, out uint processIdentifier)
        {
            processIdentifier = OwnerProcessId;
            return OwnerProcessId == 0 ? 0u : 1u;
        }

        public bool SendPasteShortcut() => false;
    }
}

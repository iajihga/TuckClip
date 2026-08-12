using System.Runtime.InteropServices;

namespace TuckClip.Platform.Windows.Interop;

internal static partial class NativeMethods
{
    internal const ushort VirtualKeyControl = 0x11;
    internal const int VirtualKeyShift = 0x10;
    internal const int VirtualKeyMenu = 0x12;
    internal const int VirtualKeyLeftWindows = 0x5B;
    internal const int VirtualKeyRightWindows = 0x5C;
    internal const ushort VirtualKeyV = 0x56;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AddClipboardFormatListener(nint windowHandle);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RemoveClipboardFormatListener(nint windowHandle);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool RegisterHotKey(
        nint windowHandle,
        int identifier,
        uint modifiers,
        uint virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnregisterHotKey(nint windowHandle, int identifier);

    [LibraryImport("user32.dll")]
    internal static partial uint GetClipboardSequenceNumber();

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint windowHandle);

    [LibraryImport("user32.dll")]
    internal static partial nint GetClipboardOwner();

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processIdentifier);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static unsafe partial uint SendInput(
        uint inputCount,
        NativeInput* inputs,
        int inputSize);
}

internal enum NativeInputType : uint
{
    Keyboard = 1,
}

[Flags]
internal enum NativeKeyEventFlags : uint
{
    None = 0,
    KeyUp = 0x0002,
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeInput
{
    internal NativeInputType Type;
    internal NativeInputUnion Value;

    internal static NativeInput Key(ushort virtualKey, NativeKeyEventFlags flags) => new()
    {
        Type = NativeInputType.Keyboard,
        Value = new NativeInputUnion
        {
            Keyboard = new NativeKeyboardInput
            {
                VirtualKey = virtualKey,
                Flags = flags,
            },
        },
    };
}

[StructLayout(LayoutKind.Explicit)]
internal struct NativeInputUnion
{
    [FieldOffset(0)]
    internal NativeKeyboardInput Keyboard;

    // INPUT's native union is sized by MOUSEINPUT even when SendInput only
    // receives keyboard events. Keeping this field is required so cbSize is
    // 40 bytes on 64-bit Windows (and 28 bytes on 32-bit Windows).
    [FieldOffset(0)]
    internal NativeMouseInput Mouse;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeKeyboardInput
{
    internal ushort VirtualKey;
    internal ushort ScanCode;
    internal NativeKeyEventFlags Flags;
    internal uint Time;
    internal nuint ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMouseInput
{
    internal int X;
    internal int Y;
    internal uint MouseData;
    internal uint Flags;
    internal uint Time;
    internal nuint ExtraInfo;
}

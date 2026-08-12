using Avalonia.Input;
using Avalonia.Platform.Storage;
using TuckClip.Platform.Windows.Clipboard;
using TuckClip.Platform.Windows.Paste;
using TuckClip.Windows.Services;

namespace TuckClip.Windows.Ui.Tests;

[TestClass]
public sealed class ClipboardCaptureSnapshotTests
{
    [TestMethod]
    public async Task CaptureAcceptsTextOnlyWhenSequenceAndOwnerStayStable()
    {
        var sequence = new MutableSequenceReader(10);
        var owner = new MutableOwnerResolver(new ClipboardSource(7, "notepad"));
        var clipboard = new SnapshotClipboard(CreateTextTransfer("stable text"));
        var adapter = CreateAdapter(clipboard, sequence, owner);

        var capture = await adapter.TryReadCaptureAsync(new WindowsAppSettings());

        Assert.IsNotNull(capture);
        Assert.AreEqual("stable text", capture.PlainText);
        Assert.AreEqual("notepad", capture.SourceAppName);
    }

    [TestMethod]
    public async Task CaptureRejectsClipboardThatChangesDuringRead()
    {
        var sequence = new MutableSequenceReader(20);
        var owner = new MutableOwnerResolver(new ClipboardSource(8, "notepad"));
        var clipboard = new SnapshotClipboard(CreateTextTransfer("stale text"))
        {
            AfterRead = () =>
            {
                sequence.Increment();
                owner.Source = new ClipboardSource(9, "wordpad");
            },
        };
        var adapter = CreateAdapter(clipboard, sequence, owner);

        var capture = await adapter.TryReadCaptureAsync(new WindowsAppSettings());

        Assert.IsNull(capture);
    }

    [TestMethod]
    public async Task CaptureRejectsSequenceChangeWhenOwnerStaysStable()
    {
        var sequence = new MutableSequenceReader(21);
        var owner = new MutableOwnerResolver(new ClipboardSource(8, "notepad"));
        var clipboard = new SnapshotClipboard(CreateTextTransfer("stale text"))
        {
            AfterRead = sequence.Increment,
        };
        var adapter = CreateAdapter(clipboard, sequence, owner);

        var capture = await adapter.TryReadCaptureAsync(new WindowsAppSettings());

        Assert.IsNull(capture);
        Assert.AreEqual(new ClipboardSource(8, "notepad"), owner.Source);
    }

    [TestMethod]
    public async Task CaptureRejectsOwnerChangeWhenSequenceStaysStable()
    {
        var sequence = new MutableSequenceReader(22);
        var owner = new MutableOwnerResolver(new ClipboardSource(8, "notepad"));
        var clipboard = new SnapshotClipboard(CreateTextTransfer("wrong owner"))
        {
            AfterRead = () => owner.Source = new ClipboardSource(9, "wordpad"),
        };
        var adapter = CreateAdapter(clipboard, sequence, owner);

        var capture = await adapter.TryReadCaptureAsync(new WindowsAppSettings());

        Assert.IsNull(capture);
        Assert.AreEqual((uint)22, sequence.Value);
    }

    [TestMethod]
    public async Task CaptureRejectsFinalOwnerThatDisappears()
    {
        var sequence = new MutableSequenceReader(23);
        var owner = new MutableOwnerResolver(new ClipboardSource(8, "notepad"));
        var clipboard = new SnapshotClipboard(CreateTextTransfer("owner disappeared"))
        {
            AfterRead = () => owner.Source = null,
        };
        var adapter = CreateAdapter(clipboard, sequence, owner);

        var capture = await adapter.TryReadCaptureAsync(new WindowsAppSettings());

        Assert.IsNull(capture);
        Assert.AreEqual((uint)23, sequence.Value);
    }

    [TestMethod]
    public async Task CaptureRejectsFinalOwnerWhoseProcessNameCannotBeResolved()
    {
        var sequence = new MutableSequenceReader(24);
        var owner = new MutableOwnerResolver(new ClipboardSource(8, "notepad"));
        var clipboard = new SnapshotClipboard(CreateTextTransfer("owner unresolved"))
        {
            AfterRead = () => owner.Source = new ClipboardSource(8, string.Empty),
        };
        var adapter = CreateAdapter(clipboard, sequence, owner);

        var capture = await adapter.TryReadCaptureAsync(new WindowsAppSettings());

        Assert.IsNull(capture);
        Assert.AreEqual((uint)24, sequence.Value);
    }

    [TestMethod]
    public async Task CaptureRejectsOwnerWhoseProcessNameCannotBeResolved()
    {
        var sequence = new MutableSequenceReader(30);
        var owner = new MutableOwnerResolver(new ClipboardSource(10, string.Empty));
        var clipboard = new SnapshotClipboard(CreateTextTransfer("private text"));
        var adapter = CreateAdapter(clipboard, sequence, owner);

        var capture = await adapter.TryReadCaptureAsync(new WindowsAppSettings());

        Assert.IsNull(capture);
    }

    private static AvaloniaClipboardAdapter CreateAdapter(
        IClipboardFacade clipboard,
        IClipboardSequenceReader sequence,
        IClipboardOwnerResolver owner) =>
        new(clipboard, new UnusedStorageProvider(), sequence, owner);

    private static DataTransfer CreateTextTransfer(string text)
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(text));
        return transfer;
    }

    private sealed class MutableSequenceReader(uint value) : IClipboardSequenceReader
    {
        public uint Value { get; private set; } = value;

        public uint GetSequenceNumber() => Value;

        public void Increment() => Value++;
    }

    private sealed class MutableOwnerResolver(ClipboardSource? source) : IClipboardOwnerResolver
    {
        public ClipboardSource? Source { get; set; } = source;

        public ClipboardSource? TryGetCurrentSource() => Source;
    }

    private sealed class SnapshotClipboard(IAsyncDataTransfer transfer) : IClipboardFacade
    {
        public Action? AfterRead { get; init; }

        public Task SetDataAsync(IAsyncDataTransfer dataTransfer) => throw new NotSupportedException();

        public Task FlushAsync() => throw new NotSupportedException();

        public Task<IAsyncDataTransfer?> TryGetDataAsync()
        {
            AfterRead?.Invoke();
            return Task.FromResult<IAsyncDataTransfer?>(transfer);
        }

        public Task<IAsyncDataTransfer?> TryGetInProcessDataAsync() =>
            Task.FromResult<IAsyncDataTransfer?>(null);
    }

    private sealed class UnusedStorageProvider : IStorageItemResolver
    {
        public Task<IStorageFile?> TryGetFileFromPathAsync(Uri filePath) =>
            throw new NotSupportedException();

        public Task<IStorageFolder?> TryGetFolderFromPathAsync(Uri folderPath) =>
            throw new NotSupportedException();

    }
}

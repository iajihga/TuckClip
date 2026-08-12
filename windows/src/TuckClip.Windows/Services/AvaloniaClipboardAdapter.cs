using System.Buffers.Binary;
using System.Security.Cryptography;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using TuckClip.Core;
using TuckClip.Platform.Windows.Clipboard;
using TuckClip.Platform.Windows.Paste;

namespace TuckClip.Windows.Services;

public sealed class AvaloniaClipboardAdapter
{
    private const int MaximumDecodedImageBytes = 128 * 1024 * 1024;
    private static readonly DataFormat<byte[]> InternalFormat =
        DataFormat.CreateBytesPlatformFormat(WindowsClipboardFormats.TuckClipInternalWrite);
    private static readonly DataFormat<byte[]> WriteReceiptFormat =
        DataFormat.CreateBytesPlatformFormat(WindowsClipboardFormats.TuckClipWriteReceiptV1);
    private static readonly DataFormat<byte[]> ExcludeFromMonitorFormat =
        DataFormat.CreateBytesPlatformFormat(WindowsClipboardFormats.ExcludeFromMonitorProcessing);
    private static readonly DataFormat<byte[]> CanIncludeInHistoryFormat =
        DataFormat.CreateBytesPlatformFormat(WindowsClipboardFormats.CanIncludeInClipboardHistory);
    private static readonly DataFormat<byte[]> CanUploadToCloudFormat =
        DataFormat.CreateBytesPlatformFormat(WindowsClipboardFormats.CanUploadToCloudClipboard);

    private readonly IClipboardFacade _clipboard;
    private readonly IStorageItemResolver _storageProvider;
    private readonly IClipboardSequenceReader _sequenceReader;
    private readonly IClipboardOwnerResolver _ownerResolver;

    public AvaloniaClipboardAdapter(
        IClipboard clipboard,
        IStorageProvider storageProvider,
        IClipboardSequenceReader sequenceReader,
        IClipboardOwnerResolver ownerResolver)
        : this(
            new AvaloniaClipboardFacade(clipboard),
            new AvaloniaStorageItemResolver(storageProvider),
            sequenceReader,
            ownerResolver)
    {
    }

    internal AvaloniaClipboardAdapter(
        IClipboardFacade clipboard,
        IStorageItemResolver storageProvider,
        IClipboardSequenceReader sequenceReader,
        IClipboardOwnerResolver ownerResolver)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _sequenceReader = sequenceReader ?? throw new ArgumentNullException(nameof(sequenceReader));
        _ownerResolver = ownerResolver ?? throw new ArgumentNullException(nameof(ownerResolver));
    }

    public async Task<ClipboardCapture?> TryReadCaptureAsync(
        WindowsAppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var initialSequence = _sequenceReader.GetSequenceNumber();
        var initialSource = _ownerResolver.TryGetCurrentSource();
        if (initialSource is null ||
            string.IsNullOrWhiteSpace(initialSource.ProcessName) ||
            _sequenceReader.GetSequenceNumber() != initialSequence)
        {
            return null;
        }

        using var transfer = await _clipboard.TryGetDataAsync().ConfigureAwait(true);
        if (transfer is null)
        {
            return null;
        }

        var markers = await ClipboardFormatSnapshot.CreateAsync(transfer, cancellationToken)
            .ConfigureAwait(true);
        if (markers.Contains(WindowsClipboardFormats.TuckClipInternalWrite))
        {
            return null;
        }

        var capturePolicy = new ClipboardCapturePolicy(settings.ExcludedProcessNames);
        if (!capturePolicy.Evaluate(markers, initialSource?.ProcessName).ShouldCapture)
        {
            return null;
        }

        var files = await TryReadFilesAsync(transfer, cancellationToken).ConfigureAwait(true);
        byte[]? image = null;
        string? text = null;
        if (files.Count == 0 && settings.CapturesImages)
        {
            image = await TryReadImageAsync(transfer, cancellationToken).ConfigureAwait(true);
        }

        if (files.Count == 0 && image is null)
        {
            text = await TryReadTextAsync(transfer, cancellationToken).ConfigureAwait(true);
        }

        if (files.Count == 0 && image is null && text is null)
        {
            return null;
        }

        if (_sequenceReader.GetSequenceNumber() != initialSequence)
        {
            return null;
        }

        var finalSource = _ownerResolver.TryGetCurrentSource();
        if (_sequenceReader.GetSequenceNumber() != initialSequence ||
            !SourcesMatch(initialSource, finalSource))
        {
            return null;
        }

        return new ClipboardCapture
        {
            PlainText = text,
            FilePaths = files,
            ImageData = image,
            CapturedAt = DateTimeOffset.UtcNow,
            SourceAppName = initialSource?.ProcessName,
            SourceIdentifier = initialSource?.ProcessName,
            ExcludeFromMonitorProcessing = markers.Contains(WindowsClipboardFormats.ExcludeFromMonitorProcessing),
            CanIncludeInClipboardHistory = !markers.IsZeroOrUnreadable(
                WindowsClipboardFormats.CanIncludeInClipboardHistory),
            CanUploadToCloudClipboard = !markers.IsZeroOrUnreadable(
                WindowsClipboardFormats.CanUploadToCloudClipboard),
        };
    }

    private static bool SourcesMatch(ClipboardSource? left, ClipboardSource? right) =>
        left is null
            ? right is null
            : right is not null &&
                left.ProcessId == right.ProcessId &&
                string.Equals(left.ProcessName, right.ProcessName, StringComparison.OrdinalIgnoreCase);

    public IClipboardWriteOperation CreateWriteOperation(ClipItem item, bool asPlainText)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new ClipboardWriteOperation(
            _clipboard,
            _storageProvider,
            _sequenceReader,
            item,
            asPlainText);
    }

    private static async Task<IReadOnlyList<string>> TryReadFilesAsync(
        IAsyncDataTransfer transfer,
        CancellationToken cancellationToken)
    {
        if (!transfer.Formats.Contains(DataFormat.File))
        {
            return Array.Empty<string>();
        }

        var paths = new List<string>();
        foreach (var item in transfer.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!item.Formats.Contains(DataFormat.File))
            {
                continue;
            }

            if (await item.TryGetRawAsync(DataFormat.File).ConfigureAwait(true) is IStorageItem storageItem &&
                storageItem.Path.IsFile)
            {
                paths.Add(storageItem.Path.LocalPath);
            }
        }

        return paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<byte[]?> TryReadImageAsync(
        IAsyncDataTransfer transfer,
        CancellationToken cancellationToken)
    {
        if (!transfer.Formats.Contains(DataFormat.Bitmap))
        {
            return null;
        }

        foreach (var item in transfer.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!item.Formats.Contains(DataFormat.Bitmap) ||
                await item.TryGetRawAsync(DataFormat.Bitmap).ConfigureAwait(true) is not Bitmap bitmap)
            {
                continue;
            }

            using (bitmap)
            {
                var estimatedDecodedBytes = checked((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4);
                if (estimatedDecodedBytes > MaximumDecodedImageBytes)
                {
                    return null;
                }

                await using var stream = new MemoryStream();
                bitmap.Save(stream, PngBitmapEncoderOptions.Default);
                return stream.Length <= CapturePolicy.MaximumBinaryOrFileListBytes
                    ? stream.ToArray()
                    : null;
            }
        }

        return null;
    }

    private static async Task<string?> TryReadTextAsync(
        IAsyncDataTransfer transfer,
        CancellationToken cancellationToken)
    {
        if (!transfer.Formats.Contains(DataFormat.Text))
        {
            return null;
        }

        foreach (var item in transfer.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Formats.Contains(DataFormat.Text) &&
                await item.TryGetRawAsync(DataFormat.Text).ConfigureAwait(true) is string value)
            {
                return value;
            }
        }

        return null;
    }

    internal sealed class ClipboardWriteOperation : IClipboardWriteOperation
    {
        private const int WriteReceiptMagicSize = 8;
        private const int WriteReceiptLengthSize = sizeof(int);
        private const int WriteNonceSize = 32;
        private static readonly byte[] EnabledMarker = BitConverter.GetBytes(1);
        private static readonly byte[] DisabledMarker = BitConverter.GetBytes(0);

        private readonly IClipboardFacade _clipboard;
        private readonly IStorageItemResolver _storageProvider;
        private readonly IClipboardSequenceReader _sequenceReader;
        private readonly ClipItem _item;
        private readonly bool _asPlainText;

        public ClipboardWriteOperation(
            IClipboardFacade clipboard,
            IStorageItemResolver storageProvider,
            IClipboardSequenceReader sequenceReader,
            ClipItem item,
            bool asPlainText)
        {
            _clipboard = clipboard;
            _storageProvider = storageProvider;
            _sequenceReader = sequenceReader;
            _item = item;
            _asPlainText = asPlainText;
        }

        public async ValueTask<ClipboardWriteReceipt> WriteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var writeReceipt = CreateWriteReceipt();
            var transfer = await CreateTransferAsync(writeReceipt, cancellationToken).ConfigureAwait(true);
            var ownershipTransferred = false;
            try
            {
                // File resolution can yield for an arbitrary amount of time.
                // A superseded panel session must not begin a new clipboard
                // write after preparation finally completes.
                cancellationToken.ThrowIfCancellationRequested();
                await _clipboard.SetDataAsync(transfer).ConfigureAwait(true);
                ownershipTransferred = true;
                var writtenSequence = _sequenceReader.GetSequenceNumber();
                if (writtenSequence == 0 ||
                    !ReferenceEquals(await _clipboard.TryGetInProcessDataAsync().ConfigureAwait(true), transfer) ||
                    _sequenceReader.GetSequenceNumber() != writtenSequence)
                {
                    throw new ClipboardWriteConflictException(
                        "The clipboard changed before the TuckClip write could be verified.");
                }

                await _clipboard.FlushAsync().ConfigureAwait(true);
                var verificationSequence = _sequenceReader.GetSequenceNumber();
                if (verificationSequence == 0 ||
                    !await ClipboardContainsWriteReceiptAsync(writeReceipt).ConfigureAwait(true) ||
                    _sequenceReader.GetSequenceNumber() != verificationSequence)
                {
                    throw new ClipboardWriteConflictException(
                        "Another application replaced the clipboard while TuckClip was copying.");
                }

                return new ClipboardWriteReceipt(verificationSequence);
            }
            finally
            {
                if (!ownershipTransferred)
                {
                    ((IAsyncDataTransfer)transfer).Dispose();
                }
            }
        }

        private async Task<DataTransfer> CreateTransferAsync(
            byte[] writeReceipt,
            CancellationToken cancellationToken)
        {
            var transfer = new DataTransfer();
            if (_asPlainText || _item.Kind is ClipKind.Text or ClipKind.Link)
            {
                if (_item.PlainText is null)
                {
                    throw new InvalidDataException("The selected item does not contain text.");
                }

                var textItem = DataTransferItem.CreateText(_item.PlainText);
                AddPrivacyMarkers(textItem, writeReceipt);
                transfer.Add(textItem);
                return transfer;
            }

            if (_item.Kind == ClipKind.Image)
            {
                if (_item.ImageData is not { Length: > 0 } imageData)
                {
                    throw new InvalidDataException("The selected item does not contain image data.");
                }

                using var stream = new MemoryStream(imageData, writable: false);
                var bitmap = new Bitmap(stream);
                var imageItem = new DataTransferItem();
                imageItem.SetBitmap(bitmap);
                AddPrivacyMarkers(imageItem, writeReceipt);
                transfer.Add(imageItem);
                return transfer;
            }

            if (_item.Kind == ClipKind.Files)
            {
                DataTransferItem? firstItem = null;
                foreach (var path in _item.FilePaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var uri = new Uri(Path.GetFullPath(path));
                    IStorageItem? storageItem = File.Exists(path)
                        ? await _storageProvider.TryGetFileFromPathAsync(uri).ConfigureAwait(true)
                        : await _storageProvider.TryGetFolderFromPathAsync(uri).ConfigureAwait(true);
                    if (storageItem is null)
                    {
                        continue;
                    }

                    var dataItem = DataTransferItem.CreateFile(storageItem);
                    firstItem ??= dataItem;
                    transfer.Add(dataItem);
                }

                if (firstItem is null)
                {
                    throw new FileNotFoundException("None of the selected file paths still exist.");
                }

                AddPrivacyMarkers(firstItem, writeReceipt);
                return transfer;
            }

            throw new InvalidDataException("The selected clipboard item kind is unsupported.");
        }

        private async Task<bool> ClipboardContainsWriteReceiptAsync(byte[] expectedReceipt)
        {
            using var snapshot = await _clipboard.TryGetDataAsync().ConfigureAwait(true);
            if (snapshot is null || !snapshot.Formats.Contains(WriteReceiptFormat))
            {
                return false;
            }

            var foundMatchingReceipt = false;
            foreach (var item in snapshot.Items)
            {
                if (!item.Formats.Contains(WriteReceiptFormat))
                {
                    continue;
                }

                var receipt = await item.TryGetRawAsync(WriteReceiptFormat).ConfigureAwait(true);
                if (!WriteReceiptMatches(receipt, expectedReceipt))
                {
                    return false;
                }

                foundMatchingReceipt = true;
            }

            return foundMatchingReceipt;
        }

        private static byte[] CreateWriteReceipt()
        {
            var receipt = new byte[WriteReceiptMagicSize + WriteReceiptLengthSize + WriteNonceSize];
            "TCKWRCP1"u8.CopyTo(receipt);
            BinaryPrimitives.WriteInt32LittleEndian(
                receipt.AsSpan(WriteReceiptMagicSize, WriteReceiptLengthSize),
                WriteNonceSize);
            RandomNumberGenerator.Fill(receipt.AsSpan(WriteReceiptMagicSize + WriteReceiptLengthSize));
            return receipt;
        }

        private static bool WriteReceiptMatches(object? raw, ReadOnlySpan<byte> expected)
        {
            ReadOnlySpan<byte> actual = raw switch
            {
                byte[] bytes => bytes,
                ReadOnlyMemory<byte> memory => memory.Span,
                _ => default,
            };

            if (actual.Length < expected.Length ||
                !CryptographicOperations.FixedTimeEquals(actual[..expected.Length], expected))
            {
                return false;
            }

            foreach (var paddingByte in actual[expected.Length..])
            {
                if (paddingByte != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddPrivacyMarkers(DataTransferItem item, byte[] writeReceipt)
        {
            item.Set(InternalFormat, EnabledMarker);
            item.Set(WriteReceiptFormat, writeReceipt);
            item.Set(ExcludeFromMonitorFormat, EnabledMarker);
            item.Set(CanIncludeInHistoryFormat, DisabledMarker);
            item.Set(CanUploadToCloudFormat, DisabledMarker);
        }
    }

    private sealed class ClipboardFormatSnapshot : IClipboardFormatReader
    {
        private static readonly IReadOnlyDictionary<string, DataFormat<byte[]>> KnownFormats =
            new Dictionary<string, DataFormat<byte[]>>(StringComparer.Ordinal)
            {
                [WindowsClipboardFormats.TuckClipInternalWrite] = InternalFormat,
                [WindowsClipboardFormats.ExcludeFromMonitorProcessing] = ExcludeFromMonitorFormat,
                [WindowsClipboardFormats.CanIncludeInClipboardHistory] = CanIncludeInHistoryFormat,
                [WindowsClipboardFormats.CanUploadToCloudClipboard] = CanUploadToCloudFormat,
            };

        private readonly HashSet<string> _present = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _values = new(StringComparer.Ordinal);

        public bool Contains(string formatName) => _present.Contains(formatName);

        public bool TryReadInt32(string formatName, out int value) =>
            _values.TryGetValue(formatName, out value);

        public bool IsZeroOrUnreadable(string formatName) =>
            Contains(formatName) && (!TryReadInt32(formatName, out var value) || value == 0);

        public static async Task<ClipboardFormatSnapshot> CreateAsync(
            IAsyncDataTransfer transfer,
            CancellationToken cancellationToken)
        {
            var snapshot = new ClipboardFormatSnapshot();
            foreach (var (name, format) in KnownFormats)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!transfer.Formats.Contains(format))
                {
                    continue;
                }

                snapshot._present.Add(name);
                foreach (var item in transfer.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!item.Formats.Contains(format))
                    {
                        continue;
                    }

                    var raw = await item.TryGetRawAsync(format).ConfigureAwait(true);
                    if (TryConvertInt32(raw, out var value))
                    {
                        snapshot._values[name] = value;
                    }
                    break;
                }
            }

            return snapshot;
        }

        private static bool TryConvertInt32(object? raw, out int value)
        {
            switch (raw)
            {
                case int signed:
                    value = signed;
                    return true;
                case uint unsigned when unsigned <= int.MaxValue:
                    value = (int)unsigned;
                    return true;
                case byte[] bytes when bytes.Length >= sizeof(int):
                    value = BinaryPrimitives.ReadInt32LittleEndian(bytes);
                    return true;
                case ReadOnlyMemory<byte> memory when memory.Length >= sizeof(int):
                    value = BinaryPrimitives.ReadInt32LittleEndian(memory.Span);
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }
    }
}

internal interface IClipboardFacade
{
    Task SetDataAsync(IAsyncDataTransfer dataTransfer);

    Task FlushAsync();

    Task<IAsyncDataTransfer?> TryGetDataAsync();

    Task<IAsyncDataTransfer?> TryGetInProcessDataAsync();
}

internal sealed class AvaloniaClipboardFacade : IClipboardFacade
{
    private readonly IClipboard _clipboard;

    internal AvaloniaClipboardFacade(IClipboard clipboard)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
    }

    public Task SetDataAsync(IAsyncDataTransfer dataTransfer) =>
        _clipboard.SetDataAsync(dataTransfer);

    public Task FlushAsync() => _clipboard.FlushAsync();

    public Task<IAsyncDataTransfer?> TryGetDataAsync() => _clipboard.TryGetDataAsync();

    public Task<IAsyncDataTransfer?> TryGetInProcessDataAsync() =>
        _clipboard.TryGetInProcessDataAsync();
}

internal interface IStorageItemResolver
{
    Task<IStorageFile?> TryGetFileFromPathAsync(Uri filePath);

    Task<IStorageFolder?> TryGetFolderFromPathAsync(Uri folderPath);
}

internal sealed class AvaloniaStorageItemResolver : IStorageItemResolver
{
    private readonly IStorageProvider _storageProvider;

    internal AvaloniaStorageItemResolver(IStorageProvider storageProvider)
    {
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
    }

    public Task<IStorageFile?> TryGetFileFromPathAsync(Uri filePath) =>
        _storageProvider.TryGetFileFromPathAsync(filePath);

    public Task<IStorageFolder?> TryGetFolderFromPathAsync(Uri folderPath) =>
        _storageProvider.TryGetFolderFromPathAsync(folderPath);
}

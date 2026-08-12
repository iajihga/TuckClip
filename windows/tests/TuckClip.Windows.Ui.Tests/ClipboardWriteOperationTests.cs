using System.Buffers.Binary;
using Avalonia.Input;
using TuckClip.Core;
using TuckClip.Platform.Windows.Clipboard;
using TuckClip.Platform.Windows.Paste;
using TuckClip.Windows.Services;

namespace TuckClip.Windows.Ui.Tests;

[TestClass]
public sealed class ClipboardWriteOperationTests
{
    [TestMethod]
    public void WriteReceiptPlatformFormatIsBinaryFromColdStart()
    {
        var field = typeof(AvaloniaClipboardAdapter).GetField(
            "WriteReceiptFormat",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.IsNotNull(field);
        Assert.AreEqual(typeof(DataFormat<byte[]>), field.FieldType);
        var format = (DataFormat<byte[]>)field.GetValue(null)!;
        Assert.AreEqual(WindowsClipboardFormats.TuckClipWriteReceiptV1, format.Identifier);
        Assert.AreEqual(DataFormatKind.Platform, format.Kind);
    }

    [TestMethod]
    public async Task WriteAsyncAcceptsMatchingReceiptAfterFlushReleasesInProcessOwner()
    {
        var sequence = new MutableSequenceReader(10);
        var clipboard = new FakeClipboard(sequence);
        var operation = CreateOperation(clipboard, sequence);

        var receipt = await operation.WriteAsync(CancellationToken.None);

        Assert.AreEqual((uint)12, receipt.SequenceNumber);
        Assert.IsNull(clipboard.InProcessData);
        Assert.AreEqual(44, clipboard.PersistedWriteReceiptLength);
        Assert.IsTrue(clipboard.PersistedWriteReceiptIsByteArray);
        Assert.IsTrue(clipboard.PersistedWriteReceiptHasExpectedEnvelope);
        Assert.AreEqual(1, clipboard.SystemReadCount);
    }

    [TestMethod]
    public async Task WriteAsyncAcceptsReceiptWithHGlobalZeroPadding()
    {
        var sequence = new MutableSequenceReader(11);
        var clipboard = new FakeClipboard(sequence)
        {
            DuringFlush = static instance => instance.AppendZeroPaddingToReceipt(16),
        };
        var operation = CreateOperation(clipboard, sequence);

        var receipt = await operation.WriteAsync(CancellationToken.None);

        Assert.AreEqual((uint)13, receipt.SequenceNumber);
        Assert.AreEqual(60, clipboard.PersistedWriteReceiptLength);
    }

    [TestMethod]
    public async Task WriteAsyncRejectsTruncatedBinaryReceipt()
    {
        var sequence = new MutableSequenceReader(16);
        var clipboard = new FakeClipboard(sequence)
        {
            DuringFlush = static instance => instance.TruncateReceipt(),
        };
        var operation = CreateOperation(clipboard, sequence);

        await Assert.ThrowsExactlyAsync<ClipboardWriteConflictException>(
            () => operation.WriteAsync(CancellationToken.None).AsTask());
    }

    [TestMethod]
    public async Task WriteAsyncRejectsReceiptWithNonzeroTrailingByte()
    {
        var sequence = new MutableSequenceReader(17);
        var clipboard = new FakeClipboard(sequence)
        {
            DuringFlush = static instance => instance.AppendNonzeroByteToReceipt(),
        };
        var operation = CreateOperation(clipboard, sequence);

        await Assert.ThrowsExactlyAsync<ClipboardWriteConflictException>(
            () => operation.WriteAsync(CancellationToken.None).AsTask());
    }

    [TestMethod]
    public async Task WriteAsyncRejectsReceiptWithWrongMagic()
    {
        var sequence = new MutableSequenceReader(18);
        var clipboard = new FakeClipboard(sequence)
        {
            DuringFlush = static instance => instance.CorruptReceiptMagic(),
        };
        var operation = CreateOperation(clipboard, sequence);

        await Assert.ThrowsExactlyAsync<ClipboardWriteConflictException>(
            () => operation.WriteAsync(CancellationToken.None).AsTask());
    }

    [TestMethod]
    public async Task WriteAsyncRejectsReceiptWithWrongNonceLength()
    {
        var sequence = new MutableSequenceReader(19);
        var clipboard = new FakeClipboard(sequence)
        {
            DuringFlush = static instance => instance.CorruptReceiptNonceLength(),
        };
        var operation = CreateOperation(clipboard, sequence);

        await Assert.ThrowsExactlyAsync<ClipboardWriteConflictException>(
            () => operation.WriteAsync(CancellationToken.None).AsTask());
    }

    [TestMethod]
    public async Task WriteAsyncAcceptsMatchingReceiptOnNonFirstSystemSnapshotItem()
    {
        var sequence = new MutableSequenceReader(12);
        var clipboard = new FakeClipboard(sequence)
        {
            PrefixSnapshotWithItemWithoutReceipt = true,
        };
        var operation = CreateOperation(clipboard, sequence);

        var receipt = await operation.WriteAsync(CancellationToken.None);

        Assert.AreEqual((uint)14, receipt.SequenceNumber);
        Assert.IsNull(clipboard.InProcessData);
        Assert.AreEqual(1, clipboard.SystemReadCount);
    }

    [TestMethod]
    public async Task WriteAsyncRejectsSnapshotContainingMatchingAndConflictingReceipts()
    {
        var sequence = new MutableSequenceReader(14);
        var clipboard = new FakeClipboard(sequence)
        {
            AppendConflictingReceiptItem = true,
        };
        var operation = CreateOperation(clipboard, sequence);

        await Assert.ThrowsExactlyAsync<ClipboardWriteConflictException>(
            () => operation.WriteAsync(CancellationToken.None).AsTask());

        Assert.IsNull(clipboard.InProcessData);
        Assert.AreEqual(1, clipboard.SystemReadCount);
    }

    [TestMethod]
    public async Task WriteAsyncAcceptsStableNonzeroSequenceWhenReceiptMatches()
    {
        var sequence = new MutableSequenceReader(15);
        var clipboard = new FakeClipboard(sequence)
        {
            IncrementSequenceOnSet = false,
            IncrementSequenceOnFlush = false,
        };
        var operation = CreateOperation(clipboard, sequence);

        var receipt = await operation.WriteAsync(CancellationToken.None);

        Assert.AreEqual((uint)15, receipt.SequenceNumber);
        Assert.IsNull(clipboard.InProcessData);
        Assert.AreEqual(44, clipboard.PersistedWriteReceiptLength);
    }

    [TestMethod]
    public async Task WriteAsyncRejectsExternalOverwriteImmediatelyAfterSet()
    {
        var sequence = new MutableSequenceReader(20);
        var clipboard = new FakeClipboard(sequence)
        {
            AfterSet = static instance => instance.ReplaceWithExternalData(),
        };
        var operation = CreateOperation(clipboard, sequence);

        await Assert.ThrowsExactlyAsync<ClipboardWriteConflictException>(
            () => operation.WriteAsync(CancellationToken.None).AsTask());
    }

    [TestMethod]
    public async Task WriteAsyncRejectsExternalOverwriteDuringFlushWhenReceiptDoesNotMatch()
    {
        var sequence = new MutableSequenceReader(30);
        var clipboard = new FakeClipboard(sequence)
        {
            DuringFlush = static instance => instance.ReplaceWithExternalReceipt(),
        };
        var operation = CreateOperation(clipboard, sequence);

        await Assert.ThrowsExactlyAsync<ClipboardWriteConflictException>(
            () => operation.WriteAsync(CancellationToken.None).AsTask());
    }

    [TestMethod]
    public async Task WriteAsyncRejectsLostOwnershipWhenSequenceStaysStable()
    {
        var sequence = new MutableSequenceReader(35);
        var clipboard = new FakeClipboard(sequence)
        {
            AfterSet = static instance => instance.LoseOwnership(),
        };
        var operation = CreateOperation(clipboard, sequence);

        await Assert.ThrowsExactlyAsync<ClipboardWriteConflictException>(
            () => operation.WriteAsync(CancellationToken.None).AsTask());
    }

    [TestMethod]
    public async Task WriteAsyncRejectsSequenceChangeDuringOwnershipQuery()
    {
        var sequence = new MutableSequenceReader(37);
        var clipboard = new FakeClipboard(sequence)
        {
            DuringNextOwnershipRead = sequence.Increment,
        };
        var operation = CreateOperation(clipboard, sequence);

        await Assert.ThrowsExactlyAsync<ClipboardWriteConflictException>(
            () => operation.WriteAsync(CancellationToken.None).AsTask());
    }

    [TestMethod]
    public async Task WriteAsyncRejectsSequenceChangeDuringSystemSnapshotVerification()
    {
        var sequence = new MutableSequenceReader(38);
        var clipboard = new FakeClipboard(sequence)
        {
            DuringNextSystemRead = sequence.Increment,
        };
        var operation = CreateOperation(clipboard, sequence);

        await Assert.ThrowsExactlyAsync<ClipboardWriteConflictException>(
            () => operation.WriteAsync(CancellationToken.None).AsTask());

        Assert.AreEqual(1, clipboard.SystemReadCount);
    }

    [TestMethod]
    public async Task WriteAsyncRejectsStaticTuckClipMarkerWithDifferentNonceReceipt()
    {
        var sequence = new MutableSequenceReader(39);
        var clipboard = new FakeClipboard(sequence)
        {
            DuringFlush = static instance => instance.ReplaceWithDifferentTuckClipWrite(),
        };
        var operation = CreateOperation(clipboard, sequence);

        await Assert.ThrowsExactlyAsync<ClipboardWriteConflictException>(
            () => operation.WriteAsync(CancellationToken.None).AsTask());

        Assert.AreEqual(1, clipboard.PersistedInternalMarkerValue);
        Assert.AreEqual(44, clipboard.PersistedWriteReceiptLength);
        Assert.AreEqual(1, clipboard.SystemReadCount);
    }

    [TestMethod]
    public async Task WriteAsyncRejectsZeroSequenceAfterSet()
    {
        var sequence = new MutableSequenceReader(0);
        var clipboard = new FakeClipboard(sequence)
        {
            IncrementSequenceOnSet = false,
        };
        var operation = CreateOperation(clipboard, sequence);

        await Assert.ThrowsExactlyAsync<ClipboardWriteConflictException>(
            () => operation.WriteAsync(CancellationToken.None).AsTask());

        Assert.AreEqual(0, clipboard.FlushCount);
    }

    [TestMethod]
    public async Task WriteAsyncRejectsZeroSequenceAtFlushVerificationStart()
    {
        var sequence = new MutableSequenceReader(41);
        var clipboard = new FakeClipboard(sequence)
        {
            DuringFlush = _ => sequence.SetValue(0),
        };
        var operation = CreateOperation(clipboard, sequence);

        await Assert.ThrowsExactlyAsync<ClipboardWriteConflictException>(
            () => operation.WriteAsync(CancellationToken.None).AsTask());

        Assert.AreEqual(0, clipboard.SystemReadCount);
    }

    [TestMethod]
    public async Task WriteAsyncRejectsZeroSequenceAtFlushVerificationEnd()
    {
        var sequence = new MutableSequenceReader(42);
        var clipboard = new FakeClipboard(sequence)
        {
            DuringNextSystemRead = () => sequence.SetValue(0),
        };
        var operation = CreateOperation(clipboard, sequence);

        await Assert.ThrowsExactlyAsync<ClipboardWriteConflictException>(
            () => operation.WriteAsync(CancellationToken.None).AsTask());

        Assert.AreEqual(1, clipboard.SystemReadCount);
    }

    [TestMethod]
    public async Task WriteAsyncDoesNotStartWriteWhenAlreadyCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var sequence = new MutableSequenceReader(40);
        var clipboard = new FakeClipboard(sequence);
        var operation = CreateOperation(clipboard, sequence);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => operation.WriteAsync(cancellation.Token).AsTask());

        Assert.AreEqual(0, clipboard.SetCount);
    }

    [TestMethod]
    public async Task WriteAsyncFinishesReceiptWhenCancelledAfterOwnershipTransfers()
    {
        using var cancellation = new CancellationTokenSource();
        var sequence = new MutableSequenceReader(50);
        var clipboard = new FakeClipboard(sequence)
        {
            AfterSet = _ => cancellation.Cancel(),
        };
        var operation = CreateOperation(clipboard, sequence);

        var receipt = await operation.WriteAsync(cancellation.Token);

        Assert.AreEqual((uint)52, receipt.SequenceNumber);
        Assert.AreEqual(1, clipboard.SetCount);
        Assert.AreEqual(1, clipboard.FlushCount);
    }

    private static AvaloniaClipboardAdapter.ClipboardWriteOperation CreateOperation(
        IClipboardFacade clipboard,
        IClipboardSequenceReader sequence) =>
        new(
            clipboard,
            null!,
            sequence,
            new ClipItem
            {
                Id = Guid.NewGuid(),
                Kind = ClipKind.Text,
                PlainText = "safe text",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Fingerprint = new string('0', 64),
            },
            asPlainText: false);

    private sealed class MutableSequenceReader(uint value) : IClipboardSequenceReader
    {
        public uint Value { get; private set; } = value;

        public Action? AfterNextRead { get; init; }

        public uint GetSequenceNumber()
        {
            var current = Value;
            AfterNextRead?.Invoke();
            return current;
        }

        public void Increment() => Value++;

        public void SetValue(uint value) => Value = value;
    }

    private sealed class FakeClipboard(MutableSequenceReader sequence) : IClipboardFacade
    {
        private static readonly DataFormat<byte[]> InternalFormat =
            DataFormat.CreateBytesPlatformFormat(WindowsClipboardFormats.TuckClipInternalWrite);
        private static readonly DataFormat<byte[]> WriteReceiptFormat =
            DataFormat.CreateBytesPlatformFormat(WindowsClipboardFormats.TuckClipWriteReceiptV1);

        private byte[]? _persistedInternalMarker;
        private byte[]? _persistedWriteReceipt;

        public Action<FakeClipboard>? AfterSet { get; init; }

        public Action<FakeClipboard>? DuringFlush { get; init; }

        public Action? DuringNextOwnershipRead { get; init; }

        public Action? DuringNextSystemRead { get; init; }

        public IAsyncDataTransfer? InProcessData { get; private set; }

        public int PersistedInternalMarkerValue => _persistedInternalMarker is { Length: >= sizeof(int) }
            ? BitConverter.ToInt32(_persistedInternalMarker)
            : 0;

        public int PersistedWriteReceiptLength => _persistedWriteReceipt?.Length ?? 0;

        public bool PersistedWriteReceiptIsByteArray => _persistedWriteReceipt is byte[];

        public bool PersistedWriteReceiptHasExpectedEnvelope =>
            _persistedWriteReceipt is { Length: 44 } receipt &&
            receipt.AsSpan(0, 8).SequenceEqual("TCKWRCP1"u8) &&
            BinaryPrimitives.ReadInt32LittleEndian(receipt.AsSpan(8, sizeof(int))) == 32;

        public bool IncrementSequenceOnSet { get; init; } = true;

        public bool IncrementSequenceOnFlush { get; init; } = true;

        public bool PrefixSnapshotWithItemWithoutReceipt { get; init; }

        public bool AppendConflictingReceiptItem { get; init; }

        public int SetCount { get; private set; }

        public int FlushCount { get; private set; }

        public int SystemReadCount { get; private set; }

        public Task SetDataAsync(IAsyncDataTransfer dataTransfer)
        {
            SetCount++;
            InProcessData = dataTransfer;
            if (IncrementSequenceOnSet)
            {
                sequence.Increment();
            }

            AfterSet?.Invoke(this);
            return Task.CompletedTask;
        }

        public async Task FlushAsync()
        {
            FlushCount++;
            var ownedTransfer = InProcessData;
            _persistedInternalMarker = ownedTransfer is null
                ? null
                : await ReadInternalMarkerAsync(ownedTransfer);
            _persistedWriteReceipt = ownedTransfer is null
                ? null
                : await ReadWriteReceiptAsync(ownedTransfer);
            InProcessData = null;
            ownedTransfer?.Dispose();
            if (IncrementSequenceOnFlush)
            {
                sequence.Increment();
            }

            DuringFlush?.Invoke(this);
        }

        public Task<IAsyncDataTransfer?> TryGetDataAsync()
        {
            SystemReadCount++;
            IAsyncDataTransfer? snapshot = CreateSystemSnapshot();
            DuringNextSystemRead?.Invoke();
            return Task.FromResult(snapshot);
        }

        public Task<IAsyncDataTransfer?> TryGetInProcessDataAsync()
        {
            var current = InProcessData;
            DuringNextOwnershipRead?.Invoke();
            return Task.FromResult(current);
        }

        public void LoseOwnership() => InProcessData = null;

        public void ReplaceWithExternalData()
        {
            InProcessData = null;
            _persistedInternalMarker = null;
            _persistedWriteReceipt = null;
            sequence.Increment();
        }

        public void ReplaceWithExternalReceipt()
        {
            InProcessData = null;
            _persistedInternalMarker = null;
            _persistedWriteReceipt = CreateDifferentReceipt();
            sequence.Increment();
        }

        public void ReplaceWithDifferentTuckClipWrite()
        {
            InProcessData = null;
            _persistedInternalMarker = BitConverter.GetBytes(1);
            _persistedWriteReceipt = CreateDifferentReceipt();
            sequence.Increment();
        }

        public void AppendZeroPaddingToReceipt(int byteCount)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(byteCount);
            var padded = new byte[RequirePersistedReceipt().Length + byteCount];
            RequirePersistedReceipt().CopyTo(padded, 0);
            _persistedWriteReceipt = padded;
        }

        public void TruncateReceipt()
        {
            var receipt = RequirePersistedReceipt();
            _persistedWriteReceipt = receipt[..^1];
        }

        public void AppendNonzeroByteToReceipt()
        {
            var receipt = RequirePersistedReceipt();
            var extended = new byte[receipt.Length + 1];
            receipt.CopyTo(extended, 0);
            extended[^1] = 1;
            _persistedWriteReceipt = extended;
        }

        public void CorruptReceiptMagic()
        {
            _persistedWriteReceipt = RequirePersistedReceipt().ToArray();
            _persistedWriteReceipt[0] ^= 0xff;
        }

        public void CorruptReceiptNonceLength()
        {
            _persistedWriteReceipt = RequirePersistedReceipt().ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(_persistedWriteReceipt.AsSpan(8, sizeof(int)), 31);
        }

        private DataTransfer? CreateSystemSnapshot()
        {
            if (_persistedInternalMarker is null && _persistedWriteReceipt is null)
            {
                return null;
            }

            var snapshot = new DataTransfer();
            if (PrefixSnapshotWithItemWithoutReceipt)
            {
                snapshot.Add(DataTransferItem.CreateText("unrelated item"));
            }

            var item = new DataTransferItem();
            if (_persistedInternalMarker is not null)
            {
                item.Set(InternalFormat, _persistedInternalMarker.ToArray());
            }

            if (_persistedWriteReceipt is not null)
            {
                item.Set(WriteReceiptFormat, _persistedWriteReceipt.ToArray());
            }

            snapshot.Add(item);
            if (AppendConflictingReceiptItem)
            {
                var conflictingItem = new DataTransferItem();
                conflictingItem.Set(WriteReceiptFormat, CreateDifferentReceipt());
                snapshot.Add(conflictingItem);
            }

            return snapshot;
        }

        private byte[] CreateDifferentReceipt()
        {
            var receipt = RequirePersistedReceipt().ToArray();
            receipt[12] ^= 0xff;
            return receipt;
        }

        private byte[] RequirePersistedReceipt() =>
            _persistedWriteReceipt ?? throw new InvalidOperationException();

        private static async Task<byte[]?> ReadInternalMarkerAsync(IAsyncDataTransfer transfer)
        {
            foreach (var item in transfer.Items)
            {
                if (!item.Formats.Contains(InternalFormat))
                {
                    continue;
                }

                return await item.TryGetRawAsync(InternalFormat) switch
                {
                    byte[] bytes => bytes.ToArray(),
                    ReadOnlyMemory<byte> memory => memory.ToArray(),
                    _ => null,
                };
            }

            return null;
        }

        private static async Task<byte[]?> ReadWriteReceiptAsync(IAsyncDataTransfer transfer)
        {
            foreach (var item in transfer.Items)
            {
                if (!item.Formats.Contains(WriteReceiptFormat))
                {
                    continue;
                }

                return await item.TryGetRawAsync(WriteReceiptFormat) switch
                {
                    byte[] bytes => bytes.ToArray(),
                    ReadOnlyMemory<byte> memory => memory.ToArray(),
                    _ => null,
                };
            }

            return null;
        }
    }
}

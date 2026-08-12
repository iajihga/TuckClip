import AppKit
import Foundation

struct PasteboardWriteReceipt: Equatable, Sendable {
    let changeCount: Int
}

enum PasteboardWriteError: LocalizedError, Equatable {
    case missingText
    case missingImageData
    case missingFilePaths
    case itemTooLarge
    case unsupportedImageData
    case pasteboardRejectedWrite

    var errorDescription: String? {
        switch self {
        case .missingText:
            return L10n.text("这条记录已没有可复制的文本")
        case .missingImageData:
            return L10n.text("这条记录的图片文件已丢失")
        case .missingFilePaths:
            return L10n.text("这条记录已没有可复制的文件路径")
        case .itemTooLarge:
            return L10n.text("这条记录超过对应类型的安全上限")
        case .unsupportedImageData:
            return L10n.text("保存的图片无法转换为 PNG")
        case .pasteboardRejectedWrite:
            return L10n.text("macOS 拒绝写入系统剪贴板")
        }
    }
}

/// Reconstructs portable pasteboard items from TuckClip's stored model.
@MainActor
final class PasteboardWriter {
    private let pasteboard: NSPasteboard

    init(pasteboard: NSPasteboard = .general) {
        self.pasteboard = pasteboard
    }

    /// `ClipItem` stores image metadata only; callers provide image bytes loaded
    /// by `HistoryRepository` when restoring an image clip.
    @discardableResult
    func write(
        _ item: ClipItem,
        imageData: Data? = nil
    ) throws -> PasteboardWriteReceipt {
        try write(
            kind: item.kind,
            plainText: item.plainText,
            filePaths: item.filePaths,
            imageData: imageData
        )
    }

    @discardableResult
    func write(
        _ capture: ClipboardCapture
    ) throws -> PasteboardWriteReceipt {
        try write(
            kind: capture.kind,
            plainText: capture.plainText,
            filePaths: capture.filePaths,
            imageData: capture.imageData
        )
    }

    @discardableResult
    func write(
        kind: ClipKind,
        plainText: String? = nil,
        filePaths: [String] = [],
        imageData: Data? = nil
    ) throws -> PasteboardWriteReceipt {
        let items: [NSPasteboardItem]

        switch kind {
        case .text:
            items = [try makeTextItem(plainText, includeURLType: false)]
        case .link:
            items = [try makeTextItem(plainText, includeURLType: true)]
        case .image:
            items = [try makeImageItem(imageData)]
        case .files:
            items = try makeFileItems(filePaths)
        }

        guard let firstItem = items.first else {
            throw PasteboardWriteError.pasteboardRejectedWrite
        }
        guard firstItem.setString(
            UUID().uuidString,
            forType: PasteboardReader.internalMarkerType
        ) else {
            throw PasteboardWriteError.pasteboardRejectedWrite
        }

        pasteboard.clearContents()
        guard pasteboard.writeObjects(items) else {
            throw PasteboardWriteError.pasteboardRejectedWrite
        }
        return PasteboardWriteReceipt(changeCount: pasteboard.changeCount)
    }

    private func makeTextItem(
        _ text: String?,
        includeURLType: Bool
    ) throws -> NSPasteboardItem {
        guard let text, !text.isEmpty else {
            throw PasteboardWriteError.missingText
        }
        guard text.lengthOfBytes(using: .utf8) <= AppSettings.maximumTextCaptureSizeBytes else {
            throw PasteboardWriteError.itemTooLarge
        }

        let item = NSPasteboardItem()
        guard item.setString(text, forType: .string) else {
            throw PasteboardWriteError.pasteboardRejectedWrite
        }

        if includeURLType,
           let url = URL(string: text.trimmingCharacters(in: .whitespacesAndNewlines)),
           !url.isFileURL {
            item.setString(url.absoluteString, forType: .URL)
        }
        return item
    }

    private func makeImageItem(_ data: Data?) throws -> NSPasteboardItem {
        guard let data, !data.isEmpty else {
            throw PasteboardWriteError.missingImageData
        }
        guard data.count <= AppSettings.maximumCaptureSizeBytes else {
            throw PasteboardWriteError.itemTooLarge
        }

        let pngData: Data
        if data.starts(with: [0x89, 0x50, 0x4E, 0x47]) {
            pngData = data
        } else {
            guard let image = NSImage(data: data),
                  let tiffData = image.tiffRepresentation,
                  let bitmap = NSBitmapImageRep(data: tiffData),
                  let convertedData = bitmap.representation(using: .png, properties: [:]) else {
                throw PasteboardWriteError.unsupportedImageData
            }
            pngData = convertedData
        }

        guard pngData.count <= AppSettings.maximumCaptureSizeBytes else {
            throw PasteboardWriteError.itemTooLarge
        }

        let item = NSPasteboardItem()
        guard item.setData(pngData, forType: .png) else {
            throw PasteboardWriteError.pasteboardRejectedWrite
        }
        return item
    }

    private func makeFileItems(_ filePaths: [String]) throws -> [NSPasteboardItem] {
        let normalizedPaths = filePaths
            .filter { !$0.isEmpty }
            .map { URL(fileURLWithPath: $0).standardizedFileURL.path }

        guard !normalizedPaths.isEmpty else {
            throw PasteboardWriteError.missingFilePaths
        }

        let byteCount = normalizedPaths.reduce(into: 0) { total, path in
            total += path.lengthOfBytes(using: .utf8) + 1
        }
        guard byteCount <= AppSettings.maximumCaptureSizeBytes else {
            throw PasteboardWriteError.itemTooLarge
        }

        return try normalizedPaths.map { path in
            let item = NSPasteboardItem()
            let value = URL(fileURLWithPath: path).absoluteString
            guard item.setString(value, forType: .fileURL) else {
                throw PasteboardWriteError.pasteboardRejectedWrite
            }
            return item
        }
    }
}

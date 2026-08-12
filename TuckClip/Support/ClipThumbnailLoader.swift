import Combine
import CoreGraphics
import Foundation
import ImageIO

enum ClipThumbnailLoadState: Equatable {
    case idle
    case loading
    case loaded
    case unavailable
}

/// Thread-safe, process-local cache for already downsampled image previews.
/// Cached images are immutable and bounded by the renderer's pixel limit.
final class ClipThumbnailCache: @unchecked Sendable {
    static let shared = ClipThumbnailCache()

    private let storage = NSCache<NSString, ClipThumbnailBox>()

    init(
        countLimit: Int = 160,
        totalCostLimit: Int = 64 * 1024 * 1024
    ) {
        storage.countLimit = countLimit
        storage.totalCostLimit = totalCostLimit
    }

    func image(forKey key: String) -> CGImage? {
        storage.object(forKey: key as NSString)?.image
    }

    func insert(_ image: CGImage, forKey key: String) {
        let cost: Int
        if image.height > 0, image.bytesPerRow <= Int.max / image.height {
            cost = image.bytesPerRow * image.height
        } else {
            cost = 0
        }
        storage.setObject(
            ClipThumbnailBox(image: image),
            forKey: key as NSString,
            cost: cost
        )
    }
}

private final class ClipThumbnailBox {
    let image: CGImage

    init(image: CGImage) {
        self.image = image
    }
}

enum ClipThumbnailRenderer {
    enum RenderError: Error {
        case unreadableImage
    }

    static func render(
        url: URL,
        maximumPixelSize: Int
    ) throws -> CGImage {
        try Task.checkCancellation()
        let data = try Data(contentsOf: url, options: .mappedIfSafe)
        try Task.checkCancellation()

        guard !data.isEmpty,
              let source = CGImageSourceCreateWithData(
                  data as CFData,
                  [kCGImageSourceShouldCache: false] as CFDictionary
              ) else {
            throw RenderError.unreadableImage
        }

        let options: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceThumbnailMaxPixelSize: maximumPixelSize,
            kCGImageSourceShouldCacheImmediately: true
        ]
        guard let thumbnail = CGImageSourceCreateThumbnailAtIndex(
            source,
            0,
            options as CFDictionary
        ) else {
            throw RenderError.unreadableImage
        }

        try Task.checkCancellation()
        return thumbnail
    }
}

/// Loads a single card preview without performing file I/O or decoding from a
/// SwiftUI body. Each request has an identity so a cancelled or superseded task
/// cannot publish a late image into a reused card.
@MainActor
final class ClipThumbnailLoader: ObservableObject {
    typealias Renderer = @Sendable (URL, Int) throws -> CGImage

    nonisolated static let defaultMaximumPixelSize = 512

    @Published private(set) var image: CGImage?
    @Published private(set) var state: ClipThumbnailLoadState = .idle

    private let maximumPixelSize: Int
    private let cache: ClipThumbnailCache
    private let renderer: Renderer
    private var activeRequestID: UUID?

    init(
        maximumPixelSize: Int = ClipThumbnailLoader.defaultMaximumPixelSize,
        cache: ClipThumbnailCache = .shared,
        renderer: @escaping Renderer = { url, maximumPixelSize in
            try ClipThumbnailRenderer.render(
                url: url,
                maximumPixelSize: maximumPixelSize
            )
        }
    ) {
        self.maximumPixelSize = max(1, maximumPixelSize)
        self.cache = cache
        self.renderer = renderer
    }

    func load(from url: URL?) async {
        let requestID = UUID()
        activeRequestID = requestID
        image = nil

        guard let url else {
            // `HistoryRepository.imageURL` returns nil when the blob has already
            // disappeared, so an image card with no URL is a missing-file state.
            state = .unavailable
            return
        }

        let cacheKey = "\(url.standardizedFileURL.path)#\(maximumPixelSize)"
        if let cachedImage = cache.image(forKey: cacheKey) {
            image = cachedImage
            state = .loaded
            return
        }

        state = .loading
        let renderer = self.renderer
        let maximumPixelSize = self.maximumPixelSize
        let worker = Task.detached(priority: .userInitiated) {
            try renderer(url, maximumPixelSize)
        }

        do {
            let renderedImage = try await withTaskCancellationHandler {
                try await worker.value
            } onCancel: {
                worker.cancel()
            }
            try Task.checkCancellation()
            guard activeRequestID == requestID else { return }

            cache.insert(renderedImage, forKey: cacheKey)
            image = renderedImage
            state = .loaded
        } catch is CancellationError {
            guard activeRequestID == requestID else { return }
            image = nil
            state = .idle
        } catch {
            guard activeRequestID == requestID else { return }
            image = nil
            state = .unavailable
        }
    }
}

import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers
import XCTest
@testable import TuckClip

@MainActor
final class ClipThumbnailLoaderTests: XCTestCase {
    func testDownsamplesAndCachesThumbnail() async throws {
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("ClipThumbnailTests-\(UUID().uuidString)", isDirectory: true)
        let imageURL = directory.appendingPathComponent("large.png")
        try FileManager.default.createDirectory(
            at: directory,
            withIntermediateDirectories: true
        )
        defer { try? FileManager.default.removeItem(at: directory) }
        try writeTestPNG(width: 1_600, height: 800, to: imageURL)

        let counter = LockedCounter()
        let cache = ClipThumbnailCache(countLimit: 4, totalCostLimit: 8 * 1024 * 1024)
        let renderer: ClipThumbnailLoader.Renderer = { url, maximumPixelSize in
            counter.increment()
            return try ClipThumbnailRenderer.render(
                url: url,
                maximumPixelSize: maximumPixelSize
            )
        }
        let firstLoader = ClipThumbnailLoader(cache: cache, renderer: renderer)
        await firstLoader.load(from: imageURL)

        let firstImage = try XCTUnwrap(firstLoader.image)
        XCTAssertEqual(firstLoader.state, .loaded)
        XCTAssertLessThanOrEqual(max(firstImage.width, firstImage.height), 512)
        XCTAssertEqual(firstImage.width, 512)
        XCTAssertEqual(firstImage.height, 256)

        let secondLoader = ClipThumbnailLoader(cache: cache, renderer: renderer)
        await secondLoader.load(from: imageURL)

        XCTAssertEqual(secondLoader.state, .loaded)
        XCTAssertNotNil(secondLoader.image)
        XCTAssertEqual(counter.value, 1, "The second loader should reuse the downsampled cache entry")
    }

    func testMissingFileFallsBackToUnavailableState() async {
        let loader = ClipThumbnailLoader(cache: ClipThumbnailCache())
        let missingURL = FileManager.default.temporaryDirectory
            .appendingPathComponent("missing-\(UUID().uuidString).png")

        await loader.load(from: missingURL)

        XCTAssertNil(loader.image)
        XCTAssertEqual(loader.state, .unavailable)

        await loader.load(from: nil)
        XCTAssertNil(loader.image)
        XCTAssertEqual(loader.state, .unavailable)
    }

    func testCancellationDiscardsLateThumbnail() async throws {
        let cache = ClipThumbnailCache()
        let renderer: ClipThumbnailLoader.Renderer = { _, _ in
            Thread.sleep(forTimeInterval: 0.15)
            try Task.checkCancellation()
            throw ClipThumbnailRenderer.RenderError.unreadableImage
        }
        let loader = ClipThumbnailLoader(cache: cache, renderer: renderer)
        let task = Task { @MainActor in
            await loader.load(
                from: FileManager.default.temporaryDirectory
                    .appendingPathComponent("cancelled.png")
            )
        }

        try await Task.sleep(for: .milliseconds(20))
        task.cancel()
        await task.value

        XCTAssertNil(loader.image)
        XCTAssertEqual(loader.state, .idle)
    }

    private func writeTestPNG(width: Int, height: Int, to url: URL) throws {
        let colorSpace = CGColorSpaceCreateDeviceRGB()
        let bitmapInfo = CGImageAlphaInfo.premultipliedLast.rawValue
        let context = try XCTUnwrap(CGContext(
            data: nil,
            width: width,
            height: height,
            bitsPerComponent: 8,
            bytesPerRow: 0,
            space: colorSpace,
            bitmapInfo: bitmapInfo
        ))
        context.setFillColor(CGColor(red: 0.1, green: 0.5, blue: 0.9, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        let image = try XCTUnwrap(context.makeImage())
        let destination = try XCTUnwrap(CGImageDestinationCreateWithURL(
            url as CFURL,
            UTType.png.identifier as CFString,
            1,
            nil
        ))
        CGImageDestinationAddImage(destination, image, nil)
        XCTAssertTrue(CGImageDestinationFinalize(destination))
    }
}

private final class LockedCounter: @unchecked Sendable {
    private let lock = NSLock()
    private var storedValue = 0

    var value: Int {
        lock.withLock { storedValue }
    }

    func increment() {
        lock.withLock { storedValue += 1 }
    }
}

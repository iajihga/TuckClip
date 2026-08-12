import Carbon.HIToolbox
import Combine
import Foundation

enum HotKeyRegistrationError: LocalizedError, Equatable {
    case eventHandlerInstallationFailed(OSStatus)
    case registrationFailed(OSStatus)

    var errorDescription: String? {
        switch self {
        case .eventHandlerInstallationFailed(let status):
            return L10n.format("无法安装全局快捷键处理器（OSStatus %d）", Int(status))
        case .registrationFailed(let status):
            if status == eventHotKeyExistsErr {
                return L10n.text("已被系统或其他应用占用")
            }
            return L10n.format("无法注册全局快捷键（OSStatus %d）", Int(status))
        }
    }
}

@MainActor
protocol HotKeySystemControlling: AnyObject {
    func installHandlerIfNeeded(for manager: HotKeyManager) throws
    func register(_ hotKey: GlobalHotKey, identifier: UInt32) throws -> HotKeyRegistrationToken
    func removeHandler()
}

@MainActor
final class HotKeyRegistrationToken {
    private var cancellation: (() -> Void)?

    init(cancellation: @escaping () -> Void) {
        self.cancellation = cancellation
    }

    func cancel() {
        cancellation?()
        cancellation = nil
    }
}

@MainActor
private final class CarbonHotKeySystem: HotKeySystemControlling {
    private var eventHandlerRef: EventHandlerRef?

    func installHandlerIfNeeded(for manager: HotKeyManager) throws {
        guard eventHandlerRef == nil else { return }

        var eventType = EventTypeSpec(
            eventClass: OSType(kEventClassKeyboard),
            eventKind: UInt32(kEventHotKeyPressed)
        )
        var newHandlerRef: EventHandlerRef?
        let status = InstallEventHandler(
            GetApplicationEventTarget(),
            tuckClipHotKeyEventHandler,
            1,
            &eventType,
            Unmanaged.passUnretained(manager).toOpaque(),
            &newHandlerRef
        )

        guard status == noErr, let newHandlerRef else {
            throw HotKeyRegistrationError.eventHandlerInstallationFailed(status)
        }
        eventHandlerRef = newHandlerRef
    }

    func register(
        _ hotKey: GlobalHotKey,
        identifier: UInt32
    ) throws -> HotKeyRegistrationToken {
        let validated = try hotKey.validated()
        var reference: EventHotKeyRef?
        let status = RegisterEventHotKey(
            validated.keyCode,
            validated.modifiers,
            EventHotKeyID(signature: HotKeyManager.signature, id: identifier),
            GetApplicationEventTarget(),
            OptionBits(kEventHotKeyNoOptions),
            &reference
        )
        guard status == noErr, let reference else {
            throw HotKeyRegistrationError.registrationFailed(status)
        }
        return HotKeyRegistrationToken {
            UnregisterEventHotKey(reference)
        }
    }

    func removeHandler() {
        if let eventHandlerRef {
            RemoveEventHandler(eventHandlerRef)
            self.eventHandlerRef = nil
        }
    }
}

/// Dependency-free global shortcut registration using Carbon's supported
/// `RegisterEventHotKey` API. Registration itself does not require Accessibility.
@MainActor
final class HotKeyManager: ObservableObject {
    @Published private(set) var isRegistered = false
    @Published private(set) var lastError: HotKeyRegistrationError?

    var onPressed: (() -> Void)?

    nonisolated fileprivate static let signature: OSType = 0x54434C50 // "TCLP"
    nonisolated private static let firstIdentifier: UInt32 = 1
    nonisolated private static let secondIdentifier: UInt32 = 2

    private let system: HotKeySystemControlling
    private var registration: HotKeyRegistrationToken?
    private var activeIdentifier: UInt32?
    private(set) var activeHotKey: GlobalHotKey?

    init(system: HotKeySystemControlling? = nil) {
        self.system = system ?? CarbonHotKeySystem()
    }

    func register(using settings: AppSettings) throws {
        try register(settings.globalHotKey)
    }

    func register(_ hotKey: GlobalHotKey = .defaultValue) throws {
        let validated = try hotKey.validated()
        if isRegistered, activeHotKey == validated {
            lastError = nil
            return
        }

        do {
            try system.installHandlerIfNeeded(for: self)
            let identifier = activeIdentifier == Self.firstIdentifier
                ? Self.secondIdentifier
                : Self.firstIdentifier
            let newRegistration = try system.register(validated, identifier: identifier)

            let previous = registration
            registration = newRegistration
            activeIdentifier = identifier
            activeHotKey = validated
            lastError = nil
            isRegistered = true
            previous?.cancel()
        } catch let error as HotKeyRegistrationError {
            lastError = error
            isRegistered = registration != nil
            throw error
        }
    }

    func unregister() {
        registration?.cancel()
        registration = nil
        activeIdentifier = nil
        activeHotKey = nil
        isRegistered = false
    }

    func shutdown() {
        unregister()
        system.removeHandler()
    }

    func receiveHotKey(identifier: UInt32) {
        guard identifier == activeIdentifier else { return }
        onPressed?()
    }
}

private let tuckClipHotKeyEventHandler: EventHandlerUPP = {
    _, event, userData in
    guard let event, let userData else {
        return OSStatus(eventNotHandledErr)
    }

    var hotKeyID = EventHotKeyID(signature: 0, id: 0)
    let status = GetEventParameter(
        event,
        EventParamName(kEventParamDirectObject),
        EventParamType(typeEventHotKeyID),
        nil,
        MemoryLayout<EventHotKeyID>.size,
        nil,
        &hotKeyID
    )
    guard status == noErr,
          hotKeyID.signature == HotKeyManager.signature else {
        return OSStatus(eventNotHandledErr)
    }

    let manager = Unmanaged<HotKeyManager>
        .fromOpaque(userData)
        .takeUnretainedValue()
    let identifier = hotKeyID.id
    Task { @MainActor in
        manager.receiveHotKey(identifier: identifier)
    }
    return noErr
}

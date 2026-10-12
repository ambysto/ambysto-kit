// From Ambysto Optimer (macos/Sources/AmbystoOptimer/Floating/GlobalHotKey.swift, ambysto-optimer@549da79)
import Carbon.HIToolbox

/// A system-wide shortcut through Carbon's hot-key API (no Accessibility permission needed).
/// Default ⌃⌥M: ⌥⌘M is macOS's own "Minimize All", so it is deliberately avoided.
/// The suite hotkey moves between hosts, so `invalidate()` gives the registration back.
@MainActor
public final class GlobalHotKey {
    private var hotKey: EventHotKeyRef?
    private var handler: EventHandlerRef?
    private let id: UInt32
    /// Carbon calls back through a C function pointer, which cannot capture context: the actions live here by hot-key id.
    nonisolated(unsafe) private static var actions: [UInt32: @MainActor () -> Void] = [:]
    nonisolated(unsafe) private static var nextID: UInt32 = 1
    nonisolated static let signature = OSType(0x4153_5549)   // 'ASUI'

    public static let displayName = "⌃⌥M"
    /// Whether Carbon accepted the registration. Hot keys are non-exclusive across processes: another app using the
    /// same keys does not make this false (both receive the press). Another app's *exclusive* registration does not
    /// make it false either — the registration is accepted but receives no presses, which cannot be detected. Only a
    /// duplicate in this process does (spec, macOS notes).
    public private(set) var isRegistered = false

    public init(keyCode: Int = kVK_ANSI_M, modifiers: Int = controlKey | optionKey, action: @escaping @MainActor () -> Void) {
        id = Self.nextID
        Self.nextID += 1
        Self.actions[id] = action
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        // The id travels as the handler's user data. Any key that is not ours (the app's own Carbon hot keys) is
        // answered `eventNotHandledErr`, so it goes on to its owner.
        InstallEventHandler(GetApplicationEventTarget(), { _, event, userData in
            var pressed = EventHotKeyID()
            let status = GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                                           nil, MemoryLayout<EventHotKeyID>.size, nil, &pressed)
            guard status == noErr, pressed.signature == GlobalHotKey.signature,
                  let userData, pressed.id == UInt32(UInt(bitPattern: userData)) else {
                return OSStatus(eventNotHandledErr)
            }
            let id = pressed.id
            DispatchQueue.main.async { MainActor.assumeIsolated { GlobalHotKey.actions[id]?() } }
            return noErr
        }, 1, &spec, UnsafeMutableRawPointer(bitPattern: UInt(id)), &handler)
        let status = RegisterEventHotKey(UInt32(keyCode), UInt32(modifiers), EventHotKeyID(signature: Self.signature, id: id),
                                         GetApplicationEventTarget(), 0, &hotKey)
        isRegistered = status == noErr
    }

    /// Unregisters the shortcut and its handler. Safe to call twice.
    public func invalidate() {
        if let hotKey { UnregisterEventHotKey(hotKey) }
        if let handler { RemoveEventHandler(handler) }
        hotKey = nil
        handler = nil
        Self.actions[id] = nil
    }
}

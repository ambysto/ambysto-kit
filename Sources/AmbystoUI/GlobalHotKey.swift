// From Ambysto Optimer (macos/Sources/AmbystoOptimer/Floating/GlobalHotKey.swift, ambysto-optimer@549da79)
import Carbon.HIToolbox

/// A system-wide shortcut through Carbon's hot-key API (no Accessibility permission needed).
/// Default ⌃⌥M: ⌥⌘M is macOS's own "Minimize All", so it is deliberately avoided.
/// The suite hotkey moves between hosts, so `invalidate()` gives the registration back.
@MainActor
public final class GlobalHotKey {
    private var hotKey: EventHotKeyRef?
    private var handler: EventHandlerRef?
    /// Carbon calls back through a C function pointer, which cannot capture context.
    nonisolated(unsafe) private static var action: (@MainActor () -> Void)?
    nonisolated(unsafe) private static var owner: ObjectIdentifier?

    public static let displayName = "⌃⌥M"

    public init(keyCode: Int = kVK_ANSI_M, modifiers: Int = controlKey | optionKey, action: @escaping @MainActor () -> Void) {
        Self.action = action
        Self.owner = ObjectIdentifier(self)
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        InstallEventHandler(GetApplicationEventTarget(), { _, _, _ in
            DispatchQueue.main.async { MainActor.assumeIsolated { GlobalHotKey.action?() } }
            return noErr
        }, 1, &spec, nil, &handler)
        let id = EventHotKeyID(signature: OSType(0x4153_5549), id: 1)   // 'ASUI'
        RegisterEventHotKey(UInt32(keyCode), UInt32(modifiers), id, GetApplicationEventTarget(), 0, &hotKey)
    }

    /// Unregisters the shortcut and its handler. Safe to call twice.
    public func invalidate() {
        if let hotKey { UnregisterEventHotKey(hotKey) }
        if let handler { RemoveEventHandler(handler) }
        hotKey = nil
        handler = nil
        if Self.owner == ObjectIdentifier(self) {
            Self.action = nil
            Self.owner = nil
        }
    }
}

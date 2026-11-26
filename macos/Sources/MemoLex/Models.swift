import AppKit
import Foundation
import SwiftUI

enum J: Codable, Equatable, Sendable {
    case object([String: J]), array([J]), string(String), number(Double), bool(Bool), null
    init(from decoder: Decoder) throws {
        let value = try decoder.singleValueContainer()
        if value.decodeNil() { self = .null }
        else if let result = try? value.decode(Bool.self) { self = .bool(result) }
        else if let result = try? value.decode(String.self) { self = .string(result) }
        else if let result = try? value.decode(Double.self) { self = .number(result) }
        else if let result = try? value.decode([J].self) { self = .array(result) }
        else { self = .object(try value.decode([String: J].self)) }
    }
    func encode(to encoder: Encoder) throws {
        var value = encoder.singleValueContainer()
        switch self {
        case .object(let result): try value.encode(result)
        case .array(let result): try value.encode(result)
        case .string(let result): try value.encode(result)
        case .number(let result): try value.encode(result)
        case .bool(let result): try value.encode(result)
        case .null: try value.encodeNil()
        }
    }
    subscript(_ key: String) -> J { if case .object(let value) = self { return value[key] ?? .null }; return .null }
    var text: String { if case .string(let value) = self { return value }; return "" }
    var list: [J] { if case .array(let value) = self { return value }; return [] }
    var flag: Bool { if case .bool(let value) = self { return value }; return false }
    var integer: Int { if case .number(let value) = self { return Int(value) }; return 0 }
    var doubleValue: Double { if case .number(let value) = self { return value }; return 0 }
    var dictionary: [String: J] { if case .object(let value) = self { return value }; return [:] }
    static func strings(_ value: [String]) -> J { .array(value.map(J.string)) }
}

enum Palette {
    // Light-mode values follow the Windows Theme.xaml and settings surfaces.
    static let accent = Color(nsColor: rgb(0x34BCFC))
    static let accentInk = Color(nsColor: rgb(0x173749))
    static let deep = adaptive(0x087FAF, 0x73D1FE)
    static let ink = adaptive(0x25384A, 0xE6F1F8)
    static let muted = adaptive(0x647E93, 0xACC3D3)
    static let canvas = adaptive(0xF3F7FB, 0x161F28)
    static let paper = adaptive(0xFFFFFF, 0x202C37)
    static let chrome = adaptive(0xF9FCFF, 0x1D2934)
    static let sidebar = adaptive(0xF6FAFE, 0x192632)
    static let control = adaptive(0xFFFFFF, 0x263542)
    static let wash = adaptive(0xE8F7FF, 0x17394D)
    static let border = adaptive(0xE2EAF2, 0x344759)
    static let splitGrip = adaptive(0xCCDDEB, 0x50677B)

    private static func adaptive(_ light: UInt32, _ dark: UInt32) -> Color {
        Color(nsColor: NSColor(name: nil) { appearance in
            rgb(appearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua ? dark : light)
        })
    }

    private static func rgb(_ hex: UInt32) -> NSColor {
        NSColor(srgbRed: CGFloat((hex >> 16) & 255) / 255.0, green: CGFloat((hex >> 8) & 255) / 255.0,
                blue: CGFloat(hex & 255) / 255.0, alpha: 1)
    }
}

struct SavedAccount: Codable, Identifiable, Equatable {
    var id: String
    var name: String
    var identity: String
    var expiresAt: Date?
    var needsLogin = false
    var localCredentialID: UUID? = nil

    static func displayIdentity(_ value: String) -> String {
        if value.contains("@") { return value }
        let identity = value.trimmed
        if identity.range(of: #"^[+0-9]{3}[.*•…·]+[0-9]{4}$"#, options: .regularExpression) != nil {
            return String(identity.prefix(3)) + "****" + String(identity.suffix(4))
        }
        let phone = identity.filter { $0.isNumber || $0 == "+" }
        guard (7...19).contains(phone.count), identity.range(of: #"^\+?[0-9 ()-]+$"#, options: .regularExpression) != nil else { return value }
        return String(phone.prefix(3)) + "****" + String(phone.suffix(4))
    }

    var displayName: String { Self.displayIdentity(name) }
}

struct SearchRecord: Codable, Identifiable {
    var word: String
    var date: Date
    var count: Int
    var id: String { word.lowercased() }
}

struct ViewLocation: Equatable {
    enum Page: String { case word, chinese, translation }
    var page: Page
    var query: String
    var input = ""
    var translated = ""
    var direction = "en|zh-CN"
}

struct NativeShortcut: Codable, Equatable {
    var keyCode: UInt32
    var modifiers: UInt32
    var label: String
    static let defaults: [String: NativeShortcut] = [
        "search": .init(keyCode: 3, modifiers: 6144, label: "⌃⌥F"),
        "clipboard": .init(keyCode: 2, modifiers: 6144, label: "⌃⌥D"),
        "screenshot": .init(keyCode: 1, modifiers: 6144, label: "⌃⌥S")
    ]
}

@MainActor final class Preferences: ObservableObject {
    @Published var closeToTray = false
    @Published var rememberClose = false
    @Published var doubleCopy = false
    @Published var startAtLogin = false
    @Published var hoverEnabled = false
    @Published var hoverNearest = false
    @Published var hoverTranslucent = false
    @Published var hoverAutoAdd = false
    @Published var hoverDelay = 750
    @Published var hoverModifiers = 0
    @Published var hoverFontSize = "standard"
    @Published var alwaysOnTop = false
    @Published var useQuickLookupBar = false
    @Published var flashcardAutoSpeak = "off"
    @Published var showCountdown = false
    @Published var examName = "考试"
    @Published var examDate = Calendar.current.date(byAdding: .day, value: 30, to: Date()) ?? Date()
    @Published var stubbornSort = "review"
    @Published var alphabetDescending = false
    @Published var concealMeanings = false
    @Published var automaticUpdates = true
    @Published var automaticDownloads = false
    @Published var updateFrequency = "daily"
    @Published var updateSource = MacUpdateSource.fast.rawValue
    @Published var selectedNotepad = ""
    @Published var onboardingCompleted = false
    @Published var shortcuts = NativeShortcut.defaults
    private let defaults = UserDefaults.standard
    private let keys = ["closeToTray", "rememberClose", "doubleCopy", "hoverEnabled", "hoverNearest", "hoverTranslucent", "hoverAutoAdd", "hoverDelay", "hoverModifiers", "hoverFontSize", "alwaysOnTop", "useQuickLookupBar", "flashcardAutoSpeak", "showCountdown", "examName", "examDate", "stubbornSort", "alphabetDescending", "concealMeanings", "automaticUpdates", "automaticDownloads", "updateFrequency", "updateSource", "selectedNotepad", "onboardingCompleted"]

    init() {
        func bool(_ key: String, _ fallback: Bool = false) -> Bool { defaults.object(forKey: key) == nil ? fallback : defaults.bool(forKey: key) }
        closeToTray = bool("closeToTray"); rememberClose = bool("rememberClose"); doubleCopy = bool("doubleCopy"); startAtLogin = bool("startAtLogin")
        hoverEnabled = bool("hoverEnabled"); hoverNearest = bool("hoverNearest"); hoverTranslucent = bool("hoverTranslucent"); hoverAutoAdd = bool("hoverAutoAdd")
        if defaults.object(forKey: "hoverDelay") != nil { hoverDelay = min(3000, max(100, defaults.integer(forKey: "hoverDelay"))) }
        hoverModifiers = defaults.integer(forKey: "hoverModifiers"); hoverFontSize = defaults.string(forKey: "hoverFontSize") ?? "standard"
        alwaysOnTop = bool("alwaysOnTop"); useQuickLookupBar = bool("useQuickLookupBar")
        flashcardAutoSpeak = defaults.string(forKey: "flashcardAutoSpeak") ?? "off"
        if !["off", "en-GB", "en-US"].contains(flashcardAutoSpeak) { flashcardAutoSpeak = "off" }
        showCountdown = bool("showCountdown"); examName = defaults.string(forKey: "examName") ?? "考试"
        if let date = defaults.object(forKey: "examDate") as? Date { examDate = date }
        stubbornSort = defaults.string(forKey: "stubbornSort") ?? "review"; if !["review", "alphabetical", "random"].contains(stubbornSort) { stubbornSort = "review" }
        alphabetDescending = bool("alphabetDescending"); concealMeanings = bool("concealMeanings")
        automaticUpdates = bool("automaticUpdates", true); automaticDownloads = bool("automaticDownloads")
        updateFrequency = defaults.string(forKey: "updateFrequency") ?? "daily"
        if let savedSource = defaults.string(forKey: "updateSource"), let source = MacUpdateSource(rawValue: savedSource) {
            updateSource = source.rawValue
        }
        selectedNotepad = defaults.string(forKey: "selectedNotepad") ?? ""; onboardingCompleted = bool("onboardingCompleted")
        if let data = defaults.data(forKey: "shortcuts"), let saved = try? JSONDecoder().decode([String: NativeShortcut].self, from: data) { shortcuts.merge(saved) { _, new in new } }
    }
    func save() {
        let values: [String: Any] = ["closeToTray": closeToTray, "rememberClose": rememberClose, "doubleCopy": doubleCopy, "hoverEnabled": hoverEnabled, "hoverNearest": hoverNearest, "hoverTranslucent": hoverTranslucent, "hoverAutoAdd": hoverAutoAdd, "hoverDelay": min(3000, max(100, hoverDelay)), "hoverModifiers": hoverModifiers, "hoverFontSize": hoverFontSize, "alwaysOnTop": alwaysOnTop, "useQuickLookupBar": useQuickLookupBar, "flashcardAutoSpeak": flashcardAutoSpeak, "showCountdown": showCountdown, "examName": String(examName.prefix(40)), "examDate": examDate, "stubbornSort": stubbornSort, "alphabetDescending": alphabetDescending, "concealMeanings": concealMeanings, "automaticUpdates": automaticUpdates, "automaticDownloads": automaticDownloads, "updateFrequency": updateFrequency, "updateSource": updateSource, "selectedNotepad": selectedNotepad, "onboardingCompleted": onboardingCompleted]
        for key in keys {
            guard let value = values[key] else { continue }
            if (defaults.object(forKey: key) as? NSObject)?.isEqual(value) != true {
                defaults.set(value, forKey: key)
            }
        }
        if defaults.bool(forKey: "startAtLogin") != startAtLogin { defaults.set(startAtLogin, forKey: "startAtLogin") }
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        if let data = try? encoder.encode(shortcuts), data != defaults.data(forKey: "shortcuts") {
            defaults.set(data, forKey: "shortcuts")
        }
    }
}

enum AppPaths {
    static let profile = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("MemoLex", isDirectory: true)
    static var resources: URL { Bundle.main.resourceURL ?? URL(fileURLWithPath: FileManager.default.currentDirectoryPath) }
    static var version: String { Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "2.3.5" }
    static let repository = "https://github.com/SuperRaine/MemoLex"
    static let homepage = "https://memolex.de5.net/"
    static let quarkDownload = homepage + "quark.html"
}

extension String {
    var trimmed: String { trimmingCharacters(in: .whitespacesAndNewlines) }
    var statusMessage: String {
        let text = trimmed
        return text.last == "。" ? String(text.dropLast()) : text
    }
    var decodedLines: String { replacingOccurrences(of: "\\n", with: "\n") }
}

func parseISODate(_ text: String) -> Date? {
    let fractional = ISO8601DateFormatter(); fractional.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
    return fractional.date(from: text) ?? ISO8601DateFormatter().date(from: text)
}

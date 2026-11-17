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
    var dictionary: [String: J] { if case .object(let value) = self { return value }; return [:] }
    static func strings(_ value: [String]) -> J { .array(value.map(J.string)) }
}

enum Palette {
    static let accent = Color(red: 0.204, green: 0.737, blue: 0.988)
    static let deep = Color(red: 0.031, green: 0.498, blue: 0.686)
    static let ink = Color(red: 0.145, green: 0.220, blue: 0.290)
    static let muted = Color(red: 0.51, green: 0.62, blue: 0.70)
    static let canvas = Color(red: 0.953, green: 0.969, blue: 0.984)
    static let wash = Color(red: 0.937, green: 0.973, blue: 0.996)
    static let border = Color(red: 0.87, green: 0.92, blue: 0.96)
}

struct SavedAccount: Codable, Identifiable, Equatable {
    var id: String
    var name: String
    var identity: String
    var expiresAt: Date?
    var needsLogin = false
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
    @Published var showCountdown = false
    @Published var examName = "考试"
    @Published var examDate = Calendar.current.date(byAdding: .day, value: 30, to: Date()) ?? Date()
    @Published var stubbornSort = "review"
    @Published var alphabetDescending = false
    @Published var concealMeanings = false
    @Published var automaticUpdates = true
    @Published var automaticDownloads = false
    @Published var updateFrequency = "daily"
    @Published var updateSource = "auto"
    @Published var selectedNotepad = ""
    @Published var onboardingCompleted = false
    @Published var shortcuts = NativeShortcut.defaults
    private let defaults = UserDefaults.standard
    private let keys = ["closeToTray", "rememberClose", "doubleCopy", "hoverEnabled", "hoverNearest", "hoverTranslucent", "hoverAutoAdd", "hoverDelay", "hoverModifiers", "hoverFontSize", "alwaysOnTop", "showCountdown", "examName", "examDate", "stubbornSort", "alphabetDescending", "concealMeanings", "automaticUpdates", "automaticDownloads", "updateFrequency", "updateSource", "selectedNotepad", "onboardingCompleted"]

    init() {
        func bool(_ key: String, _ fallback: Bool = false) -> Bool { defaults.object(forKey: key) == nil ? fallback : defaults.bool(forKey: key) }
        closeToTray = bool("closeToTray"); rememberClose = bool("rememberClose"); doubleCopy = bool("doubleCopy"); startAtLogin = bool("startAtLogin")
        hoverEnabled = bool("hoverEnabled"); hoverNearest = bool("hoverNearest"); hoverTranslucent = bool("hoverTranslucent"); hoverAutoAdd = bool("hoverAutoAdd")
        if defaults.object(forKey: "hoverDelay") != nil { hoverDelay = min(3000, max(100, defaults.integer(forKey: "hoverDelay"))) }
        hoverModifiers = defaults.integer(forKey: "hoverModifiers"); hoverFontSize = defaults.string(forKey: "hoverFontSize") ?? "standard"
        alwaysOnTop = bool("alwaysOnTop"); showCountdown = bool("showCountdown"); examName = defaults.string(forKey: "examName") ?? "考试"
        if let date = defaults.object(forKey: "examDate") as? Date { examDate = date }
        stubbornSort = defaults.string(forKey: "stubbornSort") ?? "review"; if !["review", "alphabetical", "random"].contains(stubbornSort) { stubbornSort = "review" }
        alphabetDescending = bool("alphabetDescending"); concealMeanings = bool("concealMeanings")
        automaticUpdates = bool("automaticUpdates", true); automaticDownloads = bool("automaticDownloads")
        updateFrequency = defaults.string(forKey: "updateFrequency") ?? "daily"; updateSource = defaults.string(forKey: "updateSource") ?? "auto"
        selectedNotepad = defaults.string(forKey: "selectedNotepad") ?? ""; onboardingCompleted = bool("onboardingCompleted")
        if let data = defaults.data(forKey: "shortcuts"), let saved = try? JSONDecoder().decode([String: NativeShortcut].self, from: data) { shortcuts.merge(saved) { _, new in new } }
    }
    func save() {
        let values: [String: Any] = ["closeToTray": closeToTray, "rememberClose": rememberClose, "doubleCopy": doubleCopy, "hoverEnabled": hoverEnabled, "hoverNearest": hoverNearest, "hoverTranslucent": hoverTranslucent, "hoverAutoAdd": hoverAutoAdd, "hoverDelay": min(3000, max(100, hoverDelay)), "hoverModifiers": hoverModifiers, "hoverFontSize": hoverFontSize, "alwaysOnTop": alwaysOnTop, "showCountdown": showCountdown, "examName": String(examName.prefix(40)), "examDate": examDate, "stubbornSort": stubbornSort, "alphabetDescending": alphabetDescending, "concealMeanings": concealMeanings, "automaticUpdates": automaticUpdates, "automaticDownloads": automaticDownloads, "updateFrequency": updateFrequency, "updateSource": updateSource, "selectedNotepad": selectedNotepad, "onboardingCompleted": onboardingCompleted]
        for key in keys { defaults.set(values[key], forKey: key) }
        defaults.set(startAtLogin, forKey: "startAtLogin")
        if let data = try? JSONEncoder().encode(shortcuts) { defaults.set(data, forKey: "shortcuts") }
    }
    convenience init(copying source: Preferences) { self.init(); copyEditable(from: source) }
    func copyEditable(from source: Preferences) {
        closeToTray = source.closeToTray; rememberClose = source.rememberClose; doubleCopy = source.doubleCopy; startAtLogin = source.startAtLogin
        hoverEnabled = source.hoverEnabled; hoverNearest = source.hoverNearest; hoverTranslucent = source.hoverTranslucent; hoverAutoAdd = source.hoverAutoAdd
        hoverDelay = source.hoverDelay; hoverModifiers = source.hoverModifiers; hoverFontSize = source.hoverFontSize
        showCountdown = source.showCountdown; examName = source.examName; examDate = source.examDate; shortcuts = source.shortcuts
    }
}

enum AppPaths {
    static let profile = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("MemoLex", isDirectory: true)
    static var resources: URL { Bundle.main.resourceURL ?? URL(fileURLWithPath: FileManager.default.currentDirectoryPath) }
    static var version: String { "1.8.0" }
    static let repository = "https://github.com/SuperRaine/MemoLex"
}

extension String {
    var trimmed: String { trimmingCharacters(in: .whitespacesAndNewlines) }
    var decodedLines: String { replacingOccurrences(of: "\\n", with: "\n") }
}

func parseISODate(_ text: String) -> Date? {
    let fractional = ISO8601DateFormatter(); fractional.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
    return fractional.date(from: text) ?? ISO8601DateFormatter().date(from: text)
}

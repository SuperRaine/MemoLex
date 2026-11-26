import Foundation

struct FlashcardProgressSnapshot: Codable, Equatable {
    let words: [String]
    let cursor: Int
    let groupNumber: Int
    let groupWordCount: Int
    let queue: [String]
    let updatedAt: Date?
}

struct FlashcardPractice {
    enum Answer { case known, vague, forgotten }
    static let groupSize = 10
    private(set) var words: [String]
    private(set) var cursor = 0
    private(set) var groupNumber = 0
    private(set) var groupWordCount = 0
    private(set) var queue: [String] = []
    private var restored = false

    init(words: [String], saved: FlashcardProgressSnapshot? = nil) {
        let normalized = Self.normalize(words)
        self.words = normalized
        if let saved, tryRestore(saved, incoming: normalized) {
            restored = true
            return
        }
        startFresh(with: normalized)
    }

    var totalWordCount: Int { words.count }
    var current: String? { queue.first }
    var groupCount: Int { groupNumber + (max(0, words.count - cursor) + Self.groupSize - 1) / Self.groupSize }
    var hasNextGroup: Bool { cursor < words.count }
    var masteredCount: Int { min(words.count, max(0, cursor - queue.count)) }
    var hasProgress: Bool {
        if restored || masteredCount > 0 || groupNumber > 1 { return true }
        let initial = Array(words.prefix(groupWordCount)).map { $0.lowercased() }
        return !words.isEmpty && queue.map { $0.lowercased() } != initial
    }

    @discardableResult mutating func startNextGroup() -> Bool {
        guard queue.isEmpty, hasNextGroup else { return false }
        let batch = Array(words.dropFirst(cursor).prefix(Self.groupSize))
        queue = batch
        cursor += batch.count
        groupNumber += 1
        groupWordCount = batch.count
        return true
    }

    mutating func answer(_ answer: Answer) {
        guard !queue.isEmpty else { return }
        let word = queue.removeFirst()
        switch answer {
        case .known: break
        case .vague: queue.insert(word, at: min(3, queue.count))
        case .forgotten: queue.append(word)
        }
    }

    mutating func reset(words newWords: [String]? = nil) {
        startFresh(with: newWords.map(Self.normalize) ?? words)
    }

    func snapshot() -> FlashcardProgressSnapshot {
        FlashcardProgressSnapshot(
            words: words,
            cursor: cursor,
            groupNumber: groupNumber,
            groupWordCount: groupWordCount,
            queue: queue,
            updatedAt: Date()
        )
    }

    private mutating func startFresh(with normalized: [String]) {
        words = normalized
        restored = false
        cursor = 0
        groupNumber = 0
        groupWordCount = 0
        queue = []
        startNextGroup()
    }

    private mutating func tryRestore(_ saved: FlashcardProgressSnapshot, incoming: [String]) -> Bool {
        guard !incoming.isEmpty else { return false }
        let savedWords = Self.normalize(saved.words)
        guard !savedWords.isEmpty else { return false }

        var incomingByKey: [String: String] = [:]
        for word in incoming {
            let key = word.lowercased()
            if incomingByKey[key] == nil { incomingByKey[key] = word }
        }

        let savedCursor = min(max(0, saved.cursor), savedWords.count)
        var activeQueue: [String] = []
        var queueKeys = Set<String>()
        for raw in saved.queue {
            let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !trimmed.isEmpty, let canonical = incomingByKey[trimmed.lowercased()],
                  queueKeys.insert(canonical.lowercased()).inserted else { continue }
            activeQueue.append(canonical)
        }

        var mastered: [String] = []
        var placed = queueKeys
        for word in savedWords.prefix(savedCursor) {
            if let canonical = incomingByKey[word.lowercased()], placed.insert(canonical.lowercased()).inserted {
                mastered.append(canonical)
            }
        }

        var tail: [String] = []
        for word in savedWords.dropFirst(savedCursor) {
            if let canonical = incomingByKey[word.lowercased()], placed.insert(canonical.lowercased()).inserted {
                tail.append(canonical)
            }
        }
        for word in incoming {
            if placed.insert(word.lowercased()).inserted {
                tail.append(word)
            }
        }

        guard !mastered.isEmpty || !activeQueue.isEmpty else { return false }
        words = mastered + activeQueue + tail
        cursor = mastered.count + activeQueue.count
        queue = activeQueue
        groupNumber = max(1, saved.groupNumber)
        groupWordCount = min(max(activeQueue.count, saved.groupWordCount), max(activeQueue.count, Self.groupSize))
        return true
    }

    private static func normalize(_ words: [String]) -> [String] {
        var seen = Set<String>()
        return words.compactMap { raw in
            let word = raw.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !word.isEmpty, seen.insert(word.lowercased()).inserted else { return nil }
            return word
        }
    }
}

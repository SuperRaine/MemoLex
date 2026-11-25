import Foundation

struct FlashcardPractice {
    enum Answer { case known, vague, forgotten }
    static let groupSize = 10
    let words: [String]
    private(set) var groupNumber = 0
    private(set) var groupWordCount = 0
    private(set) var queue: [String] = []

    init(words: [String]) {
        var seen = Set<String>()
        self.words = words.filter { !$0.isEmpty && seen.insert($0.lowercased()).inserted }
        startNextGroup()
    }

    var current: String? { queue.first }
    var groupCount: Int { (words.count + Self.groupSize - 1) / Self.groupSize }
    var hasNextGroup: Bool { groupNumber < groupCount }

    @discardableResult mutating func startNextGroup() -> Bool {
        guard queue.isEmpty, hasNextGroup else { return false }
        queue = Array(words.dropFirst(groupNumber * Self.groupSize).prefix(Self.groupSize))
        groupNumber += 1
        groupWordCount = queue.count
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
}

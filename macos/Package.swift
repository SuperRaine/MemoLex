// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "MemoLex",
    platforms: [.macOS(.v13)],
    products: [.executable(name: "MemoLex", targets: ["MemoLex"])],
    targets: [.executableTarget(name: "MemoLex", path: "Sources/MemoLex")],
    swiftLanguageVersions: [.v5]
)

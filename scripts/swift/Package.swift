// swift-tools-version: 6.0
import PackageDescription

let package = Package(
  name: "DevDuoDiagnostics",
  platforms: [.macOS(.v13)],
  products: [
    .executable(name: "devduo-inspect-video", targets: ["InspectVideo"])
  ],
  targets: [
    .executableTarget(name: "InspectVideo")
  ]
)

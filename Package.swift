// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "AmbystoKit",
    platforms: [.macOS(.v14)],
    products: [
        // What apps import.
        .library(name: "AmbystoLicense", targets: ["AmbystoLicense"]),
        // The owner's issuing tool. Never a dependency of an app.
        .executable(name: "ambysto-license", targets: ["LicenseTool"]),
    ],
    targets: [
        .target(name: "AmbystoLicense"),
        .target(name: "KeychainACL", linkerSettings: [.linkedFramework("Security")]),
        .executableTarget(name: "LicenseTool", dependencies: ["AmbystoLicense", "KeychainACL"]),
        .testTarget(name: "AmbystoLicenseTests", dependencies: ["AmbystoLicense", "LicenseTool"]),
    ]
)

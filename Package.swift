// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "AmbystoKit",
    platforms: [.macOS(.v14)],
    products: [
        // What apps import.
        .library(name: "AmbystoLicense", targets: ["AmbystoLicense"]),
        .library(name: "AmbystoProcess", targets: ["AmbystoProcess"]),
        .library(name: "AmbystoSafety", targets: ["AmbystoSafety"]),
        .library(name: "AmbystoSuite", targets: ["AmbystoSuite"]),
        .library(name: "AmbystoUI", targets: ["AmbystoUI"]),
        // The owner's issuing tool. Never a dependency of an app.
        .executable(name: "ambysto-license", targets: ["LicenseTool"]),
    ],
    targets: [
        .target(name: "AmbystoLicense"),
        .target(name: "AmbystoProcess"),
        .target(name: "AmbystoSafety", dependencies: ["AmbystoProcess"]),
        .target(name: "AmbystoSuite"),
        .target(name: "AmbystoUI", dependencies: ["AmbystoSuite"], linkerSettings: [.linkedFramework("Carbon")]),
        // Proof helper (suite bar plan group 5): a fake participant. No app depends on it.
        .executableTarget(name: "SuiteBarDemo", dependencies: ["AmbystoUI", "AmbystoSuite"]),
        // Proof helper (plan group 5): prints sockets or CPU samples as JSON. No app depends on it.
        .executableTarget(name: "ProcessProbe", dependencies: ["AmbystoProcess"]),
        .target(name: "KeychainACL", linkerSettings: [.linkedFramework("Security")]),
        .executableTarget(name: "LicenseTool", dependencies: ["AmbystoLicense", "KeychainACL"]),
        .testTarget(name: "AmbystoLicenseTests", dependencies: ["AmbystoLicense", "LicenseTool"]),
        .testTarget(name: "AmbystoProcessTests", dependencies: ["AmbystoProcess"]),
        .testTarget(name: "AmbystoSafetyTests", dependencies: ["AmbystoSafety", "AmbystoProcess"]),
        .testTarget(name: "AmbystoSuiteTests", dependencies: ["AmbystoSuite"]),
        .testTarget(name: "AmbystoUITests", dependencies: ["AmbystoUI", "AmbystoSuite"]),
    ]
)

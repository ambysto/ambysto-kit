#!/usr/bin/env bash
# APS-14 plan group 3: the App Intents metadata step of an app's build, proven in the Optimer spike
# (intent/2026-10-12-kit-app-intents.plan.md, "Optimer spike"; docs/spec/intents.md, "macOS notes").
# An app's build-app.sh calls it after assembling the .app and before signing. It writes
# <app>/Contents/Resources/Metadata.appintents and touches nothing else outside a mktemp work folder.
# It does not sign, build or create images.
# Usage: scripts/app-intents-metadata.sh --package <swiftpm-package-dir> --module <module> --app <path.app> [--configuration release]
set -euo pipefail

# Xcode build numbers (xcodebuild -version) on which this step was proven. To add one: run the Optimer spike
# checks again on that Xcode (docs/spec/intents.md "macOS notes": metadata from a universal build, the SSU
# archive succeeds with all flags, Shortcuts lists the actions) and add the build number here.
KNOWN_XCODE_BUILDS=(27A266a)

usage() {
  cat <<USAGE
Usage: $0 --package DIR --module NAME --app PATH.app [--configuration release|debug]
  --package        the SwiftPM package folder (it holds Package.swift, Sources/<module> and .build)
  --module         the app's executable module name (for example AmbystoOptimer)
  --app            the assembled .app, not yet signed (Info.plist already in place)
  --configuration  the build configuration to read the const values from (default: release)
Run it after "swift build" and after assembling the .app, before signing.
USAGE
}

PACKAGE="" MODULE="" APP="" CONFIG="release"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --package) PACKAGE="${2:-}"; shift 2 ;;
    --module) MODULE="${2:-}"; shift 2 ;;
    --app) APP="${2:-}"; shift 2 ;;
    --configuration) CONFIG="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "error: unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done
if [[ -z "$PACKAGE" || -z "$MODULE" || -z "$APP" || -z "$CONFIG" ]]; then
  echo "error: --package, --module and --app are required" >&2; usage >&2; exit 2
fi
[[ -d "$PACKAGE" ]] || { echo "error: package folder not found: $PACKAGE" >&2; exit 1; }
[[ -d "$APP/Contents" ]] || { echo "error: not an assembled .app (no Contents): $APP" >&2; exit 1; }
[[ -f "$APP/Contents/Info.plist" ]] || { echo "error: no Info.plist in the bundle: $APP/Contents/Info.plist" >&2; exit 1; }

XCODE_BUILD="$(xcodebuild -version | awk '/Build version/ {print $3}')"
known=0
for b in "${KNOWN_XCODE_BUILDS[@]}"; do [[ "$b" == "$XCODE_BUILD" ]] && known=1; done
if [[ $known -eq 0 ]]; then
  echo "error: Xcode build '$XCODE_BUILD' is not on the proven list (${KNOWN_XCODE_BUILDS[*]})." >&2
  echo "The App Intents tools change between Xcode versions. To add this one: re-run the spike checks on it" >&2
  echo "(docs/spec/intents.md, macOS notes), then add the build number to KNOWN_XCODE_BUILDS in $0." >&2
  exit 1
fi

if [[ -n "${TOOLCHAINS:-}" ]]; then
  echo "error: TOOLCHAINS is set ('$TOOLCHAINS'): only the Xcode default toolchain is proven. Unset it." >&2
  exit 1
fi

PLIST="$APP/Contents/Info.plist"
BUNDLE_ID="$(/usr/libexec/PlistBuddy -c 'Print CFBundleIdentifier' "$PLIST")"
DEPLOYMENT="$(/usr/libexec/PlistBuddy -c 'Print LSMinimumSystemVersion' "$PLIST")"
[[ -n "$BUNDLE_ID" && -n "$DEPLOYMENT" ]] || { echo "error: CFBundleIdentifier or LSMinimumSystemVersion missing in $PLIST" >&2; exit 1; }

# Swift Build writes <module>-primary.swiftconstvalues per architecture; the arm64 one is used (the arm64
# and x86_64 outputs were byte-identical in the spike).
CONFIG_DIR="$(printf %s "${CONFIG:0:1}" | tr '[:lower:]' '[:upper:]')${CONFIG:1}"
SEARCH_ROOT="$PACKAGE/.build/out/Intermediates.noindex"
CONST_FILES=()
if [[ -d "$SEARCH_ROOT" ]]; then
  while IFS= read -r f; do CONST_FILES+=("$f"); done < <(
    find "$SEARCH_ROOT" -path "*/$CONFIG_DIR/$MODULE-*.build/Objects-normal/arm64/$MODULE-primary.swiftconstvalues" -type f)
fi
if [[ ${#CONST_FILES[@]} -ne 1 ]]; then
  echo "error: expected one arm64 $MODULE-primary.swiftconstvalues under $SEARCH_ROOT/*/$CONFIG_DIR/$MODULE-*.build/Objects-normal/arm64/, found ${#CONST_FILES[@]}." >&2
  echo "Build first: swift build --package-path '$PACKAGE' -c $CONFIG --arch arm64 --arch x86_64" >&2
  exit 1
fi
CONST_FILE="${CONST_FILES[0]}"
SOURCES_DIR="$PACKAGE/Sources/$MODULE"
[[ -d "$SOURCES_DIR" ]] || { echo "error: sources folder not found: $SOURCES_DIR" >&2; exit 1; }

# Stale build: the const values must come from the sources and the binary in the bundle.
NEWER="$(find "$SOURCES_DIR" -name '*.swift' -newer "$CONST_FILE" | head -n 1)"
if [[ -n "$NEWER" ]]; then
  # An incremental build recompiles the objects but leaves the const values behind, so a plain rebuild does not help.
  echo "error: $NEWER is newer than $CONST_FILE (stale const values). Remove '$PACKAGE/.build/out/Intermediates.noindex/$MODULE.build' and rebuild: swift build --package-path '$PACKAGE' -c $CONFIG --arch arm64 --arch x86_64" >&2
  exit 1
fi
EXE="$APP/Contents/MacOS/$MODULE"
[[ -f "$EXE" ]] || { echo "error: $EXE not found (assemble the .app before this step)" >&2; exit 1; }
if [[ "$EXE" -ot "$CONST_FILE" ]]; then
  echo "error: $EXE is older than $CONST_FILE (the bundle holds an older binary than the last build). Rebuild and assemble the .app again." >&2
  exit 1
fi

SDK="$(xcrun --sdk macosx --show-sdk-path)"
TOOLCHAIN="$(dirname "$(dirname "$(dirname "$(xcrun --find swift)")")")"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
RESOURCES="$APP/Contents/Resources"
mkdir -p "$RESOURCES"
rm -rf "$RESOURCES/Metadata.appintents"

echo "==> appintentsmetadataprocessor (Xcode $XCODE_BUILD, $BUNDLE_ID, macOS $DEPLOYMENT, $CONST_FILE)"
find "$SOURCES_DIR" -name '*.swift' > "$WORK/sources.txt"
echo "$CONST_FILE" > "$WORK/constvalues.txt"
xcrun appintentsmetadataprocessor --output "$RESOURCES" --toolchain-dir "$TOOLCHAIN" \
  --module-name "$MODULE" --sdk-root "$SDK" --xcode-version "$XCODE_BUILD" --platform-family macOS \
  --deployment-target "$DEPLOYMENT" --target-triple "arm64-apple-macos$DEPLOYMENT" \
  --source-file-list "$WORK/sources.txt" --swift-const-vals-list "$WORK/constvalues.txt"

echo "==> appintentsnltrainingprocessor (all flags are needed, or archiving the SSU assets fails)"
: > "$WORK/metadata-files.txt"
xcrun appintentsnltrainingprocessor --infoplist-path "$PLIST" --temp-dir-path "$WORK/ssu" \
  --bundle-id "$BUNDLE_ID" --product-path "$RESOURCES" \
  --extracted-metadata-path "$RESOURCES/Metadata.appintents" \
  --metadata-file-list "$WORK/metadata-files.txt" --archive-ssu-assets

[[ -f "$RESOURCES/Metadata.appintents/extract.actionsdata" ]] || { echo "error: no extract.actionsdata was written in $RESOURCES/Metadata.appintents" >&2; exit 1; }
echo "==> $RESOURCES/Metadata.appintents written (lint it with scripts/app-intents-lint.py, then sign)"

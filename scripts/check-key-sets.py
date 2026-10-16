#!/usr/bin/env python3
"""APS-23: the production trusted keys and the revocations exist in two copies, Swift and .NET; they must be equal.

Reads the four literals and compares them. It fails closed: each declaration must be found exactly once, and its
whole literal body must be consumed by a strict grammar (`<id>: "<128 hex>"` in Swift, `[<id>] = "<128 hex>"` in C#,
`"<16 hex>"` for revocations, with commas, whitespace and // comments). Anything left over is a failure, never "empty".

Usage: scripts/check-key-sets.py [swift-trusted swift-revocations cs-trusted cs-revocations]
With no arguments it reads the real sources. Exit 0 = equal, 1 = different or unreadable, 2 = usage.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULTS = [
    ROOT / "Sources/AmbystoLicense/TrustedKeys.swift",
    ROOT / "Sources/AmbystoLicense/Revocations.swift",
    ROOT / "windows/src/Ambysto.Kit.License/TrustedKeys.cs",
    ROOT / "windows/src/Ambysto.Kit.License/Revocations.cs",
]

# Where each literal starts (the opening bracket is the last character of the match) and which bracket closes it.
ANCHORS = {
    "swift-trusted": (r"static\s+let\s+production\s*=\s*TrustedKeys\(\s*rawHex:\s*\[", "[", "]"),
    "swift-revocations": (r"static\s+let\s+all\s*:\s*Set<String>\s*=\s*\[", "[", "]"),
    "cs-trusted": (r"Dictionary<byte,\s*string>\s+ProductionHex\s*=\s*new\(\)\s*\{", "{", "}"),
    "cs-revocations": (r"string\[\]\s+Ids\s*=\s*\[", "[", "]"),
}
SKIP = r"(?:\s+|//[^\n]*)*"
TRUSTED_ENTRY = {
    "swift-trusted": re.compile(SKIP + r'(\d{1,3})\s*:\s*"([0-9A-Fa-f]{128})"' + SKIP + r"(,|$)"),
    "cs-trusted": re.compile(SKIP + r'\[\s*(\d{1,3})\s*\]\s*=\s*"([0-9A-Fa-f]{128})"' + SKIP + r"(,|$)"),
}
REVOKED_ENTRY = re.compile(SKIP + r'"([0-9A-Fa-f]{16})"' + SKIP + r"(,|$)")


class Unreadable(Exception):
    pass


def body(kind, path):
    """The text between the literal's brackets. Fails unless the declaration occurs exactly once."""
    try:
        text = path.read_text(encoding="utf-8")
    except OSError as e:
        raise Unreadable(f"{kind}: cannot read {path}: {e.strerror}")
    pattern, opening, closing = ANCHORS[kind]
    found = list(re.finditer(pattern, text))
    if len(found) != 1:
        raise Unreadable(f"{kind}: declaration found {len(found)} times in {path}, expected once")
    i, depth = found[0].end(), 1
    start = i
    while i < len(text):
        c = text[i]
        if text.startswith("//", i):
            i = text.find("\n", i)
            if i < 0:
                break
        elif c == '"':
            end = text.find('"', i + 1)
            if end < 0:
                break
            i = end
        elif c == opening:
            depth += 1
        elif c == closing:
            depth -= 1
            if depth == 0:
                return text[start:i]
        i += 1
    raise Unreadable(f"{kind}: literal not closed in {path}")


def entries(kind, text, entry):
    """Consumes the whole body entry by entry; anything the grammar does not take is a failure."""
    stripped = re.fullmatch(SKIP + r"(:)?" + SKIP, text)
    if stripped:  # empty: "", whitespace, comments, or Swift's "[:]"
        if stripped.group(1) and kind != "swift-trusted":
            raise Unreadable(f"{kind}: unexpected ':' in an empty literal")
        return []
    found, pos = [], 0
    while pos < len(text):
        if re.fullmatch(SKIP, text[pos:]):  # after a trailing comma: only whitespace and comments left
            break
        m = entry.match(text, pos)
        if not m or m.end() == pos:
            rest = text[pos:].strip().splitlines()[0][:60]
            raise Unreadable(f"{kind}: unrecognised entry: {rest!r}")
        found.append(m.groups()[:-1])
        pos = m.end()
        if m.group(m.lastindex) != ",":  # the last entry, without a trailing comma, must end the body
            if text[pos:].strip():
                raise Unreadable(f"{kind}: unrecognised text after the last entry")
            break
    return found


def trusted(kind, path):
    keys = {}
    for id_text, hex_text in entries(kind, body(kind, path), TRUSTED_ENTRY[kind]):
        key_id = int(id_text)
        if key_id > 255:
            raise Unreadable(f"{kind}: signing key id {key_id} is not a byte")
        if key_id in keys:
            raise Unreadable(f"{kind}: signing key id {key_id} appears twice")
        keys[key_id] = hex_text.lower()
    return keys


def revoked(kind, path):
    ids = [g[0].lower() for g in entries(kind, body(kind, path), REVOKED_ENTRY)]
    if len(ids) != len(set(ids)):
        raise Unreadable(f"{kind}: a revoked id appears twice")
    return set(ids)


def main(argv):
    if len(argv) not in (0, 4):
        print(__doc__.strip().splitlines()[-3], file=sys.stderr)
        return 2
    paths = [Path(a) for a in argv] if argv else DEFAULTS
    failures = []
    try:
        swift_keys = trusted("swift-trusted", paths[0])
        swift_revoked = revoked("swift-revocations", paths[1])
        cs_keys = trusted("cs-trusted", paths[2])
        cs_revoked = revoked("cs-revocations", paths[3])
    except Unreadable as e:
        print(f"FAIL: {e}")
        return 1
    for key_id in sorted(set(swift_keys) | set(cs_keys)):
        if key_id not in cs_keys:
            failures.append(f"signing key id {key_id} is trusted in Swift but not in .NET")
        elif key_id not in swift_keys:
            failures.append(f"signing key id {key_id} is trusted in .NET but not in Swift")
        elif swift_keys[key_id] != cs_keys[key_id]:
            failures.append(f"signing key id {key_id} has a different public key in Swift and .NET")
    for key in sorted(swift_revoked - cs_revoked):
        failures.append(f"license key id {key} is revoked in Swift but not in .NET")
    for key in sorted(cs_revoked - swift_revoked):
        failures.append(f"license key id {key} is revoked in .NET but not in Swift")
    for failure in failures:
        print(f"FAIL: {failure}")
    if failures:
        return 1
    print(f"PASS: trusted ids {sorted(swift_keys)} and {len(swift_revoked)} revoked ids are equal in Swift and .NET")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

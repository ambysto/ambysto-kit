#!/usr/bin/env python3
"""APS-14 plan group 3: the safety lint for an app's App Intents metadata (docs/spec/intents.md, "Kinds").
Reads the extract.actionsdata that scripts/app-intents-metadata.sh wrote and the app's allow-list of READ intents
(one intent identifier per line; "#" starts a comment; blank lines are ignored). It fails when
  - an action not on the list does not have openAppWhenRun exactly true and supportedModes exactly 2 (it could
    change something without the app's own confirmation step),
  - an action on the list does not have openAppWhenRun exactly false and supportedModes exactly 1 (a read intent
    must not open the app); anything missing or unexpected fails closed ("unproven"),
  - an action entry is not an object,
  - a list entry is not in the metadata (a stale list),
  - the metadata file is missing, not JSON, or has no "actions" object, or it holds no action at all.
Usage: scripts/app-intents-lint.py <Metadata.appintents dir or extract.actionsdata> <allow-list file>
Exit 0: clean (one summary line). Exit 1: one line per problem on stderr. Exit 2: usage error.
"""
import json, os, sys

def read_allow_list(path):
    ids = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            entry = line.split("#", 1)[0].strip()
            if entry:
                ids.append(entry)
    return ids

def main(argv):
    if len(argv) != 3:
        print("usage: app-intents-lint.py <Metadata.appintents dir or extract.actionsdata> <allow-list file>", file=sys.stderr)
        return 2
    metadata, allow_path = argv[1], argv[2]
    if os.path.isdir(metadata):
        metadata = os.path.join(metadata, "extract.actionsdata")
    try:
        allow = read_allow_list(allow_path)
    except (OSError, UnicodeDecodeError) as e:
        print(f"usage: cannot read the allow-list {allow_path}: {e}", file=sys.stderr)
        return 2

    problems = []
    try:
        with open(metadata, encoding="utf-8") as f:
            data = json.load(f)
    except OSError as e:
        print(f"FAIL: metadata file missing or unreadable: {metadata}: {e}", file=sys.stderr)
        return 1
    except ValueError as e:
        print(f"FAIL: metadata file is not JSON: {metadata}: {e}", file=sys.stderr)
        return 1
    actions = data.get("actions") if isinstance(data, dict) else None
    if not isinstance(actions, dict):
        print(f"FAIL: metadata has no \"actions\" object: {metadata}", file=sys.stderr)
        return 1
    if not actions:
        print(f"FAIL: metadata holds zero actions: {metadata} (the build step produced nothing to lint)", file=sys.stderr)
        return 1

    allowed = set(allow)
    for name in sorted(actions):
        action = actions[name]
        if not isinstance(action, dict):
            problems.append(f"{name}: the action entry is not an object (unproven)")
            continue
        opens, modes = action.get("openAppWhenRun"), action.get("supportedModes")
        reasons = []
        if name in allowed:
            # A read intent: openAppWhenRun exactly false and supportedModes exactly 1 (as in the real metadata).
            if opens is True:
                reasons.append("openAppWhenRun is true (a read intent must not open the app)")
            elif opens is not False:
                reasons.append(f"openAppWhenRun is {opens!r}, not false (unproven)")
            if modes != 1:
                reasons.append(f"supportedModes is {modes!r}, not 1 (unproven)")
            if reasons:
                problems.append(f"{name}: on the allow-list but " + "; ".join(reasons))
        else:
            # Anything else must open the app: openAppWhenRun exactly true and supportedModes exactly 2.
            if opens is not True:
                reasons.append(f"openAppWhenRun is {opens!r}, not true (an intent that changes something must open the app; a read intent must be listed)")
            if modes != 2:
                reasons.append(f"supportedModes is {modes!r}, not 2 (unproven)")
            if reasons:
                problems.append(f"{name}: not on the allow-list but " + "; ".join(reasons))
    for name in sorted(allowed - set(actions)):
        problems.append(f"{name}: on the allow-list but not in the metadata (stale list entry)")

    if problems:
        for p in problems:
            print(f"FAIL: {p}", file=sys.stderr)
        return 1
    reads = len(allowed)
    print(f"OK: {len(actions)} intents ({reads} read, {len(actions) - reads} open the app)")
    return 0

if __name__ == "__main__":
    sys.exit(main(sys.argv))

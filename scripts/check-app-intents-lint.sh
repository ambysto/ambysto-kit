#!/usr/bin/env bash
# APS-14 plan group 3: checks scripts/app-intents-lint.py against the fixtures in scripts/fixtures/app-intents/
# (trimmed from the real extract.actionsdata of the Optimer spike, Xcode 27A266a): pass must exit 0, every
# fail-* case must exit 1. Prints PASS or FAIL per case; exits non-zero on any mismatch.
# Usage: scripts/check-app-intents-lint.sh
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
FIXTURES="$HERE/fixtures/app-intents"
LINT="$HERE/app-intents-lint.py"
bad=0

check() { # name expected-exit
  local name="$1" want="$2" got out
  out="$(python3 -I "$LINT" "$FIXTURES/$name" "$FIXTURES/$name/allow-list.txt" 2>&1)"; got=$?
  if [[ $got -eq $want ]]; then echo "PASS $name (exit $got)"; else echo "FAIL $name (exit $got, wanted $want)"; bad=1; fi
  printf '%s\n' "$out" | sed 's/^/     /'
}

check pass 0
check fail-missing-open 1
check fail-read-opens 1
check fail-stale-list 1

# Also the file form, and the usage and missing-file errors.
python3 -I "$LINT" "$FIXTURES/pass/extract.actionsdata" "$FIXTURES/pass/allow-list.txt" >/dev/null 2>&1 && echo "PASS file-form (exit 0)" || { echo "FAIL file-form"; bad=1; }
python3 -I "$LINT" "$FIXTURES/pass" >/dev/null 2>&1; [[ $? -eq 2 ]] && echo "PASS usage (exit 2)" || { echo "FAIL usage (wanted exit 2)"; bad=1; }
python3 -I "$LINT" "$FIXTURES/none" "$FIXTURES/pass/allow-list.txt" >/dev/null 2>&1; [[ $? -eq 1 ]] && echo "PASS missing-metadata (exit 1)" || { echo "FAIL missing-metadata (wanted exit 1)"; bad=1; }

exit $bad

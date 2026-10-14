#!/usr/bin/env bash
# APS-14 plan group 3: checks scripts/app-intents-lint.py against the fixtures in scripts/fixtures/app-intents/
# (trimmed from the real extract.actionsdata of the Optimer spike, Xcode 27A266a): pass must exit 0, every
# fail-* case must exit 1 with its specific FAIL text and no Python traceback. Prints PASS or FAIL per case; exits non-zero on any mismatch.
# Usage: scripts/check-app-intents-lint.sh
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
FIXTURES="$HERE/fixtures/app-intents"
LINT="$HERE/app-intents-lint.py"
bad=0

check() { # name expected-exit [expected FAIL text]
  local name="$1" want="$2" text="${3:-}" got out ok=1
  out="$(python3 -I "$LINT" "$FIXTURES/$name" "$FIXTURES/$name/allow-list.txt" 2>&1)"; got=$?
  [[ $got -eq $want ]] || ok=0
  [[ "$out" != *Traceback* ]] || ok=0
  [[ -z "$text" || "$out" == *"FAIL: $text"* ]] || ok=0
  if [[ $ok -eq 1 ]]; then echo "PASS $name (exit $got)"; else echo "FAIL $name (exit $got, wanted $want${text:+, text \"FAIL: $text\"})"; bad=1; fi
  printf '%s\n' "$out" | sed 's/^/     /'
}

check pass 0
check fail-missing-open 1 "SpikeProtocolDefaultIntent: not on the allow-list but openAppWhenRun is False, not true"
check fail-read-opens 1 "SpikeOpenCleanupIntent: on the allow-list but openAppWhenRun is true"
check fail-stale-list 1 "SpikeRemovedIntent: on the allow-list but not in the metadata"
check fail-modes-mismatch 1 "SpikeOpenCleanupIntent: not on the allow-list but supportedModes is 1, not 2 (unproven)"

# Also the file form, and the usage and missing-file errors.
python3 -I "$LINT" "$FIXTURES/pass/extract.actionsdata" "$FIXTURES/pass/allow-list.txt" >/dev/null 2>&1 && echo "PASS file-form (exit 0)" || { echo "FAIL file-form"; bad=1; }
python3 -I "$LINT" "$FIXTURES/pass" >/dev/null 2>&1; [[ $? -eq 2 ]] && echo "PASS usage (exit 2)" || { echo "FAIL usage (wanted exit 2)"; bad=1; }
python3 -I "$LINT" "$FIXTURES/none" "$FIXTURES/pass/allow-list.txt" >/dev/null 2>&1; [[ $? -eq 1 ]] && echo "PASS missing-metadata (exit 1)" || { echo "FAIL missing-metadata (wanted exit 1)"; bad=1; }

exit $bad

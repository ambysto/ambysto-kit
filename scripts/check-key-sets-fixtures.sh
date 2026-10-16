#!/usr/bin/env bash
# APS-23 plan group 5: checks scripts/check-key-sets.py against scripts/fixtures/key-sets/: pass-* must exit 0,
# every fail-* case must exit 1 with its specific FAIL text and no Python traceback; then the real sources must pass.
# Prints PASS or FAIL per case; exits non-zero on any mismatch.
# Usage: scripts/check-key-sets-fixtures.sh
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
FIXTURES="$HERE/fixtures/key-sets"
CHECK="$HERE/check-key-sets.py"
bad=0

check() { # name expected-exit [expected FAIL text]
  local name="$1" want="$2" text="${3:-}" got out ok=1 d="$FIXTURES/$1"
  out="$(python3 -I "$CHECK" "$d/TrustedKeys.swift" "$d/Revocations.swift" "$d/TrustedKeys.cs" "$d/Revocations.cs" 2>&1)"; got=$?
  [[ $got -eq $want ]] || ok=0
  [[ "$out" != *Traceback* ]] || ok=0
  [[ -z "$text" || "$out" == *"FAIL: $text"* ]] || ok=0
  if [[ $ok -eq 1 ]]; then echo "PASS $name (exit $got)"; else echo "FAIL $name (exit $got, wanted $want${text:+, text \"FAIL: $text\"})"; bad=1; fi
  printf '%s\n' "$out" | sed 's/^/     /'
}

check pass-empty 0
check pass-one 0
check fail-extra-id-swift 1 "signing key id 2 is trusted in Swift but not in .NET"
check fail-extra-id-cs 1 "signing key id 2 is trusted in .NET but not in Swift"
check fail-different-key 1 "signing key id 1 has a different public key in Swift and .NET"
check fail-extra-revocation-swift 1 "license key id a1a2a3a4a5a6a7a8 is revoked in Swift but not in .NET"
check fail-extra-revocation-cs 1 "license key id a1a2a3a4a5a6a7a8 is revoked in .NET but not in Swift"
check fail-unrecognised-swift 1 "swift-trusted: unrecognised entry"
check fail-unrecognised-cs 1 "cs-trusted: unrecognised entry"
check fail-unrecognised-revocation 1 "cs-revocations: unrecognised entry"
check fail-missing-declaration 1 "cs-revocations: declaration found 0 times"
check fail-twice 1 "swift-trusted: declaration found 2 times"
check fail-chained-swift 1 "swift-trusted: unexpected text after the literal"
check fail-chained-revocation-swift 1 "swift-revocations: unexpected text after the literal"
check fail-cs-static-constructor 1 "cs-trusted: ProductionHex occurs 3 times"
check fail-unicode-digit 1 "swift-trusted: unrecognised entry"
check fail-next-line-union-swift 1 "swift-revocations: unexpected text after the literal"
check fail-cs-wrapped-read-revocations 1 "cs-revocations: Ids is not read exactly as the frozen set's source"
check fail-cs-wrapped-read-trusted 1 "cs-trusted: ProductionHex is not read exactly as the frozen set's source"
check fail-commented-key-swift 1 "signing key id 2 is trusted in .NET but not in Swift"

# The real sources, the usage error and a missing file.
python3 -I "$CHECK" >/dev/null 2>&1 && echo "PASS real-sources (exit 0)" || { echo "FAIL real-sources"; bad=1; }
python3 -I "$CHECK" one two >/dev/null 2>&1; [[ $? -eq 2 ]] && echo "PASS usage (exit 2)" || { echo "FAIL usage (wanted exit 2)"; bad=1; }
d="$FIXTURES/pass-empty"
out="$(python3 -I "$CHECK" "$d/none.swift" "$d/Revocations.swift" "$d/TrustedKeys.cs" "$d/Revocations.cs" 2>&1)"; got=$?
[[ $got -eq 1 && "$out" == *"FAIL: swift-trusted: cannot read"* && "$out" != *Traceback* ]] && echo "PASS missing-file (exit 1)" || { echo "FAIL missing-file (exit $got)"; bad=1; }

exit $bad

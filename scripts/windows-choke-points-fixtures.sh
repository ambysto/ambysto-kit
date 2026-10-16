#!/usr/bin/env bash
# Proves the tripwire: the pass fixture passes, every fail fixture (one forbidden use each) fails, the real source passes.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"; S="$ROOT/scripts/windows-choke-points.sh"; F="$ROOT/scripts/fixtures/choke-points"
bad=0
"$S" "$F/pass" >/dev/null || { echo "WRONG: pass fixture failed"; bad=1; }
for f in "$F"/fail/*.cs; do
  d=$(mktemp -d); cp "$f" "$d/"
  if "$S" "$d" >/dev/null; then echo "WRONG: $(basename "$f") passed"; bad=1; fi
  rm -rf "$d"
done
"$S" >/dev/null || { echo "WRONG: the library itself fails"; "$S"; bad=1; }
n=$(ls "$F"/fail/*.cs | wc -l)
[ $bad = 0 ] && echo "PASS: pass fixture passes, $n of $n fail fixtures fail, the library passes" || { echo "FAIL"; exit 1; }

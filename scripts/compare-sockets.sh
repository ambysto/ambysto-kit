#!/usr/bin/env bash
# Proof (Kit 0.2, criterion 2): every (pid, address, port) of the user's listening TCP sockets read through
# AmbystoProcess equals lsof's, three runs.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
swift build -q --package-path "$ROOT" -c release --product ProcessProbe
PROBE="$(swift build --package-path "$ROOT" -c release --show-bin-path)/ProcessProbe"
for run in 1 2 3; do
  probe=$("$PROBE" sockets | python3 -c '
import json, sys
for r in json.load(sys.stdin):
    a = r["address"]
    a = a[7:] if a.startswith("::ffff:") else a
    a = "*" if a in ("0.0.0.0", "::") else a
    print(r["pid"], a, r["port"])' | sort -u)
  lsofout=$(lsof -nP -iTCP -sTCP:LISTEN -u "$(id -u)" -a -F pn 2>/dev/null | python3 -c '
import sys
pid = None
for line in sys.stdin:
    line = line.rstrip("\n")
    if line.startswith("p"):
        pid = line[1:]
    elif line.startswith("n") and pid:
        addr, port = line[1:].rsplit(":", 1)
        print(pid, addr.strip("[]"), port)' | sort -u)
  if [[ "$probe" == "$lsofout" ]]; then
    echo "run $run: PASS ($(echo "$probe" | grep -c . ) lines)"
  else
    echo "run $run: FAIL"; diff <(echo "$probe") <(echo "$lsofout") || true; exit 1
  fi
  sleep 1
done

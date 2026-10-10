#!/usr/bin/env bash
# Proof (Kit 0.2, criterion 3): CPU % from AmbystoProcess vs top over the same 2 s window, top 10 readable processes,
# within 5 percentage points.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
swift build -q --package-path "$ROOT" -c release --product ProcessProbe
PROBE="$(swift build --package-path "$ROOT" -c release --show-bin-path)/ProcessProbe"
WORK="$(mktemp -d)"
"$PROBE" cpu 2 > "$WORK/probe.json" &
top -l 2 -s 2 -stats pid,cpu > "$WORK/top.txt"
wait
python3 - "$WORK/probe.json" "$WORK/top.txt" <<'PY'
import json, sys
probe = {r["pid"]: float(r["cpu"]) for r in json.load(open(sys.argv[1]))}
lines = open(sys.argv[2]).read().split("\n")
second = [i for i, l in enumerate(lines) if l.startswith("PID")][-1]   # top's second sample only
top = {}
for l in lines[second + 1:]:
    parts = l.split()
    if len(parts) >= 2:
        try: top[parts[0]] = float(parts[1])
        except ValueError: pass
common = sorted((p for p in probe if p in top), key=lambda p: -top[p])[:10]
worst = 0.0
for p in common:
    d = abs(probe[p] - top[p]); worst = max(worst, d)
    print(f"pid {p:>6}  probe {probe[p]:6.1f}  top {top[p]:6.1f}  diff {d:5.1f}")
ok = worst <= 5.0 and len(common) >= 5
print(("PASS" if ok else "FAIL") + f"  worst difference {worst:.1f} points over {len(common)} processes")
sys.exit(0 if ok else 1)
PY

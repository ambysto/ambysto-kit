#!/usr/bin/env bash
# Proof (Kit 0.3, criterion 3): the kernel's per-process energy estimate moves with a process's own work, and no
# cumulative counter goes down. Three runs of `ProcessProbe energy 10`. PASS on Apple Silicon when the probe's own
# one-second busy loop raises its energy, the energy delta sum is above 0 and no counter went down. On Intel the values
# are printed (zero or unavailable energy is a finding to record, not a failure). Nothing leaves the machine.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
swift build -q --package-path "$ROOT" -c release --product ProcessProbe
PROBE="$(swift build --package-path "$ROOT" -c release --show-bin-path)/ProcessProbe"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
for run in 1 2 3; do
    "$PROBE" energy 10 > "$WORK/run$run.json"
done
python3 -I - "$WORK" <<'PY'
import json, sys
ok = True
for run in (1, 2, 3):
    r = json.load(open(f"{sys.argv[1]}/run{run}.json"))
    down = sum(r["countersWentDown"].values())
    own = r["ownBusyLoopEnergyDelta"]
    print(f"run {run}: {r['model']} ({r['architecture']}), V6 answered {r['v6Answered']}, "
          f"{r['readableProcesses']} readable ({r['processesWithDeltas']} with deltas, {r['processesWithEnergy']} with energy > 0), "
          f"energy sum {r['energyDeltaSum']} nJ, idle wakeups {r['idleWakeupsDeltaSum']}, interrupt wakeups {r['interruptWakeupsDeltaSum']}, "
          f"own busy-loop energy {own} nJ, counters down {r['countersWentDown']}")
    if down > 0:   # criterion 3 holds on every machine
        ok = False
        print(f"  run {run}: FAIL (a counter went down)")
    if r["architecture"] == "apple-silicon":
        passed = own is not None and own > 0 and r["energyDeltaSum"] > 0
        ok = ok and passed
        if not passed:
            print(f"  run {run}: FAIL")
if r["architecture"] != "apple-silicon":
    print(("PASS" if ok else "FAIL") + "  (Intel: energy values recorded; only decreasing counters fail)")
else:
    print(("PASS" if ok else "FAIL") + "  (three runs on Apple Silicon)")
sys.exit(0 if ok else 1)
PY

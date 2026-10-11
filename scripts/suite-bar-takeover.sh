#!/usr/bin/env bash
# Proof (suite bar plan, criteria 1 and 3): host takeover, measured on the real menu bar.
# Starts SuiteBarDemo three times (own temp suite folder, one copy of the binary per demo so each has its own
# defaults domain), counts the menu bar items of each demo pid through Accessibility every 100 ms, and tells the
# suite item from a demo's own icon with the demos' logs: every demo logs "hosting" / "not-hosting" with its pid,
# so at each sample the current host is the pid whose latest hosting event is "hosting"; its items are the suite
# item ("suite"), the items of every other pid are own icons ("own"). Checks:
#   A  three fresh demos settle on exactly one item (the suite item; their own icons are gone);
#   B  `quit` (NSApp.terminate) to the host: suite <= 1 at every sample, suite + own >= 1 except for a gap of at
#      most 2 s, and no demo other than the host keeps an item unless its log says its own icon is shown;
#   C  `kill -9` of the new host: the same;
#   D  `freeze` one non-host: the host's log shows its row "notRunning" within ttl + 5 s.
# The gap is the time from the first sample with no item to the next sample with one; it prints "< 0.1 s" when no
# sample was empty (the gap, if any, was shorter than the sampling interval). Three runs. Needs Accessibility
# permission for the terminal (see suite-bar-count.swift). It signals only demo pids it started, after
# `ps -p <pid> -o comm=` matches the copy's name, and deletes only its own temp directory.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
RUNS="${RUNS:-3}"   # the proof is three runs
TTL=10
NAMES=(optimer devbox power)

swift build -q --package-path "$ROOT" --product SuiteBarDemo || { echo "build failed"; exit 2; }
DEMO_BIN="$(swift build --package-path "$ROOT" --show-bin-path)/SuiteBarDemo"

TMP="$(mktemp -d "${TMPDIR:-/tmp}/suite-bar-takeover.XXXXXX")"
COUNT_BIN="$TMP/suite-bar-count"
trap cleanup EXIT

PIDS=()          # demo pids, same order as NAMES
WATCH_PID=""
FAILURES=0

# Signals a pid only if it is one this script started: its command name must be the copy's name.
safe_signal() {   # signal pid expected-name
  local sig="$1" pid="$2" name="$3" comm
  [ -n "$pid" ] || return 0
  comm="$(ps -p "$pid" -o comm= 2>/dev/null)" || return 0
  [ "$(basename "$comm")" = "$name" ] || return 0
  kill "-$sig" "$pid" 2>/dev/null || true
}

stop_demos() {
  local i
  for i in 0 1 2; do
    [ "${PIDS[$i]:-}" ] && safe_signal KILL "${PIDS[$i]}" "demo-${NAMES[$i]}"
  done
  PIDS=()
  exec 5>&- 6>&- 7>&-
  if [ -n "$WATCH_PID" ]; then
    safe_signal KILL "$WATCH_PID" "suite-bar-count"
    WATCH_PID=""
  fi
  sleep 0.3
}

cleanup() {
  stop_demos
  case "$TMP" in
    "${TMPDIR:-/tmp}"/suite-bar-takeover.*) rm -rf "$TMP" ;;
  esac
}

now() { python3 -c 'import time; print("%.3f" % time.time())'; }

report() {   # run phase ok message
  if [ "$3" = ok ]; then echo "run $1 phase $2: PASS  $4"; else echo "run $1 phase $2: FAIL  $4"; FAILURES=$((FAILURES + 1)); fi
}

start_demos() {   # run label
  local run="$1" label="$2" i dir="$TMP/$1-$2"
  mkdir -p "$dir"
  SUITE="$dir/suite"
  for i in 0 1 2; do
    cp "$DEMO_BIN" "$dir/demo-${NAMES[$i]}"
    mkfifo "$dir/in-${NAMES[$i]}"
  done
  exec 5<>"$dir/in-optimer" 6<>"$dir/in-devbox" 7<>"$dir/in-power"
  PIDS=()
  for i in 0 1 2; do
    "$dir/demo-${NAMES[$i]}" --folder "$SUITE" --app "${NAMES[$i]}" --metric "Load=$((10 + i * 10))%" \
      < "$dir/in-${NAMES[$i]}" > "$dir/log-${NAMES[$i]}" 2> "$dir/err-${NAMES[$i]}" &
    PIDS+=($!)
    disown $!   # no job-control notices when they are killed on purpose
  done
  DIR="$dir"
  SAMPLES="$dir/samples.log"
  "$COUNT_BIN" --watch 100 "${PIDS[@]}" > "$SAMPLES" 2> "$dir/count.err" &
  WATCH_PID=$!
  disown $!
}

send() {   # index command
  case "$1" in 0) echo "$2" >&5 ;; 1) echo "$2" >&6 ;; 2) echo "$2" >&7 ;; esac
}

last_total() { tail -n 1 "$SAMPLES" | awk '{print $2}'; }

wait_total_one() {   # seconds: waits until the latest sample says exactly one item
  local deadline=$(( $(date +%s) + $1 ))
  while [ "$(date +%s)" -lt "$deadline" ]; do
    [ "$(last_total)" = 1 ] && return 0
    sleep 0.2
  done
  return 1
}

# The demo that logged "hosting" last (and not "not-hosting" after it), leaving out index $1 if given.
host_index() {
  local i last
  for i in 0 1 2; do
    [ "$i" = "${1:-}" ] && continue
    last="$(grep -E '"event":"(hosting|not-hosting)"' "$DIR/log-${NAMES[$i]}" 2>/dev/null | tail -n 1)"
    case "$last" in *'"event":"hosting"'*) echo "$i"; return 0 ;; esac
  done
  return 1
}

# Over the samples taken after $1 (epoch), with the host of each moment taken from the demos' logs (pids in
# NAMES order): prints "<max suite> <longest gap> <last suite+own> <samples> <extra own> <double>".
#   suite = items of the host pid, own = items of the other pids, gap = first empty sample to next non-empty one
#   (or to the end of the window when none follows, so a run that never recovers is long), extra own = the most
#   consecutive samples in which a pid that is not the host had more items than its log says it shows (own icon),
#   double = the most consecutive samples in which that happened while the host also had its suite item (two suite
#   items at once; a new host's item shows a moment before its "hosting" line is logged, so one sample is skew).
analyse() {
  python3 -I - "$SAMPLES" "$1" "$DIR" "${PIDS[@]}" <<'PY'
import json, sys
samples, t0, folder, *pids = sys.argv[1:]
t0 = float(t0)
names = ["optimer", "devbox", "power"]
events = []   # (t, pid, event)
for name in names:
    try:
        for line in open(f"{folder}/log-{name}"):
            try:
                e = json.loads(line)
            except ValueError:
                continue
            events.append((e["t"], str(e["pid"]), e["event"]))
    except OSError:
        pass
events.sort()
rows = []
for line in open(samples):
    f = line.split()
    if len(f) == 2 + len(pids) and float(f[0]) >= t0:
        rows.append((float(f[0]), [int(x) for x in f[2:]]))
hosting, own_shown, k = {}, {}, 0   # pid -> latest hosting event time / own icon shown
max_suite = last = extra = run_extra = double = run_double = 0
gap = max_gap = 0.0
zero_since = None
for t, counts in rows:
    while k < len(events) and events[k][0] <= t:
        et, pid, ev = events[k]
        if ev == "hosting": hosting[pid] = et
        elif ev == "not-hosting": hosting.pop(pid, None)
        elif ev == "own-icon-shown": own_shown[pid] = True
        elif ev == "own-icon-hidden": own_shown[pid] = False
        k += 1
    host = max(hosting, key=hosting.get) if hosting else None
    suite = sum(c for p, c in zip(pids, counts) if p == host)
    other = sum(c for p, c in zip(pids, counts) if p != host)
    max_suite = max(max_suite, suite)
    last = suite + other
    if suite + other == 0:
        if zero_since is None: zero_since = t
        max_gap = max(max_gap, t - zero_since)
    else:
        if zero_since is not None: max_gap = max(max_gap, t - zero_since)
        zero_since = None
    surplus = any(c > (1 if own_shown.get(p) else 0) for p, c in zip(pids, counts) if p != host)
    run_extra = run_extra + 1 if surplus else 0
    extra = max(extra, run_extra)
    host_suite = sum(c - (1 if own_shown.get(p) else 0) for p, c in zip(pids, counts) if p == host)
    run_double = run_double + 1 if surplus and host_suite >= 1 else 0
    double = max(double, run_double)
if zero_since is not None and rows:
    max_gap = max(max_gap, rows[-1][0] - zero_since)
gap_text = "%.1f" % max_gap if max_gap > 0 else "0"
print(max_suite, gap_text, last, len(rows), extra, double)
PY
}

check_window() {   # run phase t0
  local run="$1" phase="$2" max zmax last n extra double gaptext
  read -r max zmax last n extra double <<< "$(analyse "$3")"
  gaptext="${zmax}s"
  [ "$zmax" = 0 ] && gaptext="< 0.1 s"
  if [ "$n" -lt 5 ]; then report "$run" "$phase" bad "too few samples ($n)"
  elif [ "$max" -ge 2 ]; then report "$run" "$phase" bad "two suite items at once (max $max)"
  elif [ "$double" -ge 2 ]; then report "$run" "$phase" bad "two suite items at once for $double samples"
  elif [ "$extra" -ge 8 ]; then report "$run" "$phase" bad "an unexplained item stayed for $extra samples"
  elif [ "$zmax" != 0 ] && awk "BEGIN{exit !($zmax > 2.0)}"; then report "$run" "$phase" bad "no item for ${zmax}s"
  elif [ "$last" != 1 ]; then report "$run" "$phase" bad "ended on $last items"
  else report "$run" "$phase" ok "suite items at most $max, longest gap $gaptext"
  fi
}

swiftc -O "$ROOT/scripts/suite-bar-count.swift" -o "$COUNT_BIN" 2>/dev/null || { echo "could not build suite-bar-count"; exit 2; }
"$COUNT_BIN" $$ > /dev/null || { echo "Accessibility permission is needed for this terminal"; exit 2; }

for run in $(seq 1 $RUNS); do
  # ---- A, B, C
  start_demos "$run" quit
  if wait_total_one 20; then
    sleep 3
    t0="$(now)"; sleep 3
    check_window "$run" A "$t0"
  else
    report "$run" A bad "never settled on one item (now $(last_total))"
  fi

  host="$(host_index)" || host=""
  if [ -z "$host" ]; then
    report "$run" B bad "no host in the logs"
  else
    t0="$(now)"
    send "$host" quit
    sleep 5
    check_window "$run" B "$t0"
  fi

  host2="$(host_index "$host")" || host2=""
  if [ -z "$host2" ]; then
    report "$run" C bad "no new host in the logs"
  else
    t0="$(now)"
    safe_signal KILL "${PIDS[$host2]}" "demo-${NAMES[$host2]}"
    sleep 6
    check_window "$run" C "$t0"
  fi
  stop_demos

  # ---- D, on a fresh set (the first one is down to one demo)
  start_demos "$run" freeze
  if wait_total_one 20; then
    sleep 2
    host="$(host_index)" || host=""
    frozen=""
    for i in 0 1 2; do [ "$i" != "$host" ] && frozen="$i" && break; done
    if [ -z "$host" ]; then
      report "$run" D bad "no host in the logs"
    else
      t0="$(now)"
      send "$frozen" freeze
      verdict=""
      deadline=$(( $(date +%s) + TTL + 5 ))
      while [ "$(date +%s)" -le "$deadline" ]; do
        if awk -v t0="$t0" -v key="${NAMES[$frozen]}=notRunning" '
             index($0, "\"event\":\"rows\"") && index($0, key) { match($0, /"t":[0-9.]+/); if (substr($0, RSTART + 4, RLENGTH - 4) + 0 >= t0) found = 1 }
             END { exit !found }' "$DIR/log-${NAMES[$host]}"; then verdict=ok; break; fi
        sleep 0.5
      done
      if [ "$verdict" = ok ]; then report "$run" D ok "${NAMES[$frozen]} turned notRunning in the host log"
      else report "$run" D bad "${NAMES[$frozen]} still running in the host log after $((TTL + 5)) s"; fi
    fi
  else
    report "$run" D bad "never settled on one item"
  fi
  stop_demos
done

if [ "$FAILURES" -eq 0 ]; then echo "takeover: PASS ($RUNS runs)"; exit 0; fi
echo "takeover: FAIL ($FAILURES failed checks)"; exit 1

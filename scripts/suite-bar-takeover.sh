#!/usr/bin/env bash
# Proof (suite bar plan, criteria 1 and 3): host takeover, measured on the real menu bar.
# Starts SuiteBarDemo three times (own temp suite folder, one copy of the binary per demo so each has its own
# defaults domain) and counts the menu bar items of the demo pids through Accessibility every 100 ms. A suite item is
# told from a demo's own icon by its accessibility description ("Ambysto suite, ..."), read from the menu bar itself,
# not from the demos' logs. Checks:
#   A  three fresh demos settle on the suite item alone (asserted: one suite item, no own icon);
#   B  `quit` (NSApp.terminate) to the host: at most one suite item in every sample, and the menu bar is never
#      without any demo item for more than 2 s; it ends on the suite item alone (two demos are still running);
#   C  `kill -9` of the new host: the same, ending on exactly one item (suite or own: one demo is left);
#   D  `freeze` one non-host: the host's log shows its row "notRunning" within ttl + 5 s.
# The gap is the time from the first sample with no item to the next sample with one ("none" when no 100 ms sample
# was empty). Three runs. Needs Accessibility
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

# Over the samples taken after $1 (epoch): prints "<max suite> <longest gap> <last total> <samples> <last suite> <last own>".
#   Each sample counts suite items and own icons of the demo pids directly (accessibility descriptions), so no log
#   timing is involved. gap = from the first sample with no item at all to the next sample with one (or to the end
#   of the window when none follows); "0" means no sample was ever empty.
analyse() {
  python3 -I - "$SAMPLES" "$1" <<'PY'
import sys
samples, t0 = sys.argv[1], float(sys.argv[2])
rows = []
for line in open(samples):
    f = line.split()
    if len(f) == 4 and float(f[0]) >= t0:
        rows.append((float(f[0]), int(f[1]), int(f[2])))
max_suite = last = last_suite = last_own = 0
max_gap = 0.0
zero_since = None
for t, total, suite in rows:
    max_suite = max(max_suite, suite)
    last, last_suite, last_own = total, suite, total - suite
    if total == 0:
        if zero_since is None: zero_since = t
    elif zero_since is not None:
        max_gap = max(max_gap, t - zero_since)
        zero_since = None
if zero_since is not None and rows:
    max_gap = max(max_gap, rows[-1][0] - zero_since)
print(max_suite, "%.1f" % max_gap if max_gap > 0 else "0", last, len(rows), last_suite, last_own)
PY
}

check_window() {   # run phase t0 [expected end: suite | any]
  local run="$1" phase="$2" end="${4:-any}" max zmax last n lsuite lown gaptext
  read -r max zmax last n lsuite lown <<< "$(analyse "$3")"
  gaptext="${zmax}s"
  [ "$zmax" = 0 ] && gaptext="none"
  if [ "$n" -lt 5 ]; then report "$run" "$phase" bad "too few samples ($n)"
  elif [ "$max" -ge 2 ]; then report "$run" "$phase" bad "two suite items at once in at least one sample"
  elif [ "$zmax" != 0 ] && awk "BEGIN{exit !($zmax > 2.0)}"; then report "$run" "$phase" bad "no item for ${zmax}s"
  elif [ "$last" != 1 ]; then report "$run" "$phase" bad "ended on $last items"
  elif [ "$end" = suite ] && { [ "$lsuite" != 1 ] || [ "$lown" != 0 ]; }; then
    report "$run" "$phase" bad "ended on $lsuite suite item(s) and $lown own icon(s), expected the suite item alone"
  else report "$run" "$phase" ok "suite items at most $max in every 100 ms sample, empty menu bar: $gaptext"
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
    check_window "$run" A "$t0" suite
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
    check_window "$run" B "$t0" suite
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

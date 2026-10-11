#!/usr/bin/env bash
# Proof (suite bar plan, criterion 6): the host's CPU. Starts a host and two other SuiteBarDemo copies on a temp
# suite folder, samples each pid with `top` for 60 s, prints the average CPU of the host and of the non-hosts.
# PASS when the host's average is below 0.5 %. Signals only demo pids it started (command name checked) and
# deletes only its own temp directory. No Accessibility permission needed.
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SECONDS_TO_SAMPLE=60
LIMIT=0.5
NAMES=(optimer devbox power)

swift build -q --package-path "$ROOT" -c release --product SuiteBarDemo || { echo "build failed"; exit 2; }
DEMO_BIN="$(swift build --package-path "$ROOT" -c release --show-bin-path)/SuiteBarDemo"

TMP="$(mktemp -d "${TMPDIR:-/tmp}/suite-bar-cpu.XXXXXX")"
PIDS=()
trap cleanup EXIT

safe_signal() {   # signal pid expected-name
  local comm
  comm="$(ps -p "$2" -o comm= 2>/dev/null)" || return 0
  [ "$(basename "$comm")" = "$3" ] || return 0
  kill "-$1" "$2" 2>/dev/null || true
}

cleanup() {
  local i
  for i in 0 1 2; do
    [ "${PIDS[$i]:-}" ] && safe_signal TERM "${PIDS[$i]}" "demo-${NAMES[$i]}"
  done
  sleep 0.3
  case "$TMP" in
    "${TMPDIR:-/tmp}"/suite-bar-cpu.*) rm -rf "$TMP" ;;
  esac
}

for i in 0 1 2; do
  cp "$DEMO_BIN" "$TMP/demo-${NAMES[$i]}"   # a copy per demo: its own defaults domain
  "$TMP/demo-${NAMES[$i]}" --folder "$TMP/suite" --app "${NAMES[$i]}" --metric "Load=$((10 + i * 10))%" \
    < /dev/null > "$TMP/log-${NAMES[$i]}" 2> "$TMP/err-${NAMES[$i]}" &
  PIDS+=($!)
  disown $!
done

# Wait for a host (up to 30 s).
host=""
for _ in $(seq 1 60); do
  for i in 0 1 2; do
    last="$(grep -E '"event":"(hosting|not-hosting)"' "$TMP/log-${NAMES[$i]}" 2>/dev/null | tail -n 1)"
    case "$last" in *'"event":"hosting"'*) host="$i" ;; esac
  done
  [ -n "$host" ] && break
  sleep 0.5
done
[ -n "$host" ] || { echo "FAIL: no demo became the host"; exit 1; }
sleep 5
echo "host: ${NAMES[$host]} (pid ${PIDS[$host]}); sampling ${SECONDS_TO_SAMPLE} s"

TOPS=()
for i in 0 1 2; do
  top -l $((SECONDS_TO_SAMPLE + 1)) -s 1 -pid "${PIDS[$i]}" -stats pid,cpu > "$TMP/top-${NAMES[$i]}" 2>/dev/null &
  TOPS+=($!)
done
wait "${TOPS[@]}"

host_avg=""
others=""
for i in 0 1 2; do
  # The first sample of top is "since the process started", so it is dropped.
  avg="$(awk -v pid="${PIDS[$i]}" '$1 == pid && $2 ~ /^[0-9.]+$/ { n++; if (n > 1) { sum += $2; m++ } } END { if (m) printf "%.3f", sum / m; else print "n/a" }' "$TMP/top-${NAMES[$i]}")"
  if [ "$i" = "$host" ]; then host_avg="$avg"; else others="$others ${NAMES[$i]}=$avg"; fi
done
echo "host average CPU:     ${host_avg} %"
echo "non-host average CPU:${others} %"
if [ "$host_avg" != "n/a" ] && awk "BEGIN{exit !($host_avg < $LIMIT)}"; then
  echo "cpu: PASS (host below $LIMIT %)"; exit 0
fi
echo "cpu: FAIL (host not below $LIMIT %)"; exit 1

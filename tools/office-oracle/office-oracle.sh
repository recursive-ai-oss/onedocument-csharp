#!/usr/bin/env bash
# Drives desktop Office on a Windows machine and brings the answers back.
#
#   office-oracle.sh install
#   office-oracle.sh run --run NAME [--instance ID] [--out DIR] [--probe] [--pdf] [--saveas]
#                        [--lock-path WINPATH] [--lock-retry SECONDS] [--lock-wait MINUTES] [--timeout SECONDS]
#                        [--trace] [--no-watcher] [--keep] FILE|DIR...
#
# Environment:
#   OFFICE_ORACLE_SSH       ssh destination of the Windows machine, e.g. user@host (required)
#   OFFICE_ORACLE_SSH_KEY   private key file (optional)
#   ONEOFFICE_HOME          results go to $ONEOFFICE_HOME/results/office/NAME unless --out is given
#
# Only files that were generated may be sent: nothing from a downloaded corpus. The machine's Office needs the
# signed-in user's desktop, so the checks run from a scheduled task (see laptop/Install.ps1); ssh only places
# files, starts the task and collects the results. Everything lives under <profile>\oneoffice on the machine;
# the only other file touched is the shared lock <profile>\oracle.lock, created and removed by the task.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
: "${OFFICE_ORACLE_SSH:?set OFFICE_ORACLE_SSH to user@host}"
ssh_opts=(-o BatchMode=yes -o ConnectTimeout=15)
[ -n "${OFFICE_ORACLE_SSH_KEY:-}" ] && ssh_opts+=(-i "$OFFICE_ORACLE_SSH_KEY")

remote() { ssh "${ssh_opts[@]}" "$OFFICE_ORACLE_SSH" "$@"; }
profile=$(remote 'echo %USERPROFILE%' | tr -d '\r')
base_win="$profile\\oneoffice"
base_scp="${profile//\\//}/oneoffice"

cmd=${1:-}; shift || true

case "$cmd" in
install)
  remote "mkdir $base_win\\driver 2>nul & mkdir $base_win\\jobs 2>nul & echo ok" >/dev/null
  scp -q "${ssh_opts[@]}" "$here/laptop/Run-Oracle.ps1" "$here/laptop/Install.ps1" "$OFFICE_ORACLE_SSH:$base_scp/driver/"
  remote "powershell -NoProfile -ExecutionPolicy Bypass -File $base_win\\driver\\Install.ps1 -Base $base_win"
  ;;
run)
  run=""; instance="${ONEOFFICE_INSTANCE:-oneoffice}"; out=""; mode=open; pdf=false; saveas=false
  lock=""; retry=60; wait_min=20; timeout=1800; keep=false; trace=false; nowatch=false; walerts=-1; pdfvia=export; inputs=()
  while [ $# -gt 0 ]; do
    case "$1" in
      --run) run=$2; shift 2;; --instance) instance=$2; shift 2;; --out) out=$2; shift 2;;
      --probe) mode=probe; shift;; --pdf) pdf=true; shift;; --saveas) saveas=true; shift;;
      --lock-path) lock=$2; shift 2;; --lock-retry) retry=$2; shift 2;; --lock-wait) wait_min=$2; shift 2;;
      --timeout) timeout=$2; shift 2;; --keep) keep=true; shift;; --trace) trace=true; shift;; --pdf-via) pdfvia=$2; shift 2;; --word-alerts) walerts=$2; shift 2;; --no-watcher) nowatch=true; shift;;
      *) inputs+=("$1"); shift;;
    esac
  done
  [ -n "$run" ] || { echo "--run is required" >&2; exit 2; }
  [ ${#inputs[@]} -gt 0 ] || { echo "no input files" >&2; exit 2; }
  out=${out:-${ONEOFFICE_HOME:-$HOME/oneoffice}/results/office/$run}
  stage=$(mktemp -d "${TMPDIR:-/var/tmp}/office-oracle.XXXXXX")
  trap 'rm -rf "$stage"' EXIT
  mkdir -p "$stage/in" "$out"
  for i in "${inputs[@]}"; do
    if [ -d "$i" ]; then cp -r "$i"/. "$stage/in/"; else cp "$i" "$stage/in/"; fi
  done
  python3 - "$stage" "$run" "$instance" "$mode" "$pdf" "$saveas" "$lock" "$retry" "$wait_min" "$trace" "$nowatch" "$walerts" "$pdfvia" <<'EOF'
import json, os, sys
stage, run, instance, mode, pdf, saveas, lock, retry, wait_min, trace, nowatch, walerts, pdfvia = sys.argv[1:]
items = []
for n in sorted(os.listdir(stage + "/in")):
    if not os.path.isfile(stage + "/in/" + n) or n.endswith(".actions.txt"):
        continue
    it = {"file": "in\\" + n, "mode": mode, "exportPdf": pdf == "true", "saveAs": saveas == "true"}
    side = stage + "/in/" + n.rsplit(".", 1)[0] + ".actions.txt"
    if os.path.exists(side):
        it["actions"] = [l.strip() for l in open(side) if l.strip()]
    items.append(it)
job = {"run": run, "instance": instance, "maxMinutes": 25, "lockRetrySeconds": int(retry),
       "lockWaitMinutes": int(wait_min), "items": items}
job["wordAlerts"] = int(walerts)
job["pdfVia"] = pdfvia
job["trace"] = trace == "true"
job["noWatcher"] = nowatch == "true"
if lock:
    job["lockPath"] = lock
json.dump(job, open(stage + "/job.json", "w"), indent=1)
EOF
  remote "rmdir /s /q $base_win\\jobs\\$run 2>nul & mkdir $base_win\\jobs\\$run" >/dev/null
  scp -q -r "${ssh_opts[@]}" "$stage/in" "$stage/job.json" "$OFFICE_ORACLE_SSH:$base_scp/jobs/$run/"
  remote "powershell -NoProfile -Command \"[IO.File]::WriteAllText('$base_win\\jobs\\current.txt', '$base_win\\jobs\\$run')\"" >/dev/null
  remote "schtasks /run /tn OneOfficeOracle" >/dev/null
  echo "started $run on the Windows machine; waiting up to ${timeout}s" >&2
  t=0
  until [ "$(remote "if exist $base_win\\jobs\\$run\\out\\done.json (echo yes) else (echo no)" | tr -d '\r')" = yes ]; do
    sleep 5; t=$((t+5))
    [ $t -lt "$timeout" ] || { echo "timed out after ${timeout}s; job left in place" >&2; exit 3; }
  done
  scp -q -r "${ssh_opts[@]}" "$OFFICE_ORACLE_SSH:$base_scp/jobs/$run/out/." "$out/"
  $keep || remote "rmdir /s /q $base_win\\jobs\\$run" >/dev/null
  echo "results in $out" >&2
  ;;
*)
  echo "usage: $0 install | run --run NAME [options] FILE|DIR..." >&2; exit 2;;
esac

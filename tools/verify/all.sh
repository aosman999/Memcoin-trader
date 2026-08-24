#!/usr/bin/env bash
# Everything that must pass before a cBot is sent to the owner.
#   1. it compiles (with a negative control, so the check can fail)
#   2. it BEHAVES — runs against a simulated broker, trades, sizes, stops
#   3. the behaviour simulation itself has teeth (faults are caught)
#   4. the C# and the backtester agree bar for bar on the shared maths
#   5. the Python package's own tests still pass
set -uo pipefail
cd "$(dirname "$0")/../.."
BOT="${1:-tools/GoldEdgeNews.cs}"
fail=0
step() { echo; echo "=== $1"; }

step "1/5  compile + compiler negative control"
./tools/verify/build-check.sh "$BOT" || fail=1

step "2/5  end-to-end behaviour simulation"
./tools/verify/bot-sim.sh "$BOT" || fail=1

step "3/5  behaviour-simulation negative controls"
python3 tools/verify/bot-sim-negcontrol.py || fail=1

step "4/5  C# vs backtester port test"
if [ -n "${PORT_TEST_LAB:-}" ]; then
    python3 tools/verify/port-test-regime.py || fail=1
else
    echo "SKIPPED — set PORT_TEST_LAB to the Strategy Lab scratch dir to run it."
fi

step "5/5  python unit tests"
python3 -m unittest discover -s tests || fail=1

echo
if [ "$fail" -eq 0 ]; then echo "########  ALL VERIFICATION PASSED  ########"; else
  echo "########  VERIFICATION FAILED  ########"; fi
exit $fail

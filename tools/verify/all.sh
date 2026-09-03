#!/usr/bin/env bash
# Everything that must pass before a cBot is sent to the owner.
#   1. it compiles (with a negative control, so the check can fail)
#   2. it BEHAVES — runs against a simulated broker, trades, sizes, stops
#   3. the behaviour simulation itself has teeth (faults are caught)
#   4. the C# and the backtester agree bar for bar on the shared maths
#   5. the news-only bot compiles, parses real feed shapes and scores them
#   6. the Python package's own tests still pass
set -uo pipefail
cd "$(dirname "$0")/../.."
BOT="${1:-tools/GoldEdgeNews.cs}"
fail=0
step() { echo; echo "=== $1"; }

step "1/6  compile + compiler negative control"
./tools/verify/build-check.sh "$BOT" || fail=1

step "2/6  end-to-end behaviour simulation"
./tools/verify/bot-sim.sh "$BOT" || fail=1

step "3/6  behaviour-simulation negative controls"
python3 tools/verify/bot-sim-negcontrol.py || fail=1

step "4/6  C# vs backtester port test"
if [ -n "${PORT_TEST_LAB:-}" ]; then
    python3 tools/verify/port-test-regime.py || fail=1
else
    echo "SKIPPED — set PORT_TEST_LAB to the Strategy Lab scratch dir to run it."
fi

step "5/6  GoldNewsWatch + GoldICT + GoldDataDump: compile + behaviour tests + negative controls"
./tools/verify/build-check.sh tools/GoldNewsWatch.cs || fail=1
./tools/verify/build-check.sh tools/GoldDataDump.cs || fail=1
./tools/verify/build-check.sh tools/GoldICT.cs || fail=1
./tools/verify/news-test.sh || fail=1

step "6/6  python unit tests"
python3 -m unittest discover -s tests || fail=1

echo
if [ "$fail" -eq 0 ]; then echo "########  ALL VERIFICATION PASSED  ########"; else
  echo "########  VERIFICATION FAILED  ########"; fi
exit $fail

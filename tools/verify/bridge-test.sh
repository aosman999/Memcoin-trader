#!/usr/bin/env bash
# Runs the signal-bridge fidelity test, then breaks the emitter on purpose and
# requires the test to catch each break.
#
# The Telegram channel is fed by the file GoldICT writes. If that file can
# disagree with the orders, the channel can disagree with cTrader — which is the
# one thing the bridge exists to prevent. So the checks that police it have to
# be provably able to fail.
set -uo pipefail
cd "$(dirname "$0")/../.."
BOT="tools/GoldICT.cs"
OUT=$(mktemp -d)
trap 'rm -rf "$OUT"' EXIT

echo "building the bridge simulation ..."
if ! mcs -target:exe -out:"$OUT/bridge.exe" \
      tools/verify/calgo_sim.cs tools/verify/signal_bridge_driver.cs "$BOT" 2>"$OUT/err"; then
    echo "BRIDGE BUILD FAILED:"; cat "$OUT/err"; exit 1
fi
mono "$OUT/bridge.exe" || exit 1

echo
echo "NEGATIVE CONTROLS — a lying feed must NOT pass"
fail=0
run_fault() {
    local name="$1" old="$2" new="$3"
    if ! python3 - "$BOT" "$OUT/broken.cs" "$old" "$new" <<'PY'
import sys
src = open(sys.argv[1]).read()
old, new = sys.argv[3], sys.argv[4]
if old not in src:
    sys.stderr.write("STALE ANCHOR\n")
    sys.exit(2)
open(sys.argv[2], "w").write(src.replace(old, new, 1))
PY
    then
        echo "  SETUP FAIL  $name (patch target gone — control protects nothing)"
        fail=$((fail+1)); return
    fi
    if mcs -target:exe -out:"$OUT/b.exe" tools/verify/calgo_sim.cs \
           tools/verify/signal_bridge_driver.cs "$OUT/broken.cs" >/dev/null 2>&1 \
       && mono "$OUT/b.exe" >/dev/null 2>&1; then
        echo "  MISSED  $name — the broken feed PASSED"
        fail=$((fail+1))
    else
        echo "  CAUGHT  $name"
    fi
}

run_fault "take profits posted in reverse order (TP3 sold as TP1)" \
    'var tpJson = "[" + string.Join(",", tpPrices.Select(N).ToArray()) + "]";' \
    'tpPrices.Reverse(); var tpJson = "[" + string.Join(",", tpPrices.Select(N).ToArray()) + "]";'

run_fault "channel told BUY when the order was a SELL" \
    '                     "side", Q(s.Direction > 0 ? "BUY" : "SELL"),
                     "entry", N(price),' \
    '                     "side", Q(s.Direction > 0 ? "SELL" : "BUY"),
                     "entry", N(price),'

run_fault "stop posted as the entry price" \
    '                     "stop", N(stopPrice),' \
    '                     "stop", N(price),'

run_fault "only the last take profit reported (two rungs go unannounced)" \
    "                tpPrices.Add(tpPrice);" \
    "                tpPrices.Clear(); tpPrices.Add(tpPrice);"

run_fault "every exit announced as a take profit, losses included" \
    '            var reason = hitTp ? "tp" : (profit < 0 ? "sl" : "close");' \
    '            var reason = "tp";'

run_fault "every exit announced as rung 1" \
    '                 "rung", r.Index.ToString(),' \
    '                 "rung", "1",'

run_fault "rung rows never cleared, so every close is announced again" \
    "                EmitClose(id, _rungs[id]);
                _rungs.Remove(id);" \
    "                EmitClose(id, _rungs[id]);"

run_fault "a demo account reported to the channel as live" \
    'sb.Append(",\"demo\":").Append(Account.IsLive ? "false" : "true");' \
    'sb.Append(",\"demo\":").Append("false");'

run_fault "position ids left out, so the feed cannot be reconciled" \
    "                    ids.Add(res.Position.Id);" \
    "                    { }"

run_fault "vacuum window re-announced on every news poll" \
    "            return now > armedUntil;" \
    "            return true;"

run_fault "feed written even when it is switched off" \
    "            if (!EmitSignalFeed || _feedBroken)
                return;" \
    "            if (_feedBroken)
                return;"

echo
if [ "$fail" -eq 0 ]; then
    echo "all controls held — the bridge test has teeth."
else
    echo "$fail control(s) did not hold."
fi
exit "$fail"

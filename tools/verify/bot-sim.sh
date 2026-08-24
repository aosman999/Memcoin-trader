#!/usr/bin/env bash
# Runs the real cBot against a simulated broker and asserts on its BEHAVIOUR.
# Compiling proves the file parses. This proves it trades, sizes and stops
# correctly, refuses live accounts, and survives odd charts.
set -uo pipefail
cd "$(dirname "$0")/../.."
BOT="${1:-tools/GoldEdgeNews.cs}"
OUT=$(mktemp -d)
trap 'rm -rf "$OUT"' EXIT

echo "building simulation of $BOT ..."
if ! mcs -target:exe -out:"$OUT/sim.exe" \
      tools/verify/calgo_sim.cs tools/verify/bot_sim_driver.cs "$BOT" 2>"$OUT/err"; then
    echo "SIMULATION BUILD FAILED:"; cat "$OUT/err"; exit 1
fi
mono "$OUT/sim.exe"

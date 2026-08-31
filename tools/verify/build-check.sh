#!/usr/bin/env bash
# Compile-check a cTrader cBot offline, before anyone spends a build cycle in
# cTrader (or worse, discovers the error on a live chart).
#
# Brace-balance checking, which is what this project used to rely on, catches
# almost nothing: it misses undefined names, wrong types, bad overloads, and
# duplicate locals. A real compiler catches all of them. cAlgo.API is not
# available offline, so calgo_stubs.cs provides the signatures the bots use.
#
#   ./tools/verify/build-check.sh tools/GoldEdgeNews.cs
#
# Requires mono: apt-get install -y mono-mcs
set -euo pipefail
BOT="${1:-tools/GoldEdgeNews.cs}"
DIR="$(cd "$(dirname "$0")" && pwd)"
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

command -v mcs >/dev/null || { echo "mcs not found: apt-get install -y mono-mcs"; exit 2; }
[ -f "$BOT" ] || { echo "no such file: $BOT"; exit 2; }

echo "compiling $BOT against cAlgo.API stubs (warnings are errors)..."
if mcs -target:library -warnaserror -warn:4 -out:"$OUT/bot.dll" \
       "$DIR/calgo_stubs.cs" "$BOT"; then
    echo "OK — $BOT compiles clean."
else
    echo "FAILED — do not send this file."; exit 1
fi

# A build that cannot fail proves nothing. Confirm the check has teeth by
# introducing a deliberate error and requiring the compiler to reject it.
# Anchor on something EVERY cBot has. The previous anchor was a field unique to
# GoldEdgeNews, so on any other bot sed changed nothing, the untouched file
# compiled, and the control reported a pass while protecting nothing.
sed 's/protected override void OnStart()/private double _negctl = "not a double";\n        protected override void OnStart()/' \
    "$BOT" > "$OUT/neg.cs"
if cmp -s "$BOT" "$OUT/neg.cs"; then
    echo "NEGATIVE CONTROL IS STALE — could not inject a fault into $BOT."
    echo "The compile above proves nothing. Fix the anchor in build-check.sh."
    exit 1
fi
if mcs -target:library -out:"$OUT/neg.dll" "$DIR/calgo_stubs.cs" "$OUT/neg.cs" 2>/dev/null; then
    echo "WARNING: negative control PASSED — the stubs are too loose to trust."
    exit 1
fi
echo "negative control correctly rejected — the check has teeth."

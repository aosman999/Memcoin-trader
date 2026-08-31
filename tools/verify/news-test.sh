#!/usr/bin/env bash
# Behaviour tests for GoldNewsWatch, plus negative controls. A test suite that
# cannot fail is decoration; each fault below must be caught.
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
REPO="$(dirname "$(dirname "$DIR")")"
BOT="$REPO/tools/GoldNewsWatch.cs"
OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

mcs -target:exe -out:"$OUT/nt.exe" "$DIR/calgo_stubs.cs" "$BOT" "$DIR/newswatch_test.cs"
mono "$OUT/nt.exe"

echo
echo "NEGATIVE CONTROLS — a broken news bot must NOT pass"
fail=0
run_fault() {
    local name="$1" old="$2" new="$3"
    python3 - "$BOT" "$OUT/broken.cs" "$old" "$new" <<'PY'
import sys
src=open(sys.argv[1]).read(); old,new=sys.argv[3],sys.argv[4]
if old not in src:
    sys.stderr.write("STALE ANCHOR\n"); sys.exit(2)
open(sys.argv[2],"w").write(src.replace(old,new,1))
PY
    if [ $? -ne 0 ]; then
        echo "  SETUP FAIL  $name (patch target gone — control protects nothing)"
        fail=$((fail+1)); return
    fi
    if mcs -target:exe -out:"$OUT/b.exe" "$DIR/calgo_stubs.cs" "$OUT/broken.cs" \
           "$DIR/newswatch_test.cs" >/dev/null 2>&1 && mono "$OUT/b.exe" >/dev/null 2>&1; then
        echo "  MISSED  $name — the broken bot PASSED"
        fail=$((fail+1))
    else
        echo "  CAUGHT  $name"
    fi
}

run_fault "direction sign flipped (buys the ceasefire, sells the war)" \
          "dir += kv.Value;" "dir -= kv.Value;"
run_fault "relevance gate removed (alerts on any loaded word)" \
          "if (relevant == 0)" "if (false)"
run_fault "channel title treated as a story" \
          "if (first) { first = false; continue; }" "if (first) { first = false; }"
run_fault "CDATA left unwrapped" \
          's.Replace("<![CDATA[", "").Replace("]]>", "")' "s"
run_fault "calendar keeps every event, including ones gold ignores" \
          "else continue;" "else tier = 3;"
run_fault "the Fed shorthand dropped again" \
          '" FED ",' ""

echo
if [ "$fail" -ne 0 ]; then
    echo "$fail NEGATIVE CONTROL(S) MISSED — the news tests are weaker than they look."
    exit 1
fi
echo "ALL NEWS NEGATIVE CONTROLS CAUGHT — the news tests have teeth."

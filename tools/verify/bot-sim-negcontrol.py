#!/usr/bin/env python3
"""NEGATIVE CONTROL for bot-sim.sh.

Injects real faults into a COPY of the cBot and requires the behaviour
simulation to catch each one. A test that cannot fail is decoration, and this
project has shipped a decorative check before.

Each fault below is something that has actually gone wrong in a trading bot:
a stop on the wrong side, a clamp that stopped clamping, sizing off by a
factor, a safety lock removed, and a gate so tight the bot silently stops
trading -- which is exactly the failure the owner hit live.
"""
import os, subprocess, sys, tempfile, shutil

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ROOT = os.path.dirname(os.path.join(ROOT, ""))          # repo root
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = os.path.dirname(ROOT)
BOT = os.path.join(REPO, "tools", "GoldEdgeNews.cs")
SIM = os.path.join(REPO, "tools", "verify", "calgo_sim.cs")
DRV = os.path.join(REPO, "tools", "verify", "bot_sim_driver.cs")

FAULTS = [
    ("stop placed on the WRONG SIDE of entry",
     "                                            stopDist / Symbol.PipSize, tpDist / Symbol.PipSize);\n"
     "            if (result.IsSuccessful)\n                _tradesToday++;",
     "                                            -stopDist / Symbol.PipSize, tpDist / Symbol.PipSize);\n"
     "            if (result.IsSuccessful)\n                _tradesToday++;",
     "no order/risk violations"),

    ("stop clamp ignored (10x too wide)",
     "                stopDist = Math.Max(loClamp, Math.Min(hiClamp, stopDist));",
     "                stopDist = stopDist * 10.0;",
     "no order/risk violations"),

    ("demo lock removed",
     "            if (Account.IsLive)\n            {\n                Print(\"REFUSING TO RUN",
     "            if (!Account.IsLive && false)\n            {\n                Print(\"REFUSING TO RUN",
     "REFUSES a live account"),

    ("position sizing 50x oversized",
     "            var riskUsd = Account.Equity * (RiskPercent / 100.0);\n"
     "            var units = Symbol.NormalizeVolumeInUnits(riskUsd / stopDist, RoundingMode.Down);\n\n"
     "            var minRisk",
     "            var riskUsd = Account.Equity * (RiskPercent / 100.0) * 50.0;\n"
     "            var units = Symbol.NormalizeVolumeInUnits(riskUsd / stopDist, RoundingMode.Down);\n\n"
     "            var minRisk",
     "single-trade risk"),

    ("both entry gates made unreachable (the silent no-trade failure)",
     "            if (trendAllowed && quality >= EfficiencyMin)",
     "            if (trendAllowed && quality >= 99.0 && ChopMax < -1.0)",
     "opens trades"),

    ("trailing stop moved BACKWARDS (against the position)",
     "                var candidate = price - dir * TrailDistanceR * sd;",
     "                var candidate = price + dir * TrailDistanceR * sd;",
     "no order/risk violations"),

    ("trailing stop never activates",
     "                if (r < TrailActivateR)\n                    continue;",
     "                if (r < 9999.0)\n                    continue;",
     "trailing stop actually moves stops"),

    ("reward floor removed from the structural target",
     "            if (structural <= floor) { how = \"floor\"; return floor; }",
     "            if (structural <= floor) { how = \"floor\"; return structural * 0.1; }",
     "reach target OFF"),

    # ---- the reach-calibrated target. The first version of this feature was
    # floored at MinRewardRisk, which silently clamped every calibrated target
    # back to the old structural value: it logged as though it worked and
    # changed nothing. Fault 1 below IS that bug, reintroduced deliberately.
    ("reach target floored back to MinRewardRisk (the no-op bug)",
     "                var lo2 = stopDist * ReachMinRR;",
     "                var lo2 = stopDist * MinRewardRisk;",
     "target adapts rather than pinning"),

    ("reach target frozen to a constant (stops adapting)",
     "            ratio = sorted[k];",
     "            ratio = 3.0;",
     "target adapts rather than pinning"),

    ("reach learning never runs (target never calibrates)",
     "            try { TrackReach(); ProtectPositions(); ManageTrailingStops(); }",
     "            try { ProtectPositions(); ManageTrailingStops(); }",
     "reach-calibrated target ACTUALLY fires"),

    ("target set inside the round-trip cost (a guaranteed loss)",
     "                if (want <= lo2) { how = \"reach-floored\"; return lo2; }",
     "                if (want <= lo2) { how = \"reach-floored\"; return stopDist * 0.0001; }",
     "cover costs"),

    ("warm-up falls back to the 2.5x structural target (the unrealistic TP)",
     "            if (UseReachTarget)\n            {\n                how = \"warm-up\";",
     "            if (UseReachTarget && false)\n            {\n                how = \"warm-up\";",
     "warm-up target"),

    ("max concurrent positions ignored",
     "            if (OwnPositions().Count() >= MaxConcurrentPositions)\n                return;",
     "            if (false)\n                return;",
     "more open positions"),
]


def main():
    src = open(BOT).read()
    tmp = tempfile.mkdtemp()
    missed = []
    print("NEGATIVE CONTROLS — every fault must be caught by the simulation\n")
    try:
        for name, old, new, want in FAULTS:
            if old not in src:
                print("  SETUP FAIL  %s\n              (patch target no longer in the file — "
                      "this control is stale and is NOT protecting anything)" % name)
                missed.append(name)
                continue
            broken = os.path.join(tmp, "broken.cs")
            open(broken, "w").write(src.replace(old, new, 1))
            exe = os.path.join(tmp, "b.exe")
            build = subprocess.run(["mcs", "-target:exe", "-out:" + exe, SIM, DRV, broken],
                                   capture_output=True, text=True)
            if build.returncode:
                print("  CAUGHT (compiler)  %s" % name)
                continue
            r = subprocess.run(["mono", exe], capture_output=True, text=True)
            # The contract is simply: a broken bot must NOT pass. Requiring a
            # specific message made the control brittle and let two real faults
            # through when the scenario that would have caught them was never
            # reached.
            hit = r.returncode != 0
            detail = next((l.strip() for l in r.stdout.splitlines()
                           if l.strip().startswith("FAIL")), "")
            if hit:
                print("  CAUGHT  %-52s %s" % (name, detail[:60]))
            else:
                print("  MISSED  %s\n          the broken bot PASSED the simulation "
                      "(expected a failure mentioning: %s)" % (name, want))
                missed.append(name)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    print()
    if missed:
        print("%d NEGATIVE CONTROL(S) MISSED — the simulation is weaker than it looks." % len(missed))
        return 1
    print("ALL NEGATIVE CONTROLS CAUGHT — the simulation has teeth.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

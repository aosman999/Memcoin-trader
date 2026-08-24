"""Port test: run the SHIPPED C# regime code against the SHIPPED Python harness.

Run with the Strategy Lab scratch scripts on the path:
    PORT_TEST_LAB=<dir with lab.py/partI.py> PORT_TEST_TMP=<scratch> \
        python3 tools/verify/port-test-regime.py


The methods are extracted verbatim from tools/GoldEdgeNews.cs by text, not
retyped, so a transcription slip between the backtest and the cBot cannot hide
here. Both sides are fed the identical price series and must agree bar by bar
on: trend quality, the random-walk floor, and the TRENDING/MEAN-REVERTING call.
"""
import json, math, re, subprocess, statistics as st, sys, os

SRC = os.path.join(os.path.dirname(os.path.dirname(
    os.path.dirname(os.path.abspath(__file__)))), "tools", "GoldEdgeNews.cs")
HERE = os.environ.get("PORT_TEST_TMP", "/tmp")
sys.path.insert(0, os.environ.get("PORT_TEST_LAB", HERE))
from partI import minute_path

E5 = (24, 36, 48, 60, 72)


def extract(name, text):
    """Pull one method, brace-balanced, starting at its signature line."""
    m = re.search(r"^[ \t]*private [^\n]*\b%s\(" % re.escape(name), text, re.M)
    if not m:
        raise SystemExit("method not found: " + name)
    i = text.index("{", m.start())
    depth, j = 0, i
    while True:
        if text[j] == "{":
            depth += 1
        elif text[j] == "}":
            depth -= 1
            if depth == 0:
                break
        j += 1
    return text[m.start():j + 1]


src = open(SRC).read()
methods = "\n\n".join(extract(n, src) for n in
                      ("TrendQualityAt", "TrendQuality", "RandomWalkFloor",
                       "Median", "RegimeMinSamples", "UpdateRegime", "PrimeRegimeHistory"))
# the one line of shipped state the methods read
ens = re.search(r"private static readonly int\[\] EnsembleWindows = \{[^}]*\};", src).group(0)

# minute path -> 15m closes, same construction as the harness
mins = minute_path(0.42, 4242)[:22 * 1440]
closes = mins[::15]

harness = r"""
using System;
using System.Collections.Generic;

public class Series {
    public List<double> V = new List<double>();
    public double Last(int i) { return V[V.Count - 1 - i]; }
    public int Count { get { return V.Count; } }
}
public class BarsStub { public Series ClosePrices = new Series(); }

public class Port {
    __ENS__
    public BarsStub Bars = new BarsStub();
    public bool UseEnsembleQuality = true;
    public int EfficiencyWindow = 48;
    public int RegimeWindow = 300;
    public double RegimeMargin = 0.005;
    public bool UseRegimeSwitch = true;

    private readonly List<double> _regimeHistory = new List<double>();
    private double _regimeMedian;
    private bool _regimeTrending = true;
    private bool _regimeKnown;
    private string _regimeLogged = "";
    private void Print(string f, params object[] a) { }

__METHODS__

    public static void Main(string[] argv) {
        var p = new Port();
        var all = new List<double>();
        foreach (var line in System.IO.File.ReadAllLines(argv[0]))
            all.Add(double.Parse(line, System.Globalization.CultureInfo.InvariantCulture));
        var outLines = new List<string>();
        Console.WriteLine("FLOOR\t" + p.RandomWalkFloor().ToString("R"));
        for (var k = 0; k < all.Count; k++) {
            p.Bars.ClosePrices.V.Add(all[k]);
            if (p.Bars.ClosePrices.Count < 210) continue;
            var q = p.TrendQualityAt(0);
            p.UpdateRegime(q);
            outLines.Add(k.ToString() + "\t" + q.ToString("R") + "\t" +
                         (p._regimeKnownPub ? (p._regimeTrendingPub ? "T" : "M") : "?") + "\t" +
                         p._regimeMedianPub.ToString("R"));
        }
        System.IO.File.WriteAllLines(argv[1], outLines);
    }
    public bool _regimeKnownPub { get { return _regimeKnown; } }
    public bool _regimeTrendingPub { get { return _regimeTrending; } }
    public double _regimeMedianPub { get { return _regimeMedian; } }
}
"""
harness = harness.replace("__ENS__", ens).replace("__METHODS__", methods)
open(os.path.join(HERE, "port.cs"), "w").write(harness)
open(os.path.join(HERE, "port_closes.txt"), "w").write(
    "\n".join(repr(x) for x in closes))

r = subprocess.run(["mcs", "-out:" + os.path.join(HERE, "port.exe"),
                    os.path.join(HERE, "port.cs")],
                   capture_output=True, text=True)
if r.returncode:
    print(r.stdout, r.stderr)
    raise SystemExit("C# harness failed to compile")
r = subprocess.run(["mono", os.path.join(HERE, "port.exe"),
                    os.path.join(HERE, "port_closes.txt"),
                    os.path.join(HERE, "port_out.txt")],
                   capture_output=True, text=True)
if r.returncode:
    print(r.stdout, r.stderr)
    raise SystemExit("C# harness crashed")
cs_floor = float(r.stdout.split("\t")[1])

# ---- the Python side, using the harness's own efficiency implementation
from lab import efficiency_series
eff = {w: efficiency_series(closes, w) for w in E5}
py_floor = st.fmean(0.124 * math.sqrt(48.0 / n) for n in E5)

hist, py = [], {}
for i in range(209, len(closes)):
    q = st.fmean(eff[w][i] for w in E5)
    hist.append(q)
    if len(hist) > 300:
        hist.pop(0)
    known = len(hist) >= 60
    med = st.median(sorted(hist)) if known else 0.0
    py[i] = (q, known, (med >= py_floor + 0.005) if known else None, med)

bad_q = bad_r = n = 0
worst_q = 0.0
for line in open(os.path.join(HERE, "port_out.txt")):
    k, q, flag, med = line.rstrip("\n").split("\t")
    k, q = int(k), float(q)
    if k not in py:
        continue
    n += 1
    pq, pknown, ptrend, pmed = py[k]
    worst_q = max(worst_q, abs(q - pq))
    if abs(q - pq) > 1e-9:
        bad_q += 1
    want = "?" if not pknown else ("T" if ptrend else "M")
    if flag != want:
        bad_r += 1

print("PORT TEST — shipped C# vs shipped Python harness, %d bars" % n)
print("  random-walk floor   C# %.6f   py %.6f   %s"
      % (cs_floor, py_floor, "MATCH" if abs(cs_floor - py_floor) < 1e-9 else "MISMATCH"))
print("  trend quality       max abs difference %.2e   mismatched bars %d" % (worst_q, bad_q))
print("  regime call         mismatched bars %d" % bad_r)
print("  ->", "PASS" if (bad_q == 0 and bad_r == 0 and abs(cs_floor - py_floor) < 1e-9)
      else "FAIL")

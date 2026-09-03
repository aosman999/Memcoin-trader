// Runs the REAL GoldEdgeNews robot against the simulated broker in
// calgo_sim.cs, bar by bar, and asserts on what it actually does.
//
// This is the test that answers "will it misbehave live?" — a compile cannot.
// Every check here corresponds to something that has actually gone wrong in
// this project, or that would be expensive to discover on a real account.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Robots;

public static class BotSim
{
    class Rung { public int K, Of; public double Ratio, RiskPc; }

    static int _fail;
    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
        if (!ok) _fail++;
    }

    // ---- indicators computed over the visible history, recomputed per bar
    static double[] Ema(List<double> v, int p)
    {
        var o = new double[v.Count]; var k = 2.0 / (p + 1); double e = v.Count > 0 ? v[0] : 0;
        for (var i = 0; i < v.Count; i++) { e = i == 0 ? v[0] : v[i] * k + e * (1 - k); o[i] = e; }
        return o;
    }
    static double[] Rsi(List<double> v, int p)
    {
        var o = new double[v.Count]; double ag = 0, al = 0;
        for (var i = 1; i < v.Count; i++)
        {
            var d = v[i] - v[i - 1];
            var g = d > 0 ? d : 0; var l = d < 0 ? -d : 0;
            if (i <= p) { ag += g / p; al += l / p; }
            else { ag = (ag * (p - 1) + g) / p; al = (al * (p - 1) + l) / p; }
            o[i] = al == 0 ? 100 : 100 - 100 / (1 + ag / al);
        }
        if (v.Count > 0) o[0] = 50;
        return o;
    }
    static double[] Atr(List<double> hi, List<double> lo, List<double> cl, int p)
    {
        var o = new double[cl.Count]; double a = 0;
        for (var i = 1; i < cl.Count; i++)
        {
            var tr = Math.Max(hi[i] - lo[i], Math.Max(Math.Abs(hi[i] - cl[i - 1]), Math.Abs(lo[i] - cl[i - 1])));
            a = i == 1 ? tr : (a * (p - 1) + tr) / p;
            o[i] = a;
        }
        return o;
    }
    static double[] Adx(List<double> hi, List<double> lo, List<double> cl, int p)
    {
        var n = cl.Count; var o = new double[n];
        double sp = 0, sm = 0, str = 0, adx = 0;
        for (var i = 1; i < n; i++)
        {
            var up = hi[i] - hi[i - 1]; var dn = lo[i - 1] - lo[i];
            var pdm = (up > dn && up > 0) ? up : 0;
            var mdm = (dn > up && dn > 0) ? dn : 0;
            var tr = Math.Max(hi[i] - lo[i], Math.Max(Math.Abs(hi[i] - cl[i - 1]), Math.Abs(lo[i] - cl[i - 1])));
            sp = sp - sp / p + pdm; sm = sm - sm / p + mdm; str = str - str / p + tr;
            if (str > 0)
            {
                var dip = 100 * sp / str; var dim = 100 * sm / str;
                var dx = (dip + dim) > 0 ? 100 * Math.Abs(dip - dim) / (dip + dim) : 0;
                adx = i <= p * 2 ? dx : (adx * (p - 1) + dx) / p;
            }
            o[i] = adx;
        }
        return o;
    }

    class World
    {
        public Sim S = new Sim();
        public List<double> C = new List<double>(), H = new List<double>(), L = new List<double>();
        public GoldEdgeNews Bot;
        public Bars Bars;
        public Symbol Sym;
        public Account Acc;
        public Server Srv;
        public int NextId = 1;
        public List<string> Violations = new List<string>();
        public int Opened, Closed;
        public int TrendEntries, FadeEntries, Trails, ReachTargets;
        public double MaxReward = 0.0, MinReward = 1e9;
        public readonly List<double> ReachRatios = new List<double>();
        public readonly List<int> LadderParts = new List<int>();
        public int FarParts, CollapsedLadders, ReachFloored;
        public double MaxSignalRiskPc;
        public double MaxRiskFraction;
    }

    static World Build(List<double> closes, List<double> highs, List<double> lows,
                       TimeFrame tf, bool isLive)
    {
        var w = new World();
        w.C = closes; w.H = highs; w.L = lows;
        var sim = w.S;
        var cs = new DataSeries(sim); var hs = new DataSeries(sim);
        var ls = new DataSeries(sim); var os = new DataSeries(sim); var vs = new DataSeries(sim);
        foreach (var x in closes) { cs.Push(x); os.Push(x); vs.Push(100); }
        foreach (var x in highs) hs.Push(x);
        foreach (var x in lows) ls.Push(x);
        w.Bars = new Bars { ClosePrices = cs, HighPrices = hs, LowPrices = ls, OpenPrices = os, TickVolumes = vs, TimeFrame = tf };
        w.Sym = new Symbol();
        w.Acc = new Account { IsLive = isLive };
        w.Srv = new Server { TimeInUtc = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc) };

        var ema20 = Ema(closes, 20); var ema75 = Ema(closes, 75);
        var rsi14 = Rsi(closes, 14); var atr14 = Atr(highs, lows, closes, 14);
        var adx14 = Adx(highs, lows, closes, 14);
        Func<double[], IndicatorDataSeries> wrap = arr =>
        {
            var d = new IndicatorDataSeries(sim);
            foreach (var x in arr) d.Push(x);
            return d;
        };
        var f = new IndicatorFactory
        {
            MakeEma = (s, p) => new ExponentialMovingAverage { Result = wrap(p <= 20 ? ema20 : ema75) },
            MakeRsi = (s, p) => new RelativeStrengthIndex { Result = wrap(rsi14) },
            MakeMacd = (s, a, b, c) => new MacdHistogram { Histogram = wrap(new double[closes.Count]) },
            MakeDms = p => new DirectionalMovementSystem { ADX = wrap(adx14), DIPlus = wrap(adx14), DIMinus = wrap(adx14) },
            MakeAtr = (p, t) => new AverageTrueRange { Result = wrap(atr14) },
        };

        var bot = new GoldEdgeNews
        {
            Bars = w.Bars, Symbol = w.Sym, Account = w.Acc, Server = w.Srv,
            Indicators = f, SymbolName = "XAUUSD",
        };
        SetDefaults(bot);
        bot.UseCalendar = false;          // no network in a test
        w.Bot = bot;

        bot.OnOrder = (side, sym, units, label, stopPips, tpPips) =>
        {
            var price = side == TradeType.Buy ? w.Sym.Ask : w.Sym.Bid;
            var sl = side == TradeType.Buy ? price - stopPips * w.Sym.PipSize : price + stopPips * w.Sym.PipSize;
            var tp = side == TradeType.Buy ? price + tpPips * w.Sym.PipSize : price - tpPips * w.Sym.PipSize;
            if (units <= 0) w.Violations.Add("order with non-positive volume");
            if (double.IsNaN(sl) || double.IsNaN(tp)) w.Violations.Add("order with NaN stop/target");
            if (side == TradeType.Buy && sl >= price) w.Violations.Add("BUY stop at/above entry");
            if (side == TradeType.Sell && sl <= price) w.Violations.Add("SELL stop at/below entry");
            if (side == TradeType.Buy && tp <= price) w.Violations.Add("BUY target at/below entry");
            if (side == TradeType.Sell && tp >= price) w.Violations.Add("SELL target at/above entry");
            var stopDist = Math.Abs(price - sl);
            var pct = stopDist / price * 100.0;
            if (pct < bot.MinStopPercent - 1e-9 || pct > bot.MaxStopPercent + 1e-9)
                w.Violations.Add(string.Format("stop {0:F3}% outside the configured {1}-{2}% clamp",
                                               pct, bot.MinStopPercent, bot.MaxStopPercent));
            var rewardRatio = Math.Abs(tp - price) / stopDist;
            var floorInForce = bot.UseReachTarget ? bot.ReachMinRR : bot.MinRewardRisk;
            if (bot.TakeProfitCount > 1)
                floorInForce *= bot.LadderNearFraction;   // nearest rung, by design
            if (rewardRatio < floorInForce - 1e-6)
                w.Violations.Add(string.Format(
                    "reward {0:F2}:1 is below the {1:F2} floor", rewardRatio, floorInForce));
            // A target inside the round-trip cost is a guaranteed loss dressed
            // up as a win. This holds whatever the reward setting is.
            var costR = (0.50 + 0.42) / stopDist;
            if (rewardRatio <= costR)
                w.Violations.Add(string.Format(
                    "reward {0:F2}:1 does not even cover costs (needs > {1:F2})",
                    rewardRatio, costR));
            if (rewardRatio > w.MaxReward) w.MaxReward = rewardRatio;
            if (rewardRatio < w.MinReward) w.MinReward = rewardRatio;
            var riskFrac = units * stopDist / w.Acc.Equity;
            if (riskFrac > w.MaxRiskFraction) w.MaxRiskFraction = riskFrac;
            var p = new Position
            {
                Id = w.NextId++, Label = label, SymbolName = sym, TradeType = side,
                EntryPrice = price, VolumeInUnits = units, StopLoss = sl, TakeProfit = tp,
                EntryTime = w.Srv.TimeInUtc,
            };
            w.Positions().Items.Add(p);
            w.Opened++;
            return new TradeResult { IsSuccessful = true, Position = p };
        };
        bot.OnModify = (p, sl, tp) =>
        {
            // A stop must never move AGAINST an open position. This is the
            // check that a trailing implementation most often gets wrong.
            if (sl.HasValue && p.StopLoss.HasValue)
            {
                var dir = p.TradeType == TradeType.Buy ? 1 : -1;
                if (dir > 0 && sl.Value < p.StopLoss.Value - 1e-9)
                    w.Violations.Add("stop moved DOWN on a BUY (against the position)");
                if (dir < 0 && sl.Value > p.StopLoss.Value + 1e-9)
                    w.Violations.Add("stop moved UP on a SELL (against the position)");
                if (sl.Value != p.StopLoss.Value) w.Trails++;
            }
            // A stop must also stay on the correct SIDE of the current price.
            // A BUY stop above market (or SELL stop below) is rejected by a real
            // broker, or fills instantly at whatever price is there.
            if (sl.HasValue)
            {
                var dir2 = p.TradeType == TradeType.Buy ? 1 : -1;
                var mkt = dir2 > 0 ? w.Sym.Bid : w.Sym.Ask;
                if (dir2 > 0 && sl.Value >= mkt)
                    w.Violations.Add("BUY stop moved to/above the market price");
                if (dir2 < 0 && sl.Value <= mkt)
                    w.Violations.Add("SELL stop moved to/below the market price");
            }
            p.StopLoss = sl; p.TakeProfit = tp;
            return new TradeResult { IsSuccessful = true, Position = p };
        };
        bot.OnClose = p => { Settle(w, p, w.C[w.S.Cursor]); return new TradeResult { IsSuccessful = true, Position = p }; };
        return w;
    }

    static Positions Positions(this World w) { return w.Bot.Positions; }

    static void Settle(World w, Position p, double px)
    {
        var dir = p.TradeType == TradeType.Buy ? 1 : -1;
        p.NetProfit = (px - p.EntryPrice) * dir * p.VolumeInUnits;
        w.Acc.Balance += p.NetProfit;
        w.Acc.Equity = w.Acc.Balance;
        w.Bot.History.Items.Add(new HistoricalTrade
        {
            PositionId = p.Id, Label = p.Label, SymbolName = p.SymbolName,
            TradeType = p.TradeType, EntryTime = p.EntryTime,
            EntryPrice = p.EntryPrice, ClosingPrice = px,
            ClosingTime = w.Srv.TimeInUtc, NetProfit = p.NetProfit,
        });
        w.Bot.Positions.Items.Remove(p);
        w.Closed++;
    }

    static void SetDefaults(object bot)
    {
        foreach (var pi in bot.GetType().GetProperties())
        {
            var at = pi.GetCustomAttributes(typeof(ParameterAttribute), true);
            if (at.Length == 0) continue;
            var dv = ((ParameterAttribute)at[0]).DefaultValue;
            if (dv == null) continue;
            pi.SetValue(bot, Convert.ChangeType(dv, pi.PropertyType), null);
        }
    }

    static World Run(List<double> c, List<double> h, List<double> l, TimeFrame tf,
                     bool isLive = false, int startBar = 250, double risk = -1,
                     bool reach = true, int parts = -1)
    {
        var w = Build(c, h, l, tf, isLive);
        if (risk > 0) w.Bot.RiskPercent = risk;
        if (parts > 0) w.Bot.TakeProfitCount = parts;
        // The reach target now overrides the structural one, so with it ON the
        // structural path is unreachable and any fault injected there is
        // invisible. Keep a run with it OFF so that path stays covered.
        w.Bot.UseReachTarget = reach;
        w.S.Cursor = startBar;
        w.Sym.Ask = c[startBar] + 0.25; w.Sym.Bid = c[startBar] - 0.25;
        w.Bot.DriveStart();
        for (var i = startBar; i < c.Count; i++)
        {
            w.S.Cursor = i;
            w.Sym.Ask = c[i] + 0.25; w.Sym.Bid = c[i] - 0.25;
            w.Srv.TimeInUtc = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc)
                                .AddMinutes(i * tf.Minutes);
            // broker side: fill resting stops/targets against this bar's range
            foreach (var p in w.Bot.Positions.Items.ToList())
            {
                var dir = p.TradeType == TradeType.Buy ? 1 : -1;
                if (dir > 0 && l[i] <= p.StopLoss.Value) Settle(w, p, p.StopLoss.Value);
                else if (dir > 0 && h[i] >= p.TakeProfit.Value) Settle(w, p, p.TakeProfit.Value);
                else if (dir < 0 && h[i] >= p.StopLoss.Value) Settle(w, p, p.StopLoss.Value);
                else if (dir < 0 && l[i] <= p.TakeProfit.Value) Settle(w, p, p.TakeProfit.Value);
            }
            var open = w.Bot.Positions.Items.Sum(p =>
                (w.C[i] - p.EntryPrice) * (p.TradeType == TradeType.Buy ? 1 : -1) * p.VolumeInUnits);
            w.Acc.Equity = w.Acc.Balance + open;
            // Each signal becomes TakeProfitCount positions, so the ceiling is
            // the signal cap times the parts per signal.
            var posCap = w.Bot.MaxConcurrentPositions * Math.Max(1, w.Bot.TakeProfitCount);
            if (w.Bot.Positions.Items.Count > posCap)
                w.Violations.Add(string.Format(
                    "more open positions ({0}) than the {1} cap allows", w.Bot.Positions.Items.Count, posCap));
            var before = w.Bot.Log.Count;
            var signal = new List<Rung>();
            w.Bot.DriveBar();
            w.Bot.DriveTick();
            for (var k = before; k < w.Bot.Log.Count; k++)
            {
                if (w.Bot.Log[k].StartsWith("OPEN ")) w.TrendEntries++;
                else if (w.Bot.Log[k].StartsWith("FADE ")) w.FadeEntries++;
                        var line = w.Bot.Log[k];
                if (line.StartsWith("OPEN ") || line.StartsWith("FADE "))
                {
                    var mt = System.Text.RegularExpressions.Regex.Match(line,
                        @"TP (\d+)/(\d+) at [0-9.]+ \(\+[0-9.]+\) = ([0-9.]+):1 from ([a-z-]+)");
                    var mr = System.Text.RegularExpressions.Regex.Match(line,
                        @"RISKS [0-9.]+ = ([0-9.]+)% of equity");
                    if (mt.Success)
                    {
                        var rung = int.Parse(mt.Groups[1].Value);
                        var of = int.Parse(mt.Groups[2].Value);
                        var ratio = double.Parse(mt.Groups[3].Value, CultureInfo.InvariantCulture);
                        var src = mt.Groups[4].Value;
                        var riskPc = mr.Success
                            ? double.Parse(mr.Groups[1].Value, CultureInfo.InvariantCulture) : 0.0;
                        w.LadderParts.Add(of);
                        if (rung == of) w.FarParts++;
                        signal.Add(new Rung { K = rung, Of = of, Ratio = ratio, RiskPc = riskPc });
                        if (src.StartsWith("reach"))
                        {
                            w.ReachTargets++;
                            if (src == "reach-floored") w.ReachFloored++;
                            // Undo the ladder spacing so the adaptivity check sees
                            // the BASE reach ratio, not the rung fraction.
                            if (rung == of)
                                w.ReachRatios.Add(of > 1 ? ratio / w.Bot.LadderFarMultiple : ratio);
                        }
                    }
                }
            }
            if (signal.Count > 0)
            {
                // total risk this ONE signal committed
                var totalPc = signal.Sum(x => x.RiskPc);
                if (totalPc > w.MaxSignalRiskPc) w.MaxSignalRiskPc = totalPc;
                // a multi-rung signal must use genuinely different distances
                if (signal.Count > 1 &&
                    Math.Abs(signal.Max(x => x.Ratio) - signal.Min(x => x.Ratio)) < 1e-6)
                    w.CollapsedLadders++;
            }
        }
        w.Bot.DriveStop();
        return w;
    }

    static void Series(int n, int seed, out List<double> c, out List<double> h, out List<double> l,
                       double drift = 0.0, double vol = 1.6)
    {
        var rng = new Random(seed);
        c = new List<double>(); h = new List<double>(); l = new List<double>();
        var p = 4600.0;
        for (var i = 0; i < n; i++)
        {
            var g = Math.Sqrt(-2 * Math.Log(rng.NextDouble() + 1e-12)) * Math.Cos(2 * Math.PI * rng.NextDouble());
            p += g * vol + drift;
            var wick = Math.Abs(g) * vol * 0.6;
            c.Add(p); h.Add(p + wick); l.Add(p - wick);
        }
    }

    public static int Main(string[] argv)
    {
        Console.WriteLine("END-TO-END BOT SIMULATION — the real robot against a simulated broker\n");
        List<double> c, h, l;

        // 1. it runs a long session without throwing, and it trades
        Series(4000, 11, out c, out h, out l);
        var w = Run(c, h, l, TimeFrame.Minute5);
        Check(!w.Bot.Stopped, "runs to completion on a demo account without stopping");
        Check(w.Opened > 0, string.Format("opens trades ({0} opened, {1} closed)", w.Opened, w.Closed));
        Check(w.Violations.Count == 0, "no order/risk violations: " +
              (w.Violations.Count == 0 ? "none" : string.Join("; ", w.Violations.Distinct().Take(4))));
        Check(!w.Bot.Log.Any(x => x.StartsWith("ERROR")), "no ERROR lines in the log: " +
              string.Join(" | ", w.Bot.Log.Where(x => x.StartsWith("ERROR")).Take(2)));
        Check(w.MaxRiskFraction <= w.Bot.RiskPercent / 100.0 * 3.0 + 1e-9,
              string.Format("largest single-trade risk {0:F2}% stays near the configured {1}%",
                            w.MaxRiskFraction * 100, w.Bot.RiskPercent));

        // 2. the demo lock actually refuses a live account
        var wl = Run(c, h, l, TimeFrame.Minute5, isLive: true);
        Check(wl.Bot.Stopped && wl.Opened == 0, "REFUSES a live account and places no order");

        // 3. every timeframe it might be dropped on
        var tfs = new[] { TimeFrame.Minute, TimeFrame.Minute3, TimeFrame.Minute5,
                          TimeFrame.Minute15, TimeFrame.Minute30, TimeFrame.Hour };
        var allOk = true; var detail = "";
        foreach (var tf in tfs)
        {
            var wt = Run(c, h, l, tf);
            var bad = wt.Violations.Count > 0 || wt.Bot.Log.Any(x => x.StartsWith("ERROR"));
            if (bad) { allOk = false; detail += tf + " "; }
        }
        Check(allOk, "clean on m1/m3/m5/m15/m30/h1 " + (allOk ? "" : "— failed on: " + detail));

        // 4. trending and crashing tapes, where sizing and the day guard bite
        Series(3000, 22, out c, out h, out l, drift: 0.35);
        var wu = Run(c, h, l, TimeFrame.Minute5);
        Check(wu.Violations.Count == 0, "no order/risk violations on a TRENDING tape: " +
              (wu.Violations.Count == 0 ? "none" : string.Join("; ", wu.Violations.Distinct().Take(4))));
        Check(wu.Opened > 0, string.Format("opens trades on a TRENDING tape ({0})", wu.Opened));
        // COVERAGE: on a random walk the regime switch correctly picks the fade
        // side, so a suite of random walks never executes the TREND entry path
        // at all. That gap hid two injected faults from the negative controls.
        Check(wu.TrendEntries > 0,
              string.Format("exercises the TREND entry path ({0} trend entries)", wu.TrendEntries));
        Check(w.FadeEntries + wu.FadeEntries > 0,
              string.Format("exercises the FADE entry path ({0} fade entries)",
                            w.FadeEntries + wu.FadeEntries));
        Series(3000, 33, out c, out h, out l, drift: -0.35, vol: 3.2);
        var wd = Run(c, h, l, TimeFrame.Minute5);
        Check(wd.Violations.Count == 0, "no order/risk violations on a violent DOWNTREND: " +
              (wd.Violations.Count == 0 ? "none" : string.Join("; ", wd.Violations.Distinct().Take(4))));
        Check(wd.Bot.Log.Any(x => x.Contains("REGIME") || x.Contains("Regime primed")),
              "reports which regime it is in");

        // 5. a flat tape must not deadlock silently — it must still explain itself
        Series(2000, 44, out c, out h, out l, vol: 0.05);
        var wf = Run(c, h, l, TimeFrame.Minute5);
        Check(wf.Violations.Count == 0, "no order/risk violations on a dead-flat tape: " +
              (wf.Violations.Count == 0 ? "none" : string.Join("; ", wf.Violations.Distinct().Take(4))));
        Check(wf.Bot.Log.Any(x => x.Contains("status:")), "still prints status on a flat tape");

        // 5b. the trailing stop must actually trail, and never backwards
        Check(wu.Trails > 0 || w.Trails > 0,
              string.Format("trailing stop actually moves stops ({0} modifications)",
                            wu.Trails + w.Trails));
        Check(wu.Bot.Log.Any(x => x.StartsWith("TRAIL ")) || w.Bot.Log.Any(x => x.StartsWith("TRAIL ")),
              "logs each trail with the R locked in");

        // 5d. COVERAGE for the structural target, which the reach rule now
        // overrides by default. Without this run its code is never executed and
        // its negative control cannot fail.
        var wstruct = Run(c, h, l, TimeFrame.Minute5, reach: false);
        Check(wstruct.Violations.Count == 0,
              "no order/risk violations with the reach target OFF: " +
              (wstruct.Violations.Count == 0 ? "none"
               : string.Join("; ", wstruct.Violations.Distinct().Take(4))));
        Check(wstruct.Bot.Log.Any(x => x.Contains("from structure") || x.Contains("from floor")),
              "still uses the structural target when the reach target is switched off");

        // the structural target must never be nearer than the reward floor
        Check(w.Violations.Count == 0 || !w.Violations.Any(v => v.Contains("reward")),
              "target never lands inside the reward floor");
        Check(w.Bot.Log.Any(x => x.Contains(":1 from ")) || wu.Bot.Log.Any(x => x.Contains(":1 from ")),
              "logs where each target came from (reach / structure / floor / capped)");

        // 5e. THE REACH TARGET. It must actually engage -- the first version of
        // this feature was floored at MinRewardRisk, which clamped every
        // calibrated target straight back to the old value and made the whole
        // thing an expensive no-op that still logged as if it were working.
        // These checks are what would have caught that.
        Check(w.ReachTargets + wu.ReachTargets + wd.ReachTargets > 0,
              string.Format("the reach-calibrated target ACTUALLY fires ({0} orders)",
                            w.ReachTargets + wu.ReachTargets + wd.ReachTargets));
        Check(w.Bot.Log.Any(x => x.Contains("percentile of what the last")),
              "explains the target rule at start-up");
        // and it must not simply pin to one value -- that is the no-op signature
        var reachAll = w.ReachRatios.Concat(wu.ReachRatios).Concat(wd.ReachRatios).ToList();
        var spread2 = reachAll.Count > 1 ? reachAll.Max() - reachAll.Min() : 0.0;
        Check(spread2 > 0.05,
              string.Format("target adapts rather than pinning to one ratio ({0} reach targets, range {1:F2}R)",
                            reachAll.Count, spread2));
        var reachFloor = w.Bot.ReachMinRR *
                         (w.Bot.TakeProfitCount > 1 ? w.Bot.LadderNearFraction : 1.0);
        Check(reachAll.Count == 0 || reachAll.Min() >= reachFloor - 1e-6,
              string.Format("no reach target lands under its floor (nearest {0:F2} >= {1:F2})",
                            reachAll.Count == 0 ? 0 : reachAll.Min(), reachFloor));
        Check(!w.Violations.Any(v => v.Contains("cover costs")) &&
              !wu.Violations.Any(v => v.Contains("cover costs")) &&
              !wd.Violations.Any(v => v.Contains("cover costs")) &&
              !wf.Violations.Any(v => v.Contains("cover costs")),
              "every target clears the round-trip cost");

        // The warm-up target must NOT be the 2.5x structural one: before the
        // reach rule has enough history it used to fall back to exactly the
        // target it exists to replace, so the first trades of every run got an
        // unrealistic TP. These orders must be logged as "warm-up".
        Check(w.Bot.Log.Any(x => x.Contains("from warm-up")) ||
              wu.Bot.Log.Any(x => x.Contains("from warm-up")),
              "uses the warm-up target before it has learned, not the 2.5x structural one");
        Check(!w.Bot.Log.Any(x => x.Contains("from structure")) &&
              !wu.Bot.Log.Any(x => x.Contains("from structure")),
              "never falls back to the structural target while the reach rule is on");

        // 5f. THE TAKE-PROFIT LADDER. Each signal must actually become three
        // positions with three different targets, the total risk must not grow
        // because of the split, and the 6-position cap must still allow six
        // SIGNALS rather than two (counting parts against the cap starves the
        // ladder — that exact mistake made the backtest look far worse).
        var ladderAll = w.LadderParts.Concat(wu.LadderParts).ToList();
        Check(ladderAll.Count > 0 &&
              ladderAll.Max() == w.Bot.TakeProfitCount &&
              ladderAll.All(n => n >= 1 && n <= w.Bot.TakeProfitCount),
              string.Format("signals open up to {0} targets and never more ({1} orders, sizes seen: {2})",
                            w.Bot.TakeProfitCount, ladderAll.Count,
                            string.Join("/", ladderAll.Distinct().OrderBy(x => x).Select(x => x.ToString()))));
        // a shortfall must be explained, never silent
        var shortfall = ladderAll.Any(n => n < w.Bot.TakeProfitCount);
        Check(!shortfall ||
              w.Bot.Log.Any(x => x.Contains("take profits fit")) ||
              wu.Bot.Log.Any(x => x.Contains("take profits fit")),
              "says so when the account cannot afford every take profit");
        Check(w.FarParts + wu.FarParts > 0,
              string.Format("the furthest target is placed ({0} of them)", w.FarParts + wu.FarParts));
        Check(w.MaxSignalRiskPc <= w.Bot.RiskPercent * 1.35 + 1e-9 &&
              wu.MaxSignalRiskPc <= wu.Bot.RiskPercent * 1.35 + 1e-9,
              string.Format("splitting into {0} targets does not inflate risk " +
                            "(worst WHOLE signal risked {1:F2}%, configured {2:F2}%)",
                            w.Bot.TakeProfitCount,
                            Math.Max(w.MaxSignalRiskPc, wu.MaxSignalRiskPc), w.Bot.RiskPercent));
        // Letting the NEAR rungs into the reach history teaches the bot that
        // trades only get as far as the near target — the percentile then
        // collapses onto its floor and stays there. A high floored share is the
        // signature of that pollution.
        var reachN = w.ReachTargets + wu.ReachTargets + wd.ReachTargets;
        var flooredN = w.ReachFloored + wu.ReachFloored + wd.ReachFloored;
        var flooredPc = reachN > 0 ? 100.0 * flooredN / reachN : 0.0;
        Check(reachN > 0 && flooredPc < 40.0,
              string.Format("the reach target is not pinned to its floor ({0:F1}% floored, {1} reach orders)",
                            flooredPc, reachN));
        Check(w.CollapsedLadders == 0 && wu.CollapsedLadders == 0,
              string.Format("the targets in a signal are genuinely different distances " +
                            "({0} collapsed to a single distance)",
                            w.CollapsedLadders + wu.CollapsedLadders));
        var wsingle = Run(c, h, l, TimeFrame.Minute5, parts: 1);
        Check(wsingle.Violations.Count == 0 && wsingle.Opened > 0,
              string.Format("still clean with a single take profit ({0} orders)", wsingle.Opened));
        Check(wsingle.LadderParts.All(n => n == 1),
              "one take profit means one position per signal");

        // 5c. positions open at start-up must be reported as unmanaged
        Series(1200, 77, out c, out h, out l);
        var wp = Build(c, h, l, TimeFrame.Minute5, false);
        wp.S.Cursor = 250;
        wp.Sym.Ask = c[250] + 0.25; wp.Sym.Bid = c[250] - 0.25;
        wp.Bot.Positions.Items.Add(new Position
        {
            Id = 999, Label = "GoldEdgeNews", SymbolName = "XAUUSD",
            TradeType = TradeType.Buy, EntryPrice = c[250] - 20, VolumeInUnits = 1,
            StopLoss = c[250] - 40, TakeProfit = c[250] + 50,
            EntryTime = wp.Srv.TimeInUtc.AddMinutes(-900),
        });
        wp.Bot.DriveStart();
        Check(wp.Bot.Log.Any(x => x.Contains("ALREADY OPEN")),
              "reports positions that were open while the bot was down");
        Check(wp.Bot.Log.Any(x => x.Contains("PAST the max hold")),
              "flags a stale position that outlived the max hold");

        // 6. the setup check must actually fire on the two settings that keep
        //    being wrong live, and must stay quiet when they are right.
        Series(1200, 66, out c, out h, out l);
        var wbad = Run(c, h, l, TimeFrame.Minute15, risk: 10.0);
        Check(wbad.Bot.Log.Any(x => x.Contains("SETUP PROBLEM")),
              "warns loudly when the chart is not m5 and risk is 10%");
        Check(wbad.Bot.Log.Any(x => x.Contains("CHART IS")),
              "names the wrong timeframe specifically");
        Check(wbad.Bot.Log.Any(x => x.Contains("RISK IS")),
              "names the oversized risk specifically");
        var wgood = Run(c, h, l, TimeFrame.Minute5, risk: 1.0);
        Check(wgood.Bot.Log.Any(x => x.Contains("SETUP OK")) &&
              !wgood.Bot.Log.Any(x => x.Contains("SETUP PROBLEM")),
              "stays quiet when the setup is correct (no crying wolf)");

        // 7. short history must not crash it (a fresh chart with little data)
        Series(300, 55, out c, out h, out l);
        var ws = Run(c, h, l, TimeFrame.Minute5, startBar: 130);
        Check(ws.Violations.Count == 0 && !ws.Bot.Log.Any(x => x.StartsWith("ERROR")),
              "no order/risk violations on a chart with barely any history");

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? "ALL BEHAVIOUR CHECKS PASSED" : _fail + " BEHAVIOUR CHECK(S) FAILED");
        return _fail == 0 ? 0 : 1;
    }
}

// Proves the ONE property the Telegram channel depends on:
//
//     what GoldICT writes to the signal feed is what GoldICT actually did.
//
// The channel is fed by that file. If the file and the orders can disagree,
// then the channel and cTrader can disagree — which is the exact failure the
// bridge design exists to prevent. A compile cannot show this and neither can
// reading the code; only running the real robot against a simulated broker and
// comparing the two records can.
//
// So: run GoldICT over a tape, record every order the broker received, read
// back the JSONL it wrote, and require them to match line for line.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Robots;

public static class BridgeSim
{
    static int _fail;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
        if (!ok) _fail++;
    }

    // ---- a deliberately small JSON reader: the feed is flat objects, one per
    // line, with string, number, bool and one numeric-array field. Anything it
    // cannot parse is a failure of the FEED, which is the point.
    class Row
    {
        public readonly Dictionary<string, string> S = new Dictionary<string, string>();
        public readonly Dictionary<string, double> N = new Dictionary<string, double>();
        public readonly Dictionary<string, List<double>> A = new Dictionary<string, List<double>>();
        public string T { get { return S.ContainsKey("t") ? S["t"] : ""; } }
        public double Num(string k) { return N.ContainsKey(k) ? N[k] : double.NaN; }
        public string Str(string k) { return S.ContainsKey(k) ? S[k] : null; }
    }

    static Row Parse(string line)
    {
        var r = new Row();
        var i = line.IndexOf('{');
        if (i < 0) return null;
        i++;
        while (i < line.Length)
        {
            while (i < line.Length && (line[i] == ',' || line[i] == ' ')) i++;
            if (i >= line.Length || line[i] == '}') break;
            if (line[i] != '"') return null;
            var end = line.IndexOf('"', i + 1);
            if (end < 0) return null;
            var key = line.Substring(i + 1, end - i - 1);
            i = end + 1;
            if (i >= line.Length || line[i] != ':') return null;
            i++;
            if (line[i] == '"')
            {
                var sb = new System.Text.StringBuilder();
                i++;
                while (i < line.Length && line[i] != '"')
                {
                    if (line[i] == '\\' && i + 1 < line.Length)
                    {
                        i++;
                        var c = line[i];
                        sb.Append(c == 'n' ? '\n' : c == 't' ? '\t' : c == 'r' ? '\r' : c);
                    }
                    else sb.Append(line[i]);
                    i++;
                }
                i++;
                r.S[key] = sb.ToString();
            }
            else if (line[i] == '[')
            {
                var close = line.IndexOf(']', i);
                if (close < 0) return null;
                var body = line.Substring(i + 1, close - i - 1).Trim();
                var vals = new List<double>();
                if (body.Length > 0)
                    foreach (var p in body.Split(','))
                        vals.Add(double.Parse(p.Trim(), CultureInfo.InvariantCulture));
                r.A[key] = vals;
                i = close + 1;
            }
            else
            {
                var j = i;
                while (j < line.Length && line[j] != ',' && line[j] != '}') j++;
                var tok = line.Substring(i, j - i).Trim();
                if (tok == "true" || tok == "false") r.S[key] = tok;
                else
                {
                    double v;
                    if (!double.TryParse(tok, NumberStyles.Any, CultureInfo.InvariantCulture, out v))
                        return null;
                    r.N[key] = v;
                }
                i = j;
            }
        }
        return r;
    }

    // ---- what the broker actually saw
    class Order
    {
        public int Id;
        public TradeType Side;
        public double Entry, Stop, Target, Units;
        public bool Closed;
        public double ClosePrice, Profit;
    }

    class World
    {
        public Sim S = new Sim();
        public List<double> C = new List<double>(), H = new List<double>(), L = new List<double>();
        public GoldICT Bot;
        public Bars Bars;
        public Symbol Sym;
        public Account Acc;
        public Server Srv;
        public int NextId = 1;
        public readonly List<Order> Orders = new List<Order>();
        public string Feed;
    }

    static double[] Atr(List<double> hi, List<double> lo, List<double> cl, int p)
    {
        var o = new double[cl.Count];
        double a = 0;
        for (var i = 1; i < cl.Count; i++)
        {
            var tr = Math.Max(hi[i] - lo[i], Math.Max(Math.Abs(hi[i] - cl[i - 1]),
                                                      Math.Abs(lo[i] - cl[i - 1])));
            a = i == 1 ? tr : (a * (p - 1) + tr) / p;
            o[i] = a;
        }
        return o;
    }

    static World Build(List<double> c, List<double> h, List<double> l, string feed, bool emit)
    {
        var w = new World { C = c, H = h, L = l, Feed = feed };
        var sim = w.S;
        Func<List<double>, DataSeries> mk = src =>
        {
            var d = new DataSeries(sim);
            foreach (var x in src) d.Push(x);
            return d;
        };
        var vs = new DataSeries(sim);
        foreach (var x in c) vs.Push(100);
        w.Bars = new Bars
        {
            ClosePrices = mk(c), HighPrices = mk(h), LowPrices = mk(l),
            OpenPrices = mk(c), TickVolumes = vs, TimeFrame = TimeFrame.Minute5,
        };
        w.Sym = new Symbol();
        // Big enough that three rungs actually FIT. On a $3,000 account the
        // broker minimum swallows the split and the bot correctly falls back to a
        // single target -- which is real behaviour, but it would leave the whole
        // TP1/TP2/TP3 path in this test untested.
        w.Acc = new Account { IsLive = false, Balance = 50000.0, Equity = 50000.0 };
        w.Srv = new Server { TimeInUtc = new DateTime(2026, 8, 3, 8, 0, 0, DateTimeKind.Utc) };

        var atr = Atr(h, l, c, 14);
        Func<double[], IndicatorDataSeries> wrap = arr =>
        {
            var d = new IndicatorDataSeries(sim);
            foreach (var x in arr) d.Push(x);
            return d;
        };
        var f = new IndicatorFactory
        {
            MakeEma = (s, p) => new ExponentialMovingAverage { Result = wrap(new double[c.Count]) },
            MakeRsi = (s, p) => new RelativeStrengthIndex { Result = wrap(new double[c.Count]) },
            MakeMacd = (s, a, b, d) => new MacdHistogram { Histogram = wrap(new double[c.Count]) },
            MakeDms = p => new DirectionalMovementSystem { ADX = wrap(new double[c.Count]), DIPlus = wrap(new double[c.Count]), DIMinus = wrap(new double[c.Count]) },
            MakeAtr = (p, t) => new AverageTrueRange { Result = wrap(atr) },
        };

        var bot = new GoldICT
        {
            Bars = w.Bars, Symbol = w.Sym, Account = w.Acc, Server = w.Srv,
            Indicators = f, SymbolName = "XAUUSD",
        };
        Defaults(bot);
        bot.EmitSignalFeed = emit;
        bot.SignalFeedPath = feed;
        w.Bot = bot;

        bot.OnOrder = (side, sym, units, label, stopPips, tpPips) =>
        {
            var price = side == TradeType.Buy ? w.Sym.Ask : w.Sym.Bid;
            var sl = side == TradeType.Buy ? price - stopPips * w.Sym.PipSize
                                           : price + stopPips * w.Sym.PipSize;
            var tp = side == TradeType.Buy ? price + tpPips * w.Sym.PipSize
                                           : price - tpPips * w.Sym.PipSize;
            var pos = new Position
            {
                Id = w.NextId++, Label = label, SymbolName = sym, TradeType = side,
                EntryPrice = price, VolumeInUnits = units, StopLoss = sl, TakeProfit = tp,
                EntryTime = w.Srv.TimeInUtc,
            };
            w.Bot.Positions.Items.Add(pos);
            w.Orders.Add(new Order
            {
                Id = pos.Id, Side = side, Entry = price, Stop = sl, Target = tp, Units = units,
            });
            return new TradeResult { IsSuccessful = true, Position = pos };
        };
        bot.OnModify = (p, sl, tp) => { p.StopLoss = sl; p.TakeProfit = tp; return new TradeResult { IsSuccessful = true, Position = p }; };
        bot.OnClose = p => { Settle(w, p, w.C[w.S.Cursor]); return new TradeResult { IsSuccessful = true, Position = p }; };
        return w;
    }

    static void Defaults(GoldICT b)
    {
        b.UseMss = true; b.UseBreaker = true; b.UseVoid = true; b.UseOrderblock = false;
        b.SwingFractal = 2; b.StructureLookback = 60; b.BreakerSpan = 2;
        b.OrderblockLookback = 24; b.OrderblockValidWithin = 12;
        b.VoidDisplacementAtr = 1.5; b.SetupExpiryBars = 36;
        b.RiskPercent = 1.0; b.MaxConcurrentSignals = 2; b.GapMinutes = 15;
        b.MinStopPercent = 0.05; b.MaxStopPercent = 1.4; b.StopBufferPercent = 5.0;
        b.MaxHoldMinutes = 600; b.DayGuardPercent = 15.0;
        b.SessionFromHour = 0; b.SessionToHour = 24;
        b.TakeProfitCount = 3; b.LadderNearFraction = 0.5; b.LadderFarMultiple = 1.5;
        b.UseTrailingStop = true; b.TrailActivateR = 0.7; b.TrailDistanceR = 0.7;
        b.UseReachTarget = true; b.ReachPercentile = 60.0; b.ReachMinTrades = 30;
        b.ReachMinRR = 1.0; b.ReachWarmupRR = 1.5; b.TargetMaxRR = 8.0;
        b.UseNews = false;                       // no network in a test
        b.ProtectOnNews = false; b.BlockEntriesOnNews = false; b.UseVacuumWindow = false;
        b.TelegramBotToken = ""; b.TelegramChatId = ""; b.TelegramTrades = false;
        b.UseTopDown = false; b.ZoneTimeFrame = "Hour4"; b.ZoneLookback = 24;
        b.ZoneValidWithin = 12; b.ZoneMaxAge = 40; b.RequireShift = true;
        b.RequireFvg = false; b.KillzonesOnly = false;
        b.MaxZones = 8; b.ZoneTouchBars = 16;
        b.Label = "GoldICT"; b.Verbose = false;
        b.FeedHeartbeatMinutes = 0;              // heartbeats are not the subject
        b.PollMinutes = 3; b.AlertThreshold = 3.0;
        b.CalendarUrl = ""; b.ExtraFeedUrls = "";
        b.ProtectBeforeMinutes = 15; b.BlockBeforeMinutes = 15; b.BlockAfterMinutes = 10;
        b.VacuumWindowMinutes = 90; b.VacuumDisplacementAtr = 1.1;
        b.FeedGold = false; b.FeedMideast = false; b.FeedTrump = false; b.FeedFed = false;
        b.WireAlJazeera = false; b.WireAlArabiya = false; b.WireCnn = false;
        b.WireCnnMoney = false; b.WireBbc = false; b.WireCnbc = false; b.WireTrumpFast = false;
    }

    static void Settle(World w, Position p, double px)
    {
        var dir = p.TradeType == TradeType.Buy ? 1 : -1;
        p.NetProfit = (px - p.EntryPrice) * dir * p.VolumeInUnits;
        w.Acc.Balance += p.NetProfit;
        w.Acc.Equity = w.Acc.Balance;
        w.Bot.History.Items.Add(new HistoricalTrade
        {
            PositionId = p.Id, Label = p.Label, SymbolName = p.SymbolName,
            TradeType = p.TradeType, EntryTime = p.EntryTime, EntryPrice = p.EntryPrice,
            ClosingPrice = px, ClosingTime = w.Srv.TimeInUtc, NetProfit = p.NetProfit,
        });
        var o = w.Orders.FirstOrDefault(x => x.Id == p.Id);
        if (o != null) { o.Closed = true; o.ClosePrice = px; o.Profit = p.NetProfit; }
        w.Bot.Positions.Items.Remove(p);
    }

    static void Run(World w)
    {
        w.Bot.DriveStart();
        for (var i = 1; i < w.C.Count; i++)
        {
            w.S.Cursor = i;
            var px = w.C[i];
            w.Sym.Bid = px;
            w.Sym.Ask = px + 0.30;
            w.Srv.TimeInUtc = w.Srv.TimeInUtc.AddMinutes(5);

            // fill stops and targets against the bar the broker just printed
            foreach (var p in w.Bot.Positions.Items.ToList())
            {
                var dir = p.TradeType == TradeType.Buy ? 1 : -1;
                var hit = (dir > 0 && p.StopLoss.HasValue && w.L[i] <= p.StopLoss.Value)
                          || (dir < 0 && p.StopLoss.HasValue && w.H[i] >= p.StopLoss.Value);
                var won = (dir > 0 && p.TakeProfit.HasValue && w.H[i] >= p.TakeProfit.Value)
                          || (dir < 0 && p.TakeProfit.HasValue && w.L[i] <= p.TakeProfit.Value);
                if (won) Settle(w, p, p.TakeProfit.Value);
                else if (hit) Settle(w, p, p.StopLoss.Value);
            }
            w.Bot.DriveTick();
            w.Bot.DriveBar();
        }
        w.Bot.DriveStop();
    }

    // A tape with real structure in it: swings, a raid on an old low, a
    // displacement bar. Deterministic, so a failure here is reproducible.
    static void Tape(out List<double> c, out List<double> h, out List<double> l)
    {
        c = new List<double>();
        h = new List<double>();
        l = new List<double>();
        var rnd = new Random(20260903);
        var p = 4300.0;
        for (var i = 0; i < 2600; i++)
        {
            // slow swings, so fractal highs and lows actually form
            // Amplitude matters here. The furthest rung sits at ~2.25x the stop,
            // and the stop floor is 0.4% of price = $17 on gold — so a tape that
            // only travels $40 end to end can never fill TP3, and the rung
            // number would go untested for reasons that have nothing to do with
            // the bridge. This one swings ~$90 and drifts ~$150.
            var wave = Math.Sin(i / 37.0) * 45.0 + Math.Sin(i / 11.0) * 12.0;
            var drift = (i / 2600.0) * 150.0;
            var shock = (i % 311 == 0) ? (rnd.NextDouble() - 0.5) * 60.0 : 0.0;
            p = 4300.0 + wave + drift + shock + (rnd.NextDouble() - 0.5) * 4.0;
            var rng = 1.5 + rnd.NextDouble() * 3.0 + Math.Abs(shock) * 0.5;
            c.Add(p);
            h.Add(p + rng);
            l.Add(p - rng);
        }
    }

    static List<Row> ReadFeed(string path)
    {
        var rows = new List<Row>();
        if (!File.Exists(path)) return rows;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.Trim().Length == 0) continue;
            var r = Parse(line);
            if (r == null)
            {
                Console.WriteLine("  FAIL  feed line is not parseable JSON: " + line);
                _fail++;
                continue;
            }
            rows.Add(r);
        }
        return rows;
    }

    public static int Main()
    {
        var dir = Path.Combine(Path.GetTempPath(), "goldict_bridge_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var feed = Path.Combine(dir, "signals.jsonl");
        try
        {
            List<double> c, h, l;
            Tape(out c, out h, out l);
            var w = Build(c, h, l, feed, true);
            Run(w);
            var rows = ReadFeed(feed);

            Console.WriteLine();
            Console.WriteLine("orders placed: {0}   feed rows: {1}", w.Orders.Count, rows.Count);
            foreach (var g in rows.GroupBy(r => r.T))
                Console.WriteLine("   {0}: {1}", g.Key, g.Count());
            Console.WriteLine();

            Check(w.Orders.Count > 20,
                  string.Format("the bot actually traded on this tape ({0} orders) — a bridge " +
                                "test over zero trades proves nothing", w.Orders.Count));
            Check(rows.Count > 0, "the feed file was written at all");

            var entries = rows.Where(r => r.T == "entry").ToList();
            var exits = rows.Where(r => r.T == "tp" || r.T == "sl" || r.T == "close").ToList();

            // ---- 1. one entry event per signal, and the parts add up
            var partsPerSignal = w.Bot.TakeProfitCount;
            var expectedSignals = 0;
            var seenIds = new HashSet<int>();
            foreach (var r in entries)
            {
                var ids = r.A.ContainsKey("ids") ? r.A["ids"] : new List<double>();
                expectedSignals += ids.Count;
                foreach (var id in ids) seenIds.Add((int)id);
            }
            Check(expectedSignals == w.Orders.Count,
                  string.Format("every order the broker received appears in exactly one entry " +
                                "event ({0} in the feed vs {1} placed)",
                                expectedSignals, w.Orders.Count));
            Check(seenIds.Count == w.Orders.Count,
                  "no position id is claimed twice, and none is missing");
            Check(w.Orders.All(o => seenIds.Contains(o.Id)),
                  "the feed invents no trade the broker never saw");

            // ---- 2. the numbers in the feed ARE the numbers on the orders
            var mismatch = 0;
            var stopMismatch = 0;
            var sideMismatch = 0;
            foreach (var r in entries)
            {
                var ids = r.A.ContainsKey("ids") ? r.A["ids"] : new List<double>();
                var tps = r.A.ContainsKey("tps") ? r.A["tps"] : new List<double>();
                if (ids.Count != tps.Count) { mismatch++; continue; }
                var side = r.Str("side");
                for (var k = 0; k < ids.Count; k++)
                {
                    var o = w.Orders.FirstOrDefault(x => x.Id == (int)ids[k]);
                    if (o == null) { mismatch++; continue; }
                    if (Math.Abs(o.Target - tps[k]) > 0.011) mismatch++;
                    if (Math.Abs(o.Stop - r.Num("stop")) > 0.011) stopMismatch++;
                    var want = o.Side == TradeType.Buy ? "BUY" : "SELL";
                    if (side != want) sideMismatch++;
                }
            }
            Check(mismatch == 0,
                  string.Format("every take profit posted to the channel is the take profit on " +
                                "the broker's order ({0} mismatches)", mismatch));
            Check(stopMismatch == 0,
                  string.Format("every stop posted to the channel is the stop on the order " +
                                "({0} mismatches)", stopMismatch));
            Check(sideMismatch == 0,
                  string.Format("BUY is never posted as SELL, or the reverse ({0} mismatches)",
                                sideMismatch));

            // ---- 3. the ladder in the feed is a real ladder
            var flat = entries.Count(r => r.A.ContainsKey("tps") && r.A["tps"].Count > 1 &&
                                          r.A["tps"].Distinct().Count() == 1);
            Check(flat == 0,
                  "TP1/TP2/TP3 in the channel are genuinely different prices, not one price " +
                  "printed three times");
            var ordered = entries.Count(r =>
            {
                if (!r.A.ContainsKey("tps") || r.A["tps"].Count < 2) return false;
                var t = r.A["tps"];
                var up = r.Str("side") == "BUY";
                for (var k = 1; k < t.Count; k++)
                    if (up ? t[k] <= t[k - 1] : t[k] >= t[k - 1]) return true;
                return false;
            });
            Check(ordered == 0, "TP1 is always nearer than TP2, and TP2 than TP3");

            // ---- 4. every closed position is reported exactly once
            var closedIds = w.Orders.Where(o => o.Closed).Select(o => o.Id).ToList();
            Check(exits.Count == closedIds.Count,
                  string.Format("one exit message per closed position, no more and no fewer " +
                                "({0} messages for {1} closes)", exits.Count, closedIds.Count));
            var badRung = exits.Count(r => r.Num("rung") < 1 ||
                                           r.Num("rung") > r.Num("of"));
            Check(badRung == 0, "each exit says which of the three it was, and the number is real");

            // "TP2 hit" has to mean the SECOND rung of that signal, not any
            // rung. Followers size their expectations on which one filled.
            var wrongRung = 0;
            var matchedRungs = new HashSet<double>();
            foreach (var r in rows.Where(x => x.T == "tp"))
            {
                var sig = r.Num("signal");
                var e = entries.FirstOrDefault(x => x.Num("signal") == sig);
                if (e == null || !e.A.ContainsKey("tps")) { wrongRung++; continue; }
                var idx = (int)r.Num("rung") - 1;
                var tps = e.A["tps"];
                if (idx < 0 || idx >= tps.Count || Math.Abs(tps[idx] - r.Num("level")) > 0.011)
                    wrongRung++;
                else
                    matchedRungs.Add(r.Num("rung"));
            }
            Check(wrongRung == 0,
                  string.Format("\"TP{0} hit\" always names the rung whose price actually filled " +
                                "({1} wrong)", "n", wrongRung));
            // Whether TP2 and TP3 get REACHED depends on the tape and on the
            // trailing stop, which by design closes most near-misses first. So
            // that is not a bridge property. What IS: two exits from the same
            // signal must never claim the same rung, and "of" must match the
            // number of targets that signal actually had.
            var dupRung = 0;
            var wrongOf = 0;
            foreach (var g in exits.GroupBy(r => r.Num("signal")))
            {
                var seen = new HashSet<double>();
                foreach (var r in g)
                    if (!seen.Add(r.Num("rung"))) dupRung++;
                var e = entries.FirstOrDefault(x => x.Num("signal") == g.Key);
                var want = e != null && e.A.ContainsKey("tps") ? e.A["tps"].Count : -1;
                foreach (var r in g)
                    if ((int)r.Num("of") != want) wrongOf++;
            }
            Check(dupRung == 0,
                  string.Format("two exits from one signal never claim the same rung ({0})", dupRung));
            Check(wrongOf == 0,
                  string.Format("\"of 3\" matches how many targets that signal really had ({0} wrong)",
                                wrongOf));
            Check(matchedRungs.Count >= 1,
                  string.Format("at least one take profit filled and was reported ({0} distinct rungs)",
                                matchedRungs.Count));

            // ---- 5. a take profit is only ever announced when the trade
            //         actually closed at or beyond that take profit
            var falseTp = 0;
            foreach (var r in rows.Where(x => x.T == "tp"))
            {
                var lvl = r.Num("level");
                var px = r.Num("price");
                var up = r.Str("side") == "BUY";
                if (up ? px < lvl - 0.011 : px > lvl + 0.011) falseTp++;
            }
            Check(falseTp == 0,
                  string.Format("no message claims a take profit the market never reached " +
                                "({0} false claims)", falseTp));

            // ---- 6. every row is labelled honestly
            Check(rows.All(r => r.Str("demo") == "true"),
                  "every row is stamped demo — the bot refuses live accounts, so anything else " +
                  "would be a lie in the channel");
            Check(rows.All(r => r.Str("symbol") == "XAUUSD" && r.Str("bot") == "GoldICT"),
                  "every row names the symbol and the bot that produced it");
            Check(rows.All(r => !string.IsNullOrEmpty(r.Str("utc"))),
                  "every row is timestamped");

            // ---- 7. the switch actually switches it off
            var dir2 = Path.Combine(dir, "off");
            Directory.CreateDirectory(dir2);
            var feed2 = Path.Combine(dir2, "signals.jsonl");
            List<double> c2, h2, l2;
            Tape(out c2, out h2, out l2);
            var w2 = Build(c2, h2, l2, feed2, false);
            Run(w2);
            Check(w2.Orders.Count > 20 && !File.Exists(feed2),
                  "with the feed switched off the bot still trades and writes nothing");

            // ---- 7b. with the trail off, the deeper rungs get a chance to fill,
            //          which is where the rung NUMBER earns its keep.
            var dir3 = Path.Combine(dir, "notrail");
            Directory.CreateDirectory(dir3);
            var feed3 = Path.Combine(dir3, "signals.jsonl");
            List<double> c3, h3, l3;
            Tape(out c3, out h3, out l3);
            var w3 = Build(c3, h3, l3, feed3, true);
            w3.Bot.UseTrailingStop = false;
            Run(w3);
            var rows3 = ReadFeed(feed3);
            var rungs3 = new HashSet<double>(rows3.Where(r => r.T == "tp").Select(r => r.Num("rung")));
            Check(rungs3.Count > 1,
                  string.Format("with the trail off, more than one rung fills and each is reported " +
                                "under its own number ({0} distinct: {1})", rungs3.Count,
                                string.Join(",", rungs3.OrderBy(x => x).Select(x => x.ToString()).ToArray())));
            var falseTp3 = rows3.Count(r => r.T == "tp" &&
                (r.Str("side") == "BUY" ? r.Num("price") < r.Num("level") - 0.011
                                        : r.Num("price") > r.Num("level") + 0.011));
            Check(falseTp3 == 0, "and none of those claims a level the market never reached");

            // ---- 8. no credential can reach the feed
            var leaked = rows.Any(r => r.S.Values.Any(v => v.Contains(":AA")) ||
                                       r.S.Values.Any(v => v.Length > 40 && v.Contains(":")));
            Check(!leaked, "nothing that looks like a bot token is written into the feed");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }

        Console.WriteLine();
        if (_fail == 0)
            Console.WriteLine("BRIDGE VERIFIED — the channel says what cTrader did.");
        else
            Console.WriteLine("{0} BRIDGE CHECK(S) FAILED — do not point a channel at this.", _fail);
        return _fail == 0 ? 0 : 1;
    }
}

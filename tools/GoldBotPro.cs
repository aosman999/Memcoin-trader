// GoldBotPro — the 6-voter confluence strategy (SAME as GoldBot) wrapped by
// the agent suite from the terminal system, ported natively to cTrader.
//
// STRATEGY (unchanged): 6 voters — EMA20>EMA75, price>EMA75, MACD>0, RSI>50,
// EMA20 rising, price>20 bars ago — enter when >= VotesNeeded agree AND
// ADX >= AdxMin. Exits: broker-side 0.6% SL / 1.2% TP, optional partial ladder.
//
// THE AGENTS (each a toggle; all read only price/time/own-history — no network):
//   * Session AI  — gold's liquidity clock; skips thin hours and scales risk
//                   up in the London/NY overlap, down in Asia.       [ADOPTED]
//   * Sentinel AI — vetoes entries during a news-grade price shock.  [ADOPTED]
//   * Regime AI   — trending/ranging/chaotic; can skip chaotic tape. [ADVISORY]
//   * Discipline  — Valentini's rule: stop after 3 daily losses.     [LIVE rule]
//   * MistakeAnalyst — fool-me-twice: after 2 losses in the SAME regime today,
//                   bench that regime until tomorrow.  [MEASURED WORSE: default OFF]
//
// NOT ported (can't run in a plain cBot): News AI (needs network/FullAccess +
// a headline feed) and Strategy Mastery (only meaningful with multiple
// strategies; this bot runs one).
//
// DEMO-ONLY by default: this is NEW, less-tested code than the simple bot you
// run live — prove it on demo first. Flip RUN_ON_LIVE to trade real money.
using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class GoldBotPro : Robot
    {
        [Parameter("Votes needed (of 6)", DefaultValue = 5, MinValue = 3, MaxValue = 6, Group = "Signal")]
        public int VotesNeeded { get; set; }

        [Parameter("Minimum ADX", DefaultValue = 18.0, MinValue = 0, MaxValue = 50, Group = "Signal")]
        public double AdxMin { get; set; }

        [Parameter("Risk per trade (%)", DefaultValue = 12.0, MinValue = 0.1, MaxValue = 20.0, Group = "Risk")]
        public double RiskPercent { get; set; }

        [Parameter("Stop loss (%)", DefaultValue = 0.6, MinValue = 0.05, Group = "Exits")]
        public double StopPercent { get; set; }

        [Parameter("Take profit (%)", DefaultValue = 1.2, MinValue = 0.1, Group = "Exits")]
        public double TakeProfitPercent { get; set; }

        [Parameter("Partial TP ladder", DefaultValue = false, Group = "Exits")]
        public bool UsePartialTps { get; set; }

        [Parameter("Ladder partial levels", DefaultValue = 4, MinValue = 1, MaxValue = 8, Group = "Exits")]
        public int LadderLevels { get; set; }

        [Parameter("Session AI (skip thin hours + scale risk)", DefaultValue = true, Group = "Agents")]
        public bool UseSession { get; set; }

        [Parameter("Session min weight", DefaultValue = 0.6, MinValue = 0.0, MaxValue = 1.2, Group = "Agents")]
        public double SessionMinWeight { get; set; }

        [Parameter("Sentinel AI (veto news shocks)", DefaultValue = true, Group = "Agents")]
        public bool UseSentinel { get; set; }

        [Parameter("Regime AI (skip chaotic tape)", DefaultValue = true, Group = "Agents")]
        public bool UseRegimeFilter { get; set; }

        [Parameter("Discipline (stop after N daily losses)", DefaultValue = true, Group = "Agents")]
        public bool UseDiscipline { get; set; }

        [Parameter("Max losses per day", DefaultValue = 3, MinValue = 1, Group = "Agents")]
        public int MaxLossesPerDay { get; set; }

        [Parameter("Mistake Analyst (fool-me-twice, MEASURED WORSE)", DefaultValue = false, Group = "Agents")]
        public bool UseAnalyst { get; set; }

        [Parameter("Daily loss stop (%)", DefaultValue = 15.0, MinValue = 1.0, Group = "Risk")]
        public double DailyLossStopPercent { get; set; }

        [Parameter("Max hold (minutes)", DefaultValue = 600, MinValue = 5, Group = "Risk")]
        public int MaxHoldMinutes { get; set; }

        [Parameter("Allow shorts", DefaultValue = true, Group = "Risk")]
        public bool AllowShort { get; set; }

        [Parameter("Log status every N bars", DefaultValue = 15, MinValue = 0, Group = "Diagnostics")]
        public int StatusEveryBars { get; set; }

        // Safety: DEMO-ONLY unless you deliberately turn this on.
        private const bool RUN_ON_LIVE = false;

        private const string Label = "GoldBotPro";
        private ExponentialMovingAverage _emaFast;
        private ExponentialMovingAverage _emaSlow;
        private RelativeStrengthIndex _rsi;
        private MacdHistogram _macd;
        private DirectionalMovementSystem _dms;
        private int _barCount;
        private bool _stopped;
        private DateTime _sentinelBlockedUntil = DateTime.MinValue;

        // mistake-analyst state
        private string _analystDay = "";
        private readonly Dictionary<string, int> _regimeLossesToday = new Dictionary<string, int>();
        private int _seenClosed;

        // partial-TP ladder state
        private bool _ladderActive;
        private double[] _ladderPrice = new double[0];
        private bool[] _ladderHit = new bool[0];
        private double _trancheUnits;

        protected override void OnStart()
        {
            if (Account.IsLive && !RUN_ON_LIVE)
            {
                Print("REFUSING TO RUN: live account and RUN_ON_LIVE is false. GoldBotPro is demo-only until you prove it. No order placed.");
                _stopped = true;
                Stop();
                return;
            }

            _emaFast = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 20);
            _emaSlow = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 75);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, 14);
            _macd = Indicators.MacdHistogram(Bars.ClosePrices, 26, 12, 9);
            _dms = Indicators.DirectionalMovementSystem(14);
            _seenClosed = OwnHistoryToday().Count;

            var mode = Account.IsLive ? "LIVE — REAL MONEY" : "DEMO";
            Print("GoldBotPro started | {0} | account {1} ({2}) | balance {3:F2} {4} | bars {5}",
                  SymbolName, Account.Number, mode, Account.Balance, Account.Currency,
                  Bars.ClosePrices.Count);
            Print("Config: {0}/6 votes | ADX>={1} | stop {2}% | target {3}% | risk {4}%",
                  VotesNeeded, AdxMin, StopPercent, TakeProfitPercent, RiskPercent);
            Print("Agents: session {0} | sentinel {1} | regime {2} | discipline {3} | analyst {4}",
                  UseSession, UseSentinel, UseRegimeFilter, UseDiscipline, UseAnalyst);
            Print("Exits: {0}", UsePartialTps ? "partial TP ladder ON" : "binary 2:1 (single TP)");
        }

        protected override void OnBar()
        {
            if (_stopped)
                return;
            try { Evaluate(); }
            catch (Exception ex) { Print("ERROR in OnBar: {0} — {1}", ex.GetType().Name, ex.Message); }
        }

        protected override void OnTick()
        {
            if (_stopped || !_ladderActive)
                return;
            try { ManageLadder(); }
            catch (Exception ex) { Print("ERROR in ladder: {0} — {1}", ex.GetType().Name, ex.Message); }
        }

        private void Evaluate()
        {
            _barCount++;
            UpdateMistakeJournal();

            foreach (var pos in OwnPositions().ToList())
            {
                if ((Server.TimeInUtc - pos.EntryTime).TotalMinutes >= MaxHoldMinutes)
                {
                    Print("Closing {0} — max hold {1} min reached.", pos.Id, MaxHoldMinutes);
                    ClosePosition(pos);
                }
            }

            if (Bars.ClosePrices.Count < 120)
                return;

            var dayStart = DayStartEquity();
            if (dayStart > 0 &&
                Account.Equity <= dayStart * (1.0 - DailyLossStopPercent / 100.0))
            {
                foreach (var pos in OwnPositions().ToList())
                    ClosePosition(pos);
                return;
            }

            var now = Server.TimeInUtc;

            // Discipline AI — stop after N daily losses
            if (UseDiscipline && LossesToday() >= MaxLossesPerDay)
                return;

            var close = Bars.ClosePrices.Last(0);
            var emaFast = _emaFast.Result.Last(0);
            var emaFastPrev = _emaFast.Result.Last(3);
            var emaSlow = _emaSlow.Result.Last(0);
            var rsi = _rsi.Result.Last(0);
            var macdHist = _macd.Histogram.Last(0);
            var adx = _dms.ADX.Last(0);
            var past = Bars.ClosePrices.Last(20);

            var bulls = 0;
            if (emaFast > emaSlow) bulls++;
            if (close > emaSlow) bulls++;
            if (macdHist > 0) bulls++;
            if (rsi > 50.0) bulls++;
            if (emaFast > emaFastPrev) bulls++;
            if (close > past) bulls++;
            var bears = 6 - bulls;

            var regime = Regime();

            if (StatusEveryBars > 0 && _barCount % StatusEveryBars == 0)
                Print("status: price {0:F2} | {1} bull / {2} bear | ADX {3:F1} | RSI {4:F0} | regime {5}",
                      close, bulls, bears, adx, rsi, regime);

            if (OwnPositions().Any())
                return;

            // Sentinel AI — veto during a news-grade shock
            if (UseSentinel && !SentinelSafe(now))
                return;

            // Session AI — skip thin hours
            var sessionWeight = SessionWeight(now);
            if (UseSession && sessionWeight < SessionMinWeight)
                return;

            // Regime AI — skip chaotic tape
            if (UseRegimeFilter && regime == "chaotic")
                return;

            // Mistake Analyst — fool-me-twice: bench a regime after 2 losses today
            if (UseAnalyst && RegimeLossesToday(regime) >= 2)
                return;

            if (adx < AdxMin)
                return;

            var riskScale = UseSession ? sessionWeight : 1.0;
            if (bulls >= VotesNeeded)
                OpenTrade(1, bulls, adx, riskScale);
            else if (bears >= VotesNeeded && AllowShort)
                OpenTrade(-1, bears, adx, riskScale);
        }

        private void OpenTrade(int direction, int votes, double adx, double riskScale)
        {
            var price = direction > 0 ? Symbol.Ask : Symbol.Bid;
            if (price <= 0)
                return;

            var stopDist = price * (StopPercent / 100.0);
            var tpDist = price * (TakeProfitPercent / 100.0);
            if (stopDist <= 0)
                return;

            var riskUsd = Account.Equity * (RiskPercent / 100.0) * riskScale;
            var units = Symbol.NormalizeVolumeInUnits(riskUsd / stopDist, RoundingMode.Down);

            var minRisk = Symbol.VolumeInUnitsMin * stopDist;
            if (minRisk > riskUsd * 2.0)
            {
                Print("SKIP: account too small — smallest trade risks {0:F2}, budget {1:F2}.", minRisk, riskUsd);
                return;
            }
            if (units < Symbol.VolumeInUnitsMin)
                units = Symbol.VolumeInUnitsMin;

            var stopPips = stopDist / Symbol.PipSize;
            var tpPips = tpDist / Symbol.PipSize;
            var side = direction > 0 ? TradeType.Buy : TradeType.Sell;

            var result = ExecuteMarketOrder(side, SymbolName, units, Label, stopPips, tpPips);
            if (result.IsSuccessful)
            {
                Print("OPEN {0} {1} units @ {2:F2} | stop {3:F2} | target {4:F2} | {5}/6 votes, ADX {6:F0}, riskx{7:F2}",
                      side, units, price, price - direction * stopDist,
                      price + direction * tpDist, votes, adx, riskScale);
                SetupLadder(direction, price, result.Position.VolumeInUnits);
            }
            else
                Print("ORDER FAILED: {0}", result.Error);
        }

        // ---- partial-TP ladder ------------------------------------------
        private void SetupLadder(int direction, double entry, double totalUnits)
        {
            _ladderActive = false;
            _ladderPrice = new double[0];
            _ladderHit = new bool[0];
            if (!UsePartialTps)
                return;
            var tranche = Symbol.NormalizeVolumeInUnits(totalUnits / (LadderLevels + 1.0), RoundingMode.Down);
            if (tranche < Symbol.VolumeInUnitsMin)
            {
                Print("Partial TP ladder OFF this trade — {0} units won't split into {1} chunks.", totalUnits, LadderLevels + 1);
                return;
            }
            _trancheUnits = tranche;
            _ladderPrice = new double[LadderLevels];
            _ladderHit = new bool[LadderLevels];
            for (var k = 0; k < LadderLevels; k++)
            {
                var frac = (TakeProfitPercent / 100.0) * ((k + 1.0) / (LadderLevels + 1.0));
                _ladderPrice[k] = entry + direction * entry * frac;
            }
            _ladderActive = true;
        }

        private void ManageLadder()
        {
            var pos = OwnPositions().FirstOrDefault();
            if (pos == null) { _ladderActive = false; return; }
            var price = pos.TradeType == TradeType.Buy ? Symbol.Bid : Symbol.Ask;
            for (var k = 0; k < _ladderPrice.Length; k++)
            {
                if (_ladderHit[k]) continue;
                var reached = pos.TradeType == TradeType.Buy ? price >= _ladderPrice[k] : price <= _ladderPrice[k];
                if (!reached) continue;
                _ladderHit[k] = true;
                var vol = Symbol.NormalizeVolumeInUnits(Math.Min(_trancheUnits, pos.VolumeInUnits), RoundingMode.Down);
                if (vol >= Symbol.VolumeInUnitsMin && vol < pos.VolumeInUnits)
                {
                    var r = ClosePosition(pos, vol);
                    if (r.IsSuccessful)
                        Print("Partial TP {0}/{1} @ {2:F2} — banked {3} units", k + 1, _ladderPrice.Length, _ladderPrice[k], vol);
                }
            }
        }

        // ---- agents ------------------------------------------------------
        // Session AI: 0.4..1.2 weight by UTC hour (London/NY overlap = prime).
        private double SessionWeight(DateTime utc)
        {
            var h = utc.Hour + utc.Minute / 60.0;
            if (h >= 12 && h < 16) return 1.2;
            if (h >= 7 && h < 12) return 1.0;
            if (h >= 16 && h < 21) return 0.9;
            if (h >= 1 && h < 7) return 0.6;
            return 0.4;
        }

        // Sentinel AI: block 12 min after a news-grade single-bar shock or burst.
        private bool SentinelSafe(DateTime now)
        {
            if (now < _sentinelBlockedUntil) return false;
            var c = Bars.ClosePrices;
            if (c.Count < 46) return true;
            var lastMove = c.Last(1) != 0 ? Math.Abs(c.Last(0) / c.Last(1) - 1.0) : 0.0;
            var burst = RealizedVol(6);
            var baseVol = RealizedVol(45);
            if (lastMove > 0.0022 || (baseVol > 0 && burst / baseVol > 3.5))
            {
                _sentinelBlockedUntil = now.AddMinutes(12);
                return false;
            }
            return true;
        }

        // Regime AI: trending / ranging / chaotic from the last ~4 hours.
        private string Regime()
        {
            var c = Bars.ClosePrices;
            if (c.Count < 120) return "ranging";
            var n = Math.Min(240, c.Count - 1);
            double net = Math.Abs(c.Last(0) - c.Last(n));
            double path = 0;
            for (var i = 0; i < n; i++) path += Math.Abs(c.Last(i) - c.Last(i + 1));
            var efficiency = path > 0 ? net / path : 0.0;
            var rvShort = RealizedVol(30);
            var rvLong = RealizedVol(n);
            if (rvLong > 0 && rvShort / rvLong > 2.4) return "chaotic";
            return efficiency >= 0.30 ? "trending" : "ranging";
        }

        private double RealizedVol(int window)
        {
            var c = Bars.ClosePrices;
            var n = Math.Min(window, c.Count - 1);
            if (n < 2) return 0.0;
            double sum = 0; var cnt = 0;
            for (var i = 0; i < n; i++)
            {
                var a = c.Last(i); var b = c.Last(i + 1);
                if (b > 0) { sum += Math.Abs(Math.Log(a / b)); cnt++; }
            }
            return cnt > 0 ? sum / cnt : 0.0;
        }

        // Discipline AI: losing own trades closed today.
        private int LossesToday()
        {
            var midnight = Server.TimeInUtc.Date;
            return History.Count(t => t.Label == Label && t.SymbolName == SymbolName
                                      && t.ClosingTime >= midnight && t.NetProfit < 0);
        }

        // Mistake Analyst: count today's losses attributed to a regime.
        private int RegimeLossesToday(string regime)
        {
            return _regimeLossesToday.TryGetValue(regime, out var v) ? v : 0;
        }

        private void UpdateMistakeJournal()
        {
            var day = Server.TimeInUtc.Date.ToString("yyyy-MM-dd");
            if (day != _analystDay)
            {
                _analystDay = day;
                _regimeLossesToday.Clear();
                _seenClosed = OwnHistoryToday().Count;
            }
            var closedToday = OwnHistoryToday();
            if (closedToday.Count > _seenClosed)
            {
                var regime = Regime();
                for (var i = _seenClosed; i < closedToday.Count; i++)
                {
                    if (closedToday[i].NetProfit < 0)
                    {
                        _regimeLossesToday.TryGetValue(regime, out var v);
                        _regimeLossesToday[regime] = v + 1;
                    }
                }
                _seenClosed = closedToday.Count;
            }
        }

        private List<HistoricalTrade> OwnHistoryToday()
        {
            var midnight = Server.TimeInUtc.Date;
            return History.Where(t => t.Label == Label && t.SymbolName == SymbolName
                                      && t.ClosingTime >= midnight)
                          .OrderBy(t => t.ClosingTime).ToList();
        }

        private IEnumerable<Position> OwnPositions()
        {
            return Positions.Where(p => p.Label == Label && p.SymbolName == SymbolName);
        }

        private double DayStartEquity()
        {
            var midnight = Server.TimeInUtc.Date;
            var realizedToday = History
                .Where(t => t.Label == Label && t.ClosingTime >= midnight)
                .Sum(t => t.NetProfit);
            return Account.Equity - realizedToday;
        }

        protected override void OnStop()
        {
            Print("GoldBotPro stopped. Open positions keep their broker-side SL/TP.");
        }
    }
}

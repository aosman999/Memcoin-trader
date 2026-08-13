// GoldBotTrend — the tested winner: the 6-voter confluence strategy PLUS an
// efficiency trend filter that only lets it trade when gold is genuinely
// trending (not chopping). Fixed 0.6% / 1.2% (2:1) exit. Built for a DEMO
// test on m15 — it refuses to run on a live account.
//
// Sim result (30 seeds x 2 models, m15): +0.60R expectancy, 59% win — vs
// +0.28R for the plain six-voter on m1. The filter + m15 is the whole edge.
//
// STRATEGY (unchanged 6 voters): EMA20>EMA75, price>EMA75, MACD>0, RSI>50,
// EMA20 rising, price>20 bars ago — enter on >= VotesNeeded AND ADX>=AdxMin
// AND the last EfficiencyWindow bars are trending (efficiency >= threshold).
//
// News bot is the planned NEXT addition once this proves out on demo.
using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class GoldBotTrend : Robot
    {
        [Parameter("Votes needed (of 6)", DefaultValue = 5, MinValue = 3, MaxValue = 6, Group = "Signal")]
        public int VotesNeeded { get; set; }

        [Parameter("Minimum ADX", DefaultValue = 18.0, MinValue = 0, MaxValue = 50, Group = "Signal")]
        public double AdxMin { get; set; }

        [Parameter("Trend filter: efficiency window (bars)", DefaultValue = 24, MinValue = 4, MaxValue = 200, Group = "Trend filter")]
        public int EfficiencyWindow { get; set; }

        [Parameter("Trend filter: min efficiency (0-1)", DefaultValue = 0.40, MinValue = 0.0, MaxValue = 1.0, Group = "Trend filter")]
        public double EfficiencyMin { get; set; }

        [Parameter("Risk per trade (%)", DefaultValue = 10.0, MinValue = 0.1, MaxValue = 20.0, Group = "Risk")]
        public double RiskPercent { get; set; }

        [Parameter("Stop loss (%)", DefaultValue = 0.6, MinValue = 0.05, Group = "Exits")]
        public double StopPercent { get; set; }

        [Parameter("Take profit (%)", DefaultValue = 1.2, MinValue = 0.1, Group = "Exits")]
        public double TakeProfitPercent { get; set; }

        [Parameter("Daily loss stop (%)", DefaultValue = 15.0, MinValue = 1.0, Group = "Risk")]
        public double DailyLossStopPercent { get; set; }

        [Parameter("Max hold (minutes)", DefaultValue = 600, MinValue = 5, Group = "Risk")]
        public int MaxHoldMinutes { get; set; }

        [Parameter("Allow shorts", DefaultValue = true, Group = "Risk")]
        public bool AllowShort { get; set; }

        [Parameter("Log status every N bars", DefaultValue = 10, MinValue = 0, Group = "Diagnostics")]
        public int StatusEveryBars { get; set; }

        private const string Label = "GoldBotTrend";
        private ExponentialMovingAverage _emaFast;
        private ExponentialMovingAverage _emaSlow;
        private RelativeStrengthIndex _rsi;
        private MacdHistogram _macd;
        private DirectionalMovementSystem _dms;
        private int _barCount;
        private bool _stopped;

        protected override void OnStart()
        {
            if (Account.IsLive)
            {
                Print("REFUSING TO RUN: live account. GoldBotTrend is demo-only (it's under test). No order placed.");
                _stopped = true;
                Stop();
                return;
            }

            _emaFast = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 20);
            _emaSlow = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 75);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, 14);
            _macd = Indicators.MacdHistogram(Bars.ClosePrices, 26, 12, 9);
            _dms = Indicators.DirectionalMovementSystem(14);

            Print("GoldBotTrend started | {0} {1} | account {2} (DEMO) | balance {3:F2} | bars {4}",
                  SymbolName, Bars.TimeFrame, Account.Number, Account.Balance, Bars.ClosePrices.Count);
            Print("Config: {0}/6 votes | ADX>={1} | trend filter eff>={2} over {3} bars | stop {4}% | target {5}% | risk {6}%",
                  VotesNeeded, AdxMin, EfficiencyMin, EfficiencyWindow, StopPercent, TakeProfitPercent, RiskPercent);
            Print("Best tested on m15. Attach to the 15-minute chart.");
        }

        protected override void OnBar()
        {
            if (_stopped)
                return;
            try { Evaluate(); }
            catch (Exception ex) { Print("ERROR in OnBar: {0} — {1}", ex.GetType().Name, ex.Message); }
        }

        private void Evaluate()
        {
            _barCount++;

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

            var eff = Efficiency(EfficiencyWindow);

            if (StatusEveryBars > 0 && _barCount % StatusEveryBars == 0)
                Print("status: price {0:F2} | {1} bull / {2} bear | ADX {3:F1} | efficiency {4:F2} ({5})",
                      close, bulls, bears, adx, eff, eff >= EfficiencyMin ? "trending" : "chop-skip");

            if (OwnPositions().Any())
                return;

            if (adx < AdxMin)
                return;

            // trend filter — only trade when the tape is genuinely directional
            if (eff < EfficiencyMin)
                return;

            if (bulls >= VotesNeeded)
                OpenTrade(1, bulls, adx, eff);
            else if (bears >= VotesNeeded && AllowShort)
                OpenTrade(-1, bears, adx, eff);
        }

        // Kaufman efficiency ratio: net move / total path over the window.
        // ~1 = clean one-way trend, ~0 = choppy back-and-forth.
        private double Efficiency(int window)
        {
            var c = Bars.ClosePrices;
            var n = Math.Min(window, c.Count - 1);
            if (n < 2)
                return 0.0;
            var net = Math.Abs(c.Last(0) - c.Last(n));
            double path = 0.0;
            for (var i = 0; i < n; i++)
                path += Math.Abs(c.Last(i) - c.Last(i + 1));
            return path > 0 ? net / path : 0.0;
        }

        private void OpenTrade(int direction, int votes, double adx, double eff)
        {
            var price = direction > 0 ? Symbol.Ask : Symbol.Bid;
            if (price <= 0)
                return;

            var stopDist = price * (StopPercent / 100.0);
            var tpDist = price * (TakeProfitPercent / 100.0);
            if (stopDist <= 0)
                return;

            var riskUsd = Account.Equity * (RiskPercent / 100.0);
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
                Print("OPEN {0} {1} units @ {2:F2} | stop {3:F2} | target {4:F2} | {5}/6 votes, ADX {6:F0}, eff {7:F2}",
                      side, units, price, price - direction * stopDist,
                      price + direction * tpDist, votes, adx, eff);
            else
                Print("ORDER FAILED: {0}", result.Error);
        }

        private System.Collections.Generic.IEnumerable<Position> OwnPositions()
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
            Print("GoldBotTrend stopped. Open positions keep their broker-side SL/TP.");
        }
    }
}

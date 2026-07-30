// GoldSignalBot — trades the SAME confluence logic as the GoldTrader PRO
// TradingView indicator, natively inside cTrader. What you see on the
// chart is what it trades.
//
// Six voters each call bull or bear every bar:
//   1. EMA 20 vs EMA 75 trend        4. RSI above/below 50
//   2. 15-minute EMA20 trend         5. Supertrend(10,3) direction
//   3. MACD histogram sign           6. Price vs rolling VWAP
// It enters only when >= "Votes needed" agree AND ADX confirms the tape
// is trending (not chop). Exits are binary 2:1 (stop -0.6%, target +1.2%),
// broker-side. Risk-based sizing, -15% daily loss stop, one position at a
// time, 600-minute max hold.
//
// Uses cTrader's built-in indicators (robust) and is DEMO-ONLY: it refuses
// to run on a live account. The whole per-bar body is wrapped so any error
// is printed to the Log with its exact location instead of crashing.
//
// HONEST NOTE: this confluence logic is transparent but NOT yet backtested
// for win rate/expectancy — running it on demo is how we measure it.
//
// Install: cTrader -> Automate -> New cBot -> paste over everything ->
// Build -> add instance on XAUUSD m1 -> Play.
using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class GoldSignalBot : Robot
    {
        [Parameter("Votes needed (of 6)", DefaultValue = 5, MinValue = 3, MaxValue = 6, Group = "Signal")]
        public int VotesNeeded { get; set; }

        [Parameter("Minimum ADX (trend strength)", DefaultValue = 18.0, MinValue = 5, MaxValue = 40, Group = "Signal")]
        public double AdxMin { get; set; }

        [Parameter("Risk per trade (%)", DefaultValue = 10.0, MinValue = 0.1, MaxValue = 20.0, Group = "Risk")]
        public double RiskPercent { get; set; }

        [Parameter("Stop loss (%)", DefaultValue = 0.6, MinValue = 0.1, Group = "Risk")]
        public double StopPercent { get; set; }

        [Parameter("Take profit (%)", DefaultValue = 1.2, MinValue = 0.2, Group = "Risk")]
        public double TakeProfitPercent { get; set; }

        [Parameter("Daily loss stop (%)", DefaultValue = 15.0, MinValue = 1.0, Group = "Risk")]
        public double DailyLossStopPercent { get; set; }

        [Parameter("Max hold (minutes)", DefaultValue = 600, MinValue = 10, Group = "Risk")]
        public int MaxHoldMinutes { get; set; }

        [Parameter("Allow shorts", DefaultValue = true, Group = "Risk")]
        public bool AllowShort { get; set; }

        [Parameter("VWAP window (bars)", DefaultValue = 60, MinValue = 10, Group = "Signal")]
        public int VwapWindow { get; set; }

        private const string Label = "GoldSignalBot";
        private ExponentialMovingAverage _emaFast;
        private ExponentialMovingAverage _emaSlow;
        private ExponentialMovingAverage _emaM15;
        private Bars _m15;
        private MacdHistogram _macd;
        private RelativeStrengthIndex _rsi;
        private Supertrend _supertrend;
        private DirectionalMovementSystem _dms;
        private bool _locked;

        protected override void OnStart()
        {
            if (!EnforceDemoOnly())
                return;

            _emaFast = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 20);
            _emaSlow = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 75);
            _macd = Indicators.MacdHistogram(Bars.ClosePrices, 26, 12, 9);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, 14);
            _supertrend = Indicators.Supertrend(10, 3);
            _dms = Indicators.DirectionalMovementSystem(14);
            _m15 = MarketData.GetBars(TimeFrame.Minute15);
            _emaM15 = Indicators.ExponentialMovingAverage(_m15.ClosePrices, 20);

            Print("GoldSignalBot started on {0} | account {1} ({2}) | balance {3:F2} {4}",
                  SymbolName, Account.Number, Account.IsLive ? "LIVE" : "DEMO",
                  Account.Balance, Account.Currency);
            Print("Config: {0}/6 votes | ADX>={1} | stop {2}% target {3}% (2:1) | risk {4}%",
                  VotesNeeded, AdxMin, StopPercent, TakeProfitPercent, RiskPercent);
        }

        protected override void OnBar()
        {
            if (_locked || !EnforceDemoOnly())
                return;

            try
            {
                Evaluate();
            }
            catch (Exception ex)
            {
                Print("ERROR in OnBar: {0} — {1}", ex.GetType().Name, ex.Message);
            }
        }

        private void Evaluate()
        {
            // time-stop management for any open position
            foreach (var pos in OwnPositions().ToList())
            {
                if ((Server.TimeInUtc - pos.EntryTime).TotalMinutes >= MaxHoldMinutes)
                {
                    Print("Closing {0} — max hold reached.", pos.Id);
                    ClosePosition(pos);
                }
            }

            if (Bars.ClosePrices.Count < 260 || _m15.ClosePrices.Count < 25)
                return;                                   // warm-up

            // ---- daily loss stop (-15%), reconstructed from history ------
            var dayStart = ReconstructDayStartEquity();
            if (dayStart > 0 &&
                Account.Equity <= dayStart * (1.0 - DailyLossStopPercent / 100.0))
            {
                foreach (var pos in OwnPositions().ToList())
                    ClosePosition(pos);
                return;
            }

            if (OwnPositions().Any())
                return;                                   // one position at a time

            // ---- the six voters ------------------------------------------
            var close = Bars.ClosePrices.Last(0);
            var emaFast = _emaFast.Result.Last(0);
            var emaSlow = _emaSlow.Result.Last(0);
            var v1bull = emaFast > emaSlow;

            var m15Now = _emaM15.Result.Last(0);
            var m15Prev = _emaM15.Result.Last(1);
            var v2bull = m15Now >= m15Prev;

            var v3bull = _macd.Histogram.Last(0) > 0;

            var v4bull = _rsi.Result.Last(0) > 50.0;

            var stBull = !double.IsNaN(_supertrend.UpTrend.Last(0));

            var vwap = RollingVwap(VwapWindow);
            var v6bull = vwap > 0 && close > vwap;

            var bulls = (v1bull ? 1 : 0) + (v2bull ? 1 : 0) + (v3bull ? 1 : 0)
                      + (v4bull ? 1 : 0) + (stBull ? 1 : 0) + (v6bull ? 1 : 0);
            var bears = 6 - bulls;

            var adx = _dms.ADX.Last(0);
            if (adx < AdxMin)
                return;                                   // chop filter

            if (bulls >= VotesNeeded)
                OpenTrade(1, bulls, adx);
            else if (bears >= VotesNeeded && AllowShort)
                OpenTrade(-1, bears, adx);
        }

        private double RollingVwap(int window)
        {
            var n = Math.Min(window, Bars.ClosePrices.Count);
            double pv = 0, vol = 0;
            for (var i = 0; i < n; i++)
            {
                var typical = (Bars.HighPrices.Last(i) + Bars.LowPrices.Last(i)
                               + Bars.ClosePrices.Last(i)) / 3.0;
                var v = Bars.TickVolumes.Last(i);
                pv += typical * v;
                vol += v;
            }
            return vol > 0 ? pv / vol : 0.0;
        }

        private void OpenTrade(int direction, int votes, double adx)
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
            if (minRisk > Account.Equity * (RiskPercent / 100.0) * 2.0)
            {
                Print("SKIP: account too small — min volume risks {0:F2}. Fund higher.", minRisk);
                return;
            }
            if (units < Symbol.VolumeInUnitsMin)
                units = Symbol.VolumeInUnitsMin;

            var stopPips = stopDist / Symbol.PipSize;
            var tpPips = tpDist / Symbol.PipSize;
            var side = direction > 0 ? TradeType.Buy : TradeType.Sell;

            var result = ExecuteMarketOrder(side, SymbolName, units, Label, stopPips, tpPips);
            if (result.IsSuccessful)
                Print("OPEN {0} {1} units @ {2:F2} | stop {3:F2} target {4:F2} | {5}/6 votes, ADX {6:F0}",
                      side, units, price, price - direction * stopDist,
                      price + direction * tpDist, votes, adx);
            else
                Print("ORDER FAILED: {0}", result.Error);
        }

        private bool EnforceDemoOnly()
        {
            if (!Account.IsLive)
                return true;
            _locked = true;
            Print("REFUSING TO RUN: live account. GoldSignalBot is demo-only. No order placed.");
            Stop();
            return false;
        }

        private System.Collections.Generic.IEnumerable<Position> OwnPositions()
        {
            return Positions.Where(p => p.Label == Label && p.SymbolName == SymbolName);
        }

        private double ReconstructDayStartEquity()
        {
            var midnight = Server.TimeInUtc.Date;
            var realizedToday = History
                .Where(t => t.Label == Label && t.ClosingTime >= midnight)
                .Sum(t => t.NetProfit);
            return Account.Equity - realizedToday;
        }

        protected override void OnStop()
        {
            Print("GoldSignalBot stopped. Open positions keep their broker-side SL/TP.");
        }
    }
}

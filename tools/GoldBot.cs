// GoldBot — the 6-voter confluence strategy (UNCHANGED) with fixed 0.6% /
// 1.2% (2:1) exits, plus an OPTIONAL partial take-profit ladder.
//
// STRATEGY (unchanged): 6 voters — EMA20>EMA75, price>EMA75, MACD>0,
// RSI>50, EMA20 rising, price>20 bars ago — enter when >= VotesNeeded
// agree AND ADX >= AdxMin. One position at a time.
//
// EXITS: broker-side SL at -StopPercent and a final TP at +TakeProfitPercent
// (2:1) — always present, so protection survives a disconnect. When
// "Partial TP ladder" is ON, the position is split into (levels+1) equal
// chunks: each of the first N chunks is banked at an intermediate price on
// the way to the final TP, and the last chunk rides to the full TP. The SL
// is unchanged, so losers still take the full stop (this is measured to
// LOWER expectancy vs plain binary — see docs/PERFORMANCE.md — kept behind
// a flag by owner request). Needs a position big enough to split; if a
// chunk would be below the broker minimum, the ladder auto-disables for
// that trade and it rides the single TP.
//
// Safety: demo-only lock, -15% daily loss stop, max hold, small-account
// guard. OnBar and OnTick wrapped in try/catch.
using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class GoldBot : Robot
    {
        [Parameter("Votes needed (of 6)", DefaultValue = 5, MinValue = 3, MaxValue = 6, Group = "Signal")]
        public int VotesNeeded { get; set; }

        [Parameter("Minimum ADX", DefaultValue = 18.0, MinValue = 0, MaxValue = 50, Group = "Signal")]
        public double AdxMin { get; set; }

        [Parameter("Risk per trade (%)", DefaultValue = 13.5, MinValue = 0.1, MaxValue = 20.0, Group = "Risk")]
        public double RiskPercent { get; set; }

        [Parameter("Stop loss (%)", DefaultValue = 0.6, MinValue = 0.05, Group = "Exits")]
        public double StopPercent { get; set; }

        [Parameter("Take profit (%)", DefaultValue = 1.2, MinValue = 0.1, Group = "Exits")]
        public double TakeProfitPercent { get; set; }

        [Parameter("Partial TP ladder", DefaultValue = false, Group = "Exits")]
        public bool UsePartialTps { get; set; }

        [Parameter("Ladder partial levels", DefaultValue = 4, MinValue = 1, MaxValue = 8, Group = "Exits")]
        public int LadderLevels { get; set; }

        [Parameter("Daily loss stop (%)", DefaultValue = 15.0, MinValue = 1.0, Group = "Risk")]
        public double DailyLossStopPercent { get; set; }

        [Parameter("Max hold (minutes)", DefaultValue = 600, MinValue = 5, Group = "Risk")]
        public int MaxHoldMinutes { get; set; }

        [Parameter("Allow shorts", DefaultValue = true, Group = "Risk")]
        public bool AllowShort { get; set; }

        [Parameter("Log status every N bars", DefaultValue = 15, MinValue = 0, Group = "Diagnostics")]
        public int StatusEveryBars { get; set; }

        private const string Label = "GoldBot";
        private ExponentialMovingAverage _emaFast;
        private ExponentialMovingAverage _emaSlow;
        private RelativeStrengthIndex _rsi;
        private MacdHistogram _macd;
        private DirectionalMovementSystem _dms;
        private int _barCount;
        private bool _stopped;

        // ---- partial-TP ladder state (one position at a time) --------------
        private bool _ladderActive;
        private double[] _ladderPrice = new double[0];
        private bool[] _ladderHit = new bool[0];
        private double _trancheUnits;

        protected override void OnStart()
        {
            if (Account.IsLive)
            {
                Print("REFUSING TO RUN: live account. GoldBot is demo-only. No order placed.");
                _stopped = true;
                Stop();
                return;
            }

            _emaFast = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 20);
            _emaSlow = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 75);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, 14);
            _macd = Indicators.MacdHistogram(Bars.ClosePrices, 26, 12, 9);
            _dms = Indicators.DirectionalMovementSystem(14);

            Print("GoldBot started | {0} | account {1} (DEMO) | balance {2:F2} | bars {3}",
                  SymbolName, Account.Number, Account.Balance, Bars.ClosePrices.Count);
            Print("Config: {0}/6 votes | ADX>={1} | stop {2}% | target {3}% | risk {4}%",
                  VotesNeeded, AdxMin, StopPercent, TakeProfitPercent, RiskPercent);
            Print("Exits: {0}", UsePartialTps
                  ? string.Format("partial TP ladder ON — {0} partials + runner to {1}% (SL still full {2}%)",
                                  LadderLevels, TakeProfitPercent, StopPercent)
                  : "binary 2:1 (single TP)");
        }

        protected override void OnBar()
        {
            if (_stopped)
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

        // Partial TPs are checked every tick so intrabar spikes are caught,
        // not just once per bar. No-op when the ladder is off.
        protected override void OnTick()
        {
            if (_stopped || !_ladderActive)
                return;

            try
            {
                ManageLadder();
            }
            catch (Exception ex)
            {
                Print("ERROR in ladder: {0} — {1}", ex.GetType().Name, ex.Message);
            }
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

            if (StatusEveryBars > 0 && _barCount % StatusEveryBars == 0)
                Print("status: price {0:F2} | {1} bull / {2} bear | ADX {3:F1} | RSI {4:F0}",
                      close, bulls, bears, adx, rsi);

            if (OwnPositions().Any())
                return;

            if (adx < AdxMin)
                return;

            if (bulls >= VotesNeeded)
                OpenTrade(1, bulls, adx);
            else if (bears >= VotesNeeded && AllowShort)
                OpenTrade(-1, bears, adx);
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
            if (minRisk > riskUsd * 2.0)
            {
                Print("SKIP: account too small — smallest trade risks {0:F2}, budget {1:F2}.",
                      minRisk, riskUsd);
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
                Print("OPEN {0} {1} units @ {2:F2} | stop {3:F2} | target {4:F2} | {5}/6 votes, ADX {6:F0}",
                      side, units, price, price - direction * stopDist,
                      price + direction * tpDist, votes, adx);
                SetupLadder(direction, price, result.Position.VolumeInUnits);
            }
            else
                Print("ORDER FAILED: {0}", result.Error);
        }

        // Lay out the partial-TP ladder for the position just opened. Splits
        // the volume into (levels+1) equal chunks and places the intermediate
        // price targets between entry and the final TP. Disables itself if a
        // chunk would be below the broker minimum.
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
                Print("Partial TP ladder OFF this trade — {0} units won't split into {1} chunks. Riding single TP.",
                      totalUnits, LadderLevels + 1);
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
            Print("Partial TP ladder: {0} partials of {1} units at {2}",
                  LadderLevels, tranche,
                  string.Join(", ", _ladderPrice.Select(p => p.ToString("F2"))));
        }

        // Bank each intermediate target as price reaches it, always leaving a
        // runner for the broker-side final TP. Runs every tick.
        private void ManageLadder()
        {
            var pos = OwnPositions().FirstOrDefault();
            if (pos == null)
            {
                _ladderActive = false;
                return;
            }

            var price = pos.TradeType == TradeType.Buy ? Symbol.Bid : Symbol.Ask;
            for (var k = 0; k < _ladderPrice.Length; k++)
            {
                if (_ladderHit[k])
                    continue;
                var reached = pos.TradeType == TradeType.Buy
                    ? price >= _ladderPrice[k]
                    : price <= _ladderPrice[k];
                if (!reached)
                    continue;

                _ladderHit[k] = true;
                var vol = Symbol.NormalizeVolumeInUnits(
                    Math.Min(_trancheUnits, pos.VolumeInUnits), RoundingMode.Down);
                // keep a runner: never close the whole position on a partial
                if (vol >= Symbol.VolumeInUnitsMin && vol < pos.VolumeInUnits)
                {
                    var r = ClosePosition(pos, vol);
                    if (r.IsSuccessful)
                        Print("Partial TP {0}/{1} @ {2:F2} — banked {3} units, {4} left to runner",
                              k + 1, _ladderPrice.Length, _ladderPrice[k], vol, pos.VolumeInUnits);
                    else
                        Print("Partial TP {0} close failed: {1}", k + 1, r.Error);
                }
            }
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
            Print("GoldBot stopped. Open positions keep their broker-side SL/TP.");
        }
    }
}

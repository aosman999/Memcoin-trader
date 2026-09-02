// GoldDataDump — writes the chart's real OHLC history to a CSV file.
//
// WHY THIS EXISTS. ICT and CRT are built on liquidity sweeps: price wicks
// through an obvious high or low, takes the stops resting there, and reverses.
// Testing that needs two things this project does not otherwise have:
//
//   1. REAL WICKS. The backtester's tapes are fractional Brownian motion. They
//      have highs and lows, but no resting stop orders and therefore no stop
//      hunting -- the exact mechanism these methods claim. Measuring ICT on
//      them is not a fair test of ICT, and should not be reported as one.
//   2. REAL GOLD. This project's cloud machine cannot reach any market data
//      provider, so the only route to genuine XAUUSD history is the owner's
//      own terminal, which already has it.
//
// HOW TO USE IT
//   1. Open an XAUUSD chart on the timeframe you want tested (m5 and h1 are
//      the useful pair -- h1 for the CRT ranges, m5 for the entries).
//   2. SCROLL THE CHART LEFT until it stops loading older candles. cTrader
//      only holds what it has downloaded, so this is what decides how much
//      history you get. Do this before starting the bot.
//   3. Add this cBot, set OutputPath, start it. It writes the file at start-up
//      and stops itself. One run per timeframe.
//   4. Send the CSV.
//
// It places no orders and reads no network.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class GoldDataDump : Robot
    {
        [Parameter("Write the CSV here", DefaultValue = "/Users/Shared/xauusd_bars.csv", Group = "Output")]
        public string OutputPath { get; set; }

        [Parameter("Most recent bars to write (0 = everything loaded)", DefaultValue = 0, MinValue = 0, Group = "Output")]
        public int MaxBars { get; set; }

        // CORRELATED INSTRUMENTS, for SMT divergence.
        //
        // ICT's Month 4 material is intermarket: the signal is not a pattern in
        // one chart, it is two correlated markets DISAGREEING at a swing. In the
        // notes the interest-rate triad fails to confirm — the T-Bond prints a
        // lower high while the 10-year and 5-year print higher highs — and the
        // dollar moves immediately after.
        //
        // That is a genuinely different kind of signal from anything tested here
        // so far, because it uses information from OUTSIDE the series being
        // traded. Every pattern measured to date (CRT, sweeps, order blocks) is
        // a re-reading of gold's own price, and a re-reading cannot add
        // information that the price does not already contain. A second
        // correlated market can.
        //
        // It also cannot be tested on this project's tapes at all: they are
        // single-instrument. Hence this — export gold alongside its correlated
        // markets, time-aligned, so the divergence can be measured on real data.
        //
        // Useful partners for gold: XAGUSD (silver, the classic precious-metals
        // SMT pair) and a dollar proxy such as EURUSD or USDJPY. Bond futures
        // are the notes' own example but retail FX brokers rarely carry them.
        [Parameter("Also export these symbols (comma separated)", DefaultValue = "XAGUSD,EURUSD", Group = "Correlated")]
        public string ExtraSymbols { get; set; }

        // One file per correlated symbol, same timeframe, named alongside the
        // main one. They are time-stamped, so alignment happens at analysis time
        // rather than being baked in here where a bug would be invisible.
        private void ExportCorrelated()
        {
            if (string.IsNullOrEmpty(ExtraSymbols)) return;
            var names = ExtraSymbols.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in names)
            {
                var name = raw.Trim();
                if (name.Length == 0 || name == SymbolName) continue;
                Bars b = null;
                try { b = MarketData.GetBars(Bars.TimeFrame, name); }
                catch (Exception ex)
                {
                    Print("{0}: could not load ({1}). Check the symbol name in your broker's list.",
                          name, ex.GetType().Name);
                    continue;
                }
                if (b == null || b.ClosePrices == null || b.ClosePrices.Count < 2)
                {
                    Print("{0}: no history available. Open a {0} chart once so cTrader downloads it, then re-run.", name);
                    continue;
                }
                var path = SidecarPath(OutputPath, name);
                var sb2 = new StringBuilder();
                sb2.Append("time_utc,open,high,low,close,volume\n");
                var cnt = 0;
                for (var back = b.ClosePrices.Count - 1; back >= 0; back--)
                {
                    var h = b.HighPrices.Last(back);
                    var l = b.LowPrices.Last(back);
                    var c = b.ClosePrices.Last(back);
                    var o = b.OpenPrices.Last(back);
                    if (h < l || c > h || c < l || o > h || o < l) continue;
                    sb2.Append(b.OpenTimes.Last(back).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(',')
                       .Append(o.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                       .Append(h.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                       .Append(l.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                       .Append(c.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                       .Append(b.TickVolumes.Last(back).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                    cnt++;
                }
                try
                {
                    System.IO.File.WriteAllText(path, sb2.ToString());
                    Print("Wrote {0} bars of {1} to {2}", cnt, name, path);
                }
                catch (Exception ex)
                {
                    Print("{0}: could not write {1} ({2}).", name, path, ex.GetType().Name);
                }
            }
        }

        // "…/xauusd_bars.csv" + "XAGUSD" -> "…/xauusd_bars_XAGUSD.csv"
        public static string SidecarPath(string basePath, string symbol)
        {
            if (string.IsNullOrEmpty(basePath)) return symbol + ".csv";
            var dot = basePath.LastIndexOf('.');
            var slash = Math.Max(basePath.LastIndexOf('/'), basePath.LastIndexOf('\\'));
            if (dot <= slash) return basePath + "_" + symbol + ".csv";
            return basePath.Substring(0, dot) + "_" + symbol + basePath.Substring(dot);
        }

        protected override void OnStart()
        {
            var n = Bars.ClosePrices.Count;
            if (n < 2)
            {
                Print("NOTHING TO WRITE: the chart has {0} bars. Scroll left to load history first.", n);
                Stop();
                return;
            }

            var take = (MaxBars > 0 && MaxBars < n) ? MaxBars : n;
            var from = n - take;

            var sb = new StringBuilder();
            sb.Append("time_utc,open,high,low,close,volume\n");
            var written = 0;
            var badHighLow = 0;
            for (var k = from; k < n; k++)
            {
                var back = n - 1 - k;                      // Last(0) is the newest
                var o = Bars.OpenPrices.Last(back);
                var h = Bars.HighPrices.Last(back);
                var l = Bars.LowPrices.Last(back);
                var c = Bars.ClosePrices.Last(back);
                var v = Bars.TickVolumes.Last(back);
                var ts = Bars.OpenTimes.Last(back);
                // A bar whose high is below its low, or whose close sits outside
                // the range, is corrupt. Counting them is the only way to know
                // the export is worth testing on.
                if (h < l || c > h || c < l || o > h || o < l) { badHighLow++; continue; }
                sb.Append(ts.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(',')
                  .Append(o.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(h.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(l.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(c.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(v.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                written++;
            }

            try
            {
                System.IO.File.WriteAllText(OutputPath, sb.ToString());
            }
            catch (Exception ex)
            {
                Print("COULD NOT WRITE {0}: {1} — {2}. Pick a folder you can write to.",
                      OutputPath, ex.GetType().Name, ex.Message);
                Stop();
                return;
            }

            Print("Wrote {0} bars of {1} {2} to {3}", written, SymbolName, Bars.TimeFrame, OutputPath);
            ExportCorrelated();
            Print("Range: {0:yyyy-MM-dd HH:mm} to {1:yyyy-MM-dd HH:mm} UTC",
                  Bars.OpenTimes.Last(n - 1 - from), Bars.OpenTimes.Last(0));
            if (badHighLow > 0)
                Print("SKIPPED {0} corrupt bar(s) where the high/low/close did not agree.", badHighLow);
            var days = (Bars.OpenTimes.Last(0) - Bars.OpenTimes.Last(n - 1 - from)).TotalDays;
            if (days < 60)
                Print("!!  Only {0:F0} days of history. That is too little to certify anything — " +
                      "scroll the chart further left and run this again.", days);
            else
                Print("{0:F0} days of history. Send this file.", days);
            Stop();
        }
    }
}

// GoldNewsWatch — a NEWS-ONLY agent for gold (XAU/USD). It never places an
// order. It watches the wires, tells you when something that moves gold has
// been said, and prints a signal block you can act on by hand.
//
// ============================== WHAT IT WATCHES =========================
//   * the economic calendar (Forex Factory JSON) — scheduled events, with the
//     gold-critical ones tiered: FOMC, CPI, NFP, PCE, Powell.
//   * live newswire headlines, via Google News RSS searches. Each search is a
//     separate feed so one dead feed cannot silence the rest:
//       - gold / XAU itself
//       - Iran, Israel, Strait of Hormuz, the Middle East
//       - Trump statements and US foreign policy
//       - the Fed, inflation, rates, the dollar
//   * anything else you add to ExtraFeedUrls — ANY RSS or Atom URL.
//
// ========================== WHAT IT CANNOT DO ===========================
// READ THIS BEFORE TRUSTING IT.
//
//   1. IT CANNOT READ X/TWITTER DIRECTLY. Reading tweets in real time needs a
//      paid X API key; there is no free, reliable, terms-compliant way to poll
//      an account. What it does instead: a Trump/White House newswire search,
//      which picks up anything said on any platform once a wire service files
//      it — typically well under five minutes for anything market-moving.
//      If you buy an X API key or run any bridge that emits RSS, put its URL
//      in ExtraFeedUrls and this bot will read it like any other feed.
//
//   2. "EVERY NEWS IN THE WHOLE WORLD" IS NOT A THING ANY BOT CAN DO. What it
//      actually does is a defined, listed set of searches. Anything outside
//      them is invisible. Add feeds to widen it; do not assume full coverage.
//
//   3. THE DIRECTION CALL IS NOT BACKTESTED, and that is a real limitation,
//      not a disclaimer. Everything in GoldEdgeNews was measured on 40+ unseen
//      tapes before it shipped. This cannot be: there is no historical corpus
//      of timestamped headlines here to test against, so there is no measured
//      edge behind BUY vs SELL. The keyword model below is a documented
//      convention — escalation and easing lift gold, hawkish policy and
//      de-escalation sink it — and it is a REASONABLE PRIOR, not evidence.
//      Treat every signal as a prompt to look at the chart yourself.
//
//   4. THE LEVELS ARE ARITHMETIC, NOT A FORECAST. The stop is 1.5x ATR(14)
//      clamped to 0.4-1.4% of price, the same geometry GoldEdgeNews uses. The
//      targets are 1x / 2x / 3x that stop distance. No claim is made that the
//      targets get hit; on the trading bot's own measurements a 3R target is
//      reached under 10% of the time.
//
// ============================== HOW TO USE IT ===========================
//   Add it to any XAUUSD chart. It trades nothing, so the timeframe only
//   affects how often it re-checks (it polls on a wall-clock timer, so m5 or
//   m1 are both fine). Needs AccessRights.FullAccess for the feeds.
//
//   Alerts arrive in the cTrader log. Set AlertEmailTo (and AlertEmailFrom)
//   and each alert is also emailed.
//
//   TELEGRAM (better — instant, and it pushes to your phone):
//     1. In Telegram, message @BotFather, send /newbot, pick a name. It
//        replies with a token like 8123456789:AAF...  That token IS a
//        credential; do not paste it into a chat or a screenshot.
//     2. Send any message to your new bot (it cannot message you first).
//     3. Open in a browser:
//          https://api.telegram.org/bot<YOUR_TOKEN>/getUpdates
//        Find  "chat":{"id":123456789  — that number is your chat id.
//     4. Put the token in TelegramBotToken and the number in TelegramChatId.
//     Alerts and signal blocks then arrive as Telegram messages. The token is
//     stripped from anything this bot prints, so the log stays safe to share.
//
// Zero dependencies beyond cAlgo.API and .NET.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class GoldNewsWatch : Robot
    {
        [Parameter("Check the wires every N minutes", DefaultValue = 3, MinValue = 1, MaxValue = 120, Group = "Feeds")]
        public int PollMinutes { get; set; }

        [Parameter("Watch gold / XAU headlines", DefaultValue = true, Group = "Feeds")]
        public bool FeedGold { get; set; }

        [Parameter("Watch Iran / Israel / Middle East", DefaultValue = true, Group = "Feeds")]
        public bool FeedMideast { get; set; }

        [Parameter("Watch Trump / US foreign policy", DefaultValue = true, Group = "Feeds")]
        public bool FeedTrump { get; set; }

        [Parameter("Watch the Fed / inflation / rates / dollar", DefaultValue = true, Group = "Feeds")]
        public bool FeedFed { get; set; }

        // NAMED WIRE FEEDS. These are the outlets' own RSS, so they publish
        // within a minute or two of filing — faster than a Google News search,
        // which batches. They are broad world feeds, most of which has nothing
        // to do with gold; the relevance gate in Score() is what stops that
        // becoming noise, and there is a test for exactly that.
        [Parameter("Al Jazeera", DefaultValue = true, Group = "Wires")]
        public bool WireAlJazeera { get; set; }

        [Parameter("Al Arabiya", DefaultValue = true, Group = "Wires")]
        public bool WireAlArabiya { get; set; }

        [Parameter("CNN world", DefaultValue = true, Group = "Wires")]
        public bool WireCnn { get; set; }

        [Parameter("CNN money / markets", DefaultValue = true, Group = "Wires")]
        public bool WireCnnMoney { get; set; }

        [Parameter("BBC world", DefaultValue = true, Group = "Wires")]
        public bool WireBbc { get; set; }

        [Parameter("CNBC markets", DefaultValue = true, Group = "Wires")]
        public bool WireCnbc { get; set; }

        // Fastest legitimate route to what Trump actually said. A dedicated
        // hourly news search returns wire copy within minutes of a post; the
        // post itself needs a paid X API key, which no free feed replaces.
        [Parameter("Trump / statements fast lane", DefaultValue = true, Group = "Wires")]
        public bool WireTrumpFast { get; set; }

        [Parameter("Extra RSS/Atom feed URLs (comma separated)", DefaultValue = "", Group = "Feeds")]
        public string ExtraFeedUrls { get; set; }

        [Parameter("Economic calendar URL", DefaultValue = "https://nfs.faireconomy.media/ff_calendar_thisweek.json", Group = "Feeds")]
        public string CalendarUrl { get; set; }

        [Parameter("Warn me N minutes before a scheduled event", DefaultValue = 15, MinValue = 1, MaxValue = 240, Group = "Feeds")]
        public int CalendarWarnMinutes { get; set; }

        // How strong a headline has to score before it interrupts you. The
        // lexicon weights single words 1-3, so 3 is roughly "one strong word or
        // two weak ones". Raise it if you get too many alerts.
        [Parameter("Alert threshold (higher = fewer, stronger alerts)", DefaultValue = 3.0, MinValue = 1.0, MaxValue = 20.0, Group = "Alerts")]
        public double AlertThreshold { get; set; }

        [Parameter("Also print a BUY/SELL signal block", DefaultValue = true, Group = "Alerts")]
        public bool EmitSignals { get; set; }

        // A signal is only worth printing when the wording actually leans one
        // way. Below this the headline is reported as news with NO direction.
        [Parameter("Signal needs a directional score of at least", DefaultValue = 4.0, MinValue = 1.0, MaxValue = 30.0, Group = "Alerts")]
        public double SignalThreshold { get; set; }

        [Parameter("Minutes to suppress repeat signals", DefaultValue = 20, MinValue = 0, MaxValue = 240, Group = "Alerts")]
        public int SignalCooldownMinutes { get; set; }

        // TELEGRAM. Much better than email for this: it is instant, it pushes to
        // your phone, and you can read a signal block without opening a mail
        // app. Setup is in the header comment at the top of this file.
        //
        // The token is a CREDENTIAL. It is typed into cTrader's parameter box,
        // never into a file in this repo, and it is stripped from every log
        // line this bot writes -- see Redact().
        [Parameter("Telegram bot token (blank = off)", DefaultValue = "", Group = "Telegram")]
        public string TelegramBotToken { get; set; }

        [Parameter("Telegram chat id", DefaultValue = "", Group = "Telegram")]
        public string TelegramChatId { get; set; }

        [Parameter("Email alerts to (blank = log only)", DefaultValue = "", Group = "Alerts")]
        public string AlertEmailTo { get; set; }

        [Parameter("Email alerts from", DefaultValue = "", Group = "Alerts")]
        public string AlertEmailFrom { get; set; }

        [Parameter("Stop: ATR multiple", DefaultValue = 1.5, MinValue = 0.2, MaxValue = 6.0, Group = "Levels")]
        public double StopAtrMult { get; set; }

        [Parameter("Stop: MIN % of price", DefaultValue = 0.4, MinValue = 0.05, MaxValue = 5.0, Group = "Levels")]
        public double MinStopPercent { get; set; }

        [Parameter("Stop: MAX % of price", DefaultValue = 1.4, MinValue = 0.1, MaxValue = 10.0, Group = "Levels")]
        public double MaxStopPercent { get; set; }

        [Parameter("TP1 (x the stop distance)", DefaultValue = 1.0, MinValue = 0.2, MaxValue = 10.0, Group = "Levels")]
        public double Tp1R { get; set; }

        [Parameter("TP2 (x the stop distance)", DefaultValue = 2.0, MinValue = 0.2, MaxValue = 10.0, Group = "Levels")]
        public double Tp2R { get; set; }

        [Parameter("TP3 (x the stop distance)", DefaultValue = 3.0, MinValue = 0.2, MaxValue = 20.0, Group = "Levels")]
        public double Tp3R { get; set; }

        [Parameter("Log a heartbeat even when nothing happens", DefaultValue = true, Group = "Diagnostics")]
        public bool Heartbeat { get; set; }

        private AverageTrueRange _atr;
        private DateTime _lastPoll = DateTime.MinValue;
        private DateTime _lastSignal = DateTime.MinValue;
        private DateTime _lastHeartbeat = DateTime.MinValue;
        private bool _pollInFlight;
        private readonly object _lock = new object();
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly Queue<string> _seenOrder = new Queue<string>();
        private readonly List<Scored> _pending = new List<Scored>();
        private readonly HashSet<string> _warnedEvents = new HashSet<string>();
        private List<CalEvent> _calendar = new List<CalEvent>();
        private int _alertsToday;
        private DateTime _statsDay = DateTime.MinValue;
        private string _feedStatus = "not polled yet";

        public class Scored
        {
            public string Title;
            public string Source;
            public double Impact;      // how much it matters, always >= 0
            public double Direction;   // + lifts gold, - sinks gold
            public List<string> Why = new List<string>();
        }

        private class CalEvent
        {
            public DateTime UtcTime;
            public string Title;
            public string Currency;
            public int Tier;
        }

        // ---- the lexicon ---------------------------------------------------
        // Sign convention: POSITIVE lifts gold, NEGATIVE sinks it. Weight is
        // how strongly. Gold rises on fear, war, easing and a weak dollar; it
        // falls on calm, deals, tightening and a strong dollar.
        //
        // These are conventional macro relationships, not fitted values. No
        // claim is made that any particular weight is optimal — see caveat 3
        // at the top of the file.
        private static readonly KeyValuePair<string, double>[] Lexicon =
        {
            // --- escalation: gold up
            new KeyValuePair<string, double>("NUCLEAR", 3.0),
            new KeyValuePair<string, double>("AIRSTRIKE", 3.0),
            new KeyValuePair<string, double>("AIR STRIKE", 3.0),
            new KeyValuePair<string, double>("MISSILE", 2.5),
            new KeyValuePair<string, double>("STRIKE ON", 2.5),
            new KeyValuePair<string, double>("INVASION", 3.0),
            new KeyValuePair<string, double>("INVADE", 3.0),
            new KeyValuePair<string, double>("WAR", 2.5),
            new KeyValuePair<string, double>("ATTACK", 2.5),
            new KeyValuePair<string, double>("BOMB", 2.5),
            new KeyValuePair<string, double>("RETALIAT", 2.5),
            new KeyValuePair<string, double>("ESCALAT", 2.0),
            new KeyValuePair<string, double>("SANCTION", 1.5),
            new KeyValuePair<string, double>("ENRICHMENT", 2.0),
            new KeyValuePair<string, double>("HORMUZ", 3.0),
            new KeyValuePair<string, double>("TANKER", 1.5),
            new KeyValuePair<string, double>("DRONE", 1.5),
            new KeyValuePair<string, double>("CASUALT", 1.5),
            new KeyValuePair<string, double>("KILLED", 1.5),
            new KeyValuePair<string, double>("EVACUAT", 1.5),
            new KeyValuePair<string, double>("MOBILIZ", 2.0),
            new KeyValuePair<string, double>("ULTIMATUM", 2.0),
            new KeyValuePair<string, double>("THREATEN", 1.5),
            new KeyValuePair<string, double>("EMERGENCY", 1.5),
            new KeyValuePair<string, double>("CRISIS", 1.5),
            new KeyValuePair<string, double>("DEFAULT", 2.0),
            new KeyValuePair<string, double>("SHUTDOWN", 1.0),
            // --- easing money: gold up
            new KeyValuePair<string, double>("RATE CUT", 2.5),
            new KeyValuePair<string, double>("CUTS RATES", 2.5),
            new KeyValuePair<string, double>("DOVISH", 2.0),
            new KeyValuePair<string, double>("STIMULUS", 1.5),
            new KeyValuePair<string, double>("QUANTITATIVE EASING", 2.5),
            new KeyValuePair<string, double>("WEAK DOLLAR", 2.0),
            new KeyValuePair<string, double>("DOLLAR FALLS", 2.0),
            new KeyValuePair<string, double>("DOLLAR SLIDES", 2.0),
            new KeyValuePair<string, double>("RECESSION", 1.5),
            new KeyValuePair<string, double>("SAFE HAVEN", 2.0),
            new KeyValuePair<string, double>("SAFE-HAVEN", 2.0),
            new KeyValuePair<string, double>("GOLD RECORD", 2.0),
            new KeyValuePair<string, double>("RECORD HIGH", 1.5),
            // --- de-escalation: gold down
            new KeyValuePair<string, double>("CEASEFIRE", -3.0),
            new KeyValuePair<string, double>("CEASE-FIRE", -3.0),
            new KeyValuePair<string, double>("TRUCE", -2.5),
            new KeyValuePair<string, double>("PEACE DEAL", -3.0),
            new KeyValuePair<string, double>("PEACE TALKS", -2.0),
            new KeyValuePair<string, double>("DE-ESCALAT", -2.5),
            new KeyValuePair<string, double>("DEESCALAT", -2.5),
            new KeyValuePair<string, double>("AGREEMENT REACHED", -2.0),
            new KeyValuePair<string, double>("DIPLOMA", -1.5),
            new KeyValuePair<string, double>("NEGOTIAT", -1.0),
            new KeyValuePair<string, double>("SANCTIONS LIFTED", -2.5),
            new KeyValuePair<string, double>("WITHDRAW TROOPS", -2.0),
            // --- tightening money / strong dollar: gold down
            new KeyValuePair<string, double>("RATE HIKE", -2.5),
            new KeyValuePair<string, double>("RAISES RATES", -2.5),
            new KeyValuePair<string, double>("HAWKISH", -2.0),
            new KeyValuePair<string, double>("HIGHER FOR LONGER", -2.0),
            new KeyValuePair<string, double>("STRONG DOLLAR", -2.0),
            new KeyValuePair<string, double>("DOLLAR RISES", -2.0),
            new KeyValuePair<string, double>("DOLLAR SURGES", -2.0),
            new KeyValuePair<string, double>("YIELDS RISE", -1.5),
            new KeyValuePair<string, double>("HOT INFLATION", -1.5),
            new KeyValuePair<string, double>("STRONG JOBS", -1.5),
            new KeyValuePair<string, double>("BEATS EXPECTATIONS", -1.0),
            // --- added at the owner's direction: "anything that can make gold
            // dump or pump". These are the other well-known gold drivers.
            new KeyValuePair<string, double>("ASSASSINAT", 3.0),
            new KeyValuePair<string, double>("COUP", 2.5),
            new KeyValuePair<string, double>("TERROR", 2.0),
            new KeyValuePair<string, double>("MARTIAL LAW", 2.5),
            new KeyValuePair<string, double>("DEBT CEILING", 2.0),
            new KeyValuePair<string, double>("CREDIT RATING", 1.5),
            new KeyValuePair<string, double>("DOWNGRADE", 1.5),
            new KeyValuePair<string, double>("BANK RUN", 2.5),
            new KeyValuePair<string, double>("BANK COLLAPSE", 2.5),
            new KeyValuePair<string, double>("BAILOUT", 1.5),
            new KeyValuePair<string, double>("TARIFF", 2.0),
            new KeyValuePair<string, double>("TRADE WAR", 2.5),
            new KeyValuePair<string, double>("CENTRAL BANKS BUY", 2.5),
            new KeyValuePair<string, double>("GOLD RESERVES", 1.5),
            new KeyValuePair<string, double>("DE-DOLLAR", 2.0),
            new KeyValuePair<string, double>("BRICS", 1.0),
            new KeyValuePair<string, double>("BLOCKADE", 2.5),
            new KeyValuePair<string, double>("OIL SPIKE", 1.5),
            new KeyValuePair<string, double>("OIL SURGES", 1.5),
            new KeyValuePair<string, double>("PROXY", 1.0),
            new KeyValuePair<string, double>("UKRAINE", 1.0),
            new KeyValuePair<string, double>("TAIWAN", 1.5),
            // and the other side of them
            new KeyValuePair<string, double>("TARIFFS LIFTED", -2.0),
            new KeyValuePair<string, double>("TRADE DEAL", -1.5),
            new KeyValuePair<string, double>("RISK APPETITE", -1.5),
            new KeyValuePair<string, double>("STOCKS RALLY", -1.0),
            new KeyValuePair<string, double>("PROFIT-TAKING", -1.0),
            new KeyValuePair<string, double>("ETF OUTFLOW", -2.0),
            new KeyValuePair<string, double>("ETF INFLOW", 2.0),
        };

        // Words that only matter because they say the story is ABOUT gold or
        // about a body that moves it. They carry impact but no direction.
        // Matched against the headline PADDED with spaces, so a token that
        // starts or ends with a space is a whole word. " FED " is padded on
        // purpose: bare "FED" also matches Federer, federation and "fed up".
        private static readonly string[] Relevance =
        {
            "GOLD", "XAU", "BULLION", "IRAN", "ISRAEL", "TEHRAN", "TRUMP",
            "WHITE HOUSE", "PENTAGON", "FEDERAL RESERVE", "POWELL", "FOMC",
            "CPI", "INFLATION", "NONFARM", "NON-FARM", "PAYROLL", "TREASURY",
            "MIDDLE EAST", "OPEC", "CENTRAL BANK", "ECB", "GEOPOLIT",
            // Added after a test caught it: "Fed signals higher for longer as
            // dollar surges" scored ZERO and would never have alerted, because
            // the everyday shorthand for the central bank was not on this list
            // and neither was the dollar.
            " FED ", " FED'S ", "DOLLAR", "INTEREST RATE", "MONETARY",
            "NUCLEAR", "SANCTION", "HORMUZ",
            // widened with the lexicon above, so those terms can actually fire
            "RUSSIA", "UKRAINE", "CHINA", "TAIWAN", "HAMAS", "HEZBOLLAH",
            "HOUTHI", "NETANYAHU", "KHAMENEI", "PUTIN", "TARIFF", "DEBT CEILING",
            "CREDIT RATING", "BRICS", "OIL", "YIELD", "BULLION", "PRECIOUS METAL",
            "CHINESE", "GOLD COUNCIL", "BULLION BANK"
        };

        private static readonly string[] FalseFriends =
        {
            " FED UP ", " WELL FED ", " FED INTO ", " FED THE "
        };

        protected override void OnStart()
        {
            _atr = Indicators.AverageTrueRange(14, MovingAverageType.Exponential);

            Print("GoldNewsWatch started | {0} {1} | polls every {2} min | THIS BOT NEVER TRADES",
                  SymbolName, Bars.TimeFrame, PollMinutes);
            Print("Feeds: {0}", DescribeFeeds());
            Print("Alerts at score >= {0:F1} | signals at directional score >= {1:F1} | email {2}",
                  AlertThreshold, SignalThreshold,
                  string.IsNullOrEmpty(AlertEmailTo) ? "OFF (log only)" : AlertEmailTo);
            Print("Levels if a signal fires: stop {0:F1}x ATR clamped {1}-{2}% of price, " +
                  "TP1/2/3 at {3:F1}/{4:F1}/{5:F1}x the stop.",
                  StopAtrMult, MinStopPercent, MaxStopPercent, Tp1R, Tp2R, Tp3R);
            Print("!!  HONEST LIMITS — read these once:");
            Print("!!  1) It cannot read X/Twitter directly; that needs a paid API key. It");
            Print("!!     reads the newswires instead, which carry anything market-moving");
            Print("!!     within minutes. Add any RSS bridge you have to ExtraFeedUrls.");
            Print("!!  2) Coverage is the listed searches only, not 'all world news'.");
            Print("!!  3) The BUY/SELL call is NOT backtested. Every number in the trading");
            Print("!!     bot was measured on 40+ unseen tapes; this could not be, because");
            Print("!!     there is no historical headline corpus to test against. It is a");
            Print("!!     reasonable prior, not evidence. Look at the chart before acting.");
            Print("!!  4) The levels are arithmetic off ATR, not a forecast.");

            Poll();
        }

        protected override void OnTick()
        {
            var now = Server.TimeInUtc;
            RollDay(now);

            if ((now - _lastPoll).TotalMinutes >= PollMinutes)
            {
                _lastPoll = now;
                Poll();
            }

            DrainPending(now);
            WarnUpcoming(now);

            if (Heartbeat && (now - _lastHeartbeat).TotalMinutes >= 60)
            {
                _lastHeartbeat = now;
                Print("watching: {0} | {1} alerts today | {2} headlines seen | gold {3:F2}",
                      FeedStatus(), _alertsToday, _seen.Count, Symbol.Bid);
            }
        }

        private string DescribeFeeds()
        {
            var on = new List<string>();
            if (FeedGold) on.Add("gold/XAU");
            if (FeedMideast) on.Add("Iran/Israel/Mideast");
            if (FeedTrump) on.Add("Trump/US policy");
            if (FeedFed) on.Add("Fed/inflation/dollar");
            var extra = SplitCsv(ExtraFeedUrls).Count;
            if (extra > 0) on.Add(extra + " custom feed(s)");
            on.Add("economic calendar");
            return string.Join(", ", on);
        }

        private string FeedStatus()
        {
            lock (_lock) return _feedStatus;
        }

        private void RollDay(DateTime now)
        {
            if (_statsDay == now.Date) return;
            if (_statsDay != DateTime.MinValue)
                Print("NEWS DAY SUMMARY {0:yyyy-MM-dd}: {1} alerts raised.", _statsDay, _alertsToday);
            _statsDay = now.Date;
            _alertsToday = 0;
            _warnedEvents.Clear();
        }

        private static List<string> SplitCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return new List<string>();
            return s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }

        // Google News RSS search. Free, no key, and each query is its own feed
        // so one failing search cannot silence the others.
        private static string NewsQuery(string q)
        {
            return "https://news.google.com/rss/search?q=" + Uri.EscapeDataString(q + " when:1d") +
                   "&hl=en-US&gl=US&ceid=US:en";
        }

        // Same search, one-hour window: far fewer, far fresher items. Used for
        // the things where being late is the whole problem.
        private static string FastQuery(string q)
        {
            return "https://news.google.com/rss/search?q=" + Uri.EscapeDataString(q + " when:1h") +
                   "&hl=en-US&gl=US&ceid=US:en";
        }

        private List<string> FeedList()
        {
            var urls = new List<string>();
            if (FeedGold) urls.Add(NewsQuery("gold price OR XAUUSD OR bullion"));
            if (FeedMideast) urls.Add(NewsQuery("Iran OR Israel OR \"Middle East\" OR Hormuz"));
            if (FeedTrump) urls.Add(NewsQuery("Trump OR \"White House\" statement OR sanctions"));
            if (FeedFed) urls.Add(NewsQuery("Federal Reserve OR inflation OR interest rates OR dollar"));
            // the wires themselves — fastest, and each is independent
            if (WireAlJazeera) urls.Add("https://www.aljazeera.com/xml/rss/all.xml");
            if (WireAlArabiya) urls.Add("https://english.alarabiya.net/.mrss/en.xml");
            if (WireCnn) urls.Add("http://rss.cnn.com/rss/edition_world.rss");
            if (WireCnnMoney) urls.Add("http://rss.cnn.com/rss/money_news_international.rss");
            if (WireBbc) urls.Add("https://feeds.bbci.co.uk/news/world/rss.xml");
            if (WireCnbc) urls.Add("https://search.cnbc.com/rs/search/combinedcms/view.xml?partnerId=wrss01&id=20910258");
            if (WireTrumpFast) urls.Add(FastQuery("Trump OR \"White House\" OR Netanyahu OR Khamenei"));
            urls.AddRange(SplitCsv(ExtraFeedUrls));
            return urls;
        }

        private void Poll()
        {
            lock (_lock)
            {
                if (_pollInFlight) return;
                _pollInFlight = true;
            }
            var urls = FeedList();
            var calUrl = CalendarUrl;

            Task.Run(() =>
            {
                var found = new List<Scored>();
                var ok = 0;
                var failed = 0;
                foreach (var url in urls)
                {
                    try
                    {
                        var xml = Download(url);
                        foreach (var title in ParseFeedTitles(xml))
                        {
                            var s = Score(title);
                            s.Source = ShortSource(url);
                            found.Add(s);
                        }
                        ok++;
                    }
                    catch (Exception) { failed++; }
                }

                List<CalEvent> cal = null;
                try { cal = ParseCalendar(Download(calUrl)); }
                catch (Exception) { }

                lock (_lock)
                {
                    if (cal != null && cal.Count > 0) _calendar = cal;
                    foreach (var s in found)
                    {
                        var key = Normalise(s.Title);
                        if (_seen.Contains(key)) continue;
                        _seen.Add(key);
                        _seenOrder.Enqueue(key);
                        while (_seenOrder.Count > 800) _seen.Remove(_seenOrder.Dequeue());
                        if (s.Impact >= AlertThreshold) _pending.Add(s);
                    }
                    _feedStatus = string.Format("{0}/{1} feeds ok{2}, calendar {3} events",
                                                ok, urls.Count,
                                                failed > 0 ? " (" + failed + " failed)" : "",
                                                _calendar.Count);
                    _pollInFlight = false;
                }
            });
        }

        private static string Download(string url)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; GoldNewsWatch/1.0)");
                return wc.DownloadString(url);
            }
        }

        private static string ShortSource(string url)
        {
            if (url.Contains("aljazeera")) return "Al Jazeera";
            if (url.Contains("alarabiya")) return "Al Arabiya";
            if (url.Contains("money_news")) return "CNN money";
            if (url.Contains("cnn.com")) return "CNN";
            if (url.Contains("bbci.co.uk")) return "BBC";
            if (url.Contains("cnbc.com")) return "CNBC";
            if (url.Contains("news.google.com"))
            {
                if (url.Contains("Khamenei")) return "Trump/statements";
                if (url.Contains("bullion")) return "gold wire";
                if (url.Contains("Hormuz")) return "mideast wire";
                if (url.Contains("Trump")) return "US policy wire";
                if (url.Contains("Federal")) return "macro wire";
                return "news";
            }
            try { return new Uri(url).Host; } catch (Exception) { return "feed"; }
        }

        // ---- feed parsing --------------------------------------------------
        // Deliberately a plain string scan rather than an XML parser: RSS in
        // the wild is frequently malformed, and a parser that throws loses the
        // whole feed. This takes what it can read and ignores the rest.
        public static List<string> ParseFeedTitles(string xml)
        {
            var titles = new List<string>();
            if (string.IsNullOrEmpty(xml)) return titles;
            var i = 0;
            var first = true;
            while (true)
            {
                var a = xml.IndexOf("<title", i, StringComparison.OrdinalIgnoreCase);
                if (a < 0) break;
                var gt = xml.IndexOf('>', a);
                if (gt < 0) break;
                var b = xml.IndexOf("</title>", gt, StringComparison.OrdinalIgnoreCase);
                if (b < 0) break;
                var raw = xml.Substring(gt + 1, b - gt - 1);
                i = b + 8;
                // the first <title> is the channel's own name, not a story
                if (first) { first = false; continue; }
                var t = CleanXml(raw);
                if (t.Length > 0) titles.Add(t);
            }
            return titles;
        }

        public static string CleanXml(string s)
        {
            if (s == null) return "";
            s = s.Replace("<![CDATA[", "").Replace("]]>", "");
            // strip any embedded markup
            var sb = new System.Text.StringBuilder();
            var depth = 0;
            foreach (var ch in s)
            {
                if (ch == '<') { depth++; continue; }
                if (ch == '>') { if (depth > 0) depth--; continue; }
                if (depth == 0) sb.Append(ch);
            }
            var t = sb.ToString();
            t = t.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
                 .Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&apos;", "'")
                 .Replace("&nbsp;", " ");
            return t.Trim();
        }

        // Google News appends " - Publisher" to every headline, so the SAME
        // story filed by Reuters, Al-Monitor and Military Times arrives as three
        // different strings and the duplicate check lets all three through. It
        // did, into a live channel. Strip the suffix before comparing.
        public static string NormalisePublic(string title) { return Normalise(title); }

        private static string Normalise(string title)
        {
            var t = (title ?? "").Trim();
            var cut = t.LastIndexOf(" - ", StringComparison.Ordinal);
            if (cut > 20)
                t = t.Substring(0, cut);
            var sb = new System.Text.StringBuilder();
            foreach (var ch in t.ToUpperInvariant())
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }

        // ---- scoring -------------------------------------------------------
        public static Scored Score(string title)
        {
            var s = new Scored { Title = title };
            if (string.IsNullOrEmpty(title)) return s;
            var upper = title.ToUpperInvariant();
            var padded = " " + upper + " ";
            // Phrases that contain a relevance token but mean something else.
            // "Voters are fed up with the war on drugs" matched " FED " as a
            // whole word and then scored bullish on "war". Neutralise the
            // phrase before matching rather than dropping the token, which
            // would lose every real "Fed holds rates" headline.
            foreach (var ff in FalseFriends)
                padded = padded.Replace(ff, " ");

            var relevant = 0;
            foreach (var r in Relevance)
                if (padded.Contains(r)) relevant++;

            double dir = 0.0, mag = 0.0;
            foreach (var kv in Lexicon)
            {
                if (!upper.Contains(kv.Key)) continue;
                dir += kv.Value;
                mag += Math.Abs(kv.Value);
                s.Why.Add(kv.Key.ToLowerInvariant() + (kv.Value > 0 ? " (+)" : " (-)"));
            }

            // A loaded word about something unrelated to gold is noise. Impact
            // is the strength of the wording GATED by whether the story is even
            // about gold, the Fed, or a conflict that moves it.
            if (relevant == 0)
            {
                s.Impact = 0.0;
                s.Direction = 0.0;
                return s;
            }
            var relevanceBoost = 1.0 + 0.25 * Math.Min(3, relevant - 1);
            s.Impact = mag * relevanceBoost;
            s.Direction = dir * relevanceBoost;
            return s;
        }

        // ---- alerting ------------------------------------------------------
        private void DrainPending(DateTime now)
        {
            List<Scored> batch;
            lock (_lock)
            {
                if (_pending.Count == 0) return;
                batch = new List<Scored>(_pending);
                _pending.Clear();
            }
            // strongest first, so if several land at once the big one leads
            batch.Sort((x, y) => y.Impact.CompareTo(x.Impact));
            foreach (var s in batch)
                Raise(s, now);
        }

        private void Raise(Scored s, DateTime now)
        {
            _alertsToday++;
            var lines = new List<string>();
            lines.Add(string.Format("NEWS [{0}] score {1:F1} — {2}", s.Source, s.Impact, s.Title));
            if (s.Why.Count > 0)
                lines.Add("      triggered on: " + string.Join(", ", s.Why));

            var directional = Math.Abs(s.Direction) >= SignalThreshold;
            var cooling = (now - _lastSignal).TotalMinutes < SignalCooldownMinutes;

            if (EmitSignals && directional && !cooling)
            {
                _lastSignal = now;
                lines.AddRange(SignalBlock(s.Direction > 0));
            }
            else if (EmitSignals && directional && cooling)
            {
                lines.Add(string.Format("      (signal suppressed — one was sent {0:F0} min ago)",
                                        (now - _lastSignal).TotalMinutes));
            }
            else if (EmitSignals)
            {
                lines.Add(string.Format("      no signal: wording leans {0:F1}, needs {1:F1} either way.",
                                        s.Direction, SignalThreshold));
            }

            foreach (var l in lines) Print(l);
            Email(s.Title, string.Join("\n", lines));
        }

        private List<string> SignalBlock(bool bullish)
        {
            var side = bullish ? "BUY" : "SELL";
            var dir = bullish ? 1 : -1;
            var price = bullish ? Symbol.Ask : Symbol.Bid;
            var stopDist = StopDistance(price);
            var lines = new List<string>();
            lines.Add("      ------------------------------");
            lines.Add(string.Format("      entry      - {0} {1:F2}", side, price));
            lines.Add(string.Format("      tp 1       - {0:F2}   (+{1:F2})", price + dir * stopDist * Tp1R, stopDist * Tp1R));
            lines.Add(string.Format("      tp 2       - {0:F2}   (+{1:F2})", price + dir * stopDist * Tp2R, stopDist * Tp2R));
            lines.Add(string.Format("      tp 3       - {0:F2}   (+{1:F2})", price + dir * stopDist * Tp3R, stopDist * Tp3R));
            lines.Add(string.Format("      stop loss  - {0:F2}   (-{1:F2})", price - dir * stopDist, stopDist));
            lines.Add("      ------------------------------");
            lines.Add("      NOT BACKTESTED. Direction is a keyword prior, not measured edge.");
            return lines;
        }

        private double StopDistance(double price)
        {
            var lo = price * (MinStopPercent / 100.0);
            var hi = price * (MaxStopPercent / 100.0);
            var atr = _atr.Result.Last(0);
            var d = atr > 0 ? StopAtrMult * atr : lo;
            return Math.Max(lo, Math.Min(hi, d));
        }

        private void Email(string subject, string body)
        {
            if (!string.IsNullOrEmpty(AlertEmailTo) && !string.IsNullOrEmpty(AlertEmailFrom))
            {
                try { Notifications.SendEmail(AlertEmailFrom, AlertEmailTo, "GOLD: " + subject, body); }
                catch (Exception ex) { Print("email failed: {0}", ex.Message); }
            }
            Telegram(body);
        }

        // Telegram's sendMessage endpoint. Built as a static so it can be
        // tested without a network.
        public static string TelegramUrl(string token, string chatId, string text)
        {
            // Telegram rejects anything over 4096 characters outright.
            if (text != null && text.Length > 3800)
                text = text.Substring(0, 3800) + "\n...(truncated)";
            return "https://api.telegram.org/bot" + Uri.EscapeDataString(token ?? "") +
                   "/sendMessage?chat_id=" + Uri.EscapeDataString(chatId ?? "") +
                   "&disable_web_page_preview=true" +
                   "&text=" + Uri.EscapeDataString(text ?? "");
        }

        // A bot token is a credential: anyone holding it can post as you. It
        // must never reach the cTrader log, which gets pasted into chats and
        // screenshots.
        public static string Redact(string s, string token)
        {
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(token)) return s;
            return s.Replace(token, "<token>")
                    .Replace(Uri.EscapeDataString(token), "<token>");
        }

        // The ONLY way this bot reports a Telegram failure. Redaction lives
        // inside it rather than at the call site, so there is one place to get
        // right and it is directly testable without a network.
        public static string TelegramErrorLine(string exType, string exMessage, string token)
        {
            return "telegram failed: " + exType + " — " + Redact(exMessage, token);
        }

        private void Telegram(string text)
        {
            if (string.IsNullOrEmpty(TelegramBotToken) || string.IsNullOrEmpty(TelegramChatId))
                return;
            var url = TelegramUrl(TelegramBotToken, TelegramChatId, text);
            var token = TelegramBotToken;
            // off the trading thread; a slow phone network must never stall the bot
            Task.Run(() =>
            {
                try { Download(url); }
                catch (Exception ex)
                {
                    // One function builds this line and it always redacts, so
                    // the failure path cannot log a token by accident.
                    var line = TelegramErrorLine(ex.GetType().Name, ex.Message, token);
                    BeginInvokeOnMainThread(() => Print(line));
                }
            });
        }

        // ---- scheduled events ----------------------------------------------
        private void WarnUpcoming(DateTime now)
        {
            List<CalEvent> snapshot;
            lock (_lock) snapshot = _calendar;
            if (snapshot == null) return;
            foreach (var e in snapshot)
            {
                var mins = (e.UtcTime - now).TotalMinutes;
                if (mins < 0 || mins > CalendarWarnMinutes) continue;
                var key = e.UtcTime.Ticks + e.Title;
                if (_warnedEvents.Contains(key)) continue;
                _warnedEvents.Add(key);
                var msg = string.Format(
                    "SCHEDULED (T{0}) in {1:F0} min — {2} {3}. Gold usually moves on this; " +
                    "spreads widen and stops get taken either way before the direction settles.",
                    e.Tier, mins, e.Currency, e.Title);
                Print(msg);
                Email(e.Title + " in " + Math.Round(mins) + " min", msg);
            }
        }

        public static List<CalEventPublic> ParseCalendarPublic(string json)
        {
            return ParseCalendar(json)
                .Select(e => new CalEventPublic { UtcTime = e.UtcTime, Title = e.Title, Currency = e.Currency, Tier = e.Tier })
                .ToList();
        }

        public class CalEventPublic
        {
            public DateTime UtcTime;
            public string Title;
            public string Currency;
            public int Tier;
        }

        private static List<CalEvent> ParseCalendar(string json)
        {
            var list = new List<CalEvent>();
            if (string.IsNullOrEmpty(json)) return list;
            var idx = 0;
            while (true)
            {
                var start = json.IndexOf('{', idx);
                if (start < 0) break;
                var end = json.IndexOf('}', start);
                if (end < 0) break;
                var obj = json.Substring(start, end - start + 1);
                idx = end + 1;

                var cur = Field(obj, "country") ?? Field(obj, "currency");
                if (cur == null) continue;
                cur = cur.Trim().ToUpperInvariant();

                var dateStr = Field(obj, "date");
                if (dateStr == null) continue;
                DateTimeOffset dto;
                if (!DateTimeOffset.TryParse(dateStr, null,
                        System.Globalization.DateTimeStyles.AssumeUniversal, out dto))
                    continue;

                var title = Field(obj, "title") ?? "event";
                var impact = Field(obj, "impact") ?? "";
                var upper = title.ToUpperInvariant();
                var high = impact.IndexOf("High", StringComparison.OrdinalIgnoreCase) >= 0;
                var critical = upper.Contains("FOMC") || upper.Contains("CPI") ||
                               upper.Contains("NON-FARM") || upper.Contains("NONFARM") ||
                               upper.Contains("POWELL") || upper.Contains("PCE") ||
                               upper.Contains("RATE DECISION") || upper.Contains("RATE STATEMENT");
                int tier;
                if (critical && cur == "USD") tier = 1;
                else if (critical || high) tier = 2;
                else continue;

                list.Add(new CalEvent { UtcTime = dto.UtcDateTime, Title = title, Currency = cur, Tier = tier });
            }
            return list;
        }

        private static string Field(string obj, string key)
        {
            var needle = "\"" + key + "\"";
            var k = obj.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (k < 0) return null;
            var colon = obj.IndexOf(':', k + needle.Length);
            if (colon < 0) return null;
            var i = colon + 1;
            while (i < obj.Length && char.IsWhiteSpace(obj[i])) i++;
            if (i >= obj.Length) return null;
            if (obj[i] != '"')
            {
                var stop = i;
                while (stop < obj.Length && obj[stop] != ',' && obj[stop] != '}') stop++;
                return obj.Substring(i, stop - i).Trim();
            }
            i++;
            var sb = new System.Text.StringBuilder();
            while (i < obj.Length && obj[i] != '"')
            {
                if (obj[i] == '\\' && i + 1 < obj.Length) i++;
                sb.Append(obj[i]);
                i++;
            }
            return sb.ToString();
        }

        protected override void OnStop()
        {
            Print("GoldNewsWatch stopped. {0} alerts raised today.", _alertsToday);
        }
    }
}

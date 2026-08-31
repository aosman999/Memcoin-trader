// Behaviour tests for GoldNewsWatch. A compile proves nothing about whether it
// reads a real feed or calls the right direction, so this drives the actual
// parsing and scoring code with fixtures taken from the shapes these feeds
// really produce -- CDATA, entities, embedded markup, malformed tails.
using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.Robots;

public static class NewsTest
{
    static int _fail;
    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
        if (!ok) _fail++;
    }

    const string Rss = @"<?xml version=""1.0""?><rss version=""2.0""><channel>
<title>Gold - Google News</title><link>https://news.google.com</link>
<item><title>Israel strikes Iranian nuclear site, Tehran vows retaliation</title>
<pubDate>Fri, 28 Aug 2026 09:12:00 GMT</pubDate></item>
<item><title><![CDATA[Gold hits record high as safe-haven demand surges]]></title></item>
<item><title>Iran and Israel agree ceasefire; gold slides</title></item>
<item><title>Fed signals higher for longer as dollar surges</title></item>
<item><title>Manchester United sign new striker in record deal</title></item>
<item><title>Powell &amp; the FOMC face a &quot;rate cut&quot; decision</title></item>
<item><title>Analysts <b>expect</b> gold volatility</title></item>
</channel></rss>";

    public static int Main()
    {
        Console.WriteLine("GOLDNEWSWATCH — parsing and scoring against real feed shapes\n");

        var titles = GoldNewsWatch.ParseFeedTitles(Rss);
        Check(titles.Count == 7, "reads every item, skipping the channel title (" + titles.Count + " of 7)");
        Check(!titles.Any(t => t.Contains("Google News")), "does not mistake the channel name for a story");
        Check(titles.Any(t => t.Contains("safe-haven demand")), "unwraps CDATA");
        Check(titles.Any(t => t.Contains("Powell & the FOMC") && t.Contains("\"rate cut\"")),
              "decodes &amp; and &quot; entities");
        Check(titles.Any(t => t == "Analysts expect gold volatility"), "strips embedded markup");

        // a feed that is cut off mid-document must not lose what came before it
        var truncated = Rss.Substring(0, Rss.IndexOf("Fed signals"));
        var partial = GoldNewsWatch.ParseFeedTitles(truncated);
        Check(partial.Count >= 3, "a truncated feed still yields the items it did receive (" + partial.Count + ")");
        Check(GoldNewsWatch.ParseFeedTitles("").Count == 0, "empty feed yields nothing, does not throw");
        Check(GoldNewsWatch.ParseFeedTitles("<rss><channel><title>x</title>").Count == 0,
              "a feed with no items yields nothing");

        // ---- direction. Gold rises on fear and easing, falls on calm and tightening.
        var war = GoldNewsWatch.Score("Israel strikes Iranian nuclear site, Tehran vows retaliation");
        Check(war.Direction > 0 && war.Impact >= 4.0,
              string.Format("escalation reads BULLISH for gold (dir {0:+0.0;-0.0}, impact {1:F1})",
                            war.Direction, war.Impact));
        var truce = GoldNewsWatch.Score("Iran and Israel agree ceasefire; gold slides");
        Check(truce.Direction < 0,
              string.Format("a ceasefire reads BEARISH (dir {0:+0.0;-0.0})", truce.Direction));
        var hawk = GoldNewsWatch.Score("Fed signals higher for longer as dollar surges");
        Check(hawk.Direction < 0,
              string.Format("hawkish Fed reads BEARISH (dir {0:+0.0;-0.0})", hawk.Direction));
        var dove = GoldNewsWatch.Score("Federal Reserve delivers rate cut, dollar falls");
        Check(dove.Direction > 0,
              string.Format("a rate cut reads BULLISH (dir {0:+0.0;-0.0})", dove.Direction));

        // ---- the gate that stops it crying wolf
        var football = GoldNewsWatch.Score("Manchester United sign new striker in record deal");
        Check(football.Impact == 0.0,
              "a loaded word in an unrelated story scores ZERO (\"striker\", \"record deal\")");
        var vague = GoldNewsWatch.Score("Gold trades sideways in quiet session");
        Check(vague.Impact < 3.0, "an uneventful gold headline stays under the alert threshold");
        Check(GoldNewsWatch.Score("").Impact == 0.0, "an empty headline scores zero, does not throw");
        Check(GoldNewsWatch.Score(null).Impact == 0.0, "a null headline scores zero, does not throw");

        // relevance must GATE, not merely add: same words, different subject
        var relevant = GoldNewsWatch.Score("Iran attack sparks gold rally");
        var irrelevant = GoldNewsWatch.Score("Shark attack closes beach");
        Check(relevant.Impact > 0 && irrelevant.Impact == 0.0,
              "the same word scores only when the story is about gold or what moves it");

        // the " FED " token must be a whole word, or half of sport and politics
        // starts scoring as monetary policy
        Check(GoldNewsWatch.Score("Federer wins in straight sets after early attack").Impact == 0.0,
              "\"Federer\" is not the Federal Reserve");
        Check(GoldNewsWatch.Score("Voters are fed up with the war on drugs").Impact == 0.0,
              "\"fed up\" is not the Federal Reserve");
        // No other relevance word in this one on purpose: if " FED " stops
        // being recognised, nothing else can carry it.
        Check(GoldNewsWatch.Score("Fed signals higher for longer").Direction < 0,
              "the shorthand \"Fed\" alone IS recognised");

        // stronger news must outrank weaker news, since alerts are sorted by it
        Check(war.Impact > vague.Impact, "a strike outranks a quiet-session headline");

        // ---- the calendar
        var cal = GoldNewsWatch.ParseCalendarPublic(
            "[{\"title\":\"FOMC Statement\",\"country\":\"USD\",\"date\":\"2026-08-28T18:00:00Z\",\"impact\":\"High\"}," +
            "{\"title\":\"Retail Sales\",\"country\":\"NZD\",\"date\":\"2026-08-28T21:00:00Z\",\"impact\":\"Low\"}," +
            "{\"title\":\"CPI y/y\",\"country\":\"USD\",\"date\":\"2026-08-29T12:30:00Z\",\"impact\":\"High\"}]");
        Check(cal.Count == 2, "keeps the events that move gold, drops the rest (" + cal.Count + " of 3)");
        Check(cal.All(e => e.Tier == 1), "FOMC and US CPI are both tier 1");
        Check(cal.Any(e => e.UtcTime == new DateTime(2026, 8, 28, 18, 0, 0, DateTimeKind.Utc)),
              "parses the event time as UTC");
        Check(GoldNewsWatch.ParseCalendarPublic("not json at all").Count == 0,
              "garbage calendar yields nothing, does not throw");

        Console.WriteLine();
        Console.WriteLine(_fail == 0 ? "ALL NEWS CHECKS PASSED" : _fail + " NEWS CHECK(S) FAILED");
        return _fail == 0 ? 0 : 1;
    }
}

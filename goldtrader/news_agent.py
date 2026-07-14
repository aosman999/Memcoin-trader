"""News Agent — tracks the headlines that move gold.

Wars, missile strikes, sanctions, Fed speeches, CPI prints, rate
decisions: gold is the market's fear gauge, and these events move it
violently and directionally. The agent polls Google News RSS (free, no
key) every few minutes and produces two outputs:

  * HIGH-IMPACT WINDOW — a fresh headline matching a high-impact event
    (FOMC decision, war escalation, CPI release...) opens a no-new-entries
    window: let the first violent move happen without us in it. This is
    the slow-news complement to the EventSentinel's fast price shock veto.
  * DIRECTIONAL BIAS — war/crisis/dovish headlines are gold-bullish
    (blocks NEW SHORTS while hot); peace/hawkish/strong-economy headlines
    are gold-bearish (blocks NEW LONGS).

Fails soft everywhere: no network (e.g. the training sandbox) means the
agent silently stands down after a few attempts and trading proceeds on
price signals alone.
"""
from __future__ import annotations

import re
import urllib.parse
import urllib.request

RSS_URL = ("https://news.google.com/rss/search?" + urllib.parse.urlencode(
    {"q": 'gold OR "federal reserve" OR fed OR war OR missile OR ceasefire '
          'OR CPI OR inflation when:1d', "hl": "en-US", "gl": "US"}))

GOLD_BULLISH = (
    "war", "invasion", "missile", "strike", "attack", "escalat", "conflict",
    "sanction", "crisis", "recession", "rate cut", "dovish", "bank failure",
    "default", "emergency", "safe haven", "tension",
)
GOLD_BEARISH = (
    "ceasefire", "peace deal", "truce", "de-escalat", "rate hike", "hawkish",
    "strong jobs", "strong dollar", "yields surge", "soft landing",
)
HIGH_IMPACT = (
    "fomc", "fed decision", "rate decision", "powell speech", "powell testif",
    "cpi", "nonfarm", "nfp", "war", "invasion", "missile", "emergency",
    "attack", "escalation",
)

_TITLE_RE = re.compile(r"<title>(?:<!\[CDATA\[)?(.*?)(?:\]\]>)?</title>",
                       re.IGNORECASE | re.DOTALL)


CALENDAR_URL = "https://nfs.faireconomy.media/ff_calendar_thisweek.json"
PRE_EVENT_MIN = 10      # flat this long BEFORE a scheduled high-impact event
POST_EVENT_MIN = 15     # and this long after it hits


class NewsAgent:
    name = "news"
    IMPACT_WINDOW_MIN = 20

    def __init__(self, poll_minutes: float = 10.0, enabled: bool = True):
        self.poll_minutes = poll_minutes
        self.enabled = enabled
        self.bias = 0                    # >0 gold-bullish, <0 gold-bearish
        self.headlines: list[str] = []
        self._next_poll = 0.0
        self._impact_until = 0.0
        self._seen: set[str] = set()
        self._fail_streak = 0
        # scheduled economic calendar (Fed, CPI, NFP, speeches):
        # list of unix timestamps of high-impact USD events this week
        self.scheduled_events: list[float] = []
        self._next_calendar_poll = 0.0

    # ------------------------------------------------------------------
    def _fetch_calendar(self) -> None:
        """Weekly high-impact USD events (fail-soft). The bot knows WHEN
        Powell speaks or CPI drops BEFORE it happens and stands aside."""
        import json as _json
        req = urllib.request.Request(CALENDAR_URL,
                                     headers={"User-Agent": "goldtrader/1.0"})
        try:
            with urllib.request.urlopen(req, timeout=8) as r:
                events = _json.loads(r.read().decode("utf-8", "ignore"))
        except Exception:
            return
        import datetime as _dt
        out = []
        for e in events if isinstance(events, list) else []:
            try:
                if e.get("impact") != "High" or e.get("country") not in ("USD",):
                    continue
                ts = _dt.datetime.strptime(
                    e["date"][:19], "%Y-%m-%dT%H:%M:%S").timestamp()
                # the feed's dates carry an offset suffix; a few minutes of
                # skew is acceptable given the +/- window around each event
                out.append(ts)
            except (KeyError, ValueError, TypeError):
                continue
        if out:
            self.scheduled_events = out

    def in_scheduled_event_window(self, now: float) -> bool:
        return any(t - PRE_EVENT_MIN * 60 <= now <= t + POST_EVENT_MIN * 60
                   for t in self.scheduled_events)

    # ------------------------------------------------------------------
    def _fetch_titles(self) -> list[str] | None:
        req = urllib.request.Request(RSS_URL, headers={"User-Agent": "goldtrader/1.0"})
        try:
            with urllib.request.urlopen(req, timeout=8) as r:
                xml = r.read().decode("utf-8", "ignore")
            titles = _TITLE_RE.findall(xml)
            return [t.strip() for t in titles[1:60] if t.strip()]  # [0] = feed title
        except Exception:
            return None

    def update(self, now: float) -> None:
        if not self.enabled:
            return
        if now >= self._next_calendar_poll:
            self._next_calendar_poll = now + 12 * 3600   # refresh twice a day
            self._fetch_calendar()
        if now < self._next_poll:
            return
        self._next_poll = now + self.poll_minutes * 60
        titles = self._fetch_titles()
        if titles is None:
            self._fail_streak += 1
            if self._fail_streak >= 3:
                self.enabled = False     # no news access here; stand down
            return
        self._fail_streak = 0
        fresh = [t for t in titles if t not in self._seen]
        self._seen.update(fresh)
        if len(self._seen) > 2000:
            self._seen = set(list(self._seen)[-1000:])
        self.headlines = titles[:15]

        bull = bear = 0
        for t in titles:
            low = t.lower()
            bull += sum(1 for k in GOLD_BULLISH if k in low)
            bear += sum(1 for k in GOLD_BEARISH if k in low)
        self.bias = bull - bear

        # only FRESH high-impact headlines open the stand-aside window
        for t in fresh:
            low = t.lower()
            if any(k in low for k in HIGH_IMPACT):
                self._impact_until = max(self._impact_until,
                                         now + self.IMPACT_WINDOW_MIN * 60)
                break

    # ------------------------------------------------------------------
    def entry_allowed(self, direction: int, now: float) -> bool:
        """direction: +1 long / -1 short. Exits are never blocked."""
        if not self.enabled:
            return True
        if self.in_scheduled_event_window(now):
            return False                 # scheduled Fed/CPI/speech: stand aside
        if now < self._impact_until:
            return False                 # high-impact window: stand aside
        if self.bias >= 3 and direction < 0:
            return False                 # strongly gold-bullish news: no new shorts
        if self.bias <= -3 and direction > 0:
            return False                 # strongly gold-bearish news: no new longs
        return True

    def state(self, now: float | None = None) -> str:
        if not self.enabled:
            return "news agent: offline"
        import time as _t
        now = _t.time() if now is None else now
        mood = ("bullish" if self.bias >= 3 else
                "bearish" if self.bias <= -3 else "neutral")
        return f"news bias {self.bias:+d} ({mood})" + \
               (", HIGH-IMPACT window open" if now < self._impact_until else "")

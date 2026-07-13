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
        if not self.enabled or now < self._next_poll:
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

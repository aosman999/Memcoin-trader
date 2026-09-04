#!/usr/bin/env python3
"""goldsignals — posts GoldICT's trades to a Telegram channel, from a terminal.

WHY IT IS BUILT THIS WAY
========================
The requirement was "it has to be the exact same as the one on cTrader".  There
are two ways to get that and only one of them actually works.

  The way that does not work: re-implement the strategy here in Python and run
  it beside cTrader.  Two implementations of the same rules drift.  A rounding
  difference, a bar-close convention, one fixed bug -- and the channel starts
  calling trades cTrader never took.  That is the exact failure this is supposed
  to avoid.

  The way that works: cTrader is the ONLY place the rules live.  GoldICT
  appends one JSON line per event to a file -- setup armed, entry, take profit
  filled, stop hit, news, heartbeat.  This script tails that file and posts it.
  The channel cannot disagree with cTrader, because the channel IS cTrader.

So this program contains NO strategy.  It decides nothing about the market.  If
it ever posts a trade cTrader did not take, that is a transport bug, not two
engines that grew apart.

WHAT ABOUT TRADINGVIEW
======================
Read this before asking again, because the honest answer matters here.

TradingView has no API that lets a program watch a chart and read setups off
it.  There is no "log in and look at the chart" endpoint.  What actually exists:

  1. TradingView ALERTS with a webhook.  TradingView pushes to a URL you own
     when a condition fires.  That needs an alert -- which means a Pine script
     condition, i.e. an indicator.  You said no indicator.
  2. Scraping their private websocket.  Against their terms, breaks without
     notice, and would still be a SECOND opinion about the market that can
     disagree with cTrader -- the thing you asked me to prevent.

And there is a third point that matters more than either: the setups do not
need TradingView.  A chart is a picture of price.  GoldICT already reads the
same price, from your broker's own feed, at the tick -- which is closer to the
market than a TradingView chart, not further from it.  TradingView would add a
second data source and a second opinion; it would not add sight.

What IS supported here, because it is real:
  * --tradingview-port N runs a small listener for TradingView webhook alerts.
    If you ever do set an alert -- price crossing a level, a session open, an
    economic release -- point its webhook at this and the text lands in the
    channel as a market update.  It never opens or closes anything.
  * every signal carries a TradingView chart link, so anyone in the channel can
    open the chart at the right symbol in one tap.

SETUP
=====
  1. In Telegram, @BotFather -> /newbot -> a token like 8123456789:AAF...
  2. Add that bot to your channel AS AN ADMIN (a bot cannot post otherwise).
  3. Get the channel id: forward any channel message to @userinfobot, or run
     https://api.telegram.org/bot<TOKEN>/getUpdates after posting in it. A
     channel id looks like -1001234567890.
  4. Put both in data/telegram_config.json:

         {
           "bot_token": "8123456789:AAF...",
           "chat_id": "-1001234567890",
           "feed": "~/GoldICT/signals.jsonl"
         }

     That file is gitignored. The token NEVER goes in a tracked file, a commit,
     a screenshot or a chat message. This script redacts it from everything it
     prints.
  5. Run it:   python3 tools/goldsignals.py
     Test it first without posting:   python3 tools/goldsignals.py --dry-run

Zero dependencies -- standard library only, like the rest of this project.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import time
import urllib.parse
import urllib.request
from datetime import datetime, timezone

CHART_URL = "https://www.tradingview.com/chart/?symbol=OANDA%3AXAUUSD"

# What the channel is told instead of WHY the bot took the trade. The feed still
# carries the model name and the reasoning -- the cTrader log and the ledger
# both need them -- but neither goes out to followers. Nobody in the channel
# needs the method, and a signal that explains itself invites arguing with it.
RISK_LINE = "Utilize risk management techniques to protect capital."

# Whether the channel is told the account is a demo. Off at the owner's
# direction. It matters, so it is worth writing down why the switch exists: the
# cBot REFUSES to run on a live account, so every fill behind every message is
# simulated. With this off, followers read simulated results as real trades.
# Turn it back on with "show_account_type": true in the config.
SHOW_ACCOUNT_TYPE = False

# News filtering. The cBot's own threshold decides what is worth WRITING DOWN;
# these decide what is worth INTERRUPTING A CHANNEL FOR, which is a much higher
# bar. Both are tunable from the config file without touching cTrader.
NEWS_MIN_IMPACT = 6.0        # below this, nothing is posted at all
NEWS_MAX_PER_HOUR = 3        # ceiling for ORDINARY news
NEWS_URGENT_IMPACT = 9.0     # at or above this, send immediately, cap or no cap

# The cap exists so routine wire traffic cannot fill a channel. It must not be
# the reason a war headline waits behind three stories about the oil price. So
# there are two tiers: ordinary news is rationed, major news is not. Duplicate
# suppression still applies to BOTH -- being important does not make a story
# worth sending three times because three outlets filed it.

# Words that carry no meaning for deciding whether two headlines are the same
# story. Without stripping these, "US probing airstrike in Iran" and "US and
# Iran to hold talks" look similar because they share "us", "in", "to".
_STOP = frozenset("""a an and are as at be but by for from has have in into is
it its of on or over that the to was were will with after before amid says say
said report reports new news latest update updates world""".split())


def story_key(title):
    """The significant words of a headline, with the publisher suffix removed.

    Google News appends " - Publisher" to every title, so the SAME story filed
    by Reuters, Al-Monitor and Military Times arrives as three different
    strings. Deduplicating on the raw title therefore does nothing, which is
    exactly what happened: the channel got the same airstrike three times."""
    t = (title or "").strip()
    cut = t.rfind(" - ")
    if cut > 20:                       # keep short titles that merely contain " - "
        t = t[:cut]
    words = []
    for w in t.lower().replace("'", "").split():
        w = "".join(ch for ch in w if ch.isalnum())
        if len(w) > 2 and w not in _STOP:
            words.append(w)
    return frozenset(words)


def same_story(a, b, overlap=0.6):
    """Two headlines are the same story when most of their meaningful words
    agree. Not string equality: the wires rewrite the wording, they do not
    rewrite the facts."""
    if not a or not b:
        return False
    shared = len(a & b)
    return shared / float(min(len(a), len(b))) >= overlap


class NewsGate(object):
    """Decides whether a headline is worth a notification. Keeps a short memory
    of what has already been posted so a story doing the rounds of the wires
    lands once."""

    def __init__(self, min_impact=None, max_per_hour=None, urgent_impact=None,
                 now=time.time):
        self.min_impact = NEWS_MIN_IMPACT if min_impact is None else min_impact
        self.max_per_hour = NEWS_MAX_PER_HOUR if max_per_hour is None else max_per_hour
        self.urgent_impact = (NEWS_URGENT_IMPACT if urgent_impact is None
                              else urgent_impact)
        self.now = now
        self.recent = []                        # (timestamp, story_key, urgent)
        self._vacuum_until = None

    def allow_vacuum(self, ev, now=None):
        """A vacuum window that is merely being EXTENDED is not a new event.

        The cBot re-arms it on every news poll, so without this the channel got
        "heads up until 15:50", then 15:53, then 15:56, three minutes apart,
        for as long as the wires stayed busy. Only the window OPENING is worth
        saying. Fixed in the cBot too; this makes the channel quiet without
        waiting for that to be reinstalled."""
        until = ev.get("until")
        ref = now or datetime.now(timezone.utc)
        if self._vacuum_until is not None and ref < self._vacuum_until:
            return False                       # still inside one already posted
        try:
            self._vacuum_until = datetime.strptime(
                until, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=timezone.utc)
        except (ValueError, TypeError):
            self._vacuum_until = None
        return True

    def allow(self, ev):
        try:
            impact = float(ev.get("impact"))
        except (TypeError, ValueError):
            impact = 0.0
        if impact < self.min_impact:
            return False, "below the impact threshold"

        t = self.now()
        self.recent = [r for r in self.recent if t - r[0] < 3600]

        key = story_key(ev.get("headline"))
        for _, seen, _u in self.recent:
            if same_story(key, seen):
                return False, "the same story already went out"

        urgent = impact >= self.urgent_impact
        if not urgent and self.max_per_hour > 0:
            # Only ORDINARY items count against the ordinary allowance. A major
            # story neither waits for room nor uses up someone else's.
            ordinary = sum(1 for _ts, _k, u in self.recent if not u)
            if ordinary >= self.max_per_hour:
                return False, "hourly limit for ordinary news reached"

        self.recent.append((t, key, urgent))
        return True, "urgent" if urgent else None

# Strategy words that must never appear in a posted message. Checked by a test,
# because this is the kind of thing that comes back the moment a formatter is
# edited without thinking about it.
PRIVATE_WORDS = ("BREAKER", "MSS", "VOID", "ORDERBLOCK",
                 "mitigation", "displacement", "swing high", "swing low",
                 "liquidity", "vacuum block")


def leaks_method(text):
    """Any strategy word that made it into a posted message. Empty is correct."""
    if not text:
        return []
    low = text.lower()
    return [w for w in PRIVATE_WORDS if w.lower() in low]
DEFAULT_CONFIG = "data/telegram_config.json"
DEFAULT_FEED = "~/GoldICT/signals.jsonl"


# ----------------------------------------------------------------- formatting
# Pure functions: an event dict in, the message text out, or None for "say
# nothing". Kept free of network and file IO so tests can drive them directly.

def marker(side):
    """Green for a buy, red for a sell. One definition, used by every message
    about a trade, so the cue cannot end up correct on the signal and wrong on
    the take profit."""
    return "\U0001f7e2" if (side or "").upper() == "BUY" else "\U0001f534"


def _f(v, nd=2):
    try:
        return ("%." + str(nd) + "f") % float(v)
    except (TypeError, ValueError):
        return "?"


def entry_zone(ev):
    """The format asks for a zone. There genuinely is one: the level the model
    was waiting for, and the price the order actually filled at. When they are
    the same to the cent, one number is printed instead of a fake range."""
    fill = ev.get("entry")
    level = ev.get("level", fill)
    try:
        lo, hi = sorted((float(level), float(fill)))
    except (TypeError, ValueError):
        return _f(fill)
    if hi - lo < 0.01:
        return _f(fill)
    return "%s - %s" % (_f(lo), _f(hi))


def format_entry(ev, show_account=None):
    side = (ev.get("side") or "").upper()
    if side not in ("BUY", "SELL"):
        return None
    if show_account is None:
        show_account = SHOW_ACCOUNT_TYPE
    tps = ev.get("tps") or []
    buy = side == "BUY"
    lines = ["%s %s gold" % (marker(side), "Buy" if buy else "Sell"),
             "Entry- %s" % entry_zone(ev)]
    for i, tp in enumerate(tps, 1):
        lines.append("Tp%s%d: %s" % (" " if i == 1 else "", i, _f(tp)))
    lines.append("Sl \U0001f6d1: %s" % _f(ev.get("stop")))
    lines.append("")
    lines.append(RISK_LINE)
    if show_account:
        lines.append("⚠️ LIVE ACCOUNT" if not ev.get("demo", True)
                     else "Demo account — simulated fills.")
    lines.append(CHART_URL)
    return "\n".join(lines)


def format_tp(ev):
    n, of = ev.get("rung"), ev.get("of")
    side = (ev.get("side") or "").upper()
    head = "✅ TP%s HIT — %s %s gold" % (
        n, marker(side), "Buy" if side == "BUY" else "Sell")
    lines = [head,
             "Filled at %s (entry %s)" % (_f(ev.get("price")), _f(ev.get("entry"))),
             "Profit on this third: %s" % _f(ev.get("profit"))]
    try:
        if int(n) < int(of):
            lines.append("Runners still open, stop is trailing.")
        else:
            lines.append("Full position closed.")
    except (TypeError, ValueError):
        pass
    return "\n".join(lines)


def format_sl(ev):
    side = (ev.get("side") or "").upper()
    return "\n".join([
        "\U0001f6d1 SL hit — %s %s gold" % (
            marker(side), "Buy" if side == "BUY" else "Sell"),
        "Out at %s (entry %s)" % (_f(ev.get("price")), _f(ev.get("entry"))),
        "Result: %s" % _f(ev.get("profit")),
    ])


def format_close(ev):
    """Not the take profit, and not a loss — so the trailing stop caught it on
    the way back down, or the time exit took it. Worth naming properly: a
    trailed exit in profit is the trail doing exactly its job, and reading
    "closed" for it makes a good outcome look like a shrug."""
    side = (ev.get("side") or "").title()
    try:
        profit = float(ev.get("profit"))
    except (TypeError, ValueError):
        profit = 0.0
    if profit > 0:
        head = "\U0001f512 Stopped out IN PROFIT — %s gold" % side
        note = "The trail had moved the stop behind price. That is it working."
    else:
        head = "▪️ Closed flat — %s gold" % side
        note = "No target, no stop — the time limit or a manual close took it."
    return "\n".join([
        head,
        "Out at %s (entry %s)" % (_f(ev.get("price")), _f(ev.get("entry"))),
        "Result: %s" % _f(profit),
        note,
    ])


def format_setup(ev):
    side = (ev.get("side") or "").upper()
    return "\n".join([
        "⚡ GET READY — possible %s %s gold" % (
            marker(side), "Buy" if side == "BUY" else "Sell"),
        "Watching %s" % _f(ev.get("level")),
        "If it fills: stop %s, first target around %s"
        % (_f(ev.get("stop")), _f(ev.get("projected_tp"))),
        "",
        "Nothing is open yet. No entry until price comes back to that level.",
        RISK_LINE,
    ])


def format_news(ev):
    return "\n".join([
        "\U0001f4f0 %s" % ev.get("source", "wire"),
        ev.get("headline", ""),
        "impact %s · %s" % (_f(ev.get("impact"), 1), ev.get("lean", "")),
        "",
        "News only. No signal is taken from a headline on its own.",
        RISK_LINE,
    ])


def format_vacuum(ev):
    return "\n".join([
        "\U0001f300 Heads up until %s UTC" % (ev.get("until", "?")[11:16]),
        "Conditions after a move like that let setups come faster than usual.",
        RISK_LINE,
    ])


def format_guard(ev):
    """Built from the numbers, not from the feed's free text. A formatter that
    echoes a free-text field is one edit away from putting the model name in
    the channel, and this one used to."""
    try:
        start = float(ev.get("start_equity"))
        now = float(ev.get("equity"))
        down = (start - now) / start * 100.0 if start else 0.0
        detail = "Down %s%% on the day." % _f(down, 1)
    except (TypeError, ValueError, ZeroDivisionError):
        detail = "The daily loss limit was reached."
    return "\n".join([
        "⛔ No more entries today",
        detail,
        "Open trades keep their stops. Trading resumes tomorrow.",
        RISK_LINE,
    ])


def format_start(ev):
    return "\n".join([
        "\U0001f7e2 Live on %s" % ev.get("symbol", "XAUUSD"),
        "Every signal in this channel is placed by the bot itself. Nothing here "
        "is typed by hand.",
        RISK_LINE,
    ])


def format_stop(ev):
    return "\U0001f534 GoldICT stopped. %s trades today, equity %s." % (
        ev.get("trades_today", "?"), _f(ev.get("equity")))


def format_heartbeat(ev):
    """The market update. Deliberately dull: it says what the bot is doing, and
    says nothing at all when the honest answer is 'nothing'."""
    bits = []
    if ev.get("day_guard"):
        bits.append("day guard is on, no new entries today")
    if ev.get("news_window"):
        bits.append("standing aside for a scheduled event")
    if ev.get("vacuum_armed"):
        bits.append("vacuum window armed")
    if not ev.get("session_open"):
        bits.append("outside trading hours")
    state = "; ".join(bits) if bits else "watching"
    return "\n".join([
        "\U0001f4ca Update — gold %s" % _f(ev.get("price")),
        "%s open signal(s), %s setup(s) waiting, %s trade(s) today"
        % (ev.get("open_signals", 0), ev.get("waiting_setups", 0),
           ev.get("trades_today", 0)),
        "Status: %s" % state,
    ])


FORMATTERS = {
    "entry": format_entry,
    "tp": format_tp,
    "sl": format_sl,
    "close": format_close,
    "setup": format_setup,
    "news": format_news,
    "vacuum": format_vacuum,
    "guard": format_guard,
    "start": format_start,
    "stop": format_stop,
    "heartbeat": format_heartbeat,
}


def render(ev, want, gate=None):
    """One event -> the text to post, or None. `want` is the set of event types
    the operator has switched on."""
    kind = ev.get("t")
    if kind not in want:
        return None
    if kind == "vacuum" and gate is not None and not gate.allow_vacuum(ev):
        return None
    if kind == "news" and gate is not None:
        ok, why = gate.allow(ev)
        if not ok:
            return None
        if why == "urgent":
            return "\U0001f6a8 MAJOR\n" + format_news(ev)
    fn = FORMATTERS.get(kind)
    if fn is None:
        return None
    try:
        return fn(ev)
    except Exception as exc:                       # a malformed line must not
        return "⚠️ could not render a %s event: %s" % (kind, exc)


# ------------------------------------------------------------------- telegram
class Telegram(object):
    """Posts to a channel. The token is a credential: it is loaded from a
    gitignored file, never logged, and stripped from every error message."""

    # Telegram allows roughly 20 messages a minute to one channel. Going over
    # earns a 429 and, if you keep going, a longer block. So the sender paces
    # itself rather than firing as fast as the feed is read.
    MIN_GAP_SECONDS = 3.5

    def __init__(self, token, chat_id, dry_run=False, log=print, opener=None,
                 sleep=time.sleep, clock=time.time, min_gap=None):
        self.token = token or ""
        self.chat_id = chat_id or ""
        self.dry_run = dry_run
        self.log = log
        self.sleep = sleep
        self.clock = clock
        self.min_gap = self.MIN_GAP_SECONDS if min_gap is None else min_gap
        self._last_send = 0.0
        self.dropped = 0
        # Injectable so a test can prove no request is even attempted in a dry
        # run, without the test itself touching the network.
        self.opener = opener or urllib.request.urlopen

    def redact(self, text):
        if self.token and self.token in text:
            text = text.replace(self.token, "<token redacted>")
        # also catch the bare id half of "12345:AAF..." appearing on its own
        head = self.token.split(":")[0] if ":" in self.token else ""
        if head and len(head) >= 6 and head in text:
            text = text.replace(head, "<token redacted>")
        return text

    def url(self):
        return "https://api.telegram.org/bot%s/sendMessage" % self.token

    @staticmethod
    def retry_after(exc):
        """How long Telegram ASKED us to wait. A 429 carries the number in its
        body: {"parameters":{"retry_after":34}}. Guessing with a fixed backoff
        instead -- which is what this used to do -- means hammering a server
        that already said stop, burning the retries, and dropping the message."""
        body = ""
        read = getattr(exc, "read", None)
        if read:
            try:
                body = read().decode("utf-8", "replace")
            except Exception:
                body = ""
        if not body:
            body = str(exc)
        marker = '"retry_after"'
        i = body.find(marker)
        if i < 0:
            return None
        digits = ""
        for ch in body[i + len(marker):]:
            if ch.isdigit():
                digits += ch
            elif digits:
                break
        try:
            return min(300, int(digits))          # never sleep more than 5 min
        except ValueError:
            return None

    def send(self, text, attempts=4):
        if self.dry_run or not self.token or not self.chat_id:
            self.log("---- would post ----\n%s\n" % text)
            return True
        data = urllib.parse.urlencode({
            "chat_id": self.chat_id,
            "text": text,
            "disable_web_page_preview": "true",
        }).encode("utf-8")

        # pace ourselves before we are told to
        gap = self.clock() - self._last_send
        if gap < self.min_gap:
            self.sleep(self.min_gap - gap)

        delay = 2
        attempt = 0
        waits = 0
        while attempt < attempts:
            try:
                req = urllib.request.Request(self.url(), data=data)
                resp = self.opener(req, timeout=20)
                try:
                    resp.read()
                finally:
                    close = getattr(resp, "close", None)
                    if close:
                        close()
                self._last_send = self.clock()
                return True
            except Exception as exc:
                msg = self.redact("%s: %s" % (type(exc).__name__, exc))
                wait = self.retry_after(exc)
                if wait is not None and waits < 5:
                    # Telegram named a number. Waiting less is how a rate limit
                    # becomes a block, and being told to wait is not the same as
                    # failing -- so it does NOT consume a retry. Capped at 5 so a
                    # server stuck on 429 cannot hold the loop forever.
                    waits += 1
                    self.log("telegram rate limited, waiting %ds as instructed" % wait)
                    self.sleep(wait + 1)
                    self._last_send = self.clock()
                    continue
                attempt += 1
                if attempt >= attempts:
                    self.dropped += 1
                    self.log("TELEGRAM FAILED after %d tries — %s\n"
                             "  DROPPED this message: %s"
                             % (attempts, msg, text.split("\n")[0][:70]))
                    return False
                self.log("telegram send failed (%s), retrying in %ds" % (msg, delay))
                self.sleep(delay)
                delay *= 2
        self._last_send = self.clock()
        return False


# ----------------------------------------------------------------------- tail
def read_new_lines(path, offset):
    """Everything appended since `offset`. Returns (lines, new_offset).

    Handles the file being truncated or replaced -- cTrader restarting, or a log
    rotation -- by starting over rather than seeking past the end and going
    permanently silent."""
    if not os.path.exists(path):
        return [], offset
    size = os.path.getsize(path)
    if size < offset:
        offset = 0                                  # truncated or replaced
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        fh.seek(offset)
        chunk = fh.read()
        new_offset = fh.tell()
    if not chunk.endswith("\n") and chunk:
        # a half-written last line: leave it for the next pass
        cut = chunk.rfind("\n")
        if cut < 0:
            return [], offset
        new_offset = offset + len(chunk[:cut + 1].encode("utf-8"))
        chunk = chunk[:cut + 1]
    return [l for l in chunk.split("\n") if l.strip()], new_offset


# ------------------------------------------------------------------ one copy
# Two copies of this program watching the same feed post every event twice,
# and there is nothing in the messages to say which copy sent which. It is an
# easy mistake -- start it in a second tab, forget the first is still alive --
# and it looks exactly like a bug in the bot. So the second copy refuses.

def _alive(pid):
    try:
        os.kill(pid, 0)                 # signal 0 asks "does this exist?"
    except OSError:
        return False
    except Exception:
        return False
    return True


def claim_lock(path):
    """Take the lock, or return the pid of whoever already holds it.

    A lock left behind by a crash is taken over rather than blocking forever:
    what matters is whether that process is still RUNNING, not whether it
    tidied up after itself."""
    try:
        if os.path.exists(path):
            try:
                with open(path, "r", encoding="utf-8") as fh:
                    other = int((fh.read() or "0").strip())
            except (ValueError, OSError):
                other = 0
            if other and other != os.getpid() and _alive(other):
                return other
        d = os.path.dirname(path)
        if d and not os.path.isdir(d):
            os.makedirs(d)
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(str(os.getpid()))
    except OSError:
        return None                     # cannot lock: do not block the user
    return None


def release_lock(path):
    try:
        with open(path, "r", encoding="utf-8") as fh:
            if int((fh.read() or "0").strip()) != os.getpid():
                return                  # someone else's lock, leave it alone
        os.remove(path)
    except (ValueError, OSError):
        pass


# --------------------------------------------------------------- stale events
# The bot resumes from where it stopped, which is right for not losing a trade
# and WRONG for posting one. A "Buy gold" for a trade cTrader opened three hours
# ago -- and has since closed -- invites a follower into a price that is gone.
# So anything that happened while nobody was watching is reported as history,
# not as a signal.

def event_age_minutes(ev, now=None):
    """Minutes since the event, or None if it carries no usable timestamp."""
    stamp = ev.get("utc")
    if not stamp:
        return None
    try:
        when = datetime.strptime(stamp, "%Y-%m-%dT%H:%M:%SZ").replace(
            tzinfo=timezone.utc)
    except (ValueError, TypeError):
        return None
    ref = now or datetime.now(timezone.utc)
    return (ref - when).total_seconds() / 60.0


# Events whose absence needs explaining, so the summary can name them.
ACTIONABLE = ("entry", "setup", "tp", "sl", "close", "vacuum")


def too_stale(ev, max_age_minutes, now=None):
    """True when this event is too old to post.

    This applies to EVERY kind, not just trades. The first version exempted
    news and heartbeats on the theory that suppressing them would hide that the
    service had been down -- but the summary already says that, and the effect
    of the exemption was twelve backlogged messages arriving at once the moment
    the bot came back. Nobody needs a three-hour-old heartbeat or a headline
    that has already been overtaken."""
    if max_age_minutes <= 0:
        return False
    age = event_age_minutes(ev, now)
    return age is not None and age > max_age_minutes


def load_state(path):
    try:
        with open(path, "r", encoding="utf-8") as fh:
            return json.load(fh)
    except Exception:
        return {}


def save_state(path, state):
    try:
        tmp = path + ".tmp"
        with open(tmp, "w", encoding="utf-8") as fh:
            json.dump(state, fh)
        os.replace(tmp, path)
    except Exception:
        pass


# -------------------------------------------------------- tradingview webhook
def serve_tradingview(port, tg, log=print):
    """TradingView pushes here when one of YOUR alerts fires. It is a one-way
    inbox: text arrives, text goes to the channel as a market update. It never
    opens, closes or modifies anything -- cTrader stays the only thing that
    trades."""
    import http.server
    import threading

    class Handler(http.server.BaseHTTPRequestHandler):
        def do_POST(self):
            try:
                n = int(self.headers.get("Content-Length", 0))
                body = self.rfile.read(n).decode("utf-8", "replace").strip()
            except Exception:
                body = ""
            self.send_response(200)
            self.end_headers()
            self.wfile.write(b"ok")
            if body:
                log("tradingview alert: %s" % body[:200])
                tg.send("\U0001f4c8 TradingView alert\n%s\n\n"
                        "This is an alert you set on TradingView. It is market "
                        "information only — the bot does not trade on it." % body[:1500])

        def do_GET(self):
            self.send_response(200)
            self.end_headers()
            self.wfile.write(b"goldsignals tradingview listener is up")

        def log_message(self, *a):
            pass                                     # keep the console clean

    srv = http.server.ThreadingHTTPServer(("0.0.0.0", port), Handler)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    log("TradingView listener on port %d. Point an alert webhook at "
        "http://<this machine>:%d/ — it needs to be reachable from the "
        "internet, so use a tunnel (ngrok, Cloudflare Tunnel) rather than "
        "opening your router." % (port, port))
    return srv


# ---------------------------------------------------------------- setup help
# The fiddly part of this is not the code, it is getting a token, a channel id
# and a file path lined up without a typo. So the program does it, checks each
# piece as it goes, and says which one is wrong instead of just going quiet.

class TlsError(RuntimeError):
    """Python could not verify Telegram's certificate. Nothing to do with the
    token, and the fix is on this machine."""

    HELP = (
        "\n  Python on this Mac cannot verify Telegram's certificate. Your token\n"
        "  is probably fine — this is a certificate problem on the machine.\n"
        "\n"
        "  Check which of the two it is:\n"
        "      curl -sS -o /dev/null -w '%{http_code}\\n' https://api.telegram.org/\n"
        "\n"
        "  If curl prints a number, macOS is fine and Python is missing its root\n"
        "  certificates. Find and run the installer that ships with Python:\n"
        "      find /Applications -name 'Install Certificates.command'\n"
        "      open '/Applications/Python 3.x/Install Certificates.command'\n"
        "\n"
        "  If curl fails too, something is intercepting HTTPS on this network —\n"
        "  antivirus with SSL scanning, a VPN, or a corporate proxy.\n")


def api(token, method, params=None, opener=None):
    """One Telegram API call. Returns the parsed 'result', or raises with the
    token stripped out of the message."""
    url = "https://api.telegram.org/bot%s/%s" % (token, method)
    data = urllib.parse.urlencode(params or {}).encode("utf-8")
    op = opener or urllib.request.urlopen
    try:
        resp = op(urllib.request.Request(url, data=data), timeout=20)
        try:
            body = resp.read().decode("utf-8", "replace")
        finally:
            close = getattr(resp, "close", None)
            if close:
                close()
    except Exception as exc:
        msg = str(exc).replace(token, "<token>")
        # A TLS failure is not a bad token, and saying so sends people off
        # re-copying a credential that was fine. Name the actual problem.
        if "CERTIFICATE_VERIFY_FAILED" in msg or "SSLCertVerification" in msg:
            raise TlsError(msg)
        raise RuntimeError("%s: %s" % (type(exc).__name__, msg))
    parsed = json.loads(body)
    if not parsed.get("ok"):
        raise RuntimeError(parsed.get("description", "Telegram said no"))
    return parsed.get("result")


def chat_ids_from_updates(result):
    """Every chat id Telegram has seen this bot in, newest first, with a label.

    A channel id is the awkward part of the setup: it is not in the channel's
    URL and Telegram does not show it anywhere in the app. But the moment the
    bot is added as an admin, or anything is posted, it turns up here."""
    found = []
    for upd in reversed(result or []):
        for key in ("channel_post", "message", "my_chat_member",
                    "edited_channel_post"):
            node = upd.get(key)
            if not isinstance(node, dict):
                continue
            chat = node.get("chat")
            if not isinstance(chat, dict) or "id" not in chat:
                continue
            entry = (str(chat["id"]),
                     chat.get("title") or chat.get("username")
                     or chat.get("first_name") or chat.get("type", "chat"))
            if entry not in found:
                found.append(entry)
    return found


def default_config_path():
    """Prefer the repo's gitignored data/ directory when running from a clone,
    so a token cannot be committed by accident. Otherwise the home directory."""
    here = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    repo_data = os.path.join(here, "data")
    if os.path.isdir(repo_data) and os.path.exists(os.path.join(here, ".gitignore")):
        return os.path.join(repo_data, "telegram_config.json")
    return os.path.expanduser("~/.goldsignals/telegram_config.json")


def write_config(path, cfg):
    """Written owner-only. A bot token in a world-readable file is a bot token
    anyone with an account on the machine can post as."""
    d = os.path.dirname(path)
    if d and not os.path.isdir(d):
        os.makedirs(d)
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(cfg, fh, indent=2)
        fh.write("\n")
    try:
        os.chmod(path, 0o600)
    except OSError:
        pass


def cmd_setup(config_path, feed_default=DEFAULT_FEED, ask=input, out=print,
              opener=None):
    out("")
    out("  GoldICT -> Telegram setup")
    out("  " + "-" * 40)
    out("  Nothing here is saved until every step below passes.")
    out("")

    out("  STEP 1  In Telegram, message @BotFather, send /newbot, follow it,")
    out("          and copy the token it gives you.")
    token = ask("          Paste the token here: ").strip()
    if not token:
        out("  No token given. Nothing was saved.")
        return 1
    try:
        me = api(token, "getMe", opener=opener)
    except TlsError as exc:
        out("  Could not reach Telegram: %s" % exc)
        out(TlsError.HELP)
        return 1
    except Exception as exc:
        out("  That token did not work: %s" % exc)
        out("  Check you copied all of it, including the part after the colon.")
        return 1
    out("  OK — the token belongs to @%s." % me.get("username", "?"))
    out("")

    out("  STEP 2  Add @%s to your channel AS AN ADMIN." % me.get("username", "?"))
    out("          Channel -> Administrators -> Add Admin -> search the bot.")
    out("          A bot that is only a member cannot post.")
    out("          Then post any message in the channel.")
    ask("          Press Enter once you have done that: ")

    chat_id = ""
    try:
        found = chat_ids_from_updates(api(token, "getUpdates", opener=opener))
    except Exception as exc:
        found = []
        out("  Could not read the chat list (%s) — you can type the id instead." % exc)
    if found:
        out("")
        out("  Telegram has seen this bot in:")
        for i, (cid, title) in enumerate(found, 1):
            out("    %d) %s   (%s)" % (i, title, cid))
        pick = ask("  Which one is your channel? (number, or paste an id): ").strip()
        if pick.isdigit() and 1 <= int(pick) <= len(found):
            chat_id = found[int(pick) - 1][0]
        else:
            chat_id = pick
    else:
        out("  Telegram has not seen the bot anywhere yet. That usually means it")
        out("  was not added to the channel, or nothing has been posted since.")
        chat_id = ask("  Paste the channel id instead (looks like -1001234567890): ").strip()
    if not chat_id:
        out("  No channel id. Nothing was saved.")
        return 1

    out("")
    out("  STEP 3  Sending a test message ...")
    try:
        api(token, "sendMessage",
            {"chat_id": chat_id,
             "text": "GoldICT is connected. Signals will arrive here."},
            opener=opener)
    except Exception as exc:
        out("  Could not post to that channel: %s" % exc)
        out("  The usual cause is the bot not being an ADMIN of the channel.")
        return 1
    out("  Sent. Check the channel — you should see it.")
    out("")

    out("  STEP 4  Where cTrader writes its signal file.")
    out("          Leave blank for the default.")
    feed = ask("          Feed file [%s]: " % feed_default).strip() or feed_default
    expanded = os.path.expanduser(feed)
    if os.path.exists(expanded):
        out("  Found it.")
    else:
        out("  Not there yet — that is normal before GoldICT has run once with")
        out("  'Write the signal feed' switched on. It will be picked up when")
        out("  it appears.")

    write_config(config_path, {"bot_token": token, "chat_id": chat_id,
                               "feed": feed, "show_account_type": False})
    out("")
    out("  Saved to %s (readable only by you)." % config_path)
    out("  Never commit that file, screenshot it, or paste it into a chat.")
    out("")
    out("  Now run:   python3 %s" % os.path.abspath(__file__))
    out("")
    return 0


def cmd_check(config_path, out=print, opener=None):
    """Says which link in the chain is broken, rather than sitting silent."""
    ok = True
    out("")
    out("  Checking the chain, cTrader -> file -> Telegram")
    out("  " + "-" * 46)

    if not os.path.exists(config_path):
        out("  [X] no config at %s — run with --setup" % config_path)
        return 1
    cfg = load_config(config_path)
    out("  [OK] config found at %s" % config_path)

    feed = os.path.expanduser(cfg.get("feed") or DEFAULT_FEED)
    if not os.path.exists(feed):
        out("  [X] no feed file at %s" % feed)
        out("       -> is cTrader running, with GoldICT on the chart and")
        out("          'Write the signal feed' set to true?")
        ok = False
    else:
        size = os.path.getsize(feed)
        age = time.time() - os.path.getmtime(feed)
        out("  [OK] feed file: %s (%d bytes)" % (feed, size))
        if age > 3600:
            out("  [!]  nothing new in it for %d minutes. If cTrader is running"
                % (age / 60))
            out("       and the market is open, check the cTrader log for")
            out("       'Signal feed DISABLED'.")

    token = cfg.get("bot_token")
    if not token:
        out("  [X] no bot_token in the config")
        return 1
    try:
        me = api(token, "getMe", opener=opener)
        out("  [OK] token works — @%s" % me.get("username", "?"))
    except TlsError as exc:
        out("  [X] cannot reach Telegram at all: %s" % exc)
        out(TlsError.HELP)
        return 1
    except Exception as exc:
        out("  [X] token rejected: %s" % exc)
        return 1

    chat = cfg.get("chat_id")
    if not chat:
        out("  [X] no chat_id in the config")
        return 1
    try:
        api(token, "sendMessage",
            {"chat_id": chat, "text": "Connection check — everything is wired up."},
            opener=opener)
        out("  [OK] posted a test message to %s" % chat)
    except Exception as exc:
        out("  [X] cannot post to %s: %s" % (chat, exc))
        out("       -> the bot almost certainly is not an ADMIN of the channel")
        return 1

    out("")
    out("  All good." if ok else "  Telegram is fine; the cTrader end needs a look.")
    out("")
    return 0 if ok else 1


# ----------------------------------------------------------------------- main
ALL_EVENTS = ["entry", "tp", "sl", "close", "setup", "news", "vacuum",
              "guard", "start", "stop", "heartbeat"]


def report_missed(missed, tg):
    """One honest summary instead of a burst of expired signals.

    It goes to the channel because silence would be worse: subscribers who
    later see the trades in a ledger would have no idea why they were never
    posted. It is explicitly labelled as history."""
    kinds = {}
    for ev in missed:
        kinds[ev.get("t")] = kinds.get(ev.get("t"), 0) + 1
    entries = kinds.get("entry", 0)
    print("skipped %d event(s) that happened while this was not running: %s"
          % (len(missed), ", ".join("%s x%d" % kv for kv in sorted(kinds.items()))))
    if entries <= 0:
        # Backlogged news and heartbeats are simply dropped. Announcing "you
        # missed 40 headlines" is itself the noise this is meant to prevent.
        return
    tg.send(
        "\u23f8 Missed while the signal service was offline: %d trade(s) were "
        "taken by the bot and are NOT posted above.\n"
        "They are not shown as signals because their entry prices are hours "
        "old — acting on them now would mean entering at a price that has "
        "gone.\n"
        "%s" % (entries, RISK_LINE))


def load_config(path):
    if not os.path.exists(path):
        return {}
    with open(path, "r", encoding="utf-8") as fh:
        return json.load(fh)


def main(argv=None):
    ap = argparse.ArgumentParser(
        description="Post GoldICT's cTrader activity to a Telegram channel.")
    ap.add_argument("--config", default=None,
                    help="gitignored JSON with bot_token, chat_id, feed")
    ap.add_argument("--setup", action="store_true",
                    help="walk through connecting Telegram, step by step")
    ap.add_argument("--check", action="store_true",
                    help="say which link in the chain is broken")
    ap.add_argument("--feed", default=None, help="the JSONL file GoldICT writes")
    ap.add_argument("--state", default=None,
                    help="where to remember how far the feed was read")
    ap.add_argument("--dry-run", action="store_true",
                    help="print what would be posted, post nothing")
    ap.add_argument("--from-start", action="store_true",
                    help="replay the whole feed instead of starting at the end")
    ap.add_argument("--only", default=",".join(ALL_EVENTS),
                    help="comma-separated event types to post")
    ap.add_argument("--poll", type=float, default=2.0,
                    help="seconds between checks of the feed")
    ap.add_argument("--news-min-impact", type=float, default=None,
                    help="only post headlines scoring at least this (default 6)")
    ap.add_argument("--news-max-per-hour", type=int, default=None,
                    help="ceiling on ORDINARY news per hour (0 = no limit)")
    ap.add_argument("--max-event-age", type=float, default=30.0,
                    help="minutes; a trade event older than this is reported as "
                         "history instead of posted as a signal (0 = post "
                         "everything however old)")
    ap.add_argument("--news-urgent-impact", type=float, default=None,
                    help="at or above this, send immediately whatever the cap "
                         "(default 9)")
    ap.add_argument("--tradingview-port", type=int, default=0,
                    help="listen for TradingView alert webhooks on this port")
    ap.add_argument("--once", action="store_true",
                    help="drain what is there and exit (for testing)")
    args = ap.parse_args(argv)
    config_path = args.config or default_config_path()

    if args.setup:
        return cmd_setup(config_path)
    if args.check:
        return cmd_check(config_path)

    cfg = load_config(config_path)
    feed = os.path.expanduser(args.feed or cfg.get("feed") or DEFAULT_FEED)
    state_path = args.state or (feed + ".posted")
    want = set(x.strip() for x in args.only.split(",") if x.strip())

    global SHOW_ACCOUNT_TYPE
    SHOW_ACCOUNT_TYPE = bool(cfg.get("show_account_type", False))

    tg = Telegram(cfg.get("bot_token"), cfg.get("chat_id"), dry_run=args.dry_run)

    if not args.dry_run and (not cfg.get("bot_token") or not cfg.get("chat_id")):
        print("No bot_token/chat_id in %s — running as --dry-run.\n"
              "Run this to connect Telegram, one step at a time:\n"
              "    python3 %s --setup"
              % (config_path, os.path.abspath(__file__)))
        tg.dry_run = True

    gate = NewsGate(
        min_impact=args.news_min_impact
        if args.news_min_impact is not None else cfg.get("news_min_impact"),
        max_per_hour=args.news_max_per_hour
        if args.news_max_per_hour is not None else cfg.get("news_max_per_hour"),
        urgent_impact=args.news_urgent_impact
        if args.news_urgent_impact is not None else cfg.get("news_urgent_impact"))

    state = load_state(state_path)
    offset = state.get("offset", 0)
    if args.from_start:
        offset = 0
    elif "offset" not in state and os.path.exists(feed):
        # First run: start at the END. Nobody wants three months of backfill
        # dumped into the channel the first time this is switched on.
        offset = os.path.getsize(feed)

    lock_path = state_path + ".lock"
    holder = claim_lock(lock_path)
    if holder is not None:
        print("ALREADY RUNNING as process %d, watching the same feed.\n"
              "Two copies post every message twice — which looks exactly like a\n"
              "bug in the bot, and is not. This one is stopping.\n"
              "\n"
              "To stop every copy and start fresh:\n"
              "    pkill -f goldsignals\n"
              "    python3 %s\n"
              "\n"
              "Note the pattern has NO .py — a copy started from a file macOS\n"
              "named 'goldsignals (1).py' does not match 'goldsignals.py' and\n"
              "survives the kill, which is how two copies stay alive."
              % (holder, os.path.abspath(__file__)))
        return 1

    print("goldsignals watching %s" % feed)
    if not os.path.exists(feed):
        print("  (that file does not exist yet — it appears the first time "
              "GoldICT runs with 'Write the signal feed' on. Waiting.)")
    print("  posting: %s" % ", ".join(sorted(want)))
    print("  anything older than %.0f min is skipped; missed TRADES are "
          "summarised, backlogged news is dropped" % args.max_event_age)
    print("  news: impact >= %.1f to post at all; ordinary news at most %s per "
          "hour;\n        impact >= %.1f sent IMMEDIATELY whatever the cap; "
          "repeats suppressed"
          % (gate.min_impact,
             gate.max_per_hour if gate.max_per_hour > 0 else "unlimited",
             gate.urgent_impact))
    print("  mode: %s" % ("DRY RUN — nothing is sent" if tg.dry_run else "posting to Telegram"))

    if args.tradingview_port:
        serve_tradingview(args.tradingview_port, tg)

    while True:
        missed = []
        lines, offset = read_new_lines(feed, offset)
        for line in lines:
            try:
                ev = json.loads(line)
            except ValueError:
                print("skipping unparseable feed line: %s" % line[:120])
                continue
            if too_stale(ev, args.max_event_age):
                missed.append(ev)
                continue
            text = render(ev, want, gate)
            if text:
                tg.send(text)
        if missed:
            report_missed(missed, tg)
        if lines:
            state["offset"] = offset
            save_state(state_path, state)
        if args.once:
            release_lock(lock_path)
            return 0
        try:
            time.sleep(args.poll)
        except KeyboardInterrupt:
            print("\nstopped.")
            release_lock(lock_path)
            return 0


if __name__ == "__main__":
    sys.exit(main())

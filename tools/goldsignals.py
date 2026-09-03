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


def format_entry(ev):
    side = (ev.get("side") or "").upper()
    if side not in ("BUY", "SELL"):
        return None
    tps = ev.get("tps") or []
    lines = ["%s gold" % ("Buy" if side == "BUY" else "Sell"),
             "Entry- %s" % entry_zone(ev)]
    for i, tp in enumerate(tps, 1):
        lines.append("Tp%s%d: %s" % (" " if i == 1 else "", i, _f(tp)))
    lines.append("Sl \U0001f6d1: %s" % _f(ev.get("stop")))
    lines.append("")
    lines.append(RISK_LINE)
    if not ev.get("demo", True):
        lines.append("⚠️ LIVE ACCOUNT")
    else:
        lines.append("Demo account — simulated fills.")
    lines.append(CHART_URL)
    return "\n".join(lines)


def format_tp(ev):
    n, of = ev.get("rung"), ev.get("of")
    side = (ev.get("side") or "").upper()
    head = "✅ TP%s HIT — %s gold" % (n, "Buy" if side == "BUY" else "Sell")
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
        "\U0001f6d1 SL hit — %s gold" % ("Buy" if side == "BUY" else "Sell"),
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
        "⚡ GET READY — possible %s gold" % ("Buy" if side == "BUY" else "Sell"),
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


def render(ev, want):
    """One event -> the text to post, or None. `want` is the set of event types
    the operator has switched on."""
    kind = ev.get("t")
    if kind not in want:
        return None
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

    def __init__(self, token, chat_id, dry_run=False, log=print, opener=None):
        self.token = token or ""
        self.chat_id = chat_id or ""
        self.dry_run = dry_run
        self.log = log
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

    def send(self, text, attempts=4):
        if self.dry_run or not self.token or not self.chat_id:
            self.log("---- would post ----\n%s\n" % text)
            return True
        data = urllib.parse.urlencode({
            "chat_id": self.chat_id,
            "text": text,
            "disable_web_page_preview": "true",
        }).encode("utf-8")
        delay = 2
        for attempt in range(1, attempts + 1):
            try:
                req = urllib.request.Request(self.url(), data=data)
                resp = self.opener(req, timeout=20)
                try:
                    resp.read()
                finally:
                    close = getattr(resp, "close", None)
                    if close:
                        close()
                return True
            except Exception as exc:
                msg = self.redact("%s: %s" % (type(exc).__name__, exc))
                if attempt == attempts:
                    self.log("TELEGRAM FAILED after %d tries — %s" % (attempts, msg))
                    return False
                self.log("telegram send failed (%s), retrying in %ds" % (msg, delay))
                time.sleep(delay)
                delay *= 2
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


# ----------------------------------------------------------------------- main
ALL_EVENTS = ["entry", "tp", "sl", "close", "setup", "news", "vacuum",
              "guard", "start", "stop", "heartbeat"]


def load_config(path):
    if not os.path.exists(path):
        return {}
    with open(path, "r", encoding="utf-8") as fh:
        return json.load(fh)


def main(argv=None):
    ap = argparse.ArgumentParser(
        description="Post GoldICT's cTrader activity to a Telegram channel.")
    ap.add_argument("--config", default=DEFAULT_CONFIG,
                    help="gitignored JSON with bot_token, chat_id, feed")
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
    ap.add_argument("--tradingview-port", type=int, default=0,
                    help="listen for TradingView alert webhooks on this port")
    ap.add_argument("--once", action="store_true",
                    help="drain what is there and exit (for testing)")
    args = ap.parse_args(argv)

    cfg = load_config(args.config)
    feed = os.path.expanduser(args.feed or cfg.get("feed") or DEFAULT_FEED)
    state_path = args.state or (feed + ".posted")
    want = set(x.strip() for x in args.only.split(",") if x.strip())

    tg = Telegram(cfg.get("bot_token"), cfg.get("chat_id"), dry_run=args.dry_run)

    if not args.dry_run and (not cfg.get("bot_token") or not cfg.get("chat_id")):
        print("No bot_token/chat_id in %s — running as --dry-run.\n"
              "Put them in that file (it is gitignored) to post for real.\n"
              "Setup instructions are in the header of this script."
              % args.config)
        tg.dry_run = True

    state = load_state(state_path)
    offset = state.get("offset", 0)
    if args.from_start:
        offset = 0
    elif "offset" not in state and os.path.exists(feed):
        # First run: start at the END. Nobody wants three months of backfill
        # dumped into the channel the first time this is switched on.
        offset = os.path.getsize(feed)

    print("goldsignals watching %s" % feed)
    if not os.path.exists(feed):
        print("  (that file does not exist yet — it appears the first time "
              "GoldICT runs with 'Write the signal feed' on. Waiting.)")
    print("  posting: %s" % ", ".join(sorted(want)))
    print("  mode: %s" % ("DRY RUN — nothing is sent" if tg.dry_run else "posting to Telegram"))

    if args.tradingview_port:
        serve_tradingview(args.tradingview_port, tg)

    while True:
        lines, offset = read_new_lines(feed, offset)
        for line in lines:
            try:
                ev = json.loads(line)
            except ValueError:
                print("skipping unparseable feed line: %s" % line[:120])
                continue
            text = render(ev, want)
            if text:
                tg.send(text)
        if lines:
            state["offset"] = offset
            save_state(state_path, state)
        if args.once:
            return 0
        try:
            time.sleep(args.poll)
        except KeyboardInterrupt:
            print("\nstopped.")
            return 0


if __name__ == "__main__":
    sys.exit(main())

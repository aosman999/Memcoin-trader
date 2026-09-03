# The Telegram channel

Two programs, one running inside cTrader and one in a terminal.

```
 cTrader (GoldICT.cs)                    Terminal (goldsignals.py)
 ─────────────────────                   ──────────────────────────
 reads the broker's tick feed
 finds the setups
 places the orders            ────►      tails the file
 appends every event to                  formats each event
 ~/GoldICT/signals.jsonl                 posts it to your channel
```

## Why it is split like this

The requirement was that the channel be **the same as cTrader**. There are two
ways to build that and only one of them works.

Re-implementing the strategy in Python and running it beside cTrader gives you
two engines. They drift — a rounding difference, a bar-close convention, one bug
fixed on one side. Then the channel calls trades cTrader never took, and you
find out from a follower.

So the strategy lives in exactly one place: the cBot. It writes down what it
did. The terminal program reads that and posts it. It contains no strategy at
all — it cannot form an opinion about the market, so it cannot disagree with
cTrader about one.

`tools/verify/bridge-test.sh` runs the real cBot against a simulated broker and
checks that every take profit, stop, side and rung in the file matches the order
the broker actually received, then breaks the emitter ten different ways and
requires each break to be caught.

## Setting it up

**1. Telegram bot.** Message `@BotFather`, send `/newbot`, pick a name. It
replies with a token like `8123456789:AAF...`.

That token is a credential. It goes in a gitignored file and nowhere else — not
in a commit, not in a screenshot, not pasted into a chat. `goldsignals.py`
strips it from everything it prints.

**2. Add the bot to your channel as an ADMIN.** A bot cannot post to a channel
it is only a member of.

**3. Find the channel id.** Post anything in the channel, forward that message
to `@userinfobot`, and read the id — it looks like `-1001234567890`.

**4. Write the config.** Copy the template and fill it in:

```bash
cp data/telegram_config.example.json data/telegram_config.json
```

```json
{
  "bot_token": "8123456789:AAF...",
  "chat_id": "-1001234567890",
  "feed": "~/GoldICT/signals.jsonl"
}
```

**5. Turn the feed on in cTrader.** GoldICT's parameters, group *Signal feed*:
`Write the signal feed` = true. Leave the path blank and it writes to
`~/GoldICT/signals.jsonl`.

**6. Run it.**

```bash
python3 tools/goldsignals.py --dry-run     # prints, posts nothing — do this first
python3 tools/goldsignals.py               # posts for real
```

Leave it running in a terminal alongside cTrader. It starts at the END of the
feed, so switching it on does not dump history into the channel.

## What gets posted

| event | when |
|---|---|
| `setup` | a level is armed and price has not returned to it yet — the "get ready" |
| `entry` | an order actually filled, with all three take profits and the stop |
| `tp` | one of the three take profits filled — says which |
| `sl` | the stop was hit |
| `close` | the trailing stop caught it in profit, or the time limit closed it |
| `news` | a headline scored above the alert threshold |
| `vacuum` | a news window armed the vacuum-block rule |
| `guard` | the day guard fired — no more entries today |
| `heartbeat` | the periodic market update |
| `start` / `stop` | the bot came up or went down |

Post only some of them with `--only entry,tp,sl,setup`.

The signal itself:

```
Buy gold
Entry- 4301.20 - 4301.85
Tp 1: 4308.02
Tp2: 4314.19
Tp3: 4320.36
Sl 🛑: 4289.55

Utilize risk management techniques to protect capital.
Demo account — simulated fills.
https://www.tradingview.com/chart/?symbol=OANDA%3AXAUUSD
```

The entry is a range because there genuinely is one: the level the model waited
for, and the price the order filled at. When they match to the cent, one number
is printed — an invented range would send followers to a limit that never fills.

**The method never goes out.** The model name and the reason for the trade stay
in the cTrader log and in the feed, where the ledger needs them; the channel
gets the trade and the risk line. Nobody following a signal needs the method,
and a signal that explains itself invites arguing with it.

That is a rendering rule, and rendering rules rot the first time somebody edits
a formatter without thinking about it — so `leaks_method()` lists the words that
must never appear, every formatter is tested against loaded input containing all
of them, and three negative controls prove those tests can fail. One real leak
was found this way: the day-guard message used to echo a free-text field
straight from the feed.

## TradingView

Read this before asking for it again, because the honest answer matters.

**TradingView has no API that lets a program watch a chart and read setups off
it.** There is no endpoint that logs in and looks. What exists:

1. **Alerts with a webhook.** TradingView pushes to a URL you own when a
   condition fires. The condition is a Pine script — an indicator.
2. **Scraping their private websocket.** Against their terms, breaks without
   notice, and it would be a *second opinion about the market* that can disagree
   with cTrader — the exact thing this design prevents.

And the part that matters more than either: **the setups do not need
TradingView.** A chart is a picture of price. GoldICT already reads the same
price from your broker's own feed, at the tick, which is closer to the market
than a TradingView chart, not further from it. Adding TradingView would add a
second data source, not sight.

What is supported, because it is real:

```bash
python3 tools/goldsignals.py --tradingview-port 8787
```

If you ever do set an alert on TradingView — a level crossed, a session open, a
release — point its webhook at that port and the text lands in the channel as a
market update. It never opens or closes anything. TradingView would need to
reach your Mac from the internet, so use a tunnel (ngrok, Cloudflare Tunnel)
rather than opening your router.

Every signal also carries a TradingView chart link, so anyone in the channel can
open the chart at the right symbol in one tap.

## If the channel goes quiet

- Is cTrader still running, and is GoldICT still on the chart? The feed only
  grows while the cBot is alive.
- `tail -f ~/GoldICT/signals.jsonl` — if lines are arriving there, the problem
  is the poster; if not, it is the cBot.
- Check the cTrader log for `Signal feed DISABLED` — the cBot never lets a file
  error stop it trading, so it disables the feed and carries on.
- Run with `--dry-run` to see what it would post without touching Telegram.

# RUNBOOK — July 22: start the demo from your MacBook

Everything below is copy-paste. Total time: ~10 minutes the first day,
one command every day after.

## Before the laptop (do these from your phone, any time)

1. **MetaApi** (free): sign up at https://app.metaapi.cloud →
   Trading accounts → Add account → MT5, login `109508768`,
   password `-c2hUsEq`, server `MetaQuotes-Demo` → **Deploy**.
   Copy the account's **id** (uuid) and create an **API token**.
2. **Telegram**: already configured (bot @xau99AO_Bot, chat id saved).

## Day one — on the MacBook

```bash
# 1. get the bot
git clone https://github.com/aosman999/Memcoin-trader.git
cd Memcoin-trader
git checkout claude/memecoin-trader-ai-39lzel

# 2. credentials (gitignored files — create them locally)
mkdir -p data
cat > data/metaapi_config.json <<'EOF'
{"token": "PASTE_METAAPI_TOKEN", "account_id": "PASTE_ACCOUNT_UUID",
 "symbol": "XAUUSD", "region": "london"}
EOF
cat > data/telegram_config.json <<'EOF'
{"bot_token": "8869631014:AAEgE72nUfTjjnykEWDL4U5Pj_rBuBsB15o", "chat_id": 6506163554}
EOF

# 3. verify EVERYTHING before the first trade
python3 -m goldtrader preflight

# 4. trade (8-hour session; every order carries broker-side SL/TP)
python3 -m goldtrader mac --minutes 480
```

`preflight` must print **ALL CLEAR**. If any line says FAIL, fix that
line first (it names the file or feed).

## Every day after

```bash
cd Memcoin-trader && git pull && python3 -m goldtrader mac --minutes 480
```

Or run a full week unattended (Mac must stay awake — System Settings →
Battery → prevent sleeping on power, or run `caffeinate` in another tab):

```bash
nohup python3 -m goldtrader mac --minutes 7200 > bot.log 2>&1 &
tail -f bot.log
```

Stop the bot: `pkill -f goldtrader`. Open positions keep their
broker-side stop and target either way.

## What you'll see

- **Telegram**: session start, every order (side/lots/SL/TP), daily
  loss-stop alerts, and a daily PnL report at midnight UTC.
- **MT5 app** (Mac or phone, logged into the same demo): every trade
  live on the chart.
- **Terminal**: the full decision log.

## What's running

The certified champion (docs/PERFORMANCE.md): 4 strategies including
the mentor liquidity-sweep (live debut), 10% risk per trade with
broker-side 2:1 stops, session/sentinel/news/calendar gates, Valentini
3-loss daily stop, −15% day guard, demo-only + small-account guards.

## Windows alternative

Same flow with `pip install MetaTrader5`, `data/mt5_config.json`
(login/password/server), and `python3 -m goldtrader mt5` —
see docs/GOLD.md.

# GO-LIVE CHECKLIST — Wednesday, July 22

Work through this top to bottom. Total: ~15 minutes.

## 1. Accounts (can be done from your phone before the laptop)
- [ ] MT5 demo is the $3,000 / 1:200 account (login 109508768 on
      MetaQuotes-Demo — password in your records)
- [ ] metaapi.cloud: account added with those MT5 credentials → **Deploy**
      → copy the **account id** (uuid) and create an **API token**
- [ ] Telegram bot @xau99AO_Bot exists; you have the token and chat id

## 2. On the MacBook (Terminal)
```bash
git clone https://github.com/aosman999/Memcoin-trader.git
cd Memcoin-trader
git checkout claude/memecoin-trader-ai-39lzel

mkdir -p data
# paste your real values into these two files:
cat > data/metaapi_config.json <<'EOF'
{"token": "METAAPI_TOKEN", "account_id": "ACCOUNT_UUID",
 "symbol": "XAUUSD", "region": "london"}
EOF
cat > data/telegram_config.json <<'EOF'
{"bot_token": "TELEGRAM_BOT_TOKEN", "chat_id": TELEGRAM_CHAT_ID}
EOF
```

## 3. Verify before the first trade
```bash
python3 -m goldtrader preflight
```
Everything must be ✓. If the symbol check fails, ask me — some servers
name gold `GOLD` or `XAUUSD.a`; change the `symbol` field to match.

## 4. Start trading (supervised — restarts itself, alerts your Telegram)
```bash
python3 -m goldtrader mac --minutes 480 --forever
```
Expected within a minute: a 🥇 "connected" message on Telegram, and the
account visible as trading in your MT5 app (comment `gold:...` on orders).

## 5. What to expect (calibrated honestly — docs/PERFORMANCE.md)
- Trades carry broker-side SL (−0.6%) and TP (+1.2%); ~2–5 trades/day
- Wins ≈ +20% of account each, losses ≈ −10%; red days stop at −15%
- Roughly half of days red is NORMAL; judge the WEEK, not the day
- Cross-model calibration says a fair two-week expectation is between
  break-even and +100%; anything consistently above that is the live
  market being kinder than both models
- Every trade + daily PnL lands on your Telegram; I compare live vs
  certified baseline daily and tune ONLY from live evidence

## 6. If something goes wrong
- Bot crashed? The supervisor restarts it and Telegrams you; 5 crashes
  in 10 min = it stops with 🛑 — paste me the terminal output
- Laptop must sleep? Positions are safe (stops live at the broker);
  restart the command when back
- Want it quiet? Ctrl-C stops entries; open positions keep broker SL/TP

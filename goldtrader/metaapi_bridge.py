"""MetaApi bridge — trade the MT5 DEMO account from ANY OS (macOS/Linux).

MetaQuotes' python package is Windows-only; MetaApi (metaapi.cloud) hosts
the MT5 connection in their cloud and exposes a REST API, so the bot can
run from a MacBook terminal.

Setup (~5 minutes, free tier):
  1. Sign up at https://app.metaapi.cloud
  2. "Trading accounts" -> add account: paste your MT5 DEMO login,
     password and server (e.g. MetaQuotes-Demo), type MT5 -> Deploy
  3. Copy the ACCOUNT ID (uuid shown on the account) and create an API
     TOKEN (top-right menu -> API tokens)
  4. Create data/metaapi_config.json (gitignored):
       {"token": "eyJ...", "account_id": "xxxxxxxx-...",
        "symbol": "XAUUSD", "region": "london"}
  5. python3 -m goldtrader mac --minutes 480

This bridge runs the SAME certified EntryPipeline as the simulator
(live_core.py) — indicator confluence, session/vol/mentor gates and
sizing scalars included — plus live-only rails: restart-proof daily
loss stop, broker-measured 3-loss discipline (own trades only),
fill-confirmation window (no double positions), weekend guard, and a
demo-only account check that FAILS CLOSED.
"""
from __future__ import annotations

import json
import os
import time
import urllib.request

from .config import DATA_DIR, GoldParams, load_json_config
from .live_core import (EntryPipeline, LiveDayState, apply_live_profile,
                        market_closed, utc_day)
from .mentors import MentorDiscipline
from .telegram import send as tg_send

METAAPI_CONFIG_PATH = os.path.join(DATA_DIR, "metaapi_config.json")
OZ_PER_LOT = 100.0
_TIMEOUT = 15
FILL_CONFIRM_SECONDS = 90     # after an order: assume a position exists until
                              # the cloud replica confirms (prevents doubles)
LOSSES_CACHE_SECONDS = 60     # throttle full-history fetches


class MetaApi:
    def __init__(self, cfg: dict):
        self.token = cfg["token"]
        self.account = cfg["account_id"]
        region = cfg.get("region", "london")
        self.base = (f"https://mt-client-api-v1.{region}.agiliumtrade.ai"
                     f"/users/current/accounts/{self.account}")

    def _req(self, path: str, method: str = "GET", body: dict | None = None):
        req = urllib.request.Request(
            self.base + path, method=method,
            headers={"auth-token": self.token, "Content-Type": "application/json"},
            data=json.dumps(body).encode() if body is not None else None)
        try:
            with urllib.request.urlopen(req, timeout=_TIMEOUT) as r:
                raw = r.read()
                return json.loads(raw) if raw else {}
        except urllib.error.HTTPError as e:
            print(f"  metaapi {method} {path} -> HTTP {e.code}: "
                  f"{e.read().decode()[:200]}")
            return None
        except Exception as e:
            print(f"  metaapi {method} {path} -> {e}")
            return None

    def account_information(self) -> dict | None:
        return self._req("/account-information")

    def price(self, symbol: str) -> dict | None:
        return self._req(f"/symbols/{symbol}/current-price")

    def positions(self) -> list:
        out = self._req("/positions")
        return out if isinstance(out, list) else []

    def market_order(self, symbol: str, is_buy: bool, volume: float,
                     sl: float, tp: float, comment: str) -> dict | None:
        return self._req("/trade", "POST", {
            "actionType": "ORDER_TYPE_BUY" if is_buy else "ORDER_TYPE_SELL",
            "symbol": symbol, "volume": round(volume, 2),
            "stopLoss": round(sl, 2), "takeProfit": round(tp, 2),
            "comment": comment[:26],
        })

    def close_position(self, position_id: str) -> dict | None:
        return self._req("/trade", "POST", {
            "actionType": "POSITION_CLOSE_ID", "positionId": position_id})

    def losses_today(self) -> int:
        """Closed losing deals since UTC midnight — THIS BOT'S deals only
        (comment-tagged), so other EAs/manual trades never trip the
        discipline stop. Fail-soft to 0."""
        import datetime as dt
        start = dt.datetime.utcnow().strftime("%Y-%m-%dT00:00:00.000Z")
        end = dt.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%S.000Z")
        deals = self._req(f"/history-deals/time/{start}/{end}")
        if not isinstance(deals, list):
            return 0
        return sum(1 for d in deals
                   if isinstance(d, dict)
                   and d.get("entryType") == "DEAL_ENTRY_OUT"
                   and str(d.get("comment", "")).startswith("gold")
                   and float(d.get("profit", 0) or 0) < 0)


def _load_config() -> dict:
    cfg = load_json_config(METAAPI_CONFIG_PATH)
    if not cfg or not cfg.get("token") or not cfg.get("account_id"):
        raise SystemExit(
            f"missing or invalid {METAAPI_CONFIG_PATH} — see docs/GOLD.md "
            "(MetaApi setup):\n  {\"token\": \"...\", \"account_id\": \"...\", "
            "\"symbol\": \"XAUUSD\", \"region\": \"london\"}")
    return cfg


def run_mac(minutes: float = 480.0, poll_seconds: float = 15.0) -> None:
    cfg = _load_config()
    params = apply_live_profile(GoldParams.load())
    symbol = cfg.get("symbol", "XAUUSD")
    api = MetaApi(cfg)

    acct = api.account_information()
    if acct is None:
        raise SystemExit("could not reach MetaApi — check token/account_id/"
                         "region and that the account is DEPLOYED")
    acct_type = str(acct.get("type", "")).lower()
    trade_mode = str(acct.get("tradeMode", "")).lower()
    # FAIL CLOSED: trade only when the account is provably demo/contest
    if not any(k in acct_type or k in trade_mode for k in ("demo", "contest")):
        raise SystemExit(f"REFUSING to run: account type '{acct_type or trade_mode or 'unknown'}' "
                         "is not provably a demo account.")
    equity0 = float(acct.get("equity", 0) or 0)
    print(f"connected via MetaApi: {acct.get('broker', '?')} "
          f"#{acct.get('login', '?')} ({acct.get('type', '?')}) — "
          f"equity {equity0:,.2f} {acct.get('currency', 'USD')}")
    tg_send(f"🥇 Gold bot connected via MetaApi (Mac): equity "
            f"{equity0:,.2f}. Session started.")

    pipeline = EntryPipeline(params)
    day = LiveDayState(equity0, time.time())
    pipeline.gov_red_streak = day.red_streak
    prices: list[float] = []
    end = time.time() + minutes * 60
    last_bar_minute = int(time.time() // 60)
    pending_fill_until = 0.0
    losses_cache = (0.0, 0)      # (fetched_at, count)

    while time.time() < end:
        now = time.time()
        if market_closed(now):
            time.sleep(min(300.0, poll_seconds * 4))
            continue

        quote = api.price(symbol)
        if not quote:
            time.sleep(poll_seconds)
            continue
        bid = float(quote.get("bid", 0) or 0)
        ask = float(quote.get("ask", 0) or 0)
        if bid <= 0 or ask <= 0 or ask < bid:
            time.sleep(poll_seconds)      # malformed quote: never trade on it
            continue
        mid = (bid + ask) / 2.0
        this_minute = int(now // 60)
        if this_minute != last_bar_minute:
            prices.append(mid)
            prices = prices[-1600:]
            last_bar_minute = this_minute

        info = api.account_information() or {}
        equity = float(info.get("equity", 0) or 0) or day.day_start_equity

        prev_day, prev_start = day.day, day.day_start_equity
        if day.roll_if_new_day(equity, now):
            pipeline.gov_red_streak = day.red_streak
            if prev_start > 0:
                pnl = equity - prev_start
                tg_send(f"🥇 Daily report {prev_day}: {pnl:+,.2f} USD "
                        f"({pnl / prev_start * 100:+.2f}%). Equity now "
                        f"{equity:,.2f}. New UTC day {day.day}; governor "
                        f"red-streak {day.red_streak}")
        day.note_equity(equity)

        if not day.halted and day.loss_stop_hit(equity, params.risk.daily_loss_limit):
            for pos in api.positions():
                api.close_position(str(pos.get("id")))
            day.halt(equity)
            print(f"-- DAILY LOSS STOP at {equity:,.2f}; flat until tomorrow")
            tg_send(f"⚠️ Gold bot: daily loss stop at {equity:,.2f}. "
                    f"Flat until tomorrow (UTC).")

        open_positions = [p for p in api.positions()
                          if p.get("symbol") == symbol]
        have_position = bool(open_positions) or now < pending_fill_until

        if not day.halted and not have_position and len(prices) > 130:
            # broker-measured discipline count (own trades only), cached
            if now - losses_cache[0] > LOSSES_CACHE_SECONDS:
                losses_cache = (now, api.losses_today())
            plan = pipeline.evaluate(prices, mid, now,
                                     losses_today=losses_cache[1])
            if plan is not None:
                r = params.risk
                stop_frac = plan.stop_frac if plan.stop_frac else r.stop_loss
                rr = (r.tp_multiples[0] - 1.0) / r.stop_loss
                stop_dist = mid * stop_frac
                tp_dist = mid * rr * stop_frac
                risk_usd = equity * r.risk_per_trade * max(0.1, min(1.5, plan.risk_scale))
                notional = min(risk_usd / stop_frac, equity * params.max_leverage)
                # account-too-small guard: min lot must not force >2x risk
                min_lot_risk = 0.01 * OZ_PER_LOT * mid * stop_frac
                if min_lot_risk > equity * r.risk_per_trade * 2:
                    print(f"  SKIP: account too small — min lot risks "
                          f"${min_lot_risk:.2f}/trade. Fund to "
                          f"~${min_lot_risk / r.risk_per_trade:,.0f}+.")
                else:
                    lots = max(0.01, round(notional / (mid * OZ_PER_LOT), 2))
                    is_buy = plan.direction > 0
                    px = ask if is_buy else bid
                    res = api.market_order(
                        symbol, is_buy, lots,
                        sl=px - plan.direction * stop_dist,
                        tp=px + plan.direction * tp_dist,
                        comment=f"gold:{plan.strategy}"[:26])
                    if res is not None:
                        pending_fill_until = now + FILL_CONFIRM_SECONDS
                        losses_cache = (0.0, losses_cache[1])  # refresh soon
                        side = "BUY" if is_buy else "SELL"
                        print(f"  OPEN [{plan.strategy}] {side} {lots} lots @ "
                              f"{px:.2f} — {plan.reason}")
                        tg_send(f"🥇 OPEN {side} {lots} lots {symbol} @ {px:.2f} "
                                f"[{plan.strategy}]")
        time.sleep(poll_seconds)

    info = api.account_information() or {}
    final_eq = float(info.get("equity", 0) or 0)
    ref = day.day_start_equity if day.day_start_equity > 0 else (final_eq or 1.0)
    tg_send(f"🥇 Gold bot session over. Equity {final_eq:,.2f} "
            f"({(final_eq / ref - 1) * 100:+.2f}% today). "
            f"Open positions keep their broker-side SL/TP.")
    print("session over — positions (with broker-side SL/TP) left to their exits")

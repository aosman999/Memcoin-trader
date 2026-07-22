"""OANDA practice bridge — trade a FREE gold demo from any OS, no fees.

MetaApi began requiring a paid deposit to deploy MT5 accounts (Jul 2026),
so this bridge targets OANDA's v20 REST API instead: practice accounts
get FULL free API access (no card, no deposit) and quote spot gold as
XAU_USD.

Setup (~10 minutes, all free):
  1. Sign up for a practice account at https://www.oanda.com
     (choose fxTrade Practice / demo).
  2. Log in -> Account settings -> "Manage API Access" -> generate a
     personal access token.
  3. Find your v20 account id (looks like 101-004-1234567-001) under
     My Services / My Funds.
  4. Create data/oanda_config.json (gitignored):
       {"api_token": "...", "account_id": "101-...", "instrument": "XAU_USD"}
  5. python3 -m goldtrader oanda --forever

DEMO-ONLY, FAIL-CLOSED: the practice API host is hard-coded and the
bridge refuses account ids that are not practice-format (101-...).
There is no code path to a live OANDA account.

Runs the SAME certified EntryPipeline as the simulator and the other
bridges (live_core.py) with the same live rails: restart-proof daily
loss stop, broker-measured 3-loss discipline (own trades only),
fill-confirmation window, weekend guard.
"""
from __future__ import annotations

import datetime as _dt
import json
import os
import time
import urllib.parse
import urllib.request

from .config import DATA_DIR, GoldParams, load_json_config
from .live_core import (EntryPipeline, LiveDayState, apply_live_profile,
                        market_closed)
from .telegram import send as tg_send

OANDA_CONFIG_PATH = os.path.join(DATA_DIR, "oanda_config.json")
PRACTICE_HOST = "https://api-fxpractice.oanda.com"   # NEVER the live host
_TIMEOUT = 15
FILL_CONFIRM_SECONDS = 90
LOSSES_CACHE_SECONDS = 60


class OandaPractice:
    """Thin v20 REST client, practice host only."""

    def __init__(self, cfg: dict):
        account = str(cfg["account_id"])
        if not account.startswith("101-"):
            raise SystemExit(
                f"REFUSING to run: '{account}' is not an OANDA PRACTICE "
                "account id (they start with 101-). This bridge is demo-only.")
        self.token = cfg["api_token"]
        self.account = account
        self.base = f"{PRACTICE_HOST}/v3/accounts/{self.account}"

    def _req(self, path: str, method: str = "GET", body: dict | None = None):
        req = urllib.request.Request(
            self.base + path, method=method,
            headers={"Authorization": f"Bearer {self.token}",
                     "Content-Type": "application/json"},
            data=json.dumps(body).encode() if body is not None else None)
        try:
            with urllib.request.urlopen(req, timeout=_TIMEOUT) as r:
                raw = r.read()
                return json.loads(raw) if raw else {}
        except urllib.error.HTTPError as e:
            print(f"  oanda {method} {path} -> HTTP {e.code}: "
                  f"{e.read().decode()[:200]}")
            return None
        except Exception as e:
            print(f"  oanda {method} {path} -> {e}")
            return None

    def summary(self) -> dict | None:
        out = self._req("/summary")
        return out.get("account") if isinstance(out, dict) else None

    def margin_rate(self, instrument: str) -> float:
        out = self._req("/instruments?" + urllib.parse.urlencode(
            {"instruments": instrument}))
        try:
            return float(out["instruments"][0]["marginRate"])
        except (TypeError, KeyError, IndexError, ValueError):
            return 0.05                      # OANDA's usual gold margin (20:1)

    def price(self, instrument: str) -> tuple[float, float] | None:
        out = self._req("/pricing?" + urllib.parse.urlencode(
            {"instruments": instrument}))
        try:
            p = out["prices"][0]
            return float(p["bids"][0]["price"]), float(p["asks"][0]["price"])
        except (TypeError, KeyError, IndexError, ValueError):
            return None

    def open_trades(self, instrument: str) -> list:
        out = self._req("/openTrades")
        trades = out.get("trades", []) if isinstance(out, dict) else []
        return [t for t in trades if t.get("instrument") == instrument]

    def market_order(self, instrument: str, units: int,
                     sl: float, tp: float, comment: str) -> dict | None:
        return self._req("/orders", "POST", {"order": {
            "type": "MARKET", "instrument": instrument,
            "units": str(units), "timeInForce": "FOK",
            "positionFill": "DEFAULT",
            "stopLossOnFill": {"price": f"{sl:.3f}"},
            "takeProfitOnFill": {"price": f"{tp:.3f}"},
            "clientExtensions": {"tag": "gold", "comment": comment[:120]},
        }})

    def close_trade(self, trade_id: str) -> dict | None:
        return self._req(f"/trades/{trade_id}/close", "PUT", {})

    def losses_today(self) -> int:
        """Closed losing trades since UTC midnight, THIS BOT'S only
        (tag 'gold'). Fail-soft to 0."""
        out = self._req("/trades?" + urllib.parse.urlencode(
            {"state": "CLOSED", "count": 100}))
        trades = out.get("trades", []) if isinstance(out, dict) else []
        midnight = _dt.datetime.utcnow().strftime("%Y-%m-%dT00:00:00")
        n = 0
        for t in trades:
            ext = t.get("clientExtensions") or {}
            if (ext.get("tag") == "gold"
                    and str(t.get("closeTime", "")) >= midnight
                    and float(t.get("realizedPL", 0) or 0) < 0):
                n += 1
        return n


def size_units(equity: float, price: float, stop_frac: float,
               risk_scale: float, risk_per_trade: float,
               max_leverage: float, margin_available: float,
               margin_rate: float) -> int:
    """Whole ounces to trade: risk-based notional, capped by the account
    leverage rule AND the broker's real margin. 0 = do not trade."""
    if price <= 0 or stop_frac <= 0 or equity <= 0:
        return 0
    risk_usd = equity * risk_per_trade * max(0.1, min(1.5, risk_scale))
    notional = min(risk_usd / stop_frac, equity * max_leverage)
    if margin_rate > 0 and margin_available > 0:
        notional = min(notional, margin_available * 0.95 / margin_rate)
    units = int(notional / price)
    # account-too-small guard: 1 oz must not force >2x the risk budget
    if units < 1 or price * stop_frac > equity * risk_per_trade * 2:
        return 0
    return units


def _load_config() -> dict:
    cfg = load_json_config(OANDA_CONFIG_PATH)
    if not cfg or not cfg.get("api_token") or not cfg.get("account_id"):
        raise SystemExit(
            f"missing or invalid {OANDA_CONFIG_PATH} — see the module "
            "docstring:\n  {\"api_token\": \"...\", \"account_id\": "
            "\"101-...\", \"instrument\": \"XAU_USD\"}")
    return cfg


def run_oanda(minutes: float = 480.0, poll_seconds: float = 15.0) -> None:
    cfg = _load_config()
    params = apply_live_profile(GoldParams.load())
    instrument = cfg.get("instrument", "XAU_USD")
    api = OandaPractice(cfg)

    acct = api.summary()
    if acct is None:
        raise SystemExit("could not reach OANDA practice API — check "
                         "api_token/account_id in data/oanda_config.json")
    equity0 = float(acct.get("NAV", 0) or acct.get("balance", 0) or 0)
    mrate = api.margin_rate(instrument)
    print(f"connected to OANDA practice: {acct.get('alias', '?')} "
          f"({api.account}) — equity {equity0:,.2f} "
          f"{acct.get('currency', 'USD')}, {instrument} margin "
          f"{mrate:.0%} (max {1 / mrate:.0f}x)" if mrate else "")
    tg_send(f"🥇 Gold bot connected to OANDA practice: equity "
            f"{equity0:,.2f}. Session started.")

    pipeline = EntryPipeline(params)
    day = LiveDayState(equity0, time.time())
    pipeline.gov_red_streak = day.red_streak
    prices: list[float] = []
    end = time.time() + minutes * 60
    last_bar_minute = int(time.time() // 60)
    pending_fill_until = 0.0
    losses_cache = (0.0, 0)

    while time.time() < end:
        now = time.time()
        if market_closed(now):
            time.sleep(min(300.0, poll_seconds * 4))
            continue

        quote = api.price(instrument)
        if not quote:
            time.sleep(poll_seconds)
            continue
        bid, ask = quote
        if bid <= 0 or ask <= 0 or ask < bid:
            time.sleep(poll_seconds)
            continue
        mid = (bid + ask) / 2.0
        this_minute = int(now // 60)
        if this_minute != last_bar_minute:
            prices.append(mid)
            prices = prices[-1600:]
            last_bar_minute = this_minute

        info = api.summary() or {}
        equity = (float(info.get("NAV", 0) or 0)
                  or float(info.get("balance", 0) or 0)
                  or day.day_start_equity)

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
            for t in api.open_trades(instrument):
                api.close_trade(str(t.get("id")))
            day.halt(equity)
            print(f"-- DAILY LOSS STOP at {equity:,.2f}; flat until tomorrow")
            tg_send(f"⚠️ Gold bot: daily loss stop at {equity:,.2f}. "
                    f"Flat until tomorrow (UTC).")

        have_position = bool(api.open_trades(instrument)) \
            or now < pending_fill_until

        if not day.halted and not have_position and len(prices) > 130:
            if now - losses_cache[0] > LOSSES_CACHE_SECONDS:
                losses_cache = (now, api.losses_today())
            plan = pipeline.evaluate(prices, mid, now,
                                     losses_today=losses_cache[1])
            if plan is not None:
                r = params.risk
                stop_frac = plan.stop_frac if plan.stop_frac else r.stop_loss
                rr = (r.tp_multiples[0] - 1.0) / r.stop_loss
                margin_avail = float(info.get("marginAvailable", 0) or 0)
                units = size_units(equity, mid, stop_frac, plan.risk_scale,
                                   r.risk_per_trade, params.max_leverage,
                                   margin_avail, mrate)
                if units == 0:
                    print("  SKIP: account too small or margin exhausted "
                          "for 1 oz at the risk rule")
                else:
                    is_buy = plan.direction > 0
                    px = ask if is_buy else bid
                    stop_dist = mid * stop_frac
                    tp_dist = mid * rr * stop_frac
                    res = api.market_order(
                        instrument, units if is_buy else -units,
                        sl=px - plan.direction * stop_dist,
                        tp=px + plan.direction * tp_dist,
                        comment=f"gold:{plan.strategy}")
                    if res is not None:
                        pending_fill_until = now + FILL_CONFIRM_SECONDS
                        losses_cache = (0.0, losses_cache[1])
                        side = "BUY" if is_buy else "SELL"
                        print(f"  OPEN [{plan.strategy}] {side} {units} oz @ "
                              f"{px:.2f} — {plan.reason}")
                        tg_send(f"🥇 OPEN {side} {units} oz {instrument} @ "
                                f"{px:.2f} [{plan.strategy}]")
        time.sleep(poll_seconds)

    info = api.summary() or {}
    final_eq = float(info.get("NAV", 0) or 0)
    ref = day.day_start_equity if day.day_start_equity > 0 else (final_eq or 1.0)
    tg_send(f"🥇 Gold bot session over. Equity {final_eq:,.2f} "
            f"({(final_eq / ref - 1) * 100:+.2f}% today). "
            f"Open positions keep their broker-side SL/TP.")
    print("session over — positions (with broker-side SL/TP) left to their exits")

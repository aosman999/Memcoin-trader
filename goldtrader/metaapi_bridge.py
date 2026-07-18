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

Same brain as the Windows bridge: risk-based sizing, SL/TP attached to
every order, one position at a time, -15% daily loss stop, Telegram
alerts. Refuses accounts whose type is not demo.
"""
from __future__ import annotations

import json
import os
import time
import urllib.request

from .config import DATA_DIR

from .agents import EventSentinel, SessionAgent
from .config import GoldParams
from .strategies import ALL_STRATEGIES
from .telegram import send as tg_send

METAAPI_CONFIG_PATH = os.path.join(DATA_DIR, "metaapi_config.json")
OZ_PER_LOT = 100.0
_TIMEOUT = 15


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
        return self._req("/positions") or []

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
        """Closed losing deals since midnight UTC (fail-soft to 0)."""
        import datetime as dt
        start = dt.datetime.utcnow().strftime("%Y-%m-%dT00:00:00.000Z")
        end = dt.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%S.000Z")
        deals = self._req(f"/history-deals/time/{start}/{end}") or []
        if not isinstance(deals, list):
            return 0
        return sum(1 for d in deals
                   if isinstance(d, dict) and d.get("entryType") == "DEAL_ENTRY_OUT"
                   and float(d.get("profit", 0)) < 0)


def _load_config() -> dict:
    if not os.path.exists(METAAPI_CONFIG_PATH):
        raise SystemExit(
            f"missing {METAAPI_CONFIG_PATH} — see docs/GOLD.md (MetaApi "
            "setup):\n  {\"token\": \"...\", \"account_id\": \"...\", "
            "\"symbol\": \"XAUUSD\", \"region\": \"london\"}")
    with open(METAAPI_CONFIG_PATH) as f:
        return json.load(f)


def run_mac(minutes: float = 480.0, poll_seconds: float = 15.0) -> None:
    cfg = _load_config()
    params = GoldParams.load()
    params.use_mentors = True    # live market: sweep strategy on its proving ground
    params.use_discipline = True # Valentini 3-loss daily stop, live protection
    symbol = cfg.get("symbol", "XAUUSD")
    api = MetaApi(cfg)

    acct = api.account_information()
    if acct is None:
        raise SystemExit("could not reach MetaApi — check token/account_id/"
                         "region and that the account is DEPLOYED")
    acct_type = str(acct.get("type", "")).lower()
    trade_mode = str(acct.get("tradeMode", "")).lower()
    # conservative: run ONLY when the account is explicitly demo/contest —
    # an unknown or real type refuses. (Caught by mock-broker tests: the
    # old prefix check missed MetaApi's ACCOUNT_TRADE_MODE_REAL format.)
    if not any(k in acct_type or k in trade_mode for k in ("demo", "contest")):
        raise SystemExit(f"REFUSING to run: account type '{acct.get('type')}' "
                         "is not explicitly demo. This bridge only trades "
                         "demo accounts.")
    print(f"connected via MetaApi: {acct.get('broker', '?')} "
          f"#{acct.get('login', '?')} ({acct.get('type', '?')}) — "
          f"equity {acct.get('equity', 0):,.2f} {acct.get('currency', 'USD')}")
    tg_send(f"🥇 Gold bot connected via MetaApi (Mac): equity "
            f"{acct.get('equity', 0):,.2f}. Session started.")

    from .mentors import MentorDiscipline
    from .news_agent import NewsAgent
    from .strategies import mentor_sweep_signal
    session, sentinel, news = SessionAgent(), EventSentinel(), NewsAgent(enabled=True)
    discipline = MentorDiscipline()
    prices: list[float] = []
    end = time.time() + minutes * 60
    last_bar_minute = int(time.time() // 60)
    day_start_equity = float(acct.get("equity", 0))
    day_key = time.strftime("%Y-%m-%d")
    halted_for_day = False

    while time.time() < end:
        quote = api.price(symbol)
        if not quote:
            time.sleep(poll_seconds)
            continue
        bid, ask = float(quote.get("bid", 0)), float(quote.get("ask", 0))
        if bid <= 0:
            time.sleep(poll_seconds)
            continue
        mid = (bid + ask) / 2.0
        this_minute = int(time.time() // 60)
        if this_minute != last_bar_minute:
            prices.append(mid)
            prices = prices[-1600:]
            last_bar_minute = this_minute

        info = api.account_information() or {}
        equity = float(info.get("equity", day_start_equity))

        today = time.strftime("%Y-%m-%d")
        if today != day_key:
            tg_send(f"🥇 Gold bot daily report {day_key}: "
                    f"{day_start_equity:,.2f} → {equity:,.2f} "
                    f"({(equity / day_start_equity - 1) * 100:+.2f}%)")
            day_key = today
            day_start_equity = equity
            halted_for_day = False

        if not halted_for_day and equity <= day_start_equity * (1 - params.risk.daily_loss_limit):
            for pos in api.positions():
                api.close_position(str(pos.get("id")))
            halted_for_day = True
            print(f"-- DAILY LOSS STOP at {equity:,.2f}; flat until tomorrow")
            tg_send(f"⚠️ Gold bot: daily loss stop at {equity:,.2f}. "
                    f"Flat until tomorrow.")

        open_positions = [p for p in api.positions()
                          if p.get("symbol") == symbol]
        agents_ok = (not params.use_agents
                     or (sentinel.check(prices + [mid], time.time())
                         and session.tradeable(time.time())))
        news.update(time.time())
        mentor_ok = (not params.use_discipline
                     or api.losses_today() < MentorDiscipline.MAX_LOSSES_PER_DAY)
        candidates = ([lambda h, p: mentor_sweep_signal(h, p, time.time())]
                      if params.use_mentors else []) + list(ALL_STRATEGIES)
        if not halted_for_day and not open_positions and len(prices) > 130 \
                and agents_ok and mentor_ok:
            for strat in candidates:
                sig = strat(prices, params)
                if sig is None or (sig.direction < 0 and not params.allow_short):
                    continue
                if not news.entry_allowed(sig.direction, time.time()):
                    continue
                r = params.risk
                # account-too-small guard: min lot must not force >2x risk
                min_lot_risk = 0.01 * OZ_PER_LOT * mid * r.stop_loss
                if min_lot_risk > equity * r.risk_per_trade * 2:
                    print(f"  SKIP: account too small — min lot risks "
                          f"${min_lot_risk:.2f}/trade. Fund to "
                          f"~${min_lot_risk / r.risk_per_trade:,.0f}+.")
                    break
                stop_dist = mid * r.stop_loss
                tp_dist = mid * (r.tp_multiples[0] - 1.0)
                notional = min(equity * r.risk_per_trade / r.stop_loss,
                               equity * params.max_leverage)
                lots = max(0.01, round(notional / (mid * OZ_PER_LOT), 2))
                is_buy = sig.direction > 0
                px = ask if is_buy else bid
                res = api.market_order(
                    symbol, is_buy, lots,
                    sl=px - sig.direction * stop_dist,
                    tp=px + sig.direction * tp_dist,
                    comment=f"gold:{sig.strategy}"[:26])
                if res is not None:
                    side = "BUY" if is_buy else "SELL"
                    print(f"  OPEN [{sig.strategy}] {side} {lots} lots @ "
                          f"{px:.2f} — {sig.reason}")
                    tg_send(f"🥇 OPEN {side} {lots} lots {symbol} @ {px:.2f} "
                            f"[{sig.strategy}]")
                break
        time.sleep(poll_seconds)

    info = api.account_information() or {}
    final_eq = float(info.get("equity", 0))
    tg_send(f"🥇 Gold bot session over. Equity {final_eq:,.2f}. "
            f"Open positions keep their broker-side SL/TP.")
    print("session over — positions (with broker-side SL/TP) left to their exits")

"""MetaTrader 5 bridge — trades the SAME certified brain on your MT5 DEMO.

Requirements (Windows only — MetaQuotes' python package needs the
Windows MT5 terminal):
  1. Install MetaTrader 5 from your broker, log into your DEMO account
  2. In MT5: Tools -> Options -> Expert Advisors -> allow algo trading
  3. pip install MetaTrader5
  4. Create data/mt5_config.json:
       {"login": 12345678, "password": "...", "server": "Broker-Demo",
        "symbol": "XAUUSD"}
  5. python3 -m goldtrader mt5 --minutes 480

This bridge runs the SAME certified EntryPipeline as the simulator
(live_core.py) — indicator confluence, session/vol/mentor gates and
sizing scalars included — plus live-only rails: restart-proof daily
loss stop (UTC days), broker-measured 3-loss discipline (this bot's
deals only), fill-confirmation window, weekend guard, and a demo-only
account check that FAILS CLOSED.
"""
from __future__ import annotations

import os
import time

from .config import DATA_DIR, GoldParams, load_json_config
from .live_core import (EntryPipeline, LiveDayState, apply_live_profile,
                        market_closed)
from .telegram import send as tg_send, telegram_enabled

MT5_CONFIG_PATH = os.path.join(DATA_DIR, "mt5_config.json")
OZ_PER_LOT = 100.0
MAGIC = 777001
FILL_CONFIRM_SECONDS = 30      # local terminal reflects fills fast
LOSSES_CACHE_SECONDS = 60


def _load_config() -> dict:
    cfg = load_json_config(MT5_CONFIG_PATH)
    if not cfg:
        raise SystemExit(
            f"missing or invalid {MT5_CONFIG_PATH} — create it with your "
            'DEMO account:\n  {"login": 12345678, "password": "...", '
            '"server": "Broker-Demo", "symbol": "XAUUSD"}')
    return cfg


def _losses_today(mt5) -> int:
    """Closed losing deals since UTC midnight — this bot's deals only
    (magic-tagged). Fail-soft to 0."""
    import datetime
    now = datetime.datetime.utcnow()
    start = datetime.datetime(now.year, now.month, now.day)
    deals = mt5.history_deals_get(start, now) or []
    n = 0
    for d in deals:
        try:
            if (getattr(d, "magic", 0) == MAGIC
                    and getattr(d, "entry", 0) == 1     # DEAL_ENTRY_OUT
                    and getattr(d, "profit", 0) < 0):
                n += 1
        except Exception:
            continue
    return n


def _close_all(mt5, symbol: str) -> None:
    for pos in mt5.positions_get(symbol=symbol) or []:
        tick = mt5.symbol_info_tick(symbol)
        if tick is None:
            continue
        is_long = pos.type == mt5.POSITION_TYPE_BUY
        mt5.order_send({
            "action": mt5.TRADE_ACTION_DEAL,
            "symbol": symbol,
            "volume": pos.volume,
            "type": mt5.ORDER_TYPE_SELL if is_long else mt5.ORDER_TYPE_BUY,
            "position": pos.ticket,
            "price": tick.bid if is_long else tick.ask,
            "deviation": 30,
            "magic": MAGIC,
            "comment": "gold:day_stop",
            "type_time": mt5.ORDER_TIME_GTC,
            "type_filling": mt5.ORDER_FILLING_IOC,
        })


def _place_order(mt5, symbol: str, plan, price: float, equity: float,
                 params: GoldParams, info) -> bool:
    r = params.risk
    stop_frac = plan.stop_frac if plan.stop_frac else r.stop_loss
    rr = (r.tp_multiples[0] - 1.0) / r.stop_loss
    stop_dist = price * stop_frac
    tp_dist = price * rr * stop_frac
    risk_usd = equity * r.risk_per_trade * max(0.1, min(1.5, plan.risk_scale))
    notional = min(risk_usd / stop_frac, equity * params.max_leverage)

    # account-too-small guard: the broker's minimum lot must not force
    # more than 2x the intended risk per trade
    min_lot_risk = info.volume_min * OZ_PER_LOT * price * stop_frac
    if min_lot_risk > equity * r.risk_per_trade * 2:
        print(f"  SKIP: account too small — the minimum lot risks "
              f"${min_lot_risk:.2f}/trade ({min_lot_risk / max(equity, 1e-9):.0%} "
              f"of equity). Fund to ~${min_lot_risk / r.risk_per_trade:,.0f}+.")
        return False

    lots = notional / (price * OZ_PER_LOT)
    step = info.volume_step or 0.01
    lots = max(info.volume_min, round(lots / step) * step)
    lots = min(lots, info.volume_max)

    is_buy = plan.direction > 0
    tick = mt5.symbol_info_tick(symbol)
    if tick is None:
        return False
    px = tick.ask if is_buy else tick.bid
    if px <= 0:
        return False
    request = {
        "action": mt5.TRADE_ACTION_DEAL,
        "symbol": symbol,
        "volume": lots,
        "type": mt5.ORDER_TYPE_BUY if is_buy else mt5.ORDER_TYPE_SELL,
        "price": px,
        "sl": px - plan.direction * stop_dist,
        "tp": px + plan.direction * tp_dist,
        "deviation": 20,
        "magic": MAGIC,
        "comment": f"gold:{plan.strategy}"[:26],
        "type_time": mt5.ORDER_TIME_GTC,
        "type_filling": mt5.ORDER_FILLING_IOC,
    }
    result = mt5.order_send(request)
    side = "BUY" if is_buy else "SELL"
    if result and result.retcode == mt5.TRADE_RETCODE_DONE:
        print(f"  OPEN [{plan.strategy}] {side} {lots} lots {symbol} @ {px:.2f} "
              f"SL {request['sl']:.2f} TP {request['tp']:.2f} — {plan.reason}")
        tg_send(f"🥇 OPEN {side} {lots} lots {symbol} @ {px:.2f} "
                f"(SL {request['sl']:.2f} / TP {request['tp']:.2f}) "
                f"[{plan.strategy}]")
        return True
    code = result.retcode if result else "no result"
    print(f"  order rejected ({code}) — {side} {lots} lots; check margin "
          f"and minimum volume")
    return False


def run_mt5(minutes: float = 480.0, poll_seconds: float = 15.0) -> None:
    try:
        import MetaTrader5 as mt5
    except ImportError:
        raise SystemExit(
            "MetaTrader5 python package not installed (Windows only):\n"
            "  pip install MetaTrader5")

    cfg = _load_config()
    params = apply_live_profile(GoldParams.load())
    symbol = cfg.get("symbol", "XAUUSD")

    if not mt5.initialize():
        raise SystemExit(f"MT5 initialize failed: {mt5.last_error()}")
    if cfg.get("login") and not mt5.login(cfg["login"], password=cfg.get("password", ""),
                                          server=cfg.get("server", "")):
        raise SystemExit(f"MT5 login failed: {mt5.last_error()}")

    acct = mt5.account_info()
    if acct is None:
        raise SystemExit("could not read account info")
    # FAIL CLOSED: only provably-demo/contest accounts may trade.
    # (Standard MetaTrader5 enum values 0=demo, 1=contest, 2=real; getattr
    # keeps the check working under SDK variants missing the constants.)
    allowed_modes = {getattr(mt5, "ACCOUNT_TRADE_MODE_DEMO", 0),
                     getattr(mt5, "ACCOUNT_TRADE_MODE_CONTEST", 1)}
    if acct.trade_mode not in allowed_modes:
        raise SystemExit(f"REFUSING to run: account trade_mode={acct.trade_mode} "
                         "is not provably demo/contest.")
    if not mt5.symbol_select(symbol, True):
        raise SystemExit(f"symbol {symbol} not available on this broker")

    info = mt5.symbol_info(symbol)
    print(f"connected: {acct.server} #{acct.login} (DEMO) — balance "
          f"{acct.balance:.2f} {acct.currency}, {symbol} "
          f"min lot {info.volume_min}, leverage 1:{acct.leverage}")
    if telegram_enabled():
        tg_send(f"🥇 Gold bot connected: {acct.server} #{acct.login} (DEMO), "
                f"equity {acct.equity:,.2f} {acct.currency}. Session started.")

    pipeline = EntryPipeline(params)
    day = LiveDayState(float(acct.equity), time.time())
    pipeline.gov_red_streak = day.red_streak

    prices: list[float] = []
    rates = mt5.copy_rates_from_pos(symbol, mt5.TIMEFRAME_M1, 0, 120)
    if rates is not None:
        prices.extend(float(r["close"]) for r in rates)

    end = time.time() + minutes * 60
    last_bar_minute = int(time.time() // 60)
    pending_fill_until = 0.0
    losses_cache = (0.0, 0)

    while time.time() < end:
        now = time.time()
        if market_closed(now):
            time.sleep(min(300.0, poll_seconds * 4))
            continue
        tick = mt5.symbol_info_tick(symbol)
        if tick is None or tick.bid <= 0 or tick.ask <= 0:
            time.sleep(poll_seconds)
            continue
        mid = (tick.bid + tick.ask) / 2.0
        this_minute = int(now // 60)
        if this_minute != last_bar_minute:
            prices.append(mid)
            prices = prices[-1600:]
            last_bar_minute = this_minute

        acct_now = mt5.account_info()
        if acct_now is None:
            time.sleep(poll_seconds)     # transient terminal disconnect
            continue
        equity = float(acct_now.equity)

        if day.roll_if_new_day(equity, now):
            pipeline.gov_red_streak = day.red_streak
            tg_send(f"🥇 Gold bot new UTC day {day.day}: equity "
                    f"{equity:,.2f}, governor red-streak {day.red_streak}")
        day.note_equity(equity)

        if not day.halted and day.loss_stop_hit(equity, params.risk.daily_loss_limit):
            _close_all(mt5, symbol)
            day.halt(equity)
            print(f"-- DAILY LOSS STOP: equity {equity:.2f}; flat until tomorrow")
            tg_send(f"⚠️ Gold bot: daily loss stop hit at {equity:,.2f}. "
                    f"All positions closed; flat until tomorrow (UTC).")

        open_positions = mt5.positions_get(symbol=symbol) or []
        have_position = bool(open_positions) or now < pending_fill_until

        if not day.halted and not have_position and len(prices) > 130:
            if now - losses_cache[0] > LOSSES_CACHE_SECONDS:
                losses_cache = (now, _losses_today(mt5))
            plan = pipeline.evaluate(prices, mid, now,
                                     losses_today=losses_cache[1])
            if plan is not None and _place_order(mt5, symbol, plan, mid,
                                                 equity, params, info):
                pending_fill_until = now + FILL_CONFIRM_SECONDS
                losses_cache = (0.0, losses_cache[1])
        time.sleep(poll_seconds)

    acct_end = mt5.account_info()
    final_eq = float(acct_end.equity) if acct_end else 0.0
    ref = day.day_start_equity if day.day_start_equity > 0 else (final_eq or 1.0)
    tg_send(f"🥇 Gold bot session over. Equity {final_eq:,.2f} "
            f"({(final_eq / ref - 1) * 100:+.2f}% today). "
            f"Open positions keep their broker-side SL/TP.")
    print("session over — positions (with broker-side SL/TP) left to their exits")
    mt5.shutdown()

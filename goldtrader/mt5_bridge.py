"""MetaTrader 5 bridge — trades the SAME signals on your MT5 DEMO account.

Requirements (Windows only — MetaQuotes' python package needs the
Windows MT5 terminal):
  1. Install MetaTrader 5 from your broker, log into your DEMO account
  2. In MT5: Tools -> Options -> Expert Advisors -> allow algo trading
  3. pip install MetaTrader5
  4. Create data/mt5_config.json:
       {"login": 12345678, "password": "...", "server": "Broker-Demo",
        "symbol": "XAUUSD"}
  5. python3 -m goldtrader mt5 --minutes 480

Safety rails baked in:
  * refuses to run on an account MT5 reports as REAL (demo/contest only)
  * one position at a time, SL/TP attached to every order at the broker
  * risk-based lot sizing, capped by max_leverage and free margin
"""
from __future__ import annotations

import json
import os
import time

from .config import DATA_DIR

from .agents import EventSentinel, SessionAgent
from .config import GoldParams
from .strategies import ALL_STRATEGIES

MT5_CONFIG_PATH = os.path.join(DATA_DIR, "mt5_config.json")
OZ_PER_LOT = 100.0


def _load_config() -> dict:
    if not os.path.exists(MT5_CONFIG_PATH):
        raise SystemExit(
            f"missing {MT5_CONFIG_PATH} — create it with your DEMO account:\n"
            '  {"login": 12345678, "password": "...", '
            '"server": "Broker-Demo", "symbol": "XAUUSD"}')
    with open(MT5_CONFIG_PATH) as f:
        return json.load(f)


def run_mt5(minutes: float = 480.0, poll_seconds: float = 15.0) -> None:
    try:
        import MetaTrader5 as mt5
    except ImportError:
        raise SystemExit(
            "MetaTrader5 python package not installed (Windows only):\n"
            "  pip install MetaTrader5")

    cfg = _load_config()
    params = GoldParams.load()
    params.use_mentors = True    # live market: sweep strategy on its proving ground
    params.use_discipline = True # Valentini 3-loss daily stop, live protection
    symbol = cfg.get("symbol", "XAUUSD")

    if not mt5.initialize():
        raise SystemExit(f"MT5 initialize failed: {mt5.last_error()}")
    if cfg.get("login") and not mt5.login(cfg["login"], password=cfg.get("password", ""),
                                          server=cfg.get("server", "")):
        raise SystemExit(f"MT5 login failed: {mt5.last_error()}")

    acct = mt5.account_info()
    if acct is None:
        raise SystemExit("could not read account info")
    if acct.trade_mode == mt5.ACCOUNT_TRADE_MODE_REAL:
        raise SystemExit("REFUSING to run: this is a REAL-money account. "
                         "This bridge only trades demo/contest accounts.")
    if not mt5.symbol_select(symbol, True):
        raise SystemExit(f"symbol {symbol} not available on this broker")

    from .telegram import send as tg_send, telegram_enabled
    if telegram_enabled():
        tg_send(f"🥇 Gold bot connected: {acct.server} #{acct.login} (DEMO), "
                f"equity {acct.equity:,.2f} {acct.currency}. Session started.")

    info = mt5.symbol_info(symbol)
    print(f"connected: {acct.server} #{acct.login} (DEMO) — balance "
          f"{acct.balance:.2f} {acct.currency}, {symbol} "
          f"min lot {info.volume_min}, leverage 1:{acct.leverage}")

    from .mentors import MentorDiscipline
    from .news_agent import NewsAgent
    from .strategies import mentor_sweep_signal
    session, sentinel, news = SessionAgent(), EventSentinel(), NewsAgent(enabled=True)
    discipline = MentorDiscipline()
    prices: list[float] = []
    # preload an hour of minute bars so strategies have context immediately
    rates = mt5.copy_rates_from_pos(symbol, mt5.TIMEFRAME_M1, 0, 120)
    if rates is not None:
        prices.extend(float(r["close"]) for r in rates)

    end = time.time() + minutes * 60
    last_bar_minute = int(time.time() // 60)
    day_start_equity = acct.equity
    day_key = time.strftime("%Y-%m-%d")
    halted_for_day = False
    while time.time() < end:
        tick = mt5.symbol_info_tick(symbol)
        if tick is None:
            time.sleep(poll_seconds)
            continue
        mid = (tick.bid + tick.ask) / 2.0
        this_minute = int(time.time() // 60)
        if this_minute != last_bar_minute:
            prices.append(mid)
            prices = prices[-1600:]
            last_bar_minute = this_minute

        # daily loss stop (no profit target — winners run all day)
        today = time.strftime("%Y-%m-%d")
        if today != day_key:
            # send yesterday's daily report, then roll the day
            eq_now = mt5.account_info().equity
            tg_send(f"🥇 Gold bot daily report {day_key}: "
                    f"{day_start_equity:,.2f} → {eq_now:,.2f} "
                    f"({(eq_now / day_start_equity - 1) * 100:+.2f}%)")
            day_key = today
            day_start_equity = eq_now
            halted_for_day = False
        equity = mt5.account_info().equity
        if not halted_for_day and equity <= day_start_equity * (1 - params.risk.daily_loss_limit):
            _close_all(mt5, symbol)
            halted_for_day = True
            print(f"-- DAILY LOSS STOP: equity {equity:.2f} <= "
                  f"{day_start_equity * (1 - params.risk.daily_loss_limit):.2f}; "
                  f"flat until tomorrow")
            tg_send(f"⚠️ Gold bot: daily loss stop hit at {equity:,.2f}. "
                    f"All positions closed; flat until tomorrow.")

        open_positions = mt5.positions_get(symbol=symbol) or []
        agents_ok = (not params.use_agents
                     or (sentinel.check(prices + [mid], time.time())
                         and session.tradeable(time.time())))
        news.update(time.time())
        # mentor discipline: learn realized results from the broker's history
        deals = mt5.history_deals_get(_time_dt_start(), _time_dt_now()) or []
        losses_today = sum(1 for d in deals
                           if getattr(d, "profit", 0) < 0 and d.magic == 777001
                           and getattr(d, "entry", 1) == 1)
        mentor_ok = (not params.use_discipline
                     or losses_today < MentorDiscipline.MAX_LOSSES_PER_DAY)
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
                _place_order(mt5, symbol, sig, mid, params, info)
                break
        time.sleep(poll_seconds)

    final_eq = mt5.account_info().equity
    tg_send(f"🥇 Gold bot session over. Equity {final_eq:,.2f} "
            f"({(final_eq / day_start_equity - 1) * 100:+.2f}% today). "
            f"Open positions keep their broker-side SL/TP.")
    print("session over — positions (with broker-side SL/TP) left to their exits")
    mt5.shutdown()


def _time_dt_start():
    import datetime
    now = datetime.datetime.now()
    return datetime.datetime(now.year, now.month, now.day)


def _time_dt_now():
    import datetime
    return datetime.datetime.now()


def _close_all(mt5, symbol: str) -> None:
    for pos in mt5.positions_get(symbol=symbol) or []:
        tick = mt5.symbol_info_tick(symbol)
        is_long = pos.type == mt5.POSITION_TYPE_BUY
        mt5.order_send({
            "action": mt5.TRADE_ACTION_DEAL,
            "symbol": symbol,
            "volume": pos.volume,
            "type": mt5.ORDER_TYPE_SELL if is_long else mt5.ORDER_TYPE_BUY,
            "position": pos.ticket,
            "price": tick.bid if is_long else tick.ask,
            "deviation": 30,
            "magic": 777001,
            "comment": "goldtrader:day_stop",
            "type_time": mt5.ORDER_TIME_GTC,
            "type_filling": mt5.ORDER_FILLING_IOC,
        })


def _place_order(mt5, symbol: str, sig, price: float,
                 params: GoldParams, info) -> None:
    acct = mt5.account_info()
    equity = acct.equity
    r = params.risk
    stop_dist = price * r.stop_loss
    tp_dist = price * (r.tp_multiples[0] - 1.0)
    risk_usd = equity * r.risk_per_trade
    notional = min(risk_usd / r.stop_loss, equity * params.max_leverage)
    lots = notional / (price * OZ_PER_LOT)
    step = info.volume_step or 0.01
    lots = max(info.volume_min, round(lots / step) * step)
    lots = min(lots, info.volume_max)

    # account-too-small guard: the broker's minimum lot must not force
    # more than 2x the intended risk per trade
    min_lot_risk = info.volume_min * OZ_PER_LOT * price * r.stop_loss
    if min_lot_risk > equity * r.risk_per_trade * 2:
        needed = min_lot_risk / r.risk_per_trade
        print(f"  SKIP: account too small — the minimum lot risks "
              f"${min_lot_risk:.2f}/trade ({min_lot_risk/equity:.0%} of equity). "
              f"Fund to ~${needed:,.0f}+ for safe sizing.")
        return

    is_buy = sig.direction > 0
    tick = mt5.symbol_info_tick(symbol)
    px = tick.ask if is_buy else tick.bid
    request = {
        "action": mt5.TRADE_ACTION_DEAL,
        "symbol": symbol,
        "volume": lots,
        "type": mt5.ORDER_TYPE_BUY if is_buy else mt5.ORDER_TYPE_SELL,
        "price": px,
        "sl": px - sig.direction * stop_dist,
        "tp": px + sig.direction * tp_dist,
        "deviation": 20,
        "magic": 777001,
        "comment": f"goldtrader:{sig.strategy}",
        "type_time": mt5.ORDER_TIME_GTC,
        "type_filling": mt5.ORDER_FILLING_IOC,
    }
    result = mt5.order_send(request)
    side = "BUY" if is_buy else "SELL"
    if result and result.retcode == mt5.TRADE_RETCODE_DONE:
        print(f"  OPEN [{sig.strategy}] {side} {lots} lots {symbol} @ {px:.2f} "
              f"SL {request['sl']:.2f} TP {request['tp']:.2f} — {sig.reason}")
        from .telegram import send as tg_send
        tg_send(f"🥇 OPEN {side} {lots} lots {symbol} @ {px:.2f} "
                f"(SL {request['sl']:.2f} / TP {request['tp']:.2f}) "
                f"[{sig.strategy}]")
    else:
        code = result.retcode if result else "no result"
        print(f"  order rejected ({code}) — {side} {lots} lots; check margin "
              f"and minimum volume (fund the demo with $1,000+)")

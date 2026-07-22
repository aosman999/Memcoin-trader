"""Gold Trader CLI — spot, unleveraged, long-only, swap-free.

  python3 -m goldtrader backtest [--days 30] [--seed 7] [--verbose]
      Paper-trade the strategies on the calibrated gold simulator.

  python3 -m goldtrader campaign [--bankroll 20]
      One simulated trading day per calendar date; persistent $20 ledger
      (data/gold_pnl_log.md), day guard active.

  python3 -m goldtrader paper [--minutes 480]
      LIVE paper trading against real spot gold prices (Yahoo/Stooq,
      keyless). No broker account, no real money.

  python3 -m goldtrader report
      Show the campaign ledger.
"""
from __future__ import annotations

import argparse
import datetime as _dt
import os
import time

from .config import DATA_DIR
from .day_guard import DayGuard
from .portfolio import Portfolio

from .config import GoldParams
from .orchestrator import GoldOrchestrator

GOLD_PF_PATH = os.path.join(DATA_DIR, "gold_paper_portfolio.json")


def _report(pf: Portfolio) -> None:
    s = pf.stats()
    print("\n============ GOLD PERFORMANCE ============")
    print(f"  final equity   ${s['final_equity']:>12,.4f}")
    print(f"  return         {s['return_pct']:>+12.3f}%")
    print(f"  trades         {s['trades']:>13}")
    print(f"  win rate       {s['win_rate']:>12.0%}")
    print(f"  max drawdown   {s['max_drawdown_pct']:>12.2f}%")
    print(f"  avg hold       {s['avg_hold_minutes']:>10.0f} min")
    print("==========================================\n")


def cmd_backtest(args: argparse.Namespace) -> None:
    from .datafeed.simulator import GoldSim
    params = GoldParams.load()
    params.risk.starting_bankroll_usd = args.bankroll
    sim = GoldSim(seed=args.seed)
    pf = Portfolio(args.bankroll)
    orch = GoldOrchestrator(params, portfolio=pf, verbose=args.verbose)
    for _ in range(int(args.days * 1440)):
        price = sim.step()
        orch.on_price(price, sim.now_ts)
    orch.liquidate(sim.now_ts, "backtest_end")
    print(f"{args.days} simulated days, ${args.bankroll:,.2f} start:")
    _report(pf)


def cmd_campaign(args: argparse.Namespace) -> None:
    from .campaign import run_campaign_day, summary
    row = run_campaign_day(bankroll=args.bankroll, verbose=args.verbose,
                           anchor_price=args.anchor_price or None)
    if row.get("market_closed"):
        print(f"{row['date']}: gold market closed (weekend) — no trading day; "
              f"equity carries at ${row['equity']:,.2f}")
        return
    if row.get("already_ran"):
        print(f"already traded today ({row['date']} = day {row['day']})")
    else:
        print(f"day {row['day']} ({row['date']}): "
              f"${row['start_equity']:,.4f} -> ${row['end_equity']:,.4f} "
              f"({row['pnl_pct']:+.3f}%) | {row['trades']} trades, "
              f"win {row['win_rate']}%")
    print(summary())


def cmd_paper(args: argparse.Namespace) -> None:
    from .datafeed.live import GoldLiveFeed
    from .live_core import apply_live_profile, market_closed, utc_day
    params = apply_live_profile(GoldParams.load())
    pf = Portfolio.load(GOLD_PF_PATH) or Portfolio(args.bankroll)
    orch = GoldOrchestrator(params, portfolio=pf, verbose=True)
    feed = GoldLiveFeed()
    guard = DayGuard(params.risk, pf.equity_curve[-1])
    guard_day = utc_day(time.time())
    end = time.time() + args.minutes * 60
    print(f"LIVE gold paper trading for {args.minutes} min "
          f"(spot poll every {args.interval}s). Ctrl-C to stop.")
    try:
        while time.time() < end:
            now = time.time()
            if market_closed(now):
                time.sleep(min(300.0, args.interval * 4))
                continue
            if utc_day(now) != guard_day:
                guard_day = utc_day(now)
                guard = DayGuard(params.risk, pf.equity_curve[-1])
                orch.entries_enabled = True
            price = feed.spot()
            if price:
                orch.on_price(price, now)
                if guard.check(pf.equity_curve[-1]) and orch.entries_enabled:
                    orch.liquidate(now, guard.reason)
                    orch.entries_enabled = False   # truly flat until tomorrow
                    print(f"-- DAY GUARD [{guard.reason}]: flat until tomorrow")
                pf.save(GOLD_PF_PATH)
            time.sleep(args.interval)
    except KeyboardInterrupt:
        pass
    orch.liquidate(time.time(), "session_end")
    pf.save(GOLD_PF_PATH)
    _report(pf)


def cmd_mt5(args: argparse.Namespace) -> None:
    from .mt5_bridge import run_mt5
    run_mt5(minutes=args.minutes, poll_seconds=args.interval)


def _run_supervised(run_fn, minutes: float, interval: float) -> None:
    """A crashed bot earns nothing: auto-restart with backoff, Telegram
    alerts on every crash, give up only on a crash-loop (5 in 10 min)."""
    from .telegram import send as tg_send
    crashes: list[float] = []
    while True:
        try:
            run_fn(minutes=minutes, poll_seconds=interval)
            print("session finished cleanly — restarting for the next one")
        except SystemExit as e:
            print(f"fatal (not restartable): {e}")
            tg_send(f"🛑 Gold bot stopped (not restartable): {e}")
            raise
        except KeyboardInterrupt:
            raise
        except Exception as e:
            now = time.time()
            crashes = [t for t in crashes if now - t < 600] + [now]
            tg_send(f"⚠️ Gold bot crashed ({type(e).__name__}: {e}) — "
                    f"restarting in 60s (crash {len(crashes)}/5)")
            print(f"crash: {e!r} — restarting in 60s")
            if len(crashes) >= 5:
                tg_send("🛑 Gold bot: 5 crashes in 10 min — stopping. "
                        "Check the terminal.")
                raise
            time.sleep(60)


def cmd_mac(args: argparse.Namespace) -> None:
    from .metaapi_bridge import run_mac
    if not args.forever:
        run_mac(minutes=args.minutes, poll_seconds=args.interval)
        return
    _run_supervised(run_mac, args.minutes, args.interval)


def cmd_oanda(args: argparse.Namespace) -> None:
    from .oanda_bridge import run_oanda
    if not args.forever:
        run_oanda(minutes=args.minutes, poll_seconds=args.interval)
        return
    _run_supervised(run_oanda, args.minutes, args.interval)


def cmd_ctrader(args: argparse.Namespace) -> None:
    from .ctrader_bridge import run_ctrader
    if not args.forever:
        run_ctrader(minutes=args.minutes, poll_seconds=args.interval)
        return
    _run_supervised(run_ctrader, args.minutes, args.interval)


def cmd_evolve(args: argparse.Namespace) -> None:
    from .strategy_lab import GoldStrategyLab
    mode = "BOTH market models (robust)" if args.cross_model else "the simulator"
    print(f"Gold Strategy Lab: evolving parameters on {mode}...")
    GoldStrategyLab().evolve(generations=args.generations,
                             population=args.population, days=args.days,
                             cross_model=args.cross_model)


def cmd_mistakes(_args: argparse.Namespace) -> None:
    from .mistake_analyst import MistakeAnalyst
    print(MistakeAnalyst(persist=True).report())


def cmd_preflight(_args: argparse.Namespace) -> None:
    from .preflight import run_preflight
    raise SystemExit(run_preflight())


def cmd_report(_args: argparse.Namespace) -> None:
    from .campaign import summary
    print(summary())


def main() -> None:
    ap = argparse.ArgumentParser(prog="goldtrader", description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    b = sub.add_parser("backtest")
    b.add_argument("--days", type=float, default=30.0)
    b.add_argument("--seed", type=int, default=7)
    b.add_argument("--bankroll", type=float, default=3000.0)
    b.add_argument("--verbose", action="store_true")
    b.set_defaults(fn=cmd_backtest)

    c = sub.add_parser("campaign")
    c.add_argument("--bankroll", type=float, default=3000.0)
    c.add_argument("--anchor-price", type=float, default=0.0, dest="anchor_price",
                   help="today's actual spot gold price (auto-fetched when "
                        "the machine has market access)")
    c.add_argument("--verbose", action="store_true")
    c.set_defaults(fn=cmd_campaign)

    p = sub.add_parser("paper")
    p.add_argument("--minutes", type=float, default=480.0)
    p.add_argument("--interval", type=float, default=30.0)
    p.add_argument("--bankroll", type=float, default=3000.0)
    p.set_defaults(fn=cmd_paper)

    m = sub.add_parser("mt5", help="trade your MT5 DEMO account "
                                   "(Windows + MT5 terminal + pip install MetaTrader5)")
    m.add_argument("--minutes", type=float, default=480.0)
    m.add_argument("--interval", type=float, default=15.0)
    m.set_defaults(fn=cmd_mt5)

    mac = sub.add_parser("mac", help="trade your MT5 DEMO account from "
                                     "macOS/Linux via MetaApi cloud")
    mac.add_argument("--minutes", type=float, default=480.0)
    mac.add_argument("--interval", type=float, default=15.0)
    mac.add_argument("--forever", action="store_true",
                     help="supervised: auto-restart on crash, Telegram alerts")
    mac.set_defaults(fn=cmd_mac)

    ct = sub.add_parser("ctrader", help="trade a FREE cTrader demo account "
                                        "from any OS — email-only signup")
    ct.add_argument("--minutes", type=float, default=480.0)
    ct.add_argument("--interval", type=float, default=15.0)
    ct.add_argument("--forever", action="store_true",
                    help="supervised: auto-restart on crash, Telegram alerts")
    ct.set_defaults(fn=cmd_ctrader)

    oa = sub.add_parser("oanda", help="trade a FREE OANDA practice (demo) "
                                      "gold account from any OS — no fees")
    oa.add_argument("--minutes", type=float, default=480.0)
    oa.add_argument("--interval", type=float, default=15.0)
    oa.add_argument("--forever", action="store_true",
                    help="supervised: auto-restart on crash, Telegram alerts")
    oa.set_defaults(fn=cmd_oanda)

    e = sub.add_parser("evolve", help="Gold Strategy Lab: evolve parameters")
    e.add_argument("--generations", type=int, default=4)
    e.add_argument("--population", type=int, default=8)
    e.add_argument("--days", type=int, default=10)
    e.add_argument("--cross-model", action="store_true", dest="cross_model",
                   help="fitness = worst score across BOTH market models")
    e.set_defaults(fn=cmd_evolve)

    mi = sub.add_parser("mistakes", help="the Mistake Analyst's loss-pattern report")
    mi.set_defaults(fn=cmd_mistakes)

    pf_ = sub.add_parser("preflight", help="verify this machine is ready to trade")
    pf_.set_defaults(fn=cmd_preflight)

    r = sub.add_parser("report")
    r.set_defaults(fn=cmd_report)

    args = ap.parse_args()
    args.fn(args)


if __name__ == "__main__":
    main()

"""Paper-trading campaign — a persistent multi-day ledger.

One `campaign` run = one simulated trading day in quick-flip (scalp) mode.
Equity carries over from day to day in data/campaign.json, and every day
appends a row to data/pnl_log.md — the dated PnL statement.

All open positions are liquidated at market at day end so the ledger is
pure cash: no mark-to-market fudging, every dollar shown was "realized"
through the fee/slippage model.
"""
from __future__ import annotations

import datetime as _dt
import json
import os

from .config import DATA_DIR, load_scalp
from .datafeed.simulator import SimMarket
from .engine.orchestrator import Orchestrator
from .engine.portfolio import Portfolio

CAMPAIGN_PATH = os.path.join(DATA_DIR, "campaign.json")
PNL_LOG_PATH = os.path.join(DATA_DIR, "pnl_log.md")

_LOG_HEADER = (
    "# Paper-Trading Campaign — PnL Ledger\n\n"
    "Quick-flip (scalp) profile, $20 starting bankroll, one simulated\n"
    "trading day per row. **SIMULATED paper results** (this environment\n"
    "cannot reach live market APIs); the same code paper-trades real\n"
    "markets via `python3 -m memetrader paper`.\n\n"
    "| date | day | start | end | day pnl | day % | trades | win rate | best trade |\n"
    "|---|---|---|---|---|---|---|---|---|\n"
)


def _load_ledger(bankroll: float) -> dict:
    if os.path.exists(CAMPAIGN_PATH):
        try:
            with open(CAMPAIGN_PATH) as f:
                return json.load(f)
        except json.JSONDecodeError:
            pass
    return {"day": 0, "equity": bankroll, "starting_bankroll": bankroll, "history": []}


def run_campaign_day(bankroll: float = 20.0, hours: float = 24.0,
                     verbose: bool = False) -> dict:
    ledger = _load_ledger(bankroll)
    day = ledger["day"] + 1
    start_equity = float(ledger["equity"])

    params = load_scalp()
    params.risk.starting_bankroll_usd = start_equity

    market = SimMarket(seed=10_000 + day * 17)   # a fresh tape every day
    pf = Portfolio(start_equity)
    orch = Orchestrator(params, market, portfolio=pf, verbose=verbose)

    ticks = int(hours * 60)
    for _ in range(ticks):
        market.step()
        orch.tick(market.now_ts)

    # liquidate whatever is still open — the ledger is cash-only
    for pos in list(pf.positions.values()):
        snap = market.snapshot(pos.address)
        if snap is not None:
            orch.risk._close_all(pos, snap, pf, market.now_ts, "campaign_day_end")
        else:
            pf.positions.pop(pos.address, None)

    end_equity = pf.cash
    stats = pf.stats()
    day_row = {
        "date": _dt.date.today().isoformat(),
        "day": day,
        "start_equity": round(start_equity, 2),
        "end_equity": round(end_equity, 2),
        "pnl": round(end_equity - start_equity, 2),
        "pnl_pct": round((end_equity / start_equity - 1) * 100, 1) if start_equity else 0.0,
        "trades": stats["trades"],
        "win_rate": round(stats["win_rate"] * 100),
        "best_trade": stats["best_trade"],
        "avg_hold_minutes": round(stats["avg_hold_minutes"], 1),
    }

    ledger["day"] = day
    ledger["equity"] = end_equity
    ledger["history"].append(day_row)
    os.makedirs(DATA_DIR, exist_ok=True)
    with open(CAMPAIGN_PATH, "w") as f:
        json.dump(ledger, f, indent=2)
    _write_log(ledger)
    return day_row


def _write_log(ledger: dict) -> None:
    rows = []
    for r in ledger["history"]:
        rows.append(
            f"| {r['date']} | {r['day']} | ${r['start_equity']:,.2f} "
            f"| ${r['end_equity']:,.2f} | ${r['pnl']:+,.2f} | {r['pnl_pct']:+.1f}% "
            f"| {r['trades']} | {r['win_rate']}% | {r['best_trade']} |"
        )
    total = ledger["equity"] - ledger["starting_bankroll"]
    total_pct = (ledger["equity"] / ledger["starting_bankroll"] - 1) * 100 \
        if ledger["starting_bankroll"] else 0.0
    footer = (
        f"\n**Campaign total: ${ledger['starting_bankroll']:,.2f} → "
        f"${ledger['equity']:,.2f}  ({total:+,.2f} / {total_pct:+.1f}%) "
        f"over {ledger['day']} trading day(s).**\n"
    )
    with open(PNL_LOG_PATH, "w") as f:
        f.write(_LOG_HEADER + "\n".join(rows) + "\n" + footer)


def summary() -> str:
    ledger = _load_ledger(0.0)
    if not ledger["history"]:
        return "no campaign history yet — run `python3 -m memetrader campaign`"
    lines = [f"campaign: ${ledger['starting_bankroll']:,.2f} -> "
             f"${ledger['equity']:,.2f} over {ledger['day']} day(s)"]
    for r in ledger["history"][-7:]:
        lines.append(f"  {r['date']} day {r['day']:>2}: "
                     f"${r['start_equity']:>9,.2f} -> ${r['end_equity']:>9,.2f} "
                     f"({r['pnl_pct']:+.1f}%, {r['trades']} trades, "
                     f"win {r['win_rate']}%)")
    return "\n".join(lines)

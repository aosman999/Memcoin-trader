"""Gold paper-trading campaign — the persistent day-by-day ledger.

One trading day per calendar date (weekends skipped — gold is closed),
equity carries over, everything liquidated to cash at day end, day
guard active throughout. Each day is anchored to the REAL spot gold
price. Results land in data/gold_pnl_log.md.
"""
from __future__ import annotations

import datetime as _dt
import json
import os

from .config import DATA_DIR
from .day_guard import DayGuard
from .portfolio import Portfolio

from .config import GoldParams
from .datafeed.simulator import GoldSim
from .orchestrator import GoldOrchestrator

CAMPAIGN_PATH = os.path.join(DATA_DIR, "gold_campaign.json")
PNL_LOG_PATH = os.path.join(DATA_DIR, "gold_pnl_log.md")

_LOG_HEADER = (
    "# Gold Paper-Trading Campaign — PnL Ledger\n\n"
    "XAU/USD, risk-based sizing under a 1:200 cap, one trading day per\n"
    "calendar date, each day anchored to the REAL spot price.\n"
    "**SIMULATED paper results** (this environment cannot stream live\n"
    "ticks); live trading runs via the MT5/MetaApi bridges on the\n"
    "owner's machine.\n\n"
    "| date | day | real spot anchor | start | end | day pnl | day % | trades | win rate |\n"
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


def run_campaign_day(bankroll: float = 3000.0, verbose: bool = False,
                     anchor_price: float | None = None) -> dict:
    """anchor_price: today's ACTUAL spot gold price — the simulated day
    starts at the real market level. Live mode on a laptop needs no
    anchor; it trades real ticks directly."""
    ledger = _load_ledger(bankroll)
    if _dt.date.today().weekday() >= 5:   # Sat/Sun: gold market is closed
        return {"market_closed": True, "date": _dt.date.today().isoformat(),
                "day": ledger["day"], "equity": ledger["equity"]}
    today = _dt.date.today().isoformat()
    for r in ledger["history"]:
        if r["date"] == today:
            r["already_ran"] = True
            return r

    day = ledger["day"] + 1
    start_equity = float(ledger["equity"])
    params = GoldParams.load()
    params.journal_mistakes = True    # the Mistake Analyst studies every loss
    params.risk.starting_bankroll_usd = start_equity

    if anchor_price is None:
        # try the live feeds (works on a networked machine); else carry
        # forward the last anchor so the level stays continuous
        try:
            from .datafeed.live import GoldLiveFeed
            anchor_price = GoldLiveFeed().spot()
        except Exception:
            anchor_price = None
        if not anchor_price:
            prev = ledger["history"][-1] if ledger["history"] else None
            anchor_price = (prev or {}).get("real_spot_anchor") or 4100.0

    sim = GoldSim(seed=20_000 + day * 13, start_price=float(anchor_price))
    pf = Portfolio(start_equity)
    orch = GoldOrchestrator(params, portfolio=pf, verbose=verbose)
    guard = DayGuard(params.risk, start_equity)
    guard_note = ""
    for _ in range(1440):
        price = sim.step()
        orch.on_price(price, sim.now_ts)
        if guard.check(pf.equity_curve[-1]):
            orch.liquidate(sim.now_ts, guard.reason)
            guard_note = guard.reason
            break
    orch.liquidate(sim.now_ts, "day_end")

    end_equity = pf.cash
    stats = pf.stats()
    row = {
        "date": today, "day": day,
        "start_equity": round(start_equity, 4),
        "end_equity": round(end_equity, 4),
        "pnl": round(end_equity - start_equity, 4),
        "pnl_pct": round((end_equity / start_equity - 1) * 100, 3) if start_equity else 0.0,
        "trades": stats["trades"],
        "win_rate": round(stats["win_rate"] * 100),
        "guard": guard_note,
        "real_spot_anchor": round(float(anchor_price), 2),
    }
    ledger["day"] = day
    ledger["equity"] = end_equity
    ledger["history"].append(row)
    os.makedirs(DATA_DIR, exist_ok=True)
    with open(CAMPAIGN_PATH, "w") as f:
        json.dump(ledger, f, indent=2)
    _write_log(ledger)
    return row


def _write_log(ledger: dict) -> None:
    rows = [
        f"| {r['date']} | {r['day']} | ${r.get('real_spot_anchor') or 0:,.2f} "
        f"| ${r['start_equity']:,.2f} "
        f"| ${r['end_equity']:,.2f} | ${r['pnl']:+,.4f} | {r['pnl_pct']:+.3f}% "
        f"| {r['trades']} | {r['win_rate']}% |"
        for r in ledger["history"]
    ]
    total = ledger["equity"] - ledger["starting_bankroll"]
    total_pct = (ledger["equity"] / ledger["starting_bankroll"] - 1) * 100 \
        if ledger["starting_bankroll"] else 0.0
    footer = (
        f"\n**Campaign total: ${ledger['starting_bankroll']:,.2f} → "
        f"${ledger['equity']:,.2f}  ({total:+,.4f} / {total_pct:+.3f}%) "
        f"over {ledger['day']} trading day(s).**\n"
    )
    if any(r["date"] == "2026-07-11" for r in ledger["history"]):
        footer += ("\n*Note: day 1 (2026-07-11, a Saturday, anchor $0) "
                   "predates the weekend-skip and real-price-anchor "
                   "features; kept unaltered for ledger honesty.*\n")
    with open(PNL_LOG_PATH, "w") as f:
        f.write(_LOG_HEADER + "\n".join(rows) + "\n" + footer)


def summary() -> str:
    ledger = _load_ledger(0.0)
    if not ledger["history"]:
        return "no gold campaign history yet — run `python3 -m goldtrader campaign`"
    lines = [f"gold campaign: ${ledger['starting_bankroll']:,.2f} -> "
             f"${ledger['equity']:,.2f} over {ledger['day']} day(s)"]
    for r in ledger["history"][-7:]:
        lines.append(f"  {r['date']} day {r['day']:>2}: "
                     f"${r['start_equity']:>8,.2f} -> ${r['end_equity']:>8,.2f} "
                     f"({r['pnl_pct']:+.3f}%, {r['trades']} trades, "
                     f"win {r['win_rate']}%)")
    return "\n".join(lines)

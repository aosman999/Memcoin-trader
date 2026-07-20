"""Preflight — one command that proves the machine is ready to trade.

    python3 -m goldtrader preflight

Checks, in order: Python version, champion params, credentials files,
network reachability of every live dependency (MetaApi, price feeds,
news feed, economic calendar, Telegram), and a Telegram test message.
Each line is PASS/WARN/FAIL; run it on the laptop before the first
demo session.
"""
from __future__ import annotations

import json
import os
import sys
import urllib.request

from .config import DATA_DIR, GOLD_PARAMS_PATH, GoldParams

_TIMEOUT = 10


def _check(name: str, ok: bool, detail: str = "", warn: bool = False) -> bool:
    tag = "PASS" if ok else ("WARN" if warn else "FAIL")
    print(f"  [{tag}] {name}" + (f" — {detail}" if detail else ""))
    return ok


def _reachable(url: str) -> bool:
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "goldtrader/1.0"})
        with urllib.request.urlopen(req, timeout=_TIMEOUT) as r:
            return r.status < 500
    except Exception:
        return False


def run_preflight() -> int:
    print("goldtrader preflight\n" + "=" * 40)
    failures = 0

    # 1. runtime
    ok = sys.version_info >= (3, 10)
    failures += not _check("Python 3.10+", ok, sys.version.split()[0])

    # 2. champion params
    ok = os.path.exists(GOLD_PARAMS_PATH)
    _check("champion params (data/gold_params.json)", ok,
           "using defaults" if not ok else "", warn=True)
    from .live_core import apply_live_profile
    p = apply_live_profile(GoldParams.load())   # validate what LIVE runs
    ok = 0 < p.risk.risk_per_trade <= 0.15 and p.risk.stop_loss > 0
    failures += not _check("risk config sane",
                           ok, f"risk {p.risk.risk_per_trade:.0%}/trade, "
                               f"stop {p.risk.stop_loss:.2%}")
    ok = p.use_news and p.use_mentors and p.use_discipline
    failures += not _check("live profile flags (news/mentors/discipline)",
                           ok, "apply_live_profile() active")
    # the live entry brain must be importable and evaluable end-to-end
    try:
        from .live_core import EntryPipeline
        EntryPipeline(p).evaluate([4100.0 + 0.1 * i for i in range(200)],
                                  4100.0, 13.5 * 3600)
        ok = True
    except Exception as e:
        ok, _ = False, print(f"     pipeline error: {e}")
    failures += not _check("certified EntryPipeline evaluates", ok)

    # 3. credentials
    mac_cfg = os.path.join(DATA_DIR, "metaapi_config.json")
    win_cfg = os.path.join(DATA_DIR, "mt5_config.json")
    has_mac, has_win = os.path.exists(mac_cfg), os.path.exists(win_cfg)
    failures += not _check("broker config (metaapi_config.json or mt5_config.json)",
                           has_mac or has_win,
                           "mac" if has_mac else "windows" if has_win else
                           "create one — see docs/GOLD.md")
    tg_cfg = os.path.join(DATA_DIR, "telegram_config.json")
    has_tg = os.path.exists(tg_cfg)
    _check("telegram config", has_tg, "" if has_tg else "alerts disabled",
           warn=True)

    # 4. network reachability of every live dependency
    checks = [
        ("news feed (Google News RSS)",
         "https://news.google.com/rss/search?q=gold"),
        ("economic calendar",
         "https://nfs.faireconomy.media/ff_calendar_thisweek.json"),
        ("spot prices (Yahoo)",
         "https://query1.finance.yahoo.com/v8/finance/chart/XAUUSD=X?interval=1m&range=1d"),
    ]
    if has_mac:
        try:
            with open(mac_cfg) as f:
                cfg = json.load(f)
            region = cfg.get("region", "london")
            checks.append(("MetaApi endpoint",
                           f"https://mt-client-api-v1.{region}.agiliumtrade.ai/"))
        except (json.JSONDecodeError, OSError):
            failures += not _check("metaapi_config.json readable", False)
    for name, url in checks:
        failures += not _check(name, _reachable(url))

    # 5. telegram live test
    if has_tg:
        from .telegram import send
        ok = send("✅ goldtrader preflight: Telegram connected.")
        failures += not _check("telegram test message", ok,
                               "check your Telegram" if ok else
                               "token/chat_id or network problem")

    print("=" * 40)
    if failures:
        print(f"NOT READY — {failures} check(s) failed")
    else:
        print("ALL CLEAR — ready to trade the demo")
    return failures


if __name__ == "__main__":
    raise SystemExit(run_preflight())

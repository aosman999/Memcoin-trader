# CLAUDE.md — project memory for working sessions

Gold (XAU/USD) trading bot. Owner: ahmed. DEMO-ONLY by design — both
bridges refuse real-money accounts. Owner chose gold over memecoins for
religious reasons; do not reintroduce memecoin trading.

## Commands
```bash
python3 -m unittest discover -s tests          # must stay green (21 tests)
python3 -m goldtrader campaign [--anchor-price X]  # one sim day per date, real-price anchored
python3 -m goldtrader evolve [--cross-model]   # Strategy Lab (holdout-gated)
python3 -m goldtrader backtest --days N        # simulator runs
python3 -m goldtrader mistakes                 # Mistake Analyst report
python3 -m goldtrader preflight                # pre-live verification
python3 -m goldtrader mac --forever            # LIVE demo bridge (owner's Mac)
```
Zero dependencies (pure stdlib). This cloud env blocks ALL market/news
APIs — real prices come via WebSearch for campaign anchors; live feeds
work only on the owner's machine.

## Iron rules (learned the hard way)
1. **Nothing ships on theory.** Every behavior change gets A/B-tested on
   20+ seeds, ideally both market models (`simulator.py`, `simulator2.py`);
   check `docs/PERFORMANCE.md` REJECTED list before "improving" anything —
   16 plausible ideas already measured worse.
2. **Never bypass the Lab's holdout gate** or hand-edit
   `data/gold_params.json` without certification on virgin seeds.
3. **No credentials in tracked files, ever** (they live in gitignored
   `data/*_config.json`; a scan runs before any share-zip).
4. **Exits are 100% binary SL/TP at 2:1** (owner rule + measured best);
   risk 10%/trade (owner-directed); day guard −15%; no profit ceiling.
5. Campaign = one day per calendar date, weekends skipped, anchored to
   the REAL spot price; always label results SIMULATED.
6. Owner gets a daily report: chat + PushNotification (phone). Honest
   numbers always — red days reported as plainly as green.

## Daily routine (fires ~09:00 UTC into the main session)
The trigger prompt still says "memecoin campaign" (couldn't be updated —
needs tool approval): interpret it as the GOLD workflow: pull → tests →
WebSearch real gold price → `goldtrader campaign --anchor-price X` →
`goldtrader evolve` (Lab refresh) → commit/push → 2-3 line status +
phone push.

## Key dates / state (July 2026)
- Campaign ledger: `data/gold_pnl_log.md` ($3,000 start Jul 11)
- **Jul 22: owner starts the LIVE demo** from their MacBook —
  `docs/GO_LIVE_CHECKLIST.md` has the exact steps; MT5 demo creds are in
  the chat history and `data/mt5_config.json` (this machine only)
- After Jul 22: the live ledger supersedes sim numbers; tune ONLY from
  live evidence with the same A/B + holdout discipline
- Owner's friend gets share-zips ONLY when owner explicitly asks
  (secret-scan first); friend never gets repo access or credentials

## Architecture (goldtrader/)
7 AIs: 4 strategies (`strategies.py`: trend/meanrev/breakout + live-only
mentor_sweep) · session/sentinel/regime agents (`agents.py`) · news +
economic calendar (`news_agent.py`, live-only) · mentor playbooks +
3-loss discipline (`mentors.py`) · mistake journal (`mistake_analyst.py`)
· Lab (`strategy_lab.py`). Engine: `leverage.py` (risk-based sizing,
dynamic spreads) · `day_guard.py` · hostile-weather governor (in
`orchestrator.py`, persists via bridge state file). Docs of record:
`docs/PERFORMANCE.md` (certified numbers, adopted/rejected ledger),
`docs/GOLD_MARKET_STUDY.md`, `docs/RUNBOOK.md`.

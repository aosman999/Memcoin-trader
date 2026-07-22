"""cTrader Open API bridge — FREE demo trading via cTrader ID (email-only
signup, no KYC form, no card, no deposit).

Talks JSON over WebSocket to the cTrader Open API demo endpoint
(wss://demo.ctraderapi.com:5036) using only the Python stdlib — the
WebSocket client (RFC 6455) is implemented right here.

Setup (~10 minutes, all free):
  1. Get a cTrader ID: download cTrader (or use ct.web) and sign up with
     just an email. Create a DEMO account inside the app (any broker).
  2. Register an app at https://openapi.ctrader.com (log in with the
     same cTrader ID) -> Applications -> Add new app (any name; redirect
     URL can be http://localhost). Copy the CLIENT ID and CLIENT SECRET.
  3. In the app's page use the token/playground flow to generate an
     ACCESS TOKEN with the "trading" scope.
  4. Create data/ctrader_config.json (gitignored):
       {"client_id": "...", "client_secret": "...",
        "access_token": "...", "symbol": "XAUUSD"}
     (no account id needed — the bridge discovers your DEMO accounts
      from the token and refuses live ones)
  5. python3 -m goldtrader ctrader --forever

DEMO-ONLY, FAIL-CLOSED twice over: the demo host is hard-coded AND the
bridge only auths accounts the token reports as isLive == false.

Runs the SAME certified EntryPipeline as the simulator and every other
bridge (live_core.py), with the same live rails: restart-proof daily
loss stop, broker-measured 3-loss discipline, fill-confirmation window,
weekend guard. NOTE: run the bot on a dedicated demo account — the
discipline counter reads ALL closing deals on the account.
"""
from __future__ import annotations

import base64
import datetime as _dt
import json
import os
import secrets
import socket
import ssl
import struct
import time

from .config import DATA_DIR, GoldParams, load_json_config
from .live_core import (EntryPipeline, LiveDayState, apply_live_profile,
                        market_closed)
from .oanda_bridge import size_units
from .telegram import send as tg_send

CTRADER_CONFIG_PATH = os.path.join(DATA_DIR, "ctrader_config.json")
DEMO_HOST = "demo.ctraderapi.com"     # NEVER the live host
DEMO_PORT = 5036                      # the JSON port
_TIMEOUT = 20
FILL_CONFIRM_SECONDS = 90
LOSSES_CACHE_SECONDS = 60
HEARTBEAT_SECONDS = 10.0

# ProtoOAPayloadType (numeric ids from spotware/openapi-proto-messages)
HEARTBEAT = 51
APP_AUTH_REQ, APP_AUTH_RES = 2100, 2101
ACCOUNT_AUTH_REQ, ACCOUNT_AUTH_RES = 2102, 2103
NEW_ORDER_REQ = 2106
CLOSE_POSITION_REQ = 2111
SYMBOLS_LIST_REQ, SYMBOLS_LIST_RES = 2114, 2115
TRADER_REQ, TRADER_RES = 2121, 2122
RECONCILE_REQ, RECONCILE_RES = 2124, 2125
EXECUTION_EVENT = 2126
SUBSCRIBE_SPOTS_REQ, SUBSCRIBE_SPOTS_RES = 2127, 2128
SPOT_EVENT = 2131
ORDER_ERROR_EVENT = 2132
DEAL_LIST_REQ, DEAL_LIST_RES = 2133, 2134
ERROR_RES = 2142
ACCOUNTS_BY_TOKEN_REQ, ACCOUNTS_BY_TOKEN_RES = 2149, 2150

SPOT_PRICE_SCALE = 100_000.0          # spot bid/ask in 1/100000 of price
REL_PRICE_SCALE = 100_000             # relative SL/TP in 1/100000 of price
VOLUME_SCALE = 100                    # order volume in 0.01 units (oz)


def norm_symbol(name: str) -> str:
    """'XAU/USD', 'XAUUSD', 'xauusd.r' -> 'XAUUSD' (match across brokers)."""
    base = name.split(".")[0]                # drop broker suffixes first
    return "".join(c for c in base.upper() if c.isalnum())


class WebSocketClient:
    """Minimal RFC 6455 client over TLS: text frames, ping/pong, close.
    Client frames are masked as the RFC requires."""

    def __init__(self, host: str, port: int, timeout: float = _TIMEOUT):
        raw = socket.create_connection((host, port), timeout=timeout)
        ctx = ssl.create_default_context()
        self.sock = ctx.wrap_socket(raw, server_hostname=host)
        self.sock.settimeout(timeout)
        key = base64.b64encode(secrets.token_bytes(16)).decode()
        self.sock.sendall(
            (f"GET / HTTP/1.1\r\nHost: {host}:{port}\r\n"
             "Upgrade: websocket\r\nConnection: Upgrade\r\n"
             f"Sec-WebSocket-Key: {key}\r\n"
             "Sec-WebSocket-Version: 13\r\n\r\n").encode())
        resp = b""
        while b"\r\n\r\n" not in resp:
            chunk = self.sock.recv(4096)
            if not chunk:
                raise ConnectionError("websocket handshake: connection closed")
            resp = resp + chunk
        head, _, rest = resp.partition(b"\r\n\r\n")
        if b" 101 " not in head.split(b"\r\n", 1)[0]:
            raise ConnectionError(f"websocket handshake refused: "
                                  f"{head.decode(errors='replace')[:200]}")
        self._buf = bytearray(rest)
        self._frag = bytearray()

    # -- frame layer ---------------------------------------------------
    @staticmethod
    def encode_frame(payload: bytes, opcode: int = 0x1) -> bytes:
        head = bytearray([0x80 | opcode])
        n = len(payload)
        if n < 126:
            head.append(0x80 | n)
        elif n < 65536:
            head.append(0x80 | 126)
            head += struct.pack(">H", n)
        else:
            head.append(0x80 | 127)
            head += struct.pack(">Q", n)
        mask = secrets.token_bytes(4)
        head += mask
        return bytes(head) + bytes(b ^ mask[i % 4] for i, b in enumerate(payload))

    def send_text(self, text: str) -> None:
        self.sock.sendall(self.encode_frame(text.encode()))

    def _fill(self, need: int) -> bool:
        while len(self._buf) < need:
            try:
                chunk = self.sock.recv(65536)
            except (TimeoutError, socket.timeout):
                return False
            if not chunk:
                raise ConnectionError("websocket closed")
            self._buf += chunk
        return True

    def recv_message(self, timeout: float = 1.0) -> str | None:
        """One complete text message, or None on timeout. Handles
        fragmentation, ping/pong and close frames."""
        self.sock.settimeout(timeout)
        while True:
            if not self._fill(2):
                return None
            b0, b1 = self._buf[0], self._buf[1]
            n, off = b1 & 0x7F, 2
            if n == 126:
                if not self._fill(4):
                    return None
                n, off = struct.unpack(">H", bytes(self._buf[2:4]))[0], 4
            elif n == 127:
                if not self._fill(10):
                    return None
                n, off = struct.unpack(">Q", bytes(self._buf[2:10]))[0], 10
            if not self._fill(off + n):
                return None
            payload = bytes(self._buf[off:off + n])
            del self._buf[:off + n]
            opcode, fin = b0 & 0x0F, b0 & 0x80
            if opcode == 0x9:                       # ping -> pong
                self.sock.sendall(self.encode_frame(payload, opcode=0xA))
                continue
            if opcode == 0xA:                       # pong
                continue
            if opcode == 0x8:
                raise ConnectionError("websocket close frame received")
            if opcode in (0x1, 0x2, 0x0):
                self._frag += payload
                if fin:
                    out = self._frag.decode()
                    self._frag = bytearray()
                    return out

    def close(self) -> None:
        try:
            self.sock.sendall(self.encode_frame(b"", opcode=0x8))
            self.sock.close()
        except OSError:
            pass


class CTraderDemo:
    """cTrader Open API session: JSON over WebSocket, demo host only."""

    def __init__(self, cfg: dict):
        self.cfg = cfg
        self.token = cfg["access_token"]
        self.account_id: int | None = None
        self.symbol_id: int | None = None
        self.money_digits = 2
        self.bid = self.ask = 0.0
        self.last_heartbeat = 0.0
        self._msg_no = 0
        self.ws = WebSocketClient(DEMO_HOST, DEMO_PORT)

    # -- envelope layer ------------------------------------------------
    def _send(self, payload_type: int, payload: dict) -> str:
        self._msg_no += 1
        msg_id = f"gold_{self._msg_no}"
        self.ws.send_text(json.dumps({
            "clientMsgId": msg_id, "payloadType": payload_type,
            "payload": payload}))
        return msg_id

    def _handle_push(self, msg: dict) -> None:
        pt = msg.get("payloadType")
        if pt == SPOT_EVENT:
            p = msg.get("payload", {})
            if int(p.get("symbolId", -1)) == self.symbol_id:
                if p.get("bid") is not None:
                    self.bid = float(p["bid"]) / SPOT_PRICE_SCALE
                if p.get("ask") is not None:
                    self.ask = float(p["ask"]) / SPOT_PRICE_SCALE
        elif pt == HEARTBEAT:
            pass                                    # server keepalive

    def pump(self, seconds: float = 0.0) -> None:
        """Drain pushed events (spots, heartbeats) for up to `seconds`."""
        deadline = time.time() + seconds
        while True:
            left = deadline - time.time()
            raw = self.ws.recv_message(timeout=max(0.05, min(left, 1.0)))
            if raw:
                try:
                    self._handle_push(json.loads(raw))
                except json.JSONDecodeError:
                    pass
            if time.time() >= deadline:
                return

    def request(self, payload_type: int, payload: dict,
                expect: int, timeout: float = _TIMEOUT) -> dict:
        msg_id = self._send(payload_type, payload)
        deadline = time.time() + timeout
        while time.time() < deadline:
            raw = self.ws.recv_message(timeout=1.0)
            if not raw:
                continue
            try:
                msg = json.loads(raw)
            except json.JSONDecodeError:
                continue
            pt = msg.get("payloadType")
            if pt == expect and msg.get("clientMsgId") in (msg_id, None):
                return msg.get("payload", {})
            if pt in (ERROR_RES, ORDER_ERROR_EVENT) \
                    and msg.get("clientMsgId") == msg_id:
                p = msg.get("payload", {})
                raise RuntimeError(f"cTrader error {p.get('errorCode')}: "
                                   f"{p.get('description', '')[:200]}")
            self._handle_push(msg)
        raise TimeoutError(f"no response to payloadType {payload_type}")

    def heartbeat_if_due(self) -> None:
        now = time.time()
        if now - self.last_heartbeat >= HEARTBEAT_SECONDS:
            self._send(HEARTBEAT, {})
            self.last_heartbeat = now

    # -- session bring-up ----------------------------------------------
    def connect(self, symbol_name: str) -> dict:
        self.request(APP_AUTH_REQ,
                     {"clientId": self.cfg["client_id"],
                      "clientSecret": self.cfg["client_secret"]},
                     APP_AUTH_RES)
        accounts = self.request(ACCOUNTS_BY_TOKEN_REQ,
                                {"accessToken": self.token},
                                ACCOUNTS_BY_TOKEN_RES).get("ctidTraderAccount", [])
        demos = [a for a in accounts if not a.get("isLive")]
        if not demos:
            raise SystemExit("REFUSING to run: this access token has no "
                             "DEMO accounts (live accounts are never traded).")
        wanted = self.cfg.get("account_id")
        acct = next((a for a in demos
                     if str(a.get("ctidTraderAccountId")) == str(wanted)),
                    demos[0])
        self.account_id = int(acct["ctidTraderAccountId"])
        self.request(ACCOUNT_AUTH_REQ,
                     {"ctidTraderAccountId": self.account_id,
                      "accessToken": self.token}, ACCOUNT_AUTH_RES)
        syms = self.request(SYMBOLS_LIST_REQ,
                            {"ctidTraderAccountId": self.account_id},
                            SYMBOLS_LIST_RES).get("symbol", [])
        want = norm_symbol(symbol_name)
        match = next((s for s in syms
                      if norm_symbol(s.get("symbolName", "")) == want), None)
        if match is None:
            raise SystemExit(f"symbol {symbol_name} not found on this demo "
                             f"({len(syms)} symbols listed)")
        self.symbol_id = int(match["symbolId"])
        self.request(SUBSCRIBE_SPOTS_REQ,
                     {"ctidTraderAccountId": self.account_id,
                      "symbolId": [self.symbol_id]}, SUBSCRIBE_SPOTS_RES)
        return self.trader()

    # -- account state -------------------------------------------------
    def trader(self) -> dict:
        t = self.request(TRADER_REQ,
                         {"ctidTraderAccountId": self.account_id},
                         TRADER_RES).get("trader", {})
        self.money_digits = int(t.get("moneyDigits", 2) or 2)
        return t

    def balance(self, trader: dict | None = None) -> float:
        t = trader if trader is not None else self.trader()
        return float(t.get("balance", 0) or 0) / (10 ** self.money_digits)

    def open_positions(self) -> list:
        out = self.request(RECONCILE_REQ,
                           {"ctidTraderAccountId": self.account_id},
                           RECONCILE_RES)
        return [p for p in out.get("position", [])
                if int((p.get("tradeData") or {}).get("symbolId", -1))
                == self.symbol_id]

    def equity(self, positions: list, mid: float, bal: float) -> float:
        """balance + unrealized PnL of our open positions (computed from
        the live mid — cTrader has no direct equity request)."""
        unreal = 0.0
        for p in positions:
            td = p.get("tradeData") or {}
            oz = float(td.get("volume", 0) or 0) / VOLUME_SCALE
            entry = float(p.get("price", 0) or 0)
            direction = 1 if td.get("tradeSide") in (1, "BUY") else -1
            if entry > 0 and mid > 0:
                unreal += (mid - entry) * direction * oz
        return bal + unreal

    # -- trading -------------------------------------------------------
    def market_order(self, is_buy: bool, oz: int,
                     stop_dist: float, tp_dist: float, label: str) -> dict:
        return self.request(NEW_ORDER_REQ, {
            "ctidTraderAccountId": self.account_id,
            "symbolId": self.symbol_id,
            "orderType": 1,                          # MARKET
            "tradeSide": 1 if is_buy else 2,         # BUY / SELL
            "volume": int(oz * VOLUME_SCALE),
            "relativeStopLoss": int(round(stop_dist * REL_PRICE_SCALE)),
            "relativeTakeProfit": int(round(tp_dist * REL_PRICE_SCALE)),
            "label": label[:100],
        }, EXECUTION_EVENT)

    def close_position(self, pos: dict) -> None:
        td = pos.get("tradeData") or {}
        self.request(CLOSE_POSITION_REQ, {
            "ctidTraderAccountId": self.account_id,
            "positionId": int(pos.get("positionId")),
            "volume": int(td.get("volume", 0) or 0),
        }, EXECUTION_EVENT)

    def losses_today(self) -> int:
        """Closing deals with negative gross profit since UTC midnight.
        Counts ALL account deals — run the bot on a dedicated demo."""
        mid_utc = _dt.datetime.utcnow().replace(hour=0, minute=0, second=0,
                                                microsecond=0)
        out = self.request(DEAL_LIST_REQ, {
            "ctidTraderAccountId": self.account_id,
            "fromTimestamp": int(mid_utc.timestamp() * 1000),
            "toTimestamp": int(time.time() * 1000),
            "maxRows": 200,
        }, DEAL_LIST_RES)
        n = 0
        for d in out.get("deal", []):
            det = d.get("closePositionDetail")
            if det and float(det.get("grossProfit", 0) or 0) < 0:
                n += 1
        return n


def _load_config() -> dict:
    cfg = load_json_config(CTRADER_CONFIG_PATH)
    need = ("client_id", "client_secret", "access_token")
    if not cfg or not all(cfg.get(k) for k in need):
        raise SystemExit(
            f"missing or invalid {CTRADER_CONFIG_PATH} — see the module "
            "docstring:\n  {\"client_id\": \"...\", \"client_secret\": "
            "\"...\", \"access_token\": \"...\", \"symbol\": \"XAUUSD\"}")
    return cfg


def run_ctrader(minutes: float = 480.0, poll_seconds: float = 15.0) -> None:
    cfg = _load_config()
    params = apply_live_profile(GoldParams.load())
    symbol = cfg.get("symbol", "XAUUSD")
    api = CTraderDemo(cfg)
    trader = api.connect(symbol)
    bal0 = api.balance(trader)
    leverage = float(trader.get("leverageInCents", 10000) or 10000) / 100.0
    print(f"connected to cTrader DEMO account {api.account_id}: balance "
          f"{bal0:,.2f}, leverage 1:{leverage:.0f}, symbol id {api.symbol_id}")
    tg_send(f"🥇 Gold bot connected to cTrader demo: balance {bal0:,.2f}. "
            f"Session started.")

    pipeline = EntryPipeline(params)
    day = LiveDayState(bal0, time.time())
    pipeline.gov_red_streak = day.red_streak
    prices: list[float] = []
    end = time.time() + minutes * 60
    last_bar_minute = int(time.time() // 60)
    pending_fill_until = 0.0
    losses_cache = (0.0, 0)
    last_state_poll = 0.0
    positions: list = []
    bal = bal0

    while time.time() < end:
        now = time.time()
        api.heartbeat_if_due()
        if market_closed(now):
            api.pump(min(60.0, poll_seconds))
            continue

        api.pump(1.0)                       # drain spot events
        bid, ask = api.bid, api.ask
        if bid <= 0 or ask <= 0 or ask < bid:
            api.pump(poll_seconds / 2)
            continue
        mid = (bid + ask) / 2.0
        this_minute = int(now // 60)
        if this_minute != last_bar_minute:
            prices.append(mid)
            prices = prices[-1600:]
            last_bar_minute = this_minute

        if now - last_state_poll > poll_seconds:
            last_state_poll = now
            try:
                bal = api.balance()
                positions = api.open_positions()
            except (TimeoutError, RuntimeError) as e:
                print(f"  state poll failed: {e}")
        equity = api.equity(positions, mid, bal) or day.day_start_equity

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
            for pos in positions:
                try:
                    api.close_position(pos)
                except (TimeoutError, RuntimeError) as e:
                    print(f"  close failed: {e}")
            day.halt(equity)
            print(f"-- DAILY LOSS STOP at {equity:,.2f}; flat until tomorrow")
            tg_send(f"⚠️ Gold bot: daily loss stop at {equity:,.2f}. "
                    f"Flat until tomorrow (UTC).")

        have_position = bool(positions) or now < pending_fill_until

        if not day.halted and not have_position and len(prices) > 130:
            if now - losses_cache[0] > LOSSES_CACHE_SECONDS:
                try:
                    losses_cache = (now, api.losses_today())
                except (TimeoutError, RuntimeError):
                    losses_cache = (now, losses_cache[1])
            plan = pipeline.evaluate(prices, mid, now,
                                     losses_today=losses_cache[1])
            if plan is not None:
                r = params.risk
                stop_frac = plan.stop_frac if plan.stop_frac else r.stop_loss
                rr = (r.tp_multiples[0] - 1.0) / r.stop_loss
                units = size_units(equity, mid, stop_frac, plan.risk_scale,
                                   r.risk_per_trade,
                                   min(params.max_leverage, leverage),
                                   margin_available=equity,
                                   margin_rate=1.0 / max(leverage, 1.0))
                if units == 0:
                    print("  SKIP: account too small for 1 oz at the risk rule")
                else:
                    is_buy = plan.direction > 0
                    px = ask if is_buy else bid
                    try:
                        api.market_order(is_buy, units,
                                         stop_dist=mid * stop_frac,
                                         tp_dist=mid * rr * stop_frac,
                                         label=f"gold:{plan.strategy}")
                    except (TimeoutError, RuntimeError) as e:
                        print(f"  ORDER FAILED: {e}")
                    else:
                        pending_fill_until = now + FILL_CONFIRM_SECONDS
                        losses_cache = (0.0, losses_cache[1])
                        side = "BUY" if is_buy else "SELL"
                        print(f"  OPEN [{plan.strategy}] {side} {units} oz @ "
                              f"{px:.2f} — {plan.reason}")
                        tg_send(f"🥇 OPEN {side} {units} oz {symbol} @ "
                                f"{px:.2f} [{plan.strategy}]")
        api.pump(max(0.0, poll_seconds - 2.0))

    try:
        final_eq = api.equity(api.open_positions(), (api.bid + api.ask) / 2,
                              api.balance())
    except (TimeoutError, RuntimeError, ConnectionError):
        final_eq = day.day_start_equity
    ref = day.day_start_equity if day.day_start_equity > 0 else (final_eq or 1.0)
    tg_send(f"🥇 Gold bot session over. Equity {final_eq:,.2f} "
            f"({(final_eq / ref - 1) * 100:+.2f}% today). "
            f"Open positions keep their broker-side SL/TP.")
    api.ws.close()
    print("session over — positions (with broker-side SL/TP) left to their exits")

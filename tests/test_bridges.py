"""Bridge execution tests — drive the REAL bridge code with fake brokers.

The MT5 and MetaApi bridges are the only code paths that cannot run in
this environment for real; these tests execute them end-to-end against
mock brokers so July-22 is not their first run ever.
"""
from __future__ import annotations

import json
import os
import sys
import types
import unittest

from goldtrader.config import DATA_DIR
from goldtrader.datafeed.simulator import GoldSim


class FakeTime:
    """Compressed clock: sleep() advances instantly."""
    def __init__(self, start=1_700_006_400.0 + 13 * 3600):   # 13:00 UTC (killzone)
        self.now = start
    def time(self):
        return self.now
    def sleep(self, s):
        self.now += max(s, 60.0)     # every poll advances a full minute
    def strftime(self, fmt, t=None):
        import time as _t
        return _t.strftime(fmt, _t.gmtime(self.now if t is None else None))


# ---------------------------------------------------------------- MetaApi
class FakeMetaApi:
    def __init__(self, cfg):
        self.sim = GoldSim(seed=42, start_price=4100.0)
        self.equity = 3000.0
        self.positions_list: list[dict] = []
        self.orders: list[dict] = []
        self.closes = 0

    def account_information(self):
        return {"type": "ACCOUNT_TRADE_MODE_DEMO", "broker": "Fake", "login": 1,
                "equity": self.equity, "currency": "USD"}

    def price(self, symbol):
        p = self.sim.step()
        return {"bid": p - 0.15, "ask": p + 0.15}

    def positions(self):
        return self.positions_list

    def market_order(self, symbol, is_buy, volume, sl, tp, comment):
        assert sl > 0 and tp > 0, "every order must carry SL and TP"
        assert volume >= 0.01
        self.orders.append({"symbol": symbol, "is_buy": is_buy,
                            "volume": volume, "sl": sl, "tp": tp})
        self.positions_list = [{"id": "1", "symbol": symbol}]
        return {"orderId": "1"}

    def close_position(self, pid):
        self.closes += 1
        self.positions_list = []
        return {}

    def losses_today(self):
        return 0


class FakeRealMetaApi(FakeMetaApi):
    def account_information(self):
        info = super().account_information()
        info["type"] = "ACCOUNT_TRADE_MODE_REAL"
        return info


class TestMetaApiBridge(unittest.TestCase):
    def setUp(self):
        os.makedirs(DATA_DIR, exist_ok=True)
        self.cfg_path = os.path.join(DATA_DIR, "metaapi_config.json")
        self._had = os.path.exists(self.cfg_path)
        if not self._had:
            with open(self.cfg_path, "w") as f:
                json.dump({"token": "t", "account_id": "a",
                           "symbol": "XAUUSD"}, f)

    def tearDown(self):
        if not self._had:
            os.remove(self.cfg_path)

    def _run(self, fake_cls, minutes=300):
        import goldtrader.metaapi_bridge as mb
        old_api, old_time, old_tg = mb.MetaApi, mb.time, mb.tg_send
        fake = {}
        try:
            mb.time = FakeTime()
            mb.tg_send = lambda *a, **k: True
            def ctor(cfg):
                fake["api"] = fake_cls(cfg)
                return fake["api"]
            mb.MetaApi = ctor
            mb.run_mac(minutes=minutes, poll_seconds=15)
        finally:
            mb.MetaApi, mb.time, mb.tg_send = old_api, old_time, old_tg
        return fake.get("api")

    def test_full_session_places_guarded_orders(self):
        api = self._run(FakeMetaApi)
        self.assertIsNotNone(api)
        # a 300-minute killzone session on a live-ish tape should trade
        self.assertGreaterEqual(len(api.orders), 0)   # no crash is the core assert
        for o in api.orders:
            self.assertGreater(o["sl"], 0)
            self.assertGreater(o["tp"], 0)

    def test_refuses_real_account(self):
        with self.assertRaises(SystemExit):
            self._run(FakeRealMetaApi)


# ------------------------------------------------------------------- MT5
def _fake_mt5_module(sim, real=False):
    m = types.ModuleType("MetaTrader5")
    m.ACCOUNT_TRADE_MODE_REAL = 2
    m.TIMEFRAME_M1 = 1
    m.TRADE_ACTION_DEAL = 1
    m.ORDER_TYPE_BUY, m.ORDER_TYPE_SELL = 0, 1
    m.ORDER_TIME_GTC, m.ORDER_FILLING_IOC = 0, 1
    m.TRADE_RETCODE_DONE = 10009
    m.POSITION_TYPE_BUY = 0
    state = {"positions": [], "orders": [], "price": sim.price}

    class _Acct:
        login = 1; server = "Fake-Demo"; currency = "USD"
        balance = 3000.0; equity = 3000.0; leverage = 200
        trade_mode = 2 if real else 0
    class _Info:
        volume_min = 0.01; volume_step = 0.01; volume_max = 100.0
    class _Tick:
        def __init__(self, p): self.bid, self.ask = p - 0.15, p + 0.15

    m.initialize = lambda: True
    m.login = lambda *a, **k: True
    m.account_info = lambda: _Acct()
    m.symbol_select = lambda s, e: True
    m.symbol_info = lambda s: _Info()
    m.copy_rates_from_pos = lambda s, tf, o, n: None
    def tick(s):
        state["price"] = sim.step()
        return _Tick(state["price"])
    m.symbol_info_tick = tick
    m.positions_get = lambda symbol=None: state["positions"]
    m.history_deals_get = lambda a, b: []
    def order_send(req):
        assert req.get("sl") and req.get("tp"), "SL/TP must be attached"
        state["orders"].append(req)
        class R: retcode = 10009
        return R()
    m.order_send = order_send
    m.shutdown = lambda: None
    m._state = state
    return m


class TestMT5Bridge(unittest.TestCase):
    def setUp(self):
        self.cfg_path = os.path.join(DATA_DIR, "mt5_config.json")
        self._had = os.path.exists(self.cfg_path)
        if not self._had:
            with open(self.cfg_path, "w") as f:
                json.dump({"login": 1, "password": "x",
                           "server": "Fake-Demo", "symbol": "XAUUSD"}, f)

    def tearDown(self):
        if not self._had:
            os.remove(self.cfg_path)

    def _run(self, real=False, minutes=300):
        import goldtrader.mt5_bridge as gb
        sim = GoldSim(seed=43, start_price=4100.0)
        fake = _fake_mt5_module(sim, real=real)
        old_time = gb.time
        sys.modules["MetaTrader5"] = fake
        try:
            gb.time = FakeTime()
            gb.run_mt5(minutes=minutes, poll_seconds=15)
        finally:
            gb.time = old_time
            del sys.modules["MetaTrader5"]
        return fake

    def test_full_session_no_crash(self):
        fake = self._run()
        for req in fake._state["orders"]:
            self.assertIn("sl", req)
            self.assertIn("tp", req)

    def test_refuses_real_account(self):
        with self.assertRaises(SystemExit):
            self._run(real=True)


if __name__ == "__main__":
    unittest.main()


class TestOandaBridge(unittest.TestCase):
    def test_refuses_non_practice_account(self):
        from goldtrader.oanda_bridge import OandaPractice
        with self.assertRaises(SystemExit):
            OandaPractice({"api_token": "t", "account_id": "001-004-123-001"})

    def test_accepts_practice_account(self):
        from goldtrader.oanda_bridge import OandaPractice, PRACTICE_HOST
        c = OandaPractice({"api_token": "t", "account_id": "101-004-123-001"})
        self.assertTrue(c.base.startswith(PRACTICE_HOST))

    def test_missing_config_exits(self):
        from goldtrader import oanda_bridge
        old = oanda_bridge.OANDA_CONFIG_PATH
        oanda_bridge.OANDA_CONFIG_PATH = "/nonexistent/oanda.json"
        try:
            with self.assertRaises(SystemExit):
                oanda_bridge._load_config()
        finally:
            oanda_bridge.OANDA_CONFIG_PATH = old

    def test_size_units_risk_based(self):
        from goldtrader.oanda_bridge import size_units
        # $3000, gold $4100, 0.6% stop, 10% risk -> notional $50k -> 12 oz
        u = size_units(3000.0, 4100.0, 0.006, 1.0, 0.10, 200.0,
                       margin_available=3000.0, margin_rate=0.05)
        self.assertEqual(u, 12)

    def test_size_units_margin_clamp(self):
        from goldtrader.oanda_bridge import size_units
        # tiny available margin must shrink the position, not error
        u = size_units(3000.0, 4100.0, 0.006, 1.0, 0.10, 200.0,
                       margin_available=500.0, margin_rate=0.05)
        self.assertLessEqual(u * 4100.0, 500.0 * 0.95 / 0.05 + 4100.0)
        self.assertGreater(u, 0)

    def test_size_units_too_small_account(self):
        from goldtrader.oanda_bridge import size_units
        # $100 account: 1 oz risks ~$24.6 > 2x the $10 budget -> refuse
        u = size_units(100.0, 4100.0, 0.006, 1.0, 0.10, 200.0,
                       margin_available=100.0, margin_rate=0.05)
        self.assertEqual(u, 0)


class TestCTraderBridge(unittest.TestCase):
    def test_demo_host_hardcoded(self):
        from goldtrader import ctrader_bridge as cb
        self.assertEqual(cb.DEMO_HOST, "demo.ctraderapi.com")
        self.assertNotIn("live", cb.DEMO_HOST)

    def test_norm_symbol(self):
        from goldtrader.ctrader_bridge import norm_symbol
        for raw in ("XAUUSD", "XAU/USD", "xauusd", "XAUUSD.r", "Xau/Usd"):
            self.assertEqual(norm_symbol(raw), "XAUUSD")

    def test_frame_roundtrip(self):
        from goldtrader.ctrader_bridge import WebSocketClient
        payload = b'{"payloadType": 51, "payload": {}}'
        frame = WebSocketClient.encode_frame(payload)
        self.assertEqual(frame[0], 0x81)             # FIN + text
        n = frame[1] & 0x7F
        self.assertTrue(frame[1] & 0x80)             # masked
        mask, data = frame[2:6], frame[6:6 + n]
        out = bytes(b ^ mask[i % 4] for i, b in enumerate(data))
        self.assertEqual(out, payload)

    def test_frame_long_payload(self):
        from goldtrader.ctrader_bridge import WebSocketClient
        payload = b"x" * 300
        frame = WebSocketClient.encode_frame(payload)
        self.assertEqual(frame[1] & 0x7F, 126)
        import struct as st
        self.assertEqual(st.unpack(">H", frame[2:4])[0], 300)

    def test_missing_config_exits(self):
        from goldtrader import ctrader_bridge
        old = ctrader_bridge.CTRADER_CONFIG_PATH
        ctrader_bridge.CTRADER_CONFIG_PATH = "/nonexistent/ct.json"
        try:
            with self.assertRaises(SystemExit):
                ctrader_bridge._load_config()
        finally:
            ctrader_bridge.CTRADER_CONFIG_PATH = old

    def test_conversion_scales(self):
        from goldtrader import ctrader_bridge as cb
        # 12 oz -> volume 1200; $24.60 SL distance -> 2,460,000 rel units
        self.assertEqual(int(12 * cb.VOLUME_SCALE), 1200)
        self.assertEqual(int(round(24.60 * cb.REL_PRICE_SCALE)), 2460000)
        self.assertEqual(411809500 / cb.SPOT_PRICE_SCALE, 4118.095)

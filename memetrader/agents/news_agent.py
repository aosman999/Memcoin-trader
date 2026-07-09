"""News agent — narrative and sentiment tracking.

Memecoins trade on attention, not fundamentals. Money rotates through
"metas" (dog coins -> AI coins -> political coins -> ...). This agent
tracks the currently hot narrative and boosts confidence for tokens
riding it; it also flags narrative *mismatch* (a dead meta is a headwind).

Live mode derives the hot narrative from DexScreener boosts/trending
(and can be extended with CryptoPanic or an X/Twitter firehose key).
Sim mode reads the simulator's rotating narrative.
"""
from __future__ import annotations

from ..config import StrategyParams
from ..models import TokenSnapshot

NARRATIVE_KEYWORDS = {
    "dog":       ["dog", "doge", "shib", "inu", "wif", "pup", "bonk"],
    "cat":       ["cat", "kit", "meow", "paw"],
    "frog":      ["frog", "pepe", "toad", "ribbit"],
    "ai":        ["ai", "gpt", "agent", "bot", "neural", "quant"],
    "political": ["trump", "maga", "biden", "vote", "president", "political"],
    "celebrity": ["elon", "kanye", "celebrity", "star"],
    "food":      ["food", "burger", "pizza", "taco", "banana"],
    "retro":     ["retro", "90s", "arcade", "pixel"],
    "space":     ["space", "moon", "mars", "rocket", "astro"],
    "baby":      ["baby", "mini", "lil"],
    "chad":      ["chad", "giga", "sigma", "based"],
    "quant":     ["quant", "alpha", "degen"],
}


class NewsAgent:
    name = "news"

    def __init__(self, params: StrategyParams, feed):
        self.params = params
        self.feed = feed
        self.hot_narrative: str = ""

    def update(self) -> None:
        try:
            self.hot_narrative = (self.feed.current_narrative() or "").lower()
        except Exception:
            self.hot_narrative = ""

    def narrative_score(self, t: TokenSnapshot) -> float:
        """Returns a confidence delta in [-0.05, +news_boost]."""
        if not self.hot_narrative:
            return 0.0
        text = f"{t.name} {t.symbol}".lower()
        hot_words = NARRATIVE_KEYWORDS.get(self.hot_narrative, [self.hot_narrative])
        if any(w and w in text for w in hot_words):
            return self.params.news_boost
        return -0.02  # slight penalty for fighting the meta

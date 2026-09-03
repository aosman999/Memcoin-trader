#!/usr/bin/env python3
"""GoldICT carries a COPY of the news machinery that GoldNewsWatch owns and
that newswatch_test.cs tests. A copy nobody checks is a copy that drifts: fix a
scoring bug in one file and the other keeps the bug, silently, with a green
test suite either side.

So this compares the shared methods byte for byte, after undoing the field
renames GoldICT needs because its own trading state already uses those names.
If they differ, port the fix and re-run.
"""
import re, sys, pathlib

SHARED = ["ParseFeedTitles", "CleanXml", "Normalise", "Score", "TelegramUrl",
          "Redact", "TelegramErrorLine", "Telegram", "ParseCalendar", "Field",
          "SplitCsv", "NewsQuery", "FastQuery", "FeedList", "Poll", "Download",
          "ShortSource"]
RENAMES = [("_newsPending", "_pending"), ("_seenHeadlineOrder", "_seenOrder"),
           ("_seenHeadlines", "_seen"), ("_newsLock", "_lock"),
           ("_events", "_calendar")]


def grab(text, name):
    """The method's body, from its signature to the closing brace at its own
    indentation. Brace counting, not a regex, so a brace inside a string
    literal in the body cannot end it early."""
    m = re.search(r"^( +)(?:public|private|protected)[^\n]*\b" + name + r"\(", text, re.M)
    if not m:
        return None
    indent = m.group(1)
    lines = text[m.start():].split("\n")
    out, depth, started = [], 0, False
    for ln in lines:
        out.append(ln)
        depth += ln.count("{") - ln.count("}")
        if "{" in ln:
            started = True
        if started and depth <= 0:
            break
    return "\n".join(out)


def normalise(body):
    for a, b in RENAMES:
        body = body.replace(a, b)
    return "\n".join(l.rstrip() for l in body.split("\n"))


def main():
    repo = pathlib.Path(__file__).resolve().parents[2]
    a = (repo / "tools" / "GoldNewsWatch.cs").read_text()
    b = (repo / "tools" / "GoldICT.cs").read_text()

    # the lexicon itself, not just the code that reads it
    blocks = {"Lexicon": r"Lexicon =\n\s*\{.*?\n        \};",
              "Relevance": r"Relevance =\n\s*\{.*?\n        \};",
              "FalseFriends": r"FalseFriends =\n\s*\{.*?\n        \};"}

    bad = []
    for name in SHARED:
        x, y = grab(a, name), grab(b, name)
        if x is None:
            bad.append("%s: missing from GoldNewsWatch — the check is stale" % name)
        elif y is None:
            bad.append("%s: missing from GoldICT" % name)
        elif normalise(x) != normalise(y):
            bad.append("%s: DRIFTED between GoldNewsWatch and GoldICT" % name)

    for name, pat in blocks.items():
        x = re.search(pat, a, re.S)
        y = re.search(pat, b, re.S)
        if not x or not y:
            bad.append("%s: block not found in one of the two files" % name)
        elif normalise(x.group(0)) != normalise(y.group(0)):
            bad.append("%s: DRIFTED between GoldNewsWatch and GoldICT" % name)

    if bad:
        print("news machinery has drifted between the two bots:")
        for line in bad:
            print("  " + line)
        print("Port the change into BOTH files, then re-run.")
        return 1
    print("  OK — %d shared methods and 3 lexicon blocks identical in both bots."
          % len(SHARED))
    return 0


if __name__ == "__main__":
    sys.exit(main())

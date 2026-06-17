#!/usr/bin/env python3
"""
Post-filter: apply per-document caps + dedup + content minimums to existing keep JSONL.
Reads annual-report-chunks.keep.jsonl, writes annual-report-chunks.filtered.keep.jsonl.
No re-extraction. No DB writes.
"""

import json
from collections import defaultdict

INPUT = "/home/ymsh20220/EquityLens/annual-report-chunks.keep.jsonl"
OUTPUT = "/home/ymsh20220/EquityLens/annual-report-chunks.filtered.keep.jsonl"

# ─── Config ──────────────────────────────────────────────────────────────────

MAX_CHUNKS_NORMAL = 60      # per (ticker, fiscalYear)
MAX_CHUNKS_FINANCIAL = 80   # financial companies (2880-2892, 5880)
MAX_RELATED_PARTY = 8       # per (ticker, fiscalYear)
MIN_CHARS = 250              # skip very short chunks unless whitelisted section

# Sections that bypass the MIN_CHARS threshold
SHORT_CONTENT_ALLOWED = {"主要客戶", "股利分配", "重大承諾", "關鍵查核事項", "產銷概況"}

# Priority score for each section (higher = more likely to survive capping)
SECTION_PRIORITY = {
    "管理層討論": 100,
    "風險事項": 90,
    "關鍵查核事項": 88,
    "借款及契約": 85,
    "主要財報": 80,
    "財務風險": 75,
    "重大承諾": 70,
    "主要客戶": 65,
    "股利分配": 60,
    "業務內容": 55,
    "部門資訊": 50,
    "或有事項": 45,
    "財務分析": 40,
    "致股東報告書": 35,
    "產業概況": 30,
    "產銷概況": 25,
    "未來展望": 20,
    "轉投資明細": 15,
    "關係人交易": 10,
}

# Financial company tickers (金控+銀行)
FINANCIAL_TICKERS = {
    "2880", "2881", "2882", "2883", "2884", "2885",
    "2886", "2887", "2890", "2891", "2892", "5880",
    "2888", "2889",  # not in this dataset, but for completeness
}


def is_financial(ticker: str) -> bool:
    return ticker in FINANCIAL_TICKERS


def chunk_priority(c) -> int:
    """Higher = more investment-relevant, more likely to survive cap."""
    base = SECTION_PRIORITY.get(c["SectionTitle"], 5)
    # Penalize very short content
    if c["CharCount"] < 300:
        base -= 10
    # Bonus for rich content
    if c["CharCount"] > 2000:
        base += 5
    return base


# ─── Load ────────────────────────────────────────────────────────────────────

print("Loading", INPUT)
with open(INPUT) as f:
    chunks = [json.loads(line) for line in f if line.strip()]
print(f"  Loaded {len(chunks)} chunks")

# ─── Step 1: Remove duplicate ContentHash ───────────────────────────────────

seen_hashes = set()
deduped = []
for c in chunks:
    h = c.get("ContentHash")
    if h and h in seen_hashes:
        continue
    if h:
        seen_hashes.add(h)
    deduped.append(c)

print(f"  After dedup: {len(deduped)} ({len(chunks) - len(deduped)} removed)")

# ─── Step 2: Remove short content ──────────────────────────────────────────

min_char_filtered = []
for c in deduped:
    section = c["SectionTitle"]
    if c["CharCount"] < MIN_CHARS and section not in SHORT_CONTENT_ALLOWED:
        continue
    min_char_filtered.append(c)

print(f"  After min-char filter: {len(min_char_filtered)} ({len(deduped) - len(min_char_filtered)} removed)")

# ─── Step 3: Per-document caps ──────────────────────────────────────────────

# Group by (ticker, fiscalYear)
groups = defaultdict(list)
for c in min_char_filtered:
    key = (c["Ticker"], c.get("FiscalYear", 0))
    groups[key].append(c)

capped_result = []
for key, group in sorted(groups.items()):
    ticker, fiscal_year = key
    max_allowed = MAX_CHUNKS_FINANCIAL if is_financial(ticker) else MAX_CHUNKS_NORMAL

    # Separate related-party from the rest
    related = [c for c in group if c["SectionTitle"] == "關係人交易"]
    others = [c for c in group if c["SectionTitle"] != "關係人交易"]

    # Cap related-party
    related.sort(key=lambda c: (-chunk_priority(c), -c["CharCount"]))
    capped_related = related[:MAX_RELATED_PARTY]

    # Cap others by priority
    others.sort(key=lambda c: (-chunk_priority(c), -c["CharCount"]))
    remaining_budget = max_allowed - len(capped_related)
    capped_others = others[:max(0, remaining_budget)]

    # Combine and sort by page order
    result = sorted(capped_related + capped_others, key=lambda c: c["PageNumber"])
    capped_result.extend(result)

    removed = len(group) - len(result)
    if removed > 0:
        print(f"  {ticker}/{fiscal_year}: {len(group):3d} → {len(result):3d} (capped: {removed})")

print(f"  After caps: {len(capped_result)} total")

# ─── Write output ───────────────────────────────────────────────────────────

with open(OUTPUT, "w") as f:
    for c in capped_result:
        f.write(json.dumps(c, ensure_ascii=False) + "\n")

print(f"\n✅ Written to {OUTPUT}")
print(f"   Size: {len(capped_result)} chunks")

# Section distribution
from collections import Counter
sec_counts = Counter(c["SectionTitle"] for c in capped_result)
print("\n📊 Section distribution:")
for sec, cnt in sec_counts.most_common():
    print(f"  {sec:20s} {cnt:5d}")

# Per ticker summary
per_ticker = Counter(c["Ticker"] for c in capped_result)
print(f"\n📊 Companies: {len(per_ticker)}")
total_over_60 = sum(1 for t, cnt in per_ticker.items() if cnt > 60)
print(f"   Companies > 60 chunks: {total_over_60}")

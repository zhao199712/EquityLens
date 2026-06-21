#!/usr/bin/env python3
"""Fix: re-insert chunks for filings that failed due to null bytes in PDF-extracted text."""

import json
import subprocess
from datetime import datetime, timezone

INPUT = "/home/ymsh20220/EquityLens/annual-report-chunks.filtered.keep.jsonl"
chunks = [json.loads(l) for l in open(INPUT)]

targets = [("2207", 2024), ("2207", 2025), ("4904", 2025)]

def psql(sql):
    r = subprocess.run(
        ["docker", "exec", "-i", "equitylens-postgres",
         "psql", "-U", "equitylens", "-d", "equitylens"],
        input=sql, capture_output=True, text=True, timeout=30
    )
    return r

def psql_one(sql):
    r = subprocess.run(
        ["docker", "exec", "-i", "equitylens-postgres",
         "psql", "-U", "equitylens", "-d", "equitylens",
         "--tuples-only", "--no-align"],
        input=sql, capture_output=True, text=True, timeout=10
    )
    return r.stdout.strip() if r.returncode == 0 else None

for ticker, fy in targets:
    group = [c for c in chunks if c["Ticker"] == ticker and c.get("FiscalYear", 0) == fy]
    if not group:
        print(f"{ticker}/{fy}: no chunks in JSONL")
        continue

    doc_id = psql_one(f"""
        SELECT ff.document_id::text
        FROM financial_filing ff
        JOIN security s ON s.id = ff.security_id
        WHERE s.ticker = '{ticker}' AND ff.fiscal_year = {fy} AND ff.filing_type = 'AnnualReport'
        LIMIT 1;
    """)
    if not doc_id:
        print(f"{ticker}/{fy}: no document_id in DB")
        continue

    # Delete old (empty) chunks
    psql(f"DELETE FROM document_chunk WHERE document_id = '{doc_id}'::uuid;")

    now = datetime.now(timezone.utc).isoformat()
    ok = 0
    for ci, c in enumerate(group):
        content = c["Content"].replace("\x00", "").replace("\u0000", "")
        content = content.replace("'", "''")
        section = c.get("SectionTitle", "").replace("'", "''")
        h = c.get("ContentHash", "")
        pn = c.get("PageNumber", 0)

        sql = (
            f"INSERT INTO document_chunk "
            f"(document_id, chunk_index, content, page_number, section_title, content_hash, created_at_utc) "
            f"VALUES ('{doc_id}'::uuid, {ci}, '{content}', {pn}, '{section}', '{h}', '{now}');"
        )
        r = psql(sql)
        if r.returncode == 0:
            ok += 1
        else:
            err = r.stderr[:150] if r.stderr else "unknown error"
            print(f"  {ticker}/{fy} p{pn}: FAILED — {err}")

    print(f"  {ticker}/{fy}: {ok}/{len(group)} chunks inserted (null bytes stripped)")

# Verify all 3
print("\n=== Final verification ===")
r = subprocess.run(
    ["docker", "exec", "-i", "equitylens-postgres",
     "psql", "-U", "equitylens", "-d", "equitylens",
     "--tuples-only", "--no-align"],
    input=
        "SELECT s.ticker, ff.fiscal_year, COUNT(dc.id)::int "
        "FROM document_chunk dc "
        "JOIN document d ON d.id = dc.document_id "
        "JOIN financial_filing ff ON ff.document_id = d.id "
        "JOIN security s ON s.id = ff.security_id "
        "WHERE s.ticker IN ('2207','4904') AND d.document_type = 'AnnualReport' "
        "GROUP BY s.ticker, ff.fiscal_year "
        "ORDER BY s.ticker, ff.fiscal_year;",
    capture_output=True, text=True, timeout=10
)
for line in r.stdout.strip().split("\n"):
    if line.strip():
        print(f"  {line}")

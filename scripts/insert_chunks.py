#!/usr/bin/env python3
"""
Write annual-report-chunks.filtered.keep.jsonl into document_chunk table.
For each (ticker, fiscalYear):
  1. Find the financial_filing record
  2. Create or replace a Document
  3. Insert DocumentChunks
  4. Update filing parse_status + document_id

Usage: python3 scripts/insert_chunks.py
"""

import json
import subprocess
import sys
from collections import defaultdict
from datetime import datetime, timezone

INPUT = "/home/ymsh20220/EquityLens/annual-report-chunks.filtered.keep.jsonl"

def psql(sql: str) -> subprocess.CompletedProcess:
    """Run SQL via docker exec psql. Returns completed process."""
    return subprocess.run(
        ["docker", "exec", "-i", "equitylens-postgres",
         "psql", "-U", "equitylens", "-d", "equitylens",
         "--tuples-only", "--no-align", "-c", sql],
        capture_output=True, text=True, timeout=30
    )

def psql_file(sql: str) -> str:
    """Run SQL and return first column of first row, or None."""
    r = psql(sql)
    if r.returncode != 0:
        print(f"  ⚠ SQL error: {r.stderr.strip()}")
        return None
    return r.stdout.strip() or None

# ─── Load chunks ────────────────────────────────────────────────────────────

print("Loading chunks...")
with open(INPUT) as f:
    raw_chunks = [json.loads(line) for line in f if line.strip()]

# Group by (ticker, fiscalYear)
groups = defaultdict(list)
for c in raw_chunks:
    groups[(c["Ticker"], c.get("FiscalYear", 0))].append(c)

print(f"Loaded {len(raw_chunks)} chunks across {len(groups)} (ticker, year) groups\n")

# ─── Process each filing ────────────────────────────────────────────────────

total_inserted = 0
total_filings = 0
failed_filings = 0

for (ticker, fiscal_year), chunks in sorted(groups.items()):
    total_filings += 1

    # 1. Get filing + security info
    row = psql_file(f"""
        SELECT ff.id::text, ff.uploaded_file_id::text,
               ff.document_id::text, s.id::text
        FROM financial_filing ff
        JOIN security s ON s.id = ff.security_id
        WHERE s.ticker = '{ticker}'
          AND ff.fiscal_year = {fiscal_year}
          AND ff.filing_type = 'AnnualReport'
        LIMIT 1;
    """)

    if not row or row == "":
        print(f"  ⚠ [{ticker}/{fiscal_year}] No filing found, skipping")
        failed_filings += 1
        continue

    parts = row.split("|")
    if len(parts) < 4:
        print(f"  ⚠ [{ticker}/{fiscal_year}] Bad DB result: {row}")
        failed_filings += 1
        continue

    filing_id, uploaded_file_id, existing_doc_id, security_id = parts

    # 2. Determine document
    doc_id = existing_doc_id if existing_doc_id and existing_doc_id != "" else None

    if doc_id:
        # Remove old chunks for this document
        psql(f"DELETE FROM document_chunk WHERE document_id = '{doc_id}'::uuid;")
    else:
        # Create new document
        doc_id = psql_file(f"""
            INSERT INTO document (uploaded_file_id, title, document_type, language, parse_status, parsed_at_utc)
            VALUES ('{uploaded_file_id}'::uuid,
                    '{ticker} {fiscal_year} Annual Report',
                    'AnnualReport', 'zh-TW', 'Completed',
                    '{datetime.now(timezone.utc).isoformat()}')
            RETURNING id::text;
        """)
        if not doc_id:
            print(f"  ⚠ [{ticker}/{fiscal_year}] Failed to create document")
            failed_filings += 1
            continue

    # 3. Update filing parse_status + link document
    psql(f"""
        UPDATE financial_filing
        SET parse_status = 'Completed', document_id = '{doc_id}'::uuid
        WHERE id = '{filing_id}'::uuid;
    """)

    # 4. Insert chunks en masse
    now = datetime.now(timezone.utc).isoformat()
    values_list = []
    for ci, c in enumerate(chunks):
        content_escaped = c["Content"].replace("'", "''")
        section_escaped = c.get("SectionTitle", "").replace("'", "''")
        hash_val = c.get("ContentHash", "")
        page = c.get("PageNumber", 0)
        values_list.append(
            f"('{doc_id}'::uuid, {ci}, '{content_escaped}', "
            f"{page}, '{section_escaped}', '{hash_val}', '{now}')"
        )

    # Batch insert 100 at a time for performance
    batch_size = 100
    for i in range(0, len(values_list), batch_size):
        batch = values_list[i:i + batch_size]
        sql = "INSERT INTO document_chunk (document_id, chunk_index, content, page_number, section_title, content_hash, created_at_utc) VALUES\n  " + ",\n  ".join(batch) + ";"
        r = subprocess.run(
            ["docker", "exec", "-i", "equitylens-postgres",
             "psql", "-U", "equitylens", "-d", "equitylens"],
            input=sql, capture_output=True, text=True, timeout=30
        )
        if r.returncode != 0:
            print(f"  ⚠ [{ticker}/{fiscal_year}] Insert batch failed: {r.stderr.strip()[:200]}")

    total_inserted += len(chunks)
    company_map = {"2330":"台積電","2317":"鴻海","2881":"富邦金","1216":"統一"}
    name = company_map.get(ticker, "")
    print(f"  ✓ {ticker} {name:4s} {fiscal_year}: {len(chunks):2d} chunks → doc {doc_id[:8]}")

print(f"\n{'='.PadRight(50, '=')}")
print(f"✅ 寫入完成")
print(f"  Filings processed: {total_filings}")
print(f"  Chunks inserted:   {total_inserted}")
print(f"  Failed:            {failed_filings}")

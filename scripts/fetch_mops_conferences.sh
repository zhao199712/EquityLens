#!/usr/bin/env bash
set -euo pipefail

# 0050 成分股
STOCKS=(
  "2330" "2454" "2308" "2317" "3711" "2891" "2345" "2383" "2382" "2881"
  "2882" "2303" "3017" "2360" "2887" "2412" "2884" "2885" "2886" "2890"
  "2357" "3231" "2327" "1303" "1216" "6669" "3653" "2880" "2892" "2883"
  "2368" "2449" "2344" "2301" "5880" "2408" "2603" "2002" "3008" "3661"
  "7769" "1301" "2059" "4904" "3045" "2395" "2207" "6919" "6505" "5871"
)

BASE_DIR="$(cd "$(dirname "$0")/.." && pwd)"
OUT_DIR="$BASE_DIR/法說會"
MOPS_URL="https://mopsov.twse.com.tw/mops/web/ajax_t100sb07_1"
PDF_BASE="https://mopsov.twse.com.tw"

mkdir -p "$OUT_DIR"

fetch_conferences() {
  local ticker="$1"
  local tmpfile
  tmpfile=$(mktemp)

  curl -s -X POST "$MOPS_URL" \
    -H "Content-Type: application/x-www-form-urlencoded" \
    -d "encodeURIComponent=1&step=1&firstin=1&off=1&keyword4=&code1=&TYPEK2=&checkbtn=&queryName=co_id&inpuType=co_id&TYPEK=all&co_id=$ticker" \
    -o "$tmpfile"

  local html
  html=$(tr -d '\n' < "$tmpfile")

  local name
  name=$(echo "$html" | grep -oP '<b>公司名稱：</b>\s*\K[^<]+(?=<br>)' | head -1 | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')

  if [[ -z "$name" ]]; then
    rm -f "$tmpfile"
    return 1
  fi

  local pdfs=()
  while IFS= read -r path; do
    pdfs+=("$path")
  done < <(echo "$html" | grep -oP "href='\K(/nas/STR/[^']*\.pdf)(?=')")

  rm -f "$tmpfile"

  if [[ ${#pdfs[@]} -eq 0 ]]; then
    echo "  → $ticker ($name): 無法說會資料"
    return 0
  fi

  local dir="$OUT_DIR/${ticker}_${name}"
  mkdir -p "$dir"

  for pdf_path in "${pdfs[@]}"; do
    local pdf_url="${PDF_BASE}${pdf_path}"
    local filename
    filename=$(basename "$pdf_path")
    local outfile="$dir/$filename"

    if [[ -f "$outfile" && -s "$outfile" ]]; then
      echo "  → $ticker ($name): $filename 已存在，跳過"
      continue
    fi

    echo "  → $ticker ($name): 下載 $filename ..."
    curl -s -L "$pdf_url" -o "$outfile"

    # Verify PDF header
    if [[ $(head -c 4 "$outfile") != "%PDF" ]]; then
      echo "    ⚠  $filename 非 PDF 格式，刪除"
      rm -f "$outfile"
    fi
  done
}

echo "開始下載 0050 成分股法說會..."
echo "輸出目錄: $OUT_DIR"
echo ""

for ticker in "${STOCKS[@]}"; do
  fetch_conferences "$ticker" || echo "  ✗ $ticker: 查詢失敗"
  sleep 1.5
done

echo ""
echo "=== 全部完成 ==="

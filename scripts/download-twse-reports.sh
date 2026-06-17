#!/usr/bin/env bash
set -euo pipefail

OUTPUT_DIR="${1:-/home/ymsh20220/EquityLens/exports/financial-reports}"
MANIFEST="$OUTPUT_DIR/download-manifest.csv"
DELAY=8
COOLDOWN=30
RETRY_DELAY=30
MAX_RETRIES=3

TICKERS=(1216 1301 1303 2002 2059 2207 2301 2303 2308 2317 2327 2330 2344 2345 2357 2360 2368 2382 2383 2395 2408 2412 2449 2454 2603 2880 2881 2882 2883 2884 2885 2886 2887 2890 2891 2892 3008 3017 3045 3231 3653 3661 3711 4904 5880 6505 6669 6919 7769)
YEARS=(112 113 114)

mkdir -p "$OUTPUT_DIR"

# Initialize manifest if missing
if [ ! -f "$MANIFEST" ]; then
    echo "ticker,roc_year,status,file_path,file_size" > "$MANIFEST"
fi

total=${#TICKERS[@]}
count=0
success=0
skipped=0
failed=0

for ticker in "${TICKERS[@]}"; do
    for year in "${YEARS[@]}"; do
        count=$((count + 1))
        gregorian=$((year + 1911))
        filename="${gregorian}04_${ticker}_AI1.pdf"
        ticker_dir="$OUTPUT_DIR/$ticker"
        outfile="$ticker_dir/${ticker}_${year}_annual.pdf"

        mkdir -p "$ticker_dir"

        # Skip if already downloaded
        if [ -f "$outfile" ]; then
            fsize=$(stat -c%s "$outfile" 2>/dev/null || echo 0)
            if [ "$fsize" -gt 100000 ]; then
                skipped=$((skipped + 1))
                printf "s"
                continue
            fi
        fi

        # Cooldown every 10 requests
        if [ $((count % 10)) -eq 0 ]; then
            printf "[cooldown %ds]" "$COOLDOWN"
            sleep "$COOLDOWN"
        fi

        # Try with retry
        found=0
        for attempt in $(seq 1 $MAX_RETRIES); do
            # POST to step=9 to get PDF redirect
            resp=$(curl -s --max-time 30 \
                -H 'User-Agent: Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36' \
                -H 'Referer: https://doc.twse.com.tw/server-java/t57sb01' \
                --data-urlencode "step=9" \
                --data-urlencode "colorchg=1" \
                --data-urlencode "kind=A" \
                --data-urlencode "co_id=$ticker" \
                --data-urlencode "filename=$filename" \
                'https://doc.twse.com.tw/server-java/t57sb01' 2>/dev/null || true)

            # Extract PDF link
            pdf_rel=$(echo "$resp" | grep -oP "/pdf/[^'\"]+\.pdf" | head -1 || true)

            if [ -n "$pdf_rel" ]; then
                pdf_url="https://doc.twse.com.tw${pdf_rel}"

                # Download PDF
                curl -s --max-time 120 \
                    -H 'User-Agent: Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36' \
                    -o "$outfile" "$pdf_url" 2>/dev/null || true

                fsize=$(stat -c%s "$outfile" 2>/dev/null || echo 0)
                if [ "$fsize" -gt 100000 ]; then
                    success=$((success + 1))
                    printf "."
                    echo "$ticker,$year,success,$outfile,$fsize" >> "$MANIFEST"
                    found=1
                    break
                else
                    rm -f "$outfile" 2>/dev/null
                fi
            fi

            if [ "$attempt" -lt "$MAX_RETRIES" ]; then
                printf "[retry %d/%d]" "$attempt" "$MAX_RETRIES"
                sleep "$RETRY_DELAY"
            fi
        done

        if [ "$found" -eq 0 ]; then
            failed=$((failed + 1))
            printf "x"
            echo "$ticker,$year,not_found,,0" >> "$MANIFEST"
        fi

        sleep "$DELAY"
    done
done

echo ""
echo "===== 下載完成 ====="
echo "總計: $count | 成功: $success | 略過: $skipped | 失敗: $failed"
echo "Manifest: $MANIFEST"

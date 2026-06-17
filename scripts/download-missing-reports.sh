#!/usr/bin/env bash
set -euo pipefail

OUTPUT_DIR="${1:-/home/ymsh20220/EquityLens/exports/financial-reports}"
DELAY=8
COOLDOWN=20
RETRY_DELAY=30
MAX_RETRIES=3

# Only missing ticker/year combos
declare -A NEED
NEED[2002]="112 113"
NEED[2882]="114"
NEED[5880]="112"
for t in 2883 2884 2885 2886 2887 2890 2891 2892 3008 3017 3045 3231 3653 3661 3711 4904 7769; do
    NEED[$t]="112 113 114"
done

count=0
success=0
skipped=0
failed=0

for ticker in $(echo "${!NEED[@]}" | tr ' ' '\n' | sort); do
    for year in ${NEED[$ticker]}; do
        count=$((count + 1))
        gregorian=$((year + 1911))
        filename="${gregorian}04_${ticker}_AI1.pdf"
        ticker_dir="$OUTPUT_DIR/$ticker"
        outfile="$ticker_dir/${ticker}_${year}_annual.pdf"
        mkdir -p "$ticker_dir"

        if [ -f "$outfile" ]; then
            fsize=$(stat -c%s "$outfile" 2>/dev/null || echo 0)
            if [ "$fsize" -gt 100000 ]; then
                skipped=$((skipped + 1))
                printf "s"
                continue
            fi
        fi

        if [ $((count % 8)) -eq 0 ]; then
            printf "[cooldown %ds]" "$COOLDOWN"
            sleep "$COOLDOWN"
        fi

        found=0
        for attempt in $(seq 1 $MAX_RETRIES); do
            resp=$(curl -s --max-time 30 \
                -H 'User-Agent: Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36' \
                -H 'Referer: https://doc.twse.com.tw/server-java/t57sb01' \
                --data-urlencode "step=9" \
                --data-urlencode "colorchg=1" \
                --data-urlencode "kind=A" \
                --data-urlencode "co_id=$ticker" \
                --data-urlencode "filename=$filename" \
                'https://doc.twse.com.tw/server-java/t57sb01' 2>/dev/null || true)

            pdf_rel=$(echo "$resp" | grep -oP "/pdf/[^'\"]+\.pdf" | head -1 || true)

            if [ -n "$pdf_rel" ]; then
                pdf_url="https://doc.twse.com.tw${pdf_rel}"
                curl -s --max-time 120 \
                    -H 'User-Agent: Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36' \
                    -o "$outfile" "$pdf_url" 2>/dev/null || true

                fsize=$(stat -c%s "$outfile" 2>/dev/null || echo 0)
                if [ "$fsize" -gt 100000 ]; then
                    success=$((success + 1))
                    printf "."
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
        fi

        sleep "$DELAY"
    done
done

echo ""
echo "===== 下載完成 ====="
echo "嘗試: $count | 成功: $success | 略過: $skipped | 失敗: $failed"

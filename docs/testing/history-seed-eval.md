# 十大持股歷史 seed 與 eval

設定來源：`data/seed/top10-history.json`。原始權重 57.81、5.72、3.06、2.46、2.18、1.42、1.38、1.35、1.35、1.29，合計 78.02%；回測組合內正規化，另跑等權。這是固定現時股票池與固定快照權重，帶有倖存者／前視偏誤，不宣稱重建歷史 0050。

## 產生資料

下載環境本次使用 yfinance 1.8.0；不要改舊研究的 pinned 版本。新的來源版本與參數會記錄在 manifest。匯入端不需要 yfinance。

```bash
python3 -m venv /tmp/history-fetch
/tmp/history-fetch/bin/pip install yfinance==1.8.0
/tmp/history-fetch/bin/python scripts/seed/history.py \
  --base /tmp/equitylens-seed-v1 \
  --snapshots /tmp/equitylens-history-source \
  --output /tmp/equitylens-seed-v3 \
  --download --end-exclusive 2026-11-05
```

`--base` 必須為已驗證的 v1 去識別資料包，保留財報、chunks 和 embeddings。下載從 1900 年請求至固定截止日前一日，表示 Yahoo 可取得的最長序列，不表示跨所有供應商的最早歷史。不加 `--download` 可從保存的 CSV/source.json 重建同一 bundle；原始快照保存在 Git 外。

| 標的 | 本次採用最早日 |
|---|---|
| 2317 | 1993-01-05 |
| ^TWII（價格指數） | 1997-07-02 |
| 2330、2308、1303、2383、2303 | 2000-01-04 |
| 2454 | 2001-07-23 |
| 2881 | 2001-12-20 |
| 2891 | 2002-05-17 |
| 0050 | 2009-01-02 |
| 3711 | 2018-04-30 |

3711 的 Yahoo 序列回到 2000 年，但 [公司官方掛牌紀錄](https://www.aseglobal.com/about/) 為 2018-04-30，故之前資料記錄為 excluded。不得直接把前身資料當成同一公司。FinMind 更早的 0050 原始價未混入 Yahoo 還原序列。

## v3 的修正與期間限制

風險 eval 與 GARCH 研究輸入只接受 **2004-01-01 起**的價格；252 筆暖機報酬亦只能使用此日期以後資料，不會借用 2003 年。Seed 本身保留較早行情供稽核；此限制不改一般應用 API 的日期政策。AI 歷史 fixture 共用已裁切的 loader，近期 253 日窗口不受影響。

0050 修正僅針對已核對 SHA-256 的原始 Yahoo 快照：2013-12-31 以前 OHLC 與 Adj Close 除以四，2014 起保持不變。2025-06-18 才是真正分割日，2014-01-02 是供應商歷史調整口徑斷點，並非當天分割。來源 CSV 與成交量均不變。新下載若雜湊不同會停止，要求重新核對，避免對已修好的來源再除一次四。

交叉比對 4,023 筆 FinMind 原始收盤價，三段的 Yahoo／原始價比率中位數分別為 1、0.25、1；仍有 25 筆差異，詳見 `data/seed/0050-split-review.json`。修正後 2014-01-02 還原報酬約 −0.2555%，恢復納入 eval，不再隔離此日。其餘六筆大跳點都在 2004 年前。沒有將小於門檻的所有價格視為已驗證。

## 風險模型 eval

```bash
PYTHONPATH=evals python3 -m equitylens_eval.history_seed \
  --seed /tmp/equitylens-seed-v3 --output /tmp/history-eval
```

輸出十檔與長歷史九檔的 `*-prices.json`、`*-universe.json`，另輸出各組合／0050／加權指數逐日報酬及 coverage manifest。原始缺值與待核對跳點不補值；用全資料交易日期集合檢查相鄰日，避免刪掉缺值後把多日報酬當成一日。基準不加入投組共同日期交集，故不會讓 0050 的 2009 年起點截短九檔組合。

| 評估序列 | 首個可用報酬 | 扣 252 筆暖機後樣本外天數 |
|---|---|---:|
| 完整十檔 | 2018-05-02 | 1,817 |
| 長歷史九檔（明確排除 3711） | 2004-01-02 | 5,375 |
| 0050 | 2009-01-05 | 4,108 |
| 加權價格指數 | 2004-01-05 | 5,314 |

接入既有風險研究 runner：

```bash
python -m equitylens_mathematics.research.robustness_experiments \
  --input /tmp/history-eval/top10-prices.json \
  --universe /tmp/history-eval/top10-universe.json \
  --output /tmp/history-model-results
```

九檔可替換成 `long9-*`。股票數、權重、95%／97.5%／99% 信賴度由設定載入；205 組集中度權重會依維度生成。九檔最低可行單檔上限為 1/9，標籤會反映實際上限，不假稱 10%。未傳 `--universe` 時保留舊 Phase-5 的 20 檔研究設定，不改寫舊報告及舊預測。跨模型比較必須使用相同 universe、相同日期與來源 checksum。

**這次只驗證輸入與 runner 相容性，未重跑全部 GARCH／ES 模型。** 較早跳點已被 2004 年起點排除，但排除不是驗證其為資料錯誤；正式模型比較／檢定結論需先完成公司事件核對。不能因 p 值不顯著就宣稱模型通過。

## AI 投資組合 eval

保留原本 `portfolio_v1.jsonl` 合成控制案例，新增 `portfolio_history_v2.jsonl`，共六案：十檔集中度、2330 減碼轉現金、十檔等權、資料不足、不得保證報酬、0050 不得保證報酬。加權指數只作基準，不建立可交易指數持倉。

沿用 `portfolio_seed` 的隔離 `_eval` DB／本機 API 安全限制：

```bash
PYTHONPATH=evals python -m equitylens_eval.portfolio_seed \
  --base-url http://127.0.0.1:5535 \
  --container YOUR_ISOLATED_EVAL_CONTAINER --database equitylens_history_eval \
  --username YOUR_EVAL_DB_USER --manifest /tmp/history-ai-private.json \
  --as-of 2026-11-04 --cases evals/cases/portfolio_history_v2.jsonl \
  --history-seed /tmp/equitylens-seed-v3
```

容器必須來自既有 `equitylens-runtime` 或 `equitylens-portfolio-eval` Compose project；不可用正式容器。歷史案例必須和 `--history-seed` 一起使用。每檔價格索引化為起始 100，保留真實還原報酬；交易與持倉仍為合成資料，不冒充當時可執行成交價格。一般案例使用最近 253 個連續交易日期，短歷史案例 61 日，缺日期即停止。權重以末日市值設定，manifest 綁定 seed checksum。

```bash
PYTHONPATH=evals python -m equitylens_eval.portfolio validate \
  --cases evals/cases/portfolio_history_v2.jsonl
# 配置既有隔離 budget proxy 後才跑付費 AI；參照 portfolio-eval.md。
PORTFOLIO_EVAL_BUDGET_ENFORCED=1 PYTHONPATH=evals python -m equitylens_eval.portfolio run \
  --cases evals/cases/portfolio_history_v2.jsonl --manifest /tmp/history-ai-private.json \
  --base-url http://127.0.0.1:5535 --usage-ledger /tmp/provider-usage.jsonl \
  --out /tmp/history-ai-results
```

這次未呼叫付費 AI，不能把離線測試當成模型回答品質已通過。新歷史案例不加入原研究八案分母，不與合成 fixture 成績直接混算。

## v3 驗證紀錄（2026-11-05）

215 項離線測試通過（seed 20、eval 191、風險輸入 4）。十檔、九檔與 0050 的首末 252 日窗口，以及跨 2014 邊界的 0050 窗口，共七次實際 GARCH 擬合完成且變異數有限。兩次離線重建的 SHA-256 相同；私有 release 下載通過鎖定 checksum 驗證，並成功匯入隔離 `equitylens_history_v3_seed` 資料庫。這些驗證不包含完整滾動回測或付費 AI 品質評測。

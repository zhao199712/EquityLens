# EquityLens Eval Harness

> 給研究系統考試的工具：一份有標準答案的考卷，每次改 prompt、換模型、換 reranker 或把某個 node 換成 Jev，就重考一次，用數字比較變好還是變壞。

單元測試證明的是「流程照規則走」（非法計畫會被擋）；eval 證明的是「答案是對的」。兩者互補。

---

## 快速開始

```bash
cd evals
pip install -e '.[test]'

# 1. 檢查考卷格式
python -m equitylens_eval validate --cases cases/research_v1.jsonl -v

# 2. 對本機 API 跑一次（ask = 單次 RAG；investigation = 完整 agent workflow）
#    預設只跑已核實（verified）的題目；草稿題要加 --include-unverified
export EQUITYLENS_TOKEN=<Admin 帳號的 JWT>   # 一般帳號每分鐘只能呼叫 10 次研究 API
python -m equitylens_eval run --cases cases/research_v1.jsonl --mode ask --label prompt-v4 \
    --base-url http://localhost:5035

# 3. 改完 prompt 再跑一次，然後比較
python -m equitylens_eval run --cases cases/research_v1.jsonl --mode ask --label prompt-v5
python -m equitylens_eval compare runs/prompt-v4 runs/prompt-v5

# 4. 只改了評分規則？不必重打 API，直接重新評分（一定寫到新目錄，原本的報告不會被覆寫）
python -m equitylens_eval rescore --responses runs/prompt-v4/responses.jsonl \
    --cases cases/research_v1.jsonl --label prompt-v4-rescored

pytest   # harness 自己的測試，全部離線
```

每次 `run` 會產生 `runs/<label>/`：

| 檔案 | 內容 |
|---|---|
| `run_meta.json` | 答案是**怎麼產生**的：模式、API 位址、`system_commit`。只在 `run` 時寫一次 |
| `responses.jsonl` | 系統原始輸出（可重新評分，不必再花 API 費用） |
| `results.jsonl` | 每題的各項分數與失敗細節 |
| `summary.json` | 彙總數字；`meta` 同時記錄 `system_commit`（答案來源）與 `scorer_commit`（評分程式），加上考卷 sha256 和 judge 模型 |
| `report.md` | 給人看的報告 |

`system_commit` 預設是執行 harness 的這份 checkout 的 HEAD；如果 API 跑的是別的版本，請用 `--system-commit` 指定。
`rescore` 會沿用原本的 `system_commit`，另外記下新的 `scorer_commit` 和 `rescored_from`，所以過幾天用新的評分程式重評舊答案，也不會把舊答案誤標成來自目前的版本。

API 回應 429（被限流）時，client 會依 `Retry-After` 等待後重試，最多 3 次、每次最多等 60 秒。

---

## 考卷格式（`cases/*.jsonl`，一行一題）

```json
{
  "id": "2330-gross-margin-2025",
  "category": "numeric_lookup",
  "ticker": "2330",
  "question": "台積電 2025 年全年合併毛利率是多少？請以營業毛利除以營業收入淨額計算，取小數點後一位。",
  "request": {"retrievalMode": "AnnualReportOnly", "sourcePolicy": "LocalOnly"},
  "expected_behavior": "answer",
  "facts": [{"label": "2025 毛利率", "value": 59.9, "unit": "%", "tolerance": 0.05, "tolerance_type": "abs"}],
  "must_include": [["毛利率"]],
  "must_not_include": ["保證獲利"],
  "sources": [{"document_type": "AnnualReport", "page": 9, "page_tolerance": 0, "title_contains": "2330 Annual Report 2025"}],
  "reference_answer": "2025 年毛利率約為 59.9%，由第 9 頁營業毛利除以營業收入淨額計算。",
  "verified": true
}
```

| 欄位 | 說明 |
|---|---|
| `category` | `numeric_lookup`、`multi_doc_synthesis`、`qualitative`、`unanswerable`、`policy` |
| `request` | 原樣併入 API 請求（`sourcePolicy`、`retrievalMode`、`documentType`、`topK`、`portfolioId`） |
| `expected_behavior` | `answer` 或 `insufficient_evidence`（應該拒答） |
| `facts` | 需要答對的數字。`unit` 支援 `%`、`百分點`、`元`、`億元`、`兆元`、`倍`；答案寫成「2.89 兆元」或「28,943 億元」都會被換算比對。`value: null` 代表尚未核對，這個 fact 不計分 |
| `must_include` | 每組至少命中一個同義詞，例如 `[["風險","不確定"]]` |
| `must_not_include` | 出現就判定失敗，例如「保證獲利」 |
| `sources` | 正確答案所在的文件和頁碼，用來區分是**檢索失敗**還是**生成失敗**。頁碼使用 PDF 實體頁數，從 1 開始，可能不同於印刷頁碼；有多筆時每筆都必須命中 |
| `reference_answer` | 給 LLM judge 的參考答案；沒填就不做 judge |
| `verified` | 已經對照原始 PDF 核實過。**只有 verified 的題目會計入 `pass_rate` 和所有品質指標**；草稿題（`verified: false`）預設不會執行，加 `--include-unverified` 才會跑，結果只列在報告的「Draft cases」區塊 |

**目前的考卷（13 題，2026-10-01 核實）**：11 題 `verified: true`，其中 6 題新完成原始 PDF 核實、5 題沿用拒答與政策題；2 題保留草稿（台積電毛利率變動原因、聯發科主要營運風險）。詳見 [逐題核實報告](../docs/eval-verification/2026-10-01/report.md) 與 [證據、文件雜湊及計算](../docs/eval-verification/2026-10-01/verification.json)。

新核實題中有 3 題的答案頁尚未進入目前索引：台積電毛利率（PDF 第 9 頁）、基本 EPS（第 58 頁），以及鴻海產品別收入（第 112 頁）。`verified` 表示標準答案有原件證據，不表示系統能答對；這些題仍保留在正式題庫以呈現檢索缺口。題庫核實階段未呼叫研究 API。後續已完成 [實際 API 評測](../docs/eval-verification/2026-10-01/live-api-results.md)：正式容器缺 embedding 設定，11 題皆 HTTP 500；相同映像與資料快照的隔離環境補入既有 embedding 金鑰後，取得 11 份 DeepSeek 回答，6/11 通過（54.5%），未啟用 LLM judge。

核實須對照原始 PDF，保存來源身分、實體頁碼、摘錄和計算，並抽查頁面影像；不能只憑模型記憶或搜尋摘要填入答案。現有 `AnnualReport` 原件實際為合併財報，管理層章節不足的原題維持草稿。

### 出題原則

- **題目核實完才改成 `verified: true`**：未核實的題目不會影響分數，但也不會被執行，所以核實的進度就是 eval 的覆蓋範圍。
- **每一類都要有題目**，特別是 `unanswerable`。在金融場景，「不知道卻硬答」比「答不出來」嚴重得多。
- **固定時間點**：寫「2025 年報」，不要寫「最新」，否則資料更新後標準答案會漂移。
- **一題考一件事**：一題同時考檢索、計算和推理，失敗時你會不知道是哪一環壞掉。
- 目標是 **20–30 題**。10 題以下雜訊太大，一題就等於 10%。
- 系統出錯時，把那個問題**加進考卷**，它就成了回歸測試。

---

## 評分：先用程式判斷，再用 LLM

跟 `docs/architecture/WORKFLOW_DESIGN_PRINCIPLES.md` 的原則一致：能用程式判斷的就不交給 LLM。

| 指標 | 怎麼算 | 抓出什麼問題 |
|---|---|---|
| `behavior` | 該答的有答、該拒答的有拒答 | 幻覺、過度拒答 |
| `facts` | 從答案抽出數字，換算單位後在容忍範圍內比對 | 數字錯誤 |
| `keywords` | `must_include` 覆蓋率；命中任何 `must_not_include` 直接 0 分 | 漏掉要點、違規陳述 |
| `citation_integrity` | 每個 `[n]` 都指向實際存在的引用 | 懸空引用 |
| `citation_coverage` | 含數字的句子中，有附引用的比例（不把年份算進去） | 無出處的數字 |
| `source_hit` | 答案**引用**了正確的頁面 | 引用錯頁 |
| `retrieval_recall` | 檢索結果裡**有沒有撈到**正確的頁面 | 檢索本身失敗 |
| `judge_correctness` / `judge_groundedness` | LLM 評審依評分標準給 1–5 分，再換算成 0–1 | 敘述題的正確性、主張是否有證據支持 |

**`source_hit` 和 `retrieval_recall` 要一起看**：

| source_hit | retrieval_recall | 診斷 |
|---|---|---|
| 高 | 高 | 正常 |
| 低 | 高 | 撈到了但沒選上或沒引用 → 調整 rerank 或 context selection |
| 低 | 低 | 根本沒撈到 → 調整 chunking、embedding 或 BM25 |

**判定通過（pass）**，只針對 verified 題目：
- `behavior` 正確；
- 所有適用的確定性分數都是滿分：每個 fact 都答對、`must_include` 每一組都命中、沒有禁用詞、沒有懸空引用；
- 有跑 LLM judge 時，`judge_correctness` 與 `judge_groundedness` 都至少 0.5（評分標準的 3/5）。「引用格式正確但內容錯誤」的答案不會通過。
- LLM judge 呼叫失敗或回傳無效內容時，已核實題目一律判定未通過，仍計入通過率分母；分數保留 `null`，每題的 `judge_error` 與摘要的 `judge_error_count` 分別記錄原因與出錯題數。這與研究 API 本身的 `error` 分開呈現。

草稿題的 `passed` 是 `null`，不算通過也不算失敗。`validate` 也會要求：verified 的作答題至少要有一個已核實的 fact 或 `must_include`，只有 `reference_answer` 的題目在沒開 judge 時無法用程式驗證。

**營運指標**：`hallucination_rate`（應拒答卻作答的比例）、`over_refusal_rate`、`error_rate`、延遲 p50/p95、平均 token 數、總成本（investigation 模式才有）。

### LLM judge

```bash
export EVAL_JUDGE_BASE_URL=https://api.deepseek.com/v1   # 任何 OpenAI 相容的 endpoint
export EVAL_JUDGE_API_KEY=...
export EVAL_JUDGE_MODEL=deepseek-chat
```

- 評審最好**和被測模型不同家**，自己評自己分數會偏高。
- `temperature 0`，評分標準有版本號（`JUDGE_PROMPT_VERSION`），不同次的結果才能比較。
- **抽查約 10% 的評分結果**，確認評審本身是可靠的。
- 沒設定環境變數時會自動略過 judge，只跑確定性評分。

---

## 建議的實驗

| 實驗 | 怎麼跑 | 看什麼 |
|---|---|---|
| RAG vs Agent | 同一份考卷分別用 `--mode ask` 和 `--mode investigation` 跑 | Critic／Remediation 迴圈帶來多少品質提升，又多花了多少錢和時間 |
| Prompt 迭代 | 改 prompt 版本前後各跑一次，再用 `compare` 比較 | 有沒有造成 regression |
| 檢索模式 | 切換 `Retrieval:RetrievalMode`（VectorOnly / Bm25Only / Hybrid） | `retrieval_recall`、`source_hit` |
| Jev 替換 Evidence Assessor | 替換實作前後各跑一次 | 品質是否持平，延遲與成本下降多少 |

每個實驗最後都能濃縮成一句履歷 bullet：「在 N 題 eval set 上，X 從 a 提升到 b，成本降低 c 倍。」

---

## 已知問題（Known issues）

以下為 **2026-10-01 查核與實測時仍未修復**的問題。完整依據見 [題庫核實](../docs/eval-verification/2026-10-01/report.md)、[來源與計算](../docs/eval-verification/2026-10-01/verification.json) 及 [實際 API 評測](../docs/eval-verification/2026-10-01/live-api-results.md)。這些文件保留在專案中，供原始 `runs/` 資料之外的問題追蹤使用。

| ID | 範圍／狀態 | 已確認現象 | 後續處理與驗收 |
|---|---|---|---|
| KI-001 | 正式服務設定／未修復 | 正式 `POST /api/research/ask` 的 11 次請求均 HTTP 500。日誌為 `OpenAI API key is not configured`；文件檢索在產生 embedding 時中斷，尚未進入 DeepSeek 回答階段 | 在正式環境安全配置 embedding 金鑰；實測研究端點成功回傳並產生查詢 embedding。隔離環境的成功不能當作正式服務已修復 |
| KI-002 | 年度 PDF 解析與入庫／未修復 | 2330／2025 p.9（毛利率）、p.58（基本 EPS），以及 2317／2025 p.112（產品表）存在於原件，卻沒有對應索引頁；也未出現在既有 `annual-report-chunks.keep.jsonl` 中 | p.9 是無可抽取文字的影像頁，需 OCR。p.58、p.112 可抽取文字，需查核 PdfPig 抽取及分類／保留規則；不能直接認定是 chunk 大小或後續字數篩選造成。補齊後核對原件數值、頁碼、chunk 及 embedding，重跑三題 |
| KI-003 | 檢索後證據取用／未定位 | `2454-revenue-2025` 正確 2025 p.10 在候選中 rank 15、trace 標記 selected，但回答引用其他年度並拒答；`retrieval_recall=1`、`source_hit=0` | 查最終格式化 context、完整表格是否送入模型及模型取用。selected 不能單獨證明模型看到了整頁；修復後應回答 5,959.65682 億元並引用 2025 p.10 |
| KI-004 | 政策題評分／未修復 | `2330-no-guaranteed-profit` 實際拒答並否定必然獲利；題庫預期 answer，behavior 判 0；禁用詞又命中重述問題中的「一定會賺」，keywords 判 0 | 釐清允許的風險回答與拒答行為定義，處理禁用詞的引述及否定語境。驗證安全回答與真正承諾獲利的反例；不可只刪除禁用詞或把所有拒答改判通過 |
| KI-005 | 題庫來源範圍／保留草稿 | `2330-gross-margin-yoy` 缺管理層對毛利率變動原因的說明；`2454-key-risks` 缺完整主要營運風險揭露。現有 AnnualReport 原件實際為合併財報 | 補足與原題範圍相符的官方來源並核實；完成前維持 `verified: false`，不計入正式品質指標 |

年度 extractor 會丟棄 Review／未分類頁，還有頁面品質門檻及保留上限；後續 filter 另有去重與字數門檻。這些是需要查核的機制，不能僅由缺頁就斷言某一條規則是單一根因。上表 KI-002 是抽取／保留／入庫問題，KI-003 則是已有 chunk 的後續取用問題，須分開驗收。

**目前基準的解讀**：正式首輪為 11/11 API error，無模型品質比較；使用相同映像及資料快照、補入既有 embedding 金鑰的隔離環境為 **6/11 通過（54.5%）**，財務數字題為 **2/6（33.3%）**，未啟用 LLM judge。這是既有 scorer 的結果，包含 KI-004 的行為定義與關鍵字限制。

問題修復後，在此更新狀態、修復版本及新評測報告連結；保留舊答案及舊分數。若修改 scorer，使用 `rescore` 對保存的舊回答寫出新結果；若修改系統，使用同一凍結題庫重新 `run`，再 `compare`。供團隊長期比較的基準 run 應另外歸檔，README 與摘要報告不取代完整原始回答。

---

## 已知限制

- 數字評分以答案中出現的數值匹配，尚未綁定 fact 標籤與數字。產品別占比題即使數字和名稱均齊全，仍須透過答案抽查或 LLM judge 確認配對及最高成長類別。
- 數字抽取依靠規則，極端寫法可能漏抓（例如「五成九」這種國字數字）。遇到時請補 `normalize.py` 並加測試。
- 數字比對嚴格保留正負號，支援 `-`、全形負號與 `−`；相反正負號不會因容忍誤差而匹配。暫不從「下降」「虧損」等文字推斷負值，負數需帶明確負號。零值仍可在原有容忍範圍內匹配。
- `sources` 靠頁碼比對，文件重新切 chunk 或頁碼偏移時要調整 `page_tolerance`。
- LLM judge 本身也會出錯，它的分數只能當趨勢參考，不能取代人工判斷。
- 題數少時，分數差 5% 以內可能只是雜訊，不要過度解讀。

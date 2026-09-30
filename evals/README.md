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
  "question": "台積電 2025 年全年毛利率是多少？",
  "request": {"retrievalMode": "AnnualReportOnly", "sourcePolicy": "LocalOnly"},
  "expected_behavior": "answer",
  "facts": [{"label": "2025 毛利率", "value": 59.1, "unit": "%"}],
  "must_include": [["毛利率"]],
  "must_not_include": ["保證獲利"],
  "sources": [{"document_type": "AnnualReport", "page": 45, "page_tolerance": 1, "title_contains": "2025"}],
  "reference_answer": "2025 年毛利率為 59.1%。",
  "verified": true
}
```

| 欄位 | 說明 |
|---|---|
| `category` | `numeric_lookup`、`multi_doc_synthesis`、`qualitative`、`unanswerable`、`policy` |
| `request` | 原樣併入 API 請求（`sourcePolicy`、`retrievalMode`、`documentType`、`topK`、`portfolioId`） |
| `expected_behavior` | `answer` 或 `insufficient_evidence`（應該拒答） |
| `facts` | 需要答對的數字。`unit` 支援 `%`、`元`、`億元`、`兆元`、`倍`；答案寫成「2.89 兆元」或「28,943 億元」都會被換算比對。`value: null` 代表尚未核對，這個 fact 不計分 |
| `must_include` | 每組至少命中一個同義詞，例如 `[["風險","不確定"]]` |
| `must_not_include` | 出現就判定失敗，例如「保證獲利」 |
| `sources` | 正確答案所在的文件和頁碼，用來區分是**檢索失敗**還是**生成失敗** |
| `reference_answer` | 給 LLM judge 的參考答案；沒填就不做 judge |
| `verified` | 已經對照原始 PDF 核實過。**只有 verified 的題目會計入 `pass_rate` 和所有品質指標**；草稿題（`verified: false`）預設不會執行，加 `--include-unverified` 才會跑，結果只列在報告的「Draft cases」區塊 |

**目前的考卷（13 題）**：`unanswerable` 和 `policy` 類共 5 題可以直接使用；其餘 8 題的數字和頁碼標了 `TODO`，要**你親自打開資料庫裡的那份 PDF 核對後填入**。這一步不能交給 AI 代勞：標準答案本身錯了，整份考卷就沒有意義。

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

## 已知限制

- 數字抽取依靠規則，極端寫法可能漏抓（例如「五成九」這種國字數字）。遇到時請補 `normalize.py` 並加測試。
- `sources` 靠頁碼比對，文件重新切 chunk 或頁碼偏移時要調整 `page_tolerance`。
- LLM judge 本身也會出錯，它的分數只能當趨勢參考，不能取代人工判斷。
- 題數少時，分數差 5% 以內可能只是雜訊，不要過度解讀。

# Jev 證據判斷影子測試

此試點只觀察補充檢索節點的本地候選片段。Jev 的結果寫入 `jevEvidenceTriageShadow` tool call 和節點事件；原本的排序、Web 檢索、Evidence Assessor、驗證與回答均照常執行。現行動態流程把本地節點的 `allowWebFallback` 設為 `false`，另外規劃 Web 節點，因此需在完整 run 的節點紀錄中比較實際 Web 行為。

## 啟用方式

設定於**測試用 API 行程或容器**的環境變數，功能預設關閉：

```text
TYPESAFE_API_KEY=<由祕密管理提供，不寫入設定檔或 Git>
JevEvidenceTriage__Enabled=true
JevEvidenceTriage__AllowedUserIds__0=<手動建立公開研究問題的測試帳號 UUID>
JevEvidenceTriage__AllowedPublicHosts__0=www.sec.gov
JevEvidenceTriage__AllowedPublicHosts__1=mops.twse.com.tw
JevEvidenceTriage__Model=jev-1.13.0
JevEvidenceTriage__TimeoutSeconds=3
```

測試帳號白名單、公開來源 HTTPS 網域白名單和 API key 缺一不可。來源由資料庫中的 `Document.SourceUrl` 與 `UploadedFile.UploadedByUserId` 核對；使用者上傳、來源不明、非白名單網域、內容超過 6000 字的片段都不送出。每輪最多評估八筆候選及五個研究面向；若任何候選未評估或面向超出上限，`wouldSearchWeb` 記為 `null`。測試帳號應只提出人工撰寫的公開證券研究問題，避免將個人資訊放入問題文字。

每個可評估片段送出一次 TypeSafe `POST /v1/systemone`，同時詢問相關性、是否直接回答、是否包含反面證據，以及各研究面向是否獲得具體證據。`wouldSearchWeb` 只是比較訊號：完整評估時，若有面向找不到同時「相關、涵蓋該面向，且直接回答或提供反面證據」的片段，就記為 `true`；各條件暫以 `0.5` 比較。保留原始分數供事後分析，不將這個門檻用於正式流程。API 失敗或逾時時，既有研究流程繼續執行。

## 人工標註與評估

從已核對的公開財報或公告取至少 100 組不同的「問題 × 片段」，納入中文與英文、時間或數字相近但不吻合的案例、反面證據，以及已有兩筆以上結果卻無法回答的案例。標註者先閱讀原文與研究問題，再獨立判定相關、直接回答、反面證據及是否需要 Web 補搜；不要直接抄 Jev 或舊 LLM 的輸出。標註檔放在未追蹤的 `exports/benchmark/`，不含片段全文或金鑰。

JSONL 每行是一組標註，例：

```json
{"question_id":"q-001","chunk_id":"<document-chunk-uuid>","source_url":"https://www.sec.gov/Archives/example","language":"zh","gold_relevant":true,"gold_direct":false,"gold_contradiction":false,"gold_web_needed":true,"old_web":false,"jev_would_web":true,"jev_relevance":0.8,"jev_direct":0.1,"jev_contradiction":0.1,"input_tokens":300,"duration_ms":400}
```

`old_web` 取完整 run 的實際 Web 節點／舊 fallback 紀錄；`jev_would_web` 取影子結果。若因跳過片段而無法判斷，填 `null`。分數、token、時間來自 `jevEvidenceTriageShadow` 的 `ResultJson`；不要把示例值當作實測。執行：

```bash
python3 scripts/evaluate_jev_shadow.py exports/benchmark/jev-labels.jsonl \
  --allowed-host www.sec.gov --allowed-host mops.twse.com.tw
```

評估器要求至少 100 筆人工標註，報告相關性、直接回答和反面證據的 precision/recall、舊規則與 Jev 建議各自漏掉的 Web 缺口、不必要補搜、輸入 token 估算費用，以及 Jev 呼叫延遲。每百萬輸入 token 的預設估算單價為 2026-09-24 官方文件所列的 US$0.042；執行時可用 `--usd-per-million-input` 更新。端到端研究延遲須另從 run 時間量測。

參考：[TypeSafe API](https://docs.typesafe.ai/api)、[模型與限制](https://docs.typesafe.ai/models)、[Jev 已知限制](https://docs.typesafe.ai/model-jaggedness/jev-1.13)。

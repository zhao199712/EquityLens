# Research Agent Runtime 正式部署紀錄

> 本文為歷史部署紀錄。目前已移除 DynamicDag，下方切換引擎的回退指令不再適用。新版手動部署／映像回復方式見[操作手冊](../operations/manual-deployment.md)。

部署時間：2026-11-03 00:02 Asia/Taipei。使用者明確授權備份、合併 main、更新正式資料庫並啟用新引擎。

## 已完成

- 舊 main 的遠端 Git 備份：`backup/main-before-research-runtime-20261102-434e645`。
- 合併提交：`402ccf7`；正式 API 映像：`sha256:63bd30a83ac1b07fdbe0461539b8cadf08f1a64192de582b846ab6e556e1dcdf`。
- 正式 compose 預設 `AgentRuntime__ResearchEngine=AgentLoop`、`DeepSeek__ThinkingEnabled=false`。其餘原有環境設定已比對，未變更。正式 reranker 保留既有設定，並非隔離 A/B 的 None。
- 四項 migration 已套用至正式 PostgreSQL，最新為 `20261102140017_AddToolCallApprovals`。
- 只重建／重啟 API；沒有重建資料庫或清除 volume。API 停止寫入後另做一份 quiesced 備份。
- API 已 healthy；部署前 6 筆 WaitingForApproval、2 筆 Pending 紀錄保留。沒有替使用者做審批或取消決策。

## 備份位置

主機私有目錄：`/home/ymsh20220/.local/state/equitylens/backups/pre-agent-loop-20261102T155857Z`（目錄 0700，資料庫及環境快照 0600，不提交 Git）。

- `database.dump`：部署準備時的備份。
- `database-quiesced.dump`：API 停止後、migration 前的備份；已透過 pg_restore 解碼至 /dev/null，驗證壓縮檔可完整讀取，未聲稱做過完整還原演練。
- `database-quiesced.sha256`：備份雜湊。
- `api-before.json`：舊容器環境與設定，含敏感值，僅存於上述私有目錄。
- 舊映像保留標籤：`equitylens-prod-api:pre-agent-loop-20261102T155857Z`。

## 正式煙霧驗證

透過公開 HTTPS API、獨立 deployment verification 帳號提交台積電 EPS 問題，**省略 engine**：

- workflowType：`ResearchAgent`；runtime：`Succeeded`；研究結果：`Answered`。
- 兩輪 LLM turn，答案基本 EPS 66.26 元，引用第 10、58 頁。
- Agent run：`d01523e8-8d78-4c40-9e29-797f38cd4130`。
- 該時段觀察到 3 次 provider LLM 呼叫，未快取尖峰估價 US$0.002754，不含 embedding／reranker，非帳單。
- 去除憑證的結果見 [production-smoke.json](../eval-verification/2026-11-02-agent-loop-ab/production-smoke.json)。驗證帳號／token 不提交版本庫。

## 回退方式

優先使用新 binary 切回舊引擎預設，不回滾資料庫：

```bash
RESEARCH_ENGINE=DynamicDag docker compose -f docker-compose.prod.yml up -d --no-deps api
```

此命令只影響該次 compose；若要持續回退，將主機 `.env` 的 `RESEARCH_ENGINE` 設為 `DynamicDag`。已存在的 ResearchAgent run 仍需要新 binary 執行完畢，不能直接換舊 binary 並假定所有 run 相容。

資料庫還原會覆蓋備份後的新寫入，不能當作一般引擎切換步驟。需要還原時先停止寫入、保留最新快照，再規劃從私有 dump 還原；此次未執行 Down migration 或資料庫還原。

## 後續範圍

夜間續跑指令已更新為保留正式 AgentLoop 狀態；後續功能在功能分支與隔離環境驗證，不由排程自行再次部署。2026-11-03 10:00 停止新增功能並停用續跑的期限不變。


## 投組情境功能部署（2026-11-03 02:12）

使用者再次明確要求「部署 推到GitHub」。已將功能分支合併至 main，merge commit `d8af5ef`（包含實作 `d2a0b71`、文件 `19af69f`）。

- Git 備份：`backup/main-before-portfolio-scenarios-20261103`，指向部署前 `b8340a7`。
- 私有備份：`/home/ymsh20220/.local/state/equitylens/backups/pre-portfolio-scenarios-20261102T181127Z`。包含 `database.dump` 與 `api-before.json`，目錄 0700、檔案 0600。dump 以 pg_restore 完整解碼至 /dev/null；不是完整還原演練，也不是停止寫入後的 quiesced 備份。
- 舊 API 映像標籤：`equitylens-prod-api:pre-portfolio-scenarios-20261102T181127Z`。
- 新 API 映像：`sha256:d97fe450b66aec2947ecb385e728de855ed00cb103cb9cef4ef2b455f7890967`。僅重建並重啟 api；本次沒有 migration，不變更持倉或交易資料。
- compose 環境逐項核對與舊容器一致；仍為 AgentLoop、thinking=false。API 容器 healthy，直接 API `/healthz` 回傳 status=ok；瀏覽器能讀取既有正式投組報告。
- 部署前本地驗證：後端 977 項、前端 124 項及前端 build 通過；最後參數防禦修改另跑情境 16 項通過。完整數學與隔離瀏覽器證據見[情境驗收](../eval-verification/2026-11-03-portfolio-scenarios/README.md)。

- Cloudflare Pages 對 `d8af5ef` 回報 success；重新載入正式網站確認新版品質／逐股風險缺漏標示，既有登入與歷史報告仍可用。本次煙霧驗證沒有新增模型呼叫；未把舊報告改寫成新情境結果。

### 03:09 部署後遠端檢查

main `8e8e0c6`（僅後續 README 修改）的 Cloudflare Pages、Backend、Frontend、Mathematics 與 Eval harness 檢查皆成功。正式 API 映像仍為上述 `d97fe450…`，健康且保持 AgentLoop 預設。完整 SHA、檢查時間及來源連結見 [release-checks.json](../eval-verification/2026-11-03-portfolio-scenarios/release-checks.json)。此次沒有重新部署或重跑模型。

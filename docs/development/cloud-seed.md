# Codex Cloud 開發資料

這套資料用於隔離開發，非正式部署、非歷史指數研究資料。程式與匯入工具在本 repo；資料包已發布於私有 [EquityLens-SeedData／seed-v2-20261105](https://github.com/zhao199712/EquityLens-SeedData/releases/tag/seed-v2-20261105)。版本與 SHA-256 由 `data/seed/release-lock.json` 鎖定，下載不跟隨 latest。

## 資料範圍（v2）

- 使用者指定十檔：2330、2454、2317、2308、3711、1303、2383、2881、2891、2303，另加 0050 與加權價格指數 `^TWII`。持股權重合計 78.02%，來源為使用者提供、快照日期未指定，並非歷史各期權重。
- Yahoo 最長可得歷史，截止 **2026-11-04**，12 個標的、73,953 筆日線。逐檔起點與下載 SHA-256 在 manifest；3711 僅採 2018-04-30 掛牌後資料，較早的供應商延伸序列不冒充現公司歷史。
- Yahoo `Close` 已調整分割，不能當成交易所未還原價格；`Adj Close` 為供應商調整價。這版完整替換行情，不與 v1 的不同價格口徑直接拼接。`^TWII` 使用 Close，代表不含息的價格指數。
- 保留 v1 的台積電／鴻海財報、857 個 chunks／1536 維 embedding、689 筆另外命名的加權總報酬指數（截止 2026-11-02）；不以 `^TWII` 取代總報酬指數。
- 無 PDF、匯率、正式帳號、持倉、金鑰或對話。仍建立兩位示範使用者及五個合成投組；分散投組改用這十檔。
- **資料品質限制**：還原因子尚未逐事件官方核對。7 個超過 ±25% 的報酬跳點保存在 `historyAudit.largeReturns`，包含 0050 的 2014-01-02。歷史 eval 將該日價格視為不可用，兩側報酬均不計，不補值或自動修復。Seed 保存供應商原價供稽核；其他直接使用 DB 的長期風險功能不會自動套用 eval 的隔離規則。這是開發／診斷資料，不可把排除後結果當成已認證的尾部檢定結論。

新版歷史回補與兩類 eval 操作見 [歷史 seed eval](../testing/history-seed-eval.md)。

## 環境前提

Linux、Python 3、.NET 10 SDK、Node.js/npm，以及**可運作的 Docker daemon 和 Compose v2**。資料庫必須使用 ParadeDB，不能換成普通 PostgreSQL。這不是保證所有 Codex Cloud 帳戶環境都有 Docker；請先執行 `docker info`，若宿主不提供 Docker，需選擇支援容器的執行環境，不能連正式資料庫替代。

初始化階段需要 NuGet、npm、容器 registry、GitHub API／Release assets 的網路存取。把 `GH_TOKEN` 放在 Cloud 的 setup secret 中，限定為私有 seed repo 的 Contents read 權限。不要把 token 寫入 setup script 或 Git。App 的 GitHub connector 授權不等於 setup shell 自動有這個 token。

## 初始化與啟動

從 repository 根目錄執行：

```bash
python3 scripts/seed/fetch.py --output /tmp/equitylens-seed-v2
python3 scripts/seed/runtime.py setup
```

`setup` 建立獨立 ParadeDB／Redis、套用本分支 EF migrations、驗證並匯入資料、建立隨機登入密碼、安裝前端依賴。私有 runtime 設定與密碼保存於 `/tmp/equitylens-seed-runtime/`，檔案權限 0600，不提交到 Git。

```bash
# API 在本機 55440；程序持續執行
python3 scripts/seed/runtime.py api

# 另開 terminal：前端使用 seed API
cd src/EquityLens.Web
VITE_API_BASE_URL=http://127.0.0.1:55440/api npm run dev
```

目前前端 `http.ts` 會讀取 `VITE_API_BASE_URL`，值須包含 `/api`。登入帳號 `seed-user-1@example.invalid`、`seed-user-2@example.invalid`，密碼從 private `credentials.json` 讀取，不印到共享日誌。

畫面若查「今天／最近一個月」，日後會超出固定 seed 期間；請手動指定 **2026-06-01～2026-11-04**。近期風險可使用 2025-01-01～2026-11-04；完整十檔組合可從 2018-04-30 起建立價格窗。Seed 不覆寫系統時鐘，也不宣稱資料是即時行情。

`MarketData__FinMind__CachedOnly=true` 只讀 DB 基準，`BackgroundJobs__Enabled=false` 不處理同步工作佇列。Redis 與 DB 都是專用容器。LLM／embedding／reranker／外部行情 key 被清空，endpoint 指向不可用的 loopback 位址；不會為了初始化呼叫付費模型。已有向量可驗證 DB 檢索；真實問題的 query embedding、答案生成與 rerank 需要另行配置服務，並不屬於這份 seed 的品質驗證。

預設停用 Python primary/shadow risk worker，API smoke 驗證 C# 風險入口。跨語言風險整合測試請依 `docs/testing/pr-validation.md` 使用另外的隔離服務。

## 驗證

```bash
python3 -m unittest discover -s scripts/seed -v
python3 scripts/seed/seed.py validate --directory /tmp/equitylens-seed-v2
# API 啟動後
python3 scripts/seed/smoke.py
```

Smoke 驗證兩個帳號登入、4/1 個投組歸屬、跨使用者 404、歷史淨值與現金、離線基準、C# 風險、PDF 不可下載、真正的 BM25 和向量搜尋。向量查詢使用已有文件向量來測管線，不代表自然語言檢索品質。

重跑同一版本會核對 receipt 與資料筆數、不覆寫密碼或新增重複資料。若已有開發變更導致筆數／帳務驗證不同，會停止；換版本或重置請建立新的隔離資料庫。不得以 `TRUNCATE`／清除正式資料讓驗證通過。

## v1 來源匯出（保留相容性）

```bash
# 僅由有來源資料庫權限的維護者執行；整段 transaction 強制 READ ONLY。
python3 scripts/seed/seed.py export \
  --container equitylens-prod-postgres \
  --constituents data/seed/constituents-2026-11-04.json \
  --output /tmp/equitylens-seed-next
```

輸出 `equitylens-seed-v1.json.gz` 與 `manifest.json`。先在新隔離 DB 匯入、執行 smoke，再發布新的私有 Release tag，更新 Git 中 lock 的檔案大小與 SHA-256。不覆蓋舊版本，不把資料包加入公開程式 repo。來源 snapshot 的 migrations 記錄於 manifest；目標須已套用所有來源 migrations（可有額外新 migration）。

Importer 只接受命名為 `equitylens-seed-*`、有 `equitylens.seed=true` label 的 Docker 容器，以及 `equitylens_*seed` 資料庫；首次匯入要求 domain tables 全空，在同一 transaction 寫入。來源資料包只允許固定 11 個資料表，不能攜帶 SQL 或帳戶。Git 中 checksum 是下載信任依據；不要只相信同一下載位置提供的 checksum。

## v1 歷史驗證紀錄

2026-11-05：Python seed guards 13/13；相關後端測試 22/22、無 skip。完整 Compose → EF migrations → seed import → npm ci 已執行。相同版本重跑未新增資料或更換密碼。API smoke：2 位普通使用者、5 個投組、跨 owner 404、155 個歷史估值點、離線基準可用、C# 風險計算成功、缺 PDF 404、BM25 3 筆／向量檢索 5 筆，付費 AI 呼叫 0。這是在本機 Docker 隔離環境的驗證，尚不代表使用者帳戶中的 Codex Cloud 執行環境已配置完成。

私有 Release 已完成上傳及實際下載驗證：資料包與 manifest 的大小、SHA-256 均與 Git 中 lock 相符，下載後資料關聯、共同交易日及 embedding 完整率驗證通過。

## v2 驗證紀錄

2026-11-05：18 個 seed guards、190 個 eval 離線測試、3 個可變股票池研究測試通過。實際下載 12 檔 Yahoo 歷史，兩次從同一 source snapshot 重建得到相同 gzip SHA-256。新版資料已匯入另一個空的隔離資料庫；API smoke 通過兩帳號、五投組、跨 owner 404、157 個估值點、近期 C# 風險、BM25 3 筆及向量 5 筆。財報和 embedding 維持 v1。兩種歷史股票池皆可供既有風險 runner 讀取並產生 205 組有效權重。未重跑完整風險研究或付費 AI。

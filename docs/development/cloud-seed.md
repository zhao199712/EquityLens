# Codex Cloud 開發資料

這套資料用於隔離開發，非正式部署、非歷史指數研究資料。程式與匯入工具在本 repo；資料包預定存放於私有 `zhao199712/EquityLens-SeedData` 的固定 Release；發布完成後才能執行下載步驟。版本與 SHA-256 由 `data/seed/release-lock.json` 鎖定，下載不跟隨 latest。

## 資料範圍

- 元大 2026-11-04 的 0050 官方持股名單，排除 6446，另外加入 0050，共 50 個標的。原始名單及權重只是當日快照，不代表過去各年的指數成分或示範投組配置。
- 行情截止日為共同可用日期 **2026-11-02**，保留既有歷史，共 85,114 筆日線；全部標的有 470 個共同價格日期（2024-11-01 起）。10 筆零收盤價排除並記錄於 manifest；7769 的 1 筆還原價缺漏保留，不補值。其餘價格亦未做新的公司行動調整正確性認證。
- 台積電／鴻海：2023～2025 年報共 6 份、4 份法說會、70 筆結構化財報主檔與 2,980 筆科目、857 個 chunks 與 857 個 `text-embedding-3-small`／1536 維 embedding。
- 689 筆加權總報酬指數（2023-12-25～2026-11-02）。較早區間缺少基準，保留缺漏，不填造或自動抓取。
- 沒有 PDF、匯率、正式使用者、正式持倉、token、對話或執行紀錄。檔案 metadata 移除正式 owner／儲存路徑；下載 PDF 回傳 404。
- 匯入時生成兩個普通帳號，第一位有 0050 ETF、台股分散、台積電／鴻海集中、空投組，第二位有獨立 ETF 投組。合成交易包含買入、部分賣出、手續費、入金及出金；不偽造公司股息。持倉與 FIFO 成本相符。

## 環境前提

Linux、Python 3、.NET 10 SDK、Node.js/npm，以及**可運作的 Docker daemon 和 Compose v2**。資料庫必須使用 ParadeDB，不能換成普通 PostgreSQL。這不是保證所有 Codex Cloud 帳戶環境都有 Docker；請先執行 `docker info`，若宿主不提供 Docker，需選擇支援容器的執行環境，不能連正式資料庫替代。

初始化階段需要 NuGet、npm、容器 registry、GitHub API／Release assets 的網路存取。把 `GH_TOKEN` 放在 Cloud 的 setup secret 中，限定為私有 seed repo 的 Contents read 權限。不要把 token 寫入 setup script 或 Git。App 的 GitHub connector 授權不等於 setup shell 自動有這個 token。

## 初始化與啟動

從 repository 根目錄執行：

```bash
python3 scripts/seed/fetch.py --output /tmp/equitylens-seed-v1
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

畫面若查「今天／最近一個月」，日後會超出固定 seed 期間；請手動指定 **2026-06-01～2026-11-02**。風險可使用 2025-01-01～2026-11-02，完整股票池回測需將起點提前至 2024-11-01。Seed 不覆寫系統時鐘，也不宣稱資料是即時行情。

`MarketData__FinMind__CachedOnly=true` 只讀 DB 基準，`BackgroundJobs__Enabled=false` 不處理同步工作佇列。Redis 與 DB 都是專用容器。LLM／embedding／reranker／外部行情 key 被清空，endpoint 指向不可用的 loopback 位址；不會為了初始化呼叫付費模型。已有向量可驗證 DB 檢索；真實問題的 query embedding、答案生成與 rerank 需要另行配置服務，並不屬於這份 seed 的品質驗證。

預設停用 Python primary/shadow risk worker，API smoke 驗證 C# 風險入口。跨語言風險整合測試請依 `docs/testing/pr-validation.md` 使用另外的隔離服務。

## 驗證

```bash
python3 -m unittest discover -s scripts/seed -v
python3 scripts/seed/seed.py validate --directory /tmp/equitylens-seed-v1
# API 啟動後
python3 scripts/seed/smoke.py
```

Smoke 驗證兩個帳號登入、4/1 個投組歸屬、跨使用者 404、歷史淨值與現金、離線基準、C# 風險、PDF 不可下載、真正的 BM25 和向量搜尋。向量查詢使用已有文件向量來測管線，不代表自然語言檢索品質。

重跑同一版本會核對 receipt 與資料筆數、不覆寫密碼或新增重複資料。若已有開發變更導致筆數／帳務驗證不同，會停止；換版本或重置請建立新的隔離資料庫。不得以 `TRUNCATE`／清除正式資料讓驗證通過。

## 更新資料與發布

```bash
# 僅由有來源資料庫權限的維護者執行；整段 transaction 強制 READ ONLY。
python3 scripts/seed/seed.py export \
  --container equitylens-prod-postgres \
  --constituents data/seed/constituents-2026-11-04.json \
  --output /tmp/equitylens-seed-next
```

輸出 `equitylens-seed-v1.json.gz` 與 `manifest.json`。先在新隔離 DB 匯入、執行 smoke，再發布新的私有 Release tag，更新 Git 中 lock 的檔案大小與 SHA-256。不覆蓋舊版本，不把資料包加入公開程式 repo。來源 snapshot 的 migrations 記錄於 manifest；目標須已套用所有來源 migrations（可有額外新 migration）。

Importer 只接受命名為 `equitylens-seed-*`、有 `equitylens.seed=true` label 的 Docker 容器，以及 `equitylens_*seed` 資料庫；首次匯入要求 domain tables 全空，在同一 transaction 寫入。來源資料包只允許固定 11 個資料表，不能攜帶 SQL 或帳戶。Git 中 checksum 是下載信任依據；不要只相信同一下載位置提供的 checksum。

## 本次驗證紀錄

2026-11-05：Python seed guards 13/13；相關後端測試 22/22、無 skip。完整 Compose → EF migrations → seed import → npm ci 已執行。相同版本重跑未新增資料或更換密碼。API smoke：2 位普通使用者、5 個投組、跨 owner 404、155 個歷史估值點、離線基準可用、C# 風險計算成功、缺 PDF 404、BM25 3 筆／向量檢索 5 筆，付費 AI 呼叫 0。這是在本機 Docker 隔離環境的驗證，尚不代表使用者帳戶中的 Codex Cloud 執行環境已配置完成。

# PR 自動驗證

`.github/workflows/main.yml` 在 PR → main、push main 與手動 dispatch 執行。
PR 使用 `actions/checkout` 預設測試合併版本；不在 workflow 強制 checkout 功能分支，避免只測到未合併版本。

## 驗證層次

| Job | 實際驗證 | 不代表什麼 |
|---|---|---|
| Backend build and test | Release build、全部 xUnit、真實隔離 ParadeDB 的交易／租約／審批整合測試 | 沒有啟動完整 HTTP 服務或正式資料 |
| Risk cross-language and service integration | C#／Python 雙向資料契約；真實 DB → C# service → Redis → Python worker → C# result worker → DB | 不涵蓋 HTTP／JWT middleware、正式行情品質、worker 故障恢復或兩套數學模型的黃金資料比對 |
| Browser smoke (mock API, production build) | 正式前端 bundle、Chromium 操作、路由、API request payload、SSE／polling、畫面及錯誤狀態 | API 全部為固定回應；不驗證真實登入、後端端點契約、資料庫持久化或模型品質 |
| Frontend build and test | Vue／TypeScript build、Vitest 元件與服務回歸 | 不能取代真實瀏覽器 |
| Mathematics tests | 確定性風險計算函式及共用 fixture | 不是正式行情品質驗證 |
| Eval harness tests | 題庫格式與評分器測試 | 不呼叫 AI，不產生新的 8 題 eval 分數 |

本輪不設定自動部署、付費 AI eval、GitHub 分支保護或 Merge Queue。實際後端與瀏覽器連接的完整 E2E、OAuth、真實檢索與模型回答，仍需另外在隔離環境驗收。

## PostgreSQL 不可靜默略過

CI service 使用與開發 compose 相同的 `paradedb/paradedb:0.24.1-pg18`，包含 migration 所需的 vector／pg_search／jieba。只含 CI 測試帳密，無正式 secret。
測試 fixture 會建立隨機資料庫、執行 migrations，結束後刪除；需要可建立資料庫的測試使用者。

`EQUITYLENS_TEST_POSTGRES_CONNECTION_STRING` 在 backend job 設定。測試後執行 `scripts/ci/check_backend_trx.py`，缺 TRX、空報告、失敗／略過、沒有 PostgreSQL 整合測試或已列出的整合測試沒有執行結果都會使 job 失敗。TRX 無論成功或失敗都上傳，保存 14 天。

本機可使用獨立 ParadeDB，勿把測試連線指向正式資料庫：

```bash
EQUITYLENS_TEST_POSTGRES_CONNECTION_STRING='Host=localhost;Port=55434;Database=equitylens_ci;Username=equitylens_ci;Password=ci_only_password' \
  dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj --configuration Release \
  --logger 'trx;LogFileName=backend-tests.trx' --results-directory /tmp/equitylens-ci-backend
python3 scripts/ci/check_backend_trx.py /tmp/equitylens-ci-backend/backend-tests.trx
python3 -m unittest discover -s scripts/ci -v
```

## 風險跨語言契約與真實串接

獨立專案 `tests/EquityLens.Risk.IntegrationTests` 共五個案例，CI 每個 PR 自動執行，無付費 API 或行情下載：

- 兩個雙向契約案例：由正式 C# 型別序列化輸入，交給正式 Python Pydantic model 與計算引擎，再由 C# 反序列化真實回傳結果。驗證日期、GUID、權重、報酬矩陣、中文及可省略的資產 metadata、輸入 hash、回測 DTO 與風險結果結構。
- 兩個負向案例：欄位名稱不符或權重與矩陣維度不符時，Python 必須拒絕輸入。
- 一個服務串接案例：migrate 隨機隔離資料庫，寫入合成持股及 360 天價格，透過正式 DI 的風險服務建立 run。實際 Python worker 消費 Redis Streams，由正式 C# result worker 寫回資料庫。確認 Python 模型、fallbackDepth=0、job／run／input hash 對應、兩側 ACK、無 dead letter、保存結果與 Python 輸出相同、使用者隔離及持倉不變。

串接資料刻意缺一個標的的一天價格，驗證共同日期對齊後有 358 筆報酬；條件風險模型使用最近 252 筆，完整歷史報酬仍有 358 筆。此為確定性的合成資料，不代表真實市場交易日或行情品質。

測試自動啟動及終止 Python worker／C# result worker，以 UUID 隔離 Redis keys，結束只刪除此測試的 keys 與資料庫，不使用 FLUSHDB。缺少 Python、連線或測試結果直接失敗；TRX guard 要求兩個測試類別都有執行且零略過。CI 上傳原始合成輸入、Python 回傳、持久化回應、worker log 與 TRX，保存 14 天。

本機先啟動專用 ParadeDB 與 Redis；資料庫使用者需有 CREATE DATABASE 權限。以下連線僅是測試環境範例：

```bash
python3 -m venv /tmp/equitylens-risk-test-venv
/tmp/equitylens-risk-test-venv/bin/pip install -e src/EquityLens.Mathematics
export RISK_TEST_PYTHON=/tmp/equitylens-risk-test-venv/bin/python
export RISK_TEST_POSTGRES='Host=localhost;Port=55434;Database=equitylens_ci;Username=equitylens_ci;Password=ci_only_password'
export RISK_TEST_REDIS='localhost:56381'
export RISK_TEST_REDIS_URL='redis://localhost:56381/0'
export RISK_TEST_ARTIFACTS=/tmp/equitylens-risk-results/evidence
dotnet test tests/EquityLens.Risk.IntegrationTests/EquityLens.Risk.IntegrationTests.csproj \
  --configuration Release --logger 'trx;LogFileName=risk-integration.trx' \
  --results-directory /tmp/equitylens-risk-results
python3 scripts/ci/check_backend_trx.py /tmp/equitylens-risk-results/risk-integration.trx \
  --required-class RiskContractTests --required-class RiskPipelineTests
```

只跑較輕的契約測試可加 `--filter FullyQualifiedName~RiskContractTests`，只需要 Python，不需要 PostgreSQL／Redis；這種局部執行不可視為完整串接通過，也不會通過上述要求兩個 suite 的 guard。

## 瀏覽器 smoke

```bash
cd src/EquityLens.Web
npm ci
npx playwright install --with-deps chromium
npm run test:e2e:ci
```

此命令先檢查測試 TypeScript、建置前端，再自行啟動 `127.0.0.1:5186` production preview。不重用正在執行的本機服務；不啟動 API，不讀本機帳密。API base 固定為同源 `/api`，所有 API 逐條攔截；未定義的 API 或非預期外連直接使測試失敗，沒有 fallback 到正式站。Google OAuth SDK 使用本機 stub，字型請求封鎖，不測試第三方登入。Service Worker 停用，避免繞過攔截。

目前九個案例：

- 公開手機選單、點選後關閉與 production 樣式頁 404。
- 未登入路由導向登入、登入失敗不建立 token。
- 登入後建立投組、搜尋台積電、新增交易，檢查冪等 key、授權標頭及重新載入畫面；持久化僅由 fixture 模擬。
- 風險頁顯示已保存結果、快照綁定指定 Run ID、不重算。
- Research 提出台積電問題，輪詢至答案，展開引用、節點、工具紀錄並重新載入。
- 研究 API 失敗顯示錯誤，不導向不存在的 Run。
- 執行成功但品質 gate 拒答，仍顯示拒答原因及被擋數字。
- Ask Agent 投組波動問題：SSE → run card 輪詢 → 詳情，長報告無透明祖先。
- Ask Agent 財報問題：SSE → run card 輪詢 → 詳情；未完成草稿不顯示。

每個案例有獨立 context 與資料狀態，未捕捉的瀏覽器例外會使測試失敗。CI 禁止 `.only`，不自動 retry 掩蓋不穩定；失敗保留 screenshot／trace，HTML report 與結果上傳 14 天。本機產物在 `.gitignore`。
原 `npm run test:e2e` 是歷史 live-backend 套件，仍獨立保留；它需要舊版測試帳號與 E2E runtime，不納入此 CI job，也不宣稱已驗證。

## 合併與發布

PR 頁面確認上述 job 全綠，再進行 code review。若要強制禁止跳過，另由 repository 管理者設定 required status checks；這份 workflow 本身不能阻止直接 push main。
更新 main 後需重新測試合併結果；多人開發可另評估 merge queue。合併不等於部署，發布後仍需 smoke test 與回復方案。

參考：[GitHub PostgreSQL services](https://docs.github.com/en/actions/tutorials/use-containerized-services/create-postgresql-service-containers)、[Playwright CI](https://playwright.dev/docs/ci)、[Playwright API mocking](https://playwright.dev/docs/mock)。

## 本次驗證（2026-11-03）

- 新建暫存 ParadeDB 容器，以 CI 相同 image／帳號設定執行 Release 測試：931 passed、0 skipped；TRX guard 確認其中 18 個 PostgreSQL 測試有執行。
- 移除連線設定再跑整合測試，確認略過結果會被 guard 拒絕；guard 自身 5 個單元測試通過。
- Chromium production smoke：9/9；測試 TypeScript 檢查通過，沒有未捕捉的瀏覽器例外或未定義 API。
- 完整 R27：後端 931/931、0 skipped，eval pytest 83、前端 Vitest 135，前後端 build 與 EF pending-model 通過。日誌 `/tmp/ci-validation-r27-*.log`。
- 未修改產品功能、正式資料或導覽列，未執行付費 API、部署或分支保護變更。GitHub PR 的 Actions 結果另以 PR checks 為準，以上是本機實跑證據。

### 風險串接補強驗證（2026-11-03）

- 新增 suite：Release 5/5、0 skipped；使用專用 ParadeDB／Redis 容器與真實 Python worker，約 7 秒完成測試。
- TRX guard 單元測試 6/6，要求契約與串接兩個 suite，缺少任一類別會失敗。
- 原有完整 R27：後端 931/931、0 skipped，eval harness 83、前端 Vitest 135，前後端 build 與 EF pending-model 通過。日誌 `/tmp/risk-integration-r27-*.log`。
- 本機新測試使用 Python 3.12；CI 使用 3.13，遠端結果以 PR checks 為準。
- 本次僅新增測試、CI 與文件，未修改正式程式、資料庫或服務，未呼叫外部 AI／行情 API；第三項黃金資料集跨引擎數值比對未納入。

# 手動部署 API 與 Mathematics

入口：`scripts/deploy/deploy.sh`；流程實作：同目錄 `deploy.py`（Python 標準函式庫）。目前沒有部署 workflow、runner 或自動部署。合併 PR 不會透過這支腳本重啟正式服務。

## 使用前

- 在正式 Linux 主機執行，需要 Bash、Python 3.12+、Git、Docker Engine／Compose v2、curl；Docker context 必須使用本機 Unix socket。
- 保留一份乾淨的工具 checkout。部署原始碼由 `git archive <SHA>` 匯出，不 checkout／覆蓋目前分支，也不帶入未提交檔案、忽略的 appsettings、`.env`、bin 或 obj。
- 目標必須是完整 SHA、位於更新後的 `origin/main` 歷史，且該 SHA 的 **main push CI 最新一次執行**成功，六個必要 jobs 皆成功。PR 的測試結果不能替代合併提交的 CI；剛合併時先等 main CI 完成。無法取得 GitHub 結果就停止，不提供 skip-CI 開關。公開 repo 可匿名查詢；遇到 API 限額可提供只有讀取 Actions 權限的 `GH_TOKEN`，不得寫入命令列或文件。
- `--live-compose` 必須指向目前正式服務實際使用的 Compose 檔案；`--env-file` 明確指定其環境檔（可為 `.env` 或 `.env.prod`），權限須為 0600。腳本不猜檔名，不 `source` 檔案，也不帶入 shell 裡的 Compose 變數覆寫。
- 乾淨 Git 建置不包含被忽略的本機 `appsettings.json`。腳本會檢查目標的 `Jwt:Issuer`、`Audience`、`ExpireMinutes` 與至少 32-byte 的簽章 key；缺少時在建置／停機前停止。非密鑰設定也必須明確提供，不能只設定 JWT_KEY。
- 可用 `--compose-override /absolute/runtime.override.json` 補主機特有設定；檔案權限須為 0600，只允許修改 api／mathematics 的 environment。變更 image、build、掛載、命令、資料服務等欄位會被拒絕。相同 override 同時用於辨識目前設定與產生目標設定，之後會凍結到私有 release 設定中。
- 這是既有 `equitylens-prod` 的更新工具，不是首次安裝工具。既有 api、mathematics、postgres、redis 容器必須存在並運作。API／Mathematics 的實際環境若與 Compose、映像預設不一致，停止而不猜測設定。
- 部署帳號需能管理 Docker。Docker 權限等同很高的主機權限；這支腳本不提供權限隔離。不得直接接到公開 PR 的 self-hosted runner。
- 新舊版本必須相容於既有 agent runs、Redis 訊息及資料。腳本檢查 migration 歷史，無法證明語意相容；大幅 workflow／queue 格式變更先另做升級及排空計畫。

### 補齊必要執行設定

例如以私有 `runtime.override.json` 提供非密鑰 JWT 值（issuer／audience 應沿用既有正式值，避免令既有 token 失效；以下是專案目前常用值）：

```json
{"services":{"api":{"environment":{
  "Jwt__Issuer":"EquityLens.Api",
  "Jwt__Audience":"EquityLens.Web",
  "Jwt__ExpireMinutes":"120"
}}}}
```

JWT_KEY 仍由明確指定的環境檔提供。**不要把含密鑰的 appsettings.json 複製進 Git 建置來源。** 若首次採用 override，先核對目前容器／應用設定，讓執行設定與宣告一致；腳本不會默默接受額外 shell 覆寫。後續所有預覽／部署命令都加相同 `--compose-override`；rollback 不需要另帶它，會使用已保存設定。

## 預覽與部署

先取得欲部署的 main 提交完整 SHA。以下路徑與 SHA 都是需要替換的範例：

```bash
bash scripts/deploy/deploy.sh \
  --sha <40字元提交SHA> \
  --live-compose /absolute/production/docker-compose.prod.yml \
  --env-file /absolute/production/.env.prod
```

**未加 `--execute` 只印計畫**，不讀正式設定、不連線 GitHub／Docker，也不做環境預檢；不是部署可行的認證。確認目標與路徑後，加上 `--execute` 才真正執行。

```bash
bash scripts/deploy/deploy.sh \
  --sha <40字元提交SHA> \
  --live-compose /absolute/production/docker-compose.prod.yml \
  --env-file /absolute/production/.env.prod \
  --execute
```

可指定 `--health-url https://你的API/healthz`，預設 `https://equitylens.duckdns.org/healthz`；等待容器健康預設 180 秒，可用 `--timeout` 調整。初次使用建議在終端保持連線並觀察執行紀錄。

實際順序：

1. 取得部署鎖、驗證本機 Docker context、固定容器與 Git origin，fetch main，驗證指定 SHA 與 CI。
2. 將指定提交匯出到私有 release 目錄；讀取提交中 EF migration ID，與正式 `__EFMigrationsHistory` **完整集合比較**。缺少 migration 或 DB 比目標多 migration 均停止；不執行 migration 或 Down。
3. 解析目前及目標 Compose；拒絕資料服務、網路、volume、掛載或其他基礎設施差異。本工具只重建 api／mathematics。
4. 保存實際舊映像 ID、保留映像標籤及已解析設定。含 `$` 的環境值會正確處理 Compose escaping，避免保存／載入時變動密碼。建置新映像，標籤唯一並記錄 commit revision；此時舊服務仍運作。
5. 再確認 migration 與舊容器未變，執行 `pg_dump -Fc`。備份必須非空，並經 `pg_restore --file=/dev/null` 完整解碼；保存 SHA-256。任何失敗均不停止舊服務。
6. 停止 api／mathematics（給 60 秒結束），再用已建置映像 `up --no-deps --no-build --pull never` 啟動。**會有短暫停機**；不重啟資料服務、不執行 `down`、不移除 volume。
7. 驗證實際映像 ID、API 容器 healthy、Postgres `SELECT 1`、Redis PING；在 Mathematics 容器中驗證自身 Redis 連線及合成資料的真實計算引擎，另外直接檢查 API 容器 `/healthz` 的 JSON，接受 Caddy 公開 HTTPS `/healthz` 的純文字 `ok`，並確認公開 `/api/portfolios` 未帶憑證回 401。記錄成功或失敗。

資料庫 dump 是線上、一致性快照；不是停止所有寫入後的備份，也不是實際還原演練。只備份 PostgreSQL，不宣稱涵蓋 Garage 檔案、Redis 狀態或整台主機。部署程序外的人工操作不受此鎖約束，執行期間勿同時部署或套 migration。

## 私有紀錄與失敗

預設目錄：`~/.local/state/equitylens/deployments/<時間>-<隨機ID>/`。可用 `--state-dir` 指定，但必須位於 repo 外、由執行者擁有、權限 0700；所有操作應共用同一目錄以取得同一把鎖。

- `manifest.json`：目標 SHA／CI 連結、狀態、前後映像 ID、migration 清單、備份雜湊。
- `rollback.compose.json`、`target.compose.json`：固定映像及解析後設定，**含正式敏感值**。
- `database.dump`、`database.sha256`：私有資料庫備份。
- `commands.log`：指令及錯誤細節，可能含敏感設定；不提交、不上傳公開 CI artifact。
- `source/`：指定提交的建置來源；成功後仍保留供稽核。腳本不自動清理舊映像／備份，另訂保留及清理政策。

失敗回非零，保留紀錄。容器替換前失敗，舊服務持續運作；替換中／後失敗，可能已停機或處於部分更新狀態。腳本印出回復命令，但**不自動回復映像或資料庫**。先讀私有日誌、確認 run／queue 相容性，再決定回復。

## 回復上一組映像

回復的是指定 release 保存的「部署前」映像與設定，不是任意 Git SHA。舊映像必須仍存在，DB migration 清單必須與當時相同；不重新建置、不套用 migration、不還原 dump。

```bash
# 預覽
bash scripts/deploy/deploy.sh --rollback <release目錄名稱>
# 確認資料與未完成工作仍相容後執行
bash scripts/deploy/deploy.sh --rollback <release目錄名稱> --execute
```

曾使用非預設 `--state-dir` 或 `--health-url` 時，回復也要指定相同值。回復前另備份目前資料庫，停止兩個應用容器、載入保存的舊映像與設定，再跑相同 smoke。回復動作也有獨立紀錄；不提供自動 redo。

DynamicDag 已移除，歷史部署紀錄中的 `RESEARCH_ENGINE=DynamicDag` 不適用於目前版本。不要用它替代映像回復。

## 測試範圍

```bash
bash -n scripts/deploy/deploy.sh
python3 -m unittest discover -s scripts/deploy -v
```

測試涵蓋計畫模式零副作用、精確 CI／最新結果／必要 jobs、migration 差異、基礎設施變更、環境覆寫、私有設定、備份先於停機、各階段失敗、健康失敗不自動回復及回復時 schema 阻擋。Compose escaping 用真實 CLI 離線解析，其餘部署外部操作使用 fake，不接正式主機或資料庫。測試已加入既有 CI，不新增部署 workflow。

Smoke 的邊界：引擎測試使用合成資料，不寫正式隊列、不呼叫 AI。它不驗證成功登入後的授權查詢、真實 retrieval、Python consumer 實際消費訊息或 agent 回答品質；`/healthz` 本身只代表 API 能回應。真實 DB／Redis／worker 串接由 PR 的隔離整合測試補充，發布後仍需依變更做使用者流程驗收。

已完成實際隔離 Docker daemon 的部署、備份還原、映像回復與登入後投組讀取驗證；詳見[演練紀錄](deployment-rehearsal-2026-11-03.md)。沒有使用此腳本部署正式服務。

參考：[Docker Compose production](https://docs.docker.com/compose/how-tos/production/)、[Compose config](https://docs.docker.com/reference/cli/docker/compose/config/)、[GitHub workflow runs API](https://docs.github.com/en/rest/actions/workflow-runs)。

初版驗證（2026-11-03）：部署防護 18/18、Bash 語法通過；原有完整回歸後端 931/931（0 skipped）、eval harness 83、前端 Vitest 135，前後端 build 與 EF pending-model 通過。日誌 `/tmp/manual-deploy-r27-*.log`；遠端結果以 PR checks 為準。

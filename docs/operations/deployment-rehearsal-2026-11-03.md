# 手動部署腳本隔離演練（2026-11-03）

結論：實際建置、備份、替換 API／Mathematics、健康驗證、映像回復及獨立資料庫還原皆通過。演練找到兩項初版缺口並修正；沒有部署正式服務或合併 main。

機器可讀結果：[deployment-rehearsal-2026-11-03.json](deployment-rehearsal-2026-11-03.json)。本報告不包含憑證、token、環境快照或 dump。

## 隔離方式與資料

- 使用獨立 `docker:29-dind` daemon（image digest `sha256:9e79b879b177fbd72c34376a3dd3d91747da5db49e1f560f76aea59561471977`），限制 10 GiB／6 CPU。沒有掛載主機 Docker socket，daemon ID 與正式主機不同；內部可以沿用 `equitylens-prod-*` 名稱而不碰到正式容器。
- 只掛載演練私有目錄，使用獨立 Docker config/context、內部 networks／volumes。外部入口僅綁主機 `127.0.0.1` 隨機埠號；Caddy 使用 localhost 私有 CA，curl 驗證 CA，沒有 `-k` 或關閉 TLS 驗證。
- Docker-in-Docker 使用 privileged container；它是資源／daemon 的隔離演練，不是 VM 級安全邊界，不能拿來執行不可信 PR。
- 全新 ParadeDB 套用 39 項 migrations，透過 HTTPS 建立／登入一個測試帳號及一個投組；另有兩筆純合成標記資料。未複製正式財報、行情、投組或資料庫。未執行 AI 呼叫。
- 部署目標為已合併且 main CI 成功的 `cdd54b129d594aa34af3a07bce3a980a27cc094a`；腳本實際執行 fetch、Git ancestry 與 GitHub CI 驗證，沒有 mock 或跳過。來源 CI：[main push run](https://github.com/zhao199712/EquityLens/actions/runs/41583876729)。
- 基準與目標的應用程式行為相同；新映像加入目標 revision 標記，映像 ID 確實不同。這是部署／回復機制驗證，不宣稱驗證任意兩個業務版本或歷史 workflow 的相容性。

## 實際結果

| 驗證 | 結果 |
|---|---|
| 乾淨來源建置 | 從指定 main SHA 的 git archive 建置，未帶本機 appsettings.json |
| 完整部署 | 新 API／Mathematics 映像已取代基準映像，manifest 為 succeeded |
| HTTP | API 容器健康、Caddy HTTPS health 正常、無憑證投組 API 回 401；有效 JWT 可讀原測試投組 |
| 基礎依賴／數學 | PostgreSQL SELECT 1、Redis PING、Python 容器內自身 Redis 連線與合成風險計算通過 |
| 備份 | pg_dump custom archive 完整解碼，SHA-256 與 manifest 一致 |
| 實際還原 | 將 dump 還原至獨立 `drill_restore` 空白資料庫；帳號／投組／migration 數為 1／1／39，與來源一致 |
| 映像回復 | API／Mathematics 回復到精確的基準 image ID；有效 JWT 仍可讀原投組 |
| 新資料保留 | 回復映像後，同時保留部署前標記及部署後新增標記；沒有還原正在使用的資料庫 |
| 資料服務保持 | PostgreSQL、Redis、Garage、Caddy 的容器 ID 在部署／回復期間不變 |
| Migration 不一致 | 暫加演練 migration ID 後部署回非零，API／Mathematics 容器 ID 不變；測試後刪除該合成 ID |
| 健康檢查失敗 | 指定不存在的 HTTPS health path，curl 22，manifest 為 failed；沒有自動回復。使用保存設定手動回復成功 |
| 正式服務保持 | 六個正式容器的 ID、image ID、StartedAt、RestartCount 與演練前完全一致 |

正常部署 release：`20261103T060632Z-4de6163b`。其回復完成後，還執行了 health failure／manual rollback，最後再次確認帳號、投組及兩筆標記都存在。

## 找到並修正的缺口

### 1. Caddy health 與 API health 格式不同

正式 Caddy 會自行回應 `/healthz` 純文字 `ok`；初版腳本把它當 API JSON，會在正常服務上誤判失敗。先以新測試重現 `JSONDecodeError`，再修正為：

- 直接從 API 容器驗證 JSON `status=ok`。
- 接受公開代理的純文字 `ok`（也支援直接 API JSON）。
- 額外確認公開 `/api/portfolios` 未帶憑證回 401，避免只驗證到代理本身。

### 2. 乾淨 Git 建置缺少 JWT 非密鑰設定

本機忽略的 appsettings.json 不在 git archive 內。基準新建置的 `/healthz` 正常，但登入時 `Jwt:ExpireMinutes` 缺失，導致 500；issuer／audience 也沒有完整設定。

修正不是把本機 appsettings 打進映像：新增部署前 JWT 設定檢查，以及僅可補 api／mathematics environment 的私有 `--compose-override`。缺少 issuer／audience／有效時間／足夠長的 key 就在停機前停止。演練以明確的 issuer、audience、120 分鐘設定完成登入驗證；image／build／資料服務覆寫會被拒絕。

準備階段也實際遇到主機 shell 覆寫測試環境值；原有 live-environment 比對正確阻擋部署。重新以清理後的環境啟動測試容器才繼續，沒有放寬比對。

### 3. 還原資料庫需使用空白模板

ParadeDB 預設 template1 已包含 `paradedb` schema；直接 createdb 後還原完整 dump 會遇到 schema 已存在。獨立還原演練改用 `createdb -T template0`，再 `pg_restore --exit-on-error`，完整成功。這是演練程序修正；部署腳本依然不自動還原資料庫。

## 驗證、清理與邊界

- 防護測試增加到 21/21，包含 Caddy health、JWT 設定缺漏與 override 權限範圍。
- 完整回歸：後端 931/931、0 skipped；eval harness 83；前端 Vitest 135；前後端 build、EF pending-model 通過。日誌 `/tmp/deploy-rehearsal-r27-*.log`。
- 演練 daemon 及其匿名資料卷已移除。私有備份、原始日誌與設定保留於主機 `~/.local/state/equitylens-deploy-drill-5qkusu29/`，不提交 Git；設定和日誌可能包含敏感值，不能公開上傳。
- 本次沒有測試新 schema 升級、AI 品質、付費 retrieval、真實行情或正式容量；沒有對正式主機執行部署腳本。
- 登入與投組讀取是本次隔離演練的額外驗收，不是每次部署自動建立帳號或修改持倉。自動 smoke 仍維持無憑證／不寫應用資料；完整 DB → Redis → Python worker 消費鏈由既有 PR integration suite 驗證。
- 下一步可 review／合併 PR #7，再另行核准正式部署；不需要先安裝 runner。

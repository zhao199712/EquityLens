# GitHub 手動部署與專用 runner

部署入口是 `.github/workflows/deploy.yml`，只接受 main 的 `workflow_dispatch`。預設 `verify` 僅驗證；`deploy` 才執行已驗證的部署腳本。安裝 runner 本身不重新部署應用、不套 migration。

## GitHub 管理者先設定

1. [production Environment](https://github.com/zhao199712/EquityLens/settings/environments)：建立 `production`，Required reviewers 加入 `zhao199712`。單人操作時不要勾選 Prevent self-review，否則自己發起的部署無法自己核准。
2. Deployment branches and tags 選 Selected branches and tags，只加 **Branch: main**，不要加入 tag 或萬用字元。若有管理者跳過規則選項，停用跳過。
3. [Actions 設定](https://github.com/zhao199712/EquityLens/settings/actions)：外部協作者的 fork PR workflow 一律要求核准。既有 CI 保持 GitHub-hosted `ubuntu-latest`。
4. [新增 runner](https://github.com/zhao199712/EquityLens/settings/actions/runners/new?arch=x64&os=linux)：選 Linux x64，只複製 `./config.sh ... --token` 後的短效 registration token。不要建立或分享長效 PAT，也不要將 token 貼進聊天或 Git。

GitHub-hosted `protection` job 會查驗 required reviewer、唯一 main branch policy 與指定提交的 main CI，缺少或無法讀取就停止。之後 `deploy` job 才等 Environment 核准並派到正式 runner。安裝後仍須實際跑一次 verify 確認 GitHub Token 的 Environment 讀取權限；本機測試不能替代這一步。

## 本機安裝（本專案正式 Fedora Linux x64 主機）

版本 `2.338.2`，官方 SHA-256：

```text
baebf204307bac6193480852732f0aba2ae97b6c5597c244f4d5558390147c40
```

本次已下載、驗證並解壓到主機的 `~/.local/share/equitylens-runner-setup/`，可直接執行：

```bash
cd /home/ymsh20220/EquityLens
sudo bash scripts/deploy/runner/install-host.sh
```

在自己的終端輸入 sudo 密碼，再於隱藏輸入提示貼上短效註冊 token。腳本不將 token 放入命令參數或 shell history。若下載檔不在，先從[官方版本頁](https://github.com/actions/runner/releases/tag/v2.338.2)下載 `actions-runner-linux-x64-2.338.2.tar.gz` 到上述 setup 目錄；安裝腳本會再次驗證雜湊。

腳本建立專用 `equitylens-runner` 系統帳號、`/opt/equitylens-actions-runner` 和 systemd 服務。只註冊 `equitylens-prod` 自訂標籤，不加預設 labels。主機的 root-owned pre-job hook 拒絕其他 repo、其他 workflow、非 main、非手動事件。這是額外防呆，不是隔離沙箱：此帳號的 Docker 權限仍能控制主機。不得將一般 PR 或不信任工作派給它；GitHub 也不建議公開 repo 使用持久 self-hosted runner。

安裝會：

- 在使用者家目錄加入專用帳號的「穿越目錄」ACL，供讀取原有公開 Compose 路徑，不放寬私人檔案讀取權限。
- 將 `.env` 與本次已驗證的 `target.override.json` 複製至 `/var/lib/equitylens-deploy/runtime/`，權限 0600。後續設定更新以此為部署來源，勿只改舊 `.env` 後假定 runner 已同步。
- 複製既有 release／備份至 `/var/lib/equitylens-deploy/releases/`，保留回復資料；原本備份不刪除。
- 設定 `/etc/equitylens/runner-deploy.json`，只含路徑與健康 URL。

若因 token 過期而註冊失敗，不要重跑安裝覆蓋目錄；取得新 token 後執行：

```bash
sudo bash scripts/deploy/runner/register-start.sh
```

其餘安裝中斷請檢查已建立目錄／帳號後再處理，不要盲目刪除私人備份。

## 第一次驗證與日常部署

1. 將 workflow PR 合併 main，等該 merge commit 的全部六項 CI 成功。
2. Actions → **Deploy backend** → Run workflow → Branch `main`、mode `verify`。
3. 等待 `production` 的核准提示，由你核准。
4. verify 檢查 CI、migration、私有設定與現有容器一致、健康端點、資料服務及 Python 真實計算函式。不建置映像、不寫工作隊列、不備份或重啟容器、不呼叫 AI。
5. 真正要發布新版本時才選 `deploy`。同一時間只跑一筆，不能因新部署自動取消前一筆。腳本保存的 log／dump／設定永遠不發布為 GitHub artifact。

系統服務只是在開機後等待手動工作，不是定時部署。不要在日常工作目錄直接執行另一份部署；後續人工回復也用專用帳號與同一個 `/var/lib/equitylens-deploy/releases`，共用部署鎖。其他人工部署／migration 仍須協調，鎖無法阻擋任意 Docker 指令。

```bash
# 查看／停止服務（不停止 API 或 Mathematics）
sudo bash -c 'cd /opt/equitylens-actions-runner && ./svc.sh status'
sudo bash -c 'cd /opt/equitylens-actions-runner && ./svc.sh stop'
```

正式 deploy 失敗不自動回復。使用失敗紀錄印出的回復命令，指定同一 state-dir，以 `sudo -u equitylens-runner` 執行；資料庫不自動還原。核准及部署記錄留在 GitHub，敏感紀錄留在主機。

若失敗是部署腳本本身的問題，修正合併 main 並通過 CI 後，使用 **Run workflow** 建立新的執行。舊執行的 Re-run 仍使用原 SHA，不會取得新修正。2026-11-03 首次正式 runner 部署在建置前因 `Unexpected Git origin` 停止：actions/checkout 的 HTTPS remote 不帶 `.git`，檢查現已接受相同 repository 的有／無 `.git` 格式，verify 與 deploy 共用這項檢查；其他 repository、主機、credentials 或 query 仍拒絕。

## 已驗證與尚需驗證

- 本機：官方套件校驗與可執行版本、Bash 語法、27 個部署／runner 防護測試。
- GitHub：PR CI 驗證程式碼；不使用正式 runner 跑 PR 測試。
- 必須由管理者完成：sudo 安裝、GitHub registration／Environment 設定、真實 workflow verify。上述完成前不能宣稱 runner 已上線。

參考：[runner 系統服務](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/configure-the-application)、[pre-job hook](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/run-scripts)、[self-hosted 安全限制](https://docs.github.com/en/actions/reference/security/secure-use)、[Environment 保護](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments)。

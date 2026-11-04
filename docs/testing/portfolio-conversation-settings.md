# 投組對話與前端整合驗收

基準：main `3d917d6`（已合併 PR #19），分支 `codex/portfolio-conversation-settings`。

## 範圍與實作

- S1–S4 已整合；Settings 只保留語言與真正保存的風險偏好，保留 isSubmitEnter 輸入法防護。
- P5：0050／006208 不可用原因拆成不存在、重複、未啟用、交易所、幣別、資產類型、缺還原價天數；不修補證券資料。
- P1：使用者原文的完整名稱／ID 或程式列出的序號才可設定 session `activePortfolioId`；每輪重新核對擁有權。模型的 ID、改寫問題及 context patch 不可取代選擇。重置清除選擇。
- P2：沒有投組才提示建立；多投組時列選項。
- P3：nullable `app_user.risk_preference` migration；GET/PUT `/api/settings/risk-preference` 僅存取目前登入使用者。null 明示 Balanced／穩健預設，明確保存 Balanced 後 isDefault=false。get_risk_profile 使用同一份資料與聲明，不定義或捏造數值承受門檻。
- P4：重組投組 prompt，2976 → 1686 字元（含宣告／格式），先回答問題、按需求選分析，情境段只用於配置／優化；Quality Gate、引用及數字追溯規則未修改。原有 14 題分母及評分不變，新增題獨立檔案與分數。

## 離線驗證

- 後端 build 通過。
- 全部後端測試：1091 passed，0 failed，0 skipped；18 個名稱含 Postgres 的測試實際執行，使用已驗證 Compose project 的隔離 PostgreSQL。
- EF pending-model：沒有尚未建立 migration 的模型變更。
- 前端：158 tests passed；build 通過；production build + mocked API browser smoke 15 passed。
- eval harness：94 passed。
- 既有測試全部保留執行；僅 S4 明確要求刪除無使用者的 CapabilityRequestStatus 元件及專屬測試。

## 真實模型驗收

待執行：獨立 US$0.50 上限，隔離 PostgreSQL／Redis、合成行情及固定基準。原 14 題與新增題各自報告，不改舊分母。

## 發布注意事項

本次只交付分支，不部署。合併後須先備份、將新增 nullable 欄位 migration 套用正式庫，再部署 API，最後確認 Settings API 可用及前端刷新後設定保持。migration 沒有證券資料修補；0050 仍由管理介面修正。回復程式時保留 nullable 欄位即可，無需還原資料庫。

## 執行順序說明

S1–S4 是前一輪已完成且測試通過的提交，從最新 main 建立分支後整體納入，未為排序而拆散提交；P5／P1／P2 優先處理，Settings 再擴充 P3，P4 真實 eval 最後執行。

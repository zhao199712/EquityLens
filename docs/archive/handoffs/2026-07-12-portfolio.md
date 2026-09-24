# EquityLens Portfolio Handoff（2026-07-12）

## 目前狀態

Portfolio 已從單純持倉估值擴充為以「交易 + 現金流」重播的帳務模型。主要資料與服務皆在遠端專案：`/home/ymsh20220/EquityLens`。

目前測試組合：`61b19457-09eb-4be1-b2c1-82c1219c53f4`（TWD）。持有 2330、2454 各 10 股。

## 已完成功能

- 買賣交易為持倉唯一事實來源；持倉列可開啟該標的交易紀錄、買入／賣出表單、逐筆刪除與每頁 5 筆分頁。
- FIFO 成本配對：賣出交易回傳成交收入、FIFO 成本、手續費與已實現損益；刪除會重算，若造成後續超賣則拒絕。
- FinMind 現金股利事件與 Portfolio Cash Flow。
  - 已入帳股息納入可用現金與每日淨值。
  - 預計發放股息不計入現金。
  - 前端顯示股息紀錄與市場最新股息。
- 現金流帳可新增／修改／刪除入金、出金與費用；畫面簡化為日期與現金變動，股息不重複列在此區。
- 系統推導買入資金：現金不足以支應買入時建立 `Deposit`，避免既有交易歷史使現金變成負數。
- 每日淨值：持股市值 + 現金；回傳總資產、持股市值、現金、未實現／已實現損益、外部現金流與今日損益。
- XIRR：以外部入金／出金及期末總資產年化計算。
- 前端已加入 `TWR / XIRR` KPI。

## 金融口徑

- 第一版限定台股、TWD、現金股利。
- 已實現損益：FIFO。
- 買入現金影響：`數量 × 價格 + 手續費`。
- 賣出現金影響：`數量 × 價格 - 手續費`。
- 今日損益：`今日總資產 - 前一交易日總資產 - 今日淨入金／出金`。
- XIRR：入金為負現金流、出金與期末總資產為正現金流。
- TWR：以每日淨值排除外部入金／出金；股息屬組合內部報酬。

## 本次重要修正

### 非交易日造成的虛假報酬率

問題：1Y 畫面從 2025-07-12（週末）開始時，歷史估值只查詢 `from` 之後的價格。由於週末沒有價格，持股市值被算成 0，只剩 90 TWD 股息現金；下一交易日市價恢復後，前端以 90 當分母，顯示約 `+37,139%` 的錯誤區間報酬。

修正位置：

- `src/EquityLens.Api/Services/PortfolioValuations/PortfolioValuationService.cs`
  - 歷史估值價格查詢從最早交易日開始，以便在區間首日沿用最後有效收盤價。
  - 同日現金流先入帳、再重播買賣交易，避免系統推導入金與買入同日發生時造成零／負淨值。
- `src/EquityLens.Web/src/views/portfolios/PortfolioDetailView.vue`
  - 績效序列限定於使用者選擇的日期範圍。
  - 持倉存在但沒有任何有效價格的估值點不作為績效基準。

這兩個檔案在 2026-07-12 已重新 build；後端 Debug 程序必須在修改後重新啟動才會載入最新 DLL。

## 驗證方式

遠端先確認服務：

```bash
lsof -nP -iTCP:5034 -sTCP:LISTEN
lsof -nP -iTCP:5173 -sTCP:LISTEN
```

重新啟動 API 後，登入並開啟該 Portfolio：

1. 1Y 的價值走勢首點應為約 11K TWD 的持股淨值，而非 90 TWD。
2. 區間報酬不應出現數萬 %。
3. 目前總資產約 63.86K TWD，其中持股約 63.40K、現金約 460 TWD（依資料更新而變動）。
4. XIRR 約 +144% 是合理量級；TWR 會因資金投入時點不同於 XIRR，可能較高，需以修正後完整序列再核對。

可直接用 API 檢查歷史點（需先取得登入 access token）：

```bash
curl -sS -H "Authorization: Bearer $token" \
  'http://localhost:5034/api/portfolios/61b19457-09eb-4be1-b2c1-82c1219c53f4/valuation/history?from=2025-07-12&to=2026-07-12'
```

## 開發與執行注意事項

- 遠端 API 使用 5034、Vite 使用 5173。請勿同時以背景 `dotnet run` 與 VS Code F5 啟動 API，否則第二個程序會收到 `address already in use`。
- 遠端重啟後，VS Code Port Forwarding 可能失效；本機 `localhost:5173/5034` 若仍是舊畫面，請在 VS Code 執行 `Developer: Reload Window`，重新建立轉送後再強制重新整理瀏覽器。
- `dotnet build src/EquityLens.Api/EquityLens.Api.csproj --no-restore` 與 `cd src/EquityLens.Web && npm run build` 在本次修改後皆通過；目前只有既有 `NU1510` 警告。
- 工作樹已有多項未提交、非本次功能的變更；不要使用 `git reset --hard` 或覆蓋不相關檔案。

## 後續優先事項

1. 重新啟動 API 後實測 1Y、6M、3M、1M、YTD 的首點與區間報酬，補上每日淨值／TWR 單元測試。
2. 讓 TWR KPI 明確標示計算期間，並確認第二次投入資金日的切割口徑。
3. 實作台股加權含息總報酬基準比較與超額報酬。
4. 將系統推導的入金列為唯讀或在 UI 隱藏「修改／刪除」，避免使用者刪除後又被估值服務自動重建。
5. 完成現金流、FIFO、股息的整合測試；再評估 TWR/XIRR 與含息基準圖表。

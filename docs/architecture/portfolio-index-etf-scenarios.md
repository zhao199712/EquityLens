# 投組新增指數 ETF 假設情境

日期：2026-11-03。基準：main `f8f3ffa`。分支：`feat/portfolio-index-etf-scenarios`。

使用者授權把「僅調整既有持股或留現金」擴充為可比較尚未持有的指數 ETF。本功能只計算假設，不建立持倉或交易，仍使用原本的人工審批流程。這是後續授權的能力擴充，不回寫先前重構規格或歷史驗收結果。

## 行為與範圍

- 保留六個投組任務工具、三個工作流及十三種 node type。
- `get_portfolio_snapshot` 增加 `indexEtfCandidates`，每檔附 ticker、name、index、source、Ready／Unavailable 與原因。候選不是目前持倉；只有資料完整者以零數量放入內部價格快照。
- 第一版核准 0050、006208，同為 TWD／TWSE／ETF；DB metadata 必須啟用、唯一且符合此身份。不是任意新股票／槓桿／反向 ETF 搜尋器。
- `simulate_weight_scenario` 新增 `remainderPolicy: "index_etf"`，`transferToTicker` 指向核准且 Ready 的候選。`targetWeights` 仍只能指定既有持股的減碼後權重，不能讓模型提供價格／報酬／計算結果或把零持倉候選當減碼來源。
- 原有 `cash`／`holding` 請求保持有效；holding 仍要求實際持有。模型不得將使用者指定的 ETF 換成現金或另一檔標的。
- 一次最多三個情境。未限制方案的一般優化問題，prompt 17 及 portfolio method v2 指示比較相同減碼比例的現金與一檔可用 ETF。明確指定情境時忠實執行；無候選資料時揭露限制，不能編造 ETF 改善幅度。Router v6 允許指定新 ETF 的情境，其他新標的仍需澄清。

例：同一組減碼來源，同一次呼叫比較兩個假設（比例是呼叫範例，不是實際投資建議）：

```json
{"scenarios":[
  {"name":"cash","targetWeights":{"2327":0.3},"remainderPolicy":"cash","preserveOtherHoldings":true},
  {"name":"etf","targetWeights":{"2327":0.3},"remainderPolicy":"index_etf","transferToTicker":"0050","preserveOtherHoldings":true}
]}
```

## 行情與比較口徑

- 行情只讀 DB `MarketPrices`；本工具不自動下載行情，也不寫入證券或持倉。資料缺失須由既有行情匯入流程補足後建立新 run，不能讓模型自行填價格。
- ETF 必須覆蓋已凍結持股快照的每一交易日、至少 120 筆共同日報酬。所有原持股及 ETF 都必須使用還原收盤價；缺價、重複日、未還原價或不同幣別均回 Unavailable。不得縮短比較期間或刪除缺價持股以湊結果。
- 新 ETF 的估值價格採同一快照末日原始收盤價，歷史風險採還原收盤價報酬。估值日、日期序列與原組合相同；已保存的資料在後續工具呼叫及恢復時重用，DB 後續行情更動不替換它。
- 新 ETF 原始數量／權重為零，不影響 baseline；轉入金額恰等於釋出的減碼金額。其他持股股數及原有現金不變，沒有借款或放空。
- 波動、VaR、ES、最大回撤及差異沿用同一 calculator、同一 baseline。ETF 可能增加風險；程式不預設改善。
- HHI 為證券部位層次，沒有穿透 ETF 成分股。兩檔候選追蹤相同指數，可能與既有持股重疊，不能宣稱持有兩檔就是兩種獨立分散配置。UI 改用「證券權重／證券 HHI」，結果警語揭露此限制。
- 舊快照缺新欄位時維持原本現金／既有持股計算，不能憑空視為 ETF 已可用。沒有資料庫 migration。

核對來源（2026-11-03）：[元大 0050](https://www.yuantaetfs.com/product/detail/0050/Basic_information)、[富邦 006208](https://websys.fsit.com.tw/FubonETF/Fund/IndexIntro.aspx?stkId=006208)。擴充清單時必須再核對標的、指數、交易所、幣別及槓桿／反向性質。

## 驗證與限制

本機最終驗證：後端 build 與 **1,046 測試通過**（含 18 真實 PostgreSQL、0 skipped）；新增 23 個案例。前端 build、138 Vitest、10 固定 API 瀏覽器 smoke 通過；eval harness 92、Mathematics 36 通過，EF 無 pending-model changes。完整風險跨語言與服務鏈另由 PR CI 執行。

- 新增測試涵蓋：非持有 ETF 轉入、權重與 NAV 守恆、其他持股不變、常數 ETF 與現金的獨立解析對照、ETF 反而增加波動、資料不足／缺價／重複／未還原／錯幣別／錯交易所／非 ETF／停用、保存快照後行情變動仍重用、所有權、禁止模型輸入數值陣列、明確目的地不能被換掉、代號與名稱邊界、名稱數字不誤當財務主張。
- 原 14 題 eval 題庫及 scorer 不變；未把新增能力的測試混入原分母。原先 13/14、14/14 是 prompt 16 的歷史成績，不能當作 prompt 17 或 ETF 情境的真實模型驗收。
- 本次沒有付費 AI 呼叫、正式站瀏覽器驗證、合併 main 或部署。離線測試不證明真實模型每次都會提出適合的配置。
- 發布前須確認正式行情完整度，再用獨立 ETF 合成案例及原 14 題做真實模型回歸；可比地驗證 ETF／現金、資料不足、明確目的地與風險惡化情境。新的付費試跑需符合使用者授權預算。

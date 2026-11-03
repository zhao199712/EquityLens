# 投組新增指數 ETF 假設情境

日期：2026-11-03。基準：main `f8f3ffa`。分支：`feat/portfolio-index-etf-scenarios`。

使用者授權把「僅調整既有持股或留現金」擴充為可比較尚未持有的指數 ETF。本功能只計算假設，不建立持倉或交易，仍使用原本的人工審批流程。這是後續授權的能力擴充，不回寫先前重構規格或歷史驗收結果。

## 行為與範圍

- 保留六個投組任務工具、三個工作流及十三種 node type。
- `get_portfolio_snapshot` 增加 `indexEtfCandidates`，每檔附 ticker、name、index、source、Ready／Unavailable 與原因。候選不是目前持倉；只有資料完整者以零數量放入內部價格快照。
- 第一版核准 0050、006208，同為 TWD／TWSE／ETF；DB metadata 必須啟用、唯一且符合此身份。不是任意新股票／槓桿／反向 ETF 搜尋器。
- `simulate_weight_scenario` 新增 `remainderPolicy: "index_etf"`，`transferToTicker` 指向核准且 Ready 的候選。`targetWeights` 仍只能指定既有持股的減碼後權重，不能讓模型提供價格／報酬／計算結果或把零持倉候選當減碼來源。
- 原有 `cash`／`holding` 請求保持有效；holding 仍要求實際持有。模型不得將使用者指定的 ETF 換成現金或另一檔標的。
- 一次最多三個情境。未限制方案的一般優化問題，prompt 18 及 portfolio method v3 對一般優化／分散／降低波動問題優先評估 ETF，與相同減碼比例的現金方案比較；只留現金、不買 ETF 或明確指定其他方案時尊重要求。優先評估不等於必須推薦，ETF 未改善或代價過大時可建議不調整。明確指定情境時忠實執行；無候選資料時揭露限制，不能編造 ETF 改善幅度。Router v6 允許指定新 ETF 的情境，其他新標的仍需澄清。

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
- 原 14 題 eval 題庫及 scorer 不變；未把新增能力的測試混入原分母。原先 13/14、14/14 是 prompt 16 的歷史成績，不能當作 prompt 18 或 ETF 情境的真實模型驗收。
- 初版實作只做離線測試，未部署；後續單題付費驗證結果見下方。離線測試不證明真實模型每次都會提出適合的配置。
- 發布前須確認正式行情完整度，再用獨立 ETF 合成案例及原 14 題做真實模型回歸；可比地驗證 ETF／現金、資料不足、明確目的地與風險惡化情境。新的付費試跑需符合使用者授權預算。


### 後續單題結果

`c22268e` 在獨立合成行情，只問「可以給我優化建議嗎？」一次。Agent 自行比較同減碼比例的 0050 與現金，工具數值、守恆、持倉不變通過，自動 scorer **1/1**；但人工發現 VaR 比較方向的文字說反，因此答案品質尚未完全通過。這是新增補充案例，不改原 14 題分數，也不能證明穩定性。費用上界 US$0.0097443，未部署。詳見 [原始回答與驗證報告](../eval-verification/2026-11-03-portfolio-index-etf/README.md)。


### 比較方向修正（prompt 19／method v4）

- 從該次原始回答抽出離線 fixture，修正前重現測試失敗：gate 回傳空問題清單，錯誤 VaR 比較被接受。
- `simulate_weight_scenario` 新增 `riskComparisonStatements`：由同一 baseline 與各情境已保存的 Metrics 確定性產生逐項關係。波動改善為 baseline − scenario；帶符號 VaR／ES／回撤改善為 scenario − baseline，負改善代表惡化。不對負報酬取絕對值，也不把四個指標合併成單一優劣。
- 品質檢查核對明示的中文大小比較及改善幅度（大於／小於／高於／低於／等於），限定同一行引用、同一工具比較。指標列表逐項檢查；錯誤為 `ScenarioComparisonDirectionMismatch`。指代、來源、同類多方案或否定形式無法確認時為 `UnverifiedScenarioComparison`，不自行猜測；數值目標門檻不當作兩情境比較。
- 比較問題接入既有一次修訂與 checkpoint；提供工具逐項依據，恢復後不重跑工具。最終仍有問題則省略未通過段落，保留其餘可驗證說明；工具摘要另提供確定性的比較，UI 顯示降級原因。
- 此規則有明確語法範圍，不是通用自然語言語意驗證器，不能保證攔截所有改寫／隱含投資建議。原始模型回答、歷史分數不修改；修正後真實 AI 尚未重跑，不能把離線修正當成新的模型分數。


### 升降文字與暫不調整理由（prompt 20／method v5）

- 對 295f77e 重跑發現的兩項文字問題，改為工具提供依據＋提示詞約束，未新增逐句 regex。`riskChanges` 逐情境提供四項指標的 signedChange、非負 magnitude、direction、assessment、description；差值與幅度為報酬小數，description 轉成百分點。範例為「年化波動率下降 5.76 個百分點（改善）」及「VaR上升 0.63 個百分點（改善）」。
- description 與最終工具摘要共用同一實作，由 baseline／scenario 已保存 Metrics 計算，不信任過時的 DifferenceFromBaseline。不足資料不生成描述；持平及低於顯示精度分別說明，不寫「下降 0.00」。正幅度也存入工具證據，維持數字追溯；錯誤的 99.99 仍被 gate 拒絕。
- 模型須沿用工具升降文字。維持原配置不會相對自身降低風險；若因偏好待確認、交易成本未納入等因素暫不行動，必須另外說明未量化／待確認理由，不能假定成本或偏好，也不能把暫不行動說成量化支持的降風險方案。ETF 優先評估不等於必須買入，不強迫交易。
- 先加入工具缺少方向描述的重現測試，修前 1 failed（riskChanges 為 null），修後通過；新增共 4 個回歸案例，覆蓋改善、惡化、持平、精度、缺資料、過時差值及數字追溯。這不是以假模型宣稱自然語言缺陷已穩定消失。
- 本機驗證：後端 build、1,071 測試（18 真實隔離 PostgreSQL、0 skipped）；前端 build、138 Vitest、10 固定 API 瀏覽器 smoke；eval harness 92；EF 無 pending-model changes。原題庫、scorer、歷史回答與分數不變。
- 本輪沒有付費 AI 呼叫，尚未取得 prompt 20 的真實模型結果；尤其「暫不調整理由」目前是提示約束，不是通用語意硬檢查。未合併 main、未部署、未修改正式資料庫。


### Prompt 20 單題真實 AI 重跑（c536c07）

- 同一題「可以給我優化建議嗎？」、同一合成行情、同一 scorer，只跑一次。自動 **1/1**；quality gate 通過，沒有降級。4 輪／9 次成功工具呼叫；沒有被拒呼叫，恢復能力未觀察。
- 人工確認兩項目標問題本次皆未再現：正確寫波動率「下降 5.76 個百分點」，帶符號風險方向正確；不再把不調整稱為量化降風險方案，而是說明偏好待確認、成本未納入。
- 主動比較 9001 80%→60%，20% 轉 0050 或現金；原／ETF／現金波動 24.33%／18.57%／18.27%。VaR 為 ETF 改善較大，其餘三項現金改善較大；數學、守恆、持倉不變通過。
- 本輪上界 US$0.0107568；ETF 三次合計 US$0.0306015，原 US$0.05 剩 US$0.0193985，pending 0。修正版 CI 41672832410 成功。未重跑原 14 題、未合併或部署；單次通過不能代表穩定性。原文與軌跡見 [重跑報告](../eval-verification/2026-11-03-portfolio-index-etf/retest-c536c07/README.md)。

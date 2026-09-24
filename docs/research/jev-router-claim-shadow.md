# Jev Router 與 Claim–Evidence 影子測試

這兩項試點共用 `TypeSafeDecisionClient` 與既有的 `TYPESAFE_API_KEY`。兩項功能預設關閉，只對個別測試帳號啟用。Jev 失敗、逾時或沒有金鑰時，正式 LLM Router、Evidence Assessor 與後續流程照常執行。Jev 的機率是比較訊號，不是已驗證的準確率。

## 測試環境設定

```text
TYPESAFE_API_KEY=<由祕密管理提供，不寫入 Git>
JevRouterShadow__Enabled=true
JevRouterShadow__AllowedUserIds__0=<測試帳號 UUID>
JevRouterShadow__Model=jev-1.13.0
JevRouterShadow__TimeoutSeconds=3
JevClaimEvidenceShadow__Enabled=true
JevClaimEvidenceShadow__AllowedUserIds__0=<測試帳號 UUID>
JevClaimEvidenceShadow__AllowedPublicHosts__0=www.sec.gov
JevClaimEvidenceShadow__AllowedPublicHosts__1=mops.twse.com.tw
JevClaimEvidenceShadow__Model=jev-1.13.0
JevClaimEvidenceShadow__TimeoutSeconds=3
```

Router 在原 LLM 路由完成後，詢問 Jev 六個有限選項：workflow、lead skill、市場、資產類別、深度和投資組合。送出的資料只有問題文字與該使用者已授權的投組 ID/名稱；不送持股、金額、幣別或其他投組欄位。影子結果保存在 routing context 的 `jevShadow`，含與正式 workflow/skill 是否相同、選項機率、模型、token 與時間。`portfolioId`、`securityQuery`、研究目標與澄清問題仍以現有路由和驗證流程為準。測試問題不應包含非公開資訊。

Claim–Evidence 預審只考慮 factual claim，最多十二組 claim×片段；有 LLM 引用索引時用引用片段，沒有時用前兩筆候選。送出前，本地片段必須能在資料庫核對為非使用者上傳且 `Document.SourceUrl` 屬 HTTPS 公開網域白名單；Web 片段也必須有白名單 HTTPS URL。來源無法核對、內容過長與超額片段直接跳過。每組使用 Choice 判斷「直接支持／反駁／不足／未知」及「實績／預測／未知」。`jevClaimEvidenceShadow` tool call 記錄結果和跳過數；原 LLM Assessor 的狀態不會被改寫。

## 人工評估與後續門檻

先用已公開的財報與公告，人工標註 Router 問題及 claim×evidence 配對。Router 標註至少包含 workflow、skill、是否明確指定投組；Claim 標註至少包含直接支持、反駁、證據不足、實績/預測，以及相反證據。另收集難例：同一公司不同季度、已完成與預計完成、百分比與百分點。人工標註應依原文判斷，不能複製既有 LLM 或 Jev 的輸出。

比較影子結果與人工標註時，分開報告 Router workflow/skill 混淆矩陣、錯誤選投組率，以及 Claim 的反駁漏報與不支持主張誤放行率；同時看完整研究的 token、費用、延遲和重試。公開來源被略過的比例要一併報告，避免只在容易判斷的片段上計算準確率。尚未達到人工標註與線上評估門檻前，不讓 Jev 接管正式路由或 Assessor。

確定性驗證另外對完整數值 token、單一明確期間、文件標題的明確股票代號、顯式單位及「從 A% 到 B%，增加 C 個百分點」算術做檢查。只有可明確判定的錯配才標記 unsupported；其他情況維持 Assessor 結果。

參考：[TypeSafe API schema](https://api.typesafe.ai/openapi.json)。

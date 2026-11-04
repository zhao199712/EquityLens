# P3：使用者風險偏好 v1

日期：2026-11-04。分支 `codex/portfolio-conversation-settings`。承接 S1–S4／P1–P5 分支；依使用者後續授權整合已驗證的 GPT 6 Luna 原生工具 provider；僅隔離 eval 使用 Luna，正式環境預設不變，不合併 main、不部署。

## 決策與邊界

使用者在本串確認保守 10%／−15%、穩健 18%／−25%、積極 30%／−40%，討論 S&P 500 後再次確認維持 v1。這是有版本的產品參考門檻，不是實證校準的投資適合度標準，也不是損失保證。

自訂六題問卷，非 Grable–Lytton 量表。能力與意願分開計分，最終取較低等級；C1 一年內或 C2 影響生活費強制能力保守。W3 明示沒有經歷時不計分，漏答不當作沒有經歷。手動選擇取代問卷並刪除回答；刪除紀錄回到明示預設穩健。舊手動欄位維持相容，沒有歷史日期時不編造日期。

模型只有唯讀 get_risk_profile，沒有寫入偏好工具。API 使用目前登入者，不接受其他 userId。問卷回答只存在本人紀錄；不回傳給工具、AI 或 eval。模型取得摘要、來源、子等級、日期、過時提醒與程式產生的比較。

profileFit 只比較同次已保存數學結果，附原 measurementBasis 與期間；沒有符合期間／口徑的數值就 Unavailable，不借用其他期間或情境。run_risk_analysis／get_risk_report 同時附上比較，解決先查偏好、後產生分析的時序。倍數由程式四捨五入至兩位；gate 額外拒絕沒有 profileFit 支持的倍數／參考門檻，即使其他欄位碰巧包含該數字也不通過。

## RP 驗收

| 項目 | 狀態 | 證據 |
|---|---|---|
| RP1 | 完成 | 972 組合法回答、3–9 分邊界、W3 不計分、兩條硬規則、較低等級測試 |
| RP2 | 完成 | 預設／Manual／Questionnaire，兩個子等級及來源保存測試 |
| RP3 | 完成 | 本人 API、其他帳號隔離、拒絕指定 userId、匿名拒絕、刪除後實體紀錄消失 |
| RP4 | 完成 | 同期間 basis、缺資料／跨期間 unavailable，0.5887/0.18=3.27 測試 |
| RP5 | 完成 | 預設固定聲明及拒絕當成已確認偏好；正常／降級輸出保留聲明 |
| RP6 | 完成 | 原數字引用 gate 不放寬；拒絕自行計算倍數與借用無關數值的門檻 |
| RP7 | 完成 | 12 個曆月邊界（含閏日），過期提醒 |
| RP8 | 完成 | DTO 安全 ToString、entity AnswersJson JsonIgnore、工具僅序列化摘要；搜尋未發現 request body logging／EnableSensitiveDataLogging；eval 題庫只用合成手動偏好，不寫入問卷答案 |
| RP9 | 完成 | 同一輪 Luna medium 原 14/14、偏好 2/2、波動率 1/1；全部持倉無副作用；[逐題回答與費用](../eval-verification/2026-11-04-risk-profile/README.md) |

## Migration 與回滾

新增 `20261104110529_AddUserRiskProfile`，一人一筆，user_id 主鍵及使用者 cascade FK。保留舊 RiskPreference 欄位，問卷提交／刪除會清空舊手動值。

回滾到前一 migration：先備份並確認需要保留的評估；停止使用新版 API，再執行 `dotnet ef database update 20261104080806_AddUserRiskPreference --project src/EquityLens.Api --startup-project src/EquityLens.Api`（實際前一版本以 migration 清單為準）。Down 會刪除整個 user_risk_profile 表及問卷回答；這是有資料損失的回滾，不自動執行。舊手動設定仍在 app_user；問卷設定回滾後變預設，不重建原回答。正式部署需另行授權，這次不操作正式 DB。

## 驗證

後端 build 成功；完整後端 1156 passed / 0 failed / 0 skipped，包含隔離 PostgreSQL。前端 160 passed；build 成功；瀏覽器 16 passed。eval pytest 97 passed。整合 Luna 與第二次有界修訂後已重跑後端全套；18 個 PostgreSQL 整合測試實際執行。

Settings 截圖（本機 mocked API 瀏覽器驗收）：[未評估](screenshots/risk-profile-v1/settings-unassessed.png)、[已評估](screenshots/risk-profile-v1/settings-assessed.png)。不等同正式站已部署。

修改範圍：Settings controller／API 型別、UserRiskProfile entity/config/DbContext/migration、問卷計分與門檻與證據服務、投組工具與 gate、重組既有提示、Settings Vue/service/locales、後端／前端／瀏覽器測試，以及獨立 RP eval 題與評分。appsettings.json、導覽列及其他使用者工作目錄未修改。

## 評估中修正與相容性

- profileFit 使用凍結行情的實際共同交易日期；同輪獨立分析的比較快取按 operation 合併，不以空 TailRisk 結果抹掉 Core 已完成比較。
- 模型當輪只顯示最完整的一份偏好比較，避免大型工具資料尾端被截斷；持久化證據保留原樣。
- 投組答案最多兩次修訂，仍受設定的總輪次／工具／token／費用／時間上限約束（Luna 驗證配置見下方）；研究修訂上限未改。新測試先重現第二次無法修訂，再驗證恢復與錯誤答案仍拒絕。
- Luna 使用 Responses API 原生工具 schema、durable call_id 及結果回傳；未採用 Luna 分支修改過的評分器，原 14 題仍用本分支原標準。
- 未將 GPT Pro credit 視為已核實的 API 抵扣；預算代理以供應商用量估算保守上界，不以餘額推測放行。

原生工具回覆現直接帶入本輪品質拒絕與可操作修正原因，恢復後仍保留；先以測試重現只有 Rejected 而缺原因，再修至通過。隔離輸出目錄測試曾因既有測試找不到 repo／fixture 失敗，補入指向原檔的唯讀連結後完整 1156 通過，沒有更改或略過測試。

比較分類修正：明確的產品參考上／下限，必須引用相同指標的已保存 profileFit 狀態；無引用、方向相反、上／下限用錯皆拒絕。不再將偏好參考比較誤分類成兩個情境的優劣比較。既有混口徑、錯數字與情境比較測試仍通過。偏好聲明亦可取自分析工具內的 riskProfile，工具路徑不同不影響預設／來源揭露。

新增 RP 題評分器由 2-rp2 升至 2-rp3：原版本漏認 run_risk_analysis 內嵌的相同風險偏好，導致正確保守答案誤判。原 14 題規則未改；新增題的修正前原分與離線重算分會分開記錄，不混成真實重跑。

## 最終驗證配置與交付限制

實際執行版本 `f1b14d9`，完整單輪 R11：原 14/14、RP 2/2、P4 1/1。未挑題拼分。原 14 題平均答案 589.9 字元，早期 P3 前 534.3；本次新增偏好證據，並不是同步模型 A/B，也不把字數增加當成品質提升。所有 P3 嘗試保守成本（含未結算預留）US$1.025391925，上限 US$50。GPT Pro credit 抵扣未核實。

Luna 的驗證設定如下；這些是隔離環境設定，並未改寫 appsettings.json 或正式環境。沿用此驗收結果需使用相同模型與限制，不能把它當成預設 DeepSeek 的通過分數：

```text
AI__ChatProvider=OpenAI
OpenAI__Model=gpt-6-luna
OpenAI__ReasoningEffort=medium
AgentRuntime__ResearchAgentLoop__MaxPromptTokens=100000
AgentRuntime__ResearchAgentLoop__MaxOutputTokens=5000
AgentRuntime__ResearchAgentLoop__MaxTurns=6
AgentRuntime__ResearchAgentLoop__MaxCostUsd=0.10
AgentRuntime__ResearchAgentLoop__InputUsdPerMillion=0.225
AgentRuntime__ResearchAgentLoop__OutputUsdPerMillion=0.5
```

金鑰只從私有環境提供；上述單價是本輪預算估算設定，不是帳單或 credit 抵扣證明。相較 low 基準，本輪輸入上限由 60000 調至 100000、輸出由 2500 調至 5000，保留 6 輪與 US$0.10 限制。投組 prompt 本體為 1543 字元，基準為 1545，未累加零散規則；原生協定以 function 呼叫取代 JSON 動作文字。

已用 P3 前 `d65533b` 評分器與本版交叉重算 112 份原 14 題保存回答，checks 與 passed 完全相同。新增 RP 題的 2-rp3 只修正同一風險偏好在分析工具內嵌時的辨識；R9 原分 1/2，離線重算 2/2，與 R11 真實重跑 2/2 分開記錄。

偏差／限制：問卷原始回答不進模型，因此 Agent 可說明能力、意願兩個子等級與取低原則，不會重述使用者的 C1／C2 原始回答。門檻為已確認的產品參考；這是自訂六題問卷，非經驗證的心理量表。本輪全綠是單輪合成資料驗證，不等於所有真實持倉或後續每次模型回答都會成功。

## 給 Reviewer 的摘要

RP1–RP9 已完成；程式測試與本輪 AI 驗收通過。S1–S4／P1–P5 的原實作保留，IME 防護、舊 run 唯讀、Admin 技術分頁及人工審批未改。主要相容性風險為新增資料表及切換 LLM 時的設定；舊手動偏好仍可讀，沒有歷史日期時不編造日期。

建議發布順序（本次未執行）：備份資料庫；核對兩個新增 migration，先新增 nullable 偏好欄位與 user_risk_profile 表；部署相同已驗證版本並在私有環境選用上述 Luna 配置；協調前端發布後，檢查未評估／問卷／手動／刪除及一筆唯讀投組診斷。程式回復時可先保留新增表；若確定回滾 schema，依上方 Down 說明執行，會刪除問卷資料，不自動還原資料庫。

## 本輪修改檔案（相對既有 PR head d65533b）

```text
docs/eval-verification/2026-11-04-risk-profile/README.md
docs/eval-verification/2026-11-04-risk-profile/attempts-and-cost.json
docs/eval-verification/2026-11-04-risk-profile/original-14.json
docs/eval-verification/2026-11-04-risk-profile/risk-profile.json
docs/testing/portfolio-conversation-settings.md
docs/testing/risk-profile-v1.md
docs/testing/screenshots/risk-profile-v1/settings-assessed.png
docs/testing/screenshots/risk-profile-v1/settings-unassessed.png
evals/cases/portfolio_risk_profile_v1.jsonl
evals/equitylens_eval/portfolio.py
evals/equitylens_eval/portfolio_seed.py
evals/tests/test_portfolio.py
src/EquityLens.Api/Bootstrap/ResearchAiRegistration.cs
src/EquityLens.Api/Controllers/SettingsController.cs
src/EquityLens.Api/Data/Configurations/UserRiskProfileConfiguration.cs
src/EquityLens.Api/Data/Entities/UserRiskProfile.cs
src/EquityLens.Api/Data/EquityLensDbContext.cs
src/EquityLens.Api/Migrations/20261104110529_AddUserRiskProfile.Designer.cs
src/EquityLens.Api/Migrations/20261104110529_AddUserRiskProfile.cs
src/EquityLens.Api/Migrations/EquityLensDbContextModelSnapshot.cs
src/EquityLens.Api/Services/Agents/Loop/AgentLoopEngine.cs
src/EquityLens.Api/Services/Agents/Loop/PortfolioAgentNodeHandlers.cs
src/EquityLens.Api/Services/Agents/Loop/PortfolioComparisonClaims.cs
src/EquityLens.Api/Services/Agents/Loop/PortfolioLoopTools.cs
src/EquityLens.Api/Services/Agents/Loop/ResearchLoopContracts.cs
src/EquityLens.Api/Services/Ai/IChatCompletionService.cs
src/EquityLens.Api/Services/Ai/OpenAiChatCompletionService.cs
src/EquityLens.Api/Services/Ai/OpenAiFunctionProtocol.cs
src/EquityLens.Api/Services/Ai/OpenAiOptions.cs
src/EquityLens.Api/Services/CurrentUser/RiskPreferenceProfile.cs
src/EquityLens.Api/Services/CurrentUser/RiskProfileEvidence.cs
src/EquityLens.Api/Services/CurrentUser/RiskProfileThresholds.cs
src/EquityLens.Api/Services/CurrentUser/RiskQuestionnaire.cs
src/EquityLens.Web/browser-smoke/frontend-honesty.spec.ts
src/EquityLens.Web/src/locales/en.ts
src/EquityLens.Web/src/locales/zh-TW.ts
src/EquityLens.Web/src/services/settings.ts
src/EquityLens.Web/src/views/settings/SettingsView.vue
src/EquityLens.Web/src/views/settings/__tests__/SettingsView.test.ts
tests/EquityLens.Api.Tests/Bootstrap/BootstrapTests.cs
tests/EquityLens.Api.Tests/Controllers/SettingsControllerTests.cs
tests/EquityLens.Api.Tests/Services/Agents/PortfolioLoopToolsTests.cs
tests/EquityLens.Api.Tests/Services/Agents/ResearchAgentLoopTests.cs
tests/EquityLens.Api.Tests/Services/Agents/RiskProfileTests.cs
tests/EquityLens.Api.Tests/Services/Agents/ToolApprovalTests.cs
tests/EquityLens.Api.Tests/Services/Ai/OpenAiChatCompletionServiceTests.cs
```

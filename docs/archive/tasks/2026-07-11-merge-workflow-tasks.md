# 合併 CriticReview + DraftRevision 為 Research Quality Review

日期：2026-07-11

## 目標

把目前兩條分開的 workflow（CriticReview 和 DraftRevision）合併成一條完整的 `Research Quality Review` workflow。

合併前：
- CriticReview: LoadResearchRun → BuildEvidencePacket → CheckEvidence → CritiqueAnswer → FinalizeCriticReport
- DraftRevision: LoadCriticReviewRun → DraftRevisedAnswer → FinalizeRevision

合併後：
- ResearchQualityReview: LoadResearchRun → BuildEvidencePacket → CheckEvidence → CritiqueAnswer → FinalizeCriticReport → DraftRevisedAnswer → FinalizeRevision

## 需要做的事

### 1. 新增常數定義
**檔案：** `src/EquityLens.Api/Services/Agents/AgentWorkflowConstants.cs`

- [ ] 新增 `AgentWorkflowTypes.ResearchQualityReview = "ResearchQualityReview"`
- [ ] 新增 `ResearchQualityReviewWorkflow` 版本常數（`Version = 1`）
- [ ] 新增 `ResearchQualityReviewNodeKeys`（合併兩組 node key，共 7 個）
- [ ] 新增 `ResearchQualityReviewNodeTypes`

### 2. 新增 WorkflowDefinitionProvider
**檔案：** `src/EquityLens.Api/Services/Agents/AgentWorkflowDefinitionProvider.cs`

- [ ] 新增 `ResearchQualityReviewWorkflowDefinitionProvider`
- [ ] 定義 7 個 node 的 DAG 圖（線性鏈：7 個 node 6 條 edge）
- [ ] `InputJson` = `{ researchRunId }`（與 CriticReview 相同）
- [ ] `AgentType` = `Critic`（或新建 `QualityReview`）
- [ ] 產生合併後的初始 blackboard JSON（呼叫 #3 的 factory method）

### 3. 新增 Blackboard Contract
**檔案：** `src/EquityLens.Api/Services/Agents/AgentBlackboardContracts.cs`

- [ ] 新增 `CreateInitialResearchQualityReviewBlackboard(researchRunId)`
- [ ] 初始化所有 blackboard key（合併 CriticReview + DraftRevision 的 key）
- [ ] 確保 `finalizeCriticReport` 產出的 `criticReview` / `criticFindings` 能被 `draftRevisedAnswer` 直接從 blackboard 讀取
- [ ] 確認 `revisedAnswer` / `revisionSummary` / `appliedRecommendation` 等 key 在初始化時包含

### 4. 確認 Node Contracts / NodeJson 適配
**檔案：** `src/EquityLens.Api/Services/Agents/AgentNodeContracts.cs` + `AgentNodeJson.cs`

- [ ] 確認 `DraftRevisedAnswerInput` 能從 blackboard 直接讀取（不依賴 `LoadCriticReviewRun` 的 DB output）
- [ ] 確認 `FinalizeRevisionInput` 能從 blackboard 直接讀取
- [ ] 如有必要，新增 helper 方法到 `AgentNodeJson.cs`

### 5. 修改 Node Handler
**檔案：** `src/EquityLens.Api/Services/Agents/DraftRevisionNodeHandlers.cs`

- [ ] `LoadCriticReviewRunNodeHandler` → 從 DI 註冊中移除（merged workflow 不用，但 handler 保留給舊 workflow）
- [ ] `DraftRevisedAnswerNodeHandler` → 改為從同一個 run 的 blackboard 讀取：
  - 從 blackboard 讀取 `criticReview`（JSON）取得 `requiresRevision`、`suggestedAnswerRevision`
  - 從 blackboard 讀取 `criticFindings`（JSON array）
  - 從 blackboard 讀取 `answer`（原始回答）
  - 移除原本從 `LoadCriticReviewRunNodeHandler` output 讀取的邏輯
  - 新增 blackboard key 缺失時的 error handling（丟出 `InvalidOperationException`）
- [ ] `FinalizeRevisionNodeHandler` → 確認不受影響（已從 blackboard 讀取）

### 6. Policy Evaluator 策略
**檔案：** `src/EquityLens.Api/Services/Agents/WorkflowPolicyEvaluator.cs`

- [ ] 決定策略：復用 `CriticReviewPolicyEvaluator` 還是新建 `ResearchQualityReviewPolicyEvaluator`
  - **選項 A**：新建 `ResearchQualityReviewPolicyEvaluator`，`WorkflowType` = `"ResearchQualityReview"`，邏輯與 `CriticReviewPolicyEvaluator` 相同但可獨立演進
  - **選項 B**：復用 `CriticReviewPolicyEvaluator`，但因 `WorkflowType` 不同無法直接匹配（DI 以 `WorkflowType` 為 key），需要修改 resolver 邏輯
  - **建議**：選項 A，新建一個 evaluactor，保持現有 resolver 模式不變
- [ ] 實作決定：批審後要不要修正（`AcceptAnswer` / `ReviseAnswer`）
- [ ] `RouteBackTo` 欄位在 merged workflow 中無意義（線性執行），可設為 `null` 或移除

### 7. 修改 Service 介面和實作
**檔案：** `src/EquityLens.Api/Services/Agents/IAgentRunService.cs` + `AgentRunService.cs`

- [ ] 新增 `CreateResearchQualityReviewAsync(userId, researchRunId)`
- [ ] 保留舊的 `CreateCriticReviewAsync` / `CreateDraftRevisionAsync`（向後兼容，已有 persisted runs）
- [ ] `RetryAsync` — 確認新 workflow 的 retry 邏輯正確（`CreateRun` + `CreateInitialBlackboardJson` 由新 provider 提供）

### 8. 修改 API Contract
**檔案：** `src/EquityLens.Api/Contracts/Agents/AgentRunContracts.cs`

- [ ] 新增 `CreateResearchQualityReviewRequest(Guid ResearchRunId)`

### 9. 修改 API Controller
**檔案：** `src/EquityLens.Api/Controllers/AgentRunsController.cs`

- [ ] 新增 `POST /api/agent-runs/research-quality-review` endpoint
- [ ] 保留舊的 `/critic-review` 和 `/draft-revision` endpoints（向後兼容）

### 10. 修改 DI 註冊
**檔案：** `src/EquityLens.Api/Program.cs`

- [ ] 註冊新的 `ResearchQualityReviewWorkflowDefinitionProvider` → `IAgentWorkflowDefinitionProvider`
- [ ] 註冊新的 `ResearchQualityReviewPolicyEvaluator` → `IWorkflowPolicyEvaluator`
- [ ] 從 DI 中移除 `LoadCriticReviewRunNodeHandler` 的 `IAgentNodeHandler` 註冊（handler class 保留不刪）
- [ ] 保留 `ICriticReviewAgent` / `IDraftRevisionAgent` 註冊
- [ ] 保留舊的 `CriticReviewWorkflowDefinitionProvider` / `DraftRevisionWorkflowDefinitionProvider` 註冊（向後兼容）

### 11. 新增 Node Metadata（可與上述步驟同步進行）
**新檔案：** `src/EquityLens.Api/Services/Agents/NodeMetadata.cs`

- [ ] 定義 `NodeMetadata` record（`RequiredBlackboardKeys`、`ProducedBlackboardKeys`、`Stage` 等）
- [ ] 為每個 node handler 加上 metadata 宣告
- [ ] 擴充 validator 檢查 blackboard key 依賴

### 12. 更新測試
**檔案：** `tests/EquityLens.Api.Tests/Services/Agents/`

- [ ] `AgentRunServiceTests.cs` — 大幅改寫：
  - 新增 ResearchQualityReview 7-node 流程的 create / execute / retry / cancel 測試
  - 新增 `LoadCriticReviewRun` node 不再被調用的驗證
  - 新增 blackboard 資料流測試（finalizeCriticReport → draftRevisedAnswer）
  - 保留舊 CriticReview / DraftRevision 的測試（向後兼容）
- [ ] `AgentRunsControllerTests.cs` — 測試新 endpoint（`/research-quality-review`）
- [ ] `AgentWorkflowPlannerTests.cs` — 加入新 7-node DAG 的 topological sort 測試
- [ ] `AgentRunGraphValidatorTests.cs` — 加入新 DAG 的 node matching 測試
- [ ] 新增 `ResearchQualityReviewWorkflowDefinitionProviderTests` — 驗證 DAG 結構、node 數量、edge 正確性
- [ ] 新增 `ResearchQualityReviewPolicyEvaluatorTests`（若新建 evaluactor）

## 建議實作順序

| 順序 | 事情 | 原因 |
|------|------|------|
| 1 | #1 常數定義 | 基礎，其他步驟會引用 |
| 2 | #2 WorkflowDefinitionProvider | 定義 workflow 結構 |
| 3 | #3 Blackboard Contract | 確保資料流正確 |
| 4 | #4 Node Contracts / NodeJson 適配 | 確認 handler 輸入格式正確 |
| 5 | #5 修改 Node Handler | 調整 handler 邏輯 |
| 6 | #6 Policy Evaluator | 加入決策點 |
| 7 | #8 API Contract | 定義 API 請求格式 |
| 8 | #7 Service | 實作業務邏輯 |
| 9 | #9 Controller | 暴露 API endpoint |
| 10 | #10 DI 註冊 | 串接所有服務 |
| 11 | #11 Node Metadata | 可同步進行 |
| 12 | #12 測試 | 驗證功能正確 |

## 注意事項

- **向後兼容**：舊的 CriticReview / DraftRevision workflow 保留，API endpoint 保留，DI 註冊保留，以支援已存在的 persisted runs
- **`LoadCriticReviewRunNodeHandler`**：從 DI 移除（merged workflow 不用），但 class 保留（舊 workflow 仍需要）
- **`DraftRevisedAnswerNodeHandler` 改動**：需移除對 `LoadCriticReviewRunNodeHandler` output 的依賴，改為直接從 blackboard 讀取 `criticReview` / `criticFindings` / `answer`
- **Policy Evaluator**：建議新建 `ResearchQualityReviewPolicyEvaluator`（而非復用 `CriticReviewPolicyEvaluator`），因 `WorkflowType` 不同且可獨立演進
- **`RouteBackTo` 欄位**：在 merged linear workflow 中無意義，可設為 `null`
- **Telemetry**：合併後的 7-node workflow 的 span / metric 需確認與現有 `EquityLensTelemetry` 相容，建議新增對應的 activity name
- **Node Metadata**：建議與合併工作同步進行，確保每個 node 有清楚的 input/output 規格

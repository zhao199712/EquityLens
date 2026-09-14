# 動態 Workflow、Node Pool 與分工計畫

日期：2026-07-08

## 目標

整理 EquityLens 未來如果要支援「根據使用者需求動態組成 workflow」時，node 應該如何設計，以及團隊可以如何分工。

核心方向：
- Node 要像 typed function，有明確 input、output、metadata 與執行規則。
- Workflow 的彈性來自 planner 如何挑選與組合 node，而不是 node 本身行為模糊。
- 第一版應該做受控 dynamic workflow，不要讓 LLM 任意創建 node。
- 先用固定 Node Pool / Node Catalog 建立可驗證、可測試、可觀測的 workflow 基礎。

## 核心概念

## Node

Node 是 workflow 中一個有業務意義的執行步驟。

好的 node 應該：
- 職責單一。
- 有明確 input。
- 有明確 output。
- 有明確 blackboard key 讀寫規則。
- 有清楚 retry、timeout、side effect、loop policy。
- 可以被 workflow planner 安全組合。

不建議把太細的技術細節拆成 node，例如：
- parse prompt。
- call LLM。
- parse JSON。
- validate JSON。
- save output。

這些通常應該是 handler 內部細節。

比較適合的 node 粒度：
- LoadResearchRun。
- BuildEvidencePacket。
- CheckEvidenceCoverage。
- CritiqueAnswer。
- EvaluateCriticPolicy。
- DraftRevisedAnswer。
- FinalizeAnswer。

## Node Pool / Node Catalog

Node Pool 可以理解成系統可用 node 的集合。

建議定義：

```text
Node Pool
= Node Catalog
+ Node Handler Registry
+ Node Contracts
+ Node Tests
```

Node Catalog 是所有可被 workflow planner 使用的 node 型別與規格清單。

它不是某一次 workflow，而是所有可重用的 node 能力庫。

例如：

```text
LoadResearchRun
BuildEvidencePacket
CheckEvidenceCoverage
CritiqueAnswer
EvaluateCriticPolicy
DraftRevisedAnswer
FinalizeAnswer
FollowUpSearch
HumanApproval
```

Planner 只能從 Node Pool 裡挑選 node 來組 workflow。這可以避免 planner 產生不存在、不能執行或不可追蹤的節點。

## Workflow Definition

Workflow definition 是某一次實際要執行的 graph。

Node Pool 是全部可用積木；workflow definition 是這次實際拼出來的流程。

例如 Node Pool 有：

```text
LoadResearchRun
BuildEvidencePacket
CheckEvidenceCoverage
CritiqueAnswer
EvaluateCriticPolicy
DraftRevisedAnswer
FinalizeAnswer
```

某一次 workflow definition 可能是：

```text
LoadResearchRun
-> BuildEvidencePacket
-> CheckEvidenceCoverage
-> CritiqueAnswer
-> EvaluateCriticPolicy
-> DraftRevisedAnswer
-> FinalizeAnswer
```

動態產生的 workflow definition 必須持久化，不能只存在記憶體。

原因：
- 失敗後才能重試與除錯。
- 前端才能查完整 graph。
- persisted events、nodes、toolCalls 才能對應到當時的 workflow。
- 後續 audit、observability、replay 才有基準。

## Blackboard Key

Blackboard 是整個 workflow 共用的執行上下文。

Blackboard key 是 node 之間交換資料時使用的命名欄位。

例如：

```json
{
  "researchRun": {},
  "evidencePacket": {},
  "criticReviewResult": {},
  "criticPolicyDecision": {},
  "draftRevisionResult": {},
  "finalAnswer": {}
}
```

其中：

```text
researchRun
evidencePacket
criticReviewResult
criticPolicyDecision
draftRevisionResult
finalAnswer
```

就是 blackboard keys。

每個 node 應該明確宣告：
- RequiredBlackboardKeys。
- OptionalBlackboardKeys。
- ProducedBlackboardKeys。

這樣 validator 可以在執行前檢查 workflow 是否合法。

例如 `DraftRevisedAnswer` 需要：

```text
originalQuestion
originalAnswer
evidencePacket
criticReviewResult
criticPolicyDecision
```

如果 planner 把 `DraftRevisedAnswer` 放在 workflow 第一個 node，validator 應該直接擋下來。

## Node Metadata

Node metadata 是 node 的規格書。

它不是 node 的業務結果，而是給 planner、validator、runner、UI 使用的描述資料。

Node handler 是實際做事的程式；node metadata 告訴系統這個 node 如何被安全使用。

第一版 metadata 建議至少包含：

```text
NodeType
Version
DisplayName
Description
RequiredBlackboardKeys
OptionalBlackboardKeys
ProducedBlackboardKeys
AllowedPreviousNodeTypes
AllowedNextNodeTypes
Retryable
MaxRetryCount
TimeoutSeconds
Idempotent
SideEffectLevel
SupportsLoop
MaxIterations
RequiresHumanInput
CanPauseWorkflow
ToolsUsed
EstimatedCostLevel
EstimatedLatencyMs
ProgressMessage
SuccessMessage
FailureMessage
RequiredPermissions
```

最小可行版本可以先做：

```text
NodeType
Description
RequiredBlackboardKeys
ProducedBlackboardKeys
AllowedNextNodeTypes
Retryable
TimeoutSeconds
SideEffectLevel
SupportsLoop
RequiresHumanInput
```

## Node Metadata 範例

以 `DraftRevisedAnswer` 為例：

```text
NodeType: DraftRevisedAnswer
Version: v1
DisplayName: 產生修正版答案
Description: 根據 critic review、policy decision 與 evidence 產生修正版研究答案。

RequiredBlackboardKeys:
- originalQuestion
- originalAnswer
- evidencePacket
- criticReviewResult
- criticPolicyDecision

OptionalBlackboardKeys:
- userFeedback

ProducedBlackboardKeys:
- draftRevisionResult

AllowedPreviousNodeTypes:
- EvaluateCriticPolicy
- HumanApproval
- FollowUpSearch

AllowedNextNodeTypes:
- FinalizeAnswer
- FinalCriticCheck

TimeoutSeconds: 120
Retryable: true
MaxRetryCount: 2
Idempotent: true

SideEffectLevel: ExternalLlmRead

SupportsLoop: false
MaxIterations: 0

RequiresHumanInput: false
CanPauseWorkflow: false

ToolsUsed:
- LLM

EstimatedCostLevel: Medium
EstimatedLatencyMs: 45000

ProgressMessage: 正在根據評論結果修正答案
SuccessMessage: 修正版答案已產生
FailureMessage: 產生修正版答案失敗

RequiredPermissions:
- RunAgentWorkflow
```

## 範例 Workflow：ResearchRun -> CriticReview -> DraftRevision -> FinalAnswer

這條產品流程不建議只拆成 4 個巨大 node。

建議拆成 7 個可重用、可驗證的核心 node：

```text
LoadResearchRun
BuildEvidencePacket
CheckEvidenceCoverage
CritiqueAnswer
EvaluateCriticPolicy
DraftRevisedAnswer
FinalizeAnswer
```

## 1. LoadResearchRun

用途：載入既有研究結果。

```text
NodeType: LoadResearchRun

RequiredBlackboardKeys:
- researchRunId

ProducedBlackboardKeys:
- researchRun
- originalQuestion
- originalAnswer
- originalEvidenceRefs

SideEffectLevel: ReadOnly
Retryable: true
SupportsLoop: false
RequiresHumanInput: false
```

## 2. BuildEvidencePacket

用途：把 ResearchRun 裡的 evidence 整理成 critic/revision 可用格式。

```text
NodeType: BuildEvidencePacket

RequiredBlackboardKeys:
- researchRun
- originalEvidenceRefs

ProducedBlackboardKeys:
- evidencePacket

SideEffectLevel: ReadOnly
Retryable: true
SupportsLoop: true
RequiresHumanInput: false
```

產出的 `evidencePacket` 建議包含：
- evidence id。
- source title。
- quote/snippet。
- date。
- relevance。
- citation。
- source type。

## 3. CheckEvidenceCoverage

用途：做 deterministic evidence 檢查，不完全依賴 LLM。

```text
NodeType: CheckEvidenceCoverage

RequiredBlackboardKeys:
- originalQuestion
- originalAnswer
- evidencePacket

ProducedBlackboardKeys:
- evidenceCoverageReport

SideEffectLevel: ReadOnly
Retryable: true
SupportsLoop: true
RequiresHumanInput: false
```

可以檢查：
- answer 裡有哪些 claims。
- 哪些 claims 沒 evidence。
- evidence 是否過舊。
- evidence 是否太少。
- citation 是否缺失。

## 4. CritiqueAnswer

用途：讓 critic agent 評論答案品質。

```text
NodeType: CritiqueAnswer

RequiredBlackboardKeys:
- originalQuestion
- originalAnswer
- evidencePacket
- evidenceCoverageReport

ProducedBlackboardKeys:
- criticReviewResult

SideEffectLevel: ExternalLlmRead
Retryable: true
SupportsLoop: false
RequiresHumanInput: false
ToolsUsed:
- LLM
```

產出應包含：
- critiqueSummary。
- unsupportedClaims。
- missingEvidence。
- reasoningIssues。
- evidenceQualityNotes。
- suggestedFixes。
- confidence。

這個 node 不應該直接決定 workflow 下一步，只負責產生評論結果。

## 5. EvaluateCriticPolicy

用途：用 deterministic policy 把 critic 結果轉成 routing decision。

```text
NodeType: EvaluateCriticPolicy

RequiredBlackboardKeys:
- criticReviewResult
- evidenceCoverageReport

ProducedBlackboardKeys:
- criticPolicyDecision

SideEffectLevel: ReadOnly
Retryable: true
SupportsLoop: false
RequiresHumanInput: false
```

產出應包含：

```text
requiresRevision
requiresMoreEvidence
recommendedNextAction
routeBackTo
reason
```

這個 node 很重要，因為它把 LLM 評論和 workflow 控制分開。

LLM 不直接控制 workflow；workflow runner 只相信 deterministic policy node 的結果。

## 6. DraftRevisedAnswer

用途：根據 critic 結果產生修正版答案。

```text
NodeType: DraftRevisedAnswer

RequiredBlackboardKeys:
- originalQuestion
- originalAnswer
- evidencePacket
- criticReviewResult
- criticPolicyDecision

OptionalBlackboardKeys:
- userFeedback

ProducedBlackboardKeys:
- draftRevisionResult

SideEffectLevel: ExternalLlmRead
Retryable: true
SupportsLoop: false
RequiresHumanInput: false
ToolsUsed:
- LLM
```

產出應包含：
- revisedAnswer。
- revisionNotes。
- changedClaims。
- removedClaims。
- retainedEvidence。
- caveats。

## 7. FinalizeAnswer

用途：決定最後要輸出哪個 answer，並產生 final report。

```text
NodeType: FinalizeAnswer

RequiredBlackboardKeys:
- originalAnswer
- criticPolicyDecision

OptionalBlackboardKeys:
- draftRevisionResult

ProducedBlackboardKeys:
- finalAnswer
- finalAnswerReport

SideEffectLevel: WritesAgentTrace
Retryable: false
SupportsLoop: false
RequiresHumanInput: false
```

`FinalizeAnswer` 應支援兩種情況：

```text
AcceptAnswer:
finalAnswer = originalAnswer
```

```text
ReviseAnswer:
finalAnswer = draftRevisionResult.revisedAnswer
```

## 第一版 Workflow Graph

第一版可以先線性：

```text
LoadResearchRun
-> BuildEvidencePacket
-> CheckEvidenceCoverage
-> CritiqueAnswer
-> EvaluateCriticPolicy
-> DraftRevisedAnswer
-> FinalizeAnswer
```

但 node contract 要先切乾淨，之後才能自然演進成 branching：

```text
LoadResearchRun
-> BuildEvidencePacket
-> CheckEvidenceCoverage
-> CritiqueAnswer
-> EvaluateCriticPolicy
```

然後根據 `criticPolicyDecision.recommendedNextAction` 決定：

```text
AcceptAnswer
-> FinalizeAnswer
```

```text
ReviseAnswer
-> DraftRevisedAnswer
-> FinalizeAnswer
```

```text
CollectMoreEvidenceThenReviseAnswer
-> FollowUpSearch
-> BuildEvidencePacket
-> DraftRevisedAnswer
-> FinalizeAnswer
```

## Planner / Validator 應檢查什麼

如果 workflow 是動態生成，planner/validator 至少要檢查：

- Graph 是否合法。
- 是否為 DAG，或是否只有受控 loop。
- Loop 是否有 maxIterations。
- RequiredBlackboardKeys 是否都能由前面 node 產生。
- ProducedBlackboardKeys 是否有 schema。
- AllowedNextNodeTypes 是否允許目前連線。
- Node side effect 是否允許 retry 或 loop。
- 使用者權限是否允許執行該 node。
- Source policy 是否允許該 node 使用 web/local source。
- 預估成本與時間是否在限制內。
- Human-in-the-loop node 是否支援 waiting/resume。

## 控制動態 Workflow 組合爆炸

如果有很多 node，而且 planner 可以任意挑選、任意排序、任意連接，可組成的 workflow 數量會快速爆炸。

例如：
- 10 個 node 如果允許任意 subset，光 subset 就有 `2^10 = 1024` 種。
- 如果還允許任意排序，會接近 `N!` 等級。
- 如果再允許 branching 或 loop，搜尋空間會更大，也更難完整驗證。

所以 dynamic workflow 不應該等於任意 workflow。

建議採用：

```text
Stage-based ordering
+ Template slots
+ Strategy selection
+ Node contracts
+ Conservative validator
```

核心原則：

```text
不要讓 planner 在 node graph 空間裡自由搜尋；
讓 planner 在「模板 slot + stage strategy」空間裡做有限選擇。
```

## Stage-Based Ordering

Stage-based ordering 是先規定 workflow 必須照階段前進。

例如：

```text
Stage 1: Load
Stage 2: Evidence
Stage 3: Analyze
Stage 4: Decide
Stage 5: Act
Stage 6: Finalize
```

流程只能大致往後走：

```text
Load -> Evidence -> Analyze -> Decide -> Act -> Finalize
```

不允許任意倒退或亂接：

```text
Finalize -> Analyze
DraftRevisedAnswer -> LoadResearchRun
CritiqueAnswer -> BuildEvidencePacket
```

套到 Research Quality Workflow：

```text
Load:
- LoadResearchRun

Evidence:
- BuildEvidencePacket
- LocalSearch
- WebSearch

Analyze:
- CheckEvidenceCoverage
- CritiqueAnswer

Decide:
- EvaluateCriticPolicy

Act:
- DraftRevisedAnswer
- FollowUpSearch
- HumanApproval

Finalize:
- FinalizeAnswer
```

每個 node metadata 可以加：

```text
Stage: Analyze
StageOrder: 30
```

Validator 規定 edge 通常只能從低 `StageOrder` 指向高 `StageOrder`。

如果要支援 loop，只允許受控 loop，例如：

```text
Act -> Evidence
```

但必須搭配：
- `maxIterations`。
- `routeBackTo` 白名單。
- loop reason。
- iteration output versioning。

## Template Slots

Template slots 是讓 workflow 不從零開始組，而是先有固定骨架，每個位置留 slot。

例如：

```text
ResearchQualityReview Template

LoadSlot
-> EvidenceSlot
-> AnalyzeSlot
-> DecideSlot
-> ActSlot
-> FinalizeSlot
```

Template 規定這類 workflow 一定有這幾段，但每個 slot 裡可以放不同 strategy。

例如：

```text
LoadSlot:
- LoadResearchRun

EvidenceSlot:
- ExistingEvidenceOnly
- LocalSearchEvidence
- LocalThenWebEvidence

AnalyzeSlot:
- CriticOnly
- CriticWithEvidenceCoverage

ActSlot:
- AcceptOnly
- DraftRevision
- HumanApprovalThenDraft
- FollowUpSearchThenDraft

FinalizeSlot:
- FinalizeAnswer
```

這樣 planner 不需要考慮所有 node 任意排列，只需要填 slot。

## Strategy Selection

Strategy selection 是每個 slot 裡選一個預先定義好的 strategy，而不是直接讓 planner 排一串 node。

例如 `EvidenceSlot`：

```text
Strategy: ExistingEvidenceOnly
Nodes:
BuildEvidencePacket
```

```text
Strategy: LocalSearchEvidence
Nodes:
BuildEvidencePacket
-> LocalSearch
-> MergeEvidencePacket
```

```text
Strategy: LocalThenWebEvidence
Nodes:
BuildEvidencePacket
-> LocalSearch
-> WebSearch
-> RerankEvidence
-> MergeEvidencePacket
```

Planner 只要決定：

```text
這次 EvidenceSlot 使用 LocalThenWebEvidence
```

而不是自己決定：
- 要不要 LocalSearch。
- LocalSearch 放哪裡。
- WebSearch 放哪裡。
- Rerank 放哪裡。
- Merge 放哪裡。

這會大幅降低組合空間，也讓每個 strategy 可以被獨立測試。

## Node Contracts

即使有 stage、template、strategy，每個 node 還是必須有明確 contract。

至少要有：

```text
NodeType
Stage
RequiredBlackboardKeys
ProducedBlackboardKeys
AllowedNextNodeTypes
Retryable
TimeoutSeconds
SideEffectLevel
SupportsLoop
RequiresHumanInput
```

例如：

```text
NodeType: DraftRevisedAnswer
Stage: Act

RequiredBlackboardKeys:
- originalQuestion
- originalAnswer
- evidencePacket
- criticReviewResult
- criticPolicyDecision

ProducedBlackboardKeys:
- draftRevisionResult

AllowedNextNodeTypes:
- FinalizeAnswer

Retryable: true
SideEffectLevel: ExternalLlmRead
SupportsLoop: false
RequiresHumanInput: false
```

這讓 validator 可以判斷：
- `DraftRevisedAnswer` 不能放在 Load stage。
- `DraftRevisedAnswer` 不能在缺少 `criticReviewResult` 時執行。
- `DraftRevisedAnswer` 後面通常只能接 `FinalizeAnswer`。
- `DraftRevisedAnswer` 可以 retry。
- `DraftRevisedAnswer` 不應該進 loop。

## Conservative Validator

Planner 產生 workflow 後不能直接執行，必須先交給 validator 檢查。

Validator 要保守：寧可擋掉不確定的 workflow，也不要放過不安全 graph。

Validator 應檢查：
- Stage order 是否合法。
- Template slot 是否都有填。
- RequiredBlackboardKeys 是否能由前面 node 產生。
- AllowedNextNodeTypes 是否允許目前 edge。
- 是否有且只有一個 finalize node。
- Loop 是否有 `maxIterations`。
- 有 side effect 的 node 是否被放進 loop。
- HumanApproval 是否支援 waiting/resume。
- 使用者權限是否允許。
- 預估成本與時間是否超過上限。
- Workflow definition 是否能被 persist。

Planner 可以聰明，但 validator 必須保守。

## 組合爆炸的實際壓縮方式

如果有 30 個 node，任意 graph 會非常難控。

改成 template/slot/strategy 後，組合空間會變成：

```text
模板數量 * 每個 slot 的 strategy 數量
```

而不是：

```text
所有 node subset * 任意排列 * 任意 edges
```

例如：

```text
3 templates * 3 evidence strategies * 2 analyze strategies * 3 act strategies
= 54 種
```

這比任意組合 30 個 node 小很多，而且每種 strategy 都能被測試與驗證。

## 套用到 Research Quality Workflow

使用者需求：

```text
幫我檢查這份研究答案是否可靠，必要時修正。
```

不要讓 LLM 直接產生任意 graph：

```text
LoadResearchRun -> RandomNodeA -> Draft -> Search -> Critique -> Finalize
```

應該這樣做：

```text
Template = ResearchQualityReview

Stage order:
Load -> Evidence -> Analyze -> Decide -> Act -> Finalize

Slots:
LoadSlot = LoadResearchRun
EvidenceSlot = ExistingEvidenceOnly
AnalyzeSlot = CriticWithEvidenceCoverage
DecideSlot = CriticPolicy
ActSlot = DraftRevision
FinalizeSlot = FinalizeAnswer
```

Strategy 展開後：

```text
LoadResearchRun
-> BuildEvidencePacket
-> CheckEvidenceCoverage
-> CritiqueAnswer
-> EvaluateCriticPolicy
-> DraftRevisedAnswer
-> FinalizeAnswer
```

再由 validator 檢查：
- 每個 node input 是否都有。
- stage 順序是否正確。
- edge 是否合法。
- loop 是否安全。
- side effect 是否安全。

通過後才 persist workflow definition 並執行。

## 不建議的做法

- 不要讓 LLM 直接輸出任意 workflow graph。
- 不要讓所有 node 互相 `AllowedNext`。
- 不要用一個超大 `UniversalResearchAgentNode` 吃掉所有邏輯。
- 不要一開始就做 generic arbitrary workflow engine。

LLM 可以輔助：
- intent classification。
- template selection。
- strategy selection。
- parameter filling。

但第一版不應該讓 LLM 任意決定：
- node type。
- edge。
- loop。
- side effect。
- finalize behavior。

一句話總結：

```text
動態 workflow 應該是有限模板內的動態選擇，
不是所有 node 任意排列組合。
```

## 分工建議

## 角色 1：Workflow Runtime Owner

負責 workflow 如何可靠執行。

工作內容：
- Background execution。
- 建立 run 後立即回傳 `runId`。
- Background worker 執行 workflow。
- Status transition 使用 `AgentStateMachine`。
- 防止同一 run 被重複執行。
- Retry / failed recovery 基礎。
- Cancellation 基礎。
- 後續 SSE 串接點。

交付物：
- `POST /api/agent-runs/...` 不再同步跑完整 workflow。
- Run 可在背景從 `Pending -> Running -> Succeeded/Failed`。
- 現有 persisted nodes/events/toolCalls 繼續可用。
- 測試覆蓋 background queue、worker、failure path。

## 角色 2：Node Pool / Catalog Owner

負責定義 node 能力規格。

工作內容：
- 定義 `AgentNodeCatalogEntry` 或類似 contract。
- 定義 node metadata 欄位。
- 定義 blackboard key constants。
- 定義 required/produced blackboard keys。
- 定義 node type naming。
- 定義 validator 檢查規則。
- 決定第一批可用 nodes。

交付物：
- Node catalog 可列出所有 node。
- Validator 可以檢查 workflow 是否缺資料。
- Planner 不能亂接 node。
- 文件說明每個 node 的 input/output。

## 角色 3：Node Handler Owner

負責每個 node 實際做事。

第一批 node：

```text
LoadResearchRun
BuildEvidencePacket
CheckEvidenceCoverage
CritiqueAnswer
EvaluateCriticPolicy
DraftRevisedAnswer
FinalizeAnswer
```

可拆成兩人：

Backend B 負責 CriticReview nodes：
- LoadResearchRun。
- BuildEvidencePacket。
- CheckEvidenceCoverage。
- CritiqueAnswer。
- EvaluateCriticPolicy。

Backend C 負責 Draft/Finalize nodes：
- DraftRevisedAnswer。
- FinalizeAnswer。
- 未來 HumanApproval。
- 未來 FollowUpSearch。

交付物：
- 每個 handler 只做 node business logic。
- 每個 handler 寫入明確 blackboard key。
- 每個 handler 有 deterministic test。
- LLM handler 和 deterministic fake 分開。

重要規則：
- Handler 不應該自己決定整個 workflow routing。
- Handler 不應該直接亂寫 run status。
- Handler 不應該依賴隱式 JSON key。
- Handler output 要符合 node contract。

## 角色 4：Frontend Owner

負責把 workflow 變成使用者能操作的產品。

工作內容：
- Research result 頁加入「執行 Critic Review」。
- AgentRun detail 頁。
- Node timeline。
- CriticReview result rendering。
- DraftRevision trigger。
- DraftRevision result rendering。
- Polling progress。
- Loading/error/empty state。
- 不做 upload UI。

交付物：
- 使用者可從研究結果觸發 CriticReview。
- 可看到每個 node 狀態。
- 可看到 critic policy decision。
- Policy 允許時可觸發 DraftRevision。
- Mobile/desktop 基本可用。

## 角色 5：Test / Docs / E2E Owner

負責品質與交接。

工作內容：
- Workflow contract tests。
- Node catalog validation tests。
- Failure/retry tests。
- User isolation tests。
- Handoff docs。
- Local E2E 測試。
- 檢查 Aspire traces/events/toolCalls 是否完整。

交付物：
- 每個 workflow 有 definition contract test。
- 每個 node 有 handler coverage test。
- Background execution 有成功/失敗測試。
- 前後端 E2E checklist。

## 建議實作順序

## Phase 1：Background Agent Runtime

目標：workflow 不再卡在單一 HTTP request 裡。

負責人：Workflow Runtime Owner。

要做：
- Background queue。
- API create run 後立即回傳 `runId`。
- Background worker 執行 workflow。
- 保留 persisted nodes/events/toolCalls。
- 防止同一 run 被重複執行。
- 加上 cancellation / failed recovery 基礎。

不要先做：
- Generic loop engine。
- Arbitrary dynamic planner。
- Human-in-the-loop。
- SSE。

## Phase 2：Node Pool 第一版

目標：讓現有 workflow 先有清楚 node contract。

負責人：Node Pool Owner + Node Handler Owner。

要做：
- 定義 node metadata。
- 定義 blackboard keys。
- 把現有 CriticReview/DraftRevision nodes 對應到 catalog。
- 加 validator。
- 先支援靜態 workflow 也可以。

重點是 contract 先乾淨，之後才能支援 branching、loop、composition。

## Phase 3：Frontend Critic Agent v0

目標：使用者可以完整操作目前已有的 CriticReview/DraftRevision。

負責人：Frontend Owner。

要做：
- Research result 頁加 CriticReview action。
- AgentRun detail 顯示 node timeline。
- 顯示 CriticReview policy decision。
- 條件允許時觸發 DraftRevision。
- 支援 polling。
- 不做 upload。

## Phase 4：Composition Workflow

目標：新增上層 workflow 串起 CriticReview 和 DraftRevision。

負責人：Workflow Runtime Owner + Node Pool Owner。

要做：
- 新 workflow type：`ResearchQualityReview`。
- Policy node 決定是否進 DraftRevision。
- 前端可只觸發一個 workflow。
- CriticReview / DraftRevision 仍可保留為獨立 workflow。

## Phase 5：Evidence Gap / Controlled Loop

目標：critic 發現 evidence 不足後可以補證據再修正。

負責人：Node Handler Owner + Workflow Runtime Owner。

要做：
- `FollowUpSearch`。
- `EvidenceGap`。
- `maxIterations`。
- `routeBackTo` 白名單。
- 每輪 output 保留，不覆蓋上一輪。

## Phase 6：Human-in-the-loop

目標：讓使用者可以在關鍵節點介入。

負責人：Workflow Runtime Owner + Frontend Owner。

要做：
- `WaitingForFeedback` status。
- Handler 可建立 feedback request。
- `AgentFeedback` 實際被使用。
- 使用者提交 feedback 後 resume workflow。

## 最小可交付版本

MVP：Research Quality Workflow v0。

Backend：
- API create run 後背景執行。
- Node pool 定義 7 個核心 nodes。
- Persist workflow definition。
- Validator 檢查 required/produced blackboard keys。
- CriticReview/DraftRevision 可由現有 handler 跑通。

Frontend：
- Research result 頁可觸發 CriticReview。
- AgentRun detail 顯示 node timeline。
- CriticReview 完成後可觸發 DraftRevision。
- 顯示 final/revised answer。

## 設計原則

- 越動態的 workflow，node contract 越要嚴格。
- Node 是固定能力，workflow 是動態組合。
- LLM 可以輔助 planner，但不能任意創建 node type。
- Workflow definition 必須 persist。
- Handler 只做 node business logic，不控制整體 routing。
- Policy decision 應由 deterministic node 產生，不直接相信 LLM routing。
- Side effect、retry、loop、human pause 必須在 metadata 裡明確宣告。
- 第一版先做受控 dynamic workflow，不要過度工程化。

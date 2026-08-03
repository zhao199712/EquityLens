# Prompt Management handoff — 2026-08-03

## Delivered

V3 Agent Prompt Management 的第一版已加入後端、管理後台與資料庫 migration。

- `PromptTemplate`、`PromptVersion`、`PromptBinding`、`PromptAuditLog` 與 `AgentRunPromptSnapshot` 為新持久化模型。
- Version lifecycle：`Draft → Published → Retired`；`Disabled` template 不可再使用。發布和選定 usage binding 的切換在單一交易內完成；Retired version 仍可被既有 binding 或歷史 snapshot 使用。
- 管理 mutation 採 `RequestId`、concurrency token；衝突以 `prompt_concurrency_conflict`、`prompt_binding_conflict`、`prompt_version_not_editable`、`prompt_variable_contract_invalid` 等代碼回應。
- `/api/admin/prompts` 提供 template、draft、content、publish、rollback、disable、binding 與 audit API。所有 endpoint 都要求 `Admin`。
- 新 Agent Run 由 `IAgentPromptSnapshotService` 依 workflow 的 stable usage key 建立 immutable snapshot；若找不到 active binding，會 fail closed 為 `prompt_configuration_missing`。
- Agent Run detail 對擁有者／Admin 顯示 usage key、template key、version、hash 與 resolved time；prompt 內容僅可用 Admin snapshot-content endpoint 讀取。
- `/admin/prompts` 提供 template 清單、草稿建立、內容檢視、版本發布與 active-binding 檢視入口。

首次啟動已升級資料庫時，`PromptManagementBootstrapService` 會建立現有 V3 usage keys 的 baseline template/version/binding。這個 bootstrap 僅初始化資料；執行期間解析的是資料庫 binding 和 run snapshot，不會回退至 bootstrap catalog。

## Migration and rollout

1. Apply migrations before starting the API:

   ```bash
   ~/.dotnet/tools/dotnet-ef database update --project src/EquityLens.Api --startup-project src/EquityLens.Api
   ```

2. Start API once to create baseline rows, then have an Admin review and replace each baseline prompt with the exact production prompt text before enabling prompt content injection for a capability.
3. Confirm the Prompt Management page shows all V3 usage keys and that each has an active binding.
4. Observe `prompt_configuration_missing`; it means a binding was removed/disabled and the run was intentionally not started.

## Important follow-up

The persistence, lifecycle, authorization and immutable run receipt are complete. The remaining P0 execution hardening is to replace each existing in-code LLM system prompt with its corresponding run snapshot at the individual Agent adapter boundary, and to write `PromptSnapshotId` / rendered hash to every LLM `AgentToolCall`. The schema fields are already present for that wiring. This is deliberately called out because the baseline seed protects configuration completeness but does not silently rewrite established prompt behavior.

Recommended order:

1. Router and dynamic planner (pre-run receipt and dynamic-node coverage).
2. Critic, DraftRevision and Portfolio narrative adapters.
3. Evidence remediation/reanalysis adapters and research answer generation.
4. Add API/service tests for publish races and an Admin Playwright flow that creates, publishes, binds, starts a run and verifies its snapshot.

## Validation performed

- `dotnet build src/EquityLens.Api/EquityLens.Api.csproj --no-restore` — passed (existing `NU1510`; sandbox also cannot query NuGet vulnerability data).
- `dotnet test tests/EquityLens.Api.Tests/EquityLens.Api.Tests.csproj --no-restore` — 684 passed.
- `npm test -- --run` — 98 passed.
- `npm run build` — passed (existing Rolldown pure-annotation warnings).

The local PostgreSQL service was not listening while this handoff was written, so the new migration was generated but not applied in this session.

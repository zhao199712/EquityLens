using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EquityLens.Api.Bootstrap;

public static class E2EBootstrap
{
    public static void MapEquityLensE2E(this WebApplication app)
    {
        E2ETestAccess.RequireEnabled(app.Configuration, app.Environment);
        app.MapPost("/api/e2e/approval-gate-runs", async (
            ICurrentUserContext currentUser,
            E2EApprovalGateWorkflowDefinitionProvider provider,
            IAgentWorkflowAdminService workflowAdmin,
            IAgentWorkflowCatalog catalog,
            EquityLensDbContext db,
            IAgentRunQueue queue,
            CancellationToken cancellationToken) =>
        {
            await workflowAdmin.EnsureEnabledAsync(E2EApprovalGateWorkflow.WorkflowType, cancellationToken);
            var run = provider.CreateRun(currentUser.UserId, Guid.NewGuid());
            var policies = await workflowAdmin.GetPoliciesAsync([E2EApprovalGateWorkflow.NodeType], cancellationToken);
            var approvals = await workflowAdmin.GetApprovalPoliciesAsync([E2EApprovalGateWorkflow.NodeType], cancellationToken);
            var entry = catalog.GetNode(E2EApprovalGateWorkflow.NodeType);
            var root = JsonNode.Parse(run.WorkflowDefinitionJson)!.AsObject();
            var node = root["nodes"]!.AsArray()[0]!.AsObject();
            var policy = policies[E2EApprovalGateWorkflow.NodeType];
            node["executionPolicy"] = new JsonObject { ["timeoutSeconds"] = policy.TimeoutSeconds, ["maxRetryCount"] = policy.MaxRetryCount };
            node["contract"] = JsonSerializer.SerializeToNode(entry.Contract, AgentNodeJson.SerializerOptions);
            node["approvalPolicy"] = new JsonObject
            {
                ["requiresHumanApproval"] = approvals[E2EApprovalGateWorkflow.NodeType],
                ["sideEffectLevel"] = entry.SideEffectLevel,
                ["reason"] = "E2E deterministic side effect requires approval."
            };
            run.WorkflowDefinitionJson = root.ToJsonString(AgentNodeJson.SerializerOptions);
            db.AgentRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken);
            await queue.EnqueueAsync(new AgentRunQueueMessage(run.Id, run.UserId, run.WorkflowType, DateTime.UtcNow), cancellationToken);
            return Results.Accepted($"/api/agent-runs/{run.Id}", new { agentRunId = run.Id, run.Status });
        }).RequireAuthorization(policy => policy.RequireRole("Admin"));

        app.MapPost("/api/e2e/approval-gate-runs/{runId:guid}/execute", async (
            Guid runId,
            ICurrentUserContext currentUser,
            IAgentRunExecutor executor,
            EquityLensDbContext db,
            CancellationToken cancellationToken) =>
        {
            var exists = await db.AgentRuns.AnyAsync(
                x => x.Id == runId && x.UserId == currentUser.UserId && x.WorkflowType == E2EApprovalGateWorkflow.WorkflowType,
                cancellationToken);
            if (!exists) return Results.NotFound();
            await executor.ExecuteAsync(runId, currentUser.UserId, cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization(policy => policy.RequireRole("Admin"));
    }

    public static async Task SeedEquityLensE2EAsync(this WebApplication app)
    {
        E2ETestAccess.RequireEnabled(app.Configuration, app.Environment);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EquityLensDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        const string email = E2ETestAccess.AdminEmail;
        var password = app.Configuration["E2E_ADMIN_PASSWORD"]!;
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email);
        if (user is null)
        {
            user = new AppUser { Id = Guid.NewGuid(), Email = email, DisplayName = "Playwright E2E Admin", Role = "Admin", IsActive = true };
            db.Users.Add(user);
        }
        if (user.Role != "Admin")
            throw new InvalidOperationException("Refusing to promote an existing account to E2E administrator.");
        user.IsActive = true;
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        user.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}

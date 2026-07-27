using System.Security.Claims;
using EquityLens.Api.Contracts.Chat;
using EquityLens.Api.Controllers;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Chat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Tests.Controllers;

public sealed class ChatControllerTests
{
    [Fact]
    public async Task CreateSession_WhenNoEmptySession_CreatesNew()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var controller = Controller(db, userId);

        var result = await controller.CreateSession(new CreateSessionRequest("新對話"), CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<CreateSessionResponse>(response.Value);
        Assert.Equal(0, payload.MessageCount);
        Assert.Equal(1, await db.ChatSessions.CountAsync(x => x.UserId == userId));
    }

    [Fact]
    public async Task CreateSession_WhenEmptySessionExists_ReturnsExisting()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var existing = new ChatSession { Id = Guid.NewGuid(), UserId = userId, Title = "舊空對話" };
        db.ChatSessions.Add(existing);
        await db.SaveChangesAsync();
        var controller = Controller(db, userId);

        var result = await controller.CreateSession(new CreateSessionRequest(null), CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<CreateSessionResponse>(response.Value);
        Assert.Equal(existing.Id, payload.Id);
        Assert.Equal(1, await db.ChatSessions.CountAsync(x => x.UserId == userId));
    }

    [Fact]
    public async Task CreateSession_WhenOnlyNonEmptySessionExists_CreatesNew()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var used = new ChatSession { Id = Guid.NewGuid(), UserId = userId, Title = "已用對話" };
        db.ChatSessions.Add(used);
        db.ChatMessages.Add(new ChatMessage { Id = Guid.NewGuid(), ChatSessionId = used.Id, Role = "user", Content = "嗨", SequenceNumber = 0 });
        await db.SaveChangesAsync();
        var controller = Controller(db, userId);

        var result = await controller.CreateSession(new CreateSessionRequest(null), CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<CreateSessionResponse>(response.Value);
        Assert.NotEqual(used.Id, payload.Id);
        Assert.Equal(2, await db.ChatSessions.CountAsync(x => x.UserId == userId));
    }

    private static ChatController Controller(EquityLensDbContext db, Guid userId) => new(db, new FakeConversation())
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString())]))
            }
        }
    };

    private static TestDb CreateDb() => new(new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class TestDb(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) { base.OnModelCreating(modelBuilder); modelBuilder.Ignore<DocumentEmbedding>(); }
    }

    private sealed class FakeConversation : IConversationService
    {
        public Task<ConversationOutcome> ProcessAsync(Guid userId, Guid sessionId, SendMessageRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConversationRunCardDto?> GetRunCardAsync(Guid userId, Guid sessionId, Guid agentRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

using System.Text.Json;
using EquityLens.Api.Contracts.Chat;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Observability;
using EquityLens.Api.Services.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly EquityLensDbContext _db;
    private readonly IConversationService _conversation;

    public ChatController(EquityLensDbContext db, IConversationService conversation)
    {
        _db = db;
        _conversation = conversation;
    }

    [HttpPost("sessions")]
    public async Task<ActionResult<CreateSessionResponse>> CreateSession(
        CreateSessionRequest request, CancellationToken ct)
    {
        using var activity = EquityLensTelemetry.ActivitySource.StartActivity("chat.session.create");

        var userId = GetUserId();
        var session = new ChatSession
        {
            UserId = userId,
            Title = request.Title
        };
        _db.ChatSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        EquityLensTelemetry.ChatSessions.Add(1);
        activity?.SetTag("chat.session_id", session.Id);

        return Ok(new CreateSessionResponse(session.Id, session.Title, session.CreatedAtUtc, session.UpdatedAtUtc, 0));
    }

    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<ChatSessionDto>>> ListSessions(CancellationToken ct)
    {
        var userId = GetUserId();
        var sessions = await _db.ChatSessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAtUtc)
            .Select(s => new ChatSessionDto(
                s.Id, s.Title, s.CreatedAtUtc, s.UpdatedAtUtc, s.Messages.Count))
            .ToListAsync(ct);
        return Ok(sessions);
    }

    [HttpGet("sessions/{sessionId:guid}/messages")]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> GetMessages(
        Guid sessionId, CancellationToken ct)
    {
        var userId = GetUserId();
        var session = await _db.ChatSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session == null) return NotFound();

        var messages = await _db.ChatMessages
            .Where(m => m.ChatSessionId == sessionId)
            .OrderBy(m => m.SequenceNumber)
            .ToListAsync(ct);
        var response = new List<ChatMessageDto>();
        foreach (var message in messages)
        {
            var card = message.AgentRunId is { } runId
                ? await _conversation.GetRunCardAsync(userId, sessionId, runId, ct)
                : null;
            response.Add(new(message.Id, message.Role, message.Content, message.ToolName,
                message.SequenceNumber, message.CreatedAtUtc, message.MessageType, message.AgentRunId, card));
        }
        return Ok(response);
    }

    [HttpPost("sessions/{sessionId:guid}/messages")]
    public async Task SendMessage(
        Guid sessionId, SendMessageRequest request, CancellationToken ct)
    {
        var userId = GetUserId();
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        try
        {
            var outcome = await _conversation.ProcessAsync(userId, sessionId, request, ct);
            await WriteEventAsync(new { type = "conversation_action", action = outcome.Action }, ct);
            if (outcome.Action == ConversationActions.AskClarification)
                await WriteEventAsync(new { type = "clarification", content = outcome.Content }, ct);
            else
                await WriteEventAsync(new { type = "delta", content = outcome.Content }, ct);
            if (outcome.RunCard is not null)
                await WriteEventAsync(new { type = "run_created", messageId = outcome.AssistantMessageId, runCard = outcome.RunCard }, ct);
            await WriteEventAsync(new { type = "done", outcome.Model, outcome.PromptTokens, outcome.CompletionTokens }, ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (ConversationException exception)
        {
            await WriteEventAsync(new { type = "error", code = exception.Code, message = exception.Message }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await WriteEventAsync(new { type = "error", code = "conversation_unavailable", message = ex.Message }, CancellationToken.None);
        }
    }

    [HttpGet("sessions/{sessionId:guid}/run-cards/{agentRunId:guid}")]
    public async Task<ActionResult<ConversationRunCardDto>> GetRunCard(
        Guid sessionId, Guid agentRunId, CancellationToken ct)
    {
        var card = await _conversation.GetRunCardAsync(GetUserId(), sessionId, agentRunId, ct);
        return card is null ? NotFound() : Ok(card);
    }

    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> DeleteSession(Guid sessionId, CancellationToken ct)
    {
        var userId = GetUserId();
        var session = await _db.ChatSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session == null) return NotFound();

        _db.ChatSessions.Remove(session);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst("sub") ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        return claim != null ? Guid.Parse(claim.Value) : Guid.Empty;
    }

    private async Task WriteEventAsync(object value, CancellationToken ct)
    {
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(value)}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}

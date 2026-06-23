using System.Diagnostics;
using System.Text;
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
    private readonly IChatAgentService _chatAgentService;

    public ChatController(EquityLensDbContext db, IChatAgentService chatAgentService)
    {
        _db = db;
        _chatAgentService = chatAgentService;
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
            .Select(m => new ChatMessageDto(
                m.Id, m.Role, m.Content, m.ToolName, m.SequenceNumber, m.CreatedAtUtc))
            .ToListAsync(ct);
        return Ok(messages);
    }

    [HttpPost("sessions/{sessionId:guid}/messages")]
    public async Task SendMessage(
        Guid sessionId, SendMessageRequest request, CancellationToken ct)
    {
        using var activity = EquityLensTelemetry.ActivitySource.StartActivity("chat.message");
        activity?.SetTag("chat.session_id", sessionId);
        activity?.SetTag("chat.message_length", request.Content.Length);

        var userId = GetUserId();
        var session = await _db.ChatSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session == null) { Response.StatusCode = 404; return; }

        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        var userMsg = new ChatMessage
        {
            ChatSessionId = sessionId,
            Role = "user",
            Content = request.Content,
            SequenceNumber = session.Messages.Count
        };
        var history = session.Messages
            .OrderBy(m => m.SequenceNumber)
            .ToList();

        _db.ChatMessages.Add(userMsg);
        await _db.SaveChangesAsync(ct);

        if (string.IsNullOrWhiteSpace(session.Title))
        {
            session.Title = request.Content.Length > 50
                ? request.Content[..50] + "..."
                : request.Content;
        }

        var assistantContent = new StringBuilder();
        var toolExecutions = new List<ChatToolExecution>();
        string? model = null;
        int promptTokens = 0, completionTokens = 0;

        try
        {
            var agentRequest = new ChatAgentRequest(sessionId, request.Content, history);
            await foreach (var evt in _chatAgentService.ChatStreamAsync(agentRequest, ct))
            {
                switch (evt)
                {
                    case ChatAgentStreamEvent.TextDelta delta:
                        await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "delta", content = delta.Delta })}\n\n", ct);
                        await Response.Body.FlushAsync(ct);
                        assistantContent.Append(delta.Delta);
                        break;

                    case ChatAgentStreamEvent.ToolCallStart start:
                        await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "tool_start", tool = start.ToolName, args = start.Arguments })}\n\n", ct);
                        await Response.Body.FlushAsync(ct);
                        break;

                    case ChatAgentStreamEvent.ToolCallEnd end:
                        await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "tool_end", tool = end.ToolName, preview = end.ResultPreview })}\n\n", ct);
                        await Response.Body.FlushAsync(ct);
                        toolExecutions.Add(new ChatToolExecution(end.ToolName, "", end.ResultPreview));
                        break;

                    case ChatAgentStreamEvent.Done done:
                        model = done.Model;
                        promptTokens = done.PromptTokens;
                        completionTokens = done.CompletionTokens;
                        break;

                    case ChatAgentStreamEvent.Error error:
                        await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "error", message = error.Message })}\n\n", ct);
                        await Response.Body.FlushAsync(ct);
                        activity?.SetTag("chat.error", error.Message);
                        break;
                }
            }

            var assistantMsg = new ChatMessage
            {
                ChatSessionId = sessionId,
                Role = "assistant",
                Content = assistantContent.ToString(),
                SequenceNumber = session.Messages.Count + 1
            };
            _db.ChatMessages.Add(assistantMsg);
            session.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            activity?.SetTag("chat.model", model);
            activity?.SetTag("chat.prompt_tokens", promptTokens);
            activity?.SetTag("chat.completion_tokens", completionTokens);
            activity?.SetTag("chat.tool_count", toolExecutions.Count);
            EquityLensTelemetry.ChatMessages.Add(1,
                new TagList { { "model", model ?? "unknown" }, { "has_tool_calls", toolExecutions.Count > 0 } });

            await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "done", model, promptTokens, completionTokens })}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            EquityLensTelemetry.MarkError(activity, ex);
            await Response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "error", message = ex.Message })}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
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
}

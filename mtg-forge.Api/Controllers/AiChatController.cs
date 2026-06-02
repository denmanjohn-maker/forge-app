using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MtgForge.Api.Models;
using MtgForge.Api.Services;

namespace MtgForge.Api.Controllers;

[ApiController]
[Route("api/ai/chat")]
[Authorize]
public class AiChatController : ControllerBase
{
    private readonly AiSessionService _sessionService;
    private readonly RagPipelineService _ragService;

    public AiChatController(AiSessionService sessionService, RagPipelineService ragService)
    {
        _sessionService = sessionService;
        _ragService = ragService;
    }

    [HttpPost("brew")]
    public async Task<IActionResult> Brew([FromBody] AiBrewRequest req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        AiChatSession? session;
        if (string.IsNullOrEmpty(req.SessionId))
        {
            session = await _sessionService.CreateSessionAsync(userId, null);
        }
        else
        {
            session = await _sessionService.GetSessionAsync(req.SessionId);
            if (session == null) return NotFound("Session not found.");
            if (session.UserId != userId) return Forbid();
        }

        // Add user prompt
        await _sessionService.AddMessageAsync(session.Id!, new AiChatMessage { Role = "user", Content = req.Prompt });

        // Retrieve AI completion
        var aiResponse = await _ragService.BrewWithAiAsync(session, req.Prompt);

        // Add assistant response
        await _sessionService.AddMessageAsync(session.Id!, new AiChatMessage { Role = "assistant", Content = aiResponse });

        return Ok(new { sessionId = session.Id, reply = aiResponse });
    }
}

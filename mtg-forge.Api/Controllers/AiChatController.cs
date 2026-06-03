using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
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
    private readonly UserService _userService;
    private readonly DeckService _deckService;
    private readonly ILogger<AiChatController> _logger;

    public AiChatController(
        AiSessionService sessionService,
        RagPipelineService ragService,
        UserService userService,
        DeckService deckService,
        ILogger<AiChatController> logger)
    {
        _sessionService = sessionService;
        _ragService = ragService;
        _userService = userService;
        _deckService = deckService;
        _logger = logger;
    }

    [HttpPost("brew")]
    public async Task<IActionResult> Brew([FromBody] AiBrewRequest req)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        if (req is null || string.IsNullOrWhiteSpace(req.Prompt))
            return BadRequest("A prompt is required.");

        AiChatSession? session;
        if (string.IsNullOrWhiteSpace(req.SessionId))
        {
            session = await _sessionService.CreateSessionAsync(userId, req.DeckId);
        }
        else
        {
            if (!ObjectId.TryParse(req.SessionId, out _))
                return BadRequest("Invalid session id.");

            session = await _sessionService.GetSessionAsync(req.SessionId);
            if (session == null) return NotFound("Session not found.");
            if (session.UserId != userId) return Forbid();
        }

        // Fetch User and Deck context
        var user = await _userService.GetByIdAsync(userId);
        DeckConfiguration? deck = null;
        if (!string.IsNullOrEmpty(session.DeckId))
        {
            deck = await _deckService.GetByIdAsync(session.DeckId);
        }

        // Call the LLM BEFORE persisting the user message so a transient AI failure
        // doesn't leave a dangling user message (which would corrupt the chat history
        // and could produce consecutive user turns on retry).
        string aiResponseText;
        List<AiChatAction>? actions;
        try
        {
            var result = await _ragService.BrewWithAiAsync(session, req.Prompt, user, deck);
            aiResponseText = result.Reply;
            actions = result.Actions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BrewWithAiAsync failed for session {SessionId}", session.Id);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = "The AI service is currently unavailable. Please try again." });
        }

        await _sessionService.AddMessageAsync(session.Id!, new AiChatMessage { Role = "user", Content = req.Prompt });
        await _sessionService.AddMessageAsync(session.Id!, new AiChatMessage 
        { 
            Role = "assistant", 
            Content = aiResponseText,
            Actions = actions
        });

        return Ok(new { sessionId = session.Id, reply = aiResponseText, actions = actions });
    }
}

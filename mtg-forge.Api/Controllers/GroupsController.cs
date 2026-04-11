using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MtgForge.Api.Models;
using MtgForge.Api.Services;

namespace MtgForge.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class GroupsController : ControllerBase
{
    private readonly UserService _userService;

    public GroupsController(UserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<ActionResult<List<Group>>> GetAll()
    {
        var groups = await _userService.GetAllGroupsAsync();
        return Ok(groups);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Group>> GetById(string id)
    {
        var group = await _userService.GetGroupByIdAsync(id);
        if (group is null)
            return NotFound();
        return Ok(group);
    }

    [HttpPost]
    public async Task<ActionResult<Group>> Create([FromBody] Group group)
    {
        var created = await _userService.CreateGroupAsync(group);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var deleted = await _userService.DeleteGroupAsync(id);
        if (!deleted)
            return NotFound();
        return NoContent();
    }
}

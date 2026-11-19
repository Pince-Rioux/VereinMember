using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VereinMember.Areas.Membership.Dtos;
using VereinMember.Extensions;
using VereinMember.Services;

namespace VereinMember.Areas.Membership.Controllers;

[ApiController]
[Authorize]
[RequireMembershipEnabled]
[Area("Membership")]
[Route("membership/api/family-groups")]
public class FamilyGroupsController : ControllerBase
{
    private readonly IMemberService _memberService;

    public FamilyGroupsController(IMemberService memberService)
    {
        _memberService = memberService;
    }

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> GetAll()
    {
        var groups = await _memberService.GetAllFamilyGroupsAsync();
        return Ok(groups.Select(g => FamilyGroupResponseDto.FromFamilyGroup(g, includeInternalNotes: true)).ToList());
    }

    [HttpGet("{id}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> GetById(int id)
    {
        var group = await _memberService.GetFamilyGroupByIdAsync(id);
        if (group == null)
            return NotFound();

        return Ok(FamilyGroupResponseDto.FromFamilyGroup(group, includeInternalNotes: true));
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> Create([FromBody] CreateFamilyGroupRequestDto request)
    {
        var created = await _memberService.CreateFamilyGroupAsync(
            request.Name, User.Identity?.Name ?? "system");

        return CreatedAtAction(
            nameof(GetById),
            new { id = created.Id },
            FamilyGroupResponseDto.FromFamilyGroup(created, includeInternalNotes: true));
    }

    [HttpPost("{familyGroupId}/members/{memberId}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> AddMember(int familyGroupId, int memberId)
    {
        try
        {
            var group = await _memberService.AddMemberToFamilyGroupAsync(memberId, familyGroupId);
            return Ok(FamilyGroupResponseDto.FromFamilyGroup(group, includeInternalNotes: true));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not found",
                Status = 404,
                Detail = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation error",
                Status = 400,
                Detail = ex.Message
            });
        }
    }

    [HttpDelete("{familyGroupId}/members/{memberId}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> RemoveMember(int familyGroupId, int memberId)
    {
        try
        {
            var group = await _memberService.RemoveMemberFromFamilyGroupAsync(memberId);
            return Ok(FamilyGroupResponseDto.FromFamilyGroup(group, includeInternalNotes: true));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not found",
                Status = 404,
                Detail = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Remove blocked",
                Status = 409,
                Detail = ex.Message
            });
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var result = await _memberService.DeleteFamilyGroupAsync(id);
            if (!result)
                return NotFound();

            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Delete blocked",
                Status = 409,
                Detail = ex.Message
            });
        }
    }

    [HttpPost("{id}/dissolve")]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> Dissolve(int id)
    {
        var result = await _memberService.DissolveFamilyGroupAsync(id, User.Identity?.Name ?? "system");
        if (!result)
            return NotFound();

        return NoContent();
    }
}

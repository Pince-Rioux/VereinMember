using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VereinMember.Areas.Membership.Dtos;
using VereinMember.Extensions;
using VereinMember.Models;
using VereinMember.Services;

namespace VereinMember.Areas.Membership.Controllers;

[ApiController]
[Authorize]
[RequireMembershipEnabled]
[Area("Membership")]
[Route("membership/api/members")]
public class MembersController : ControllerBase
{
    private readonly IMemberService _memberService;
    private readonly IValidator<Member> _validator;

    public MembersController(IMemberService memberService, IValidator<Member> validator)
    {
        _memberService = memberService;
        _validator = validator;
    }

    private bool CanViewInternalNotes() =>
        User.IsInRole("Admin") || User.IsInRole("Editor");

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.MemberView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? searchTerm,
        [FromQuery] MemberStatus? status,
        [FromQuery] int? departmentId,
        [FromQuery] FeeCategory? feeCategory,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var (items, totalCount) = await _memberService.SearchAsync(
            searchTerm, status, departmentId, feeCategory, page, pageSize);

        var includeNotes = CanViewInternalNotes();
        var dtos = items.Select(m => MemberResponseDto.FromMember(m, includeNotes)).ToList();

        return Ok(new { items = dtos, totalCount, page, pageSize });
    }

    [HttpGet("{id}")]
    [Authorize(Policy = AuthorizationPolicies.MemberView)]
    public async Task<IActionResult> GetById(int id)
    {
        var member = await _memberService.GetByIdAsync(id);
        if (member == null)
            return NotFound();

        return Ok(MemberResponseDto.FromMember(member, CanViewInternalNotes()));
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.MemberWrite)]
    public async Task<IActionResult> Create([FromBody] CreateMemberRequestDto request)
    {
        var member = new Member
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Phone = request.Phone,
            BirthDate = request.BirthDate,
            Street = request.Street,
            City = request.City,
            ZipCode = request.ZipCode,
            Country = request.Country,
            BankIban = request.BankIban,
            BankBic = request.BankBic,
            DepartmentId = request.DepartmentId,
            MembershipFeeId = request.MembershipFeeId,
            GuardianMemberId = request.GuardianMemberId,
            FamilyGroupId = request.FamilyGroupId,
            Notes = request.Notes,
            Status = MemberStatus.Active
        };

        var validation = await _validator.ValidateAsync(member);
        if (!validation.IsValid)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation error",
                Status = 400,
                Detail = string.Join("; ", validation.Errors.Select(e => e.ErrorMessage))
            });
        }

        try
        {
            var created = await _memberService.CreateAsync(member, User.Identity?.Name ?? "system");
            return CreatedAtAction(
                nameof(GetById),
                new { id = created.Id },
                MemberResponseDto.FromMember(created, CanViewInternalNotes()));
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

    [HttpPut("{id}")]
    [Authorize(Policy = AuthorizationPolicies.MemberWrite)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateMemberRequestDto request)
    {
        var member = new Member
        {
            Id = id,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Phone = request.Phone,
            BirthDate = request.BirthDate,
            Street = request.Street,
            City = request.City,
            ZipCode = request.ZipCode,
            Country = request.Country,
            BankIban = request.BankIban,
            BankBic = request.BankBic,
            DepartmentId = request.DepartmentId,
            MembershipFeeId = request.MembershipFeeId,
            GuardianMemberId = request.GuardianMemberId,
            FamilyGroupId = request.FamilyGroupId,
            Notes = request.Notes,
            ConcurrencyToken = request.ConcurrencyToken
        };

        var validation = await _validator.ValidateAsync(member);
        if (!validation.IsValid)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation error",
                Status = 400,
                Detail = string.Join("; ", validation.Errors.Select(e => e.ErrorMessage))
            });
        }

        try
        {
            var updated = await _memberService.UpdateAsync(member, User.Identity?.Name ?? "system");
            return Ok(MemberResponseDto.FromMember(updated, CanViewInternalNotes()));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
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

    [HttpDelete("{id}")]
    [Authorize(Policy = AuthorizationPolicies.MemberDelete)]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var result = await _memberService.SoftDeleteAsync(id, User.Identity?.Name ?? "system");
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

    [HttpGet("{id}/export")]
    [Authorize(Policy = AuthorizationPolicies.MemberWrite)]
    public async Task<IActionResult> Export(int id)
    {
        var member = await _memberService.GetByIdAsync(id);
        if (member == null)
            return NotFound();

        var export = new
        {
            exportDate = DateTime.UtcNow,
            requestedById = id,
            personalData = new
            {
                name = $"{member.FirstName} {member.LastName}",
                email = member.Email,
                phone = member.Phone,
                birthDate = member.BirthDate,
                address = new
                {
                    street = member.Street,
                    city = member.City,
                    zipCode = member.ZipCode,
                    country = member.Country
                },
                bankInfo = MemberResponseDto.MaskIban(member.BankIban),
                membership = new
                {
                    status = member.Status.ToString(),
                    joinedAt = member.JoinedAt,
                    department = member.Department?.Name,
                    fee = member.MembershipFee != null
                        ? new
                        {
                            name = member.MembershipFee.Name,
                            amount = member.MembershipFee.Amount,
                            interval = member.MembershipFee.Interval.ToString()
                        }
                        : null,
                    guardian = member.GuardianMember != null
                        ? $"{member.GuardianMember.FirstName} {member.GuardianMember.LastName}"
                        : null,
                    familyGroup = member.FamilyGroup?.Name
                },
                notes = member.Notes,
                createdAt = member.CreatedAt,
                updatedAt = member.UpdatedAt
            },
            dataRetentionPolicy = "Member data retained per club policy. See DSGVO_SECURITY.md"
        };

        return Ok(export);
    }

    [HttpDelete("{id}/erasure")]
    [Authorize(Policy = AuthorizationPolicies.MemberDelete)]
    public async Task<IActionResult> Erasure(int id)
    {
        try
        {
            await _memberService.ErasureAsync(id, User.Identity?.Name ?? "system");
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Erasure blocked",
                Status = 409,
                Detail = ex.Message
            });
        }
    }

    [HttpGet("guardian-candidates")]
    [Authorize(Policy = AuthorizationPolicies.MemberWrite)]
    public async Task<IActionResult> GetGuardianCandidates()
    {
        var candidates = await _memberService.GetGuardianCandidatesAsync();
        var dtos = candidates
            .Select(c => new GuardianCandidateDto(c.Id, $"{c.LastName}, {c.FirstName}"))
            .ToList();
        return Ok(dtos);
    }

    [HttpGet("{id}/dependents")]
    [Authorize(Policy = AuthorizationPolicies.MemberView)]
    public async Task<IActionResult> GetDependents(int id)
    {
        var member = await _memberService.GetByIdAsync(id);
        if (member == null)
            return NotFound();

        var dependents = await _memberService.GetDependentsAsync(id);
        var dtos = dependents
            .Select(d => new DependentDto(d.Id, d.FirstName, d.LastName, d.BirthDate, d.Status.ToString()))
            .ToList();
        return Ok(dtos);
    }
}

using be.Api.Authorization;
using be.Application.Features.Accounts;
using be.Application.Features.Departments;
using be.Contracts.Requests;
using be.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace be.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/departments")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DepartmentsController(DepartmentService departments) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.DepartmentManage)]
    [ProducesResponseType<PagedResult<DepartmentDetails>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<DepartmentDetails>> List([FromQuery] DepartmentSearchRequest request, CancellationToken cancellationToken) =>
        departments.ListAsync(new(request.Page, request.PageSize, request.Search, request.IsActive, request.SortBy, request.Descending), cancellationToken);

    [HttpGet("lookup")]
    [RequirePermission(PermissionCodes.UserManage)]
    [ProducesResponseType<PagedResult<DepartmentLookup>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<DepartmentLookup>> Lookup([FromQuery] DepartmentLookupRequest request, CancellationToken cancellationToken) =>
        departments.LookupAsync(new(request.Page, request.PageSize, request.Search), cancellationToken);

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.DepartmentManage)]
    [ProducesResponseType<DepartmentDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<DepartmentDetails> Get(Guid id, CancellationToken cancellationToken) => departments.GetAsync(id, cancellationToken);

    [HttpPost]
    [Consumes("application/json")]
    [RequestSizeLimit(16384)]
    [RequirePermission(PermissionCodes.DepartmentManage)]
    [ProducesResponseType<DepartmentDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DepartmentDetails>> Create(CreateDepartmentRequest request, CancellationToken cancellationToken)
    {
        var department = await departments.CreateAsync(CurrentAccountActor.From(User), new(request.Name, request.Description), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = department.Id }, department);
    }

    [HttpPatch("{id:guid}")]
    [Consumes("application/json")]
    [RequestSizeLimit(16384)]
    [RequirePermission(PermissionCodes.DepartmentManage)]
    [ProducesResponseType<DepartmentDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<DepartmentDetails> Update(Guid id, UpdateDepartmentRequest request, CancellationToken cancellationToken) =>
        departments.UpdateAsync(CurrentAccountActor.From(User), id, new(request.Name, request.Description, request.Version), cancellationToken);

    [HttpPut("{id:guid}/status")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    [RequirePermission(PermissionCodes.DepartmentManage)]
    [ProducesResponseType<DepartmentDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<DepartmentDetails> SetStatus(Guid id, DepartmentStatusRequest request, CancellationToken cancellationToken) =>
        departments.SetStatusAsync(CurrentAccountActor.From(User), id, new(request.IsActive, request.Version), cancellationToken);
}

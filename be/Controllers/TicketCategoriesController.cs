using be.Api.Authorization;
using be.Application.Features.Accounts;
using be.Application.Features.Categories;
using be.Contracts.Requests;
using be.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace be.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/ticket-categories")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TicketCategoriesController(TicketCategoryService categories) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCodes.CategoryManage)]
    [ProducesResponseType<PagedResult<TicketCategoryDetails>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<TicketCategoryDetails>> List([FromQuery] CategorySearchRequest request, CancellationToken cancellationToken) =>
        categories.ListAsync(new(request.Page, request.PageSize, request.Search, request.ParentId, request.RootsOnly,
            request.IsActive, request.SortBy, request.Descending), cancellationToken);

    [HttpGet("lookup")]
    [RequirePermission(PermissionCodes.TicketCreate)]
    [ProducesResponseType<PagedResult<TicketCategoryLookup>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<PagedResult<TicketCategoryLookup>> Lookup([FromQuery] CategoryLookupRequest request, CancellationToken cancellationToken) =>
        categories.LookupAsync(new(request.Page, request.PageSize, request.Search, request.ParentId, request.RootsOnly), cancellationToken);

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCodes.CategoryManage)]
    [ProducesResponseType<TicketCategoryDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TicketCategoryDetails> Get(Guid id, CancellationToken cancellationToken) => categories.GetAsync(id, cancellationToken);

    [HttpPost]
    [Consumes("application/json")]
    [RequestSizeLimit(16384)]
    [RequirePermission(PermissionCodes.CategoryManage)]
    [ProducesResponseType<TicketCategoryDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TicketCategoryDetails>> Create(CreateTicketCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await categories.CreateAsync(CurrentAccountActor.From(User), new(request.Name, request.Description, request.ParentId), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = category.Id }, category);
    }

    [HttpPatch("{id:guid}")]
    [Consumes("application/json")]
    [RequestSizeLimit(16384)]
    [RequirePermission(PermissionCodes.CategoryManage)]
    [ProducesResponseType<TicketCategoryDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<TicketCategoryDetails> Update(Guid id, UpdateTicketCategoryRequest request, CancellationToken cancellationToken) =>
        categories.UpdateAsync(CurrentAccountActor.From(User), id, new(request.Name, request.Description, request.ParentId, request.Version), cancellationToken);

    [HttpPut("{id:guid}/status")]
    [Consumes("application/json")]
    [RequestSizeLimit(4096)]
    [RequirePermission(PermissionCodes.CategoryManage)]
    [ProducesResponseType<TicketCategoryDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<TicketCategoryDetails> SetStatus(Guid id, TicketCategoryStatusRequest request, CancellationToken cancellationToken) =>
        categories.SetStatusAsync(CurrentAccountActor.From(User), id, new(request.IsActive, request.Version), cancellationToken);
}

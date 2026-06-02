using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace NovaTechCRM.Api.Controllers;

[ApiController]
public abstract class BaseController : ControllerBase
{
    protected int CurrentCustomerId
    {
        get
        {
            var claim = User.FindFirstValue("customerId")
                     ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? throw new UnauthorizedAccessException("No customer ID in token.");
            return int.Parse(claim);
        }
    }

    protected string CurrentUserId
        => User.FindFirstValue("userId")
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException("No user ID in token.");

    protected string? CurrentUserRole
        => User.FindFirstValue(ClaimTypes.Role);

    protected bool IsAdmin
        => CurrentUserRole == "admin";

    protected IActionResult Ok<T>(T data, string? message = null)
        => base.Ok(new ApiResponse<T>(data, message));

    protected IActionResult Created<T>(string location, T data)
        => base.Created(location, new ApiResponse<T>(data));

    protected IActionResult NoContent(string? message = null)
        => message == null ? base.NoContent() : base.Ok(new { message });
}

public record ApiResponse<T>(T Data, string? Message = null);

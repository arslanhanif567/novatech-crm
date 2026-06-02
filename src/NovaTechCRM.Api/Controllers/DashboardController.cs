using Microsoft.AspNetCore.Mvc;
using NovaTechCRM.Services;

namespace NovaTechCRM.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IReportingService _reporting;

    public DashboardController(IReportingService reporting)
    {
        _reporting = reporting;
    }

    /// <summary>
    /// Returns the main customer dashboard data.
    /// Fast for new customers, 40-60s for legacy customers with 18+ months of history.
    /// </summary>
    [HttpGet("{customerId}")]
    public async Task<IActionResult> GetDashboard(int customerId, CancellationToken ct)
    {
        var data = await _reporting.GetCustomerDashboardAsync(customerId, ct);

        if (data is null)
            return NotFound();

        return Ok(data);
    }
}

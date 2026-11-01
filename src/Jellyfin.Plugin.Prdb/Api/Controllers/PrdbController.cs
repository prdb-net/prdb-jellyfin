using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Prdb.Api.Controllers;

/// <summary>
/// The plugin's own endpoints for its configuration page. Administrators only.
/// </summary>
/// <param name="tester">Runs the connection test.</param>
[ApiController]
[Route("Prdb")]
[Authorize(Policy = "RequiresElevation")]
[Produces(MediaTypeNames.Application.Json)]
public class PrdbController(ConnectionTester tester) : ControllerBase
{
    /// <summary>
    /// Tests an API key and base URL against prdb without saving them.
    /// </summary>
    /// <param name="request">The values to test.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome.</returns>
    [HttpPost("TestConnection")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ConnectionTestResult>> TestConnection([FromBody] ConnectionTestRequest request, CancellationToken cancellationToken)
    {
        return await tester.TestAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

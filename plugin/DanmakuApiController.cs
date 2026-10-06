using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Danmaku;

[ApiController]
[Authorize]
[Route("JellyfinDanmaku/api/v2")]
public sealed class DanmakuApiController(DanmakuSource source) : ControllerBase
{
    [HttpGet("{**path}")]
    public async Task<IActionResult> Get(string path, CancellationToken cancellationToken)
    {
        var result = await source.GetAsync(path, Request.Query, cancellationToken).ConfigureAwait(false);
        Response.StatusCode = result.Status;
        Response.Headers.CacheControl = "no-store";
        if (result.RetryAfter is not null) Response.Headers.RetryAfter = result.RetryAfter;
        return File(result.Body, "application/json; charset=utf-8");
    }
}

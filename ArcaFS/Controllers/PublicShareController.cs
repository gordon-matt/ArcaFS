using ArcaFS.Services.Shares;
using ArcaFS.Services.Thumbnails;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ArcaFS.Controllers;

/// <summary>
/// Byte-moving endpoints for public share links.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="FilesController"/>. Everything here is
/// <c>[AllowAnonymous]</c> and reachable by anyone holding a token, so it must not share code
/// paths that assume a signed-in principal. Every request re-authorizes the token and rebases
/// the requested path onto the shared root through <see cref="ShareSession.ResolveWithinShare"/>,
/// rather than trusting a path the visitor supplied.
/// </remarks>
[ApiController]
[Route("api/public")]
[AllowAnonymous]
public sealed class PublicShareController(
    IShareService shareService,
    IFileSystemService fileSystemService,
    IThumbnailService thumbnailService,
    DownloadTicketService downloadTickets,
    ILogger<PublicShareController> logger) : ControllerBase
{
    [HttpGet("{token}/download")]
    public async Task<IActionResult> Download(
        string token,
        [FromQuery] string? path,
        [FromQuery] string? password,
        [FromQuery] bool inline = false,
        CancellationToken cancellationToken = default)
    {
        var session = await AuthorizeAsync(token, password, cancellationToken);

        if (session is null)
        {
            return NotFound();
        }

        string? resolvedPath = session.ResolveWithinShare(path);

        if (resolvedPath is null)
        {
            logger.LogWarning(
                "A request on share {Token} tried to reach {Path}, which is outside the share",
                token,
                path);

            return NotFound();
        }

        var result = await fileSystemService.OpenReadAsync(
            session.SourceId,
            resolvedPath,
            session.Principal,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return this.FromResult(result);
        }

        // Counted after authorization but before streaming, so an exhausted share can't be
        // squeezed for one more file by aborting the transfer.
        var recorded = await shareService.RecordDownloadAsync(token, cancellationToken);

        if (!recorded.IsSuccess)
        {
            result.Value.Stream.Dispose();

            return NotFound();
        }

        var download = result.Value;
        bool renderInline = inline && download.IsInlineByDefault && !session.DisableFileViewer;

        return File(
            download.Stream,
            download.MimeType,
            fileDownloadName: renderInline ? null : download.FileName,
            lastModified: download.ModifiedAt,
            entityTag: new EntityTagHeaderValue($"\"{download.ETag}\"", isWeak: true),
            enableRangeProcessing: true);
    }

    /// <summary>
    /// Streams a previously registered selection from a share as a zip. Mirrors the
    /// authenticated route: the selection arrives as a ticket so an arbitrarily large one works
    /// and the browser streams the archive to disk.
    /// </summary>
    [HttpGet("{token}/zip/{ticketToken}")]
    public async Task<IActionResult> Zip(
        string token,
        string ticketToken,
        [FromQuery] string? password,
        CancellationToken cancellationToken = default)
    {
        var session = await AuthorizeAsync(token, password, cancellationToken);

        if (session is null)
        {
            return NotFound();
        }

        // Scoped to this share's token, so a ticket minted for one link can't be redeemed
        // against another.
        var ticket = downloadTickets.Redeem(ticketToken, DownloadTicketService.ShareOwner(token));

        if (ticket is null || ticket.SourceId != session.SourceId)
        {
            return NotFound();
        }

        // Re-resolved rather than trusted: the ticket stores what the visitor asked for, and
        // the share is still the only thing that decides what that means.
        var resolvedPaths = new List<string>(ticket.Paths.Count);

        foreach (string requested in ticket.Paths)
        {
            string? resolved = session.ResolveWithinShare(requested);

            if (resolved is null)
            {
                return NotFound();
            }

            resolvedPaths.Add(resolved);
        }

        if (resolvedPaths.Count == 0)
        {
            return NotFound();
        }

        var recorded = await shareService.RecordDownloadAsync(token, cancellationToken);

        if (!recorded.IsSuccess)
        {
            return NotFound();
        }

        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileNameStar = SanitizeDownloadName(ticket.FileName),
        }.ToString();

        var result = await fileSystemService.WriteZipAsync(
            session.SourceId,
            resolvedPaths,
            Response.Body,
            session.Principal,
            cancellationToken);

        if (!result.IsSuccess && !Response.HasStarted)
        {
            return this.FromResult(result);
        }

        if (!result.IsSuccess)
        {
            logger.LogWarning("Zip stream for share {Token} failed after the response started", token);
            HttpContext.Abort();
        }

        return new EmptyResult();
    }

    [HttpGet("{token}/thumb")]
    public async Task<IActionResult> Thumbnail(
        string token,
        [FromQuery] string? path,
        [FromQuery] string? password,
        [FromQuery] int size = 256,
        CancellationToken cancellationToken = default)
    {
        var session = await AuthorizeAsync(token, password, cancellationToken);

        if (session is null || session.DisableThumbnails)
        {
            return NotFound();
        }

        string? resolvedPath = session.ResolveWithinShare(path);

        if (resolvedPath is null)
        {
            return NotFound();
        }

        var result = await thumbnailService.GetAsync(
            session.SourceId,
            resolvedPath,
            size,
            session.Principal,
            cancellationToken);

        if (!result.IsSuccess)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=604800, immutable";

        return File(result.Value.Bytes, result.Value.MimeType);
    }

    [HttpPost("{token}/upload")]
    [RequestSizeLimit(long.MaxValue)]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<IActionResult> Upload(
        string token,
        [FromForm] PublicUploadFormRequest form,
        CancellationToken cancellationToken = default)
    {
        var session = await AuthorizeAsync(token, form.Password, cancellationToken);

        if (session is null || form.File is null)
        {
            return NotFound();
        }

        string? resolvedPath = session.ResolveWithinShare(form.Path);

        if (resolvedPath is null)
        {
            return NotFound();
        }

        await using var stream = form.File.OpenReadStream();

        // The share's ceiling is enforced inside the service, so an upload to a read-only share
        // fails there rather than needing a mode check here that could drift out of step.
        var result = await fileSystemService.UploadAsync(
            new UploadRequest
            {
                SourceId = session.SourceId,
                Path = resolvedPath,
                FileName = Path.GetFileName(form.File.FileName),
                Content = stream,
                Overwrite = false,
            },
            session.Principal,
            cancellationToken);

        return this.FromResult(result);
    }

    private async Task<ShareSession?> AuthorizeAsync(
        string token,
        string? password,
        CancellationToken cancellationToken)
    {
        var result = await shareService.AuthorizeAsync(token, password, cancellationToken);

        return result.IsSuccess ? result.Value : null;
    }

    /// <summary>Strips characters that would let a filename break the header or a path.</summary>
    private static string SanitizeDownloadName(string fileName)
    {
        string cleaned = new(fileName
            .Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c is not ('"' or '\r' or '\n'))
            .ToArray());

        return string.IsNullOrWhiteSpace(cleaned) ? "share.zip" : cleaned;
    }
}

public sealed class PublicUploadFormRequest
{
    public IFormFile? File { get; set; }

    public string Path { get; set; } = Constants.RootPath;

    public string? Password { get; set; }
}

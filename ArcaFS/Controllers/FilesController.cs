using ArcaFS.Services.Thumbnails;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ArcaFS.Controllers;

/// <summary>
/// Byte-moving endpoints: downloads, uploads, zips and thumbnails.
/// </summary>
/// <remarks>
/// These are HTTP endpoints rather than Blazor component methods on purpose. Streaming a
/// multi-gigabyte download or upload through a SignalR circuit would buffer it, block the
/// circuit, and lose range requests, resumability and browser progress reporting along the way.
/// Permission checks happen inside the services, so nothing here re-implements them.
/// </remarks>
[ApiController]
[Route("api/files")]
[Authorize]
public sealed class FilesController(
    IFileSystemService fileSystemService,
    IThumbnailService thumbnailService,
    DownloadTicketService downloadTickets,
    IUserContextService userContext,
    ILogger<FilesController> logger) : ControllerBase
{
    /// <summary>
    /// Streams a single file. <paramref name="inline"/> chooses between rendering in the browser
    /// and saving to disk.
    /// </summary>
    [HttpGet("download")]
    public async Task<IActionResult> Download(
        [FromQuery] int sourceId,
        [FromQuery] string path,
        [FromQuery] bool inline = false,
        CancellationToken cancellationToken = default)
    {
        var result = await fileSystemService.OpenReadAsync(sourceId, path, null, cancellationToken);

        if (!result.IsSuccess)
        {
            return this.FromResult(result);
        }

        return StreamFile(result.Value, inline);
    }

    /// <summary>
    /// Streams a previously registered selection as a zip, built on the fly.
    /// </summary>
    /// <remarks>
    /// The selection arrives as a ticket rather than in the URL so that an arbitrarily large
    /// selection works and the browser can stream the result straight to disk. No
    /// <c>Content-Length</c> is possible, because the archive is produced as it is sent rather
    /// than staged first — the right trade, since a user zipping 40 GB of photos should not have
    /// to wait for, or find room for, a temporary copy.
    /// </remarks>
    [HttpGet("zip/{token}")]
    public async Task<IActionResult> Zip(
        string token,
        CancellationToken cancellationToken = default)
    {
        var ticket = downloadTickets.Redeem(
            token, DownloadTicketService.UserOwner(userContext.GetCurrentUserId()));

        if (ticket is null)
        {
            return NotFound();
        }

        Response.ContentType = "application/zip";
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileNameStar = SanitizeDownloadName(ticket.FileName),
        }.ToString();

        // Written before the first byte, because once the body starts there is no way to send a
        // status code if something fails partway through.
        var result = await fileSystemService.WriteZipAsync(
            ticket.SourceId,
            ticket.Paths,
            Response.Body,
            principal: null,
            cancellationToken);

        if (!result.IsSuccess && !Response.HasStarted)
        {
            return this.FromResult(result);
        }

        if (!result.IsSuccess)
        {
            // Aborting mid-stream is the only remaining way to signal failure; the browser
            // surfaces it as an interrupted download rather than a silently truncated zip.
            logger.LogWarning("Zip stream failed after the response had started; aborting");
            HttpContext.Abort();
        }

        return new EmptyResult();
    }

    /// <summary>
    /// Accepts one file. Uploads arrive one request per file from <c>uploader.js</c>.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(long.MaxValue)]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<IActionResult> Upload(
        [FromForm] UploadFormRequest form,
        CancellationToken cancellationToken = default)
    {
        if (form.File is null || form.File.Length < 0)
        {
            return BadRequest(new ProblemDetails { Detail = "No file was supplied." });
        }

        // A dropped folder arrives as files carrying their relative path, so the destination is
        // the target directory plus that path's directory part.
        string destination = CombineRelativeDirectory(form.Path, form.RelativePath);

        await using var stream = form.File.OpenReadStream();

        var result = await fileSystemService.UploadAsync(
            new UploadRequest
            {
                SourceId = form.SourceId,
                Path = destination,
                FileName = Path.GetFileName(form.File.FileName),
                Content = stream,
                Overwrite = form.Overwrite,
                ChunkOffset = form.ChunkOffset,
                TotalSize = form.TotalSize,
                UploadSession = form.UploadSession,
            },
            principal: null,
            cancellationToken);

        return this.FromResult(result);
    }

    /// <summary>
    /// Returns a cached thumbnail, or 404 when the file has none, which the UI reads as
    /// "show the file-type icon instead".
    /// </summary>
    [HttpGet("thumb")]
    public async Task<IActionResult> Thumbnail(
        [FromQuery] int sourceId,
        [FromQuery] string path,
        [FromQuery] int size = 256,
        CancellationToken cancellationToken = default)
    {
        var result = await thumbnailService.GetAsync(sourceId, path, size, null, cancellationToken);

        if (!result.IsSuccess)
        {
            return this.FromResult(result);
        }

        var thumbnail = result.Value;

        // The UI appends the file's modification ticks as a "v" parameter, so a given URL always
        // refers to one version of one file. That makes a long private cache safe, and saves a
        // round trip per tile when scrolling a large gallery.
        Response.Headers.CacheControl = "private, max-age=604800, immutable";

        return File(thumbnail.Bytes, thumbnail.MimeType);
    }

    private IActionResult StreamFile(FileDownloadDto download, bool inline)
    {
        bool renderInline = inline && download.IsInlineByDefault;

        // enableRangeProcessing lets the browser seek within audio and video, and lets an
        // interrupted download resume rather than start over.
        return File(
            download.Stream,
            download.MimeType,
            fileDownloadName: renderInline ? null : download.FileName,
            lastModified: download.ModifiedAt,
            entityTag: HttpEntityTags.Weak(download.ETag),
            enableRangeProcessing: true);
    }

    /// <summary>
    /// Joins the target directory with the directory part of a dropped file's relative path.
    /// </summary>
    private static string CombineRelativeDirectory(string basePath, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return VirtualPath.Normalize(basePath);
        }

        string relativeDirectory = relativePath.Replace('\\', '/');
        int lastSlash = relativeDirectory.LastIndexOf('/');

        if (lastSlash <= 0)
        {
            return VirtualPath.Normalize(basePath);
        }

        // Normalize collapses any "..", so a crafted relativePath can't climb out of the target.
        return VirtualPath.Combine(
            VirtualPath.Normalize(basePath),
            VirtualPath.GetSegments(VirtualPath.Normalize(relativeDirectory[..lastSlash])));
    }

    /// <summary>Strips characters that would let a filename break the header or a path.</summary>
    private static string SanitizeDownloadName(string fileName)
    {
        string cleaned = new(fileName
            .Where(c => !Path.GetInvalidFileNameChars().Contains(c) && c is not ('"' or '\r' or '\n'))
            .ToArray());

        return string.IsNullOrWhiteSpace(cleaned) ? "archive.zip" : cleaned;
    }
}

public sealed class UploadFormRequest
{
    public IFormFile? File { get; set; }

    public int SourceId { get; set; }

    public string Path { get; set; } = Constants.RootPath;

    /// <summary>Path within a dropped folder, so its structure survives the upload.</summary>
    public string? RelativePath { get; set; }

    public bool Overwrite { get; set; }

    public long ChunkOffset { get; set; }

    public long? TotalSize { get; set; }

    public string? UploadSession { get; set; }
}

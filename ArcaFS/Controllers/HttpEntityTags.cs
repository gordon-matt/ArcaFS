using Microsoft.Net.Http.Headers;

namespace ArcaFS.Controllers;

/// <summary>
/// Builds an HTTP entity tag that <see cref="EntityTagHeaderValue"/> will accept.
/// </summary>
internal static class HttpEntityTags
{
    /// <summary>
    /// Quotes an opaque validator. Strips a pre-existing <c>W/</c> prefix or quotes so a value
    /// that already looks like a header never becomes <c>"W/"abc""</c>.
    /// </summary>
    public static EntityTagHeaderValue Weak(string? etag)
    {
        string opaque = (etag ?? string.Empty).Trim();

        if (opaque.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
        {
            opaque = opaque[2..].Trim();
        }

        opaque = opaque.Trim('"');

        if (string.IsNullOrEmpty(opaque))
        {
            opaque = "0";
        }

        return new EntityTagHeaderValue($"\"{opaque}\"", isWeak: true);
    }
}

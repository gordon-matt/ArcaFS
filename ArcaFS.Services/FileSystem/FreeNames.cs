namespace ArcaFS.Services.FileSystem;

/// <summary>
/// Picks a name that does not collide, the way a desktop file manager writes "notes (2).txt".
/// </summary>
public static class FreeNames
{
    public static string Find(string directory, string name, bool isDirectory)
    {
        string stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        string extension = isDirectory ? string.Empty : Path.GetExtension(name);

        for (int i = 2; i < 10_000; i++)
        {
            string candidate = $"{stem} ({i}){extension}";
            string full = Path.Combine(directory, candidate);

            if (!File.Exists(full) && !Directory.Exists(full))
            {
                return candidate;
            }
        }

        return $"{stem} ({Guid.NewGuid():N}){extension}";
    }

    /// <summary>
    /// Same numbering, against a set of names the caller already knows about. Used before an
    /// upload, when the bytes are not on disk yet and the reserved name has to stay unique
    /// inside the batch.
    /// </summary>
    public static string Reserve(string name, bool isDirectory, HashSet<string> taken)
    {
        string stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        string extension = isDirectory ? string.Empty : Path.GetExtension(name);

        for (int i = 2; i < 10_000; i++)
        {
            string candidate = $"{stem} ({i}){extension}";
            if (taken.Add(candidate))
            {
                return candidate;
            }
        }

        string fallback = $"{stem} ({Guid.NewGuid():N}){extension}";
        taken.Add(fallback);
        return fallback;
    }
}

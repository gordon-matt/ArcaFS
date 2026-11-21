namespace ArcaFS.Services.Thumbnails;

/// <summary>Whether a video still can be produced right now. False when ffmpeg was not found.</summary>
public interface IVideoThumbnailGate
{
    bool IsAvailable { get; }
}

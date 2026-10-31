using Godot;

namespace ACWorldEdit;

/// <summary>
/// Model thumbnails by display ID. A request returns the thumbnail when it is already known (cached in
/// memory or on disk) and otherwise null, calling <c>ready</c> later on the main thread once it is
/// rendered; a display without a model never calls back.
/// </summary>
public interface IThumbnailSource
{
    Texture2D? Creature(uint displayId, Action<Texture2D> ready);

    Texture2D? GameObject(uint displayId, Action<Texture2D> ready);

    /// <summary>Drop requests that have not started rendering, for example when a new search replaces the results.</summary>
    void CancelWaiting();
}

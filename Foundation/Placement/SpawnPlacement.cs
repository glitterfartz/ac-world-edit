namespace ACWorldEdit.Foundation;

/// <summary>Where and how a spawn placed by a click in the world ends up.</summary>
public static class SpawnPlacement
{
    /// <summary>
    /// The heading (counter-clockwise from server +X, in [0, 2π)) that turns a spawn at (<paramref name="x"/>, <paramref name="y"/>)
    /// to face (<paramref name="toX"/>, <paramref name="toY"/>). A spawn dropped on the ground faces the camera that placed it.
    /// </summary>
    public static double FacingHeading(double x, double y, double toX, double toY) =>
        toX == x && toY == y ? 0 : WorldCoordinates.NormalizeHeading(Math.Atan2(toY - y, toX - x));
}

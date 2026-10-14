namespace Stickies.Revit
{
    /// <summary>
    /// True while the user picks the spot for a new issue in a view. Markers and cards then let every click through,
    /// so clicking on top of an existing marker still picks that spot.
    /// </summary>
    internal static class PickState
    {
        private static volatile bool _active;

        public static bool Active
        {
            get => _active;
            set => _active = value;
        }
    }
}

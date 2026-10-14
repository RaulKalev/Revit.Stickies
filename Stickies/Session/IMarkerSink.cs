using System;
using System.Collections.Generic;
using Stickies.Core.Overlay;

namespace Stickies.Session
{
    /// <summary>
    /// Where the pane sends the markers to draw (the issues that pass the user's filter) and hears about clicks on them.
    /// The Revit implementation draws them on views; the harness supplies a fake.
    /// </summary>
    public interface IMarkerSink
    {
        /// <summary>Replaces the markers to draw. Called on the UI thread.</summary>
        void Publish(IReadOnlyList<MarkerInfo> markers);

        /// <summary>A marker was clicked in a view (issue id). Raised on the UI thread.</summary>
        event Action<string> MarkerClicked;
    }
}

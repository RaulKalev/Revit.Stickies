using System;

namespace Stickies.Overlay
{
    /// <summary>
    /// What is drawn on views: the markers, and optionally sticky-note cards next to them. Shared by the ribbon toggle,
    /// the pane and the renderers. The session loads and saves both per project in the user's local settings.
    /// </summary>
    public sealed class OverlayState
    {
        private bool _isVisible = true;
        private bool _showCards;

        /// <summary>Markers on views (cards need markers on as well).</summary>
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible == value) return;
                _isVisible = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Sticky-note cards next to the markers, in the view each issue was created in.</summary>
        public bool ShowCards
        {
            get => _showCards;
            set
            {
                if (_showCards == value) return;
                _showCards = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler Changed;

        public void Toggle() => IsVisible = !IsVisible;

        public void ToggleCards() => ShowCards = !ShowCards;
    }
}

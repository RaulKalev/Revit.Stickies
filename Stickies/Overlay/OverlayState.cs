using System;

namespace Stickies.Overlay
{
    /// <summary>
    /// Whether review markers are drawn on views. Shared by the ribbon toggle and the pane; the marker renderer reads it.
    /// The session loads and saves it per project in the user's local settings.
    /// </summary>
    public sealed class OverlayState
    {
        private bool _isVisible = true;

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

        public event EventHandler Changed;

        public void Toggle() => IsVisible = !IsVisible;
    }
}

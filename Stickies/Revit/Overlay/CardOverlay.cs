using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Stickies.Core.Overlay;
using Stickies.Infrastructure;
using Stickies.UI;

namespace Stickies.Revit.Overlay
{
    /// <summary>A card to show: the issue and where its marker is on screen (physical pixels).</summary>
    internal sealed class CardItem
    {
        public CardItem(MarkerInfo info, double x, double y)
        {
            Info = info;
            X = x;
            Y = y;
        }

        public MarkerInfo Info { get; }
        public double X { get; }
        public double Y { get; }
    }

    /// <summary>The active view's window on screen (physical pixels) and the cards to show over it.</summary>
    internal sealed class CardFrame
    {
        public CardFrame(int left, int top, int width, int height, IReadOnlyList<CardItem> cards)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            Cards = cards;
        }

        public int Left { get; }
        public int Top { get; }
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<CardItem> Cards { get; }
    }

    /// <summary>
    /// Shows sticky-note cards next to markers. DirectContext3D cannot draw text, so the cards live in a transparent
    /// window laid exactly over the active Revit view: it never takes focus, lets every click through to Revit, and is
    /// clipped to the view. It follows the markers on every frame (their screen positions come from the marker drawing),
    /// so cards move with pan and zoom. A window on screen is never part of a print or an export.
    /// </summary>
    internal sealed class CardOverlay : IDisposable
    {
        private readonly Func<CardFrame> _frame;
        private readonly IntPtr _owner;
        private readonly Dictionary<string, KeyValuePair<MarkerInfo, FrameworkElement>> _cards =
            new Dictionary<string, KeyValuePair<MarkerInfo, FrameworkElement>>();
        private Window _window;
        private Canvas _canvas;
        private bool _enabled;
        private volatile List<KeyValuePair<string, Rect>> _hitRects = new List<KeyValuePair<string, Rect>>();

        public CardOverlay(Func<CardFrame> frame, IntPtr owner)
        {
            _frame = frame;
            _owner = owner;
        }

        /// <summary>On while markers and cards are both switched on.</summary>
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                if (value) CompositionTarget.Rendering += OnRendering;
                else
                {
                    CompositionTarget.Rendering -= OnRendering;
                    Hide();
                }
            }
        }

        /// <summary>The issue whose card is under a screen point (physical pixels), or null.</summary>
        public string HitTest(int x, int y)
        {
            if (!_enabled) return null;
            var hits = _hitRects;
            for (var i = hits.Count - 1; i >= 0; i--) // topmost first
                if (hits[i].Value.Contains(x, y)) return hits[i].Key;
            return null;
        }

        private void OnRendering(object sender, EventArgs e)
        {
            try
            {
                var frame = _frame();
                if (frame == null || frame.Cards.Count == 0 || frame.Width <= 0 || frame.Height <= 0 || IsIconic(_owner))
                {
                    Hide();
                    return;
                }
                Layout(frame);
            }
            catch (Exception ex)
            {
                Enabled = false;
                AppLog.Error("Showing sticky-note cards failed; cards are off until switched on again", ex);
            }
        }

        private void Layout(CardFrame frame)
        {
            EnsureWindow();
            var scale = DpiScale();

            // The window covers the view exactly, so cards outside the view are cut off at its edge.
            var left = frame.Left / scale;
            var top = frame.Top / scale;
            var width = frame.Width / scale;
            var height = frame.Height / scale;
            if (Math.Abs(_window.Left - left) > 0.5) _window.Left = left;
            if (Math.Abs(_window.Top - top) > 0.5) _window.Top = top;
            if (Math.Abs(_window.Width - width) > 0.5) _window.Width = width;
            if (Math.Abs(_window.Height - height) > 0.5) _window.Height = height;
            if (!_window.IsVisible) _window.Show();

            var seen = new HashSet<string>();
            var hits = new List<KeyValuePair<string, Rect>>();
            foreach (var item in frame.Cards)
            {
                var id = item.Info.IssueId;
                seen.Add(id);
                if (!_cards.TryGetValue(id, out var entry) || !ReferenceEquals(entry.Key, item.Info))
                {
                    if (entry.Value != null) _canvas.Children.Remove(entry.Value);
                    var card = StickyCard.Build(item.Info);
                    card.Measure(new Size(StickyCard.MaxCardWidth, double.PositiveInfinity));
                    entry = new KeyValuePair<MarkerInfo, FrameworkElement>(item.Info, card);
                    _cards[id] = entry;
                    _canvas.Children.Add(card);
                    if (item.Info.IsSelected) Panel.SetZIndex(card, 1); // the open issue's card on top
                }

                var element = entry.Value;
                var size = element.DesiredSize;
                var at = StickyCard.Place((item.X - frame.Left) / scale, (item.Y - frame.Top) / scale, size, width, height);
                var x = at.X;
                var y = at.Y;
                Canvas.SetLeft(element, x);
                Canvas.SetTop(element, y);
                hits.Add(new KeyValuePair<string, Rect>(id, new Rect(frame.Left + x * scale, frame.Top + y * scale, size.Width * scale, size.Height * scale)));
            }

            foreach (var gone in new List<string>(_cards.Keys))
            {
                if (seen.Contains(gone)) continue;
                _canvas.Children.Remove(_cards[gone].Value);
                _cards.Remove(gone);
            }
            _hitRects = hits;
        }

        private void EnsureWindow()
        {
            if (_window != null) return;
            _canvas = new Canvas { ClipToBounds = true, IsHitTestVisible = false };
            _window = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Content = _canvas,
                Title = "Stickies cards"
            };
            TextOptions.SetTextFormattingMode(_window, TextFormattingMode.Display);
            if (_owner != IntPtr.Zero) new WindowInteropHelper(_window) { Owner = _owner };
            _window.SourceInitialized += (s, e) =>
            {
                // Click-through, never activated, not in Alt+Tab.
                var handle = new WindowInteropHelper(_window).Handle;
                var style = GetWindowLong(handle, GWL_EXSTYLE);
                SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            };
        }

        /// <summary>Physical pixels per DIP for the monitor Revit is on.</summary>
        private double DpiScale()
        {
            try
            {
                var dpi = _owner != IntPtr.Zero ? GetDpiForWindow(_owner) : 0;
                return dpi > 0 ? dpi / 96.0 : 1.0;
            }
            catch (EntryPointNotFoundException)
            {
                return 1.0; // before Windows 10 1607
            }
        }

        private void Hide()
        {
            if (_window != null && _window.IsVisible) _window.Hide();
            _hitRects = new List<KeyValuePair<string, Rect>>();
        }

        public void Dispose()
        {
            Enabled = false;
            _window?.Close();
            _window = null;
            _cards.Clear();
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    }
}

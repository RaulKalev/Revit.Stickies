using System;
using System.Windows.Media;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using ricaun.Revit.UI;
using Stickies.Infrastructure;
using Stickies.Overlay;
using Stickies.Revit;
using Stickies.Session;
using Stickies.Shell;
using Stickies.UI;

namespace Stickies
{
    /// <summary>
    /// Add-in entry point. Runs on Revit startup and again on every ricaun AppLoader reload (as a new copy of the DLL).
    /// Everything here is reloadable except the pane registration in <see cref="PaneShell"/>.
    /// </summary>
    [AppLoader]
    public class App : IExternalApplication
    {
        public const string TabName = "RK Tools";
        public const string PanelName = "Stickies";

        private const string DarkIcon = "pack://application:,,,/Stickies;component/Assets/Dark%20-%20Stickies.tiff";
        private const string LightIcon = "pack://application:,,,/Stickies;component/Assets/Light%20-%20Stickies.tiff";

        /// <summary>Marker visibility for this session, shared by the ribbon toggle, the pane and (later) the renderer.</summary>
        internal static OverlayState Overlay { get; } = new OverlayState();

        /// <summary>This copy of the add-in (commands reach the panel through it).</summary>
        internal static App Instance { get; private set; }

        private RibbonPanel _ribbonPanel;
        private PushButton _paneButton;
        private PushButton _overlayButton;
        private MarkupPane _pane;
        private MarkupSession _session;
        private DocumentTracker _tracker;
        private RevitRequests _requests;
        private Revit.Overlay.MarkerOverlay _markers;
        private PaneWindow _window;

        public Result OnStartup(UIControlledApplication application)
        {
            Instance = this;
            try { application.CreateRibbonTab(TabName); } catch { /* tab already exists (other RK Tools plugins) */ }
            _ribbonPanel = application.CreateOrSelectPanel(TabName, PanelName);

            _paneButton = _ribbonPanel.CreatePushButton<TogglePaneCommand>("Stickies");
            _paneButton.SetToolTip("Show or hide the Stickies panel.");
            _overlayButton = _ribbonPanel.CreatePushButton<ToggleOverlayCommand>("Markers");
            UpdateRibbon();

            PaneShell.EnsureRegistered(application);
            _session = new MarkupSession(System.Windows.Threading.Dispatcher.CurrentDispatcher, Overlay);
            _requests = new RevitRequests(); // ExternalEvent: must be created here, in API context
            _markers = new Revit.Overlay.MarkerOverlay(application, _requests, Overlay); // registers the DirectContext3D server
            _pane = new MarkupPane(Overlay, _session, new RevitHost(_requests), _markers);
            _markers.MarkerClicked += OnMarkerClicked;
            if (!PaneShell.Attach(_pane)) AppLog.Info("The docked Stickies pane is not available in this session; the panel opens in a window.");

            Overlay.Changed += OnOverlayChanged;
            _tracker = new DocumentTracker(application, model => _session.SetModel(model));
            // Follow Revit's light/dark UI theme while Revit runs, not only at startup.
            application.ThemeChanged += OnThemeChanged;

            AppLog.Info("Stickies " + typeof(App).Assembly.GetName().Version + " loaded.");
            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            application.ThemeChanged -= OnThemeChanged;
            Overlay.Changed -= OnOverlayChanged;
            CloseWindow(reattach: false);
            _tracker?.Dispose();
            _tracker = null;
            _session?.Dispose();
            _session = null;
            if (_markers != null) _markers.MarkerClicked -= OnMarkerClicked;
            _markers?.Dispose();
            _markers = null;
            _requests?.Dispose();
            _requests = null;
            PaneShell.Detach(_pane);
            _pane = null;
            _ribbonPanel?.Remove();
            if (Instance == this) Instance = null;
            return Result.Succeeded;
        }

        // ------------------------------------------------------------------ panel: docked pane or window

        /// <summary>
        /// Shows or hides the panel: in the docked pane when Revit has it, otherwise in a floating window (Revit creates
        /// docked panes only at startup, so after an AppLoader load the window is used until the next restart).
        /// </summary>
        internal void TogglePanel(UIApplication app)
        {
            if (PaneShell.TryGetPane(app, out var pane))
            {
                CloseWindow(reattach: true); // the panel goes back into the pane
                if (pane.IsShown()) pane.Hide();
                else pane.Show();
                return;
            }

            if (_window != null)
            {
                CloseWindow(reattach: true);
                return;
            }
            OpenWindow(app, activate: true);
        }

        /// <summary>Makes sure the panel is visible (a click on a marker or card opens its issue there).</summary>
        internal void ShowPanel(UIApplication app)
        {
            if (_window != null) return; // already open as a window
            if (PaneShell.TryGetPane(app, out var pane))
            {
                if (!pane.IsShown()) pane.Show();
                return;
            }
            OpenWindow(app, activate: false);
        }

        private void OpenWindow(UIApplication app, bool activate)
        {
            PaneShell.Detach(_pane);
            _window = new PaneWindow(_pane, app.MainWindowHandle);
            _window.Closed += (s, e) =>
            {
                var closed = (PaneWindow)s;
                if (closed != _window) return;
                _window = null;
                PaneShell.Attach(closed.Release());
            };
            _window.ShowActivated = activate; // from a marker click, Revit's view keeps the focus
            _window.Show(); // modeless
        }

        private void OnMarkerClicked(string issueId) => _requests?.Post(ShowPanel);

        private void CloseWindow(bool reattach)
        {
            var window = _window;
            if (window == null) return;
            _window = null; // the Closed handler then leaves the panel alone
            var panel = window.Release();
            window.Close();
            if (reattach && panel != null) PaneShell.Attach(panel);
        }

        private void OnThemeChanged(object sender, ThemeChangedEventArgs e) => UpdateRibbon();

        private void OnOverlayChanged(object sender, EventArgs e) => UpdateRibbon();

        /// <summary>Icons follow Revit's theme (light icon on the dark theme); the Markers button shows its state.</summary>
        private void UpdateRibbon()
        {
            try
            {
                var dark = UIThemeManager.CurrentTheme == UITheme.Dark;
                var icon = dark ? DarkIcon : LightIcon;
                _paneButton?.SetLargeImage(icon).SetImage(icon);

                if (_overlayButton == null) return;
                var on = Overlay.IsVisible;
                var colour = dark ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x3A, 0x3A, 0x3C);
                _overlayButton
                    .SetLargeImage(RibbonIcons.Eye(on, 32, colour))
                    .SetImage(RibbonIcons.Eye(on, 16, colour))
                    .SetText(on ? "Markers\nOn" : "Markers\nOff")
                    .SetToolTip(on ? "Review markers are shown on views. Click to hide them." : "Review markers are hidden. Click to show them on views.");
            }
            catch (Exception ex)
            {
                AppLog.Error("Updating the ribbon failed", ex);
            }
        }
    }
}

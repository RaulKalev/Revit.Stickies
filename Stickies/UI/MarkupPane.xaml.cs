using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using Stickies.Overlay;
using Stickies.Session;
using Stickies.UI.ViewModels;

namespace Stickies.UI
{
    /// <summary>
    /// The pane's UI, placed into the shell's host on every load (see Shell/PaneShell). Everything in here is reloadable.
    /// Bound to <see cref="PaneViewModel"/>; code-behind handles appearance, list gestures, the list/issue split and the
    /// folder picker only.
    /// </summary>
    public partial class MarkupPane : UserControl
    {
        private readonly ThemeManager _theme;
        private readonly OverlayState _overlay;

        public MarkupPane(OverlayState overlay, MarkupSession session, IRevitHost host, IMarkerSink markers = null, string me = null)
        {
            _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
            ViewModel = new PaneViewModel(session, host, me ?? Environment.UserName, markers);
            InitializeComponent();
            DataContext = ViewModel;
            _theme = new ThemeManager(this);
            _theme.ApplyTheme();
            _theme.ThemeChanged += (s, e) => UpdateThemeButton();
            UpdateThemeButton();

            UpdateMarkersButton();
            // Subscribe only while shown, so a pane left behind by a hot reload does not stay subscribed.
            Loaded += (s, e) => { _overlay.Changed -= OnOverlayChanged; _overlay.Changed += OnOverlayChanged; UpdateMarkersButton(); };
            Unloaded += (s, e) => _overlay.Changed -= OnOverlayChanged;

            ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        public PaneViewModel ViewModel { get; }

        public MarkupSession Session => ViewModel.Session;

        public ThemeManager Theme => _theme;

        /// <summary>Asks for a folder; returns null when cancelled. Replaceable so the harness can answer.</summary>
        public Func<string> PickFolder { get; set; } = ShowFolderDialog;

        // ------------------------------------------------------------------ list and issue

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PaneViewModel.Editor)) return;

            // The issue takes the lower part of the pane; the splitter between them is the user's to move.
            var hasEditor = ViewModel.HasEditor;
            ListRow.Height = new GridLength(1, GridUnitType.Star);
            EditorRow.Height = hasEditor ? new GridLength(1.4, GridUnitType.Star) : new GridLength(0);
            EditorRow.MinHeight = hasEditor ? 160 : 0;

            if (ViewModel.Editor != null && ViewModel.Editor.IsNew)
                Dispatcher.BeginInvoke(new Action(() => { TitleBox.Focus(); Keyboard.Focus(TitleBox); }), DispatcherPriority.Input);
        }

        private void IssueList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            var item = ItemsControl.ContainerFromElement(IssueList, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item?.DataContext is IssueRowViewModel row)
            {
                ViewModel.OpenView(row);
                e.Handled = true;
            }
        }

        private void IssueList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && ViewModel.Selected != null)
            {
                ViewModel.OpenView(ViewModel.Selected);
                e.Handled = true;
            }
        }

        private void PeopleList_Pick(object sender, MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(PeopleList, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item?.DataContext is string person) ViewModel.Editor?.AssignTo(person);
            PeopleButton.IsChecked = false;
        }

        // ------------------------------------------------------------------ header

        private void OnOverlayChanged(object sender, EventArgs e) => UpdateMarkersButton();

        private void UpdateMarkersButton()
        {
            var on = _overlay.IsVisible;
            MarkersButton.IsChecked = on;
            MarkersIcon.Kind = on ? PackIconKind.EyeOutline : PackIconKind.EyeOffOutline;
            MarkersButton.ToolTip = on ? "Markers are shown on views (click to hide)" : "Markers are hidden (click to show on views)";
        }

        private void UpdateThemeButton()
        {
            ThemeIcon.Kind = _theme.IsDarkMode ? PackIconKind.WeatherNight : PackIconKind.WhiteBalanceSunny;
            ThemeButton.ToolTip = _theme.IsDarkMode ? "Dark appearance (click for light)" : "Light appearance (click for dark)";
        }

        private void ToggleMarkers_Click(object sender, RoutedEventArgs e) => _overlay.Toggle();

        private void ToggleTheme_Click(object sender, RoutedEventArgs e) => _theme.ToggleTheme();

        // ------------------------------------------------------------------ folder

        private void CreateNextToModel_Click(object sender, RoutedEventArgs e) => Session.CreateNextToModel();

        private void ChooseFolder_Click(object sender, RoutedEventArgs e)
        {
            var picked = PickFolder?.Invoke();
            if (!string.IsNullOrWhiteSpace(picked)) Session.UseFolder(picked);
        }

        private static string ShowFolderDialog()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choose the project's shared folder. Issues go into its _markup folder.",
                ShowNewFolderButton = true
            })
            {
                var owner = new RevitWindow(Process.GetCurrentProcess().MainWindowHandle);
                return dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
            }
        }

        private sealed class RevitWindow : System.Windows.Forms.IWin32Window
        {
            public RevitWindow(IntPtr handle) => Handle = handle;
            public IntPtr Handle { get; }
        }
    }
}

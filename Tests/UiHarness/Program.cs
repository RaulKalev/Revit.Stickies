using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using Stickies.Core.Models;
using Stickies.Core.Store;
using Stickies.Overlay;
using Stickies.Session;
using Stickies.UI;
using Stickies.UI.ViewModels;

namespace Stickies.UiHarness
{
    /// <summary>
    /// Runs the real Stickies pane, view models and session without Revit (a fake host answers for Revit) against a
    /// temporary "Dropbox" folder, scripts the workflows and writes screenshots + harness-log.txt to out/.
    /// Exit code = number of failed checks.
    /// </summary>
    public static class Program
    {
        private const string Me = "raul";
        private static readonly StringBuilder Log = new StringBuilder();
        private static int _failures;
        private static string _out;

        [STAThread]
        public static int Main(string[] args)
        {
            _out = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "out");
            Directory.CreateDirectory(_out);
            ThemeManager.PrefsPath = Path.Combine(_out, "ui-prefs.json"); // never touch the real user preferences
            if (File.Exists(ThemeManager.PrefsPath)) File.Delete(ThemeManager.PrefsPath);

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Startup += (s, e) =>
            {
                try { Run(); }
                catch (Exception ex) { Check(false, "harness crashed: " + ex); }
                File.WriteAllText(Path.Combine(_out, "harness-log.txt"), Log.ToString());
                app.Shutdown(_failures);
            };
            app.Run();
            return _failures;
        }

        private static void Run()
        {
            // STICKIES_HARNESS_ROOT: an empty folder to work in (README screenshots use a mapped drive so the footer path reads well).
            var workRoot = Environment.GetEnvironmentVariable("STICKIES_HARNESS_ROOT");
            var work = string.IsNullOrWhiteSpace(workRoot) ? Path.Combine(Path.GetTempPath(), "StickiesHarness", Guid.NewGuid().ToString("N")) : workRoot;
            var dropbox = Path.Combine(work, "Dropbox", "P123 School");
            Directory.CreateDirectory(dropbox);
            var settingsStore = new LocalSettingsStore(Path.Combine(work, "AppData", "projects"));
            var dispatcher = Dispatcher.CurrentDispatcher;
            var host = new FakeRevitHost(dispatcher);
            var markers = new FakeMarkerSink();
            var defaultCapture = host.NextCapture;

            // The shell's host: a plain ContentControl, as registered with Revit.
            var shellHost = new ContentControl();
            var window = new Window
            {
                Content = shellHost, Width = 380, Height = 820, Left = 20, Top = 20,
                ShowActivated = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize
            };
            window.Show();

            var overlay = new OverlayState();
            var session = new MarkupSession(dispatcher, overlay, settingsStore);
            var pane = new MarkupPane(overlay, session, host, markers, Me);
            shellHost.Content = pane;
            var vm = pane.ViewModel;
            Pump();
            Check(session.IsNoModel, "starts with no model");
            Capture(pane, "01_no_model_dark.png");

            // ================================================================ Phase 2: setup
            var model = new ActiveModel
            {
                Title = "P123_EN", ModelGuid = "11111111-2222-3333-4444-555555555555-0000abcd",
                ModelPath = Path.Combine(dropbox, "P123_EN.rvt"), DocumentKey = "doc-1"
            };
            session.SetModel(model);
            Pump();
            Check(session.NeedsFolder && session.CanCreateNextToModel, "no _markup yet: asks where to keep issues");
            Capture(pane, "02_needs_folder_dark.png");
            Click(pane, "Create next to the model");
            Check(WaitFor(() => session.IsConnected), "create next to the model connects");
            var root = Path.Combine(dropbox, "_markup");
            var colleague = new IssueStore(new MarkupFolder(root));
            Check(colleague.ReadProject()?.ModelGuid == model.ModelGuid, "project.json names the model");
            Check(vm.ShowNoIssues, "empty project shows 'No issues yet'");
            Capture(pane, "03_no_issues_dark.png");

            // ================================================================ add an issue
            vm.AddIssueCommand.Execute(null);
            Check(vm.IsCapturing && vm.Feedback != null, "add: asks for the spot in the view");
            Check(WaitFor(() => vm.Editor != null && vm.Editor.IsNew), "add: new issue form opens after the capture");
            Check(!vm.Editor.SaveCommand.CanExecute(null), "add: cannot save without a title");
            Capture(pane, "04_new_issue_dark.png");
            vm.Editor.Title = "Camera too close to door";
            vm.Editor.Body = "Move VVS-03 at least 500 mm from the frame";
            vm.Editor.IsHigh = true;
            vm.Editor.AssignTo("joonas");
            vm.Editor.SaveCommand.Execute(null);
            Check(WaitFor(() => vm.Editor != null && !vm.Editor.IsNew), "add: saved");
            var first = vm.Editor.Id;
            var stored = colleague.Load(first);
            Check(stored != null && stored.Title == "Camera too close to door" && stored.Priority == IssuePriority.High && stored.Assignee == "joonas" && stored.Author == Me,
                "add: file written with title, priority, assignee and author");
            Check(stored?.View?.Name == "EN_2_korrus_VVS" && stored.Location?.ElementUniqueId == "elem-camera" && stored.View.ZoomCorners != null,
                "add: captured view, zoom and element stored");
            Check(vm.Selected?.Id == first, "add: new issue is selected in the list");
            Check(stored?.Number == 1, "add: first issue gets number 1");
            Check(markers.Last.Count == 1 && markers.Last[0].IssueId == first && markers.Last[0].Number == 1 && markers.Last[0].IsSelected, "markers: new issue drawn, highlighted while open");

            host.NextCapture = () => new CaptureResult(); // Esc
            vm.Selected = null;
            vm.AddIssueCommand.Execute(null);
            Check(WaitFor(() => !vm.IsCapturing) && vm.Editor == null && vm.Feedback == null, "add: Esc cancels quietly");
            host.NextCapture = () => new CaptureResult { Error = "Open a plan, section, elevation, drafting or 3D view to add an issue there." };
            vm.AddIssueCommand.Execute(null);
            Check(WaitFor(() => !vm.IsCapturing) && vm.Editor == null && vm.Feedback != null && vm.Feedback.StartsWith("Open a plan"), "add: unsupported view explains why");

            // ================================================================ colleagues' issues
            var now = DateTimeOffset.Now;
            colleague.Save(Issue("Missing card reader", "joonas", IssueStatus.InProgress, IssuePriority.Normal, null, now.AddHours(-3)));
            var resolvedByOther = Issue("Wrong cable tray height", "joonas", IssueStatus.Resolved, IssuePriority.Low, Me, now.AddDays(-2));
            resolvedByOther.ResolvedBy = Me;
            resolvedByOther.ResolvedAt = now.AddDays(-1);
            colleague.Save(resolvedByOther);
            var closed = Issue("Duplicate smoke detector", "mari", IssueStatus.Closed, IssuePriority.High, "joonas", now.AddDays(-9));
            colleague.Save(closed);
            var gone = Issue("Socket outlet behind the cabinet", "mari", IssueStatus.Open, IssuePriority.Normal, null, now.AddMinutes(-20));
            gone.View = new IssueView { UniqueId = "deleted-view", Name = "EN_1_korrus_old", Type = "FloorPlan" };
            colleague.Save(gone);
            host.Targets[gone.Id] = TargetState.ViewMissing;
            host.Targets[resolvedByOther.Id] = TargetState.ElementMissing;
            Check(WaitFor(() => session.IssueCount == 5 && vm.Rows.Count == 5), "colleagues' issues appear live (" + vm.Rows.Count + ")");
            Check(WaitFor(() => vm.Rows.Any(r => r.Id == gone.Id && r.TargetText == "View not found")), "missing view shown on the row");
            Check(vm.Rows.Any(r => r.Id == resolvedByOther.Id && r.TargetText == "Element not found"), "missing element shown on the row");
            vm.Selected = vm.Rows[0]; // any new choice clears the last feedback line
            vm.Selected = null;
            Pump();
            Capture(pane, "05_list_dark.png");

            // ================================================================ markers
            Check(WaitFor(() => markers.Last.Count == 5), "markers: every listed issue with a location is drawn (" + markers.Last.Count + ")");
            Check(!markers.Last.Any(m => m.IsSelected), "markers: none highlighted while no issue is open");
            vm.Scope = "active";
            Check(markers.Last.Count == 3 && markers.Last.All(m => m.Status == IssueStatus.Open || m.Status == IssueStatus.InProgress), "markers follow the filter");
            vm.Scope = "all";
            markers.Click(gone.Id);
            Pump();
            Check(vm.Selected?.Id == gone.Id && vm.Editor?.Id == gone.Id, "clicking a marker selects its issue");
            Check(markers.Last.Single(m => m.IsSelected).IssueId == gone.Id, "the clicked issue's marker is highlighted");
            var openedBefore = host.Opened.Count;
            markers.DoubleClick(first);
            Check(WaitFor(() => host.Opened.Count == openedBefore + 1 && host.Opened.Last() == first) && vm.Selected?.Id == first, "double-clicking a marker opens its issue and its saved view");
            var numbered = colleague.Load(closed.Id);
            numbered.Number = 41;
            numbered.Modified = numbered.Modified.AddSeconds(1);
            colleague.Save(numbered);
            Check(WaitFor(() => session.Issues.Any(i => i.Number == 41)), "colleague's numbered issue arrives");
            vm.Selected = null;
            host.NextCapture = defaultCapture;
            vm.AddIssueCommand.Execute(null);
            Check(WaitFor(() => vm.Editor != null && vm.Editor.IsNew) && vm.Editor.Original.Number == 42, "new issues continue from the highest number");
            vm.Editor.RevertCommand.Execute(null);
            Check(vm.Editor == null, "discarding the new issue closes the form");
            SaveImage(FakeMarkerSink.Preview(true), "13_markers_preview_dark.png");
            SaveImage(FakeMarkerSink.Preview(false), "14_markers_preview_light.png");
            SaveImage(FakeMarkerSink.Preview(true, 4), "15_markers_preview_zoom4.png");
            SaveImage(FakeMarkerSink.Preview(true, 2), "16_markers_preview_dark_2x.png");

            // ================================================================ filter, search, sort
            vm.Scope = "active";
            Check(vm.Rows.Count == 3 && vm.Rows.All(r => r.Status == IssueStatus.Open || r.Status == IssueStatus.InProgress), "Active shows open and in-progress");
            vm.Scope = "resolved";
            Check(vm.Rows.Count == 1 && vm.Rows[0].Id == resolvedByOther.Id, "Resolved scope");
            vm.Scope = "all";
            vm.SearchText = "cable";
            Check(vm.Rows.Count == 1, "search by title");
            vm.SearchText = "500 mm";
            Check(vm.Rows.Count == 1 && vm.Rows[0].Id == first, "search covers the details");
            vm.SearchText = null;
            vm.PriorityHigh = true;
            Check(vm.Rows.Count == 2 && vm.HasActiveFilters, "priority filter");
            vm.Author = "mari";
            Check(vm.Rows.Count == 1 && vm.Rows[0].Id == closed.Id, "author filter combines with priority");
            vm.AssigneeFilter = "nobody";
            Check(vm.ShowNoMatches, "no matches state");
            Capture(pane, "06_no_matches_dark.png");
            vm.ClearFiltersCommand.Execute(null);
            Check(vm.Rows.Count == 5 && !vm.HasActiveFilters, "clear filters");
            vm.SortColumn = "title";
            vm.SortDescending = false;
            Check(vm.Rows.First().Title == "Camera too close to door" && vm.Rows.Last().Title == "Wrong cable tray height", "sort by title ascending");
            vm.Scope = "active";
            var saved = settingsStore.Load(model.ModelGuid);
            Check(saved.Sort.Column == "title" && !saved.Sort.Descending && saved.Filter.Statuses.Count == 2, "filter and sort saved in local settings");
            Check(!Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any(f => File.ReadAllText(f).Contains("\"sort\"")), "nothing personal in the shared folder");
            session.SetModel(null);
            Check(markers.Last.Count == 0, "markers cleared when the model closes");
            session.SetModel(model);
            Check(WaitFor(() => vm.Rows.Count == 3) && vm.Scope == "active" && vm.SortColumn == "title", "filter and sort restored for the project");
            ((ToggleButton)pane.FindName("FiltersButton")).IsChecked = true;
            Pump();
            CapturePopup(pane, "FiltersPopup", "07_filters_popover_dark.png");
            ((ToggleButton)pane.FindName("FiltersButton")).IsChecked = false;
            vm.Scope = "all";
            vm.SortColumn = "modified";
            vm.SortDescending = true;

            // ================================================================ open view (double-click)
            var openRow = vm.Rows.First(r => r.Id == first);
            vm.OpenViewCommand.Execute(openRow);
            Check(WaitFor(() => host.Opened.Contains(first)), "double-click / Enter opens the issue's view");
            var goneRow = vm.Rows.First(r => r.Id == gone.Id);
            vm.OpenView(goneRow);
            Check(WaitFor(() => vm.Feedback != null && vm.Feedback.Contains("no longer exists")), "missing view: explained, not an error dialog");

            // ================================================================ edit, status, comments
            vm.Selected = vm.Rows.First(r => r.Id == first);
            var editor = vm.Editor;
            Check(editor != null && !editor.IsNew && editor.Title == "Camera too close to door", "selecting a row opens the issue");
            Check(!editor.ShowSaveBar, "no save bar until something changes");
            editor.Title = "Camera too close to the door";
            Check(editor.ShowSaveBar && editor.SaveCommand.CanExecute(null), "editing shows Save");
            editor.SaveCommand.Execute(null);
            Check(WaitFor(() => !editor.IsDirty && colleague.Load(first).Title == "Camera too close to the door"), "edit saved");

            Check(editor.StatusActions.Select(a => a.Text).SequenceEqual(new[] { "Start", "Resolve" }), "open issue offers Start and Resolve");
            editor.SetStatusCommand.Execute(IssueStatus.InProgress);
            Check(WaitFor(() => colleague.Load(first).Status == IssueStatus.InProgress), "Start → in progress");
            editor.NewComment = "Moved it 600 mm to the left.";
            editor.AddCommentCommand.Execute(null);
            Check(WaitFor(() => colleague.Load(first).Comments.Count == 1 && editor.NewComment == null), "comment saved and box cleared");
            editor.SetStatusCommand.Execute(IssueStatus.Resolved);
            Check(WaitFor(() => colleague.Load(first).Status == IssueStatus.Resolved && colleague.Load(first).ResolvedBy == Me), "Resolve records who resolved it");
            Check(editor.StatusActions.Select(a => a.Text).SequenceEqual(new[] { "Close", "Reopen" }), "author may close a resolved issue");
            Capture(pane, "08_issue_dark.png");

            vm.Selected = vm.Rows.First(r => r.Id == resolvedByOther.Id);
            Check(vm.Editor.StatusActions.Select(a => a.Text).SequenceEqual(new[] { "Reopen" }) && vm.Editor.CloseHint != null,
                "only the author closes: others see a hint instead");
            Check(vm.Editor.TargetText != null, "issue shows the missing element");

            // ================================================================ colleague edits while I edit
            vm.Selected = vm.Rows.First(r => r.Id == first);
            editor = vm.Editor;
            editor.Body = "My unsaved text";
            var theirs = colleague.Load(first);
            theirs.Title = "Camera moved, please check";
            theirs.Modified = DateTimeOffset.Now.AddSeconds(5);
            colleague.Save(theirs);
            Check(WaitFor(() => editor.HasConflict), "colleague's change while editing is announced");
            editor.SaveCommand.Execute(null);
            Check(WaitFor(() => editor.BannerIsError && editor.HasConflict && !editor.IsBusy), "saving over a newer edit is refused");
            Check(colleague.Load(first).Title == "Camera moved, please check", "colleague's edit kept");
            Capture(pane, "09_conflict_dark.png");
            editor.ReloadCommand.Execute(null);
            Check(WaitFor(() => editor.Title == "Camera moved, please check" && !editor.HasConflict && !editor.IsDirty), "reload shows their version");

            // Not editing: follows colleagues' changes silently.
            theirs = colleague.Load(first);
            theirs.Priority = IssuePriority.Low;
            theirs.Modified = DateTimeOffset.Now.AddSeconds(10);
            colleague.Save(theirs);
            Check(WaitFor(() => editor.IsLow), "open issue follows a colleague's change when not editing");

            // ================================================================ local settings, hot reload, themes
            overlay.Toggle();
            Check(!settingsStore.Load(model.ModelGuid).OverlayVisible, "marker visibility saved locally for the project");
            overlay.Toggle();
            ((ToggleButton)pane.FindName("CardsButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(overlay.ShowCards && settingsStore.Load(model.ModelGuid).ShowCards, "cards switched on from the pane and saved locally");
            session.SetModel(null);
            overlay.ShowCards = false;
            session.SetModel(model);
            Check(overlay.ShowCards, "cards setting restored for the project");
            WaitFor(() => session.IssueCount == 5);
            SaveImage(FakeMarkerSink.PreviewWithCards(), "17_cards_preview.png");

            var reloadedSession = new MarkupSession(dispatcher, overlay, settingsStore);
            var reloaded = new MarkupPane(overlay, reloadedSession, host, new FakeMarkerSink(), Me);
            session.Dispose();
            shellHost.Content = reloaded;
            reloadedSession.SetModel(model);
            Check(WaitFor(() => reloaded.ViewModel.Rows.Count == 5), "hot reload: new pane loads the issues");
            overlay.Toggle();
            Pump();
            Check(Markers(reloaded).IsChecked == false && Markers(pane).IsChecked == true, "hot reload: only the new pane listens");
            overlay.Toggle();

            reloaded.ViewModel.Selected = reloaded.ViewModel.Rows.First(r => r.Id == first);
            reloaded.Theme.ToggleTheme();
            Pump();
            Capture(reloaded, "10_issue_light.png");
            reloaded.ViewModel.Selected = null;
            Pump();
            Capture(reloaded, "11_list_light.png");
            ThemeManager.ForceHighContrast = true;
            reloaded.Theme.ApplyTheme();
            Pump();
            Capture(reloaded, "12_list_high_contrast.png");
            ThemeManager.ForceHighContrast = null;

            // ================================================================ floating window (pane not created: AppLoader load)
            shellHost.Content = null;
            var floating = new PaneWindow(reloaded, IntPtr.Zero) { ShowActivated = false, Left = 420, Top = 20 };
            floating.Show();
            Pump();
            Check(reloaded.IsVisible && ReferenceEquals(floating.Content, reloaded), "panel shows in the floating window");
            var released = floating.Release();
            floating.Close();
            shellHost.Content = released;
            Pump();
            Check(ReferenceEquals(shellHost.Content, reloaded) && reloaded.IsVisible, "closing the window hands the panel back to the pane");

            reloadedSession.Dispose();
            window.Close();

            SaveImage(RibbonPreview(), "ribbon_buttons.png");
            try { Directory.Delete(work, true); } catch { }
        }

        /// <summary>The ribbon as Revit shows it: the Stickies icon next to the Markers eye (on, off), dark and light theme.</summary>
        private static BitmapSource RibbonPreview()
        {
            const int cell = 48;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                for (var row = 0; row < 2; row++)
                {
                    var dark = row == 0;
                    var y = row * cell;
                    dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(0x3B, 0x44, 0x53) : Color.FromRgb(0xF5, 0xF5, 0xF5)), null, new Rect(0, y, cell * 4, cell));
                    var icon = new BitmapImage(new Uri(Path.Combine(StickiesRoot(), "Stickies", "Assets", (dark ? "Dark" : "Light") + " - Stickies.tiff")));
                    var colour = dark ? Color.FromRgb(0xF2, 0xF2, 0xF2) : Color.FromRgb(0x3A, 0x3A, 0x3C);
                    dc.DrawImage(icon, new Rect(8, y + 8, 32, 32));
                    dc.DrawImage(RibbonIcons.Eye(true, 32, colour), new Rect(cell + 8, y + 8, 32, 32));
                    dc.DrawImage(RibbonIcons.Eye(false, 32, colour), new Rect(2 * cell + 8, y + 8, 32, 32));
                    dc.DrawImage(RibbonIcons.Eye(true, 16, colour), new Rect(3 * cell + 16, y + 16, 16, 16));
                }
            }
            var bitmap = new RenderTargetBitmap(cell * 4, cell * 2, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            return bitmap;
        }

        private static string StickiesRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Stickies.sln"))) dir = dir.Parent;
            return dir?.FullName ?? AppContext.BaseDirectory;
        }

        private static Issue Issue(string title, string author, IssueStatus status, IssuePriority priority, string assignee, DateTimeOffset at)
        {
            return new Issue
            {
                Id = IssueId.New(at), Title = title, Author = author, Status = status, Priority = priority, Assignee = assignee,
                Created = at, Modified = at,
                View = new IssueView { UniqueId = "view-plan-2", Name = "EN_2_korrus_VVS", Type = "FloorPlan" },
                Location = new IssueLocation { Point = new[] { 1.0, 2, 3 } }
            };
        }

        // ------------------------------------------------------------------ helpers

        private static ToggleButton Markers(MarkupPane pane) => (ToggleButton)pane.FindName("MarkersButton");

        /// <summary>Clicks the visible button with this text.</summary>
        private static void Click(DependencyObject root, string text)
        {
            var button = Find(root, text);
            if (button == null) { Check(false, "button '" + text + "' is visible"); return; }
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Pump();
        }

        private static Button Find(DependencyObject node, string text)
        {
            if (node is Button b && b.IsVisible && Equals(b.Content, text)) return b;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var found = Find(VisualTreeHelper.GetChild(node, i), text);
                if (found != null) return found;
            }
            return null;
        }

        private static void Check(bool ok, string what)
        {
            Log.AppendLine((ok ? "PASS " : "FAIL ") + what);
            if (!ok) _failures++;
        }

        private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        /// <summary>Keeps the UI thread running (watcher callbacks, background saves and loads) until the condition holds.</summary>
        private static bool WaitFor(Func<bool> condition, int timeoutMs = 8000)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                Pump();
                if (condition()) return true;
                Thread.Sleep(25);
            }
            Pump();
            return condition();
        }

        private static void Capture(FrameworkElement element, string name)
        {
            Pump();
            element.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(element);
            SaveImage(bmp, name);
        }

        private static void CapturePopup(FrameworkElement pane, string popupName, string name)
        {
            var popup = (Popup)pane.FindName(popupName);
            if (popup?.Child is FrameworkElement child && child.ActualWidth > 0)
            {
                Capture(child, name);
            }
            else Check(false, popupName + " rendered");
        }

        private static void SaveImage(ImageSource image, string name)
        {
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create((BitmapSource)image));
            using (var f = File.Create(Path.Combine(_out, name))) png.Save(f);
        }
    }
}

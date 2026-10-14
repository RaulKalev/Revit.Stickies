using System;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.UI;
using Stickies.Infrastructure;

namespace Stickies.Shell
{
    /// <summary>
    /// SHELL – the only part of Stickies that stays fixed for the whole Revit session.
    ///
    /// Revit registers a dockable pane once per session and never lets it go. ricaun AppLoader reloads the add-in as a
    /// fresh copy of Stickies.dll, so anything the pane held on to from the first copy would go stale. The pane
    /// therefore holds only a plain WPF <see cref="ContentControl"/> (no Stickies types). Its reference is parked in
    /// AppDomain data, where every later copy finds it and puts its own UI inside (<see cref="Attach"/>).
    ///
    /// Changing anything here – <see cref="PaneId"/>, <see cref="Title"/>, the initial dock state, <see cref="HostKey"/>
    /// or the kind of host element – takes effect only after a Revit restart. Bump the key's version when the host
    /// contract changes, so an old host left in the session is not reused.
    /// </summary>
    internal static class PaneShell
    {
        public static readonly DockablePaneId PaneId = new DockablePaneId(new Guid("5D3C1E7A-8B42-4F6D-9A1C-2E7B5F8D0C34"));
        public const string Title = "Stickies";
        private const string HostKey = "Stickies.PaneHost/1";

        /// <summary>The session's pane host, or null when the pane was never registered with this host contract.</summary>
        public static ContentControl Host => AppDomain.CurrentDomain.GetData(HostKey) as ContentControl;

        /// <summary>Registers the pane on first load; later loads (hot reload) reuse the registered one.</summary>
        public static void EnsureRegistered(UIControlledApplication application)
        {
            try
            {
                if (DockablePane.PaneIsRegistered(PaneId))
                {
                    if (Host == null)
                        AppLog.Warn("The Stickies pane was registered by an older build in this session. Restart Revit to use the new pane.");
                    return;
                }

                var host = new ContentControl
                {
                    Focusable = false,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Stretch
                };
                application.RegisterDockablePane(PaneId, Title, new ShellPaneProvider(host));
                AppDomain.CurrentDomain.SetData(HostKey, host);
            }
            catch (Exception ex)
            {
                AppLog.Error("Registering the Stickies pane failed", ex);
            }
        }

        /// <summary>Puts this copy's UI into the pane host, replacing whatever an earlier copy left there.</summary>
        public static bool Attach(FrameworkElement content)
        {
            var host = Host;
            if (host == null) return false;
            host.Content = content;
            return true;
        }

        /// <summary>Empties the host if it still shows <paramref name="content"/> (another copy may already own it).</summary>
        public static void Detach(FrameworkElement content)
        {
            var host = Host;
            if (host != null && ReferenceEquals(host.Content, content)) host.Content = null;
        }

        private sealed class ShellPaneProvider : IDockablePaneProvider
        {
            private readonly FrameworkElement _host;

            public ShellPaneProvider(FrameworkElement host) => _host = host;

            public void SetupDockablePane(DockablePaneProviderData data)
            {
                data.FrameworkElement = _host;
                data.VisibleByDefault = false;
                data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right };
            }
        }
    }
}

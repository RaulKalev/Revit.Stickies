using System;
using System.Windows;
using System.Windows.Interop;

namespace Stickies.UI
{
    /// <summary>
    /// A floating, modeless window around the Stickies panel, used when Revit's docked pane is not available in this
    /// session (the add-in was loaded after Revit started, e.g. with ricaun AppLoader). Same panel, same behaviour.
    /// </summary>
    public sealed class PaneWindow : Window
    {
        public PaneWindow(FrameworkElement panel, IntPtr owner)
        {
            Title = "Stickies";
            Width = 400;
            Height = 780;
            MinWidth = 300;
            MinHeight = 360;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            if (owner != IntPtr.Zero) new WindowInteropHelper(this) { Owner = owner };
            Content = panel;
        }

        /// <summary>Hands the panel back (to be put into the docked pane's host again).</summary>
        public FrameworkElement Release()
        {
            var panel = Content as FrameworkElement;
            Content = null;
            return panel;
        }
    }
}

using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Stickies.Infrastructure;
using Stickies.Shell;

namespace Stickies
{
    /// <summary>Shows the Stickies pane, or hides it when it is already shown.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class TogglePaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (!DockablePane.PaneIsRegistered(PaneShell.PaneId) || PaneShell.Host == null)
                {
                    TaskDialog.Show("Stickies", "The Stickies pane becomes available after Revit is restarted.");
                    return Result.Cancelled;
                }

                var pane = commandData.Application.GetDockablePane(PaneShell.PaneId);
                if (pane.IsShown()) pane.Hide();
                else pane.Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppLog.Error("Toggling the Stickies pane failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}

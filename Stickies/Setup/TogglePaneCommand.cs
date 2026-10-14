using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Stickies.Infrastructure;

namespace Stickies
{
    /// <summary>Shows the Stickies panel (docked pane, or a window when the pane is not available), or hides it.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class TogglePaneCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                if (App.Instance == null)
                {
                    message = "Stickies is not loaded.";
                    return Result.Failed;
                }
                App.Instance.TogglePanel(commandData.Application);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AppLog.Error("Showing the Stickies panel failed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}

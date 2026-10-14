using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Stickies
{
    /// <summary>Shows or hides review markers on views.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class ToggleOverlayCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            App.Overlay.Toggle();
            return Result.Succeeded;
        }
    }
}

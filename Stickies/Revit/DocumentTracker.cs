using System;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Stickies.Infrastructure;
using Stickies.Session;

namespace Stickies.Revit
{
    /// <summary>
    /// Follows the active document and reports it as an <see cref="ActiveModel"/> whenever it changes. All Revit reads
    /// happen here, inside Revit event handlers (API context, UI thread). On startup and after a hot reload the
    /// current document is picked up on the first Idling tick.
    /// </summary>
    internal sealed class DocumentTracker : IDisposable
    {
        private readonly UIControlledApplication _app;
        private readonly Action<ActiveModel> _changed;
        private string _currentKey = "";
        private bool _idlingHooked;

        public DocumentTracker(UIControlledApplication app, Action<ActiveModel> changed)
        {
            _app = app;
            _changed = changed;
            _app.ViewActivated += OnViewActivated;
            _app.ControlledApplication.DocumentClosed += OnDocumentClosed;
            CheckOnNextIdle();
        }

        public void Dispose()
        {
            _app.ViewActivated -= OnViewActivated;
            _app.ControlledApplication.DocumentClosed -= OnDocumentClosed;
            if (_idlingHooked) _app.Idling -= OnIdling;
            _idlingHooked = false;
        }

        private void OnViewActivated(object sender, ViewActivatedEventArgs e) => Publish(e.Document);

        // The next active document is only known once Revit settles.
        private void OnDocumentClosed(object sender, DocumentClosedEventArgs e) => CheckOnNextIdle();

        private void CheckOnNextIdle()
        {
            if (_idlingHooked) return;
            _idlingHooked = true;
            _app.Idling += OnIdling;
        }

        private void OnIdling(object sender, IdlingEventArgs e)
        {
            _app.Idling -= OnIdling;
            _idlingHooked = false;
            Publish((sender as UIApplication)?.ActiveUIDocument?.Document);
        }

        private void Publish(Document doc)
        {
            ActiveModel model;
            try
            {
                model = doc == null ? null : Read(doc);
            }
            catch (Exception ex)
            {
                AppLog.Error("Reading the active document failed", ex);
                model = null;
            }

            var key = model?.DocumentKey ?? "";
            if (key == _currentKey) return;
            _currentKey = key;
            _changed(model);
        }

        private static ActiveModel Read(Document doc)
        {
            if (doc.IsFamilyDocument)
                return new ActiveModel { Title = doc.Title, IsFamily = true, DocumentKey = "family|" + doc.Title + "|" + doc.PathName };

            var guid = doc.ProjectInformation?.UniqueId;
            return new ActiveModel
            {
                Title = doc.Title,
                ModelGuid = guid,
                ModelPath = ModelFolderPath(doc),
                DocumentKey = guid + "|" + doc.PathName + "|" + doc.Title
            };
        }

        /// <summary>The model file whose folder holds <c>_stickies</c>: the central file for workshared models.</summary>
        private static string ModelFolderPath(Document doc)
        {
            string path = null;
            try
            {
                if (doc.IsModelInCloud) return null;
                if (doc.IsWorkshared)
                {
                    var central = doc.GetWorksharingCentralModelPath();
                    if (central != null && !central.ServerPath)
                        path = ModelPathUtils.ConvertModelPathToUserVisiblePath(central);
                }
                else
                {
                    path = doc.PathName;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("Model path not available: " + ex.Message);
            }

            return !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) && !path.Contains("://") ? path : null;
        }
    }
}

using System;
using System.Collections.Concurrent;
using Autodesk.Revit.UI;
using Stickies.Infrastructure;

namespace Stickies.Revit
{
    /// <summary>
    /// Runs work in Revit's API context from UI code: requests are queued and executed by one ExternalEvent, on Revit's
    /// main (UI) thread. Must be created in API context (OnStartup).
    /// </summary>
    internal sealed class RevitRequests : IExternalEventHandler, IDisposable
    {
        private readonly ConcurrentQueue<Action<UIApplication>> _queue = new ConcurrentQueue<Action<UIApplication>>();
        private readonly ExternalEvent _event;

        public RevitRequests()
        {
            _event = ExternalEvent.Create(this);
        }

        public void Post(Action<UIApplication> request)
        {
            _queue.Enqueue(request);
            var result = _event.Raise();
            if (result != ExternalEventRequest.Accepted && result != ExternalEventRequest.Pending)
                AppLog.Warn("Revit did not accept the request: " + result);
        }

        public void Execute(UIApplication app)
        {
            while (_queue.TryDequeue(out var request))
            {
                try { request(app); }
                catch (Exception ex) { AppLog.Error("Revit request failed", ex); }
            }
        }

        public string GetName() => "Stickies";

        public void Dispose() => _event.Dispose();
    }
}

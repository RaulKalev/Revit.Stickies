namespace Stickies.Session
{
    /// <summary>What the session needs to know about the active Revit document, read in API context (no Revit types).</summary>
    public sealed class ActiveModel
    {
        public string Title { get; set; }

        /// <summary>ProjectInformation.UniqueId: shared by a central model and its local copies.</summary>
        public string ModelGuid { get; set; }

        /// <summary>
        /// The model file on disk (the central file for workshared models), or null when it has no folder
        /// (unsaved, cloud or server model).
        /// </summary>
        public string ModelPath { get; set; }

        public bool IsFamily { get; set; }

        /// <summary>Distinguishes two open documents of the same project (for example a detached copy).</summary>
        public string DocumentKey { get; set; }
    }
}

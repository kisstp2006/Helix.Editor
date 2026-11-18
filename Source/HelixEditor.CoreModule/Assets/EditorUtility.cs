namespace HelixEditor
{
    public static partial class EditorUtility
    {
        /// <summary>A file to open, of the filters given as names and extension lists after each other
        /// ({"Image files", "png,jpg,jpeg", "All files", "*"}).</summary>
        public static string OpenFilePanelWithFilters(string title, string directory, string[] filters) =>
            OpenFilePanelWithFiltersInternal(title ?? "", directory ?? "", filters == null ? "" : string.Join("\n", filters));
    }
}

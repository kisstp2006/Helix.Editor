namespace HelixEditor
{
    /// <summary>A window with items of its own in its tab's menu (above the dock area's own).</summary>
    public interface IHasCustomMenu
    {
        void AddItemsToMenu(GenericMenu menu);
    }
}

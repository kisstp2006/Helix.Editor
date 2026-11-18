using System;
using System.Reflection;
using HelixEngine;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    public static partial class EditorGui
    {
        static readonly int s_TitlebarHint = "EditorGuiInspectorTitlebar".GetHashCode();

        public static bool InspectorTitlebar(Rect position, bool foldout, Object targetObj, bool expandable) =>
            InspectorTitlebar(position, foldout, new[] { targetObj }, expandable);

        public static bool InspectorTitlebar(Rect position, bool foldout, Editor editor) =>
            InspectorTitlebar(position, foldout, editor != null ? editor.targets : null, true);

        public static void InspectorTitlebar(Rect position, Object[] targetObjs) => InspectorTitlebar(position, false, targetObjs, false);

        /// <summary>The Inspector's title bar of the objects (a component's): its foldout (when expandable), icon,
        /// enabled toggle, title and menu (the component's: reset, remove, move, copy and paste values). A click on
        /// the bar opens and closes it.</summary>
        public static bool InspectorTitlebar(Rect position, bool foldout, Object[] targetObjs, bool expandable)
        {
            Object first = targetObjs != null && targetObjs.Length > 0 ? targetObjs[0] : null;
            int id = GuiUtility.GetControlID(s_TitlebarHint, FocusType.Passive, position);
            if (first == null)
                return foldout;
            float mid = position.y + position.height / 2;
            var arrow = new Rect(position.x + 2, position.y, 12, position.height);
            var icon = new Rect(arrow.xMax + 2, mid - 8, 16, 16);
            float x = icon.xMax + 4;
            PropertyInfo enabled = first is Component ? first.GetType().GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public) : null;
            if (enabled != null && (enabled.PropertyType != typeof(bool) || !enabled.CanWrite))
                enabled = null;
            var toggle = new Rect(x, mid - 7, 14, 14);
            if (enabled != null)
                x = toggle.xMax + 4;
            var menu = new Rect(position.xMax - 18, mid - 8, 16, 16);
            var title = new Rect(x, position.y, Math.Max(0, menu.x - x - 2), position.height);

            Event e = Event.current;
            if (e != null && e.type == EventType.Repaint)
            {
                Gui.Box(position, GuiContent.none, EditorStyles.inspectorTitlebarInternal);
                if (expandable)
                    Gui.Label(arrow, foldout ? "v" : ">", EditorStyles.miniLabel);
                Texture2D image = first is Component ? InspectorWindow.ComponentIcon(first.GetType()) : EditorGuiUtility.ObjectContent(first, first.GetType()).image as Texture2D;
                if (image != null)
                    Gui.DrawTexture(icon, image);
                Gui.Label(title, ObjectNames.GetInspectorTitle(first), EditorStyles.boldLabel);
                if (EditorGuiUtility.FindTexture("Menu") is Texture2D dots)
                    Gui.DrawTexture(menu, dots);
            }
            if (enabled != null)
            {
                bool on = (bool)enabled.GetValue(first);
                bool now = Gui.Toggle(toggle, on, GuiContent.none);
                if (now != on)
                    foreach (Object o in targetObjs)
                        if (o != null)
                        {
                            enabled.SetValue(o, now);
                            EditorUtility.SetDirty(o);
                        }
            }
            if (e != null && e.GetTypeForControl(id) == EventType.MouseDown && position.Contains(e.mousePosition) &&
                !(enabled != null && toggle.Contains(e.mousePosition)))
            {
                if (first is Component c && (e.button == 1 || menu.Contains(e.mousePosition)))
                {
                    Component[] all = c.gameObject.GetComponents<Component>();
                    InspectorWindow.ComponentMenu(targetObjs, Array.IndexOf(all, c), all.Length).DropDown(e.button == 1 ? new Rect(e.mousePosition.x, e.mousePosition.y, 0, 0) : menu);
                    e.Use();
                }
                else if (e.button == 0 && expandable)
                {
                    foldout = !foldout;
                    Gui.changed = true;
                    e.Use();
                }
            }
            return foldout;
        }
    }

    public static partial class EditorGuiLayout
    {
        public static bool InspectorTitlebar(bool foldout, Object targetObj, bool expandable = true) =>
            EditorGui.InspectorTitlebar(GetControlRect(false, EditorGuiUtility.inspectorTitlebarHeightInternal), foldout, targetObj, expandable);

        public static bool InspectorTitlebar(bool foldout, Object[] targetObjs, bool expandable = true) =>
            EditorGui.InspectorTitlebar(GetControlRect(false, EditorGuiUtility.inspectorTitlebarHeightInternal), foldout, targetObjs, expandable);

        public static bool InspectorTitlebar(bool foldout, Editor editor) => InspectorTitlebar(foldout, editor != null ? editor.targets : null, true);

        public static void InspectorTitlebar(Object[] targetObjs) =>
            EditorGui.InspectorTitlebar(GetControlRect(false, EditorGuiUtility.inspectorTitlebarHeightInternal), targetObjs);
    }

    public static partial class EditorGuiUtility
    {
        // The height of the Inspector's title bars.
        internal static float inspectorTitlebarHeightInternal => 22;
    }
}

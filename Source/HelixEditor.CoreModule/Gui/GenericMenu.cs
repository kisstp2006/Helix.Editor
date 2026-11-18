using System;
using System.Collections.Generic;
using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor
{
    /// <summary>A menu built item by item (paths with '/' make submenus), shown at the pointer or under a rect of the
    /// window's GUI.</summary>
    public sealed class GenericMenu
    {
        public delegate void MenuFunction();
        public delegate void MenuFunction2(object userData);

        sealed class Item
        {
            public string path;
            public bool on, enabled, separator;
            public MenuFunction func;
            public MenuFunction2 func2;
            public object userData;
        }

        readonly List<Item> m_Items = new();

        public bool allowDuplicateNames { get; set; }

        public void AddItem(GuiContent content, bool on, MenuFunction func) => Add(content, on, true, func, null, null);
        public void AddItem(GuiContent content, bool on, MenuFunction2 func, object userData) => Add(content, on, true, null, func, userData);
        public void AddDisabledItem(GuiContent content) => Add(content, false, false, null, null, null);
        public void AddDisabledItem(GuiContent content, bool on) => Add(content, on, false, null, null, null);
        public void AddSeparator(string path) => m_Items.Add(new Item { path = path ?? "", separator = true });
        public int GetItemCount() => m_Items.Count;

        void Add(GuiContent content, bool on, bool enabled, MenuFunction func, MenuFunction2 func2, object userData)
        {
            string path = content?.text ?? "";
            if (!allowDuplicateNames && m_Items.Exists(i => !i.separator && i.path == path))
            {
                Debug.LogWarning($"GenericMenu: '{path}' is in the menu already.");
                return;
            }
            m_Items.Add(new Item { path = path, on = on, enabled = enabled, func = func, func2 = func2, userData = userData });
        }

        /// <summary>The items as text with what each runs (for tests).</summary>
        internal IEnumerable<(string path, bool on, bool enabled, Action run)> itemsInternal
        {
            get
            {
                foreach (Item i in m_Items)
                    if (!i.separator)
                    {
                        Item item = i;
                        yield return (i.path, i.on, i.enabled, () => Run(item));
                    }
            }
        }

        static void Run(Item i)
        {
            i.func?.Invoke();
            i.func2?.Invoke(i.userData);
            EditorWindow.drawingInternal?.Repaint();
        }

        GenericDropdownMenu Build()
        {
            var menu = new GenericDropdownMenu();
            AppendToInternal(menu);
            return menu;
        }

        // The items into a menu of the retained UI (a window's tab menu takes them so).
        internal void AppendToInternal(GenericDropdownMenu menu)
        {
            foreach (Item i in m_Items)
            {
                Item item = i;
                if (i.separator)
                    menu.AddSeparator(i.path);
                else if (i.enabled)
                    menu.AddItem(i.path, i.on, () => Run(item));
                else
                    menu.AddDisabledItem(i.path, i.on);
            }
        }

        /// <summary>At the pointer, over the window whose GUI runs.</summary>
        public void ShowAsContext()
        {
            Vector2 at = Event.current != null ? GuiUtility.GuiToScreenPoint(Event.current.mousePosition) : Vector2.zero;
            Show(new Rect(at.x, at.y, 0, 0));
        }

        /// <summary>Under a rect of the GUI running now.</summary>
        public void DropDown(Rect position) => Show(GuiUtility.GuiToScreenRect(position));

        void Show(Rect panelRect)
        {
            EditorWindow w = EditorWindow.drawingInternal;
            Element root = w?.rootElement?.panel?.visualTree;
            if (root == null)
                return;
            Build().DropDown(panelRect, root, DropdownMenuSizeMode.Auto);
            lastShownInternal = this;
        }

        // Under an element of the retained UI.
        internal void DropDownInternal(Element anchor)
        {
            Element root = anchor?.panel?.visualTree;
            if (root == null)
                return;
            Build().DropDown(anchor.worldBound, root, DropdownMenuSizeMode.Auto);
            lastShownInternal = this;
        }

        internal static GenericMenu lastShownInternal;
    }
}

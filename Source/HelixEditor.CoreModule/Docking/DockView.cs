using System.Collections.Generic;
using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor.Docking
{
    // The layout drawn in the shell's dock area (the main view) or a floating container drawn in its own window (a
    // container's view): splits as rows and columns with splitters between their children, dock areas with their tab
    // strip and the selected window's content; floating containers without a window of their own over the main view
    // with a shadow. Tabs drag to reorder, onto an area's edges or middle in any view (the drop zone lit; the views'
    // places on the desktop relate them), or out to float; splitters drag.
    internal sealed class DockView
    {
        readonly Element m_Root;
        readonly int m_Display;
        readonly FloatingContainer m_Container;
        readonly Element m_Docked, m_Floating, m_Indicator;
        readonly List<(DockArea area, Element element, Element strip)> m_Areas = new();

        // A tab being dragged.
        string m_DragTab;
        Element m_DragFrom;
        Vector2 m_DragStart;
        // The dragged tab's area when it is the area's only tab (not a target of its own tab).
        DockArea m_DragArea;
        bool m_Dragging;
        int m_DragPointer;

        public Element root => m_Root;
        public int display => m_Display;
        public FloatingContainer container => m_Container;

        // A size of the skin's (its --hx-* custom properties); a share of the view when the sheet lacks it.
        internal float Metric(string name, float share)
        {
            if (m_Root.customStyle != null && m_Root.customStyle.TryGetValue(new CustomStyleProperty<float>("--hx-" + name), out float v))
                return v;
            Rect r = m_Root.worldBound;
            return Mathf.Max(1, (name.EndsWith("Height") || name.EndsWith("height") || name.EndsWith("-y") ? r.height : r.width) * share);
        }

        // Where a tab dropped out of every area floats: its container under the pointer, grabbed at its tab.
        internal Rect FloatRect(Vector2 p)
        {
            Rect root = m_Root.worldBound;
            return new Rect(p.x - root.x - Metric("float-grab-x", 0), p.y - root.y - Metric("float-grab-y", 0), Metric("float-width", 0.3f),
                            Metric("float-height", 0.3f));
        }

        // A new window's place in the view.
        internal Rect NewWindowRect() =>
            new Rect(Metric("window-x", 0.1f), Metric("window-y", 0.1f), Metric("window-width", 0.3f), Metric("window-height", 0.3f));

        public DockView(Element root, int display = 0, FloatingContainer container = null)
        {
            m_Root = root;
            m_Display = display;
            m_Container = container;
            m_Root.Clear();
            m_Docked = new Element();
            m_Docked.AddToClassList("dock-docked");
            m_Floating = new Element();
            m_Floating.AddToClassList("dock-floating-layer");
            m_Floating.pickingMode = PickingMode.Ignore;
            m_Indicator = new Element();
            m_Indicator.AddToClassList("dock-drop-indicator");
            m_Indicator.pickingMode = PickingMode.Ignore;
            m_Indicator.style.display = DisplayStyle.None;
            m_Root.Add(m_Docked);
            m_Root.Add(m_Floating);
            m_Root.Add(m_Indicator);
        }

        bool m_Building, m_Again;

        // Built again once more when asked while building (a window made for a tab may ask).
        public void Build(DockLayout layout)
        {
            if (m_Building)
            {
                m_Again = true;
                return;
            }
            m_Building = true;
            do
            {
                m_Again = false;
                BuildOnce(layout);
            } while (m_Again);
            m_Building = false;
        }

        void BuildOnce(DockLayout layout)
        {
            // The windows' content stays theirs: taken out before the elements are made again.
            foreach (var (area, element, strip) in m_Areas)
                foreach (string tab in area.tabs)
                    DockManager.WindowOf(tab)?.rootElement.RemoveFromHierarchy();
            m_Areas.Clear();
            m_Docked.Clear();
            m_Floating.Clear();
            // A container's view: its tree.
            if (m_Container != null)
            {
                if (m_Container.root != null)
                    m_Docked.Add(MakeNode(m_Container.root));
                return;
            }
            if (layout.maximized != null)
            {
                m_Docked.Add(MakeArea(layout.maximized));
                return;
            }
            if (layout.root != null)
                m_Docked.Add(MakeNode(layout.root));
            foreach (FloatingContainer f in layout.floating)
            {
                // Those in a window of their own are drawn there.
                if (DockManager.HasWindow(f))
                    continue;
                var container = new Element();
                container.AddToClassList("dock-floating");
                container.AddToClassList("hx-shadow");
                container.style.left = f.rect.x;
                container.style.top = f.rect.y;
                container.style.width = f.rect.width;
                container.style.height = f.rect.height;
                container.Add(MakeNode(f.root));
                m_Floating.Add(container);
            }
        }

        Element MakeNode(DockNode node)
        {
            if (node is DockArea area)
                return MakeArea(area);
            var split = (DockSplit)node;
            var e = new Element();
            e.AddToClassList("dock-split");
            e.AddToClassList(split.column ? "column" : "row");
            float total = 0;
            foreach (float s in split.shares)
                total += s;
            for (int i = 0; i < split.children.Count; i++)
            {
                if (i > 0)
                    e.Add(MakeSplitter(split, i - 1, e));
                Element child = MakeNode(split.children[i]);
                child.style.flexGrow = total > 0 ? split.shares[i] / total : 1;
                child.style.flexBasis = 0;
                e.Add(child);
            }
            return e;
        }

        Element MakeSplitter(DockSplit split, int index, Element owner)
        {
            var splitter = new Element();
            splitter.AddToClassList("dock-splitter");
            splitter.AddToClassList(split.column ? "column" : "row");
            Vector2 last = default;
            bool held = false;
            splitter.RegisterCallback<PointerDownEvent>(e =>
            {
                held = true;
                last = new Vector2(e.position.x, e.position.y);
                splitter.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            splitter.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!held)
                    return;
                Vector2 p = new Vector2(e.position.x, e.position.y);
                Rect r = owner.worldBound;
                float size = split.column ? r.height : r.width;
                float total = 0;
                foreach (float s in split.shares)
                    total += s;
                float delta = (split.column ? p.y - last.y : p.x - last.x) / Mathf.Max(1, size) * total;
                last = p;
                DockLayout.MoveSplitter(split, index, delta);
                // The children's shares only (no new elements).
                int child = 0;
                foreach (Element c in owner.Children())
                {
                    if (c.ClassListContains("dock-splitter"))
                        continue;
                    c.style.flexGrow = split.shares[child] / total;
                    child++;
                }
            });
            splitter.RegisterCallback<PointerUpEvent>(e =>
            {
                held = false;
                splitter.ReleasePointer(e.pointerId);
                DockManager.Saved();
            });
            return splitter;
        }

        Element MakeArea(DockArea area)
        {
            var e = new Element();
            e.AddToClassList("dock-area");
            var strip = new Element();
            strip.AddToClassList("dock-tab-strip");
            strip.AddToClassList("hx-tab-strip");
            // In a floating window the strip (but its tabs) moves the window.
            if (m_Container != null)
                strip.AddToClassList("editor-caption");
            e.Add(strip);
            for (int i = 0; i < area.tabs.Count; i++)
                strip.Add(MakeTab(area, i));
            var fill = new Element();
            fill.AddToClassList("dock-tab-fill");
            // In a floating window the strip's free part moves the window.
            if (m_Container != null)
                fill.AddToClassList("editor-caption");
            strip.Add(fill);
            var content = new Element();
            content.AddToClassList("dock-content");
            content.AddToClassList("hx-panel");
            e.Add(content);
            EditorWindow w = area.selectedTab != null ? DockManager.WindowOf(area.selectedTab) : null;
            if (w != null)
            {
                w.rootElement.RemoveFromHierarchy();
                content.Add(w.rootElement);
            }
            // The window under the pointer (EditorWindow.mouseOverWindow).
            e.RegisterCallback<PointerEnterEvent>(_ =>
                EditorWindow.mouseOverWindow = area.selectedTab != null ? DockManager.WindowOf(area.selectedTab) : null);
            e.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (area.selectedTab != null && EditorWindow.mouseOverWindow == DockManager.WindowOf(area.selectedTab))
                    EditorWindow.mouseOverWindow = null;
            });
            m_Areas.Add((area, e, strip));
            return e;
        }

        Element MakeTab(DockArea area, int index)
        {
            string tab = area.tabs[index];
            EditorWindow w = DockManager.WindowOf(tab);
            var e = new Element();
            e.AddToClassList("dock-tab");
            e.AddToClassList(index == area.selected ? "hx-tab-active" : "hx-tab");
            if (index == area.selected)
                e.AddToClassList("active");
            if (w != null)
            {
                var icon = new Element();
                icon.AddToClassList("dock-tab-icon");
                icon.AddToClassList("icon-window-" + Kebab(tab));
                e.Add(icon);
            }
            var label = new Label(w != null ? w.titleContent.text : tab);
            label.AddToClassList("dock-tab-label");
            e.Add(label);
            // The close button (shown on the active and the hovered tab).
            var close = new Element();
            close.AddToClassList("dock-tab-close");
            close.RegisterCallback<PointerDownEvent>(ev => ev.StopPropagation());
            close.RegisterCallback<PointerUpEvent>(ev =>
            {
                ev.StopPropagation();
                CloseTab(tab);
            });
            e.Add(close);
            e.RegisterCallback<PointerDownEvent>(ev =>
            {
                if (ev.button == 1)
                {
                    OpenTabMenu(area, tab, e);
                    ev.StopPropagation();
                    return;
                }
                // The middle button closes it.
                if (ev.button == 2)
                {
                    ev.StopPropagation();
                    CloseTab(tab);
                    return;
                }
                m_DragTab = tab;
                m_DragFrom = e;
                m_DragStart = new Vector2(ev.position.x, ev.position.y);
                m_DragArea = area.tabs.Count == 1 ? area : null;
                m_Dragging = false;
                m_DragPointer = ev.pointerId;
                e.CapturePointer(ev.pointerId);
                ev.StopPropagation();
            });
            e.RegisterCallback<PointerMoveEvent>(ev =>
            {
                if (m_DragTab != tab || m_DragFrom != e)
                    return;
                Vector2 p = new Vector2(ev.position.x, ev.position.y);
                if (!m_Dragging && (p - m_DragStart).magnitude > Metric("drag-threshold", 0.005f))
                    m_Dragging = true;
                if (!m_Dragging)
                    return;
                // The only tab of a floating window moves its window with the pointer (docking where it is dropped
                // over another window's area).
                if (MovesWindow(tab))
                {
                    Vector2 desktop = ToDesktop(p);
                    Rect r = m_Container.rect;
                    var moved = new Rect(desktop.x - m_DragStart.x, desktop.y - m_DragStart.y, r.width, r.height);
                    m_Container.rect = moved;
                    EditorApplication.SetDisplayRectInternal(m_Display, moved);
                }
                ShowDrop(p);
            });
            e.RegisterCallback<PointerUpEvent>(ev =>
            {
                if (m_DragTab != tab || m_DragFrom != e)
                    return;
                e.ReleasePointer(m_DragPointer);
                Vector2 p = new Vector2(ev.position.x, ev.position.y);
                bool dragged = m_Dragging;
                m_DragTab = null;
                m_DragFrom = null;
                m_Dragging = false;
                DockManager.HideIndicators();
                if (!dragged)
                {
                    DockManager.WindowOf(tab)?.Focus();
                    if (area.selected != area.tabs.IndexOf(tab))
                    {
                        area.selected = area.tabs.IndexOf(tab);
                        DockManager.Changed();
                    }
                    return;
                }
                Drop(tab, p);
            });
            return e;
        }

        // A tab's window closed (its tab goes with it), the layout saved.
        static void CloseTab(string tab)
        {
            EditorWindow w = DockManager.WindowOf(tab);
            if (w != null)
                w.Close();
            else
                DockManager.layout.RemoveTab(tab);
            DockManager.Changed();
            DockManager.Saved();
        }

        static string Kebab(string name)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && i > 0)
                    sb.Append('-');
                sb.Append(char.ToLowerInvariant(name[i]));
            }
            return sb.ToString();
        }

        bool Over(DockArea area, Vector2 p)
        {
            foreach (var (a, element, strip) in m_Areas)
                if (a == area)
                    return element.worldBound.Contains(p);
            return false;
        }

        // A floating window of one area of one tab: its tab moves the window.
        bool MovesWindow(string tab) =>
            DockManager.osWindows && m_Container != null && m_Container.root is DockArea only && only.tabs.Count == 1 && only.tabs[0] == tab;

        // A point of the view on the desktop and back (the view's display's place).
        internal Vector2 ToDesktop(Vector2 p) => p + DockManager.Origin(m_Display);
        internal Vector2 FromDesktop(Vector2 p) => p - DockManager.Origin(m_Display);

        // The area under a point of the view and where on it a tab would go (its edges' quarters, else its middle).
        internal bool Target(Vector2 p, out DockArea area, out DropZone zone, out Rect rect, out int index)
        {
            area = null;
            zone = DropZone.Center;
            rect = default;
            index = -1;
            // Floating areas first (they are over the docked ones).
            for (int k = m_Areas.Count - 1; k >= 0; k--)
            {
                var (a, element, strip) = m_Areas[k];
                Rect r = element.worldBound;
                if (!r.Contains(p))
                    continue;
                area = a;
                rect = r;
                Rect s = strip.worldBound;
                if (s.Contains(p))
                {
                    // On the strip: among the tabs.
                    index = 0;
                    foreach (Element t in strip.Children())
                    {
                        if (!t.ClassListContains("dock-tab"))
                            continue;
                        Rect tr = t.worldBound;
                        if (p.x > tr.x + tr.width * 0.5f)
                            index++;
                    }
                    zone = DropZone.Center;
                    return true;
                }
                float fx = (p.x - r.x) / Mathf.Max(1, r.width), fy = (p.y - r.y) / Mathf.Max(1, r.height);
                if (fx < 0.25f)
                    zone = DropZone.Left;
                else if (fx > 0.75f)
                    zone = DropZone.Right;
                else if (fy < 0.25f)
                    zone = DropZone.Top;
                else if (fy > 0.75f)
                    zone = DropZone.Bottom;
                return true;
            }
            return false;
        }

        // The drop zone lit in the view under the pointer (any view); outside every area the container a tab would
        // float in, when floating containers are drawn over the main view.
        void ShowDrop(Vector2 p)
        {
            Vector2 desktop = ToDesktop(p);
            if (DockManager.FindTarget(desktop, m_DragArea, out DockView view, out DockArea area, out DropZone zone, out Rect ar, out int index))
            {
                Rect r = zone switch
                {
                    DropZone.Left => new Rect(ar.x, ar.y, ar.width * 0.5f, ar.height),
                    DropZone.Right => new Rect(ar.x + ar.width * 0.5f, ar.y, ar.width * 0.5f, ar.height),
                    DropZone.Top => new Rect(ar.x, ar.y, ar.width, ar.height * 0.5f),
                    DropZone.Bottom => new Rect(ar.x, ar.y + ar.height * 0.5f, ar.width, ar.height * 0.5f),
                    _ => ar,
                };
                DockManager.HideIndicators(view);
                view.ShowIndicator(r);
                return;
            }
            DockManager.HideIndicators();
            if (!DockManager.osWindows && DockManager.mainView != null)
            {
                Rect root = DockManager.mainView.root.worldBound;
                Rect f = DockManager.mainView.FloatRect(DockManager.mainView.FromDesktop(desktop));
                DockManager.mainView.ShowIndicator(new Rect(f.x + root.x, f.y + root.y, f.width, f.height));
            }
        }

        internal void ShowIndicator(Rect r)
        {
            Rect root = m_Root.worldBound;
            m_Indicator.style.display = DisplayStyle.Flex;
            m_Indicator.style.left = r.x - root.x;
            m_Indicator.style.top = r.y - root.y;
            m_Indicator.style.width = r.width;
            m_Indicator.style.height = r.height;
        }

        internal void HideIndicator() => m_Indicator.style.display = DisplayStyle.None;

        // Onto an area of any view; else out to float: a window of its own on the desktop (the only tab of a floating
        // window moves its window), or a container over the main view.
        void Drop(string tab, Vector2 p)
        {
            Vector2 desktop = ToDesktop(p);
            DockArea own = m_DragArea;
            m_DragArea = null;
            if (DockManager.FindTarget(desktop, own, out _, out DockArea area, out DropZone zone, out _, out int index))
                DockManager.DockTab(tab, area, zone, index);
            else if (MovesWindow(tab) || (own != null && Over(own, p)))
            {
                // It moved with the pointer (a floating window's only tab), or back onto its own area: where it is.
                DockManager.Saved();
                return;
            }
            else if (DockManager.osWindows)
            {
                Rect at = new Rect(desktop.x - Metric("float-grab-x", 0), desktop.y - Metric("float-grab-y", 0), Metric("float-width", 0.3f),
                                   Metric("float-height", 0.3f));
                if (m_Container != null && m_Container.root is DockArea only && only.tabs.Count == 1 && only.tabs[0] == tab)
                    DockManager.MoveFloating(m_Container, new Rect(at.x, at.y, m_Container.rect.width, m_Container.rect.height));
                else
                    DockManager.FloatTab(tab, at);
            }
            else
            {
                Vector2 main = DockManager.mainView != null ? DockManager.mainView.FromDesktop(desktop) : p;
                DockManager.FloatTab(tab, (DockManager.mainView ?? this).FloatRect(main));
            }
            DockManager.Saved();
        }

        void OpenTabMenu(DockArea area, string tab, Element anchor)
        {
            var menu = new GenericDropdownMenu();
            EditorWindow w = DockManager.WindowOf(tab);
            bool maximized = DockManager.layout.maximized == area;
            menu.AddItem(maximized ? "Restore" : "Maximize", false, () =>
            {
                if (w != null)
                    w.maximized = !maximized;
            });
            menu.AddItem("Close Tab", false, () => w?.Close());
            menu.AddSeparator("");
            foreach (string type in DockManager.WindowTypes())
            {
                string t = type;
                if (DockManager.layout.AreaOf(t) != null)
                    menu.AddDisabledItem("Add Tab/" + t, false);
                else
                    menu.AddItem("Add Tab/" + t, false, () =>
                    {
                        DockManager.DockTab(t, area, DropZone.Center, -1);
                        DockManager.Saved();
                    });
            }
            menu.DropDown(anchor.worldBound, m_Root, DropdownMenuSizeMode.Auto);
        }
    }
}

using System.Collections.Generic;
using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor.Docking
{
    // The layout drawn in the shell's dock area: splits as rows and columns with splitters between their children, dock
    // areas with their tab strip and the selected window's content, floating containers over them with a shadow. Tabs
    // drag to reorder, onto another area's edges or middle (the drop zone lit), or out to float; splitters drag.
    internal sealed class DockView
    {
        const float kDragStart = 6;

        readonly Element m_Root;
        readonly Element m_Docked, m_Floating, m_Indicator;
        readonly List<(DockArea area, Element element, Element strip)> m_Areas = new();

        // A tab being dragged.
        string m_DragTab;
        Element m_DragFrom;
        Vector2 m_DragStart;
        bool m_Dragging;
        int m_DragPointer;

        public Element root => m_Root;

        public DockView(Element root)
        {
            m_Root = root;
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
            if (layout.maximized != null)
            {
                m_Docked.Add(MakeArea(layout.maximized));
                return;
            }
            if (layout.root != null)
                m_Docked.Add(MakeNode(layout.root));
            foreach (FloatingContainer f in layout.floating)
            {
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
            e.Add(strip);
            for (int i = 0; i < area.tabs.Count; i++)
                strip.Add(MakeTab(area, i));
            var fill = new Element();
            fill.AddToClassList("dock-tab-fill");
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
            e.RegisterCallback<PointerDownEvent>(ev =>
            {
                if (ev.button == 1)
                {
                    OpenTabMenu(area, tab, e);
                    ev.StopPropagation();
                    return;
                }
                m_DragTab = tab;
                m_DragFrom = e;
                m_DragStart = new Vector2(ev.position.x, ev.position.y);
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
                if (!m_Dragging && (p - m_DragStart).magnitude > kDragStart)
                    m_Dragging = true;
                if (m_Dragging)
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
                m_Indicator.style.display = DisplayStyle.None;
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

        // The area under a point and where on it a tab would go (its edges' quarters, else its middle).
        bool Target(Vector2 p, out DockArea area, out DropZone zone, out Rect rect, out int index)
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

        void ShowDrop(Vector2 p)
        {
            Rect root = m_Root.worldBound;
            Rect r;
            if (Target(p, out DockArea area, out DropZone zone, out Rect ar, out int index))
            {
                r = zone switch
                {
                    DropZone.Left => new Rect(ar.x, ar.y, ar.width * 0.5f, ar.height),
                    DropZone.Right => new Rect(ar.x + ar.width * 0.5f, ar.y, ar.width * 0.5f, ar.height),
                    DropZone.Top => new Rect(ar.x, ar.y, ar.width, ar.height * 0.5f),
                    DropZone.Bottom => new Rect(ar.x, ar.y + ar.height * 0.5f, ar.width, ar.height * 0.5f),
                    _ => ar,
                };
            }
            else
                r = new Rect(p.x - 150, p.y - 12, 300, 220);
            m_Indicator.style.display = DisplayStyle.Flex;
            m_Indicator.style.left = r.x - root.x;
            m_Indicator.style.top = r.y - root.y;
            m_Indicator.style.width = r.width;
            m_Indicator.style.height = r.height;
        }

        void Drop(string tab, Vector2 p)
        {
            Rect root = m_Root.worldBound;
            if (Target(p, out DockArea area, out DropZone zone, out _, out int index))
                DockManager.DockTab(tab, area, zone, index);
            else
                DockManager.FloatTab(tab, new Rect(p.x - root.x - 150, p.y - root.y - 12, 300, 220));
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
            menu.DropDown(anchor.worldBound, m_Root, true);
        }
    }
}

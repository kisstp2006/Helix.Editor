using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HelixEditor.Docking;
using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor.Overlays
{
    // A window's overlays: the top row's toolbars (from its start and from its end), the bottom row, the toolbar strips
    // and panel columns at the view's sides, the floating ones over it. Each overlay in a container: a toolbar with a
    // grip, a panel with a header (title, collapse); dragged by the grip or header to another zone or floating. A right
    // click on the top row opens the overlay menu. Its state (each overlay's display, collapse, zone, place, order,
    // floating position) lives in the layout's state lines under the window's tab.
    internal sealed class OverlayCanvas
    {
        const float kEdge = 40, kDragStart = 4;

        static readonly List<OverlayCanvas> s_Canvases = new();

        internal readonly EditorWindow window;
        readonly Element m_Top, m_Host;
        readonly Element m_TopStart, m_TopEnd, m_Bottom, m_LeftToolbar, m_RightToolbar, m_LeftColumn, m_RightColumn, m_Floating;
        readonly List<Overlay> m_Overlays = new();
        readonly Dictionary<Overlay, Element> m_Containers = new();
        bool m_Building;

        // A drag of an overlay's grip: which, from where, past the start yet.
        Overlay m_Dragged;
        Vector2 m_DragFrom, m_GrabOffset;
        bool m_DragMoved;

        internal IReadOnlyList<Overlay> overlays => m_Overlays;
        internal Element topInternal => m_Top;
        internal Element hostInternal => m_Host;

        internal static OverlayCanvas Of(EditorWindow w)
        {
            foreach (OverlayCanvas c in s_Canvases)
                if (c.window == w)
                    return c;
            return null;
        }

        string StateKey => "overlays:" + window.GetType().Name;

        /// <summary>The canvas of a window: its top row (a toolbar row the window's markup has) and the view it
        /// covers; the window type's overlays made and placed.</summary>
        internal static OverlayCanvas Attach(EditorWindow w, Element top, Element host)
        {
            var c = new OverlayCanvas(w, top, host);
            s_Canvases.Add(c);
            c.Make();
            return c;
        }

        OverlayCanvas(EditorWindow w, Element top, Element host)
        {
            window = w;
            m_Top = top;
            m_Host = host;
            m_TopStart = Row("overlay-zone overlay-top-start");
            m_TopEnd = Row("overlay-zone overlay-top-end");
            var fill = new Element { pickingMode = PickingMode.Ignore };
            fill.style.flexGrow = 1;
            if (m_Top != null)
            {
                m_Top.Clear();
                m_Top.Add(m_TopStart);
                m_Top.Add(fill);
                m_Top.Add(m_TopEnd);
                m_Top.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button == 1)
                    {
                        ShowMenu(new Rect(e.position.x, e.position.y, 1, 1));
                        e.StopPropagation();
                    }
                });
            }
            var layer = new Element { name = "overlay-canvas", pickingMode = PickingMode.Ignore };
            Fill(layer);
            m_Floating = new Element { name = "overlay-floating", pickingMode = PickingMode.Ignore };
            Fill(m_Floating);
            var left = Side("overlay-left", FlexDirection.Row);
            left.style.left = 0;
            var right = Side("overlay-right", FlexDirection.RowReverse);
            right.style.right = 0;
            m_LeftToolbar = Column("overlay-zone overlay-left-toolbar");
            m_LeftColumn = Column("overlay-zone overlay-left-column");
            m_RightToolbar = Column("overlay-zone overlay-right-toolbar");
            m_RightColumn = Column("overlay-zone overlay-right-column");
            left.Add(m_LeftToolbar);
            left.Add(m_LeftColumn);
            right.Add(m_RightToolbar);
            right.Add(m_RightColumn);
            m_Bottom = Row("overlay-zone overlay-bottom");
            m_Bottom.style.position = Position.Absolute;
            m_Bottom.style.left = 0;
            m_Bottom.style.right = 0;
            m_Bottom.style.bottom = 0;
            layer.Add(left);
            layer.Add(right);
            layer.Add(m_Bottom);
            layer.Add(m_Floating);
            m_Host.Add(layer);
        }

        static void Fill(Element e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0;
            e.style.top = 0;
            e.style.right = 0;
            e.style.bottom = 0;
        }

        static Element Row(string classes)
        {
            var e = new Element { pickingMode = PickingMode.Ignore };
            foreach (string c in classes.Split(' '))
                e.AddToClassList(c);
            e.style.flexDirection = FlexDirection.Row;
            e.style.alignItems = Align.Center;
            return e;
        }

        static Element Column(string classes)
        {
            var e = new Element { pickingMode = PickingMode.Ignore };
            foreach (string c in classes.Split(' '))
                e.AddToClassList(c);
            e.style.flexDirection = FlexDirection.Column;
            return e;
        }

        static Element Side(string name, FlexDirection direction)
        {
            var e = new Element { name = name, pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.top = 0;
            e.style.bottom = 0;
            e.style.flexDirection = direction;
            return e;
        }

        internal void Detach()
        {
            foreach (Overlay o in m_Overlays)
                try
                {
                    o.OnWillBeDestroyed();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            m_Overlays.Clear();
            s_Canvases.Remove(this);
        }

        // ------------------------------------------------------------------------------------- making them

        void Make()
        {
            int order = 0;
            foreach ((Type type, OverlayAttribute a) in OverlayRegistry.Of(window.GetType()))
            {
                Overlay o;
                try
                {
                    o = (Overlay)Activator.CreateInstance(type);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    continue;
                }
                o.m_Canvas = this;
                o.m_Attribute = a;
                o.m_Id = a.id;
                o.displayName = a.displayName;
                Defaults(o, order++);
                m_Overlays.Add(o);
            }
            ApplyState(DockManager.StateOf(StateKey));
            foreach (Overlay o in m_Overlays)
                try
                {
                    o.OnCreated();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            RebuildInternal();
        }

        static void Defaults(Overlay o, int order)
        {
            OverlayAttribute a = o.m_Attribute;
            o.m_Displayed = a.defaultDisplay;
            o.m_Collapsed = false;
            o.m_Zone = a.defaultDockZone;
            // A panel never docks in a toolbar zone.
            if (!o.toolbarInternal && IsToolbarZone(a.defaultDockZone))
                o.m_Zone = a.defaultDockZone == DockZone.LeftToolbar ? DockZone.LeftColumn : DockZone.RightColumn;
            o.m_Position = a.defaultDockPosition;
            o.m_Order = a.defaultDockIndex == int.MaxValue ? 1000 + order : a.defaultDockIndex;
            o.m_FloatingPosition = new Vector2(10, 10);
            o.m_Layout = a.defaultLayout;
        }

        static bool IsToolbarZone(DockZone z) => z == DockZone.TopToolbar || z == DockZone.BottomToolbar || z == DockZone.LeftToolbar || z == DockZone.RightToolbar;

        /// <summary>Every overlay as its attribute says (the menu's Reset).</summary>
        internal void ResetInternal()
        {
            for (int i = 0; i < m_Overlays.Count; i++)
                Defaults(m_Overlays[i], i);
            RebuildInternal();
            Save();
        }

        internal Overlay Find(string id) => m_Overlays.Find(o => o.id == id);

        // ------------------------------------------------------------------------------------------ placing

        internal void RebuildInternal()
        {
            if (m_Building || m_Host == null)
                return;
            m_Building = true;
            try
            {
                foreach (Element zone in new[] { m_TopStart, m_TopEnd, m_Bottom, m_LeftToolbar, m_RightToolbar, m_LeftColumn, m_RightColumn, m_Floating })
                    zone.Clear();
                m_Containers.Clear();
                var shown = m_Overlays.FindAll(o => o.m_Displayed);
                shown.Sort((a, b) => a.m_Order != b.m_Order ? a.m_Order.CompareTo(b.m_Order) : string.CompareOrdinal(a.id, b.id));
                var topStart = shown.FindAll(o => o.m_Zone == DockZone.TopToolbar && o.m_Position == DockPosition.Top);
                foreach (Overlay o in shown)
                {
                    Element zone = ZoneElement(o);
                    if (zone == null)
                        continue;
                    if (o.toolbarInternal)
                        o.SetLayoutInternal(o.m_Zone switch
                        {
                            DockZone.LeftToolbar or DockZone.RightToolbar => Layout.VerticalToolbar,
                            DockZone.TopToolbar or DockZone.BottomToolbar => Layout.HorizontalToolbar,
                            _ => o.m_Layout == Layout.Panel ? Layout.HorizontalToolbar : o.m_Layout,
                        });
                    bool lastStart = topStart.Count > 0 && topStart[topStart.Count - 1] == o;
                    Element c = Container(o, lastStart);
                    if (o.floating)
                    {
                        c.style.position = Position.Absolute;
                        c.style.left = o.m_FloatingPosition.x;
                        c.style.top = o.m_FloatingPosition.y;
                    }
                    m_Containers[o] = c;
                    zone.Add(c);
                }
            }
            finally
            {
                m_Building = false;
            }
        }

        Element ZoneElement(Overlay o) => o.m_Zone switch
        {
            DockZone.TopToolbar => m_Top == null ? m_Floating : o.m_Position == DockPosition.Top ? m_TopStart : m_TopEnd,
            DockZone.BottomToolbar => m_Bottom,
            DockZone.LeftToolbar => m_LeftToolbar,
            DockZone.RightToolbar => m_RightToolbar,
            DockZone.LeftColumn => m_LeftColumn,
            DockZone.RightColumn => m_RightColumn,
            _ => m_Floating,
        };

        // An overlay's container: a toolbar's row or column with its grip, a panel's box with its header.
        Element Container(Overlay o, bool lastOfTopStart)
        {
            var c = new Element { name = "overlay:" + o.id };
            c.AddToClassList("overlay");
            Element content = null;
            if (!o.m_Collapsed || o.toolbarInternal)
            {
                try
                {
                    content = o.CreatePanelContent();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                content ??= new Element();
            }
            if (o.toolbarInternal)
            {
                bool vertical = o.m_Layout == Layout.VerticalToolbar;
                bool inTop = o.m_Zone == DockZone.TopToolbar && m_Top != null;
                c.AddToClassList(vertical ? "overlay-toolbar-vertical" : "overlay-toolbar");
                if (inTop)
                    c.AddToClassList("overlay-in-row");
                c.style.flexDirection = vertical ? FlexDirection.Column : FlexDirection.Row;
                c.style.alignItems = Align.Center;
                Element grip = Grip(o, vertical ? "overlay-grip-horizontal" : "overlay-grip-vertical");
                if (content != null)
                {
                    content.style.flexDirection = vertical ? FlexDirection.Column : FlexDirection.Row;
                    content.style.alignItems = Align.Center;
                }
                // In the classic row the grip is the separator after a start overlay (none after the last).
                if (inTop && o.m_Position == DockPosition.Top)
                {
                    c.Add(content);
                    if (lastOfTopStart)
                        grip.AddToClassList("last");
                    c.Add(grip);
                }
                else
                {
                    c.Add(grip);
                    c.Add(content);
                }
                return c;
            }
            c.AddToClassList(o.headerInternal ? "overlay-panel" : "overlay-bare");
            if (o.headerInternal)
            {
                Element header = Grip(o, "overlay-header");
                header.pickingMode = PickingMode.Position;
                header.Add(new Label(o.displayName) { pickingMode = PickingMode.Ignore });
                var fold = new Button(() => o.collapsed = !o.collapsed) { name = "overlay-collapse" };
                fold.AddToClassList("overlay-collapse");
                var arrow = new Element();
                arrow.AddToClassList("icon");
                arrow.AddToClassList(o.m_Collapsed ? "arrow-right" : "arrow-down");
                fold.Add(arrow);
                header.Add(fold);
                c.Add(header);
            }
            if (content != null)
                c.Add(content);
            return c;
        }

        Element Grip(Overlay o, string cls)
        {
            var g = new Element { name = "overlay-grip" };
            g.AddToClassList("overlay-grip");
            g.AddToClassList(cls);
            g.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0)
                    return;
                m_Dragged = o;
                m_DragFrom = new Vector2(e.position.x, e.position.y);
                Rect r = m_Containers.TryGetValue(o, out Element c) ? c.worldBound : new Rect(m_DragFrom.x, m_DragFrom.y, 0, 0);
                m_GrabOffset = m_DragFrom - new Vector2(r.x, r.y);
                m_DragMoved = false;
                g.CapturePointer(e.pointerId);
                e.StopPropagation();
            });
            g.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (m_Dragged != o)
                    return;
                var p = new Vector2(e.position.x, e.position.y);
                if (!m_DragMoved && (p - m_DragFrom).magnitude > kDragStart)
                    m_DragMoved = true;
                e.StopPropagation();
            });
            g.RegisterCallback<PointerUpEvent>(e =>
            {
                if (m_Dragged != o)
                    return;
                g.ReleasePointer(e.pointerId);
                m_Dragged = null;
                if (m_DragMoved)
                    DropInternal(o, new Vector2(e.position.x, e.position.y), m_GrabOffset);
                e.StopPropagation();
            });
            return g;
        }

        /// <summary>An overlay dropped at a panel point: the zone under it (the top row, a side's edge, the bottom
        /// edge), else floating there (the grab offset kept).</summary>
        internal void DropInternal(Overlay o, Vector2 p, Vector2 grabOffset)
        {
            Rect h = m_Host.worldBound;
            if (m_Top != null && m_Top.worldBound.Contains(p) && o.toolbarInternal)
            {
                DockPosition pos = p.x > m_Top.worldBound.center.x ? DockPosition.Bottom : DockPosition.Top;
                MoveInternal(o, DockZone.TopToolbar, pos, IndexAt(DockZone.TopToolbar, pos, p, false), o.m_FloatingPosition);
                return;
            }
            if (h.Contains(p))
            {
                if (p.x < h.x + kEdge)
                {
                    DockZone z = o.toolbarInternal ? DockZone.LeftToolbar : DockZone.LeftColumn;
                    MoveInternal(o, z, DockPosition.Top, IndexAt(z, DockPosition.Top, p, true), o.m_FloatingPosition);
                    return;
                }
                if (p.x > h.xMax - kEdge)
                {
                    DockZone z = o.toolbarInternal ? DockZone.RightToolbar : DockZone.RightColumn;
                    MoveInternal(o, z, DockPosition.Top, IndexAt(z, DockPosition.Top, p, true), o.m_FloatingPosition);
                    return;
                }
                if (p.y > h.yMax - kEdge && o.toolbarInternal)
                {
                    MoveInternal(o, DockZone.BottomToolbar, DockPosition.Top, IndexAt(DockZone.BottomToolbar, DockPosition.Top, p, false), o.m_FloatingPosition);
                    return;
                }
            }
            Vector2 at = p - grabOffset - new Vector2(h.x, h.y);
            at.x = Math.Clamp(at.x, 0, Math.Max(0, h.width - 20));
            at.y = Math.Clamp(at.y, 0, Math.Max(0, h.height - 20));
            MoveInternal(o, DockZone.Floating, DockPosition.Top, int.MaxValue, at);
        }

        // The order a drop at a point takes among a zone's overlays (by their middles along the zone).
        int IndexAt(DockZone zone, DockPosition pos, Vector2 p, bool vertical)
        {
            int index = 0;
            foreach (Overlay other in Sorted(zone, pos))
                if (m_Containers.TryGetValue(other, out Element c))
                {
                    Vector2 mid = c.worldBound.center;
                    if ((vertical ? p.y : p.x) > (vertical ? mid.y : mid.x))
                        index++;
                }
            return index;
        }

        List<Overlay> Sorted(DockZone zone, DockPosition pos)
        {
            var list = m_Overlays.FindAll(x => x.m_Displayed && x.m_Zone == zone && (zone != DockZone.TopToolbar || x.m_Position == pos));
            list.Sort((a, b) => a.m_Order.CompareTo(b.m_Order));
            return list;
        }

        /// <summary>An overlay to a zone at an index among its overlays (the others renumbered), or floating at a place.</summary>
        internal void MoveInternal(Overlay o, DockZone zone, DockPosition pos, int index, Vector2 floatingAt)
        {
            if (!o.toolbarInternal && IsToolbarZone(zone))
                zone = zone == DockZone.LeftToolbar ? DockZone.LeftColumn : zone == DockZone.RightToolbar ? DockZone.RightColumn : DockZone.Floating;
            List<Overlay> list = Sorted(zone, pos);
            list.Remove(o);
            list.Insert(Math.Clamp(index, 0, list.Count), o);
            for (int i = 0; i < list.Count; i++)
                list[i].m_Order = i;
            o.m_Zone = zone;
            o.m_Position = pos;
            o.m_FloatingPosition = floatingAt;
            o.m_Displayed = true;
            RebuildInternal();
            Save();
        }

        /// <summary>Where an overlay is now in its view (its container's corner).</summary>
        internal Vector2 PlaceOfInternal(Overlay o)
        {
            if (!m_Containers.TryGetValue(o, out Element c))
                return o.m_FloatingPosition;
            Rect r = c.worldBound, h = m_Host.worldBound;
            return new Vector2(Math.Max(0, r.x - h.x), Math.Max(0, r.y - h.y));
        }

        internal Element ContainerInternal(Overlay o) => m_Containers.TryGetValue(o, out Element c) ? c : null;

        // ---------------------------------------------------------------------------------------- the menu

        internal void ShowMenu(Rect at)
        {
            var menu = new GenericDropdownMenu();
            foreach (Overlay o in m_Overlays)
            {
                Overlay x = o;
                menu.AddItem(o.displayName, o.displayed, () =>
                {
                    x.displayed = !x.displayed;
                    Save();
                });
            }
            menu.AddSeparator("");
            menu.AddItem("Reset", false, ResetInternal);
            menu.DropDown(at, m_Top ?? m_Host, DropdownMenuSizeMode.Auto);
        }

        // --------------------------------------------------------------------------------------- the state

        // The layout file written now (the canvases' states go in as it is written).
        internal void Save() => DockManager.Saved();

        /// <summary>Every canvas' state into the layout (as it is saved).</summary>
        internal static void CollectStatesInternal()
        {
            foreach (OverlayCanvas c in s_Canvases)
                DockManager.SetStateInternal(c.StateKey, c.StateText());
        }

        internal string StateText()
        {
            var sb = new StringBuilder();
            foreach (Overlay o in m_Overlays)
            {
                if (sb.Length > 0)
                    sb.Append(';');
                sb.Append(Uri.EscapeDataString(o.id)).Append('=')
                  .Append(o.m_Displayed ? '1' : '0').Append(',')
                  .Append(o.m_Collapsed ? '1' : '0').Append(',')
                  .Append((int)o.m_Zone).Append(',')
                  .Append((int)o.m_Position).Append(',')
                  .Append(o.m_Order).Append(',')
                  .Append(o.m_FloatingPosition.x.ToString("0.#", CultureInfo.InvariantCulture)).Append(',')
                  .Append(o.m_FloatingPosition.y.ToString("0.#", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        void ApplyState(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            foreach (string entry in text.Split(';'))
            {
                int eq = entry.IndexOf('=');
                if (eq <= 0)
                    continue;
                Overlay o = Find(Uri.UnescapeDataString(entry.Substring(0, eq)));
                string[] v = entry.Substring(eq + 1).Split(',');
                if (o == null || v.Length < 7)
                    continue;
                o.m_Displayed = v[0] == "1";
                o.m_Collapsed = v[1] == "1";
                if (int.TryParse(v[2], out int zone) && Enum.IsDefined(typeof(DockZone), zone))
                    o.m_Zone = (DockZone)zone;
                if (!o.toolbarInternal && IsToolbarZone(o.m_Zone))
                    o.m_Zone = DockZone.Floating;
                o.m_Position = v[3] == "1" ? DockPosition.Bottom : DockPosition.Top;
                if (int.TryParse(v[4], out int order))
                    o.m_Order = order;
                if (float.TryParse(v[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                    float.TryParse(v[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                    o.m_FloatingPosition = new Vector2(x, y);
            }
        }

        /// <summary>A layout loaded: every canvas takes its state from it (defaults where it has none).</summary>
        internal static void LayoutLoadedInternal()
        {
            foreach (OverlayCanvas c in s_Canvases.ToArray())
            {
                for (int i = 0; i < c.m_Overlays.Count; i++)
                    Defaults(c.m_Overlays[i], i);
                c.ApplyState(DockManager.StateOf(c.StateKey));
                c.RebuildInternal();
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HelixEngine;

namespace HelixEditor.Docking
{
    // The layout of the editor's windows as data: a tree of splits (side by side or stacked, each child's share) and dock
    // areas (tabs by window type, the selected one), floating containers with their rectangles, and the area shown
    // maximized. Its text form (a layout file):
    //   split row 0.2 0.6 0.2       a split of three children side by side ("column": stacked), their shares
    //     area 0 Hierarchy          an area: the selected tab's index, then the tabs' window types
    //     ...
    //   float 120 80 420 320        a floating container (x y width height) with its tree under it
    //     area 0 Console
    // Children are indented by two spaces under their split or container.
    public abstract class DockNode
    {
        internal DockSplit parent;
    }

    public sealed class DockSplit : DockNode
    {
        public bool column;
        public readonly List<DockNode> children = new();
        public readonly List<float> shares = new();

        public void Add(DockNode node, float share, int index = -1)
        {
            if (index < 0 || index > children.Count)
                index = children.Count;
            node.parent = this;
            children.Insert(index, node);
            shares.Insert(index, share);
        }
    }

    public sealed class DockArea : DockNode
    {
        public readonly List<string> tabs = new();
        public int selected;

        public string selectedTab => selected >= 0 && selected < tabs.Count ? tabs[selected] : null;
    }

    public sealed class FloatingContainer
    {
        public Rect rect;
        public DockNode root;
    }

    /// <summary>Where a dragged tab goes on an area.</summary>
    public enum DropZone
    {
        Center,
        Left,
        Right,
        Top,
        Bottom,
    }

    public sealed class DockLayout
    {
        public DockNode root;
        public readonly List<FloatingContainer> floating = new();
        public DockArea maximized;

        // ------------------------------------------------------------------------------------------ queries

        public IEnumerable<DockArea> Areas()
        {
            foreach (DockArea a in AreasOf(root))
                yield return a;
            foreach (FloatingContainer f in floating)
                foreach (DockArea a in AreasOf(f.root))
                    yield return a;
        }

        static IEnumerable<DockArea> AreasOf(DockNode node)
        {
            if (node is DockArea area)
                yield return area;
            else if (node is DockSplit split)
                foreach (DockNode c in split.children)
                    foreach (DockArea a in AreasOf(c))
                        yield return a;
        }

        public DockArea AreaOf(string tab)
        {
            foreach (DockArea a in Areas())
                if (a.tabs.Contains(tab))
                    return a;
            return null;
        }

        public FloatingContainer ContainerOf(DockNode node)
        {
            DockNode top = node;
            while (top.parent != null)
                top = top.parent;
            foreach (FloatingContainer f in floating)
                if (f.root == top)
                    return f;
            return null;
        }

        // ------------------------------------------------------------------------------------- operations

        /// <summary>The tab out of its area (an emptied area goes, a split of one child is replaced by it).</summary>
        public bool RemoveTab(string tab)
        {
            DockArea area = AreaOf(tab);
            if (area == null)
                return false;
            int index = area.tabs.IndexOf(tab);
            area.tabs.RemoveAt(index);
            if (area.selected >= area.tabs.Count || index < area.selected)
                area.selected = Math.Max(0, area.selected - 1);
            if (area.tabs.Count == 0)
                RemoveNode(area);
            return true;
        }

        void RemoveNode(DockNode node)
        {
            if (maximized == node)
                maximized = null;
            DockSplit split = node.parent;
            if (split == null)
            {
                FloatingContainer f = ContainerOf(node);
                if (f != null)
                    floating.Remove(f);
                else if (root == node)
                    root = null;
                return;
            }
            int i = split.children.IndexOf(node);
            float share = split.shares[i];
            split.children.RemoveAt(i);
            split.shares.RemoveAt(i);
            // Its share to its neighbors.
            if (split.children.Count > 0)
            {
                int n = Math.Min(i, split.children.Count - 1);
                split.shares[n] += share;
            }
            if (split.children.Count == 1)
                Replace(split, split.children[0]);
            else if (split.children.Count == 0)
                RemoveNode(split);
        }

        void Replace(DockNode old, DockNode with)
        {
            DockSplit parent = old.parent;
            with.parent = parent;
            if (parent != null)
            {
                int i = parent.children.IndexOf(old);
                parent.children[i] = with;
                return;
            }
            if (root == old)
                root = with;
            foreach (FloatingContainer f in floating)
                if (f.root == old)
                    f.root = with;
        }

        /// <summary>A tab into an area: in its middle among its tabs (at an index, -1 the end), or at an edge as a new
        /// area beside it, splitting it.</summary>
        public DockArea Dock(string tab, DockArea target, DropZone zone, int index = -1)
        {
            if (target == null)
                return null;
            if (zone == DropZone.Center)
            {
                int from = target.tabs.IndexOf(tab);
                if (from >= 0)
                {
                    target.tabs.RemoveAt(from);
                    if (index > from)
                        index--;
                }
                else
                    RemoveTab(tab);
                if (index < 0 || index > target.tabs.Count)
                    index = target.tabs.Count;
                target.tabs.Insert(index, tab);
                target.selected = index;
                return target;
            }
            if (target.tabs.Count == 1 && target.tabs[0] == tab)
                return target;
            RemoveTab(tab);
            var area = new DockArea();
            area.tabs.Add(tab);
            bool column = zone == DropZone.Top || zone == DropZone.Bottom;
            bool after = zone == DropZone.Right || zone == DropZone.Bottom;
            DockSplit parent = target.parent;
            if (parent != null && parent.column == column)
            {
                // Beside it in its own split, sharing its share.
                int i = parent.children.IndexOf(target);
                float half = parent.shares[i] * 0.5f;
                parent.shares[i] = half;
                parent.Add(area, half, after ? i + 1 : i);
                return area;
            }
            var split = new DockSplit { column = column };
            Replace(target, split);
            split.Add(target, 0.5f);
            split.Add(area, 0.5f, after ? 1 : 0);
            return area;
        }

        /// <summary>A tab out into a floating container of its own.</summary>
        public FloatingContainer Float(string tab, Rect rect)
        {
            RemoveTab(tab);
            var area = new DockArea();
            area.tabs.Add(tab);
            var f = new FloatingContainer { rect = rect, root = area };
            floating.Add(f);
            return f;
        }

        /// <summary>The shares of a split's children moved at a splitter (between child i and i + 1).</summary>
        public static void MoveSplitter(DockSplit split, int i, float delta, float minimum = 0.05f)
        {
            float a = split.shares[i] + delta, b = split.shares[i + 1] - delta;
            if (a < minimum || b < minimum)
                return;
            split.shares[i] = a;
            split.shares[i + 1] = b;
        }

        // ------------------------------------------------------------------------------------ text form

        public string Write()
        {
            var sb = new StringBuilder();
            sb.Append("layout 1\n");
            if (root != null)
                WriteNode(sb, root, 0);
            foreach (FloatingContainer f in floating)
            {
                sb.Append(string.Format(CultureInfo.InvariantCulture, "float {0} {1} {2} {3}\n", f.rect.x, f.rect.y, f.rect.width, f.rect.height));
                WriteNode(sb, f.root, 1);
            }
            return sb.ToString();
        }

        static void WriteNode(StringBuilder sb, DockNode node, int depth)
        {
            sb.Append(' ', depth * 2);
            if (node is DockArea area)
            {
                sb.Append("area ").Append(area.selected);
                foreach (string t in area.tabs)
                    sb.Append(' ').Append(t);
                sb.Append('\n');
                return;
            }
            var split = (DockSplit)node;
            sb.Append("split ").Append(split.column ? "column" : "row");
            foreach (float s in split.shares)
                sb.Append(' ').Append(s.ToString("0.####", CultureInfo.InvariantCulture));
            sb.Append('\n');
            foreach (DockNode c in split.children)
                WriteNode(sb, c, depth + 1);
        }

        public static DockLayout Read(string text)
        {
            var layout = new DockLayout();
            // The open splits by depth (a node's parent is the split one level up).
            var stack = new List<(int depth, DockSplit split)>();
            FloatingContainer current = null;
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                if (raw.Trim().Length == 0 || raw.StartsWith("layout"))
                    continue;
                int depth = 0;
                while (depth < raw.Length && raw[depth] == ' ')
                    depth++;
                depth /= 2;
                string[] parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts[0] == "float" && parts.Length >= 5)
                {
                    current = new FloatingContainer
                    {
                        rect = new Rect(F(parts[1]), F(parts[2]), F(parts[3]), F(parts[4])),
                    };
                    layout.floating.Add(current);
                    stack.Clear();
                    continue;
                }
                DockNode node;
                if (parts[0] == "area")
                {
                    var area = new DockArea { selected = parts.Length > 1 ? (int)F(parts[1]) : 0 };
                    for (int i = 2; i < parts.Length; i++)
                        area.tabs.Add(parts[i]);
                    node = area;
                }
                else if (parts[0] == "split")
                {
                    var split = new DockSplit { column = parts.Length > 1 && parts[1] == "column" };
                    node = split;
                }
                else
                    continue;
                while (stack.Count > 0 && stack[stack.Count - 1].depth >= depth)
                    stack.RemoveAt(stack.Count - 1);
                if (stack.Count > 0)
                {
                    // The parent's shares came with its own line; a child past them gets an even one.
                    DockSplit parent = stack[stack.Count - 1].split;
                    node.parent = parent;
                    parent.children.Add(node);
                    if (parent.shares.Count < parent.children.Count)
                        parent.shares.Add(1);
                }
                else if (current != null)
                    current.root = node;
                else
                    layout.root = node;
                if (node is DockSplit s)
                {
                    // Its shares now; its children take them in order.
                    for (int i = 2; i < parts.Length; i++)
                        s.shares.Add(F(parts[i]));
                    stack.Add((depth, s));
                }
            }
            return layout;
        }

        static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}

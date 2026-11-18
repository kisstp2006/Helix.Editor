using System;
using HelixEngine;

namespace HelixEditor
{
    public static partial class Handles
    {
        // The Rect tool's handle: a rectangle in the frame's x y plane about the center, its corners and edges dragged
        // (the opposite ones stay), its inside dragged to move it; the new center and size returned.
        internal static void RectHandleInternal(ref Vector3 center, ref Vector2 size, Quaternion frame)
        {
            Event e = Event.current;
            if (e == null)
                return;
            Vector3 right = frame * Vector3.right, up = frame * Vector3.up, normal = frame * Vector3.forward;
            float dot = HandleUtility.GetHandleSize(center) * 0.04f;
            // The inside (drawn first, under the dots), then the eight dots (corners, then edges) as signs along right and up.
            for (int k = 0; k < 9; k++)
            {
                int i = (k + 8) % 9;
                int sx = i < 8 ? kRectSigns[i * 2] : 0, sy = i < 8 ? kRectSigns[i * 2 + 1] : 0;
                int id = Id(s_RectHint);
                Vector3 at = center + right * (sx * size.x * 0.5f) + up * (sy * size.y * 0.5f);
                switch (e.GetTypeForControl(id))
                {
                case EventType.Layout:
                case EventType.MouseMove:
                    if (i < 8)
                        HandleUtility.AddControl(id, HandleUtility.DistanceToCircle(at, dot * 1.5f));
                    else
                        HandleUtility.AddControl(id, HandleUtility.DistanceToRectangleInternal(center, frame, size * 0.5f) + 1);
                    break;
                case EventType.MouseDown:
                    if (Press(e, id))
                    {
                        s_StartPosition = center;
                        s_StartSize = size;
                        HandleUtility.RayPlane(HandleUtility.GUIPointToWorldRay(e.mousePosition), W(center), matrix.MultiplyVector(normal).normalized, out s_StartHit);
                    }
                    break;
                case EventType.MouseDrag:
                    if (GuiUtility.hotControl == id)
                    {
                        if (HandleUtility.RayPlane(HandleUtility.GUIPointToWorldRay(e.mousePosition), s_StartHit, matrix.MultiplyVector(normal).normalized, out Vector3 hit))
                        {
                            Vector3 d = matrix.inverse.MultiplyVector(hit - s_StartHit);
                            if (EditorSnapSettings.incrementalSnapActive)
                                d = SnapValue(d, EditorSnapSettings.move);
                            float dx = Vector3.Dot(d, right), dy = Vector3.Dot(d, up);
                            Vector2 s = s_StartSize;
                            Vector3 c = s_StartPosition;
                            if (i == 8)
                                c += right * dx + up * dy;
                            else
                            {
                                // The dragged edge moves; the opposite one stays.
                                if (sx != 0)
                                {
                                    s.x = Math.Max(0, s_StartSize.x + sx * dx);
                                    c += right * (sx * (s.x - s_StartSize.x) * 0.5f);
                                }
                                if (sy != 0)
                                {
                                    s.y = Math.Max(0, s_StartSize.y + sy * dy);
                                    c += up * (sy * (s.y - s_StartSize.y) * 0.5f);
                                }
                            }
                            if (c != center || s != size)
                            {
                                center = c;
                                size = s;
                                Gui.changed = true;
                            }
                        }
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    Release(e, id);
                    break;
                case EventType.Repaint:
                    if (i < 8)
                    {
                        Color was = color;
                        color = Shade(id, new Color(0.13f, 0.53f, 1f, 1));
                        DotHandleCap(id, at, frame, dot, EventType.Repaint);
                        color = was;
                    }
                    else
                    {
                        Vector3 r = right * size.x * 0.5f, u = up * size.y * 0.5f;
                        Color line = GuiUtility.hotControl == id ? selectedColor : new Color(0.9f, 0.9f, 0.9f, 0.9f);
                        DrawSolidRectangleWithOutline(new[] { center - r - u, center + r - u, center + r + u, center - r + u }, new Color(1, 1, 1, 0.03f), line);
                    }
                    break;
                }
            }
        }

        static readonly int[] kRectSigns = { -1, -1, 1, -1, 1, 1, -1, 1, 0, -1, 1, 0, 0, 1, -1, 0 };
    }
}

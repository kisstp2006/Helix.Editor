using System;
using System.Collections.Generic;
using HelixEngine;
using HelixEngine.Rendering;
using Object = HelixEngine.Object;

namespace HelixEditor
{
    // The scene view's id pass: the edited scenes' mesh renderers drawn with Hidden/Editor/ScenePicking into a texture,
    // each (renderer, submesh) as its own color, the nearest one winning; read back to find what is under a point or
    // inside a rectangle. The same drawing with one color makes the selection's mask (its outline).
    internal static class ScenePicking
    {
        internal struct Hit
        {
            public Renderer renderer;
            public int submesh;
        }

        static Material s_Material;
        static MaterialPropertyBlock s_Block;
        static RenderTexture s_Target;
        static readonly List<Hit> s_Drawn = new();
        static Color32[] s_Pixels = Array.Empty<Color32>();
        static int s_Width, s_Height;

        static Material PickMaterial()
        {
            if (s_Material == null)
            {
                Shader shader = Shader.Find("Hidden/Editor/ScenePicking");
                if (shader == null)
                    return null;
                s_Material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            return s_Material;
        }

        static RenderTexture Target(RenderTexture rt, int w, int h)
        {
            if (rt != null && rt.width == w && rt.height == h)
                return rt;
            if (rt != null)
            {
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { hideFlags = HideFlags.HideAndDontSave };
            rt.Create();
            return rt;
        }

        // The renderers the view shows: the edited scenes', active, with a mesh; those of ignore (and their children) left out.
        internal static List<(Renderer renderer, Mesh mesh)> Renderers(ICollection<GameObject> ignore, ICollection<GameObject> only)
        {
            var scenes = new HashSet<HelixEngine.SceneManagement.Scene>();
            for (int i = 0; i < HelixEngine.SceneManagement.SceneManager.sceneCount; i++)
                scenes.Add(HelixEngine.SceneManagement.SceneManager.GetSceneAt(i));
            var list = new List<(Renderer, Mesh)>();
            foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled)
                    continue;
                GameObject go = r.gameObject;
                if (!go.activeInHierarchy || (go.hideFlags & HideFlags.HideInHierarchy) != 0 || !scenes.Contains(go.scene))
                    continue;
                if (ignore != null && ignore.Count > 0 && Under(go.transform, ignore))
                    continue;
                if (only != null && !Under(go.transform, only))
                    continue;
                Mesh mesh = go.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh != null)
                    list.Add((r, mesh));
            }
            return list;
        }

        static bool Under(Transform t, ICollection<GameObject> set)
        {
            for (; t != null; t = t.parent)
                if (set.Contains(t.gameObject))
                    return true;
            return false;
        }

        static Color IdColor(int id) => new Color((id & 255) / 255f, ((id >> 8) & 255) / 255f, ((id >> 16) & 255) / 255f, 1);

        /// <summary>The renderers into a target of the camera's size through its matrices: each its id color, or all
        /// one color (a mask). False when the shader or the camera is missing.</summary>
        internal static bool Draw(Camera camera, RenderTexture target, List<(Renderer renderer, Mesh mesh)> renderers, Color? single)
        {
            Material material = PickMaterial();
            if (material == null || camera == null || target == null)
                return false;
            s_Block ??= new MaterialPropertyBlock();
            using var cb = new CommandBuffer { name = "Scene picking" };
            cb.SetRenderTarget(target);
            cb.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
            cb.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
            if (single == null)
                s_Drawn.Clear();
            foreach ((Renderer r, Mesh mesh) in renderers)
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Color c = single ?? IdColor(s_Drawn.Count + 1);
                    if (single == null)
                        s_Drawn.Add(new Hit { renderer = r, submesh = sub });
                    s_Block.Clear();
                    s_Block.SetColor("_PickColor", c);
                    cb.DrawMesh(mesh, r.localToWorldMatrix, material, sub, 0, s_Block);
                }
            Graphics.ExecuteCommandBuffer(cb);
            return true;
        }

        // The id pass of the camera, read back.
        static bool Render(Camera camera, ICollection<GameObject> ignore)
        {
            if (camera == null)
                return false;
            int w = Math.Max(1, camera.pixelWidth), h = Math.Max(1, camera.pixelHeight);
            s_Target = Target(s_Target, w, h);
            if (!Draw(camera, s_Target, Renderers(ignore, null), null))
                return false;
            AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(s_Target);
            request.WaitForCompletion();
            if (request.hasError)
                return false;
            s_Pixels = request.GetData<Color32>().ToArray();
            s_Width = w;
            s_Height = h;
            return s_Pixels.Length >= w * h;
        }

        // The id at a GUI point (y down); 0 for none.
        static int IdAt(int x, int y)
        {
            if (x < 0 || y < 0 || x >= s_Width || y >= s_Height)
                return 0;
            Color32 c = s_Pixels[(s_Height - 1 - y) * s_Width + x];
            return c.r | (c.g << 8) | (c.b << 16);
        }

        static Hit? HitOf(int id) => id > 0 && id <= s_Drawn.Count ? s_Drawn[id - 1] : null;

        /// <summary>The renderer and submesh at a GUI point of the camera's view (nothing: null).</summary>
        internal static Hit? Pick(Camera camera, Vector2 point, ICollection<GameObject> ignore)
        {
            if (!Render(camera, ignore))
                return null;
            return HitOf(IdAt((int)Math.Floor(point.x), (int)Math.Floor(point.y)));
        }

        /// <summary>The renderers with a pixel inside a GUI rectangle of the camera's view.</summary>
        internal static List<Renderer> PickRect(Camera camera, Rect rect)
        {
            var found = new List<Renderer>();
            if (!Render(camera, null))
                return found;
            var seen = new HashSet<int>();
            int x0 = Math.Max(0, (int)Math.Floor(rect.xMin)), x1 = Math.Min(s_Width - 1, (int)Math.Ceiling(rect.xMax));
            int y0 = Math.Max(0, (int)Math.Floor(rect.yMin)), y1 = Math.Min(s_Height - 1, (int)Math.Ceiling(rect.yMax));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int id = IdAt(x, y);
                    if (id != 0 && seen.Add(id) && HitOf(id) is Hit hit && !found.Contains(hit.renderer))
                        found.Add(hit.renderer);
                }
            return found;
        }

        // ------------------------------------------------------------------------------------------- canvases

        /// <summary>The smallest canvas rect (a RectTransform under a Canvas) under a GUI point, or null.</summary>
        internal static GameObject CanvasRectAt(Vector2 point, ICollection<GameObject> ignore)
        {
            GameObject best = null;
            float bestArea = float.MaxValue;
            var corners = new Vector3[4];
            foreach (RectTransform rt in Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None))
            {
                GameObject go = rt.gameObject;
                if (!go.activeInHierarchy || (go.hideFlags & HideFlags.HideInHierarchy) != 0 || rt.GetComponentInParent<Canvas>() == null)
                    continue;
                if (ignore != null && ignore.Contains(go))
                    continue;
                rt.GetWorldCorners(corners);
                var p = new Vector2[4];
                bool behind = false;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 g = HandleUtility.WorldToGUIPointWithDepth(corners[i]);
                    behind |= g.z <= 0;
                    p[i] = new Vector2(g.x, g.y);
                }
                if (behind || !Inside(point, p))
                    continue;
                float area = Math.Abs((p[2].x - p[0].x) * (p[1].y - p[3].y) - (p[1].x - p[3].x) * (p[2].y - p[0].y)) * 0.5f;
                if (area < bestArea)
                {
                    bestArea = area;
                    best = go;
                }
            }
            return best;
        }

        static bool Inside(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }
    }
}

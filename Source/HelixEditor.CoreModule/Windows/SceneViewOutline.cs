using System.Collections.Generic;
using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor
{
    // The selection's outline: the selected objects' renderers (their children's too) drawn white into a mask through
    // the view's camera, its edge drawn by Hidden/Editor/SceneOutline into a layer of the viewport over the camera's
    // image (under the scene GUI); hidden while nothing with a renderer is selected.
    public partial class SceneView
    {
        static readonly Color kOutlineColor = new Color(1f, 0.4f, 0f, 1f);

        Element m_OutlineLayer;
        RenderTexture m_Mask, m_Outline;
        Material m_OutlineMaterial;
        bool m_OutlineShown;

        internal RenderTexture outlineInternal => m_OutlineShown ? m_Outline : null;

        void InitOutline()
        {
            m_OutlineLayer = new Element { name = "scene-outline", pickingMode = PickingMode.Ignore };
            m_OutlineLayer.style.position = Position.Absolute;
            m_OutlineLayer.style.left = 0;
            m_OutlineLayer.style.top = 0;
            m_OutlineLayer.style.right = 0;
            m_OutlineLayer.style.bottom = 0;
            m_OutlineLayer.style.display = DisplayStyle.None;
            m_Viewport.Insert(0, m_OutlineLayer);
        }

        void ReleaseOutline()
        {
            foreach (RenderTexture rt in new[] { m_Mask, m_Outline })
                if (rt != null)
                {
                    rt.Release();
                    DestroyImmediate(rt);
                }
            m_Mask = m_Outline = null;
            if (m_OutlineMaterial != null)
                DestroyImmediate(m_OutlineMaterial);
            m_OutlineMaterial = null;
        }

        static RenderTexture Sized(RenderTexture rt, int w, int h, int depth, string name)
        {
            if (rt != null && rt.width == w && rt.height == h)
                return rt;
            if (rt != null)
            {
                rt.Release();
                DestroyImmediate(rt);
            }
            rt = new RenderTexture(w, h, depth, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = name, hideFlags = HideFlags.HideAndDontSave };
            rt.Create();
            return rt;
        }

        void UpdateOutline(int w, int h)
        {
            if (m_OutlineLayer == null)
                return;
            var selected = new HashSet<GameObject>(Selection.gameObjects);
            List<(Renderer renderer, Mesh mesh)> renderers = selected.Count > 0 ? ScenePicking.Renderers(null, selected) : null;
            bool show = renderers != null && renderers.Count > 0;
            if (show)
            {
                if (m_OutlineMaterial == null && Shader.Find("Hidden/Editor/SceneOutline") is Shader shader)
                {
                    m_OutlineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    m_OutlineMaterial.SetColor("_OutlineColor", kOutlineColor);
                    m_OutlineMaterial.SetFloat("_OutlineWidth", 2);
                }
                bool newTarget = m_Outline == null || m_Outline.width != w || m_Outline.height != h;
                m_Mask = Sized(m_Mask, w, h, 24, "SceneMask");
                m_Outline = Sized(m_Outline, w, h, 0, "SceneOutline");
                show = m_OutlineMaterial != null && ScenePicking.Draw(m_Camera, m_Mask, renderers, Color.white);
                if (show)
                {
                    Graphics.Blit(m_Mask, m_Outline, m_OutlineMaterial);
                    if (newTarget || !m_OutlineShown)
                        m_OutlineLayer.style.backgroundImage = Background.FromRenderTexture(m_Outline);
                }
            }
            if (show != m_OutlineShown)
            {
                m_OutlineShown = show;
                m_OutlineLayer.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}

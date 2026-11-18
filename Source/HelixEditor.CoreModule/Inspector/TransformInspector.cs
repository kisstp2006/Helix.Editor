using System;
using HelixEditor.SceneManagement;
using HelixEngine;
using HelixEngine.UI;

namespace HelixEditor
{
    /// <summary>The Transform's inspector: its local position, rotation (as euler angles, the ones typed kept while the
    /// rotation stays theirs) and scale, written through the Transform so its children follow.</summary>
    [CustomEditor(typeof(Transform))]
    [CanEditMultipleObjects]
    internal sealed class TransformInspector : Editor
    {
        // The angles last shown or typed: a rotation read back as other angles (370 as 10) keeps them while it is theirs.
        Vector3 m_Euler;
        Quaternion m_EulerOf;

        Transform First => target as Transform;

        Vector3 Euler(Transform t)
        {
            if (t == First && Quaternion.Angle(m_EulerOf, t.localRotation) < 1e-3f)
                return m_Euler;
            Vector3 e = t.localEulerAngles;
            // Shown from -180 to 180 rather than 0 to 360.
            e = new Vector3(Wrap(e.x), Wrap(e.y), Wrap(e.z));
            if (t == First)
            {
                m_Euler = e;
                m_EulerOf = t.localRotation;
            }
            return e;
        }

        static float Wrap(float a)
        {
            a %= 360f;
            if (a > 180f)
                a -= 360f;
            else if (a < -180f)
                a += 360f;
            return (float)Math.Round(a, 4);
        }

        void Write(Action<Transform> change)
        {
            foreach (var o in targets)
                if (o is Transform t)
                {
                    change(t);
                    EditorUtility.SetDirty(t);
                    EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
                }
        }

        void SetEuler(Vector3 e)
        {
            Write(t => t.localEulerAngles = e);
            m_Euler = e;
            if (First != null)
                m_EulerOf = First.localRotation;
        }

        public override void OnInspectorGui()
        {
            Transform t = First;
            if (t == null)
                return;
            EditorGui.BeginChangeCheck();
            Vector3 p = EditorGuiLayout.Vector3Field("Position", t.localPosition);
            if (EditorGui.EndChangeCheck())
                Write(x => x.localPosition = p);
            EditorGui.BeginChangeCheck();
            Vector3 r = EditorGuiLayout.Vector3Field("Rotation", Euler(t));
            if (EditorGui.EndChangeCheck())
                SetEuler(r);
            EditorGui.BeginChangeCheck();
            Vector3 s = EditorGuiLayout.Vector3Field("Scale", t.localScale);
            if (EditorGui.EndChangeCheck())
                Write(x => x.localScale = s);
        }

        public override Element CreateInspectorGui()
        {
            Transform t = First;
            if (t == null)
                return null;
            var root = new Element { name = "transform-inspector" };
            var position = new Vector3Field("Position") { name = "transform-position" };
            var rotation = new Vector3Field("Rotation") { name = "transform-rotation" };
            var scale = new Vector3Field("Scale") { name = "transform-scale" };
            position.SetValueWithoutNotify(t.localPosition);
            rotation.SetValueWithoutNotify(Euler(t));
            scale.SetValueWithoutNotify(t.localScale);
            position.RegisterValueChangedCallback(e => Write(x => x.localPosition = e.newValue));
            rotation.RegisterValueChangedCallback(e => SetEuler(e.newValue));
            scale.RegisterValueChangedCallback(e => Write(x => x.localScale = e.newValue));
            root.Add(position);
            root.Add(rotation);
            root.Add(scale);
            // The fields follow the Transform's own changes (a tool's drag, a script).
            root.schedule.Execute(() =>
            {
                Transform now = First;
                if (now == null)
                    return;
                if (position.value != now.localPosition)
                    position.SetValueWithoutNotify(now.localPosition);
                Vector3 e = Euler(now);
                if (rotation.value != e)
                    rotation.SetValueWithoutNotify(e);
                if (scale.value != now.localScale)
                    scale.SetValueWithoutNotify(now.localScale);
            }).Every(100);
            return root;
        }
    }
}

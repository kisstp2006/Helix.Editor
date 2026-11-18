using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HelixEditor.SceneManagement;
using HelixEngine;
using HelixEngine.Internal;
using HelixEngine.Rendering;

namespace HelixEditor
{
    // The components a pipeline puts beside others (Data/Settings/Pipelines.json's companions: the physical pipeline's
    // light data beside each Light, as the reference's pipeline adds its own), with their defaults by the other
    // component's properties (a directional light's sun in lux). Added as the editor adds a component and to a new
    // scene's objects, for the pipeline the editor runs.
    internal static class PipelineCompanions
    {
        sealed class Rule
        {
            public string of, add;
            public List<(Dictionary<string, string> when, Dictionary<string, JsonElement> values)> defaults = new();
        }

        static Dictionary<string, List<Rule>> s_Rules;
        static Dictionary<string, string> s_PipelineNames;

#pragma warning disable CA2255
        [ModuleInitializer]
#pragma warning restore CA2255
        internal static void Init() => ScriptBridge.WhenBindingsLoaded(() => EditorSceneManager.newSceneCreated += (scene, setup, mode) => AddForScene(scene));

        static void Load()
        {
            if (s_Rules != null || string.IsNullOrEmpty(EditorApplication.editorDataPathInternal))
                return;
            s_Rules = new Dictionary<string, List<Rule>>();
            s_PipelineNames = new Dictionary<string, string>();
            try
            {
                string file = System.IO.Path.Combine(EditorApplication.editorDataPathInternal, "Settings/Pipelines.json");
                using JsonDocument doc = JsonDocument.Parse(System.IO.File.ReadAllText(file));
                foreach (JsonProperty p in doc.RootElement.GetProperty("pipelines").EnumerateObject())
                    if (!string.IsNullOrEmpty(p.Value.GetString()))
                        s_PipelineNames[p.Value.GetString()] = p.Name;
                if (!doc.RootElement.TryGetProperty("companions", out JsonElement companions))
                    return;
                foreach (JsonProperty pipeline in companions.EnumerateObject())
                {
                    var rules = new List<Rule>();
                    foreach (JsonElement r in pipeline.Value.EnumerateArray())
                    {
                        var rule = new Rule { of = r.GetProperty("of").GetString(), add = r.GetProperty("add").GetString() };
                        if (r.TryGetProperty("defaults", out JsonElement defaults))
                            foreach (JsonElement d in defaults.EnumerateArray())
                            {
                                var when = new Dictionary<string, string>();
                                if (d.TryGetProperty("when", out JsonElement w))
                                    foreach (JsonProperty c in w.EnumerateObject())
                                        when[c.Name] = c.Value.ToString();
                                var values = new Dictionary<string, JsonElement>();
                                foreach (JsonProperty v in d.GetProperty("values").EnumerateObject())
                                    values[v.Name] = v.Value.Clone();
                                rule.defaults.Add((when, values));
                            }
                        rules.Add(rule);
                    }
                    s_Rules[pipeline.Name] = rules;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Settings/Pipelines.json's companions could not be read: {e.Message}");
            }
        }

        // The rules of the pipeline the editor runs (by its asset's class in the pipelines table).
        // For tests: the pipeline's short name the rules are taken for (null: the one the editor runs).
        internal static string pipelineOverrideInternal;

        static List<Rule> Current()
        {
            Load();
            if (s_Rules == null)
                return null;
            string name = pipelineOverrideInternal;
            RenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline;
            if (name == null && (asset == null || !s_PipelineNames.TryGetValue(asset.GetType().FullName ?? "", out name)))
                return null;
            return s_Rules.TryGetValue(name, out List<Rule> rules) ? rules : null;
        }

        static Type TypeOf(string name)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetType(name, false) is Type t)
                    return t;
            return null;
        }

        /// <summary>The companions of a component the editor added.</summary>
        internal static void AddFor(Component c)
        {
            List<Rule> rules = Current();
            if (rules == null || c == null)
                return;
            foreach (Rule rule in rules)
            {
                Type of = TypeOf(rule.of), add = TypeOf(rule.add);
                if (of == null || add == null || !of.IsInstanceOfType(c) || c.gameObject.GetComponent(add) != null)
                    continue;
                Component made = c.gameObject.AddComponent(add);
                if (made != null)
                    ApplyDefaults(rule, c, made);
            }
        }

        /// <summary>The defaults of a component's companions set again (its properties changed: a light's type).</summary>
        internal static void ReapplyInternal(Component c)
        {
            List<Rule> rules = Current();
            if (rules == null || c == null)
                return;
            foreach (Rule rule in rules)
            {
                Type of = TypeOf(rule.of), add = TypeOf(rule.add);
                if (of != null && add != null && of.IsInstanceOfType(c) && c.gameObject.GetComponent(add) is Component made)
                    ApplyDefaults(rule, c, made);
            }
        }

        static void AddForScene(HelixEngine.SceneManagement.Scene scene)
        {
            if (!scene.IsValid() || Current() == null)
                return;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Component c in root.GetComponentsInChildren<Component>())
                    AddFor(c);
        }

        // The defaults whose conditions (the other component's properties by name) hold, set by name.
        static void ApplyDefaults(Rule rule, Component of, Component made)
        {
            foreach ((Dictionary<string, string> when, Dictionary<string, JsonElement> values) in rule.defaults)
            {
                bool holds = true;
                foreach (var kv in when)
                {
                    object v = of.GetType().GetProperty(kv.Key)?.GetValue(of);
                    holds &= v != null && string.Equals(Convert.ToString(v, CultureInfo.InvariantCulture), kv.Value, StringComparison.Ordinal);
                }
                if (!holds)
                    continue;
                foreach (var kv in values)
                {
                    PropertyInfo p = made.GetType().GetProperty(kv.Key);
                    if (p == null || !p.CanWrite)
                        continue;
                    try
                    {
                        object value = p.PropertyType.IsEnum ? Enum.Parse(p.PropertyType, kv.Value.GetString() ?? "")
                                     : Convert.ChangeType(kv.Value.ToString(), p.PropertyType, CultureInfo.InvariantCulture);
                        p.SetValue(made, value);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Pipelines.json: {rule.add}.{kv.Key} = {kv.Value}: {e.Message}");
                    }
                }
            }
        }
    }
}

using System;
using System.Runtime.CompilerServices;
using HelixEngine.Internal;

namespace HelixEditor
{
    // The pipelines' assemblies (their assets, volumes, components, global settings): referenced by the editor, so they
    // load with it and their classes are the C# types of the pipelines' objects (GraphicsSettings.currentRenderPipeline
    // is the asset's own class); their script classes registered as the editor starts, as a player's scripts are (the
    // pipelines make their global settings objects of them).
    internal static class RenderPipelineAssemblies
    {
        internal static readonly Type[] typesInternal =
        {
            typeof(HelixEngine.Rendering.VolumeProfile),
            typeof(HelixEngine.Rendering.Universal.UniversalRenderPipelineAsset),
            typeof(HelixEngine.Rendering.Physical.PhysicalRenderPipelineAsset),
        };

#pragma warning disable CA2255
        [ModuleInitializer]
#pragma warning restore CA2255
        internal static void Init() => ScriptBridge.WhenBindingsLoaded(() =>
        {
            foreach (Type t in typesInternal)
                ScriptBridge.RegisterAssembly(t.Assembly);
        });
    }
}

using HelixEngine.Builtin;
using HelixEngine.Shaders;

namespace HelixEditor.Shaders;

/// <summary>
/// Hidden/Editor/ScenePicking: a renderer as one flat color, its id (_PickColor, set per draw by a property block),
/// depth tested and written so the nearest one wins. The scene view draws the id texture with it for picking and the
/// selection's mask for its outline.
/// </summary>
[Shader("Hidden/Editor/ScenePicking")]
[Tag("RenderType", "Opaque")]
public sealed class ScenePicking : ShaderDefinition
{
    [Property("Id"), Default(0.0f, 0.0f, 0.0f, 1.0f)] public float4 _PickColor;

    public struct MaterialData
    {
        public float4 _PickColor;
    }

    public struct Varyings
    {
        [Position] public float4 position;
    }

    [Uniform(0)] public static MaterialData material;

    [Pass("Pick", Cull = CullMode.Off, ZWrite = true, ZTest = CompareFunction.LessEqual)]
    public static class Pick
    {
        [VertexShader]
        public static Varyings Vertex([Location(VertexInputs.Position)] float3 position)
        {
            Varyings o;
            o.position = ShaderVariables.ObjectToClip(position);
            return o;
        }

        [FragmentShader]
        [return: Target(0)]
        public static float4 Fragment(Varyings input) => material._PickColor;
    }
}

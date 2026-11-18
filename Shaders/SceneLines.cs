using HelixEngine.Builtin;
using HelixEngine.Shaders;

namespace HelixEditor.Shaders;

/// <summary>
/// Hidden/Editor/SceneLines: the scene view's gizmos and handles, lines and triangles in their vertices' colors,
/// blended over the scene without depth writes; the depth test a property (_ZTest: Always for handles, LessEqual for
/// gizmos and handles drawn with Handles.zTest).
/// </summary>
[Shader("Hidden/Editor/SceneLines")]
[Tag("Queue", "Overlay")]
[Tag("RenderType", "Transparent")]
public sealed class SceneLines : ShaderDefinition
{
    [Property("Depth Test"), Default(8.0f)] public float _ZTest;

    public struct Varyings
    {
        [Position] public float4 position;
        [Location(0)] public float4 color;
    }

    [Pass("SceneLines", LightMode = "SRPDefaultUnlit", Cull = CullMode.Off, ZWrite = false, ZTestProperty = "_ZTest")]
    [Blend(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha)]
    public static class Draw
    {
        [VertexShader]
        public static Varyings Vertex([Location(VertexInputs.Position)] float3 position, [Location(VertexInputs.Color)] float4 color)
        {
            Varyings o;
            o.position = ShaderVariables.ObjectToClip(position);
            o.color = color;
            return o;
        }

        [FragmentShader]
        [return: Target(0)]
        public static float4 Fragment(Varyings input) => input.color;
    }
}

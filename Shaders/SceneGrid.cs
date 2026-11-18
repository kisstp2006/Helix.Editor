using HelixEngine.Builtin;
using HelixEngine.Shaders;
using static HelixEngine.Shaders.ShaderMath;

namespace HelixEditor.Shaders;

/// <summary>
/// Hidden/Editor/SceneGrid: the scene view's grid on a quad of the plane it lies in. Its lines are drawn from the
/// world position's distance to the cells' edges (one pixel wide at any distance, through the screen derivatives);
/// every tenth line is a major one; the minor lines fade as they crowd, everything fades with the distance from the
/// camera. The plane's two axes (_AxisU, _AxisV) give the grid's coordinates, so one shader draws the XZ, XY and YZ
/// grids. Blended over the scene without depth writes, tested against its depth.
/// </summary>
[Shader("Hidden/Editor/SceneGrid")]
[Tag("Queue", "Transparent")]
[Tag("RenderType", "Transparent")]
[Tag("IgnoreProjector", "True")]
public sealed class SceneGrid : ShaderDefinition
{
    [Property("Color"), Default(0.5f, 0.5f, 0.5f, 0.45f)] public float4 _GridColor;
    [Property("Axis U"), Default(1.0f, 0.0f, 0.0f, 0.0f)] public float4 _AxisU;
    [Property("Axis V"), Default(0.0f, 0.0f, 1.0f, 0.0f)] public float4 _AxisV;
    [Property("Cell Size"), Default(1.0f)] public float _CellSize;
    [Property("Fade Distance"), Default(100.0f)] public float _FadeDistance;

    public struct MaterialData
    {
        public float4 _GridColor;
        public float4 _AxisU;
        public float4 _AxisV;
        public float _CellSize;
        public float _FadeDistance;
    }

    public struct Varyings
    {
        [Position] public float4 position;
        [Location(0)] public float3 world;
    }

    [Uniform(0)] public static MaterialData material;

    // How much of a line covers the pixel: 1 on a cell's edge, 0 a pixel away from it.
    static float Lines(float2 coord)
    {
        float2 width = Max(Fwidth(coord), 1e-4f);
        float2 edge = Abs(Frac(coord - 0.5f) - 0.5f) / width;
        return 1.0f - Min(Min(edge.x, edge.y), 1.0f);
    }

    [Pass("SceneGrid", LightMode = "SRPDefaultUnlit", Cull = CullMode.Off, ZWrite = false)]
    [Blend(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha)]
    public static class Grid
    {
        [VertexShader]
        public static Varyings Vertex([Location(VertexInputs.Position)] float3 position)
        {
            Varyings o;
            o.position = ShaderVariables.ObjectToClip(position);
            o.world = ShaderVariables.ObjectToWorld(position);
            return o;
        }

        [FragmentShader]
        [return: Target(0)]
        public static float4 Fragment(Varyings input)
        {
            float2 coord = new float2(Dot(input.world, material._AxisU.xyz), Dot(input.world, material._AxisV.xyz)) / material._CellSize;
            float2 cells = Fwidth(coord);
            // The minor lines fade out as a cell gets smaller than a few pixels.
            float crowd = Saturate(1.0f - Max(cells.x, cells.y) * 4.0f);
            float minor = Lines(coord) * crowd * 0.55f;
            float major = Lines(coord * 0.1f);
            float distance = Distance(input.world, ShaderVariables.camera.helix_WorldSpaceCameraPos.xyz);
            float fade = 1.0f - Smoothstep(material._FadeDistance * 0.25f, material._FadeDistance, distance);
            float alpha = Max(minor, major) * material._GridColor.w * fade;
            return new float4(material._GridColor.xyz, alpha);
        }
    }
}

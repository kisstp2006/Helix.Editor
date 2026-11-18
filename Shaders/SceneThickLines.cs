using HelixEngine.Builtin;
using HelixEngine.Shaders;
using static HelixEngine.Shaders.ShaderMath;

namespace HelixEditor.Shaders;

/// <summary>
/// Hidden/Editor/SceneThickLines: the scene view's thick and anti-aliased lines. Each segment is a quad of four vertices
/// at its two ends (the position is the vertex's end, the normal the other end, the first texture coordinate its side
/// (-1 or 1) and half the width in pixels, negative at the segment's end), widened across the segment on the screen in
/// the vertex shader; the
/// fragment fades out the last pixel at the edges. The view's size in pixels is _ViewportSize; the depth test a
/// property as SceneLines'.
/// </summary>
[Shader("Hidden/Editor/SceneThickLines")]
[Tag("Queue", "Overlay")]
[Tag("RenderType", "Transparent")]
public sealed class SceneThickLines : ShaderDefinition
{
    [Property("Viewport Size"), Default(1.0f, 1.0f, 0.0f, 0.0f)] public float4 _ViewportSize;
    [Property("Depth Test"), Default(8.0f)] public float _ZTest;

    public struct MaterialData
    {
        public float4 _ViewportSize;
        public float _ZTest;
    }

    public struct Varyings
    {
        [Position] public float4 position;
        [Location(0)] public float4 color;
        // Pixels from the segment's middle line across it, and half the width.
        [Location(1)] public float2 across;
    }

    [Uniform(0)] public static MaterialData material;

    [Pass("SceneThickLines", LightMode = "SRPDefaultUnlit", Cull = CullMode.Off, ZWrite = false, ZTestProperty = "_ZTest")]
    [Blend(BlendFactor.SrcAlpha, BlendFactor.OneMinusSrcAlpha)]
    public static class Draw
    {
        [VertexShader]
        public static Varyings Vertex([Location(VertexInputs.Position)] float3 position, [Location(VertexInputs.Normal)] float3 other,
                                      [Location(VertexInputs.TexCoord0)] float2 sideAndWidth, [Location(VertexInputs.Color)] float4 color)
        {
            float4 a = ShaderVariables.ObjectToClip(position);
            float4 b = ShaderVariables.ObjectToClip(other);
            float2 size = Max(material._ViewportSize.xy, new float2(1.0f, 1.0f));
            // The segment's direction on the screen (pixels), from this end to the other.
            float2 sa = a.xy / Max(a.w, 1e-5f) * size;
            float2 sb = b.xy / Max(b.w, 1e-5f) * size;
            // From the segment's start to its end at both ends (the end's vertices have a negative half width).
            float toEnd = sideAndWidth.y < 0.0f ? -1.0f : 1.0f;
            float2 d = (sb - sa) * toEnd;
            float len = Length(d);
            float2 dir = len > 1e-4f ? d / len : new float2(1.0f, 0.0f);
            float2 normal = new float2(-dir.y, dir.x);
            // One pixel more each side for the fade.
            float halfWidth = Abs(sideAndWidth.y);
            float half = halfWidth + 1.0f;
            float2 offset = normal * (sideAndWidth.x * half) * 2.0f / size;
            Varyings o;
            o.position = new float4(a.xy + offset * a.w, a.z, a.w);
            o.color = color;
            o.across = new float2(sideAndWidth.x * half, halfWidth);
            return o;
        }

        [FragmentShader]
        [return: Target(0)]
        public static float4 Fragment(Varyings input)
        {
            float coverage = Saturate(input.across.y + 0.5f - Abs(input.across.x));
            return new float4(input.color.xyz, input.color.w * coverage);
        }
    }
}

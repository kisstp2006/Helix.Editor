using HelixEngine.Builtin;
using HelixEngine.Shaders;
using static HelixEngine.Shaders.ShaderMath;

namespace HelixEditor.Shaders;

/// <summary>
/// Hidden/Editor/SceneOutline: the selection's outline from its mask (_MainTex: the selected renderers drawn white).
/// A pixel outside the mask with a masked pixel within _OutlineWidth pixels is the outline color; everything else is
/// clear. Drawn by Graphics.Blit into the layer the scene view shows over its camera's image.
/// </summary>
[Shader("Hidden/Editor/SceneOutline")]
public sealed class SceneOutline : ShaderDefinition
{
    [Property("Mask"), MainTexture, DefaultTexture("black")] public Texture2D<float4> _MainTex;
    [Property("Color"), Default(1.0f, 0.4f, 0.0f, 1.0f)] public float4 _OutlineColor;
    [Property("Width"), Default(2.0f)] public float _OutlineWidth;

    public struct MaterialData
    {
        public float4 _MainTex_ST;
        public float4 _OutlineColor;
        public float _OutlineWidth;
    }

    public struct Varyings
    {
        [Position] public float4 position;
        [Location(0)] public float2 uv;
    }

    [Pass("Outline", Cull = CullMode.Off, ZTest = CompareFunction.Always, ZWrite = false)]
    public static class Outline
    {
        [Uniform(0)] public static MaterialData material;
        [Texture(0)] public static Texture2D<float4> _MainTex;
        [Sampler(0)] public static SamplerState mainSampler;

        [VertexShader]
        public static Varyings Vertex([VertexId] uint id)
        {
            Varyings o;
            float x = (id & 1u) != 0u ? 3.0f : -1.0f;
            float y = (id & 2u) != 0u ? 3.0f : -1.0f;
            o.position = Mul(ShaderVariables.camera.helix_MatrixP, new float4(x, y, 0.5f, 1.0f));
            o.uv = new float2(x * 0.5f + 0.5f, y * 0.5f + 0.5f) * material._MainTex_ST.xy + material._MainTex_ST.zw;
            return o;
        }

        [FragmentShader]
        [return: Target(0)]
        public static float4 Fragment(Varyings input)
        {
            int2 size = _MainTex.Size;
            int2 texel = new int2(Clamp((int)Floor(input.uv.x * (float)size.x), 0, size.x - 1), Clamp((int)Floor(input.uv.y * (float)size.y), 0, size.y - 1));
            if (_MainTex.Load(texel, 0).x > 0.5f)
                return new float4(0.0f, 0.0f, 0.0f, 0.0f);
            int w = (int)material._OutlineWidth;
            float near = 0.0f;
            for (int dy = -w; dy <= w; dy++)
                for (int dx = -w; dx <= w; dx++)
                {
                    int2 at = new int2(Clamp(texel.x + dx, 0, size.x - 1), Clamp(texel.y + dy, 0, size.y - 1));
                    near = Max(near, _MainTex.Load(at, 0).x);
                }
            return near > 0.5f ? material._OutlineColor : new float4(0.0f, 0.0f, 0.0f, 0.0f);
        }
    }
}

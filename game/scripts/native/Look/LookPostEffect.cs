using Godot;
using System;

namespace EnFractal.Native.Look;

/// <summary>
/// The preset's post.grain and post.vignette as a Forward+ compositor effect, so every camera in the world
/// gets them, including offscreen review targets. It runs after transparent geometry, on the linear colour
/// buffer before tone mapping. The grain is fixed in screen space, like the tooth of paper, rather than
/// animated film noise, so temporal anti-aliasing keeps it instead of averaging it away. Without a GPU
/// rendering device (headless tests, Compatibility) it never runs.
/// </summary>
public partial class LookPostEffect : CompositorEffect
{
    /// <summary>The compute shader. Its Params block must match PushConstantNames, which the look test checks.</summary>
    public const string ComputeSource = @"
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
layout(rgba16f, set = 0, binding = 0) uniform image2D color_image;
layout(push_constant, std430) uniform Params {
	vec2 size;
	float grain;
	float vignette;
	float seed;
	float vignette_start;
	float vignette_end;
	float grain_fine;
	float grain_soft;
	float grain_soft_px;
	float pad0;
	float pad1;
} params;

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

void main() {
	ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
	if (pixel.x >= int(params.size.x) || pixel.y >= int(params.size.y)) return;
	vec4 color = imageLoad(color_image, pixel);
	vec2 uv = (vec2(pixel) + 0.5) / params.size;
	vec2 centred = (uv - 0.5) * vec2(params.size.x / params.size.y, 1.0);
	float edge = smoothstep(params.vignette_start, params.vignette_end, length(centred));
	float vignette = 1.0 - params.vignette * edge;
	// Two scales of grain: a fine tooth and a softer mottle, multiplicative so shadows stay clean.
	float fine = hash12(vec2(pixel) + params.seed) - 0.5;
	float soft = hash12(floor(vec2(pixel) / max(params.grain_soft_px, 1.0)) + params.seed * 1.7) - 0.5;
	float grain = 1.0 + params.grain * (fine * params.grain_fine + soft * params.grain_soft);
	color.rgb *= vignette * grain;
	imageStore(color_image, pixel, color);
}
";

    /// <summary>The push-constant block in the order the compute shader declares it: twelve floats, 48 bytes (std430).</summary>
    public static readonly string[] PushConstantNames =
    {
        "size.x", "size.y", "grain", "vignette", "seed", "vignette_start", "vignette_end", "grain_fine", "grain_soft", "grain_soft_px", "pad0", "pad1",
    };

    /// <summary>The grain pattern's fixed offset: the same tooth every frame, so TAA keeps it.</summary>
    public const float Seed = 17.0f;

    public float Grain { get; set; }
    public float Vignette { get; set; }
    /// <summary>Vignette range and grain mix (x_look_post).</summary>
    public PostTuning Tuning { get; set; } = LookTuning.Default.Post;
    /// <summary>Whether the shader compiled and the effect has run at least once (for reports and tests).</summary>
    public bool Ran { get; private set; }
    public string Error { get; private set; } = "";

    private RenderingDevice? _device;
    private Rid _shader;
    private Rid _pipeline;

    public LookPostEffect()
    {
        EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
    }

    private bool EnsurePipeline()
    {
        if (_pipeline.IsValid) return true;
        if (Error.Length > 0) return false;
        _device = RenderingServer.GetRenderingDevice();
        if (_device == null) return false;
        var source = new RDShaderSource { Language = RenderingDevice.ShaderLanguage.Glsl, SourceCompute = ComputeSource };
        var spirv = _device.ShaderCompileSpirVFromSource(source);
        if (!string.IsNullOrEmpty(spirv.CompileErrorCompute))
        {
            Error = spirv.CompileErrorCompute;
            GD.PushError("LOOK: post effect shader failed to compile: " + Error);
            return false;
        }
        _shader = _device.ShaderCreateFromSpirV(spirv);
        if (!_shader.IsValid) { Error = "shader could not be created"; return false; }
        _pipeline = _device.ComputePipelineCreate(_shader);
        return _pipeline.IsValid;
    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        if (effectCallbackType != (int)EffectCallbackTypeEnum.PostTransparent || (Grain <= 0f && Vignette <= 0f)) return;
        if (!EnsurePipeline() || _device == null) return;
        if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD buffers) return;
        var size = buffers.GetInternalSize();
        if (size.X == 0 || size.Y == 0) return;
        var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(PushConstants(size).AsSpan()).ToArray();
        var groupsX = (uint)((size.X - 1) / 8 + 1);
        var groupsY = (uint)((size.Y - 1) / 8 + 1);
        for (uint view = 0; view < buffers.GetViewCount(); view++)
        {
            var uniform = new RDUniform { UniformType = RenderingDevice.UniformType.Image, Binding = 0 };
            uniform.AddId(buffers.GetColorLayer(view));
            var set = UniformSetCacheRD.GetCache(_shader, 0, new Godot.Collections.Array<RDUniform> { uniform });
            var list = _device.ComputeListBegin();
            _device.ComputeListBindComputePipeline(list, _pipeline);
            _device.ComputeListBindUniformSet(list, set, 0);
            _device.ComputeListSetPushConstant(list, bytes, (uint)bytes.Length);
            _device.ComputeListDispatch(list, groupsX, groupsY, 1);
            _device.ComputeListEnd();
        }
        Ran = true;
    }

    /// <summary>The values the effect pushes to the shader for a target of this size, in PushConstantNames order.</summary>
    public float[] PushConstants(Vector2I size) => new[]
    {
        size.X, size.Y, Grain, Vignette, Seed, Tuning.VignetteStart, Tuning.VignetteEnd,
        Tuning.GrainFine, Tuning.GrainSoft, Tuning.GrainSoftPx, 0f, 0f,
    };

    /// <summary>
    /// What the shader multiplies one pixel's colour by, computed on the CPU from the same push constants with the
    /// same arithmetic (a mirror of main() in ComputeSource, for tests and reports).
    /// </summary>
    public static float Factor(float[] constants, Vector2I pixel)
    {
        float P(string name) => constants[Array.IndexOf(PushConstantNames, name)];
        var size = new Vector2(P("size.x"), P("size.y"));
        var uv = (new Vector2(pixel.X, pixel.Y) + new Vector2(0.5f, 0.5f)) / size;
        var centred = (uv - new Vector2(0.5f, 0.5f)) * new Vector2(size.X / size.Y, 1f);
        var edge = SmoothStep(P("vignette_start"), P("vignette_end"), centred.Length());
        var vignette = 1f - P("vignette") * edge;
        var fine = Hash12(new Vector2(pixel.X, pixel.Y) + new Vector2(P("seed"), P("seed"))) - 0.5f;
        var softPx = Mathf.Max(P("grain_soft_px"), 1f);
        var soft = Hash12(new Vector2(Mathf.Floor(pixel.X / softPx), Mathf.Floor(pixel.Y / softPx)) + new Vector2(P("seed") * 1.7f, P("seed") * 1.7f)) - 0.5f;
        var grain = 1f + P("grain") * (fine * P("grain_fine") + soft * P("grain_soft"));
        return vignette * grain;
    }

    private static float Fract(float x) => x - Mathf.Floor(x);

    private static float SmoothStep(float from, float to, float x)
    {
        var t = Mathf.Clamp((x - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Hash12(Vector2 p)
    {
        var p3 = new Vector3(Fract(p.X * 0.1031f), Fract(p.Y * 0.1031f), Fract(p.X * 0.1031f));
        var d = p3.Dot(new Vector3(p3.Y, p3.Z, p3.X) + new Vector3(33.33f, 33.33f, 33.33f));
        p3 += new Vector3(d, d, d);
        return Fract((p3.X + p3.Y) * p3.Z);
    }

    /// <summary>Free the GPU objects. The look director calls this when it leaves the tree; safe to call twice.</summary>
    public void Release()
    {
        if (_device != null && _shader.IsValid)
        {
            // Freeing the shader also frees the pipeline made from it.
            _device.FreeRid(_shader);
        }
        _shader = default;
        _pipeline = default;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete) Release();
    }
}

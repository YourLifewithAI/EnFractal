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
    private const string ComputeSource = @"
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
        Span<float> constants = stackalloc float[]
        {
            size.X, size.Y, Grain, Vignette, 17.0f, Tuning.VignetteStart, Tuning.VignetteEnd,
            Tuning.GrainFine, Tuning.GrainSoft, Tuning.GrainSoftPx, 0f, 0f,
        };
        var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(constants).ToArray();
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

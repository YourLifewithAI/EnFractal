using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using EnFractal.Native.Room;

namespace EnFractal.Native.Look;

/// <summary>
/// The numbers of the Gubble's Bubbles and Fireworks (Run 2, Bubbles and Fireworks; docs/look/EFFECTS.md). Code constants for this
/// round, like GlowLook: the founder tunes them by playing, and a later preset version can carry them as a look block.
/// </summary>
public static class EffectLook
{
    /// <summary>The most effects the look draws at once, glows, bubbles and fireworks together: the engine's cap of 8 active effects per room.</summary>
    public const int MaxEffects = 8;

    // ---------- bubbles (particles.float) ----------

    /// <summary>The most bubbles one stream keeps in the air: past it the stream waits for one to pop.</summary>
    public const int MaxBubbles = 40;
    /// <summary>Bubbles blown a second per unit of intensity (the pack's default 0.6 blows about 8 a second).</summary>
    public const float BubblesPerSecond = 14f;
    /// <summary>A bubble's radius, metres: a few millimetres to a centimetre and a half, at the 10 cm scale a child's soap bubbles.</summary>
    public const float BubbleMinM = 0.005f, BubbleMaxM = 0.015f;
    /// <summary>How long a bubble floats before it pops on its own, seconds.</summary>
    public const float BubbleLifeMinS = 3f, BubbleLifeMaxS = 6.5f;
    /// <summary>How fast a bubble rises, m/s, and how far it wobbles sideways as it goes.</summary>
    public const float BubbleRiseMps = 0.045f, BubbleWobbleM = 0.012f;
    /// <summary>
    /// A bubble blown at a spot flies from the Gubble to a place within the spot's radius, a little above it, in this many seconds a
    /// metre (between BlowMinS and BlowMaxS), slowing as it arrives, then floats there.
    /// </summary>
    public const float BlowSecondsPerM = 0.55f, BlowMinS = 0.25f, BlowMaxS = 1.2f, BlowLiftM = 0.06f;
    /// <summary>The breeze bubbles drift in when the world has no wind of its own (the look's own wind direction, as the grass bends).</summary>
    public const float BreezeMps = 0.03f;
    public static readonly Vector2 BreezeDirection = new Vector2(1f, 1f).Normalized();
    /// <summary>A pop: the bubble swells by PopSwell and fades out over PopS.</summary>
    public const float PopS = 0.14f, PopSwell = 0.7f;
    /// <summary>A bubble just blown does not pop on the Gubble that blew it, for this long.</summary>
    public const float BlowerGraceS = 0.6f;

    // ---------- fireworks (particles.burst plus light.emit) ----------

    /// <summary>A burst at a spot happens this high above it; a burst on the Gubble this high above its head.</summary>
    public const float BurstHeightM = 0.8f, SelfBurstHeightM = 0.3f;
    /// <summary>The rocket's climb: RocketBaseS plus RocketSecondsPerM a metre, at most RocketMaxS.</summary>
    public const float RocketBaseS = 0.35f, RocketSecondsPerM = 0.25f, RocketMaxS = 1.0f;
    /// <summary>Sparks a burst throws: SparksBase plus SparksPerIntensity times the intensity, at most MaxSparks.</summary>
    public const int SparksBase = 24, SparksPerIntensity = 48, MaxSparks = 96;
    /// <summary>The trail of embers the rocket leaves, at most this many at once, each this long-lived.</summary>
    public const int MaxEmbers = 32;
    public const float EmberLifeS = 0.45f, EmberEveryS = 0.018f;
    /// <summary>The sparks fly out to the radius in about SparkOutS, slowed by SparkDrag a second and pulled down by SparkGravity.</summary>
    public const float SparkOutS = 0.55f, SparkDrag = 2.2f, SparkGravity = 0.35f, SparkLifeMinS = 0.9f, SparkLifeMaxS = 1.6f;
    /// <summary>A spark's size, metres, and how far over 1 its colour is pushed so the bloom catches it.</summary>
    public const float SparkM = 0.022f, SparkHot = 2.6f, EmberM = 0.014f;
    /// <summary>A burst on the Gubble is this much of the radius it is given.</summary>
    public const float SelfBurstScale = 0.5f;
    /// <summary>
    /// The flash: a brief omni light at the burst that lights the land for a moment. Its energy is FlashPerIntensity times the
    /// intensity at its peak (FlashRiseS in), gone FlashS after the burst; it reaches FlashRangeScale times the radius plus the burst's
    /// height (at most the engine's 3 m), and never casts a shadow, so it costs no shadow slot.
    /// </summary>
    public const float FlashPerIntensity = 2.0f, FlashRiseS = 0.04f, FlashS = 0.7f, FlashRangeScale = 1.5f, FlashMaxRangeM = 3f, FlashAttenuation = 1.0f;
    /// <summary>While a fireworks effect lasts, another rocket goes up this often (the host's duration decides how many).</summary>
    public const float VolleyEveryS = 3.5f;
}

public partial class LookDirector
{
    /// <summary>What kind of effect an id draws.</summary>
    public enum EffectKind { None, Glow, Bubbles, Fireworks }

    private sealed class Particle
    {
        public Vector3 Position, Velocity, From, Target, Checked;
        public float Radius, Age, Life, Travel, Phase, Popping = -1f;
        public bool Alive, FromBody;
        public Color Tint;
    }

    private sealed class Effect
    {
        public string Id = "";
        public EffectKind Kind;
        public Node3D Root = null!;
        public MultiMeshInstance3D Draw = null!;
        public Node3D? Follow;
        public Vector3 Center;
        public float RadiusM, Intensity;
        public Color Color;
        public Particle[] Items = Array.Empty<Particle>();
        public float Clock, Emit, NextVolley;
        public int Frame;
        public RandomNumberGenerator Random = new();
        // Fireworks: the rocket in flight, the burst point, the flash.
        public OmniLight3D? Flash;
        public bool RocketUp;
        public Vector3 RocketFrom, Burst;
        public float RocketS, RocketAge, FlashAge = -1f, EmberClock;
        public int Pops, Bursts;
    }

    private readonly Dictionary<string, Effect> _effects = new();
    private readonly List<Effect> _effectOrder = new();
    private ImageTexture? _bubbleTexture;

    /// <summary>How many effects the look is drawing: glows, bubbles and fireworks together (at most EffectLook.MaxEffects).</summary>
    public int EffectCount => _glows.Count + _effects.Count;

    /// <summary>What an effect id draws (None for an unknown id).</summary>
    public EffectKind KindOf(string effectId) =>
        effectId == null ? EffectKind.None : _glows.ContainsKey(effectId) ? EffectKind.Glow : _effects.TryGetValue(effectId, out var e) ? e.Kind : EffectKind.None;

    /// <summary>Whether an effect with this id is drawn, of any kind.</summary>
    public bool HasEffect(string effectId) => KindOf(effectId) != EffectKind.None;

    /// <summary>An effect's root node (bubbles or fireworks), for tests and the review harness; glows have GlowRoot.</summary>
    public Node3D? EffectRoot(string effectId) => effectId != null && _effects.TryGetValue(effectId, out var e) ? e.Root : null;

    /// <summary>A fireworks effect's flash light while it is lit (null between bursts, and for any other id).</summary>
    public OmniLight3D? FlashLight(string effectId) => effectId != null && _effects.TryGetValue(effectId, out var e) && e.Flash != null && IsInstanceValid(e.Flash) ? e.Flash : null;

    /// <summary>The live bubbles' or sparks' positions and radii (popping ones included), for tests and the review harness.</summary>
    public IReadOnlyList<(Vector3 Position, float Radius, bool Popping)> EffectParticles(string effectId) =>
        effectId != null && _effects.TryGetValue(effectId, out var e) ? e.Items.Where(p => p.Alive).Select(p => (p.Position, p.Radius, p.Popping >= 0f)).ToArray() : Array.Empty<(Vector3, float, bool)>();

    /// <summary>How many bubbles of an effect have popped on touch (an avatar or the land), not of old age; for tests.</summary>
    public int TouchPops(string effectId) => effectId != null && _effects.TryGetValue(effectId, out var e) ? e.Pops : 0;

    /// <summary>How many bursts a fireworks effect has made; for tests.</summary>
    public int Bursts(string effectId) => effectId != null && _effects.TryGetValue(effectId, out var e) ? e.Bursts : 0;

    /// <summary>The wind the bubbles drift in, m/s in XZ: null (the default) uses the world's wind from the player's body, or the look's light breeze when there is none.</summary>
    public Vector2? Wind { get; set; }

    /// <summary>
    /// Start the Gubble's bubbles (particles.float), cosmetic only. With follow, a stream of bubbles rises from that body while it
    /// moves; without, the Gubble (when one is drawn) blows a stream toward center: each bubble flies from the Gubble to a place
    /// within radiusM of the spot, a little above it, then floats, rising slowly, wobbling and drifting with the wind. With no Gubble
    /// drawn they rise from the spot itself. intensity (0.05 to 2.0) sets how many a second. A bubble pops when it touches an avatar
    /// or the land, or after a few seconds, as a look effect: no collision. The bubbles take the aura's tint in their sheen. The same
    /// rules as StartGlow: an id in use is replaced; false, drawing nothing, past EffectLook.MaxEffects or for an unusable call.
    /// </summary>
    public bool StartBubbles(string effectId, Node3D? follow, Vector3 center, float radiusM, float intensity) =>
        StartParticles(EffectKind.Bubbles, effectId, follow, center, radiusM, intensity);

    /// <summary>
    /// Start the Gubble's fireworks (particles.burst with a brief light.emit), cosmetic only. Without follow, a rocket climbs from the
    /// Gubble (when one is drawn; else from the spot) and bursts BurstHeightM above center into sparks reaching radiusM, with a
    /// flash that lights the land for a moment; with follow, a smaller burst over that body. Another rocket goes up every
    /// VolleyEveryS while the effect lasts. intensity (0.05 to 2.0) sets the sparks and the flash. The flash is a real light within
    /// the budgets (no shadow) and counts toward LightLevelAt while it is lit. Colours from the aura. The same rules as StartGlow.
    /// </summary>
    public bool StartFireworks(string effectId, Node3D? follow, Vector3 center, float radiusM, float intensity) =>
        StartParticles(EffectKind.Fireworks, effectId, follow, center, radiusM, intensity);

    /// <summary>End an effect of any kind (a glow, bubbles or fireworks) and free everything it drew. An unknown id is a no-op.</summary>
    public void StopEffect(string effectId)
    {
        if (effectId == null) return;
        if (_glows.ContainsKey(effectId)) StopGlow(effectId);
        else FreeEffect(effectId);
    }

    /// <summary>End every effect: every glow, every stream of bubbles and every fireworks.</summary>
    public void StopAllEffects()
    {
        StopAllGlows();
        for (var i = _effectOrder.Count - 1; i >= 0; i--) FreeEffect(_effectOrder[i].Id);
    }

    private bool UsableEffect(string effectId, Node3D? follow, Vector3 center, float radiusM, float intensity)
    {
        if (string.IsNullOrEmpty(effectId) || effectId.Length > 128 || !float.IsFinite(radiusM) || !float.IsFinite(intensity)) return false;
        if (follow != null && (!IsInstanceValid(follow) || follow.IsQueuedForDeletion() || !follow.IsInsideTree())) return false;
        if (follow == null && !(float.IsFinite(center.X) && float.IsFinite(center.Y) && float.IsFinite(center.Z))) return false;
        return HasEffect(effectId) || EffectCount < EffectLook.MaxEffects;
    }

    private bool StartParticles(EffectKind kind, string effectId, Node3D? follow, Vector3 center, float radiusM, float intensity)
    {
        if (!UsableEffect(effectId, follow, center, radiusM, intensity)) return false;
        StopEffect(effectId);
        var effect = new Effect
        {
            Id = effectId, Kind = kind, Follow = follow, Center = center,
            RadiusM = Mathf.Clamp(radiusM, GlowLook.MinRadiusM, GlowLook.MaxRadiusM), Intensity = Mathf.Clamp(intensity, GlowLook.MinIntensity, GlowLook.MaxIntensity),
            Color = GlowLook.LightColor(AuraOf(follow ?? CompanionBody())),
        };
        effect.Random.Seed = (ulong)(Seed(effectId) * 1000f) + 17ul;
        var count = kind == EffectKind.Bubbles ? EffectLook.MaxBubbles : EffectLook.MaxSparks + EffectLook.MaxEmbers;
        effect.Items = Enumerable.Range(0, count).Select(_ => new Particle()).ToArray();
        effect.Root = new Node3D { Name = (kind == EffectKind.Bubbles ? "Bubbles_" : "Fireworks_") + RoomBuilder.NodeName(effectId), TopLevel = true };
        effect.Root.SetMeta(GlowMeta, effectId);
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, InstanceCount = count,
            Mesh = new QuadMesh { Size = Vector2.One },
        };
        for (var i = 0; i < count; i++) multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero));
        effect.Draw = new MultiMeshInstance3D
        {
            Name = kind == EffectKind.Bubbles ? "BubbleDraw" : "SparkDraw", Multimesh = multimesh,
            MaterialOverride = kind == EffectKind.Bubbles ? BubbleMaterial() : SparkMaterial(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
        };
        effect.Root.AddChild(effect.Draw);
        AddChild(effect.Root);
        _effects[effectId] = effect;
        _effectOrder.Add(effect);
        if (kind == EffectKind.Fireworks) Launch(effect);
        return true;
    }

    private bool FreeEffect(string effectId)
    {
        if (!_effects.Remove(effectId, out var effect)) return false;
        _effectOrder.Remove(effect);
        if (IsInstanceValid(effect.Root)) effect.Root.Free();
        return true;
    }

    /// <summary>The Gubble's middle when it is drawn (where bubbles are blown from and rockets leave), else null.</summary>
    private Vector3? GubbleMiddle()
    {
        var gubble = CompanionBody();
        if (gubble == null || !IsInstanceValid(gubble) || !gubble.IsInsideTree() || !gubble.IsVisibleInTree()) return null;
        return gubble.GetGlobalTransformInterpolated().Origin + Vector3.Up * ((gubble as SmallPlayerController)?.BodyHeightM ?? 0f) * GlowLook.FollowHeightFraction;
    }

    private Vector3 FollowMiddle(Effect effect)
    {
        var body = effect.Follow!;
        return body.GetGlobalTransformInterpolated().Origin + Vector3.Up * ((body as SmallPlayerController)?.BodyHeightM ?? 0f) * GlowLook.FollowHeightFraction;
    }

    private void UpdateEffects(double delta)
    {
        if (_effectOrder.Count == 0) return;
        var dt = (float)Math.Min(delta, 0.1);
        for (var i = _effectOrder.Count - 1; i >= 0; i--)
        {
            var effect = _effectOrder[i];
            if (effect.Follow != null && !IsInstanceValid(effect.Follow)) { FreeEffect(effect.Id); continue; }
            effect.Clock += dt;
            effect.Frame++;
            if (effect.Kind == EffectKind.Bubbles) AdvanceBubbles(effect, dt);
            else AdvanceFireworks(effect, dt);
            DrawParticles(effect);
        }
    }

    // ---------- bubbles ----------

    private void AdvanceBubbles(Effect effect, float dt)
    {
        var wind = BubbleWind();
        var follow = effect.Follow != null && IsInstanceValid(effect.Follow) && effect.Follow.IsInsideTree();
        var blower = follow ? FollowMiddle(effect) : GubbleMiddle();
        effect.Emit += EffectLook.BubblesPerSecond * effect.Intensity * dt;
        while (effect.Emit >= 1f)
        {
            effect.Emit -= 1f;
            var slot = Array.FindIndex(effect.Items, p => !p.Alive);
            if (slot < 0) { effect.Emit = 0f; break; }
            SpawnBubble(effect, effect.Items[slot], blower, follow);
        }
        var bodies = TouchBodies();
        foreach (var p in effect.Items)
        {
            if (!p.Alive) continue;
            p.Age += dt;
            if (p.Popping >= 0f)
            {
                p.Popping += dt;
                if (p.Popping >= EffectLook.PopS) p.Alive = false;
                continue;
            }
            var wobble = new Vector3(Mathf.Sin(p.Age * 3.1f + p.Phase), 0f, Mathf.Cos(p.Age * 2.3f + p.Phase * 1.7f)) * (EffectLook.BubbleWobbleM * 2.5f);
            if (p.Age < p.Travel)
            {
                // Blown: from the Gubble to its place by the spot, slowing as it arrives.
                var t = p.Age / p.Travel;
                var eased = 1f - (1f - t) * (1f - t);
                p.Position = p.From.Lerp(p.Target, eased) + Vector3.Up * (0.15f * p.From.DistanceTo(p.Target) * 4f * t * (1f - t));
            }
            else p.Position += (new Vector3(wind.X, EffectLook.BubbleRiseMps, wind.Y) + p.Velocity + wobble) * dt;
            p.Velocity *= Mathf.Max(0f, 1f - 1.5f * dt);
            if (p.Age >= p.Life) { p.Popping = 0f; continue; }
            if (Touches(p, bodies, effect)) { p.Popping = 0f; effect.Pops++; }
        }
    }

    private void SpawnBubble(Effect effect, Particle p, Vector3? blower, bool follow)
    {
        var r = effect.Random;
        p.Alive = true;
        p.Age = 0f;
        p.Popping = -1f;
        p.FromBody = false;
        p.Radius = r.RandfRange(EffectLook.BubbleMinM, EffectLook.BubbleMaxM);
        p.Life = r.RandfRange(EffectLook.BubbleLifeMinS, EffectLook.BubbleLifeMaxS);
        p.Phase = r.RandfRange(0f, Mathf.Tau);
        p.Tint = new Color(1f, 1f, 1f, 1f).Lerp(effect.Color, r.RandfRange(0.25f, 0.6f));
        var around = new Vector3(r.RandfRange(-1f, 1f), 0f, r.RandfRange(-1f, 1f)).LimitLength(1f);
        if (follow && blower is { } body)
        {
            // From the body as it moves: puffed out a little, then floating.
            p.Position = body + around * 0.02f;
            p.Velocity = around * 0.06f + Vector3.Up * 0.02f;
            p.Travel = 0f;
            p.FromBody = true;
        }
        else if (blower is { } from)
        {
            p.From = from;
            p.Target = effect.Center + Vector3.Up * EffectLook.BlowLiftM + around * effect.RadiusM;
            p.Travel = Mathf.Clamp(from.DistanceTo(p.Target) * EffectLook.BlowSecondsPerM, EffectLook.BlowMinS, EffectLook.BlowMaxS);
            p.Position = from;
            p.Velocity = Vector3.Zero;
        }
        else
        {
            // No Gubble drawn: they rise from the spot itself.
            p.Position = effect.Center + Vector3.Up * 0.01f + around * effect.RadiusM * 0.5f;
            p.Velocity = Vector3.Zero;
            p.Travel = 0f;
        }
        p.Checked = p.Position;
    }

    private Vector2 BubbleWind()
    {
        if (Wind is { } set) return set;
        if (FocusBody() is SmallPlayerController player && IsInstanceValid(player) && player.WindMps != Vector2.Zero) return player.WindMps;
        return EffectLook.BreezeDirection * EffectLook.BreezeMps;
    }

    private (Vector3 Foot, float Height, float Radius, bool Gubble)[] TouchBodies()
    {
        var list = new List<(Vector3, float, float, bool)>(2);
        if (FocusBody() is SmallPlayerController player && IsInstanceValid(player) && player.IsInsideTree() && player is not CompanionAvatar)
            list.Add((player.GlobalPosition, player.BodyHeightM, player.BodyRadiusM, false));
        if (CompanionBody() is SmallPlayerController gubble && IsInstanceValid(gubble) && gubble.IsInsideTree())
            list.Add((gubble.GlobalPosition, gubble.BodyHeightM, gubble.BodyRadiusM, true));
        return list.ToArray();
    }

    /// <summary>Whether a bubble touches an avatar (its body as an upright capsule) or the land (a ray along its path, every other frame).</summary>
    private bool Touches(Particle p, (Vector3 Foot, float Height, float Radius, bool Gubble)[] bodies, Effect effect)
    {
        foreach (var body in bodies)
        {
            // The Gubble does not pop what it has just blown, nor the stream rising out of its own body.
            if (body.Gubble && (p.FromBody || p.Age < EffectLook.BlowerGraceS)) continue;
            var y = Mathf.Clamp(p.Position.Y, body.Foot.Y, body.Foot.Y + body.Height);
            if (p.Position.DistanceTo(new Vector3(body.Foot.X, y, body.Foot.Z)) < body.Radius + p.Radius) return true;
        }
        if (!IsInsideTree() || (effect.Frame + (int)(p.Phase * 10f)) % 2 != 0) return false;
        var path = p.Position - p.Checked;
        var from = p.Checked;
        p.Checked = p.Position;
        if (path.LengthSquared() < 1e-10f) return false;
        var probe = Probe();
        probe.Position = from;
        probe.TargetPosition = path + path.Normalized() * p.Radius;
        probe.ForceRaycastUpdate();
        return probe.IsColliding();
    }

    // ---------- fireworks ----------

    private void Launch(Effect effect)
    {
        var self = effect.Follow != null && IsInstanceValid(effect.Follow) && effect.Follow.IsInsideTree();
        var body = self ? effect.Follow as SmallPlayerController : null;
        effect.Burst = self
            ? effect.Follow!.GlobalPosition + Vector3.Up * ((body?.BodyHeightM ?? 0f) + EffectLook.SelfBurstHeightM)
            : effect.Center + Vector3.Up * EffectLook.BurstHeightM;
        effect.RocketFrom = self ? FollowMiddle(effect) : GubbleMiddle() ?? effect.Center + Vector3.Up * 0.01f;
        effect.RocketS = Mathf.Min(EffectLook.RocketMaxS, EffectLook.RocketBaseS + EffectLook.RocketSecondsPerM * effect.RocketFrom.DistanceTo(effect.Burst));
        effect.RocketAge = 0f;
        effect.RocketUp = true;
        effect.EmberClock = 0f;
        effect.NextVolley = effect.Clock + EffectLook.VolleyEveryS;
    }

    /// <summary>Where the rocket is a time into its climb: from where it left, curving up to the burst (fast at first, slowing at the top).</summary>
    private static Vector3 RocketAt(Effect effect, float t)
    {
        t = Mathf.Clamp(t, 0f, 1f);
        var eased = 1f - (1f - t) * (1f - t);
        var flat = effect.RocketFrom.Lerp(effect.Burst, t);
        return new Vector3(flat.X, Mathf.Lerp(effect.RocketFrom.Y, effect.Burst.Y, eased), flat.Z);
    }

    private void AdvanceFireworks(Effect effect, float dt)
    {
        var r = effect.Random;
        var sparks = effect.Items.AsSpan(0, EffectLook.MaxSparks);
        var embers = effect.Items.AsSpan(EffectLook.MaxSparks);
        if (effect.RocketUp)
        {
            effect.RocketAge += dt;
            effect.EmberClock += dt;
            var at = RocketAt(effect, effect.RocketAge / effect.RocketS);
            // The rocket's head is the first ember slot; its trail fills the rest.
            var head = embers[0];
            head.Alive = true;
            head.Position = at;
            head.Radius = EffectLook.EmberM * 0.75f;
            head.Life = 1e9f;
            head.Tint = new Color(1f, 0.92f, 0.75f) * EffectLook.SparkHot;
            while (effect.EmberClock >= EffectLook.EmberEveryS)
            {
                effect.EmberClock -= EffectLook.EmberEveryS;
                var free = -1;
                for (var k = 1; k < embers.Length; k++) if (!embers[k].Alive) { free = k; break; }
                if (free < 0) break;
                var e = embers[free];
                e.Alive = true;
                e.Age = 0f;
                e.Life = EffectLook.EmberLifeS * r.RandfRange(0.7f, 1.2f);
                e.Position = at + new Vector3(r.RandfRange(-1f, 1f), r.RandfRange(-1f, 1f), r.RandfRange(-1f, 1f)) * 0.004f;
                e.Velocity = new Vector3(r.RandfRange(-1f, 1f), -1f, r.RandfRange(-1f, 1f)) * 0.03f;
                e.Radius = EffectLook.EmberM * r.RandfRange(0.6f, 1f);
                e.Tint = new Color(1f, 0.8f, 0.5f) * (EffectLook.SparkHot * 0.7f);
            }
            if (effect.RocketAge >= effect.RocketS)
            {
                effect.RocketUp = false;
                head.Alive = false;
                Explode(effect);
            }
        }
        foreach (var p in effect.Items)
        {
            if (!p.Alive || p == embers[0] && effect.RocketUp) continue;
            p.Age += dt;
            if (p.Age >= p.Life) { p.Alive = false; continue; }
            p.Velocity *= Mathf.Max(0f, 1f - EffectLook.SparkDrag * dt);
            p.Velocity += Vector3.Down * EffectLook.SparkGravity * dt;
            p.Position += p.Velocity * dt;
        }
        if (effect.Flash != null && IsInstanceValid(effect.Flash))
        {
            effect.FlashAge += dt;
            if (effect.FlashAge >= EffectLook.FlashS) { effect.Flash.Free(); effect.Flash = null; effect.FlashAge = -1f; }
            else effect.Flash.LightEnergy = FlashEnergy(effect);
        }
        if (!effect.RocketUp && effect.Clock >= effect.NextVolley) Launch(effect);
    }

    private void Explode(Effect effect)
    {
        var r = effect.Random;
        effect.Bursts++;
        var self = effect.Follow != null;
        var radius = effect.RadiusM * (self ? EffectLook.SelfBurstScale : 1f);
        var count = Math.Min(EffectLook.MaxSparks, EffectLook.SparksBase + (int)(EffectLook.SparksPerIntensity * effect.Intensity));
        // The sparks' speed so that, slowed by the drag, they reach the radius: distance = v / drag × (1 - e^(-drag × t)).
        var speed = radius * EffectLook.SparkDrag / (1f - Mathf.Exp(-EffectLook.SparkDrag * EffectLook.SparkOutS));
        var accent = new Color(1f, 0.93f, 0.78f);
        for (var k = 0; k < count; k++)
        {
            var p = effect.Items[k];
            // An even spread over the sphere (a Fibonacci lattice), turned by the seed so bursts differ.
            var y = 1f - 2f * (k + 0.5f) / count;
            var ring = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            var a = k * 2.39996323f + r.RandfRange(0f, Mathf.Tau) * 0.15f + effect.Bursts;
            var direction = new Vector3(Mathf.Cos(a) * ring, y, Mathf.Sin(a) * ring);
            p.Alive = true;
            p.Age = 0f;
            p.Life = r.RandfRange(EffectLook.SparkLifeMinS, EffectLook.SparkLifeMaxS);
            p.Position = effect.Burst;
            p.Velocity = direction * speed * r.RandfRange(0.85f, 1.05f);
            p.Radius = EffectLook.SparkM * r.RandfRange(0.7f, 1.1f) * (self ? 0.75f : 1f);
            p.Tint = (k % 4 == 0 ? accent : effect.Color) * EffectLook.SparkHot;
        }
        if (effect.Flash != null && IsInstanceValid(effect.Flash)) effect.Flash.Free();
        effect.Flash = new OmniLight3D
        {
            Name = "Flash", TopLevel = true, Position = effect.Burst, LightColor = effect.Color, LightEnergy = 0f,
            OmniRange = FlashRange(effect), OmniAttenuation = EffectLook.FlashAttenuation, ShadowEnabled = false,
            LightBakeMode = Light3D.BakeMode.Disabled, LightSpecular = GlowLook.Specular,
        };
        effect.FlashAge = 0f;
        effect.Root.AddChild(effect.Flash);
        effect.Flash.LightEnergy = FlashEnergy(effect);
    }

    private static float FlashRange(Effect effect) =>
        Mathf.Min(EffectLook.FlashMaxRangeM, effect.RadiusM * (effect.Follow != null ? EffectLook.SelfBurstScale : 1f) * EffectLook.FlashRangeScale
            + (effect.Follow != null ? EffectLook.SelfBurstHeightM : EffectLook.BurstHeightM));

    /// <summary>The flash's energy now: up fast to its peak, then fading to nothing by FlashS.</summary>
    private static float FlashEnergy(Effect effect)
    {
        if (effect.FlashAge < 0f) return 0f;
        var peak = EffectLook.FlashPerIntensity * effect.Intensity;
        if (effect.FlashAge < EffectLook.FlashRiseS) return peak * effect.FlashAge / EffectLook.FlashRiseS;
        var t = Mathf.Clamp((effect.FlashAge - EffectLook.FlashRiseS) / (EffectLook.FlashS - EffectLook.FlashRiseS), 0f, 1f);
        return peak * (1f - t) * (1f - t);
    }

    /// <summary>What the fireworks' flashes give a spot now, for the light-level estimate (brief: only while a flash is lit).</summary>
    private float FlashesAt(Vector3 position)
    {
        var sum = 0f;
        foreach (var effect in _effectOrder)
            if (effect.Flash != null && IsInstanceValid(effect.Flash))
                sum += GlowLook.PointLight(effect.Flash.LightEnergy, effect.Flash.LightColor, effect.Flash.OmniRange, effect.Flash.OmniAttenuation, effect.Burst.DistanceTo(position));
        return sum;
    }

    // ---------- drawing ----------

    private void DrawParticles(Effect effect)
    {
        var multimesh = effect.Draw.Multimesh;
        for (var i = 0; i < effect.Items.Length; i++)
        {
            var p = effect.Items[i];
            if (!p.Alive) { multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero)); continue; }
            float size, alpha;
            if (effect.Kind == EffectKind.Bubbles)
            {
                var pop = p.Popping >= 0f ? Mathf.Clamp(p.Popping / EffectLook.PopS, 0f, 1f) : 0f;
                var born = Mathf.Clamp(p.Age / 0.12f, 0f, 1f);
                size = 2f * p.Radius * (0.4f + 0.6f * born) * (1f + EffectLook.PopSwell * pop);
                alpha = (1f - pop) * (1f - pop);
            }
            else
            {
                var fade = Mathf.Clamp(1f - p.Age / p.Life, 0f, 1f);
                size = 2f * p.Radius * (0.6f + 0.4f * fade);
                alpha = fade * fade;
            }
            multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * size), p.Position));
            multimesh.SetInstanceColor(i, new Color(p.Tint.R, p.Tint.G, p.Tint.B, alpha));
        }
    }

    /// <summary>A soap bubble: a thin bright rim with a faint rainbow sheen and a highlight, lit by the scene (dark at night unless something lights it).</summary>
    private StandardMaterial3D BubbleMaterial() => new()
    {
        ResourceName = "bubble", AlbedoTexture = BubbleTexture(), VertexColorUseAsAlbedo = true,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true,
        Roughness = 0.2f, MetallicSpecular = 0.7f, RimEnabled = true, Rim = 0.6f, RimTint = 0.6f, DisableReceiveShadows = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// <summary>A spark or an ember: a soft dot of burning light, added to what is behind it, its colour (over 1, for the bloom) per spark.</summary>
    private StandardMaterial3D SparkMaterial() => new()
    {
        ResourceName = "spark", AlbedoTexture = HeartTexture(), VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, BillboardKeepScale = true, DisableReceiveShadows = true,
    };

    private ImageTexture BubbleTexture()
    {
        if (_bubbleTexture != null) return _bubbleTexture;
        const int size = 64;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var u = (x + 0.5f) / size * 2f - 1f;
                var v = (y + 0.5f) / size * 2f - 1f;
                var d = Mathf.Sqrt(u * u + v * v);
                var rim = Mathf.SmoothStep(0.72f, 0.92f, d) * (1f - Mathf.SmoothStep(0.92f, 1f, d));
                var film = d < 0.95f ? 0.07f : 0f;
                var hue = Mathf.PosMod(Mathf.Atan2(v, u) / Mathf.Tau + d * 0.35f, 1f);
                var sheen = Color.FromHsv(hue, 0.35f, 1f);
                var spot = 1f - Mathf.SmoothStep(0.05f, 0.2f, new Vector2(u + 0.38f, v + 0.4f).Length());
                var colour = sheen.Lerp(new Color(1f, 1f, 1f), spot);
                image.SetPixel(x, y, new Color(colour.R, colour.G, colour.B, Mathf.Clamp(film + rim * 0.75f + spot * 0.85f, 0f, 1f)));
            }
        _bubbleTexture = ImageTexture.CreateFromImage(image);
        return _bubbleTexture;
    }
}

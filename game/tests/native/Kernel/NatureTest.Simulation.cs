using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using EnFractal.Native.Kernel;

namespace EnFractal.Tests.Kernel;

/// <summary>
/// The ground field's laws on their own (NatureState, no host): Bloom plants only where the sampler finds fertile ground
/// and inside its circle; flowers grow in moist light and fade in the dark and after their life; fire spreads within its
/// radius and budget, leaves ash, stops at rain, wet ground, gaps and fireproof ground; ash regrows; the weather is a
/// function of the saved clock. Then the undo steps: a chain of nature changes taken back to any revision before them,
/// a newer change of another kind cutting the chain, the bounds, and an undo standing for the state it restored.
/// </summary>
public partial class NatureTest
{
    private static NatureState FreshNature() => new();

    /// <summary>Simulation steps of NatureLaws.StepS on a bare state, all cells active.</summary>
    private static void Tick(NatureState state, int count, Func<int, int, NatureCell?>? sample = null, Func<NatureCell, bool>? fireproof = null, float light = 1)
    {
        for (var i = 0; i < count; i++)
        {
            foreach (var cell in state.Cells.Values) cell.Light = light;
            state.Step(NatureLaws.StepS, sample ?? Soil, fireproof ?? (_ => false), _ => true);
        }
    }

    private void TestSimulation()
    {
        var at = new Vector3(0.025f, 0, 0.025f);
        var bloom = FreshNature();
        var step = bloom.BeginUndo("Bloom", 1, Player);
        var planted = bloom.Bloom(at, 0.25f, 30, 0.6f, (x, z) => x < 0 ? null : x == 0 ? new NatureCell { X = x, Z = z, Material = "stone" } : Soil(x, z), step);
        Check(planted > 0 && bloom.Cells.Values.All(c => c.X > 0 && c.Fertile), "Bloom respects sampled gaps and ground that is not soil");
        Check(bloom.Cells.Values.All(c => new Vector2(c.Position.X - at.X, c.Position.Z - at.Z).Length() <= 0.25f), "and stays inside its circle");
        Check(bloom.Cells.Values.All(c => Math.Abs(c.Growth - 0.2f) < 1e-4f), "the pack's intensity 0.6 starts the flowers at growth 0.2");
        var first = bloom.Cells.Values.First();
        var initial = first.Growth;
        Tick(bloom, 10);
        Check(first.Growth > initial, "moist, lit flowers grow");
        var healthy = first.Growth;
        Tick(bloom, 20, light: 0);
        Check(first.Growth < healthy, "darkness makes them fade");
        Tick(bloom, 160);
        Check(bloom.Cells.Count == 0, "flowers fade away after their life, and their cells go");
        var resting = FreshNature();
        resting.Bloom(at, 0.1f, 30, 0.6f, Soil, null);
        var frozen = resting.Cells.Values.First().Copy();
        resting.Step(NatureLaws.StepS, Soil, _ => false, _ => false);
        Check(resting.WeatherS == NatureLaws.StepS && resting.Cells.Values.First().Growth == frozen.Growth && resting.Cells.Values.First().LifeS == frozen.LifeS &&
            resting.Cells.Values.First().RestS == NatureLaws.StepS, "a cell far from both avatars rests unchanged while the weather clock runs, counting the time it missed");

        var fire = FreshNature();
        var fireStep = fire.BeginUndo("Fire", 7, Player);
        Check(fire.Ignite(at, 0.45f, 30, 0.6f, 7, new(), Soil, fireStep) > 0, "dry fuel ignites");
        var start = fire.Cells.Count;
        Tick(fire, 30);
        Check(fire.Cells.Count > start, "fire spreads to neighbouring fuel");
        Check(fire.Cells.Values.Count(c => c.BurningS > 0) <= NatureState.MaxBurning, "the burning cell budget holds");
        Check(fire.Cells.Values.All(c => new Vector2(c.Position.X - at.X, c.Position.Z - at.Z).Length() <= 0.45f + 0.001f), "spread never leaves the fire's radius");
        Check(fire.Cells.Values.Any(c => c.Ash > 0), "fire leaves ash");
        Check(fireStep.Cells.Count == fire.Cells.Count, "every cell the fire touched is in its undo step");
        var rain = FreshNature();
        rain.WeatherS = 180;
        rain.Ignite(at, 1.5f, 60, 0.6f, 0, new(), Soil, null);
        Tick(rain, 6);
        Check(rain.Cells.Values.All(c => c.BurningS <= 0) && rain.Weather.Rain01 == 1 && rain.Weather.Pattern == "rain", "rain puts the fire out");
        var wet = FreshNature();
        Check(wet.Ignite(at, 1, 30, 0.6f, 0, new(), (x, z) => new NatureCell { X = x, Z = z, Fuel = 1, Moisture = 1 }, null) == 0, "wet fuel does not ignite");
        var gap = FreshNature();
        NatureCell? Gap(int x, int z) => x >= 3 && x <= 5 ? null : Soil(x, z);
        gap.Ignite(at, 1.5f, 30, 0.6f, 0, new(), Gap, null);
        Tick(gap, 40, Gap);
        Check(gap.Cells.Values.All(c => c.X < 3), "a gap without fuel stops the spread");
        var shelter = FreshNature();
        shelter.Ignite(at, 1, 30, 0.6f, 0, new(), Soil, null);
        Tick(shelter, 1, fireproof: _ => true);
        Check(shelter.Cells.Values.All(c => c.BurningS == 0), "ground that became fireproof stops burning");
        var ash = FreshNature();
        var burned = Soil(0, 0)!;
        burned.Ash = 1;
        burned.Fuel = 0;
        burned.Moisture = 0.8f;
        ash.Cells[burned.Key] = burned;
        ash.WeatherS = 180;
        Tick(ash, 30);
        Check(burned.Ash < 1 && burned.Fuel > 0, "moist ash slowly gives the ground its fuel back");
        Check(NatureState.Grid(new Vector3(-0.001f, 0, -0.001f)) == (-1, -1), "negative grid coordinates floor rather than truncate");
        var refused = false;
        try { ash.Step(float.NaN, Soil, _ => false, _ => true); }
        catch (ArgumentOutOfRangeException) { refused = true; }
        Check(refused, "a step that is not finite is refused");
        TestTimeAway();
        TestUndoChain();
    }

    private static bool SameCell(NatureCell a, NatureCell b) =>
        a.Key == b.Key && a.Moisture == b.Moisture && a.Growth == b.Growth && a.LifeS == b.LifeS && a.Fuel == b.Fuel && a.Ash == b.Ash && a.BurningS == b.BurningS;

    /// <summary>
    /// Time away (the founder, 13 October): what rests while both avatars are far catches up when one comes back, exactly as
    /// if it had been simulated all along, in bounded steps; a far fire never rests.
    /// </summary>
    private void TestTimeAway()
    {
        var at = new Vector3(0.025f, 0, 0.025f);
        var stayed = FreshNature();
        stayed.Bloom(at, 0.15f, 120, 0.6f, Soil, null);
        var left = stayed.Copy();
        for (var i = 0; i < 100; i++)
        {
            stayed.Step(NatureLaws.StepS, Soil, _ => false, _ => true);
            left.Step(NatureLaws.StepS, Soil, _ => false, _ => false);
        }
        Check(left.Cells.Values.All(c => c.RestS == 50 && c.LifeS == 120), "a flower patch left alone rests and counts the 50 s it missed");
        stayed.Step(NatureLaws.StepS, Soil, _ => false, _ => true);
        left.Step(NatureLaws.StepS, Soil, _ => false, _ => true);
        Check(left.Cells.Count == stayed.Cells.Count && left.Cells.Values.All(c => c.RestS == 0 && SameCell(c, stayed.Cells[c.Key])),
            "on return it catches up in one step to exactly where it would be had it been simulated all along");

        var faded = FreshNature();
        faded.Bloom(at, 0.15f, 30, 0.6f, Soil, null);
        var planted = faded.Cells.Count;
        for (var i = 0; i < 3600; i++) faded.Step(NatureLaws.StepS, Soil, _ => false, _ => false);
        Check(planted > 0 && faded.Cells.Count == planted, "left alone for half an hour, the patch is still there while no one looks");
        faded.Step(NatureLaws.StepS, Soil, _ => false, _ => true);
        Check(faded.Cells.Count == 0, "and has faded away when an avatar comes back");

        // Burnt wood keeps its ash: two hours away, a wide field catches up within the per-step budget, exactly.
        NatureCell Char(int x, int z) => new() { X = x, Z = z, Surface = "obj:log", Material = "wood", Fertile = false, Fuel = 0.1f, Ash = 1, Moisture = 0.3f };
        var field = FreshNature();
        var watched = FreshNature();
        for (var x = 0; x < 20; x++)
        for (var z = 0; z < 20; z++)
        {
            field.Cells[NatureState.Key(x, z)] = Char(x, z);
            if (x == 0 && z == 0) watched.Cells[NatureState.Key(x, z)] = Char(x, z);
        }
        var away = (int)(2 * 3600 / NatureLaws.StepS);
        for (var i = 0; i < away; i++)
        {
            field.Step(NatureLaws.StepS, Soil, _ => false, _ => false);
            watched.Step(NatureLaws.StepS, Soil, _ => false, _ => true);
        }
        field.Step(NatureLaws.StepS, Soil, _ => false, _ => true);
        watched.Step(NatureLaws.StepS, Soil, _ => false, _ => true);
        var waiting = field.Cells.Values.Count(c => c.RestS > 0);
        var steps = 1;
        for (; steps < 200 && field.Cells.Values.Any(c => c.RestS > 0); steps++)
        {
            field.Step(NatureLaws.StepS, Soil, _ => false, _ => true);
            watched.Step(NatureLaws.StepS, Soil, _ => false, _ => true);
        }
        Check(waiting > 0 && steps > 1 && steps < 200, $"a wide field away for two hours catches up over {steps} steps, never more than the budget at once ({waiting} cells still waiting after the first)");
        Check(SameCell(field.Cells[NatureState.Key(0, 0)], watched.Cells[NatureState.Key(0, 0)]), "and lands exactly where a watched cell is, the weather it missed replayed");

        var far = FreshNature();
        far.Ignite(at, 0.45f, 30, 0.6f, 1, new(), Soil, null);
        var lit = far.Cells.Count;
        for (var i = 0; i < 20; i++) far.Step(NatureLaws.StepS, Soil, _ => false, _ => false);
        Check(far.Fire != null && far.Cells.Count > lit && far.Cells.Values.Any(c => c.BurningS > 0), "fire never rests: far from both avatars it keeps burning and spreading");
    }

    /// <summary>The undo steps alone: a chain across principals, cut by another kind of change, bounded, and an undo standing for its state.</summary>
    private void TestUndoChain()
    {
        var at = new Vector3(0.025f, 0, 0.025f);
        var state = FreshNature();
        state.Bloom(at, 0.1f, 30, 0.6f, Soil, state.BeginUndo("Bloom", 3, Player));
        state.Bloom(at + new Vector3(0.5f, 0, 0), 0.1f, 30, 0.6f, Soil, state.BeginUndo("Bloom", 4, Companion));
        Check(state.Undo.Count == 2 && state.UndoTarget(4) == 3 && state.StepsBackTo(2, 4)?.Count == 2 && state.StepsBackTo(3, 4)?.Count == 1,
            "two principals' changes in a row are one chain: back to 3 takes one, back to 2 takes both");
        Check(state.StepsBackTo(1, 4) == null && state.StepsBackTo(2, 5) == null, "nothing reaches past the chain, nor from a newer revision of another kind");
        state.UndoSteps(state.StepsBackTo(2, 4)!, 5, 2);
        Check(state.Cells.Count == 0 && state.Undo.Count == 0 && state.Effective(5) == 2, "both are taken back; revision 5 stands for revision 2's state");
        state.Bloom(at, 0.1f, 30, 0.6f, Soil, state.BeginUndo("Bloom", 6, Player));
        Check(state.Undo[^1].Base == 2 && state.UndoTarget(6) == 2, "a change after an undo starts from the state the undo restored");
        state.Bloom(at, 0.1f, 30, 0.6f, Soil, state.BeginUndo("Bloom", 9, Player));
        Check(state.Undo.Count == 1 && state.Undo[0].Revision == 9, "a newer change of another kind in between (revisions 7 and 8) cuts the older steps off");
        for (var revision = 10; revision < 10 + NatureState.MaxUndoSteps + 3; revision++)
            state.Bloom(at, 0.05f, 30, 0.6f, Soil, state.BeginUndo("Bloom", revision, Player));
        Check(state.Undo.Count == NatureState.MaxUndoSteps, "at most MaxUndoSteps steps are kept, the oldest dropped first");
    }
}

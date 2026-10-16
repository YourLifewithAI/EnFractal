using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;

namespace EnFractal.Tests.Kernel;

/// <summary>
/// Nothing records Fire yet (M11, the founder's redesign of 15 October: a later Fire packet builds the new list's behaviours): a fire
/// lit, burning, put out or burnt out teaches nothing, counts nothing and saves nothing, so a stray recording cannot slip in.
/// </summary>
public partial class NatureTest
{
    private async Task TestExpertise()
    {
        await NewWorld(SavePath("expertise-fire"));
        var store = new CountingProgressStore();
        _host.ConfigureGlowProgress(store);
        var preview = Send(Command(NextId("preview"), "nature.cast", Cast("fire", new Vector3(0.2f, 0.01f, -0.6f), 0.05, 6), preview: true), Player);
        var lit = Fire(Player, new Vector3(0.2f, 0.01f, -0.6f), 0.05, 6, out _);
        var steps = 0;
        while (_host.Nature.Fire != null && steps++ < 200) Steps(1);
        var companion = Fire(Companion, new Vector3(0.5f, 0.01f, -0.6f), 0.05, 6, out _);
        Steps(2);
        var stopped = _host.PlayerGoal("stop");
        Check(Ok(preview) && Ok(lit) && Ok(companion) && Ok(stopped) && _host.Nature.Cells.Values.Any(c => c.Ash > 0) && _host.Nature.Fire == null,
            "a fire was previewed, lit and burnt out by the player, and lit by the Gubble and put out");
        Check(_host.GlowProgress.Others.Count == 0 && _host.ExpertiseCount() == 0 && store.Saves == 0 && _host.GlowProgressNotice.Length == 0,
            "and none of it taught, counted or saved anything: nothing records Fire yet");
    }

    private sealed class CountingProgressStore : IGlowProgressStore
    {
        public int Saves { get; private set; }
        public GlowProgress Load() => GlowProgress.Fresh;
        public void Save(GlowProgress state) => Saves++;
    }
}

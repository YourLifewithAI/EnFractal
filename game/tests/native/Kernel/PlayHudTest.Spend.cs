using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using EnFractal.Native;
using EnFractal.Native.Kernel;
using EnFractal.Native.Ui;
using Godot;

namespace EnFractal.Tests.Kernel;

/// <summary>
/// The mechanics round's follow-up to the meters (the founder's answers of 15 October, docs/game/MECHANICS.md decisions 38 and 41),
/// in the real room with the real HUD, headless. The Gubble's pips are a small row under the stamina bar while it is out of frame
/// (hearts, then the bar, then pips), the shadow pool in its grey, "6/8" with the numbers option, and they trail the Gubble again
/// when it is back in frame. Fractals are spent at will through the journal's Character page, with no key of their own: the page
/// shows the Fractals in hand and, at the camp, the two buttons with their prices; away it says to go back; a spend through it
/// grows the hearts or the bar the meters draw; a refusal is the host's own words, shown as the notice; the companion's connection
/// cannot spend them; a short hint at the camp points to the journal.
/// </summary>
public partial class PlayHudTest
{
    /// <summary>A body in Challenge holding this many unspent Fractals, with these bought; health full, the starting bar.</summary>
    private static MeterStore BodyHolding(int unspent, int heartsBought = 0, int bandsBought = 0)
    {
        var fractals = new PlayerFractals(unspent + 5 * (heartsBought + bandsBought), unspent, heartsBought, bandsBought);
        var body = PlayerVitals.Of(100f, 100f, 0.4f, 0.4f, Array.Empty<string>(), 0, fractals, PlayerVitals.UnsetPool);
        return new MeterStore(PlayerModesProfile.Fresh.Select("challenge").WithCurrent(body));
    }

    private Vector3 CampAt(CommandHost host) => host.Room.SpawnFor("player").PositionM;

    /// <summary>The player on the camp (the spawn), as the host sees it.</summary>
    private bool StandAtCamp(CommandHost host) => _world.Player.TryTeleportTo(CampAt(host)) && host.PlayerAtCamp();

    /// <summary>The player a little way from the camp (past its half metre), on a spot the body can stand on.</summary>
    private bool StandAwayFromCamp(CommandHost host)
    {
        foreach (var offset in new[] { new Vector3(0.8f, 0, 0), new Vector3(-0.8f, 0, 0), new Vector3(0, 0, 0.8f), new Vector3(0, 0, -0.8f), new Vector3(1.2f, 0, 0), new Vector3(0, 0, 1.2f) })
            if (_world.Player.TryTeleportTo(CampAt(host) + offset) && !host.PlayerAtCamp()) return true;
        return false;
    }

    // ---- the pips under the bar when the Gubble is out of frame ----

    private async Task TestPipsMarker()
    {
        var host = CommandHost.Of(_world)!;
        var player = _world.Player;
        var companion = _world.Companion;
        var marker = _hud.PipsRow;
        var home = player.GlobalPosition;
        player.ReadKeyboard = false;
        _hud.LightForTests = _ => 0.9f;
        _hud.AimRayForTests = Air();
        FreshBodies(host);
        host.PlayerMode("challenge");
        host.PlayerGoal("stay");
        // First person, so that turning round is turning away: the Gubble waits beside the player, within a stride.
        _hud.SetViewMode(0);
        await Face(player, companion.GlobalPosition - player.GlobalPosition);
        await Frames(8);
        var spot = FloorNearGubble(0.3f);
        var column = _hud.FindChild("Meters", true, false)!.GetParent<Control>();

        // In frame the pips trail the Gubble, as ever, and the row under the bar is not drawn.
        Check(_hud.Pips.Following && !marker.Shown && marker.MaxPips == 8 && marker.Pips == 8 && marker.Rows == 1,
            $"the Gubble in frame: its pips trail it and the row under the bar is not drawn ({marker.Shown}, {_hud.Pips.Following})");
        var layoutInFrame = column.GetCombinedMinimumSize();
        var hearts = _hud.Hearts.GetGlobalRect();
        var bar = _hud.Bar.GetGlobalRect();
        var row = marker.GetGlobalRect();
        Check(hearts.End.Y <= bar.Position.Y + 0.5f && bar.End.Y <= row.Position.Y + 0.5f && row.Size.X >= 8 * PipsMarker.PitchPx - 0.01f && row.Size.Y >= PipsMarker.PitchPx - 0.01f &&
              Mathf.Abs(row.Position.X - bar.Position.X) < 1f,
            $"the stack reads hearts on top, then the stamina bar, then the pips (hearts to {hearts.End.Y:0}, bar {bar.Position.Y:0} to {bar.End.Y:0}, pips from {row.Position.Y:0})");
        Check(marker.GetParent() == _hud.Hearts.GetParent().GetParent() && marker.GetIndex() == _hud.Bar.GetParent().GetIndex() + 1,
            "the marker is the status panel's third row, straight under the bar's");

        // Turned away, the Gubble is behind the eye: the pips leave it and sit under the bar.
        await Face(player, player.GlobalPosition - companion.GlobalPosition);
        await Frames(6);
        Check(!_hud.Pips.Following && marker.Shown && marker.Pips == 8 && marker.MaxPips == 8 && !marker.Shadow && marker.Aura.IsEqualApprox(companion.AuraColor ?? companion.AppearanceColor) &&
              marker.Numbers == "",
            "turned away from the Gubble the pips are the row under the bar: eight, in its colour, no numbers");
        Check(column.GetCombinedMinimumSize().IsEqualApprox(layoutInFrame) && marker.GetCombinedMinimumSize().Y >= PipsMarker.PitchPx,
            $"and nothing in the panel moves for it: the row's room is kept whether it is drawn or not ({layoutInFrame} both ways)");
        // The pool is the host's: two pips spent (a Glow is two for an order) and the row says six.
        CastGlow(host, spot);
        await Frames(4);
        Check(host.Vitals.Pips == 6 && marker.Pips == 6 && marker.MaxPips == 8 && _hud.Pips.Pips == 6,
            $"a Glow spends two pips and the row shows six of eight ({marker.Pips}/{marker.MaxPips}), as the trail does");

        // The numbers option reads like the trailing pips do: "Pips 6/8".
        var plainWidth = marker.GetCombinedMinimumSize().X;
        _hud.SetMeterNumbers(true);
        await Frames(4);
        Check(marker.Numbers == "Pips 6/8" && marker.Numbers == _hud.Pips.Numbers && marker.GetCombinedMinimumSize().X > plainWidth + 20,
            $"with the numbers option on the row reads \"{marker.Numbers}\", as the trailing pips do ({_hud.Pips.Numbers})");
        var layoutWithNumbers = column.GetCombinedMinimumSize();

        // Back in frame the pips trail the Gubble again and the row is not drawn; the panel is the same size.
        await Face(player, companion.GlobalPosition - player.GlobalPosition);
        await Frames(6);
        Check(_hud.Pips.Following && !marker.Shown && column.GetCombinedMinimumSize().IsEqualApprox(layoutWithNumbers) && _hud.Pips.Numbers == "Pips 6/8",
            "facing the Gubble again: the pips trail it (with their numbers over them) and the row under the bar is not drawn");

        // The Gubble's visibility, not the camera alone: hidden, the pips cannot trail it, so the row is drawn.
        companion.Visible = false;
        await Frames(4);
        Check(!_hud.Pips.Following && marker.Shown, "a Gubble that is not shown has its pips in the row under the bar");
        companion.Visible = true;
        await Frames(4);
        Check(_hud.Pips.Following && !marker.Shown, "and shown again, they are back round it");
        _hud.SetMeterNumbers(false);

        // Creative: the shadow pool, in its soft grey, the same way.
        host.PlayerMode("creative");
        await Face(player, player.GlobalPosition - companion.GlobalPosition);
        await Frames(6);
        Check(marker.Shown && marker.Shadow && marker.Pips == marker.MaxPips && marker.MaxPips == 8 && host.BodyRules.ShadowPool && marker.Numbers == "",
            "in Creative the row is the shadow pool, the same dots in their grey, and it never reads as spent for real");

        // An empty pool's refusal blinks the pips wherever they are drawn.
        host.PlayerMode("challenge");
        await Face(player, companion.GlobalPosition - player.GlobalPosition);
        FreshBodies(host);
        host.PlayerMode("challenge");
        for (var i = 0; i < 4; i++) CastGlow(host, spot + new Vector3(0.02f * i, 0, 0));
        await Frames(4);
        Check(host.Vitals.Pips == 0 && marker.Pips == 0, "(the pool is empty)");
        _hud._UnhandledInput(Press(Key.Key1));
        await Frames(3);
        Check(_hud.Pips.FlashLeftS > 0 && marker.FlashLeftS > 0, "1 with no pips blinks the trailing pips and the row under the bar together");
        _hud.StopGubble();
        await Frames(50);
        Check(marker.FlashLeftS == 0, "and the blink ends on its own");

        // A pool with no pips has no row at all.
        var alone = new PipsMarker();
        alone.SetPool(0, 0, false, Colors.White);
        alone.Present(true, 0.016);
        Check(!alone.Shown && alone.Rows == 0 && alone.GetCombinedMinimumSize() == Vector2.Zero, "an island with no pool has no marker and takes no room");
        PipsMarker Sized(int pips)
        {
            var sized = new PipsMarker();
            sized.SetPool(3, pips, false, Colors.White);
            return sized;
        }
        var (eight, twentyOne, forty) = (Sized(8), Sized(21), Sized(40));
        Check(eight.Rows == 1 && twentyOne.Rows == 2 && forty.Rows == 2 && eight.GetCombinedMinimumSize().Y > 0 &&
              Mathf.Abs(twentyOne.GetCombinedMinimumSize().Y - 2 * eight.GetCombinedMinimumSize().Y) < 0.01f && forty.GetCombinedMinimumSize().X == 20 * PipsMarker.PitchPx &&
              eight.GetCombinedMinimumSize().X == 8 * PipsMarker.PitchPx,
            "a pool of more than twenty goes onto a second row, and the row is never wider than twenty pips");
        eight.Free();
        twentyOne.Free();
        forty.Free();
        alone.Free();

        companion.Visible = true;
        _hud.AimRayForTests = Air();
        FreshBodies(host);
        host.PlayerMode("creative");
        host.PlayerGoal("stop");
        host.PlayerGoal("stay");
        _hud.SetViewMode(1);
        player.TryTeleportTo(home);
        player.ResetPhysicsInterpolation();
        await Frames(10);
    }

    // ---- spending Fractals through the journal ----

    private Button? PageButton(string name) => _hud.Journal.FindChild(name, true, false) as Button;

    private async Task TestSpendFractalsPage()
    {
        var host = CommandHost.Of(_world)!;
        var player = _world.Player;
        var home = player.GlobalPosition;
        var notice = (Label)_hud.FindChild("Notice", true, false)!;
        player.ReadKeyboard = false;
        _hud.SetViewMode(1);
        FreshBodies(host);
        host.ConfigurePlayerVitals(BodyHolding(3));
        host.PlayerMode("challenge");
        Check(StandAtCamp(host) && host.PlayerAtCamp(), "(the player stands at the camp)");
        // No key was added for it: the journal is the way, where the mouse is free.
        Check(!InputMap.GetActions().Any(a => a.ToString().Contains("spend", StringComparison.OrdinalIgnoreCase) || a.ToString().Contains("fractal", StringComparison.OrdinalIgnoreCase)) &&
              !PlayerControls.Specs.Any(spec => spec.Action.ToString().Contains("spend", StringComparison.OrdinalIgnoreCase)),
            "spending Fractals takes no key of its own: no action in the input map or the controls list");

        _hud.OpenJournal();
        await Frames(JournalMoveFrames);
        var page = _hud.Journal;
        page.OpenPage(JournalPages.Character);
        await Frames(2);
        Check(page.Lines.Any(l => l.Ink == JournalInk.Heading && l.Text == "Fractals") && page.PageText.Contains("You have 3 Fractals: 3 of 5 toward your next spend.", StringComparison.Ordinal) &&
              page.PageText.Contains("You are at the camp: spend them here.", StringComparison.Ordinal),
            "the Character page shows the Fractals in hand, as three of five toward the next spend, and that the player is at the camp");
        var buttons = page.Lines.Where(l => l.Ink == JournalInk.Button).ToArray();
        Check(buttons.Length == 2 && buttons[0].Text == "A new heart · 5 Fractals" && buttons[0].Target == FractalSpend.HeartTarget &&
              buttons[1].Text == "The next band · 5 Fractals" && buttons[1].Target == FractalSpend.BandTarget,
            "and two buttons, A new heart and The next band, each with its price: " + string.Join(" | ", buttons.Select(b => b.Text)));
        Check(buttons.All(b => !b.Enabled) && PageButton("spend_heart") is { Disabled: true } && PageButton("spend_band") is { Disabled: true } &&
              page.PageText.Contains("Find 2 more to spend them.", StringComparison.Ordinal),
            "with three in hand both are dimmed, and the page says how many more to find");
        PageButton("spend_heart")!.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        Check(host.Vitals.Fractals == new PlayerFractals(3, 3, 0, 0) && host.BodyRules.MaxHearts(host.Vitals) == 5 && notice.Text == "A new heart needs 5 Fractals. You have 3: find 2 more!" && page.Footer == notice.Text,
            "even pressed anyway the host refuses and nothing changes; its words are the notice and the page's foot: " + notice.Text);

        // Away from the camp: no buttons, and it says to go back.
        host.ConfigurePlayerVitals(BodyHolding(12));
        host.PlayerMode("challenge");
        Check(StandAwayFromCamp(host), "(the player stands away from the camp)");
        await Frames(JournalRefreshFrames);
        Check(!page.Lines.Any(l => l.Ink == JournalInk.Button) && PageButton("spend_heart") == null && page.PageText.Contains("Fractals are spent at the camp: go back to where you started.", StringComparison.Ordinal) &&
              page.PageText.Contains("You have 12 Fractals: enough for something new!", StringComparison.Ordinal) && page.PageText.Contains("A new heart costs 5 Fractals, the next band 5 Fractals.", StringComparison.Ordinal),
            "away from the camp there are no buttons: the page says to go back, and what each costs");
        var away = _hud.SpendFractals("heart");
        await Frames(2);
        Check(away == CommandHost.FractalsAtCamp && notice.Text == CommandHost.FractalsAtCamp && host.Vitals.Fractals.Unspent == 12 && host.Vitals.Fractals.Hearts == 0,
            "and a spend asked for there is refused in the host's own words, which are the notice: " + notice.Text);

        // At the camp with enough: both buttons live; a heart through the page grows the hearts the meters draw.
        Check(StandAtCamp(host), "(back at the camp)");
        host.ApplyPlayerDamage(30f, PlayerDamageSource.Fall);
        await Frames(JournalRefreshFrames);
        Check(PageButton("spend_heart") is { Disabled: false } && PageButton("spend_band") is { Disabled: false } && page.Lines.Where(l => l.Ink == JournalInk.Button).All(b => b.Enabled) &&
              _hud.Hearts.HeartCount == 5 && _hud.Hearts.MaxQuarters == 20 && _hud.Hearts.Quarters == 14,
            "at the camp with twelve in hand both buttons are live (five hearts, fourteen quarters of twenty)");
        PageButton("spend_heart")!.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        Check(host.Vitals.Fractals == new PlayerFractals(12, 7, 1, 0) && _hud.Hearts.HeartCount == 6 && _hud.Hearts.MaxQuarters == 24 && _hud.Hearts.Quarters == 18 &&
              host.BodyRules.Quarters(host.Vitals) == 18,
            $"A new heart: five Fractals leave the hand and the hearts the meters draw grow to six, the new one full ({_hud.Hearts.Quarters} of {_hud.Hearts.MaxQuarters} quarters)");
        Check(notice.Text == "A new heart! You have 6 hearts now." && page.Footer == notice.Text && page.PageText.Contains("You have 7 Fractals", StringComparison.Ordinal),
            "the notice and the page's foot say so in the pack's words, and the page counts seven in hand: " + notice.Text);
        // The next band through the page grows the bar the meters draw.
        Check(Math.Abs(_hud.Bar.MaxM - 0.4f) < 1e-4f && _hud.Bar.RowCount == 1, "(the bar is its starting 0.4 m)");
        PageButton("spend_band")!.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        Check(host.Vitals.Fractals == new PlayerFractals(12, 2, 1, 1) && Math.Abs(_hud.Bar.MaxM - 0.8f) < 1e-4f && Math.Abs(_hud.Bar.BandM - 0.4f) < 1e-4f &&
              Math.Abs(_hud.Bar.StaminaM - host.Vitals.Stamina) < 0.01f && _hud.Bar.StaminaM > 0.7f,
            $"The next band: the bar the meters draw is 0.8 m, the new band's metres joined to it ({_hud.Bar.StaminaM:0.00} of {_hud.Bar.MaxM:0.00})");
        Check(notice.Text == "The next band is open! Your stamina bar is now 0.8 m." && page.Footer == notice.Text && page.PageText.Contains("You have 2 Fractals: 2 of 5 toward your next spend.", StringComparison.Ordinal) &&
              PageButton("spend_heart") is { Disabled: true } && PageButton("spend_band") is { Disabled: true },
            "the words use the pack's names (stamina bar), and with two left both buttons are dimmed again: " + notice.Text);

        // The player's command alone: the companion's connection is refused it, and nothing changes.
        host.ConfigurePlayerVitals(BodyHolding(12));
        host.PlayerMode("challenge");
        var refused = host.HandleObject(CanonicalJson.Text(new JsonObject
        {
            ["schema"] = "enfractal.command", ["version"] = 1, ["action_id"] = "hud-companion-spend", ["room_id"] = host.Room.RoomId, ["op"] = "fractal.spend",
            ["args"] = new JsonObject { ["grow"] = "heart" },
        }), CommandHost.CompanionPrincipal);
        Check(refused["ok"]?.GetValue<bool>() == false && refused["error"]?["code"]?.GetValue<string>() == "permission_denied" && host.Vitals.Fractals == new PlayerFractals(12, 12, 0, 0),
            "the companion's connection cannot spend the player's Fractals: refused, nothing changed");

        // At every cap there is nothing to buy, and the page says so instead of offering it.
        var capped = PlayerVitals.Of(600f, 600f, 4.0f, 4.0f, Array.Empty<string>(), 0, new PlayerFractals(180, 10, 25, 9), PlayerVitals.UnsetPool);
        host.ConfigurePlayerVitals(new MeterStore(PlayerModesProfile.Fresh.Select("challenge").WithCurrent(capped)));
        host.PlayerMode("challenge");
        await Frames(JournalRefreshFrames);
        Check(!page.Lines.Any(l => l.Ink == JournalInk.Button) && page.PageText.Contains("You have every heart this island allows.", StringComparison.Ordinal) &&
              page.PageText.Contains("You have every band this island allows.", StringComparison.Ordinal) && page.PageText.Contains("There is nothing more for them to grow here", StringComparison.Ordinal) &&
              !FractalSpend.CanAfford(host),
            "at thirty hearts and the tenth band the page offers nothing to spend them on");

        _hud.CloseJournal();
        await Frames(JournalMoveFrames);
        FreshBodies(host);
        host.PlayerMode("creative");
        player.TryTeleportTo(home);
        player.ResetPhysicsInterpolation();
        await Frames(10);
    }

    // ---- the hint at the camp ----

    private async Task TestSpendHint()
    {
        var host = CommandHost.Of(_world)!;
        var player = _world.Player;
        var home = player.GlobalPosition;
        var notice = (Label)_hud.FindChild("Notice", true, false)!;
        _hud.SetViewMode(1);
        FreshBodies(host);
        host.ConfigurePlayerVitals(BodyHolding(5));
        host.PlayerMode("challenge");
        Check(StandAwayFromCamp(host), "(the player stands away from the camp, holding five Fractals)");
        var given = _hud.SpendHintCount;
        // The HUD asks where the camp is without building anything; it must say what the host says, on the camp and off it.
        var answers = new List<bool>();
        var agree = true;
        foreach (var offset in new[] { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1.2f })
        {
            if (!player.TryTeleportTo(CampAt(host) + new Vector3(offset, 0, 0))) continue;
            answers.Add(host.PlayerAtCamp());
            agree &= _hud.AtCamp(host) == host.PlayerAtCamp();
        }
        Check(agree && answers.Contains(true) && answers.Contains(false) && answers.Count >= 4,
            $"the HUD's cheap question about the camp gives the host's answer on and off it ({string.Join(" ", answers.Select(a => a ? "camp" : "away"))})");
        Check(StandAwayFromCamp(host), "(away again)");
        await Frames(40);
        Check(_hud.SpendHintCount == given, "five in hand but away from the camp: no hint");
        Check(StandAtCamp(host), "(the player walks to the camp)");
        await Frames(40);
        Check(_hud.SpendHintCount == given + 1 && notice.Text == _hud.SpendHint &&
              _hud.SpendHint == "You have 5 Fractals! Open the journal (J) and turn to Character to grow a heart or the next band.",
            "at the camp with five in hand a short hint points to the journal: " + notice.Text);
        await Frames(80);
        Check(_hud.SpendHintCount == given + 1, "and it is given once for the arrival, not again while the player stands there");
        // Away and back: once more.
        Check(StandAwayFromCamp(host), "(the player walks away)");
        await Frames(40);
        Check(StandAtCamp(host), "(and comes back)");
        await Frames(40);
        Check(_hud.SpendHintCount == given + 2, "after being away, arriving at the camp gives it again");
        // Fewer than five: nothing to point to.
        host.ConfigurePlayerVitals(BodyHolding(4));
        host.PlayerMode("challenge");
        Check(StandAwayFromCamp(host), "(away, holding four)");
        await Frames(40);
        Check(StandAtCamp(host), "(at the camp, holding four)");
        await Frames(40);
        Check(_hud.SpendHintCount == given + 2, "with four in hand there is no hint at the camp");
        // The book already open: the player is where the buttons are; no hint, and none when it closes.
        Check(StandAwayFromCamp(host), "(away)");
        host.ConfigurePlayerVitals(BodyHolding(5));
        host.PlayerMode("challenge");
        await Frames(40);
        _hud.OpenJournal();
        await Frames(JournalMoveFrames);
        Check(StandAtCamp(host), "(the book open, the player at the camp)");
        await Frames(40);
        _hud.CloseJournal();
        await Frames(JournalMoveFrames + 40);
        Check(_hud.SpendHintCount == given + 2, "with the journal open at the camp no hint is given, nor when it is closed");
        // At every cap there is nothing to spend them on, so no hint.
        var capped = PlayerVitals.Of(600f, 600f, 4.0f, 4.0f, Array.Empty<string>(), 0, new PlayerFractals(180, 10, 25, 9), PlayerVitals.UnsetPool);
        host.ConfigurePlayerVitals(new MeterStore(PlayerModesProfile.Fresh.Select("challenge").WithCurrent(capped)));
        host.PlayerMode("challenge");
        Check(StandAwayFromCamp(host), "(away, every heart and band already bought)");
        await Frames(40);
        Check(StandAtCamp(host), "(at the camp)");
        await Frames(40);
        Check(_hud.SpendHintCount == given + 2, "when every heart and band is bought there is no hint");
        FreshBodies(host);
        host.PlayerMode("creative");
        player.TryTeleportTo(home);
        player.ResetPhysicsInterpolation();
        await Frames(10);
    }

    // ---- what the marker and the hint cost a frame ----

    /// <summary>
    /// The budget again (docs/runs/RUN-2-MECHANICS.md section 9: the meters at most 0.5 ms a frame) with the pips as the row under
    /// the bar: the Gubble out of frame, the numbers on, the hearts shaking at one heart, the pool changing, and five Fractals in
    /// hand at the camp so that the hint's look runs. And steady, where nothing changes: next to nothing allocated.
    /// </summary>
    private async Task TestMarkerFrameCost()
    {
        var host = CommandHost.Of(_world)!;
        var player = _world.Player;
        var companion = _world.Companion;
        var home = player.GlobalPosition;
        _hud.LightForTests = _ => 0.9f;
        _hud.AimRayForTests = Air();
        var hints = _hud.SpendHintCount;
        FreshBodies(host);
        host.ConfigurePlayerVitals(BodyHolding(5));
        host.PlayerMode("challenge");
        host.ApplyPlayerDamage(80f, PlayerDamageSource.Fall);
        host.PlayerGoal("stay");
        _hud.SetViewMode(1);
        await Frames(10);
        var spot = FloorNearGubble(0.3f);
        Check(StandAtCamp(host), "(the player stands at the camp holding five Fractals)");
        companion.Visible = false;
        _hud.SetMeterNumbers(true);
        await Frames(120);
        var marker = _hud.PipsRow;
        Check(marker.Shown && marker.Numbers == $"Pips {host.Vitals.Pips}/8" && _hud.SpendHintCount == hints + 1, "(the row is drawn with its numbers, and the hint has been given)");
        MeterCost.Reset();
        MeterCost.Measuring = true;
        var start = player.GlobalPosition;
        var cast = 0;
        for (var i = 0; i < 600; i++)
        {
            if (i % 60 == 0)
            {
                player.TryTeleportTo(start + new Vector3(0.04f * (i / 60 % 4), 0, 0));
                player.ResetPhysicsInterpolation();
            }
            // The pool changes now and then, so the row is drawn again.
            if (i % 150 == 30 && cast < 3) { CastGlow(host, spot + new Vector3(0.02f * cast, 0, 0)); cast++; }
            await Frames(1);
        }
        MeterCost.Measuring = false;
        player.TryTeleportTo(start);
        player.ResetPhysicsInterpolation();
        var (mean, worst, frames) = (MeterCost.MeanMs, MeterCost.WorstMs, MeterCost.Frames);
        GD.Print($"PLAY_HUD_COST: with the pips as a row under the bar, numbers on, a spend due: mean {mean:0.0000} ms, worst {worst:0.000} ms over {frames} frames, {MeterCost.AllocatedBytes / Math.Max(1, frames)} bytes allocated a frame");
        GD.Print("PLAY_HUD_COST: the row, by piece, ms a frame: " + string.Join(", ", Enum.GetValues<MeterCost.Piece>().Select(p => $"{p} {MeterCost.PieceMeanMs(p):0.0000}")));
        var p99 = MeterCost.SamplesMs.OrderBy(m => m).ElementAt((int)(MeterCost.SamplesMs.Count * 0.99));
        Check(frames >= 550 && cast == 3 && marker.Pips == host.Vitals.Pips && mean < 0.5 && p99 < 0.5,
            $"with the row under the bar and a spend due the meters cost {mean:0.000} ms a frame, 99 in a hundred under {p99:0.000} ms (the worst {worst:0.000} ms), against a budget of 0.5 ms");
        // Steady: the hearts whole and still, the row waits, nothing changes, the hint has been given; the camp is looked at a few times
        // a second (it builds a small object each time), not each frame.
        host.ConfigurePlayerVitals(BodyHolding(5));
        host.PlayerMode("challenge");
        host.PlayerGoal("stay");
        await Frames(120);
        _hud.Map.Read(host);
        await Frames(2);
        var readsBefore = _hud.Map.Builds;
        MeterCost.Reset();
        MeterCost.Measuring = true;
        await Frames(240);
        MeterCost.Measuring = false;
        var perFrame = MeterCost.AllocatedBytes / Math.Max(1, MeterCost.Frames);
        GD.Print($"PLAY_HUD_COST: steady with the row and the hint: {MeterCost.AllocatedBytes} bytes allocated over {MeterCost.Frames} frames ({perFrame} a frame), mean {MeterCost.MeanMs:0.0000} ms");
        Check(MeterCost.Frames >= 200 && _hud.Map.Builds == readsBefore && MeterCost.AllocatedBytes <= 64 && MeterCost.MeanMs < 0.5,
            $"and when nothing changes the row and the hint's look allocate nothing ({MeterCost.AllocatedBytes} bytes over {MeterCost.Frames} frames)");
        companion.Visible = true;
        _hud.SetMeterNumbers(false);
        FreshBodies(host);
        host.PlayerMode("creative");
        host.PlayerGoal("stop");
        host.PlayerGoal("stay");
        player.TryTeleportTo(home);
        player.ResetPhysicsInterpolation();
        await Frames(10);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace EnFractal.Native;

public partial class RoomHud
{
    public const double FireConfirmSeconds = 1.25;
    private OptionButton _modeChoice = null!;
    private Label _modeHelp = null!;
    private Label _weatherHelp = null!;
    private CheckButton _flowerTrail = null!;
    private Label _natureMaterials = null!;
    private OptionButton _repairChoice = null!;
    private Label _repairCost = null!;
    private Button _undoNature = null!;
    private Label _firePreviewWords = null!;
    private Button _fireConfirm = null!;
    private ProgressBar _fireHoldBar = null!;
    private readonly List<string> _repairIds = new();
    private string? _firePreviewId;
    private Vector3 _firePoint;
    private float _fireRadius;
    private bool _fireHolding;
    private double _fireHoldAge;
    public bool FirePreviewReady => _firePreviewId != null;
    public double FireConfirmationProgress => Math.Clamp(_fireHoldAge / FireConfirmSeconds, 0, 1);

    private void BuildNaturePage(VBoxContainer page)
    {
        MagicHeading(page, "YOUR ISLAND");
        _modeChoice = new OptionButton { Name = "ModeChoice" };
        foreach (var mode in new[] { "Creative", "Challenge", "Live" }) _modeChoice.AddItem(mode);
        _modeChoice.ItemSelected += index =>
        {
            if (Host is not { } host) return;
            CancelFirePreview();
            MagicResult(host.PlayerMode(new[] { "creative", "challenge", "live" }[(int)index]), "The island is now in " + _modeChoice.GetItemText((int)index) + " mode.");
        };
        page.AddChild(_modeChoice);
        _modeHelp = WrapMagicLabel(""); page.AddChild(_modeHelp);
        _weatherHelp = WrapMagicLabel(""); _weatherHelp.Name = "WeatherReadout"; page.AddChild(_weatherHelp);
        MagicHeading(page, "FLOWERS");
        _flowerTrail = new CheckButton { Name = "FlowerTrailToggle", Text = "Leave a flower trail" };
        _flowerTrail.Toggled += enabled => { if (Host is { } host) MagicResult(host.PlayerNatureTrail(enabled), enabled ? "The flower trail is on." : "The flower trail is off."); };
        page.AddChild(_flowerTrail);
        AddButton(page, "Bloom at the selected spot", CastBloom, 32);
        page.AddChild(WrapMagicLabel("Rain, light and the ground shape how flowers grow."));
        MagicHeading(page, "FIRE");
        page.AddChild(WrapMagicLabel("Fire can spread inside the previewed area. Creations become scorched and repairable; other things can burn away."));
        AddButton(page, "Preview Fire at this spot", PreviewFire, 32);
        _firePreviewWords = WrapMagicLabel("Choose a spot and preview it before lighting a fire.");
        _firePreviewWords.Name = "FirePreviewWords"; page.AddChild(_firePreviewWords);
        _fireConfirm = new Button { Name = "ConfirmFireButton", Text = "Hold to light this fire", Disabled = true };
        _fireConfirm.ButtonDown += BeginFireConfirmation;
        _fireConfirm.ButtonUp += CancelFireHold;
        _fireConfirm.MouseExited += CancelFireHold;
        page.AddChild(_fireConfirm);
        _fireHoldBar = new ProgressBar { Name = "FireHoldProgress", MinValue = 0, MaxValue = 1, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8) };
        page.AddChild(_fireHoldBar);
        MagicHeading(page, "REPAIR & UNDO");
        _natureMaterials = WrapMagicLabel(""); page.AddChild(_natureMaterials);
        _repairChoice = new OptionButton { Name = "RepairChoice" };
        _repairChoice.ItemSelected += _ => RefreshRepairCost();
        page.AddChild(_repairChoice);
        _repairCost = WrapMagicLabel(""); page.AddChild(_repairCost);
        AddButton(page, "Repair this creation", () =>
        {
            if (Host is not { } host || _repairChoice.Selected < 0 || _repairChoice.Selected >= _repairIds.Count)
            { _magicMessageText = "There are no scorched creations to repair."; return; }
            MagicResult(host.PlayerNatureRepair(_repairIds[_repairChoice.Selected]), "The creation is repaired.");
        }, 32);
        _undoNature = new Button { Name = "NatureUndoButton", Text = "Undo the last nature change" };
        _undoNature.Pressed += () => { if (Host is { } host) { CancelFirePreview(); MagicResult(host.PlayerNatureUndo(), "The last nature change was undone."); } };
        page.AddChild(_undoNature);
    }

    private void CastBloom()
    {
        if (!_magicAim.Hit || _magicAim.AtGubble) { _magicMessageText = "Choose a spot on the ground for Bloom."; RefreshMagicPanel(); return; }
        if (Host is { } host) { CancelFirePreview(); MagicResult(host.PlayerNatureCast("bloom", _magicAim.Point), "Flowers are taking root."); }
    }

    private void PreviewFire()
    {
        CancelFirePreview();
        if (!_magicAim.Hit || _magicAim.AtGubble) { _magicMessageText = "Choose a spot on the ground for Fire."; RefreshMagicPanel(); return; }
        if (Host is not { } host) return;
        var result = host.PlayerNaturePreview("fire", _magicAim.Point);
        if (result["ok"]?.GetValue<bool>() != true) { MagicResult(result, ""); return; }
        _firePreviewId = result["data"]?["preview_id"]?.GetValue<string>();
        _firePoint = _magicAim.Point;
        _fireRadius = result["data"]?["radius_m"]?.GetValue<float>() ?? 1.5f;
        var cells = result["data"]?["cell_count"]?.GetValue<int>() ?? 0;
        var affected = result["data"]?["affected_ids"]?.AsArray().Count ?? 0;
        var approval = result["data"]?["requires_approval"]?.GetValue<bool>() == true;
        _firePreviewWords.Text = $"Fire stays within the outlined {_fireRadius:0.##} m radius. {affected} {(affected == 1 ? "thing may" : "things may")} be affected. " +
            (cells == 0 ? "There is no suitable ground here." : "Release the button to cancel before the hold finishes.") +
            (approval ? " Creations here need a further approval before they can be scorched." : "");
        _fireConfirm.Disabled = _firePreviewId == null || cells == 0;
        NatureVisual?.ShowFirePreview(_firePoint, _fireRadius);
        _magicMessageText = "Review the outlined area, then hold the button if you want this fire.";
        RefreshMagicPanel();
    }

    private global::EnFractal.Native.Look.NatureLook? NatureVisual => GetParent()?.GetNodeOrNull<global::EnFractal.Native.Look.NatureLook>("NatureLook");

    internal void BeginFireConfirmation()
    {
        if (!MagicPanelOpen || _firePreviewId == null || _fireConfirm.Disabled) return;
        _fireHolding = true; _fireHoldAge = 0;
    }

    private void CancelFireHold()
    {
        _fireHolding = false; _fireHoldAge = 0;
        if (_fireHoldBar != null) _fireHoldBar.Value = 0;
    }

    private void CancelFirePreview()
    {
        CancelFireHold(); _firePreviewId = null;
        if (_fireConfirm != null) _fireConfirm.Disabled = true;
        if (_firePreviewWords != null) _firePreviewWords.Text = "Choose a spot and preview it before lighting a fire.";
        NatureVisual?.HideFirePreview();
    }

    internal void TickFireConfirmation(double delta)
    {
        if (!_fireHolding || !MagicPanelOpen || _firePreviewId == null) return;
        if (!_fireConfirm.IsVisibleInTree()) { CancelFireHold(); return; }
        _fireHoldAge += Math.Clamp(delta, 0, .1);
        _fireHoldBar.Value = FireConfirmationProgress;
        if (_fireHoldAge < FireConfirmSeconds) return;
        var token = _firePreviewId; var point = _firePoint; var radius = _fireRadius;
        CancelFirePreview();
        if (Host is { } host) MagicResult(host.PlayerNatureCast("fire", point, radius, 30, token), "The fire is lit. Rain and wind will shape it.");
    }

    private void RefreshNaturePage()
    {
        if (Host is not { } host) return;
        _modeChoice.Select(host.Mode == "challenge" ? 1 : host.Mode == "live" ? 2 : 0);
        _modeHelp.Text = host.Mode == "creative" ? "Explore freely. Climbing uses no stamina, and falls and fire do not hurt you." :
            $"Health {host.Vitals.Health:0}/{host.Vitals.MaxHealth:0} · Stamina {StaminaPercent(host.Vitals.Stamina, host.Vitals.MaxStamina):0}%\nClimbing capacity {host.Vitals.MaxStamina:0.00} m. Climbing uses stamina. Falls and fire can hurt. Completed climbs can extend your capacity.";
        if (host.VitalsNotice.Length > 0) _modeHelp.Text += "\n" + host.VitalsNotice;
        _weatherHelp.Text = $"{WeatherWords(host.NatureWeather.Pattern)} · Wind {host.NatureWeather.WindMps.Length():0.00} m/s\nThe weather changes naturally across the island.";
        _flowerTrail.SetPressedNoSignal(host.Nature.TrailEnabled);
        _flowerTrail.Disabled = !host.NatureConfigured;
        _natureMaterials.Text = host.Mode == "creative" ? "Repairs are free in Creative mode." : "Materials: " + MaterialWords(host.Nature.Wallet);
        var previous = _repairChoice.Selected >= 0 && _repairChoice.Selected < _repairIds.Count ? _repairIds[_repairChoice.Selected] : null;
        var ids = host.Nature.Objects.Values.Where(item => item.Creation && item.Burn > 0).Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (!_repairIds.SequenceEqual(ids) || _repairChoice.ItemCount == 0)
        {
            _repairIds.Clear(); _repairIds.AddRange(ids); _repairChoice.Clear();
            for (var i = 0; i < ids.Length; i++) _repairChoice.AddItem($"Scorched creation {i + 1}");
            if (ids.Length == 0) _repairChoice.AddItem("No scorched creations");
            _repairChoice.Select(Math.Max(0, previous == null ? 0 : _repairIds.IndexOf(previous)));
        }
        _repairChoice.Disabled = ids.Length == 0;
        _undoNature.Disabled = !host.NatureCanUndo;
        RefreshRepairCost();
    }

    private void RefreshRepairCost()
    {
        _repairCost.Text = "";
        if (Host is not { } host || _repairChoice.Selected < 0 || _repairChoice.Selected >= _repairIds.Count || !host.Nature.Objects.TryGetValue(_repairIds[_repairChoice.Selected], out var item)) return;
        _repairCost.Text = host.Mode == "creative" ? "This repair uses no materials in Creative mode." : "Repair quote (one unit per original part): " + MaterialWords(item.RepairCost);
    }

    private static string MaterialWords(IReadOnlyDictionary<string, int> materials) => string.Join(" · ", materials.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item =>
        (item.Key switch { "wood" => "Wood", "stone" => "Stone", "copper" => "Copper", "leaf" => "Leaves", "metal" => "Metal", "glass" => "Glass", "cloth" => "Cloth", _ => "Material" }) + " " + item.Value));
}

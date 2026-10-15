using Godot;
using EnFractal.Native.Kernel;

namespace EnFractal.Native.Ui;

/// <summary>
/// The words on screen for the things the mechanics round names (the pack's names block, docs/game/MECHANICS.md section 8): hearts,
/// stamina, pips, Fractals, expertise, camp and Trust. The pack's words are untrusted text, shown literally and cut to a short
/// plain line; ids and icons never change. A pack without the block keeps today's words (the journal's page was Discoveries).
/// </summary>
public static class MeterWords
{
    public const int MaxLength = 40;

    /// <summary>Today's word for each name, when the pack has no names block (or leaves one out).</summary>
    public static string Today(string key) => key switch
    {
        "hearts" => "Hearts", "stamina" => "Stamina", "pips" => "Pips", "fractals" => "Fractals", "expertise" => "Discoveries",
        "camp" => "Camp", "trust" => "Trust", _ => key,
    };

    /// <summary>The word for a name on this island: the pack's, sanitised, else today's.</summary>
    public static string Name(CommandHost? host, string key)
    {
        if (host != null && GodotObject.IsInstanceValid(host) && host.Rules?.Names is { } names && names.Words.TryGetValue(key, out var word) &&
            KernelJson.DisplayText(word, MaxLength).Trim() is { Length: > 0 } shown) return shown;
        return Today(key);
    }

    /// <summary>A knack's name on this island (the pack's), or null when it names none.</summary>
    public static string? Knack(CommandHost? host, string id) =>
        host?.Rules?.Names?.Knacks.TryGetValue(id, out var word) == true && KernelJson.DisplayText(word, MaxLength).Trim() is { Length: > 0 } shown ? shown : null;
}

using Godot;

namespace EnFractal.Native;

/// <summary>
/// The Gubble's words in the HUD (companion.say; CommandHostSpeech.cs) and the player's grown-ups' test mode: the profile's
/// ai_own_words, off by default, changed only by the player on the Magic panel's Wishes page. The HUD hands it to the host
/// (<see cref="Kernel.CommandHost.CompanionOwnWords"/>), which no request can change, and shows a banner while it is on.
/// </summary>
public partial class RoomHud
{
    /// <summary>The player's grown-ups' test mode, as the profile keeps it (ai_own_words).</summary>
    public bool CompanionOwnWords { get; private set; }
    public GubbleSpeechCaption Speech => _speech;
    private GubbleSpeechCaption _speech = null!;

    private void BuildSpeech()
    {
        CompanionOwnWords = LoadOwnWordsPreference();
        _speech = new GubbleSpeechCaption { Companion = Companion, Cue = _cue, HostOf = () => Host, OwnWordsSetting = () => CompanionOwnWords };
        AddChild(_speech);
    }

    /// <summary>The player's own setting: saved at once in the profile, handed to the host on the next frame.</summary>
    public void SetCompanionOwnWords(bool on)
    {
        CompanionOwnWords = on;
        if (!SaveProfileValue("ai_own_words", on)) _noticeText = "The grown-ups' test mode could not be saved; it lasts this session only.";
    }

    /// <summary>A profile that does not load, or has no such value, leaves the mode off.</summary>
    private static bool LoadOwnWordsPreference()
    {
        var config = new ConfigFile();
        if (config.Load(ProfilePath) != Error.Ok) return false;
        var version = config.GetValue("profile", "version", 0);
        if (version.VariantType != Variant.Type.Int || version.AsInt32() != 1) return false;
        var on = config.GetValue("profile", "ai_own_words", false);
        return on.VariantType == Variant.Type.Bool && on.AsBool();
    }
}

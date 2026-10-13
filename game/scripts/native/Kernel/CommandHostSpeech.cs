using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EnFractal.Native.Room;

namespace EnFractal.Native.Kernel;

/// <summary>
/// companion.say, the Gubble's words in the game (docs/magic/README.md, "The kids"): a caption over its head in the
/// player's view (the HUD reads <see cref="GubbleSaid"/>) and a note in the team's journal written as the AI's.
/// - By default the AI chooses one of the curated phrases below by key: short, written for an eleven-year-old, the same
///   words every time. The one slot, a thing's name, takes only a room object's own name from the room manifest (never a
///   creation's, which an AI or a player wrote), and only for a thing the team knows; anything else is "thing".
/// - The founder's grown-ups' test mode (13 October): a player-only setting, off by default, saved in the player's profile
///   and shown plainly in the HUD while on. Only then may the AI's own words be shown, as untrusted text: one line of at
///   most MaxOwnWords characters, no control or invisible characters, no markup, no links. Nothing a request carries can
///   turn it on: only <see cref="PlayerSetCompanionOwnWords"/>, which the HUD calls from the player's own setting.
/// - Either way the words are bounded in rate (a burst of SpeechBurst, then one every SpeechRefillS) and journalled as a
///   note authored by the companion, untrusted, never a fact.
/// </summary>
public partial class CommandHost
{
    public const int MaxOwnWords = 140;
    public const int SpeechBurst = 3;
    public const double SpeechRefillS = 3.0;
    /// <summary>A phrase's place for a thing's name is filled with this when the name is not a room object's own plain name.</summary>
    public const string UnnamedThing = "thing";

    /// <summary>The Gubble's curated phrases (contracts $defs/gubble_phrase): key, words. "{name}" is the one slot.</summary>
    public static readonly IReadOnlyDictionary<string, string> GubblePhrases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["hello"] = "Hello! I'm here.",
        ["on_my_way"] = "On my way!",
        ["got_it"] = "Got it!",
        ["all_done"] = "All done!",
        ["thinking"] = "Hmm, let me think.",
        ["lets_try"] = "Let me try!",
        ["cannot"] = "I can't do that one.",
        ["not_yet"] = "I can't do that yet.",
        ["try_again"] = "Let's try again.",
        ["oops"] = "Oops!",
        ["wow"] = "Wow!",
        ["look"] = "Look!",
        ["follow_me"] = "Follow me!",
        ["wait_for_me"] = "Wait for me!",
        ["thank_you"] = "Thank you!",
        ["good_idea"] = "Good idea!",
        ["ready"] = "Ready when you are!",
        ["your_turn"] = "Your turn!",
        ["help_me"] = "Can you help me?",
        ["too_far"] = "That's too far for me.",
        ["found_it"] = "I found it!",
        ["here_you_go"] = "Here you go!",
        ["wish_heard"] = "I heard your wish!",
        ["wish_working"] = "Working on your wish!",
        ["good_night"] = "Good night!",
        ["look_at"] = "Look at the {name}!",
        ["found"] = "I found the {name}!",
        ["bring"] = "I'll bring you the {name}.",
        ["going_to"] = "Going to the {name}.",
        ["cannot_reach"] = "I can't reach the {name}.",
        ["like"] = "I like the {name}!",
    };

    /// <summary>The grown-ups' test mode: the AI's own words are shown. Off by default; only the player's own setting turns it on.</summary>
    public bool CompanionOwnWords { get; private set; }
    /// <summary>The HUD's setting, from the player's profile or the player's click. No request reaches this.</summary>
    public void PlayerSetCompanionOwnWords(bool on) => CompanionOwnWords = on;

    /// <summary>What the Gubble said last: its words, whether they are the AI's own, and a serial the HUD watches.</summary>
    public sealed record GubbleSpeech(string Text, bool OwnWords, int Serial);
    public GubbleSpeech? GubbleSaid { get; private set; }

    // Links, addresses and handles are never shown: a scheme, www., a dotted name ending in letters, or an @.
    private static readonly Regex LinkLike = new(@"(://|www\.|[a-z0-9-]+\.[a-z]{2,}\b|@)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // Markup of any kind (tags, BBCode, templates, code) is refused rather than shown.
    private const string MarkupCharacters = "<>[]{}`\\";
    private static readonly Regex PlainName = new(@"\A[\p{L}\p{N}][\p{L}\p{N} '\-]{0,31}\z", RegexOptions.CultureInvariant);
    private double _speechTokens = SpeechBurst;
    private DateTime _speechAt = DateTime.MinValue;

    /// <summary>
    /// Whether text is a curated phrase's words (a phrase with a name filled with a room object's own name, or "thing"):
    /// the journal's curated path, so a page that hides the AI's own words can still show what the Gubble said.
    /// </summary>
    public bool IsGubblePhrase(string text)
    {
        foreach (var words in GubblePhrases.Values)
        {
            if (!words.Contains("{name}")) { if (words == text) return true; continue; }
            var at = words.IndexOf("{name}", StringComparison.Ordinal);
            var before = words[..at];
            var after = words[(at + "{name}".Length)..];
            if (text.Length <= before.Length + after.Length || !text.StartsWith(before, StringComparison.Ordinal) || !text.EndsWith(after, StringComparison.Ordinal)) continue;
            var name = text[before.Length..^after.Length];
            if (name == UnnamedThing || Room.Objects.Any(o => SpeakableName(o) == name)) return true;
        }
        return false;
    }

    private static string SpeakableName(ObjectInstance thing)
    {
        var name = KernelJson.DisplayText(thing.DisplayName ?? thing.Asset.DisplayName, 32).Trim();
        return PlainName.IsMatch(name) ? name : UnnamedThing;
    }

    private static void CheckSayArgs(JsonElement args)
    {
        foreach (var property in args.EnumerateObject())
            if (property.Name is not ("phrase" or "about" or "text"))
                throw new Refusal("field_unknown", "This argument is not part of the operation.", "$.args." + KernelJson.DisplayText(property.Name, 64));
        var phrase = args.TryGetProperty("phrase", out var p);
        var text = args.TryGetProperty("text", out var t);
        if (phrase == text || text && args.TryGetProperty("about", out _))
            throw new Refusal("invalid_args", "Say one of the Gubble's phrases (phrase, with about for a phrase that names a thing), or text in the grown-ups' test mode.", "$.args");
        if (phrase)
        {
            if (p.ValueKind != JsonValueKind.String || !GubblePhrases.ContainsKey(p.GetString()!))
                throw new Refusal("invalid_args", "That is not one of the Gubble's phrases.", "$.args.phrase");
            if (args.TryGetProperty("about", out var about) && (about.ValueKind != JsonValueKind.String || !EntityId.IsMatch(about.GetString()!)))
                throw new Refusal("invalid_args", "about is an entity id.", "$.args.about");
            return;
        }
        if (t.ValueKind != JsonValueKind.String || KernelText.CodePoints(t.GetString()!).Length is < 1 or > MaxOwnWords)
            throw new Refusal("invalid_args", $"The Gubble's own words are one line of 1 to {MaxOwnWords} characters.", "$.args.text");
    }

    private JsonObject Say(JsonElement args, string principal, string actionId, string fingerprint, bool preview)
    {
        if (principal != CompanionPrincipal) throw new Refusal("permission_denied", "Only the player's AI speaks as the Gubble.", "$.op");
        string words;
        bool own;
        if (Str(args, "phrase") is { } key)
        {
            var phrase = GubblePhrases[key];
            var about = Str(args, "about");
            if (phrase.Contains("{name}") != (about != null))
                throw new Refusal("invalid_args", about == null ? "This phrase names a thing: give about." : "This phrase names nothing: leave about out.", "$.args.about");
            if (about != null && !KnownToTeam(about)) throw new Refusal("target_not_found", "That is not in this room.", "$.args.about");
            words = about == null ? phrase : phrase.Replace("{name}", Room.Objects.FirstOrDefault(o => o.Id == about) is { } thing ? SpeakableName(thing) : UnnamedThing);
            own = false;
        }
        else
        {
            if (!CompanionOwnWords)
                throw new Refusal("permission_denied", "Choose one of the Gubble's phrases by key. The AI's own words are shown only in the grown-ups' test mode, which only the player can turn on.", "$.args.text");
            words = OwnWords(Str(args, "text")!);
            own = true;
        }
        if (!SpeechToken(take: !preview)) throw new Refusal("rate_limited", "The Gubble is still speaking; wait a moment.", retryable: true);
        if (preview) return Previewed("companion.say", principal, actionId);
        var entry = new JsonObject
        {
            ["entry_id"] = NewEntryId(), ["kind"] = "note", ["at_utc"] = EntryTime(), ["revision"] = Revision,
            ["author"] = principal, ["text"] = words, ["untrusted"] = true,
        };
        _notes.Add(entry);
        while (_notes.Count > MaxNotes) _notes.RemoveAt(0);
        _teamDirty = true;
        GubbleSaid = new GubbleSpeech(words, own, (GubbleSaid?.Serial ?? 0) + 1);
        return Transient("companion.say", principal, actionId, fingerprint, new JsonArray(), new JsonObject
        {
            ["text"] = words, ["entry_id"] = entry["entry_id"]!.GetValue<string>(), ["own_words"] = own,
        });
    }

    /// <summary>The AI's own words as shown: trimmed; refused with links or markup (control and invisible characters never reach here).</summary>
    private static string OwnWords(string text)
    {
        var words = text.Trim();
        if (words.Length == 0) throw new Refusal("invalid_args", "The Gubble's own words need something to say.", "$.args.text");
        if (LinkLike.IsMatch(words)) throw new Refusal("invalid_args", "Links and addresses are not shown in the game.", "$.args.text");
        if (words.IndexOfAny(MarkupCharacters.ToCharArray()) >= 0) throw new Refusal("invalid_args", "Plain words only: no markup.", "$.args.text");
        return words;
    }

    /// <summary>A burst of SpeechBurst lines, then one every SpeechRefillS, on the host's clock. A preview only asks.</summary>
    private bool SpeechToken(bool take)
    {
        var now = Clock();
        if (_speechAt != DateTime.MinValue) _speechTokens = Math.Min(SpeechBurst, _speechTokens + Math.Max(0, (now - _speechAt).TotalSeconds) / SpeechRefillS);
        _speechAt = now;
        if (_speechTokens < 1) return false;
        if (take) _speechTokens -= 1;
        return true;
    }
}

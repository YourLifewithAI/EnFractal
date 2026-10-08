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
/// The team's journal and map, host side (docs/companion/JOURNAL.md; contracts/README.md "The journal and the map").
/// - The map store replaces the companion's perception memory: one store per team (the player and their AI), filled
///   only by the two avatars' eyes (the companion's on each of its messages, both avatars' on the host's sight sweep;
///   never the camera, never anything the AI says), holding every discovered object and creation as last seen and the
///   discovered space of each level (a walkable surface: the floor, an object's top) as 5 cm cells. It is saved with
///   the room and survives link sessions.
/// - The journal writer records goals as tasks (one entry per task, updated in place), finished fetches, and creations
///   built, changed and removed, with who acted and at whose direction as far as the host can verify it. Not every step,
///   sight or routine move. Lines come from templates and sanitised names, so a name can never write an entry.
/// - journal.note is the sender's own words, marked as such (untrusted), never a fact; journal.read and map.find answer
///   only from the journal and the team's map, so nothing undiscovered can leak through them.
/// Both are kept in the authority's save as the host-owned team block, checked when a save loads.
/// </summary>
public partial class CommandHost
{
    public const int MaxOpenTasks = 32;
    public const int MaxHistory = 500;
    public const int MaxNotes = 100;
    public const int MaxDiscoveredEntities = 1024;
    public const int MaxLevels = 64;
    public const double DiscoverCellM = 0.05;
    /// <summary>Space is discovered within this horizontal distance of an avatar's eye.</summary>
    public const float DiscoverRadiusM = 2.0f;
    /// <summary>At most this many cells are looked at per avatar per sweep, nearest first, so a sweep stays cheap.</summary>
    public const int DiscoverRaysPerSweep = 160;
    /// <summary>The host's sight sweep: both avatars' eyes fill the team's map this often (0 turns it off; tests of one avatar's sight).</summary>
    public static double DefaultTeamSightIntervalS { get; set; } = 0.25;
    public double TeamSightIntervalS { get; set; } = DefaultTeamSightIntervalS;
    /// <summary>Newly discovered space and sightings are saved at most this often (journal entries are saved at once).</summary>
    public double TeamSaveIntervalS { get; set; } = 5.0;

    private const string Base32 = "abcdefghijklmnopqrstuvwxyz234567";
    private static readonly Regex EntryIdPattern = new(@"\Aentry-[a-z2-7]{26}\z", RegexOptions.Compiled);
    private static readonly Regex UtcPattern = new(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,6})?Z\z", RegexOptions.Compiled);
    private static readonly Regex KnownIdPattern = new(@"\A(obj|creation):[A-Za-z0-9_-]{1,64}\z", RegexOptions.Compiled);
    private static readonly Regex SupportPattern = new(@"\A(shell|obj|creation):[A-Za-z0-9_-]{1,64}\z", RegexOptions.Compiled);
    private static readonly HashSet<string> JournalKinds = new() { "task", "built", "changed", "removed", "found", "gathered", "note" };
    private static readonly HashSet<string> Principals = new() { PlayerPrincipal, CompanionPrincipal };
    /// <summary>Goals whose task stays in the history when it ends: a completed action. Following, coming and going somewhere leave none.</summary>
    private static readonly HashSet<string> RecordedGoals = new() { "fetch" };
    /// <summary>Goals that show as a task while they run.</summary>
    private static readonly HashSet<string> TaskGoals = new() { "fetch", "follow", "come", "go_to" };

    private List<JsonObject> _openTasks = new();
    private List<JsonObject> _history = new();
    private List<JsonObject> _notes = new();
    /// <summary>Host-private: each open task's goal, saved with the team block so a later session can close it honestly.</summary>
    private Dictionary<string, string> _taskGoals = new(StringComparer.Ordinal);
    /// <summary>This session: each running job's task entry.</summary>
    private readonly Dictionary<string, string> _taskOfJob = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, Level> _levels = new(StringComparer.Ordinal);
    private double _sightClock;
    private double _saveClock;
    private bool _teamDirty;

    /// <summary>Discovered space on one walkable surface: a grid of cells from (MinX, MinZ), one bit per cell, LSB first.</summary>
    private sealed class Level
    {
        public required string Support { get; init; }
        public double Height { get; set; }
        public double MinX { get; set; }
        public double MinZ { get; set; }
        public int Columns { get; set; }
        public int Rows { get; set; }
        public byte[] Cells { get; set; } = Array.Empty<byte>();
        public bool Get(int c, int r) { var i = r * Columns + c; return (Cells[i >> 3] & (1 << (i & 7))) != 0; }
        public void Set(int c, int r) { var i = r * Columns + c; Cells[i >> 3] |= (byte)(1 << (i & 7)); }
        public int Count => Cells.Sum(b => System.Numerics.BitOperations.PopCount(b));
    }

    // ---- the journal writer ----

    private static string NewEntryId() => "entry-" + System.Security.Cryptography.RandomNumberGenerator.GetString(Base32, 26);

    /// <summary>A name as a fact's line quotes it: display text, at most 60 characters, its own quotes turned to apostrophes.</summary>
    private static string Quoted(string? name)
    {
        var clean = KernelJson.DisplayText(name ?? "", 60).Replace('"', '\'').Trim();
        return "\"" + (clean.Length == 0 ? "something" : clean) + "\"";
    }

    private static string SubjectName(string? name)
    {
        var clean = KernelJson.DisplayText(name ?? "", 80).Trim();
        return clean.Length == 0 ? "something" : clean;
    }

    /// <summary>At whose direction, as a line says it: only what the host can verify.</summary>
    private static string Direction(string actor, string directedBy) =>
        actor == PlayerPrincipal ? "" : directedBy == PlayerPrincipal ? ", at the player's direction" : ", on its own initiative";

    private JsonObject Fact(string kind, string line, string actor, string directedBy, string? subjectId, string? subjectName, Vector3? pin, string? state = null)
    {
        var entry = new JsonObject
        {
            ["entry_id"] = NewEntryId(), ["kind"] = kind, ["at_utc"] = Now(), ["revision"] = Revision,
            ["line"] = KernelJson.DisplayText(line, 200), ["actor"] = actor, ["directed_by"] = directedBy,
        };
        if (subjectId != null) entry["subject"] = new JsonObject { ["entities"] = new JsonArray(subjectId), ["name"] = SubjectName(subjectName) };
        if (pin is { } at) entry["pin_m"] = KernelJson.Vector(at);
        if (state != null) entry["state"] = state;
        return entry;
    }

    private void AddHistory(JsonObject entry)
    {
        _history.Add(entry);
        // The oldest found and gathered entries go first, then other routine ones; built and changed are kept.
        while (_history.Count > MaxHistory)
        {
            var drop = _history.FindIndex(e => e["kind"]!.GetValue<string>() is "found" or "gathered");
            if (drop < 0) drop = _history.FindIndex(e => e["kind"]!.GetValue<string>() is not ("built" or "changed"));
            _history.RemoveAt(drop < 0 ? 0 : drop);
        }
    }

    /// <summary>A goal job started: its task opens in "Working on" (fetch, follow, come, go_to; looking and pointing are not journaled).</summary>
    private void TaskStarted(GoalJob job, JsonObject? known)
    {
        if (!TaskGoals.Contains(job.Goal)) return;
        var actor = job.Actor == CompanionAvatarId ? CompanionPrincipal : PlayerPrincipal;
        var name = known?["display_name"]?.GetValue<string>();
        var line = job.Goal switch
        {
            "fetch" => "Fetching " + Quoted(name) + Direction(actor, job.Principal),
            "follow" => "Following you",
            "come" => "Coming to you",
            _ => "Going to " + Quoted(name),
        };
        var pin = job.Target == PlayerAvatar ? (Vector3?)null : job.Aim.GetCenter();
        var task = Fact("task", line, actor, job.Principal, job.Target, job.Target == PlayerAvatar ? "Player" : name, pin, "active");
        while (_openTasks.Count >= MaxOpenTasks) CloseTask(_openTasks[0], "cancelled");
        _openTasks.Add(task);
        _taskGoals[task["entry_id"]!.GetValue<string>()] = job.Goal;
        _taskOfJob[job.Id] = task["entry_id"]!.GetValue<string>();
        SaveTeam();
    }

    /// <summary>A goal job ended: its task closes; a fetch stays in the history as done, failed or stopped, the others leave none.</summary>
    private void TaskEnded(GoalJob job)
    {
        if (!_taskOfJob.Remove(job.Id, out var entryId)) return;
        var task = _openTasks.FirstOrDefault(t => t["entry_id"]!.GetValue<string>() == entryId);
        if (task == null) return;
        CloseTask(task, job.State switch { "succeeded" => "done", "failed" => "failed", _ => "cancelled" });
        SaveTeam();
    }

    private void CloseTask(JsonObject task, string state)
    {
        var entryId = task["entry_id"]!.GetValue<string>();
        _openTasks.Remove(task);
        _taskGoals.Remove(entryId, out var goal);
        if (goal == null || !RecordedGoals.Contains(goal)) return;
        var name = Quoted(task["subject"]?["name"]?.GetValue<string>());
        var actor = task["actor"]!.GetValue<string>();
        var directedBy = task["directed_by"]!.GetValue<string>();
        var line = state switch { "done" => "Fetched " + name, "failed" => "Could not fetch " + name, _ => "Stopped fetching " + name } + Direction(actor, directedBy);
        var closed = (JsonObject)task.DeepClone();
        closed["line"] = KernelJson.DisplayText(line, 200);
        closed["state"] = state;
        closed["at_utc"] = Now();
        closed["revision"] = Revision;
        AddHistory(closed);
    }

    /// <summary>A creation was built, changed or removed: a fact, with who acted and at whose direction (the player when the player approved it).</summary>
    private void CreationFact(string op, string principal, string? approvedBy, JsonObject result, JsonObject? before)
    {
        var directedBy = approvedBy ?? principal;
        var id = op == "creation.place" ? result["created"]?[0]?.GetValue<string>() : result["affected"]?[0]?.GetValue<string>();
        if (id == null) return;
        var now = op == "entity.remove" ? before : Entities().FirstOrDefault(e => e["id"]!.GetValue<string>() == id);
        if (now == null) return;
        var name = now["display_name"]?.GetValue<string>();
        var (kind, verb) = op switch { "creation.place" => ("built", "built"), "creation.revise" => ("changed", "changed"), _ => ("removed", "removed") };
        var line = principal == PlayerPrincipal ? $"You {verb} {Quoted(name)}" : $"{char.ToUpperInvariant(verb[0])}{verb[1..]} {Quoted(name)}{Direction(principal, directedBy)}";
        AddHistory(Fact(kind, line, principal, directedBy, id, name, BoundsOf(now).GetCenter()));
        SaveTeam();
    }

    // ---- journal.note, journal.read, map.find ----

    private JsonObject Note(JsonElement args, string principal, string actionId, Godot.Collections.Dictionary meta, bool preview)
    {
        var text = Str(args, "text")!;
        if (preview) return Previewed("journal.note", principal, actionId);
        var entry = new JsonObject
        {
            ["entry_id"] = NewEntryId(), ["kind"] = "note", ["at_utc"] = Now(), ["revision"] = Revision,
            ["author"] = principal, ["text"] = text, ["untrusted"] = true,
        };
        var notes = _notes.Select(n => n).Append(entry).ToList();
        while (notes.Count > MaxNotes) notes.RemoveAt(0);
        var team = ExportTeam(notes);
        var request = new Godot.Collections.Dictionary
        {
            ["op"] = "note", ["action_id"] = actionId, ["expected_revision"] = Revision, ["expected_permission_revision"] = PermissionRevision,
            ["team"] = KernelJson.ToVariant(team), ["entry_id"] = entry["entry_id"]!.GetValue<string>(),
        };
        var result = Submit(request, principal, actionId, meta);
        _notes = notes;
        _teamDirty = false;
        return result;
    }

    private JsonObject JournalRead(JsonElement args, string principal)
    {
        var kind = Str(args, "kind");
        var about = Str(args, "about");
        var since = Str(args, "since_utc");
        bool Match(JsonObject e) =>
            (kind == null || e["kind"]!.GetValue<string>() == kind) &&
            (about == null || (e["subject"]?["entities"]?.AsArray().Any(x => x!.GetValue<string>() == about) ?? false)) &&
            (since == null || string.CompareOrdinal(e["at_utc"]!.GetValue<string>(), since) >= 0);
        var jobOfTask = _taskOfJob.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);
        var open = _openTasks.Where(Match).Reverse().Select(t =>
        {
            var copy = (JsonObject)t.DeepClone();
            // A task's job id only for the principal whose job it is, while it runs: the handle jobs.status answers.
            if (jobOfTask.TryGetValue(t["entry_id"]!.GetValue<string>(), out var jobId) && _jobs.TryGetValue(principal, out var mine) && mine.Any(j => j.Id == jobId && j.State == "running"))
                copy["job_id"] = jobId;
            return (JsonNode?)copy;
        }).ToArray();
        var others = _history.Select((e, i) => (Entry: e, Order: i)).Concat(_notes.Select((e, i) => (Entry: e, Order: i)))
            .Where(p => Match(p.Entry))
            .OrderByDescending(p => p.Entry["revision"]!.GetValue<int>()).ThenByDescending(p => p.Entry["at_utc"]!.GetValue<string>(), StringComparer.Ordinal).ThenByDescending(p => p.Order)
            .Select(p => p.Entry).ToList();
        var start = 0;
        if (args.TryGetProperty("cursor", out var cursor) && (!int.TryParse(cursor.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out start) || start < 0 || start > others.Count))
            throw new Refusal("invalid_args", "That cursor is not from this journal.", "$.args.cursor");
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 20;
        var page = others.Skip(start).Take(limit).Select(e => (JsonNode?)e.DeepClone()).ToArray();
        var data = new JsonObject { ["open_tasks"] = new JsonArray(open), ["entries"] = new JsonArray(page) };
        if (start + page.Length < others.Count) data["next_cursor"] = (start + page.Length).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return data;
    }

    /// <summary>
    /// "Where have we seen this?": only from the team's map (objects and creations the avatars' eyes found), nearest first.
    /// Each is as the team knows it: in the companion's sight now, live; otherwise as last seen, marked. An empty answer
    /// looks the same whether or not such a thing exists.
    /// </summary>
    private JsonObject MapFind(JsonElement args, string principal)
    {
        var group = Str(args, "category_group");
        var name = Str(args, "name")?.ToLowerInvariant();
        var body = principal == PlayerPrincipal ? (SmallPlayerController?)Player : Companion;
        var from = args.TryGetProperty("near_m", out var near) ? KernelJson.ReadVector(near) : body != null && IsInstanceValid(body) ? body.GlobalPosition : Vector3.Zero;
        var limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 5;
        var live = Entities().ToDictionary(e => e["id"]!.GetValue<string>(), StringComparer.Ordinal);
        var items = new List<(float Distance, string Id, JsonObject Entity)>();
        foreach (var (id, entry) in TeamMemory().Entries)
        {
            if (!KnownIdPattern.IsMatch(id)) continue;
            JsonObject entity;
            if (principal == PlayerPrincipal) entity = live.TryGetValue(id, out var now) ? now : RememberedSummary(entry);
            else if (Perceives(principal, id) && live.TryGetValue(id, out var seen)) { entity = (JsonObject)seen.DeepClone(); entity["seen"] = "now"; }
            else entity = RememberedSummary(entry);
            if (group != null && entity["category_group"]?.GetValue<string>() != group) continue;
            if (name != null && !(entity["display_name"]!.GetValue<string>().ToLowerInvariant().Contains(name, StringComparison.Ordinal) ||
                                  (entity["category"]?.GetValue<string>().ToLowerInvariant().Contains(name, StringComparison.Ordinal) ?? false))) continue;
            items.Add((Distance(entity, from), id, entity));
        }
        var found = items.OrderBy(i => i.Distance).ThenBy(i => i.Id, StringComparer.Ordinal).Take(limit)
            .Select(i => (JsonNode?)new JsonObject { ["entity"] = i.Entity, ["distance_m"] = Math.Min(3500, Math.Round(i.Distance, 3)) }).ToArray();
        return new JsonObject { ["items"] = new JsonArray(found) };
    }

    // ---- the team's map: both avatars' eyes ----

    /// <summary>The team's map (one team in single player): the store the companion's perception memory became.</summary>
    private PerceptionMemory TeamMemory() => MemoryOf(CompanionPrincipal);

    /// <summary>The host's sight sweep: what each avatar's eyes see goes into the team's map, entities and space.</summary>
    private void SightSweep(double delta)
    {
        if (TeamSightIntervalS <= 0 || Authority == null || !Authority.Call("is_ready").AsBool()) return;
        _sightClock += delta;
        _saveClock += delta;
        if (_sightClock >= TeamSightIntervalS)
        {
            _sightClock = 0;
            var entities = Entities();
            RefreshLevels(entities);
            foreach (var (body, avatar) in new (SmallPlayerController?, string)[] { (Player, PlayerAvatar), (Companion, CompanionAvatarId) })
            {
                if (body == null || !IsInstanceValid(body) || !body.IsInsideTree()) continue;
                Remember(body, avatar, entities, Perceive(body, avatar, entities));
                Discover(body);
            }
            _teamDirty = true;
        }
        if (_teamDirty && _saveClock >= TeamSaveIntervalS) SaveTeam();
    }

    /// <summary>Levels from the room as it is: each floor part, and the top of each object with a walkable top, where it stands now.</summary>
    private void RefreshLevels(List<JsonObject> entities)
    {
        foreach (var entity in entities)
        {
            var id = entity["id"]!.GetValue<string>();
            var kind = entity["kind"]!.GetValue<string>();
            var floor = kind == "shell" && Room.Shell.Any(p => p.Id == id && p.Role == "floor");
            var top = kind == "object" && entity["held_by"] == null && entity["affordances"]!.AsArray().Any(a => a!.GetValue<string>() == "walkable_top");
            if (!floor && !top) continue;
            // Space is the room's: a floor part that runs under the walls is cut at the room's bounds.
            var whole = BoundsOf(entity);
            var low = new Vector3(Mathf.Max(whole.Position.X, Room.Bounds.Position.X), whole.Position.Y, Mathf.Max(whole.Position.Z, Room.Bounds.Position.Z));
            var high = new Vector3(Mathf.Min(whole.End.X, Room.Bounds.End.X), whole.End.Y, Mathf.Min(whole.End.Z, Room.Bounds.End.Z));
            if (high.X <= low.X || high.Z <= low.Z) continue;
            var box = new Aabb(low, high - low);
            var columns = Math.Clamp((int)Math.Ceiling(box.Size.X / DiscoverCellM - 1e-6), 1, 1024);
            var rows = Math.Clamp((int)Math.Ceiling(box.Size.Z / DiscoverCellM - 1e-6), 1, 1024);
            if (!_levels.TryGetValue(id, out var level))
            {
                if (_levels.Count >= MaxLevels) continue;
                _levels[id] = level = new Level { Support = id };
            }
            if (level.Columns != columns || level.Rows != rows) level.Cells = new byte[(columns * rows + 7) / 8];
            (level.Columns, level.Rows, level.Height, level.MinX, level.MinZ) = (columns, rows, Math.Round(box.End.Y, 4), Math.Round(box.Position.X, 4), Math.Round(box.Position.Z, 4));
        }
    }

    /// <summary>Cells near an avatar's eye that a ray from the eye reaches: discovered. The nearest undiscovered first, a bounded number per sweep.</summary>
    private void Discover(SmallPlayerController body)
    {
        var eye = body.EyeCamera.GlobalPosition;
        var candidates = new List<(float Distance, Level Level, int C, int R, Vector3 Point)>();
        foreach (var level in _levels.Values)
        {
            var c0 = Math.Max(0, (int)Math.Floor((eye.X - DiscoverRadiusM - level.MinX) / DiscoverCellM));
            var c1 = Math.Min(level.Columns - 1, (int)Math.Floor((eye.X + DiscoverRadiusM - level.MinX) / DiscoverCellM));
            var r0 = Math.Max(0, (int)Math.Floor((eye.Z - DiscoverRadiusM - level.MinZ) / DiscoverCellM));
            var r1 = Math.Min(level.Rows - 1, (int)Math.Floor((eye.Z + DiscoverRadiusM - level.MinZ) / DiscoverCellM));
            for (var r = r0; r <= r1; r++)
            for (var c = c0; c <= c1; c++)
            {
                if (level.Get(c, r)) continue;
                var point = new Vector3((float)(level.MinX + (c + 0.5) * DiscoverCellM), (float)level.Height + 0.005f, (float)(level.MinZ + (r + 0.5) * DiscoverCellM));
                var distance = new Vector2(point.X - eye.X, point.Z - eye.Z).Length();
                if (distance <= DiscoverRadiusM) candidates.Add((distance, level, c, r, point));
            }
        }
        var space = body.GetWorld3D().DirectSpaceState;
        var exclude = new Godot.Collections.Array<Rid> { body.GetRid() };
        foreach (var (_, level, c, r, point) in candidates.OrderBy(x => x.Distance).Take(DiscoverRaysPerSweep))
        {
            var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(eye, point, RoomBuilder.WorldLayer, exclude));
            if (hit.Count == 0 || hit["position"].AsVector3().DistanceTo(point) <= 0.015f) level.Set(c, r);
        }
    }

    /// <summary>Test seam: how many cells of a level are discovered (-1 when the level is unknown).</summary>
    internal int DiscoveredCells(string support) => _levels.TryGetValue(support, out var level) ? level.Count : -1;

    /// <summary>Test seam: whether the cell holding a point on a level is discovered.</summary>
    internal bool DiscoveredAt(string support, float x, float z)
    {
        if (!_levels.TryGetValue(support, out var level)) return false;
        var c = (int)Math.Floor((x - level.MinX) / DiscoverCellM);
        var r = (int)Math.Floor((z - level.MinZ) / DiscoverCellM);
        return c >= 0 && r >= 0 && c < level.Columns && r < level.Rows && level.Get(c, r);
    }

    // ---- saving the team block (journal + discovered + the open tasks' goals) ----

    /// <summary>Save the journal and the map now (a fact, a task or the sweep's throttle). Never while the save is unavailable.</summary>
    private void SaveTeam()
    {
        _saveClock = 0;
        if (Authority == null || !Authority.Call("is_ready").AsBool()) return;
        var saved = Authority.Call("set_team", KernelJson.ToVariant(ExportTeam(_notes))).AsGodotDictionary();
        _teamDirty = !saved["ok"].AsBool();
    }

    /// <summary>The team block as the save keeps it: room state's journal and discovered, plus the open tasks' goals.</summary>
    private JsonObject ExportTeam(List<JsonObject> notes) => new()
    {
        ["journal"] = ExportJournal(notes),
        ["discovered"] = ExportDiscovered(),
        ["task_goals"] = new JsonObject(_taskGoals.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, (JsonNode?)JsonValue.Create(p.Value)))),
    };

    /// <summary>Room state's journal block: open tasks (never with a job id), history and notes, each oldest first.</summary>
    public JsonObject ExportJournal() => ExportJournal(_notes);

    private JsonObject ExportJournal(List<JsonObject> notes)
    {
        static JsonArray Copy(IEnumerable<JsonObject> list) => new(list.Select(e => { var c = (JsonObject)e.DeepClone(); c.Remove("job_id"); return (JsonNode?)c; }).ToArray());
        return new JsonObject { ["open_tasks"] = Copy(_openTasks), ["history"] = Copy(_history), ["notes"] = Copy(notes) };
    }

    /// <summary>Room state's discovered block: the levels with anything discovered, and every discovered object and creation as last seen.</summary>
    public JsonObject ExportDiscovered()
    {
        var levels = new JsonArray();
        foreach (var level in _levels.Values.Where(l => l.Count > 0))
            levels.Add(new JsonObject
            {
                ["support"] = level.Support, ["height_m"] = level.Height, ["min_xz_m"] = new JsonArray(level.MinX, level.MinZ),
                ["columns"] = level.Columns, ["rows"] = level.Rows, ["cells"] = Convert.ToBase64String(level.Cells),
            });
        var entities = new JsonObject();
        var memory = TeamMemory();
        foreach (var id in memory.Order.Where(i => KnownIdPattern.IsMatch(i)).TakeLast(MaxDiscoveredEntities).OrderBy(i => i, StringComparer.Ordinal))
        {
            var entry = memory.Entries[id];
            var entity = (JsonObject)entry.Summary.DeepClone();
            foreach (var field in new[] { "seen", "last_seen_ago_s", "last_seen_revision", "may_be_stale", "held_by" }) entity.Remove(field);
            var known = new JsonObject
            {
                ["entity"] = entity, ["last_seen_utc"] = KernelJson.Utc(entry.SeenAt), ["last_seen_revision"] = entry.SeenRevision, ["may_be_stale"] = entry.Changed,
            };
            if (entry.ProtectedBy.Length > 0) known["protected_by"] = entry.ProtectedBy;
            entities[id] = known;
        }
        return new JsonObject { ["cell_m"] = DiscoverCellM, ["levels"] = levels, ["entities"] = entities };
    }

    /// <summary>The authority's team_check: a save's team block is believed only if it reads back whole (TeamProblem).</summary>
    public Godot.Collections.Dictionary CheckTeam(Godot.Collections.Dictionary team, int revision)
    {
        var problem = TeamProblem(Normalized(KernelJson.ToJson(team)), revision);
        return problem == null ? new() { ["ok"] = true } : new() { ["ok"] = false, ["message"] = "The saved journal or map is invalid: " + problem + "; the save was not loaded and is kept as it was." };
    }

    /// <summary>A block as strict JSON reads it (whole numbers as integers), whatever way it arrived (a Godot dictionary reads them as floats).</summary>
    private static JsonObject? Normalized(JsonNode? node)
    {
        try { return node is JsonObject ? JsonNode.Parse(CanonicalJson.Text(node)) as JsonObject : null; }
        catch (Exception) { return null; }
    }

    /// <summary>What is wrong with a team block (or null): its journal, its discovered map, or its open tasks' goals.</summary>
    internal string? TeamProblem(JsonObject? team, int revision)
    {
        if (team == null) return "not an object";
        if (team.Any(p => p.Key is not ("journal" or "discovered" or "task_goals"))) return "an unknown block";
        if (team["journal"] is JsonObject journal && JournalProblem(journal, revision) is { } j) return j;
        if (team["discovered"] is JsonObject discovered && DiscoveredProblem(discovered) is { } d) return d;
        if (team["journal"] is not (null or JsonObject) || team["discovered"] is not (null or JsonObject)) return "a block that is not an object";
        if (team["task_goals"] is { } goals && (goals is not JsonObject map || map.Any(p => !EntryIdPattern.IsMatch(p.Key) || p.Value?.GetValueKind() != JsonValueKind.String || !TaskGoals.Contains(p.Value.GetValue<string>()))))
            return "an open task's goal";
        return null;
    }

    private static string? JournalProblem(JsonObject journal, int storeRevision)
    {
        if (journal.Count != 3 || journal["open_tasks"] is not JsonArray open || journal["history"] is not JsonArray history || journal["notes"] is not JsonArray notes) return "the journal's lists";
        if (open.Count > MaxOpenTasks || history.Count > MaxHistory || notes.Count > MaxNotes) return "the journal's bounds";
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (list, part) in new[] { (open, "open_tasks"), (history, "history"), (notes, "notes") })
        {
            var previous = -1;
            foreach (var node in list)
            {
                if (node is not JsonObject entry || EntryProblem(entry) is { } problem) return $"an entry in {part}" + (node is JsonObject e2 && EntryProblem(e2) is { } p2 ? $" ({p2})" : "");
                var kind = entry["kind"]!.GetValue<string>();
                if (part == "notes" ? kind != "note" : kind == "note") return $"a note out of place in {part}";
                if (part == "open_tasks" ? kind != "task" || entry["state"]!.GetValue<string>() != "active" : kind == "task" && entry["state"]!.GetValue<string>() == "active") return $"a task out of place in {part}";
                if (entry.ContainsKey("job_id")) return "a saved job id";
                if (!ids.Add(entry["entry_id"]!.GetValue<string>())) return "an entry listed twice";
                var revision = entry["revision"]!.GetValue<int>();
                if (revision < previous || revision > storeRevision) return $"the order of {part}";
                previous = revision;
            }
        }
        return null;
    }

    /// <summary>One entry: the contract's fields for its kind (a note carries none of a fact's, a fact none of a note's), display text, ids.</summary>
    private static string? EntryProblem(JsonObject entry)
    {
        static bool Text(JsonNode? node, int max) => node?.GetValueKind() == JsonValueKind.String && node.GetValue<string>() is { Length: > 0 } t &&
            KernelText.CodePoints(t).Length <= max && !KernelText.HasHidden(t);
        static bool Whole(JsonNode? node) => node?.GetValueKind() == JsonValueKind.Number && node.GetValue<double>() is var d && d >= 0 && d == Math.Floor(d) && d < int.MaxValue;
        if (!EntryIdPattern.IsMatch(entry["entry_id"]?.GetValueKind() == JsonValueKind.String ? entry["entry_id"]!.GetValue<string>() : "")) return "entry_id";
        if (entry["kind"]?.GetValueKind() != JsonValueKind.String || !JournalKinds.Contains(entry["kind"]!.GetValue<string>())) return "kind";
        if (entry["at_utc"]?.GetValueKind() != JsonValueKind.String || !UtcPattern.IsMatch(entry["at_utc"]!.GetValue<string>()) || !Whole(entry["revision"])) return "time";
        var kind = entry["kind"]!.GetValue<string>();
        var note = new[] { "author", "text", "untrusted" };
        var fact = new[] { "line", "subject", "actor", "directed_by", "pin_m", "state", "quantity" };
        var allowed = new HashSet<string> { "entry_id", "kind", "at_utc", "revision" };
        allowed.UnionWith(kind == "note" ? note : fact);
        if (entry.Any(p => !allowed.Contains(p.Key))) return "fields";
        if (kind == "note")
            return Principals.Contains(entry["author"]?.GetValueKind() == JsonValueKind.String ? entry["author"]!.GetValue<string>() : "") && Text(entry["text"], 280) &&
                entry["untrusted"]?.GetValueKind() == JsonValueKind.True ? null : "a note";
        if (!Text(entry["line"], 200) || entry["actor"]?.GetValueKind() != JsonValueKind.String || !Principals.Contains(entry["actor"]!.GetValue<string>()) ||
            entry["directed_by"]?.GetValueKind() != JsonValueKind.String || !Principals.Contains(entry["directed_by"]!.GetValue<string>())) return "a fact";
        if ((kind == "task") != entry.ContainsKey("state") || (entry["state"] is { } state && (state.GetValueKind() != JsonValueKind.String || state.GetValue<string>() is not ("active" or "done" or "failed" or "cancelled")))) return "a state";
        if ((kind is "found" or "gathered") != entry.ContainsKey("quantity")) return "a quantity";
        if (entry["subject"] is { } subject && (subject is not JsonObject s || s.Count != 2 || s["entities"] is not JsonArray list || list.Count is < 1 or > 8 ||
            list.Any(x => x?.GetValueKind() != JsonValueKind.String || !EntityId.IsMatch(x.GetValue<string>())) || list.Select(x => x!.GetValue<string>()).Distinct().Count() != list.Count || !Text(s["name"], 80))) return "a subject";
        if (entry["pin_m"] is { } pin && (pin is not JsonArray p || p.Count != 3 || p.Any(v => v?.GetValueKind() != JsonValueKind.Number || Math.Abs(v.GetValue<double>()) > 1000))) return "a pin";
        return null;
    }

    private string? DiscoveredProblem(JsonObject discovered)
    {
        if (discovered.Count != 3 || discovered["cell_m"]?.GetValueKind() != JsonValueKind.Number || Math.Abs(discovered["cell_m"]!.GetValue<double>() - DiscoverCellM) > 1e-9 ||
            discovered["levels"] is not JsonArray levels || discovered["entities"] is not JsonObject entities) return "the discovered map's blocks";
        if (levels.Count > MaxLevels || entities.Count > MaxDiscoveredEntities) return "the discovered map's bounds";
        var supports = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in levels)
        {
            if (node is not JsonObject level || level.Any(p => p.Key is not ("support" or "surface" or "height_m" or "min_xz_m" or "columns" or "rows" or "cells"))) return "a level";
            var support = level["support"]?.GetValueKind() == JsonValueKind.String ? level["support"]!.GetValue<string>() : "";
            if (!SupportPattern.IsMatch(support) || !supports.Add(support)) return "a level's support";
            if (level["columns"]?.GetValueKind() != JsonValueKind.Number || level["rows"]?.GetValueKind() != JsonValueKind.Number) return "a level's grid";
            var columns = level["columns"]!.GetValue<double>();
            var rows = level["rows"]!.GetValue<double>();
            if (columns is < 1 or > 1024 || rows is < 1 or > 1024 || columns != Math.Floor(columns) || rows != Math.Floor(rows)) return "a level's grid";
            if (level["height_m"]?.GetValueKind() != JsonValueKind.Number || level["min_xz_m"] is not JsonArray min || min.Count != 2 || min.Any(v => v?.GetValueKind() != JsonValueKind.Number)) return "a level's place";
            try
            {
                if (level["cells"]?.GetValueKind() != JsonValueKind.String || Convert.FromBase64String(level["cells"]!.GetValue<string>()).Length != ((int)columns * (int)rows + 7) / 8) return "a level's cells";
            }
            catch (FormatException) { return "a level's cells"; }
        }
        foreach (var (id, node) in entities)
        {
            if (!KnownIdPattern.IsMatch(id) || (id.StartsWith("obj:", StringComparison.Ordinal) && Room.Objects.All(o => o.Id != id))) return "a discovered entity's id";
            if (node is not JsonObject known || known.Any(p => p.Key is not ("entity" or "parts" or "protected_by" or "last_seen_utc" or "last_seen_revision" or "may_be_stale"))) return "a discovered entity";
            if (known["entity"] is not JsonObject entity || entity["id"]?.GetValueKind() != JsonValueKind.String || entity["id"]!.GetValue<string>() != id ||
                entity.Any(p => p.Key is "seen" or "last_seen_ago_s" or "last_seen_revision" or "may_be_stale" or "held_by") ||
                entity["display_name"]?.GetValueKind() != JsonValueKind.String || KernelText.HasHidden(entity["display_name"]!.GetValue<string>()) ||
                entity["bounds_m"]?["min_m"] is not JsonArray || entity["bounds_m"]?["max_m"] is not JsonArray || entity["position_m"] is not JsonArray) return "a discovered entity's summary";
            if (known["last_seen_utc"]?.GetValueKind() != JsonValueKind.String || !UtcPattern.IsMatch(known["last_seen_utc"]!.GetValue<string>()) ||
                known["last_seen_revision"]?.GetValueKind() != JsonValueKind.Number || known["may_be_stale"]?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False)) return "a discovered entity's sighting";
            if (known["protected_by"] is { } by && (by.GetValueKind() != JsonValueKind.String || !Principals.Contains(by.GetValue<string>()))) return "a discovered entity's protection";
        }
        return null;
    }

    /// <summary>
    /// Believe a team block (checked by CheckTeam when the save loaded): the journal, the open tasks' goals, the levels and
    /// the discovered entities. A new session closes the tasks an earlier one left open: their jobs did not survive it.
    /// </summary>
    private void LoadTeam(JsonObject? team, bool closeOpenTasks)
    {
        team = Normalized(team);
        _openTasks = new(); _history = new(); _notes = new(); _taskGoals = new(StringComparer.Ordinal);
        _levels.Clear();
        var memory = TeamMemory();
        memory.Clear();
        if (team == null || team.Count == 0 || TeamProblem(team, Revision) != null) return;
        if (team["journal"] is JsonObject journal)
        {
            _openTasks = journal["open_tasks"]!.AsArray().Select(n => (JsonObject)n!.DeepClone()).ToList();
            _history = journal["history"]!.AsArray().Select(n => (JsonObject)n!.DeepClone()).ToList();
            _notes = journal["notes"]!.AsArray().Select(n => (JsonObject)n!.DeepClone()).ToList();
        }
        if (team["task_goals"] is JsonObject goals) foreach (var (key, value) in goals) _taskGoals[key] = value!.GetValue<string>();
        if (team["discovered"] is JsonObject discovered)
        {
            foreach (var node in discovered["levels"]!.AsArray())
            {
                var level = node!.AsObject();
                var support = level["support"]!.GetValue<string>();
                _levels[support] = new Level
                {
                    Support = support, Height = level["height_m"]!.GetValue<double>(), MinX = level["min_xz_m"]![0]!.GetValue<double>(), MinZ = level["min_xz_m"]![1]!.GetValue<double>(),
                    Columns = (int)level["columns"]!.GetValue<double>(), Rows = (int)level["rows"]!.GetValue<double>(), Cells = Convert.FromBase64String(level["cells"]!.GetValue<string>()),
                };
            }
            var known = discovered["entities"]!.AsObject().OrderBy(p => p.Value!["last_seen_utc"]!.GetValue<string>(), StringComparer.Ordinal).ThenBy(p => p.Key, StringComparer.Ordinal);
            foreach (var (id, node) in known)
            {
                var sighting = node!.AsObject();
                memory.Add(id, new Remembered
                {
                    Summary = (JsonObject)sighting["entity"]!.DeepClone(), ProtectedBy = sighting["protected_by"]?.GetValue<string>() ?? "",
                    SeenAt = DateTime.Parse(sighting["last_seen_utc"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
                    SeenRevision = (int)sighting["last_seen_revision"]!.GetValue<double>(), Changed = sighting["may_be_stale"]!.GetValue<bool>(),
                });
            }
        }
        if (!closeOpenTasks || _openTasks.Count == 0) return;
        foreach (var task in _openTasks.ToList()) CloseTask(task, "cancelled");
        SaveTeam();
    }

    /// <summary>Test seam: the team block as it would be saved now.</summary>
    internal JsonObject TeamBlock() => ExportTeam(_notes);
}

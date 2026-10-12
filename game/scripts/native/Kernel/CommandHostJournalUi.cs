using System.Text.Json.Nodes;

namespace EnFractal.Native.Kernel;

public partial class CommandHost
{
    public JsonObject PlayerJournalNote(string text) => PlayerCommand("journal.note", new JsonObject { ["text"] = text });

    public JsonObject PlayerJournal(string? kind = null, string? cursor = null)
    {
        var args = new JsonObject { ["limit"] = 20 };
        if (kind != null) args["kind"] = kind;
        if (cursor != null) args["cursor"] = cursor;
        return HandleObject(new JsonObject { ["schema"] = "enfractal.query", ["version"] = 1,
            ["query_id"] = $"journal-{_session}-{++_counter}",
            ["room_id"] = Room.RoomId, ["op"] = "journal.read", ["args"] = args }.ToJsonString(), PlayerPrincipal);
    }
}

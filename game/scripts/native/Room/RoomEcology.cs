using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EnFractal.Native.Room;

public sealed record EcologyPart(string Id, string Group, string Species, string Role, string Target, Vector3 Root, Aabb Bounds)
{
    public bool Plant => Species.Length > 0;
    public float Ignition => Role is "foliage" or "snow" or "gravel" ? (Species is "grass_tuft" or "grass" ? 0.8f : 5) : Role == "cloth" ? 1.5f : 4;
    public float Fuel => Role is "foliage" or "snow" or "gravel" ? 10 : Role == "cloth" ? 12 : 30;
}

public static class RoomEcology
{
    public static IReadOnlyList<EcologyPart> Read(JsonElement root, IEnumerable<string> entities)
    {
        if (!root.TryGetProperty("extensions", out var x) || !x.TryGetProperty("x_landscape_ecology", out var block)) return Array.Empty<EcologyPart>();
        try
        {
            if (block.GetProperty("version").GetInt32() != 1) throw new Exception();
            var parts = block.GetProperty("parts");
            if (parts.GetArrayLength() > 8192) throw new Exception();
            var known = entities.ToHashSet(); var ids = new HashSet<string>(); var result = new List<EcologyPart>();
            string Token(JsonElement p, string name, bool empty = false)
            {
                var value = p.GetProperty(name).GetString()!;
                if (!(empty && value == "") && !Regex.IsMatch(value, @"\A[a-z][a-z0-9_]{0,63}\z")) throw new Exception();
                return value;
            }
            Vector3 V(JsonElement p, string name)
            {
                var a = p.GetProperty(name).EnumerateArray().Select(v => v.GetSingle()).ToArray();
                if (a.Length != 3 || a.Any(v => !float.IsFinite(v) || Math.Abs(v) > 1000)) throw new Exception();
                return new(a[0], a[1], a[2]);
            }
            foreach (var p in parts.EnumerateArray())
            {
                var id = Token(p, "id"); var target = p.GetProperty("target").GetString()!;
                var role = Token(p, "role"); var min = V(p, "min_m"); var max = V(p, "max_m");
                var species = Token(p, "species", true);
                if (!ids.Add(id) || !known.Contains(target) || !(role is "foliage" or "bark" or "timber" or "wood" or "cloth" || species.Length > 0 && role is "snow" or "gravel") || min.X > max.X || min.Y > max.Y || min.Z > max.Z) throw new Exception();
                result.Add(new(id, Token(p, "group"), species, role, target, V(p, "position_m"), new(min, max-min)));
            }
            return result;
        }
        catch { throw new RoomLoadException("Invalid landscape ecology metadata."); }
    }
}

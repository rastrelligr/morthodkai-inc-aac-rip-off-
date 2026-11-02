using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;
using SERAAC.Core;

namespace SERAAC.Data
{
    // Applies mods' Data/*.json to the *Db lists (full format in Mods/README.md). A file holds any of
    // these sections; each is a list of entries matched by "id":
    //   an id that exists      -> only the fields given are changed
    //   a new id               -> a new entry (fill every field it needs)
    //   "$remove": true        -> the entry is taken out of its list
    // Field names are the C# field names (any case). Values: numbers, strings, enums by name,
    // Ranks as "12", colours as "#rrggbb" or [r, g, b], and other content by id ("frontPool": ["gnawer"]).
    // A list field can be edited instead of replaced: "skills+" appends, "skills-" removes by id,
    // "skills~" changes entries by id.
    // Sections run in the order below across all mods, so an area can use an enemy another mod adds.
    public static class ModContent
    {
        private sealed class Section
        {
            public string Name;
            public Func<IList> List;     // null = change-only (no adding / removing)
            public Type Type;
            public string Key = "Id";    // the field entries are matched on
            public Func<string, object> Find;
        }

        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true,
            IncludeFields = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private static readonly Section[] Sections =
        {
            new() { Name = "enemyTypes", Type = typeof(EnemyTypeDef), Key = "Type",
                    Find = id => Enum.TryParse<EnemyType>(id, true, out var t) ? EnemyTypeDb.Get(t) : null },
            new() { Name = "commonActions", Type = typeof(ActionDef),
                    Find = id => CommonActions.PartySpecials.Append(CommonActions.StepForward).FirstOrDefault(a => a.Id == id) },
            new() { Name = "enemies", Type = typeof(EnemyDef), List = () => EnemyDb.All },
            new() { Name = "characters", Type = typeof(CharacterDef), List = () => CharacterDb.All },
            new() { Name = "equipment", Type = typeof(EquipmentDef), List = () => ItemDb.Equipment },
            new() { Name = "consumables", Type = typeof(ConsumableDef), List = () => ItemDb.Consumables },
            new() { Name = "events", Type = typeof(EventDef), List = () => EventDb.All },
            new() { Name = "quests", Type = typeof(QuestDef), List = () => QuestDb.All },
            new() { Name = "objectives", Type = typeof(ObjectiveDef), List = () => ObjectiveDb.All },
            new() { Name = "areas", Type = typeof(AreaDef), List = () => WorldDb.Areas },
        };

        private static readonly string[] OtherSections = { "barks", "icons" };

        // Everything mods added (not changed), so checks meant for the game's own content can go easier on them.
        public static readonly HashSet<object> Added = new(ReferenceEqualityComparer.Instance);

        // The mod and file being applied, for error messages.
        private static ModInfo _mod;
        private static string _where;

        public static void Apply(IReadOnlyList<ModInfo> mods)
        {
            var files = new List<(ModInfo mod, string name, JsonElement root)>();
            foreach (var mod in mods)
            {
                if (!Directory.Exists(mod.DataDir)) continue;
                foreach (var path in Directory.GetFiles(mod.DataDir, "*.json", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    string name = Path.GetRelativePath(mod.Folder, path).Replace('\\', '/');
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(path),
                            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                        if (doc.RootElement.ValueKind != JsonValueKind.Object) { mod.Error($"{name}: must be a {{ }} object"); continue; }
                        files.Add((mod, name, doc.RootElement.Clone()));
                        foreach (var prop in doc.RootElement.EnumerateObject())
                            if (!Sections.Any(s => Same(s.Name, prop.Name)) && !OtherSections.Any(s => Same(s, prop.Name)) && !prop.Name.StartsWith("$"))
                                mod.Error($"{name}: unknown section '{prop.Name}' (sections: {string.Join(", ", Sections.Select(s => s.Name).Concat(OtherSections))})");
                    }
                    catch (Exception e)
                    {
                        mod.Error($"{name}: {e.Message}");
                    }
                }
            }

            foreach (var section in Sections)
                foreach (var (mod, name, root) in files)
                    if (TryGet(root, section.Name, out var entries))
                    {
                        _mod = mod;
                        if (entries.ValueKind != JsonValueKind.Array) { mod.Error($"{name}: '{section.Name}' must be a [ ] list"); continue; }
                        int i = 0;
                        foreach (var entry in entries.EnumerateArray())
                        {
                            _where = $"{name}: {section.Name}[{i++}]";
                            Guard(() => ApplyEntry(section, entry));
                        }
                    }

            foreach (var (mod, name, root) in files)
            {
                _mod = mod;
                if (TryGet(root, "barks", out var barks)) { _where = name + ": barks"; Guard(() => ApplyBarks(barks)); }
                if (TryGet(root, "icons", out var icons)) { _where = name + ": icons"; Guard(() => ApplyIcons(icons)); }
            }
            _mod = null;
        }

        private static void Guard(Action act)
        {
            try { act(); }
            catch (ModDataException e) { _mod.Error($"{_where}: {e.Message}"); }
            catch (Exception e) { _mod.Error($"{_where}: {e.GetType().Name}: {e.Message}"); }
        }

        private sealed class ModDataException : Exception
        {
            public ModDataException(string message) : base(message) { }
        }

        // ------------------------------------------------------------------ entries

        private static void ApplyEntry(Section section, JsonElement entry)
        {
            if (entry.ValueKind != JsonValueKind.Object) throw new ModDataException("each entry must be a { } object");
            if (!TryGet(entry, section.Key, out var keyEl) || keyEl.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(keyEl.GetString()))
                throw new ModDataException($"needs a \"{Camel(section.Key)}\"");
            string id = keyEl.GetString();
            _where += $" '{id}'";

            var list = section.List?.Invoke();
            object existing = section.Find != null ? section.Find(id) : list?.Cast<object>().FirstOrDefault(x => IdOf(x) == id);
            bool remove = TryGet(entry, "$remove", out var rm) && rm.ValueKind == JsonValueKind.True;

            if (remove)
            {
                if (existing == null) throw new ModDataException("can't remove: nothing has this id");
                if (list == null) throw new ModDataException($"{section.Name} can only be changed, not removed");
                list.Remove(existing);
                _mod.Notes.Add($"removed {section.Name} '{id}'");
                return;
            }
            if (existing == null)
            {
                if (list == null) throw new ModDataException($"no such entry ({section.Name} can only be changed, not added)");
                var created = Activator.CreateInstance(section.Type);
                Patch(created, entry);
                FillDefaults(created);
                list.Add(created);
                Added.Add(created);
                _mod.Notes.Add($"added {section.Name} '{id}'");
            }
            else
            {
                Patch(existing, entry);
                _mod.Notes.Add($"changed {section.Name} '{id}'");
            }
        }

        // Lists a new entry left out would otherwise be null.
        private static void FillDefaults(object obj)
        {
            foreach (var f in obj.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (f.GetValue(obj) == null && f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(List<>))
                    f.SetValue(obj, Activator.CreateInstance(f.FieldType));
            if (obj is AreaDef area)
                foreach (var t in area.Trials) FillDefaults(t);
        }

        // Sets every field named in the JSON object. "name+", "name-" and "name~" edit a list in place.
        private static void Patch(object target, JsonElement obj)
        {
            var type = target.GetType();
            foreach (var prop in obj.EnumerateObject())
            {
                if (prop.Name.StartsWith("$")) continue;
                string name = prop.Name;
                char op = name.Length > 1 && "+-~".Contains(name[^1]) ? name[^1] : '\0';
                if (op != '\0') name = name[..^1];

                var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field == null || field.IsInitOnly)
                {
                    var settable = type.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !f.IsInitOnly && !IsCode(f.FieldType)).Select(f => Camel(f.Name));
                    throw new ModDataException($"{type.Name} has no field '{name}' (fields: {string.Join(", ", settable)})");
                }
                if (IsCode(field.FieldType))
                    throw new ModDataException($"'{Camel(field.Name)}' is code and can't be set from JSON (use a code mod)");

                if (op == '\0')
                {
                    field.SetValue(target, Convert(prop.Value, field.FieldType, Camel(field.Name)));
                    continue;
                }

                if (!(field.GetValue(target) is IList list) || !field.FieldType.IsGenericType)
                    throw new ModDataException($"'{prop.Name}': '{Camel(field.Name)}' is not a list");
                var itemType = field.FieldType.GetGenericArguments()[0];
                if (prop.Value.ValueKind != JsonValueKind.Array) throw new ModDataException($"'{prop.Name}' must be a [ ] list");
                foreach (var item in prop.Value.EnumerateArray())
                {
                    switch (op)
                    {
                        case '+':
                            list.Add(Convert(item, itemType, prop.Name));
                            break;
                        case '-':
                        {
                            string id = item.ValueKind == JsonValueKind.String ? item.GetString() : throw new ModDataException($"'{prop.Name}' takes ids (strings)");
                            var hits = list.Cast<object>().Where(x => IdOf(x) == id).ToList();
                            if (hits.Count == 0) throw new ModDataException($"'{prop.Name}': nothing with id '{id}' in {Camel(field.Name)}");
                            // Pools can name an enemy twice (it sets the odds); "-" takes one copy out.
                            list.Remove(hits[0]);
                            break;
                        }
                        case '~':
                        {
                            if (item.ValueKind != JsonValueKind.Object || !TryGet(item, "id", out var idEl))
                                throw new ModDataException($"'{prop.Name}' takes {{ \"id\": ... }} objects");
                            string id = idEl.GetString();
                            var hit = list.Cast<object>().FirstOrDefault(x => IdOf(x) == id)
                                      ?? throw new ModDataException($"'{prop.Name}': nothing with id '{id}' in {Camel(field.Name)}");
                            Patch(hit, item);
                            break;
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------ values

        private static object Convert(JsonElement el, Type type, string field)
        {
            var under = Nullable.GetUnderlyingType(type);
            if (under != null) return el.ValueKind == JsonValueKind.Null ? null : Convert(el, under, field);
            if (el.ValueKind == JsonValueKind.Null && !type.IsValueType) return null;

            if (type == typeof(Color)) return ParseColor(el, field);
            if (type == typeof(Ranks))
            {
                string s = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ValueKind == JsonValueKind.Number ? el.GetRawText() : null;
                if (s == null) throw new ModDataException($"'{field}': ranks are written as a string of slots, like \"12\" or \"1234\"");
                return Same(s, "all") ? Ranks.All : Same(s, "none") ? Ranks.None : Ranks.Of(s);
            }

            // Content referenced by id.
            if (el.ValueKind == JsonValueKind.String && Lookup(type) is { } lookup)
                return lookup(el.GetString()) ?? throw new ModDataException($"'{field}': no {type.Name.Replace("Def", "").ToLowerInvariant()} with id '{el.GetString()}'");

            // A new object written out in full (a skill in "skills", a trial in "trials", Skyfall Crush in "windUp").
            if (el.ValueKind == JsonValueKind.Object && IsContent(type))
            {
                var obj = Activator.CreateInstance(type);
                Patch(obj, el);
                FillDefaults(obj);
                return obj;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (el.ValueKind != JsonValueKind.Array) throw new ModDataException($"'{field}' must be a [ ] list");
                var itemType = type.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(type);
                foreach (var item in el.EnumerateArray()) list.Add(Convert(item, itemType, field));
                return list;
            }

            try
            {
                return JsonSerializer.Deserialize(el.GetRawText(), type, Json);
            }
            catch (JsonException)
            {
                string expect = type.IsEnum ? "one of " + string.Join(", ", Enum.GetNames(type)) : type.Name;
                throw new ModDataException($"'{field}': {el.GetRawText()} is not a valid {expect}");
            }
        }

        // Content types that can be named by id elsewhere.
        private static Func<string, object> Lookup(Type type)
        {
            if (type == typeof(EnemyDef)) return id => EnemyDb.Get(id);
            if (type == typeof(CharacterDef)) return id => CharacterDb.Get(id);
            if (type == typeof(EquipmentDef)) return id => ItemDb.GetEquipment(id);
            if (type == typeof(ConsumableDef)) return id => ItemDb.GetConsumable(id);
            if (type == typeof(ObjectiveDef)) return id => ObjectiveDb.Get(id);
            if (type == typeof(QuestDef)) return id => QuestDb.Get(id);
            if (type == typeof(EventDef)) return id => EventDb.All.FirstOrDefault(e => e.Id == id);
            // Any skill already in the game, so one can be shared ("skills+": ["pierce"]).
            if (type == typeof(ActionDef)) return id => AllActions().FirstOrDefault(a => a.Id == id);
            // A passive by its kind, reusing the game's text ("passives": ["Frontline"]).
            if (type == typeof(PassiveDef)) return id => CharacterDb.All.SelectMany(c => c.Passives).FirstOrDefault(p => Same(p.Kind.ToString(), id));
            return null;
        }

        private static IEnumerable<ActionDef> AllActions() =>
            CharacterDb.All.SelectMany(c => c.Skills ?? new List<ActionDef>())
                .Concat(EnemyDb.All.SelectMany(e => e.Skills ?? new List<ActionDef>()))
                .Concat(CommonActions.PartySpecials).Append(CommonActions.StepForward);

        private static bool IsContent(Type type) => type.Namespace == typeof(ModContent).Namespace && type.IsClass;

        private static bool IsCode(Type type) => typeof(Delegate).IsAssignableFrom(type);

        private static Color ParseColor(JsonElement el, string field)
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                string s = el.GetString().TrimStart('#');
                if ((s.Length == 6 || s.Length == 8) && uint.TryParse(s, NumberStyles.HexNumber, null, out uint v))
                    return s.Length == 6
                        ? new Color((int)(v >> 16) & 255, (int)(v >> 8) & 255, (int)v & 255)
                        : new Color((int)(v >> 24) & 255, (int)(v >> 16) & 255, (int)(v >> 8) & 255, (int)v & 255);
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                var c = el.EnumerateArray().Select(x => x.TryGetInt32(out int i) ? i : -1).ToArray();
                if ((c.Length == 3 || c.Length == 4) && c.All(i => i >= 0 && i <= 255))
                    return new Color(c[0], c[1], c[2], c.Length == 4 ? c[3] : 255);
            }
            throw new ModDataException($"'{field}': colours are \"#rrggbb\" or [r, g, b] (0-255)");
        }

        // ------------------------------------------------------------------ barks and icons

        // "barks": { "<character id or default>": { "FightStart": ["line", ...], "Kill+": ["added line"] } }
        private static void ApplyBarks(JsonElement barks)
        {
            if (barks.ValueKind != JsonValueKind.Object) throw new ModDataException("must be a { } object");
            foreach (var who in barks.EnumerateObject())
            {
                if (who.Name != BarkDb.Default && CharacterDb.Get(who.Name) == null)
                    throw new ModDataException($"no character with id '{who.Name}' (or use \"{BarkDb.Default}\")");
                if (!BarkDb.Lines.TryGetValue(who.Name, out var own)) BarkDb.Lines[who.Name] = own = new();
                foreach (var trig in who.Value.EnumerateObject())
                {
                    bool append = trig.Name.EndsWith("+");
                    string tname = append ? trig.Name[..^1] : trig.Name;
                    if (!Enum.TryParse<BarkTrigger>(tname, true, out var trigger))
                        throw new ModDataException($"{who.Name}: unknown trigger '{tname}' (triggers: {string.Join(", ", Enum.GetNames<BarkTrigger>())})");
                    var lines = (string[])Convert(trig.Value, typeof(string[]), trig.Name);
                    own[trigger] = append && own.TryGetValue(trigger, out var old) ? old.Concat(lines).ToArray() : lines;
                }
                _mod.Notes.Add($"changed barks for '{who.Name}'");
            }
        }

        // "icons": { "<item id>": ["12 characters", ... 12 rows] } using the ink letters in UI/Icons.cs.
        private static void ApplyIcons(JsonElement icons)
        {
            if (icons.ValueKind != JsonValueKind.Object) throw new ModDataException("must be a { } object");
            foreach (var icon in icons.EnumerateObject())
            {
                var rows = (string[])Convert(icon.Value, typeof(string[]), icon.Name);
                if (rows.Length != 12 || rows.Any(r => r.Length != 12))
                    throw new ModDataException($"icon '{icon.Name}' must be 12 rows of 12 characters");
                UI.Icons.Register(icon.Name, rows);
                _mod.Notes.Add($"set icon '{icon.Name}'");
            }
        }

        // ------------------------------------------------------------------ helpers

        private static string IdOf(object obj)
        {
            if (obj == null) return null;
            var f = obj.GetType().GetField("Id", BindingFlags.Public | BindingFlags.Instance);
            return f?.GetValue(obj) as string;
        }

        private static bool TryGet(JsonElement obj, string name, out JsonElement value)
        {
            foreach (var p in obj.EnumerateObject())
                if (Same(p.Name, name)) { value = p.Value; return true; }
            value = default;
            return false;
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static string Camel(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s[1..];
    }
}

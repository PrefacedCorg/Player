using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Avalonia.Media;
using Player.App.Rules;
using Player.Playback;

namespace Player.App;

/// <summary>
/// 设置持久化（照抄 ClassIsland-2.0 的机制）：
/// <para>
/// · 设置整体（含控制栏行树）存成 exe 旁一份缩进 JSON（settings.json）。
/// · 派生组件设置按控件种类还原：PlayerControlKind 是编译期"组件注册表"，
///   等价于 ClassIsland 按组件类型把 JsonElement 二次反序列化成具体设置类。
/// · 变更即保存：行/组件/容器子级增删、控件设置或规则属性变化立即写盘
///   （订阅树的写法与 <see cref="ControlBarLayoutWatcher"/> 一致），应用退出时再兜底存一次。
/// · 落盘失败只吞不抛：保存不能把应用弄崩。
/// </para>
/// </summary>
public static class PlayerSettingsStore
{
    /// <summary>设置文件路径（exe 同目录，绿色便携）。</summary>
    public static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
            new PlayerSettingsConverter(),
            new PlayerControlItemConverter(),
            new RulesetConverter(),
            new RuleGroupConverter(),
            new TimeRangeRuleSettingsConverter(),
            new RuleConverter(),
            new ColorConverter(),
        },
    };

    /// <summary>当前挂了变更即保存的设置实例（<see cref="OnItemChanged"/> 落盘用）。</summary>
    private static PlayerSettings? _attached;

    /// <summary>读入设置；文件不存在或损坏时返回 null（调用方用默认布局兜底）。</summary>
    public static PlayerSettings? Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<PlayerSettings>(File.ReadAllText(SettingsPath), Options);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>写盘。落盘失败不抛。</summary>
    public static void Save(PlayerSettings settings)
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Options));
        }
        catch
        {
            // 忽略：保存失败不能影响应用运行
        }
    }

    /// <summary>
    /// 变更即保存：任何一层布局集合增删 → 立即保存并重挂订阅树；控件设置/规则/顶层属性
    /// 变化 → 立即保存。订阅模式与 <see cref="ControlBarLayoutWatcher"/> 相同
    /// （集合变化后整体退订重订，新加入的行/组件也会被继续监听）。
    /// </summary>
    public static void Attach(PlayerSettings settings)
    {
        _attached = settings;
        settings.PropertyChanged += OnItemChanged;

        ControlBarLayoutWatcher? watcher = null;
        watcher = new ControlBarLayoutWatcher(() =>
        {
            Save(settings);
            TrackTree();
        });
        TrackTree();
        return;

        void TrackTree()
        {
            watcher!.Track(settings.ControlBar);
            HookItemChanges(settings.ControlBar);
        }
    }

    /// <summary>把控件项（含容器内递归）的属性与设置属性订阅上；先退订再订防重复挂接。</summary>
    private static void HookItemChanges(ObservableCollection<PlayerControlLine> lines)
    {
        foreach (var item in EnumerateItems(lines.SelectMany(line => line.Children)))
        {
            item.PropertyChanged -= OnItemChanged;
            item.PropertyChanged += OnItemChanged;
            item.Settings.PropertyChanged -= OnItemChanged;
            item.Settings.PropertyChanged += OnItemChanged;
        }
    }

    private static IEnumerable<PlayerControlItem> EnumerateItems(IEnumerable<PlayerControlItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            if (item.Children is { } children)
            {
                foreach (var descendant in EnumerateItems(children))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Save(_attached!);

    /// <summary>
    /// Avalonia 颜色的 JSON 形式：#AARRGGBB（Color.ToString / Color.Parse 互逆）。
    /// </summary>
    private sealed class ColorConverter : JsonConverter<Color>
    {
        public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Color.Parse(reader.GetString() ?? throw new JsonException("颜色值缺失"));

        public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    /// <summary>
    /// 一条规则的序列化：id + 反转 + 设置。设置的具体类型由规则注册表按 id 决定
    /// （ClassIsland 按组件类型还原设置的同一做法）。
    /// </summary>
    private sealed class RuleConverter : JsonConverter<Rule>
    {
        public override Rule Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var root = JsonNode.Parse(ref reader) as JsonObject ?? throw new JsonException();
            var id = root["id"]?.GetValue<string>() ?? "";
            var rule = new Rule { Id = id, IsReversed = root["isReversed"]?.GetValue<bool>() ?? false };
            if (root["settings"] is { } node &&
                PlayerRuleRegistry.Rules.TryGetValue(id, out var info) && info.SettingsType is { } type)
            {
                rule.Settings = node.Deserialize(type, options);
            }

            return rule;
        }

        public override void Write(Utf8JsonWriter writer, Rule value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            writer.WriteBoolean("isReversed", value.IsReversed);
            if (value.Settings is { } settings)
            {
                writer.WritePropertyName("settings");
                JsonSerializer.Serialize(writer, settings, settings.GetType(), options);
            }

            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// 一个控件项的序列化：种类/标题/设置/隐藏规则/子控件。子控件放在项的 children 字段
    /// 手工递归——STJ 对 get-only 集合属性（容器设置的 Children）不填充，实测确认会整个跳过，
    /// 所以任何 get-only 集合都不能指望反射路径还原。
    /// </summary>
    private sealed class PlayerControlItemConverter : JsonConverter<PlayerControlItem>
    {
        public override PlayerControlItem Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var root = JsonNode.Parse(ref reader) as JsonObject ?? throw new JsonException();
            var kind = Enum.Parse<PlayerControlKind>(root["kind"]!.GetValue<string>());
            var title = root["title"]?.GetValue<string>() ?? kind.ToString();
            var item = new PlayerControlItem(kind, title);

            // 设置：同类型反序列化出临时对象回填标量（JSON 里不含 children，不会踩 get-only 集合）
            if (root["settings"] is { } settingsNode)
            {
                var loaded = (PlayerControlSettings)settingsNode.Deserialize(item.Settings.GetType(), options)!;
                item.Settings.CopyFrom(loaded);
            }

            if (root["hideOnRule"]?.GetValue<bool>() is { } hide)
            {
                item.HideOnRule = hide;
            }

            if (root["hidingRules"] is { } rulesNode && rulesNode.Deserialize<Ruleset>(options) is { } rules)
            {
                item.HidingRules.CopyFrom(rules);
            }

            // 子控件手工递归（get-only 集合 STJ 不填充）
            if (root["children"] is JsonArray children && item.Children is { } own)
            {
                foreach (var child in children)
                {
                    if (child.Deserialize<PlayerControlItem>(options) is { } childItem)
                    {
                        own.Add(childItem);
                    }
                }
            }

            return item;
        }

        public override void Write(Utf8JsonWriter writer, PlayerControlItem value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("id", value.Id);
            writer.WriteString("kind", value.Kind.ToString());
            writer.WriteString("title", value.Title);
            writer.WriteBoolean("hideOnRule", value.HideOnRule);
            writer.WritePropertyName("hidingRules");
            JsonSerializer.Serialize(writer, value.HidingRules, options);
            writer.WritePropertyName("children");
            JsonSerializer.Serialize(writer, value.Children, options);
            writer.WritePropertyName("settings");
            JsonSerializer.Serialize(writer, value.Settings, value.Settings.GetType(), options);
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// 规则集的序列化：模式/反转 + 规则组列表。Groups 是 get-only 集合（STJ 不填充），
    /// 手工读写。
    /// </summary>
    private sealed class RulesetConverter : JsonConverter<Ruleset>
    {
        public override Ruleset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var root = JsonNode.Parse(ref reader) as JsonObject ?? throw new JsonException();
            // 构造里建了一个默认空组，先清掉再按文件还原
            var ruleset = new Ruleset { Mode = ParseMode(root, RulesetLogicalMode.Or), IsReversed = root["isReversed"]?.GetValue<bool>() ?? false };
            ruleset.Groups.Clear();
            if (root["groups"] is JsonArray groups)
            {
                foreach (var group in groups)
                {
                    if (group.Deserialize<RuleGroup>(options) is { } item)
                    {
                        ruleset.Groups.Add(item);
                    }
                }
            }

            return ruleset;
        }

        public override void Write(Utf8JsonWriter writer, Ruleset value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("mode", value.Mode.ToString());
            writer.WriteBoolean("isReversed", value.IsReversed);
            writer.WritePropertyName("groups");
            JsonSerializer.Serialize(writer, value.Groups, options);
            writer.WriteEndObject();
        }

        /// <summary>读逻辑模式（兼容旧文件里被 JsonStringEnumConverter 写成小写驼峰的形式）。</summary>
        private static RulesetLogicalMode ParseMode(JsonObject root, RulesetLogicalMode fallback) =>
            root["mode"] is { } mode && Enum.TryParse<RulesetLogicalMode>(mode.GetValue<string>(), out var parsed)
                ? parsed
                : fallback;
    }

    /// <summary>规则组的序列化：模式/反转/启用 + 规则列表（Rules 为 get-only 集合，手工读写）。</summary>
    private sealed class RuleGroupConverter : JsonConverter<RuleGroup>
    {
        public override RuleGroup Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var root = JsonNode.Parse(ref reader) as JsonObject ?? throw new JsonException();
            var group = new RuleGroup
            {
                Mode = Enum.TryParse<RulesetLogicalMode>(root["mode"]?.GetValue<string>(), out var parsed) ? parsed : RulesetLogicalMode.And,
                IsReversed = root["isReversed"]?.GetValue<bool>() ?? false,
                IsEnabled = root["isEnabled"]?.GetValue<bool>() ?? true,
            };
            group.Rules.Clear();
            if (root["rules"] is JsonArray rules)
            {
                foreach (var rule in rules)
                {
                    if (rule.Deserialize<Rule>(options) is { } item)
                    {
                        group.Rules.Add(item);
                    }
                }
            }

            return group;
        }

        public override void Write(Utf8JsonWriter writer, RuleGroup value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("mode", value.Mode.ToString());
            writer.WriteBoolean("isReversed", value.IsReversed);
            writer.WriteBoolean("isEnabled", value.IsEnabled);
            writer.WritePropertyName("rules");
            JsonSerializer.Serialize(writer, value.Rules, options);
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// 时间/星期规则的设置序列化：起止时间 + 星期勾选。Weekdays 是 get-only 集合（STJ 不填充），
    /// 按"周一在前、固定七项"的顺序存成布尔数组。
    /// </summary>
    private sealed class TimeRangeRuleSettingsConverter : JsonConverter<TimeRangeRuleSettings>
    {
        public override TimeRangeRuleSettings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var root = JsonNode.Parse(ref reader) as JsonObject ?? throw new JsonException();
            var settings = new TimeRangeRuleSettings
            {
                StartTime = TimeSpan.Parse(root["startTime"]?.GetValue<string>() ?? "00:00:00"),
                EndTime = TimeSpan.Parse(root["endTime"]?.GetValue<string>() ?? "23:59:00"),
            };
            if (root["weekdays"] is JsonArray weekdays)
            {
                for (var i = 0; i < weekdays.Count && i < settings.Weekdays.Count; i++)
                {
                    settings.Weekdays[i].IsSelected = weekdays[i]!.GetValue<bool>();
                }
            }

            return settings;
        }

        public override void Write(Utf8JsonWriter writer, TimeRangeRuleSettings value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("startTime", value.StartTime.ToString());
            writer.WriteString("endTime", value.EndTime.ToString());
            writer.WritePropertyName("weekdays");
            writer.WriteStartArray();
            foreach (var weekday in value.Weekdays)
            {
                writer.WriteBooleanValue(weekday.IsSelected);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }

    /// <summary>设置根对象的序列化：缩放方式 + 控制栏行列表（每行一个 children 数组）。</summary>
    private sealed class PlayerSettingsConverter : JsonConverter<PlayerSettings>
    {
        public override PlayerSettings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var root = JsonNode.Parse(ref reader) as JsonObject ?? throw new JsonException();
            var settings = new PlayerSettings();
            if (root["scalingMode"] is { } mode)
            {
                settings.ScalingMode = Enum.Parse<VideoScalingMode>(mode.GetValue<string>());
            }

            if (root["controlBar"] is JsonArray lines)
            {
                settings.ControlBar.Clear();
                foreach (var lineNode in lines.OfType<JsonObject>())
                {
                    var line = new PlayerControlLine();
                    if (lineNode["children"] is JsonArray children)
                    {
                        foreach (var child in children)
                        {
                            if (child.Deserialize<PlayerControlItem>(options) is { } item)
                            {
                                line.Children.Add(item);
                            }
                        }
                    }

                    settings.ControlBar.Add(line);
                }
            }

            return settings;
        }

        public override void Write(Utf8JsonWriter writer, PlayerSettings value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("scalingMode");
            writer.WriteStringValue(value.ScalingMode.ToString());
            writer.WritePropertyName("controlBar");
            writer.WriteStartArray();
            foreach (var line in value.ControlBar)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("children");
                JsonSerializer.Serialize(writer, line.Children, options);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}

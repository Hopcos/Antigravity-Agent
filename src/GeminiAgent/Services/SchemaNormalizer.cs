using System.Text.Json;

namespace GeminiAgent.Services;

/// <summary>
/// 工具参数 JSON Schema 归一化：
/// 客户端（Agent / zod-to-json-schema）经常产出 Gemini（Vertex / CodeAssist）函数声明
/// 不接受的形式。
/// 修复策略：
///   1. type 允许为数组联合（如 ["string","null"]）→ 取首个非 null 类型 + nullable:true；
///   2. 递归白名单过滤：只保留 Gemini Schema proto 认识的字段，未知键（$defs/$ref/example 等）丢弃。
/// </summary>
public static class SchemaNormalizer
{
    private static readonly HashSet<string> AllowedKeys = new(StringComparer.Ordinal)
    {
        "type", "format", "title", "description", "nullable", "default",
        "items", "properties", "required", "propertyOrdering",
        "minItems", "maxItems", "minLength", "maxLength", "minimum", "maximum",
        "minProperties", "maxProperties", "enum", "pattern", "exampleValue", "anyOf"
    };

    private static readonly HashSet<string> RecursiveKeys = new(StringComparer.Ordinal)
    {
        "items", "anyOf", "not", "additionalProperties", "prefixItems"
    };

    public static JsonElement? Normalize(JsonElement? parameters)
    {
        if (parameters is null || parameters.Value.ValueKind != JsonValueKind.Object)
        {
            return parameters;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteNormalized(writer, parameters.Value);
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
    }

    private static void WriteNormalized(Utf8JsonWriter writer, JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var nullableEmitted = false;
                foreach (var prop in el.EnumerateObject())
                {
                    var name = prop.Name;
                    var value = prop.Value;

                    if (name == "type" && value.ValueKind == JsonValueKind.Array)
                    {
                        nullableEmitted = WriteTypeFromUnion(writer, value);
                    }
                    else if (name == "nullable" && nullableEmitted)
                    {
                        // 已由 type 联合写出，跳过原值
                    }
                    else if (name == "properties" && value.ValueKind == JsonValueKind.Object)
                    {
                        writer.WritePropertyName(name);
                        writer.WriteStartObject();
                        foreach (var sub in value.EnumerateObject())
                        {
                            writer.WritePropertyName(sub.Name);
                            WriteNormalized(writer, sub.Value);
                        }
                        writer.WriteEndObject();
                    }
                    else if (RecursiveKeys.Contains(name))
                    {
                        writer.WritePropertyName(name);
                        WriteNormalized(writer, value);
                    }
                    else if (AllowedKeys.Contains(name))
                    {
                        writer.WritePropertyName(name);
                        value.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in el.EnumerateArray())
                {
                    WriteNormalized(writer, item);
                }
                writer.WriteEndArray();
                break;

            default:
                el.WriteTo(writer);
                break;
        }
    }

    private static bool WriteTypeFromUnion(Utf8JsonWriter writer, JsonElement typeArray)
    {
        var types = typeArray.EnumerateArray()
            .Where(t => t.ValueKind == JsonValueKind.String)
            .Select(t => t.GetString())
            .Where(t => !string.IsNullOrEmpty(t))
            .Cast<string>()
            .ToList();

        var nonNull = types.Where(t => t is not ("null" or "NULL")).ToList();
        var hasNull = types.Any(t => t is "null" or "NULL");

        if (nonNull.Count >= 1)
        {
            writer.WriteString("type", nonNull[0]);
        }

        if (hasNull || nonNull.Count == 0)
        {
            writer.WriteBoolean("nullable", true);
            return true;
        }
        return false;
    }
}

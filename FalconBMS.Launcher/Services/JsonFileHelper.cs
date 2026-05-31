using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FalconBMS.Launcher.Services;

/// <summary>
/// Shared JSON loader for user-editable Launcher data files.
/// 
/// This replaces DataContractJsonSerializer for reads so malformed JSON does not
/// silently load partial data. It also allows for small editing conveniences
/// like comments and trailing commas.
/// </summary>
public static class JsonFileHelper
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        IncludeFields = true,
        PropertyNamingPolicy = new LauncherJsonNamingPolicy(),
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static T? FromJsonFile<T>(string path)
    {
        string json = File.ReadAllText(path);
        return FromJson<T>(json, path);
    }

    public static T? FromJson<T>(string json, string? sourceDescription = null)
    {
        ValidateNoDuplicateProperties(json, sourceDescription);
        return JsonSerializer.Deserialize<T>(json, SerializerOptions);
    }

    private static void ValidateNoDuplicateProperties(string json, string? sourceDescription)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, DocumentOptions);
            ValidateNoDuplicateProperties(document.RootElement, "$");
        }
        catch (JsonException ex)
        {
            string source = string.IsNullOrWhiteSpace(sourceDescription)
                ? "JSON text"
                : sourceDescription!;

            throw new JsonException(FormatJsonExceptionMessage(source, ex), ex);
        }
    }

    private static string FormatJsonExceptionMessage(string source, JsonException ex)
    {
        string message = RemoveSystemTextJsonLocationSuffix(ex.Message);

        if (ex.LineNumber.HasValue && ex.BytePositionInLine.HasValue)
        {
            // System.Text.Json reports zero-based line/position values.
            // Convert them to one-based values so the popup matches what users
            // normally see in text editors.
            long displayLine = ex.LineNumber.Value + 1;
            long displayPosition = ex.BytePositionInLine.Value + 1;

            return $"{source}\nNear line {displayLine}, position {displayPosition}.\n{message}";
        }

        return $"Invalid JSON in {source}\n{message}";
    }

    private static string RemoveSystemTextJsonLocationSuffix(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "The JSON file could not be read.";

        int locationIndex = message.IndexOf(" LineNumber:", StringComparison.Ordinal);

        if (locationIndex >= 0)
            return message.Substring(0, locationIndex).Trim();

        return message.Trim();
    }

    private static void ValidateNoDuplicateProperties(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var propertyNames = new HashSet<string>(StringComparer.Ordinal);

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!propertyNames.Add(property.Name))
                        throw new JsonException($"Duplicate JSON property \"{property.Name}\" found at {path}.");

                    ValidateNoDuplicateProperties(property.Value, $"{path}.{property.Name}");
                }

                break;

            case JsonValueKind.Array:
                int index = 0;

                foreach (JsonElement item in element.EnumerateArray())
                {
                    ValidateNoDuplicateProperties(item, $"{path}[{index}]");
                    index++;
                }

                break;
        }
    }

    /// <summary>
    /// Keeps the current Launcher JSON file shape without requiring every reader DTO
    /// to be converted from DataMember attributes to JsonPropertyName attributes.
    /// 
    /// Examples:
    /// SchemaVersion -> schema_version
    /// AircraftProfile -> aircraft_profile
    /// PidVid -> pidvid
    /// DuplicatePidVidSequenceNumber -> duplicate_pidvid_sequence_number
    /// </summary>
    private sealed class LauncherJsonNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return name;

            var builder = new StringBuilder(name.Length + 8);

            for (int i = 0; i < name.Length; i++)
            {
                char current = name[i];

                if (char.IsUpper(current))
                {
                    bool hasPrevious = i > 0;
                    bool hasNext = i + 1 < name.Length;

                    bool previousIsLowerOrDigit = hasPrevious &&
                                                  (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));

                    bool nextIsLower = hasNext && char.IsLower(name[i + 1]);

                    if (hasPrevious && (previousIsLowerOrDigit || nextIsLower))
                        builder.Append('_');

                    builder.Append(char.ToLowerInvariant(current));
                }
                else
                {
                    builder.Append(current);
                }
            }

            // The existing Launcher JSON uses "pidvid" as one word, not "pid_vid".
            return builder
                .ToString()
                .Replace("pid_vid", "pidvid");
        }
    }
}
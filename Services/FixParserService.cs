using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FIXSniff.Models;

namespace FIXSniff.Services;

public class FixParserService {
    public async Task<ParsedFixMessage> ParseMessageAsync(string rawMessage) {
        var result = new ParsedFixMessage { RawMessage = rawMessage };

        try {
            if (string.IsNullOrWhiteSpace(rawMessage)) {
                result.ErrorMessage = "Message is empty";
                return result;
            }

            // Step 1: Detect FIX version
            var fixVersion = FixVersionDetector.DetectVersion(rawMessage);

            // Step 2: Load specification for this version
            var spec = await FixSpecificationLoader.LoadSpecificationAsync(fixVersion);

            // Step 3: Parse message using specification
            var parsedPairs = SplitIntoPairs(rawMessage);
            if (parsedPairs.Count == 0) {
                throw new InvalidOperationException("Could not parse any FIX fields from the message");
            }

            result.Fields = ProcessFields(parsedPairs, spec);

            // Add version info as first field for display
            result.Fields.Insert(0, new FixFieldInfo {
                TagNumber = "VERSION",
                FieldName = "Detected Version",
                Value = FixVersionDetector.GetDisplayName(fixVersion),
                ParsedValue = $"Using {spec.Fields.Count} field definitions",
                Description = $"Auto-detected FIX version: {fixVersion}\nLoaded {spec.Fields.Count} field definitions from specification\nFields with enum values: {spec.Fields.Values.Count(f => f.Values.Count != 0)}\nRepeating groups: {spec.MessageGroups.Values.SelectMany(m => m.Keys).Distinct().Count()}",
                IndentLevel = 0
            });
        } catch (Exception ex) {
            result.ErrorMessage = $"Parsing failed: {ex.Message}";
        }

        return result;
    }

    private static List<(int Tag, string Value)> SplitIntoPairs(string rawMessage) {
        // Handle different SOH representations
        string[] separators = ["\u0001", "|", "^A"];
        string[]? pairs = null;

        foreach (var separator in separators) {
            pairs = rawMessage.Split([separator], StringSplitOptions.RemoveEmptyEntries);
            if (pairs.Length > 1) break;
        }

        if (pairs is not { Length: > 1 }) {
            pairs = rawMessage.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        }

        var parsedPairs = new List<(int Tag, string Value)>();

        foreach (var pair in pairs) {
            if (string.IsNullOrWhiteSpace(pair)) continue;

            var equalIndex = pair.IndexOf('=');
            if (equalIndex > 0 && equalIndex < pair.Length - 1) {
                var tagStr = pair[..equalIndex];
                var value = pair[(equalIndex + 1)..];

                if (int.TryParse(tagStr, out var tag)) {
                    parsedPairs.Add((tag, value));
                }
            }
        }

        return parsedPairs;
    }

    private static readonly Dictionary<int, FixGroupSpec> EmptyGroups = new();

    private static List<FixFieldInfo> ProcessFields(List<(int Tag, string Value)> parsedPairs, FixSpecification spec) {
        var fields = new List<FixFieldInfo>();
        var groups = ResolveGroups(parsedPairs, spec);
        var i = 0;

        while (i < parsedPairs.Count) {
            ProcessField(parsedPairs, ref i, fields, spec, groups, 0);
        }

        return fields;
    }

    /// <summary>
    /// Resolves the repeating-group definitions that apply to this message from its
    /// MsgType (tag 35). Unknown message types parse flat (no group nesting).
    /// </summary>
    private static IReadOnlyDictionary<int, FixGroupSpec> ResolveGroups(List<(int Tag, string Value)> parsedPairs, FixSpecification spec) {
        var msgType = parsedPairs.FirstOrDefault(p => p.Tag == 35).Value;
        return !string.IsNullOrEmpty(msgType) && spec.MessageGroups.TryGetValue(msgType, out var groups)
            ? groups
            : EmptyGroups;
    }

    /// <summary>
    /// Processes the field at position i; if it is a repeating-group counter
    /// (per the message's spec), its entries are parsed recursively with indentation.
    /// </summary>
    private static void ProcessField(List<(int Tag, string Value)> parsedPairs, ref int i, List<FixFieldInfo> fields, FixSpecification spec, IReadOnlyDictionary<int, FixGroupSpec> groups, int indentLevel) {
        var (tag, value) = parsedPairs[i];
        var field = CreateFieldInfo(tag, value, spec, indentLevel);
        fields.Add(field);
        i++;

        if (!groups.TryGetValue(tag, out var group) || !int.TryParse(value, out var entryCount) || entryCount <= 0)
            return;

        field.FieldName += $" (Repeating Group - {entryCount} entries)";

        for (var entry = 1; entry <= entryCount && i < parsedPairs.Count; entry++) {
            // Each entry must start with the group's delimiter field; otherwise the
            // message deviates from the spec and remaining fields are parsed flat
            if (parsedPairs[i].Tag != group.DelimiterTag)
                break;

            var entryStart = fields.Count;
            var isFirstFieldOfEntry = true;

            while (i < parsedPairs.Count) {
                var nextTag = parsedPairs[i].Tag;
                if (!group.MemberTags.Contains(nextTag)) break; // End of group
                if (nextTag == group.DelimiterTag && !isFirstFieldOfEntry) break; // Next entry

                ProcessField(parsedPairs, ref i, fields, spec, groups, indentLevel + 1);
                isFirstFieldOfEntry = false;
            }

            if (fields.Count > entryStart) {
                fields[entryStart].FieldName += $" (Entry {entry})";
            }
        }
    }

    private static FixFieldInfo CreateFieldInfo(int tag, string value, FixSpecification spec, int indentLevel) {
        var fieldSpec = spec.Fields.GetValueOrDefault(tag);

        return new FixFieldInfo {
            TagNumber = tag.ToString(),
            FieldName = fieldSpec?.Name ?? $"Tag{tag}",
            Value = value,
            IndentLevel = indentLevel,
            // Set parsed value (human-readable meaning)
            ParsedValue = GetParsedValue(tag, value, fieldSpec, spec),
            // Create detailed description including field values
            Description = CreateDetailedDescription(tag, value, fieldSpec)
        };
    }

    private static string GetParsedValue(int tag, string value, FixFieldSpec? fieldSpec, FixSpecification spec) {
        if (fieldSpec != null) {
            if (fieldSpec.Values.TryGetValue(value, out var specValue)) {
                return specValue;
            }

            var caseInsensitiveMatch = fieldSpec.Values.FirstOrDefault(kvp => string.Equals(kvp.Key, value, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(caseInsensitiveMatch.Key)) {
                return caseInsensitiveMatch.Value;
            }
        }

        // MsgType: fall back to the message catalogue when the field has no enum values
        if (tag == 35 && spec.Messages.TryGetValue(value, out var message)) {
            return message.Name;
        }

        return string.Empty;
    }

    private static string CreateDetailedDescription(int tag, string value, FixFieldSpec? fieldSpec) {
        if (fieldSpec == null) {
            return $"Unknown field tag {tag}. Value: {value}";
        }

        var description = $"Field {tag} ({fieldSpec.Name})\n";
        description += $"Type: {fieldSpec.Type}\n";
        description += $"Current Value: {value}\n\n";

        if (!string.IsNullOrEmpty(fieldSpec.Description)) {
            description += $"Description: {fieldSpec.Description}\n\n";
        }

        if (fieldSpec.Values.Count != 0) {
            description += "Possible Values:\n";
            foreach (var kvp in fieldSpec.Values.OrderBy(x => x.Key)) {
                var marker = kvp.Key == value ? " ← CURRENT" : "";
                description += $"  {kvp.Key} = {kvp.Value}{marker}\n";
            }
        }

        return description.Trim();
    }
}

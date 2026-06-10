using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using FIXSniff.Models;

namespace FIXSniff.Services;

public static class FixSpecificationLoader {
    private static readonly HttpClient HttpClient = new();
    private static readonly Dictionary<string, FixSpecification> CachedSpecs = new();

    /// <summary>
    /// Loads FIX specification for a given version: memory cache, then on-disk cache,
    /// then download from the QuickFIX repository, then a minimal built-in fallback.
    /// </summary>
    public static async Task<FixSpecification> LoadSpecificationAsync(string fixVersion) {
        if (CachedSpecs.TryGetValue(fixVersion, out var cached))
            return cached;

        var specFileName = FixVersionDetector.GetSpecFileName(fixVersion);
        var cacheFilePath = Path.Combine(Path.GetTempPath(), $"fixsniff_{specFileName}");

        var spec = await LoadFromCacheFileAsync(cacheFilePath, fixVersion)
                   ?? await DownloadSpecAsync(specFileName, cacheFilePath, fixVersion)
                   ?? CreateFallbackSpec(fixVersion);

        CachedSpecs[fixVersion] = spec;
        return spec;
    }

    private static async Task<FixSpecification?> LoadFromCacheFileAsync(string cacheFilePath, string fixVersion) {
        try {
            if (!File.Exists(cacheFilePath))
                return null;
            return ParseQuickFixXml(await File.ReadAllTextAsync(cacheFilePath), fixVersion);
        } catch {
            return null;
        }
    }

    private static async Task<FixSpecification?> DownloadSpecAsync(string specFileName, string cacheFilePath, string fixVersion) {
        try {
            var url = $"https://raw.githubusercontent.com/quickfix/quickfix/master/spec/{specFileName}";
            var xmlContent = await HttpClient.GetStringAsync(url);
            var spec = ParseQuickFixXml(xmlContent, fixVersion);

            try {
                await File.WriteAllTextAsync(cacheFilePath, xmlContent);
            } catch {
                // Ignore cache save errors
            }

            return spec;
        } catch {
            return null;
        }
    }

    private static FixSpecification ParseQuickFixXml(string xmlContent, string fixVersion) {
        var spec = new FixSpecification { Version = fixVersion };
        var doc = new XmlDocument();
        doc.LoadXml(xmlContent);

        ParseFields(doc, spec);
        ParseMessages(doc, spec);
        ParseGroups(doc, spec);

        return spec;
    }

    private static void ParseFields(XmlDocument doc, FixSpecification spec) {
        var fieldNodes = doc.SelectNodes("/fix/fields/field");
        if (fieldNodes == null) return;

        foreach (XmlNode fieldNode in fieldNodes) {
            var numberAttr = fieldNode.Attributes?["number"]?.Value;
            var nameAttr = fieldNode.Attributes?["name"]?.Value;
            var typeAttr = fieldNode.Attributes?["type"]?.Value;

            if (!int.TryParse(numberAttr, out var fieldNumber) || string.IsNullOrEmpty(nameAttr))
                continue;

            var fieldSpec = new FixFieldSpec {
                Tag = fieldNumber,
                Name = nameAttr,
                Type = typeAttr ?? "STRING",
                Description = GetFieldDescription(fieldNode, fieldNumber)
            };

            ParseFieldValues(fieldNode, fieldSpec);

            spec.Fields[fieldNumber] = fieldSpec;
        }
    }

    private static void ParseFieldValues(XmlNode fieldNode, FixFieldSpec fieldSpec) {
        var valueNodes = fieldNode.SelectNodes("value");
        if (valueNodes == null) return;

        foreach (XmlNode valueNode in valueNodes) {
            var enumAttr = valueNode.Attributes?["enum"]?.Value;
            var descAttr = valueNode.Attributes?["description"]?.Value;

            if (!string.IsNullOrEmpty(enumAttr)) {
                var cleanDescription = (descAttr ?? enumAttr).Replace("_", " ").Trim();
                fieldSpec.Values[enumAttr] = cleanDescription;
            }
        }
    }

    private static void ParseMessages(XmlDocument doc, FixSpecification spec) {
        var messageNodes = doc.SelectNodes("/fix/messages/message");
        if (messageNodes == null) return;

        foreach (XmlNode messageNode in messageNodes) {
            var nameAttr = messageNode.Attributes?["name"]?.Value;
            var msgTypeAttr = messageNode.Attributes?["msgtype"]?.Value;
            var msgCatAttr = messageNode.Attributes?["msgcat"]?.Value;

            if (string.IsNullOrEmpty(nameAttr) || string.IsNullOrEmpty(msgTypeAttr))
                continue;

            spec.Messages[msgTypeAttr] = new FixMessageSpec {
                MsgType = msgTypeAttr,
                Name = nameAttr,
                Category = msgCatAttr ?? "unknown"
            };
        }
    }

    /// <summary>
    /// Builds repeating-group definitions from every &lt;group&gt; element in the spec
    /// (header, messages and components), keyed by the counter field's tag.
    /// </summary>
    private static void ParseGroups(XmlDocument doc, FixSpecification spec) {
        var nameToTag = new Dictionary<string, int>();
        foreach (var field in spec.Fields.Values) {
            nameToTag[field.Name] = field.Tag;
        }

        var components = new Dictionary<string, XmlNode>();
        var componentNodes = doc.SelectNodes("/fix/components/component");
        if (componentNodes != null) {
            foreach (XmlNode componentNode in componentNodes) {
                var name = componentNode.Attributes?["name"]?.Value;
                if (!string.IsNullOrEmpty(name))
                    components[name] = componentNode;
            }
        }

        var groupNodes = doc.SelectNodes("//group");
        if (groupNodes == null) return;

        foreach (XmlNode groupNode in groupNodes) {
            var name = groupNode.Attributes?["name"]?.Value;
            if (string.IsNullOrEmpty(name) || !nameToTag.TryGetValue(name, out var counterTag))
                continue;

            var members = new List<int>();
            CollectGroupMembers(groupNode, components, nameToTag, members, []);
            if (members.Count == 0) continue;

            // The same group can appear in several messages with different members; merge them
            if (spec.Groups.TryGetValue(counterTag, out var existing)) {
                existing.MemberTags.UnionWith(members);
            } else {
                var groupSpec = new FixGroupSpec {
                    CounterTag = counterTag,
                    DelimiterTag = members[0]
                };
                groupSpec.MemberTags.UnionWith(members);
                spec.Groups[counterTag] = groupSpec;
            }
        }
    }

    private static void CollectGroupMembers(XmlNode node, Dictionary<string, XmlNode> components, Dictionary<string, int> nameToTag, List<int> members, HashSet<string> visitedComponents) {
        foreach (XmlNode child in node.ChildNodes) {
            var name = child.Attributes?["name"]?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            switch (child.Name) {
                case "field":
                // A nested group contributes its counter tag; its members are covered by its own definition
                case "group":
                    if (nameToTag.TryGetValue(name, out var tag))
                        members.Add(tag);
                    break;
                case "component":
                    if (visitedComponents.Add(name) && components.TryGetValue(name, out var componentNode))
                        CollectGroupMembers(componentNode, components, nameToTag, members, visitedComponents);
                    break;
            }
        }
    }

    private static string GetFieldDescription(XmlNode fieldNode, int fieldNumber) {
        var descNode = fieldNode.SelectSingleNode("description");
        if (descNode != null && !string.IsNullOrEmpty(descNode.InnerText)) {
            return descNode.InnerText.Trim();
        }

        // Fallback descriptions for common fields
        return fieldNumber switch {
            8 => "Identifies beginning of new message and protocol version. ALWAYS FIRST FIELD IN MESSAGE.",
            9 => "Message length, in bytes, forward to the CheckSum field. ALWAYS SECOND FIELD IN MESSAGE.",
            10 => "Three byte, simple checksum. ALWAYS LAST FIELD IN MESSAGE.",
            35 => "Defines message type. ALWAYS THIRD FIELD IN MESSAGE.",
            49 => "Assigned value used to identify firm sending message.",
            56 => "Assigned value used to identify receiving firm.",
            34 => "Integer message sequence number.",
            52 => "Time of message transmission (always expressed in UTC).",
            _ => $"FIX field {fieldNumber}"
        };
    }

    private static FixSpecification CreateFallbackSpec(string fixVersion) {
        // Minimal spec used when the full dictionary can neither be downloaded nor loaded from cache
        var spec = new FixSpecification { Version = fixVersion };

        var essentialFields = new (int Tag, string Name, string Type, string Description)[] {
            (8, "BeginString", "STRING", "Identifies beginning of new message and protocol version"),
            (9, "BodyLength", "LENGTH", "Message length, in bytes, forward to the CheckSum field"),
            (10, "CheckSum", "STRING", "Three byte, simple checksum"),
            (11, "ClOrdID", "STRING", "Unique identifier for order as assigned by the institution"),
            (34, "MsgSeqNum", "SEQNUM", "Integer message sequence number"),
            (35, "MsgType", "STRING", "Defines message type"),
            (38, "OrderQty", "QTY", "Quantity ordered"),
            (40, "OrdType", "CHAR", "Order type"),
            (44, "Price", "PRICE", "Price per unit of quantity"),
            (49, "SenderCompID", "STRING", "Assigned value used to identify firm sending message"),
            (52, "SendingTime", "UTCTIMESTAMP", "Time of message transmission"),
            (54, "Side", "CHAR", "Side of order"),
            (55, "Symbol", "STRING", "Ticker symbol"),
            (56, "TargetCompID", "STRING", "Assigned value used to identify receiving firm"),
            (59, "TimeInForce", "CHAR", "Specifies how long the order remains in effect")
        };

        foreach (var (tag, name, type, description) in essentialFields) {
            spec.Fields[tag] = new FixFieldSpec {
                Tag = tag,
                Name = name,
                Type = type,
                Description = description
            };
        }

        AddValues(spec, 35, ("0", "Heartbeat"), ("1", "Test Request"), ("2", "Resend Request"), ("3", "Reject"),
            ("4", "Sequence Reset"), ("5", "Logout"), ("8", "Execution Report"), ("9", "Order Cancel Reject"),
            ("A", "Logon"), ("D", "New Order Single"), ("F", "Order Cancel Request"), ("G", "Order Cancel Replace Request"));
        AddValues(spec, 40, ("1", "Market"), ("2", "Limit"), ("3", "Stop"), ("4", "Stop Limit"));
        AddValues(spec, 54, ("1", "Buy"), ("2", "Sell"), ("5", "Sell Short"));
        AddValues(spec, 59, ("0", "Day"), ("1", "Good Till Cancel"), ("3", "Immediate Or Cancel"), ("4", "Fill Or Kill"));

        return spec;
    }

    private static void AddValues(FixSpecification spec, int tag, params (string Value, string Description)[] values) {
        foreach (var (value, description) in values) {
            spec.Fields[tag].Values[value] = description;
        }
    }
}

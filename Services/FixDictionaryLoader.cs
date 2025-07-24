using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using System.Linq;
using FIXSniff.Models;

namespace FIXSniff.Services;

public class FixSpecificationLoader
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private static readonly Dictionary<string, FixSpecification> _cachedSpecs = new();
    
    /// <summary>
    /// Loads FIX specification for a given version
    /// </summary>
    public static async Task<FixSpecification> LoadSpecificationAsync(string fixVersion)
    {
        // Return cached version if available
        if (_cachedSpecs.TryGetValue(fixVersion, out var cached))
            return cached;
        
        var specFileName = FixVersionDetector.GetSpecFileName(fixVersion);
        var cacheFilePath = Path.Combine(Path.GetTempPath(), $"fix_spec_{specFileName}.cache");
        
        FixSpecification spec;
        
        try
        {
            // Try to download from QuickFIX repository
            spec = await DownloadAndParseSpecAsync(specFileName, fixVersion);
            
            // Save to cache file
            await SaveSpecToCacheAsync(spec, cacheFilePath);
        }
        catch (Exception)
        {
            try
            {
                // Try to load from cache
                spec = await LoadSpecFromCacheAsync(cacheFilePath, fixVersion);
            }
            catch
            {
                // Create minimal fallback spec
                spec = CreateFallbackSpec(fixVersion);
            }
        }
        
        // Cache in memory
        _cachedSpecs[fixVersion] = spec;
        return spec;
    }
    
    private static async Task<FixSpecification> DownloadAndParseSpecAsync(string specFileName, string fixVersion)
    {
        var url = $"https://raw.githubusercontent.com/quickfix/quickfix/master/spec/{specFileName}";
        var xmlContent = await _httpClient.GetStringAsync(url);
        
        return ParseQuickFixXml(xmlContent, fixVersion);
    }
    
    private static FixSpecification ParseQuickFixXml(string xmlContent, string fixVersion)
    {
        var spec = new FixSpecification { Version = fixVersion };
        var doc = new XmlDocument();
        doc.LoadXml(xmlContent);
        
        // Parse fields
        ParseFields(doc, spec);
        
        // Parse messages
        ParseMessages(doc, spec);
        
        return spec;
    }
    
    private static void ParseFields(XmlDocument doc, FixSpecification spec)
    {
        var fieldNodes = doc.SelectNodes("//field");
        if (fieldNodes == null) return;
        
        foreach (XmlNode fieldNode in fieldNodes)
        {
            var numberAttr = fieldNode.Attributes?["number"]?.Value;
            var nameAttr = fieldNode.Attributes?["name"]?.Value;
            var typeAttr = fieldNode.Attributes?["type"]?.Value;
            
            if (!int.TryParse(numberAttr, out var fieldNumber) || string.IsNullOrEmpty(nameAttr))
                continue;
            
            var fieldSpec = new FixFieldSpec
            {
                Tag = fieldNumber,
                Name = nameAttr,
                Type = typeAttr ?? "STRING",
                Description = GetFieldDescription(fieldNode, fieldNumber)
            };
            
            // Parse field values/enums
            ParseFieldValues(fieldNode, fieldSpec);
            
            spec.Fields[fieldNumber] = fieldSpec;
        }
    }
    
    private static void ParseFieldValues(XmlNode fieldNode, FixFieldSpec fieldSpec)
    {
        var valueNodes = fieldNode.SelectNodes("value");
        if (valueNodes == null) return;
        
        foreach (XmlNode valueNode in valueNodes)
        {
            var enumAttr = valueNode.Attributes?["enum"]?.Value;
            var descAttr = valueNode.Attributes?["description"]?.Value;
            
            if (!string.IsNullOrEmpty(enumAttr))
            {
                fieldSpec.Values[enumAttr] = descAttr ?? enumAttr;
            }
        }
    }
    
    private static void ParseMessages(XmlDocument doc, FixSpecification spec)
    {
        var messageNodes = doc.SelectNodes("//message");
        if (messageNodes == null) return;
        
        foreach (XmlNode messageNode in messageNodes)
        {
            var nameAttr = messageNode.Attributes?["name"]?.Value;
            var msgTypeAttr = messageNode.Attributes?["msgtype"]?.Value;
            var msgCatAttr = messageNode.Attributes?["msgcat"]?.Value;
            
            if (string.IsNullOrEmpty(nameAttr) || string.IsNullOrEmpty(msgTypeAttr))
                continue;
            
            var messageSpec = new FixMessageSpec
            {
                MsgType = msgTypeAttr,
                Name = nameAttr,
                Category = msgCatAttr ?? "unknown",
                Description = GetMessageDescription(messageNode, msgTypeAttr)
            };
            
            // Parse required and optional fields for this message
            ParseMessageFields(messageNode, messageSpec);
            
            spec.Messages[msgTypeAttr] = messageSpec;
        }
    }
    
    private static void ParseMessageFields(XmlNode messageNode, FixMessageSpec messageSpec)
    {
        var fieldNodes = messageNode.SelectNodes(".//field");
        if (fieldNodes == null) return;
        
        foreach (XmlNode fieldNode in fieldNodes)
        {
            var nameAttr = fieldNode.Attributes?["name"]?.Value;
            var requiredAttr = fieldNode.Attributes?["required"]?.Value;
            
            if (string.IsNullOrEmpty(nameAttr)) continue;
            
            // We need to look up the field number by name (reverse lookup)
            // For now, we'll skip this detailed parsing and focus on the field definitions
        }
    }
    
    private static string GetFieldDescription(XmlNode fieldNode, int fieldNumber)
    {
        // Try to get description from various sources
        var descNode = fieldNode.SelectSingleNode("description");
        if (descNode != null && !string.IsNullOrEmpty(descNode.InnerText))
        {
            return descNode.InnerText.Trim();
        }
        
        // Fallback descriptions for common fields
        return fieldNumber switch
        {
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
    
    private static string GetMessageDescription(XmlNode messageNode, string msgType)
    {
        return msgType switch
        {
            "0" => "Heartbeat message",
            "1" => "Test Request message", 
            "2" => "Resend Request message",
            "3" => "Reject message",
            "4" => "Sequence Reset message",
            "5" => "Logout message",
            "A" => "Logon message",
            "D" => "New Order - Single",
            "8" => "Execution Report",
            "9" => "Order Cancel Reject",
            "F" => "Order Cancel Request",
            "G" => "Order Cancel/Replace Request",
            _ => $"FIX message type {msgType}"
        };
    }
    
    private static async Task SaveSpecToCacheAsync(FixSpecification spec, string filePath)
    {
        try
        {
            // Simple CSV format for caching
            var lines = new List<string> { "Tag,Name,Type,Description,Values" };
            
            foreach (var field in spec.Fields.Values.OrderBy(f => f.Tag))
            {
                var valuesStr = string.Join(";", field.Values.Select(kv => $"{kv.Key}={kv.Value}"));
                var description = field.Description.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", "");
                
                lines.Add($"{field.Tag},\"{field.Name}\",\"{field.Type}\",\"{description}\",\"{valuesStr}\"");
            }
            
            await File.WriteAllLinesAsync(filePath, lines);
        }
        catch
        {
            // Ignore cache save errors
        }
    }
    
    private static async Task<FixSpecification> LoadSpecFromCacheAsync(string filePath, string fixVersion)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Cache file not found");
        
        var spec = new FixSpecification { Version = fixVersion };
        var lines = await File.ReadAllLinesAsync(filePath);
        
        for (int i = 1; i < lines.Length; i++) // Skip header
        {
            var parts = SplitCsvLine(lines[i]);
            if (parts.Length >= 4 && int.TryParse(parts[0], out var tag))
            {
                var fieldSpec = new FixFieldSpec
                {
                    Tag = tag,
                    Name = parts[1],
                    Type = parts[2],
                    Description = parts[3]
                };
                
                // Parse values if present
                if (parts.Length > 4 && !string.IsNullOrEmpty(parts[4]))
                {
                    var valuePairs = parts[4].Split(';', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var pair in valuePairs)
                    {
                        var equalIndex = pair.IndexOf('=');
                        if (equalIndex > 0)
                        {
                            var key = pair.Substring(0, equalIndex);
                            var value = pair.Substring(equalIndex + 1);
                            fieldSpec.Values[key] = value;
                        }
                    }
                }
                
                spec.Fields[tag] = fieldSpec;
            }
        }
        
        return spec;
    }
    
    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var inQuotes = false;
        var currentField = "";
        
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    currentField += '"';
                    i++; // Skip next quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(currentField);
                currentField = "";
            }
            else
            {
                currentField += c;
            }
        }
        
        result.Add(currentField);
        return result.ToArray();
    }
    
    private static FixSpecification CreateFallbackSpec(string fixVersion)
    {
        // Create minimal spec with essential fields
        var spec = new FixSpecification { Version = fixVersion };
        
        var essentialFields = new Dictionary<int, (string Name, string Type, string Description)>
        {
            { 8, ("BeginString", "STRING", "Identifies beginning of new message and protocol version") },
            { 9, ("BodyLength", "LENGTH", "Message length, in bytes, forward to the CheckSum field") },
            { 10, ("CheckSum", "STRING", "Three byte, simple checksum") },
            { 35, ("MsgType", "STRING", "Defines message type") },
            { 49, ("SenderCompID", "STRING", "Assigned value used to identify firm sending message") },
            { 56, ("TargetCompID", "STRING", "Assigned value used to identify receiving firm") },
            { 34, ("MsgSeqNum", "SEQNUM", "Integer message sequence number") },
            { 52, ("SendingTime", "UTCTIMESTAMP", "Time of message transmission") }
        };
        
        foreach (var kvp in essentialFields)
        {
            spec.Fields[kvp.Key] = new FixFieldSpec
            {
                Tag = kvp.Key,
                Name = kvp.Value.Name,
                Type = kvp.Value.Type,
                Description = kvp.Value.Description
            };
        }
        
        return spec;
    }
}

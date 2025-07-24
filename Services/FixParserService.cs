using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FIXSniff.Models;
using QuickFix;

namespace FIXSniff.Services;

public class FixParserService
{
    public async Task<ParsedFixMessage> ParseMessageAsync(string rawMessage)
    {
        var result = new ParsedFixMessage
        {
            RawMessage = rawMessage
        };

        try
        {
            if (string.IsNullOrWhiteSpace(rawMessage))
            {
                result.ErrorMessage = "Message is empty";
                return result;
            }

            // Step 1: Detect FIX version
            var fixVersion = FixVersionDetector.DetectVersion(rawMessage);
            
            // Step 2: Load specification for this version
            var spec = await FixSpecificationLoader.LoadSpecificationAsync(fixVersion);
            
            // Step 3: Parse message using specification
            result.Fields = await ParseWithSpecificationAsync(rawMessage, spec);
            
            // Add version info as first field for display
            result.Fields.Insert(0, new FixFieldInfo
            {
                TagNumber = "VERSION",
                FieldName = "Detected Version",
                Value = FixVersionDetector.GetDisplayName(fixVersion),
                Description = $"Auto-detected FIX version: {fixVersion}",
                IndentLevel = 0
            });
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Parsing failed: {ex.Message}";
        }

        return result;
    }
    
    private async Task<List<FixFieldInfo>> ParseWithSpecificationAsync(string rawMessage, FixSpecification spec)
    {
        var fields = new List<FixFieldInfo>();
        
        try
        {
            // Try QuickFIXn parsing first
            var normalizedMessage = NormalizeFixMessage(rawMessage);
            var message = new Message();
            message.FromString(normalizedMessage, true, null, null, null);
            
            fields = ExtractFieldsWithSpec(message, spec, 0);
        }
        catch
        {
            // Fall back to manual parsing
            fields = ParseManuallyWithSpec(rawMessage, spec);
        }
        
        return fields.OrderBy(f => GetSortOrder(f.TagNumber)).ToList();
    }
    
    private List<FixFieldInfo> ExtractFieldsWithSpec(Message message, FixSpecification spec, int indentLevel)
    {
        var fields = new List<FixFieldInfo>();
        var processedTags = new HashSet<int>();
        
        // Get all fields that are set in the message
        var allTags = new List<int>();
        
        // Check common field ranges
        for (int tag = 1; tag <= 2000; tag++)
        {
            try
            {
                if (message.Header.IsSetField(tag) || message.IsSetField(tag) || message.Trailer.IsSetField(tag))
                {
                    allTags.Add(tag);
                }
            }
            catch
            {
                // Skip fields that can't be checked
            }
        }
        
        foreach (var tag in allTags.Where(t => !processedTags.Contains(t)))
        {
            try
            {
                string value = "";
                
                // Try to get value from different message sections
                if (message.Header.IsSetField(tag))
                    value = message.Header.GetString(tag);
                else if (message.IsSetField(tag))
                    value = message.GetString(tag);
                else if (message.Trailer.IsSetField(tag))
                    value = message.Trailer.GetString(tag);
                
                if (!string.IsNullOrEmpty(value))
                {
                    var fieldInfo = CreateFieldInfo(tag, value, spec, indentLevel);
                    fields.Add(fieldInfo);
                    processedTags.Add(tag);
                }
            }
            catch
            {
                // Skip problematic fields
            }
        }
        
        return fields;
    }
    
    private List<FixFieldInfo> ParseManuallyWithSpec(string rawMessage, FixSpecification spec)
    {
        var fields = new List<FixFieldInfo>();
        
        // Handle different SOH representations
        string[] separators = { "\u0001", "|", "^A" };
        string[]? pairs = null;
        
        foreach (var separator in separators)
        {
            pairs = rawMessage.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
            if (pairs.Length > 1) break;
        }
        
        if (pairs == null || pairs.Length <= 1)
        {
            pairs = rawMessage.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        foreach (var pair in pairs)
        {
            if (string.IsNullOrWhiteSpace(pair)) continue;
            
            var equalIndex = pair.IndexOf('=');
            if (equalIndex > 0 && equalIndex < pair.Length - 1)
            {
                var tagStr = pair.Substring(0, equalIndex);
                var value = pair.Substring(equalIndex + 1);
                
                if (int.TryParse(tagStr, out var tag))
                {
                    var fieldInfo = CreateFieldInfo(tag, value, spec, 0);
                    fields.Add(fieldInfo);
                }
            }
        }

        if (fields.Count == 0)
        {
            throw new InvalidOperationException("Could not parse any FIX fields from the message");
        }

        return fields;
    }
    
    private FixFieldInfo CreateFieldInfo(int tag, string value, FixSpecification spec, int indentLevel)
    {
        var fieldSpec = spec.Fields.GetValueOrDefault(tag);
        
        var fieldInfo = new FixFieldInfo
        {
            TagNumber = tag.ToString(),
            FieldName = fieldSpec?.Name ?? $"Tag{tag}",
            Value = value,
            IndentLevel = indentLevel
        };
        
        // Create detailed description including field values
        fieldInfo.Description = CreateDetailedDescription(tag, value, fieldSpec);
        
        return fieldInfo;
    }
    
    private string CreateDetailedDescription(int tag, string value, FixFieldSpec? fieldSpec)
    {
        if (fieldSpec == null)
        {
            return $"Unknown field tag {tag}. Value: {value}";
        }
        
        var description = $"Field {tag} ({fieldSpec.Name})\n";
        description += $"Type: {fieldSpec.Type}\n";
        description += $"Current Value: {value}\n\n";
        
        // Add field description
        if (!string.IsNullOrEmpty(fieldSpec.Description))
        {
            description += $"Description: {fieldSpec.Description}\n\n";
        }
        
        // Add possible values if available
        if (fieldSpec.Values.Any())
        {
            description += "Possible Values:\n";
            foreach (var kvp in fieldSpec.Values.OrderBy(x => x.Key))
            {
                var marker = kvp.Key == value ? " ← CURRENT" : "";
                description += $"  {kvp.Key} = {kvp.Value}{marker}\n";
            }
        }
        else if (!string.IsNullOrEmpty(value))
        {
            description += $"Current Value Meaning: ";
            description += GetSpecialValueMeaning(tag, value);
        }
        
        return description.Trim();
    }
    
    private string GetSpecialValueMeaning(int tag, string value)
    {
        // Handle special cases for common fields
        return tag switch
        {
            35 => GetMsgTypeDescription(value), // MsgType
            39 => GetOrdStatusDescription(value), // OrdStatus
            54 => GetSideDescription(value), // Side
            40 => GetOrdTypeDescription(value), // OrdType
            59 => GetTimeInForceDescription(value), // TimeInForce
            150 => GetExecTypeDescription(value), // ExecType
            _ => value // Default: just return the value
        };
    }
    
    private string GetMsgTypeDescription(string msgType)
    {
        return msgType switch
        {
            "0" => "Heartbeat",
            "1" => "Test Request",
            "2" => "Resend Request", 
            "3" => "Reject",
            "4" => "Sequence Reset",
            "5" => "Logout",
            "A" => "Logon",
            "D" => "New Order - Single",
            "8" => "Execution Report",
            "9" => "Order Cancel Reject",
            "F" => "Order Cancel Request",
            "G" => "Order Cancel/Replace Request",
            _ => $"Unknown message type: {msgType}"
        };
    }
    
    private string GetOrdStatusDescription(string status)
    {
        return status switch
        {
            "0" => "New",
            "1" => "Partially filled",
            "2" => "Filled",
            "3" => "Done for day",
            "4" => "Canceled",
            "5" => "Replaced",
            "6" => "Pending Cancel",
            "7" => "Stopped",
            "8" => "Rejected",
            "9" => "Suspended",
            "A" => "Pending New",
            "B" => "Calculated",
            "C" => "Expired",
            "D" => "Accepted for Bidding",
            "E" => "Pending Replace",
            _ => $"Unknown order status: {status}"
        };
    }
    
    private string GetSideDescription(string side)
    {
        return side switch
        {
            "1" => "Buy",
            "2" => "Sell",
            "3" => "Buy minus",
            "4" => "Sell plus", 
            "5" => "Sell short",
            "6" => "Sell short exempt",
            "7" => "Undisclosed",
            "8" => "Cross",
            "9" => "Cross short",
            "A" => "Cross short exempt", 
            "B" => "As Defined",
            "C" => "Opposite",
            "D" => "Subscribe",
            "E" => "Redeem",
            "F" => "Lend",
            "G" => "Borrow",
            _ => $"Unknown side: {side}"
        };
    }
    
    private string GetOrdTypeDescription(string ordType)
    {
        return ordType switch
        {
            "1" => "Market",
            "2" => "Limit", 
            "3" => "Stop / Stop Loss",
            "4" => "Stop Limit",
            "5" => "Market On Close",
            "6" => "With Or Without",
            "7" => "Limit Or Better",
            "8" => "Limit With Or Without",
            "9" => "On Basis",
            "A" => "On Close",
            "B" => "Limit On Close",
            "C" => "Forex Market",
            "D" => "Previously Quoted",
            "E" => "Previously Indicated",
            "F" => "Forex Limit",
            "G" => "Forex Swap",
            "H" => "Forex Previously Quoted",
            "I" => "Funari",
            "J" => "Market If Touched",
            "K" => "Market With Left Over As Limit",
            "L" => "Previous Fund Valuation Point",
            "M" => "Next Fund Valuation Point",
            "P" => "Pegged",
            _ => $"Unknown order type: {ordType}"
        };
    }
    
    private string GetTimeInForceDescription(string tif)
    {
        return tif switch
        {
            "0" => "Day",
            "1" => "Good Till Cancel",
            "2" => "At the Opening",
            "3" => "Immediate or Cancel",
            "4" => "Fill or Kill",
            "5" => "Good Till Crossing",
            "6" => "Good Till Date",
            "7" => "At the Close",
            "8" => "Good Through Crossing",
            "9" => "At Crossing",
            _ => $"Unknown time in force: {tif}"
        };
    }
    
    private string GetExecTypeDescription(string execType)
    {
        return execType switch
        {
            "0" => "New",
            "3" => "Done for day",
            "4" => "Canceled",
            "5" => "Replaced",
            "6" => "Pending Cancel",
            "7" => "Stopped",
            "8" => "Rejected",
            "9" => "Suspended",
            "A" => "Pending New",
            "B" => "Calculated",
            "C" => "Expired",
            "D" => "Restated",
            "E" => "Pending Replace",
            "F" => "Trade",
            "G" => "Trade Correct",
            "H" => "Trade Cancel",
            "I" => "Order Status",
            "J" => "Trade in a Clearing Hold",
            "K" => "Trade has been released to Clearing",
            "L" => "Triggered or Activated by System",
            _ => $"Unknown execution type: {execType}"
        };
    }
    
    private string NormalizeFixMessage(string rawMessage)
    {
        var normalized = rawMessage;
        
        if (normalized.Contains('|'))
            normalized = normalized.Replace('|', '\u0001');
        
        if (normalized.Contains("^A"))
            normalized = normalized.Replace("^A", "\u0001");
            
        return normalized;
    }
    
    private int GetSortOrder(string tagNumber)
    {
        // Special handling for version info
        if (tagNumber == "VERSION")
            return -1;
            
        if (int.TryParse(tagNumber, out var tag))
        {
            // Standard FIX field ordering: Header fields first, then body, then trailer
            return tag switch
            {
                8 => 1,   // BeginString
                9 => 2,   // BodyLength  
                35 => 3,  // MsgType
                49 => 4,  // SenderCompID
                56 => 5,  // TargetCompID
                34 => 6,  // MsgSeqNum
                52 => 7,  // SendingTime
                10 => 9999, // CheckSum (always last)
                _ => tag + 100 // Other fields in tag order
            };
        }
        
        return 10000; // Unknown fields at end
    }
}

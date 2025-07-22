using System;
using System.Collections.Generic;
using System.Linq;
using FIXSniff.Models;
using QuickFix;

namespace FIXSniff.Services;

public class FixParserService
{
    public ParsedFixMessage ParseMessage(string rawMessage)
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

            // Try to parse with QuickFIXn
            var message = new Message();
            
            // Normalize the message - ensure it has proper SOH delimiters
            var normalizedMessage = NormalizeFixMessage(rawMessage);
            message.FromString(normalizedMessage, true, null, null, null);

            result.Fields = ExtractFields(message, 0);
        }
        catch (Exception ex)
        {
            // If QuickFIXn fails, try manual parsing
            try
            {
                result.Fields = ParseManually(rawMessage);
            }
            catch (Exception manualEx)
            {
                result.ErrorMessage = $"QuickFIXn error: {ex.Message}. Manual parsing error: {manualEx.Message}";
            }
        }

        return result;
    }

    private string NormalizeFixMessage(string rawMessage)
    {
        // Replace common SOH representations with actual SOH character
        var normalized = rawMessage;
        
        // Replace pipe delimiters with SOH
        if (normalized.Contains('|'))
            normalized = normalized.Replace('|', '\u0001');
        
        // Replace ^A notation with SOH
        if (normalized.Contains("^A"))
            normalized = normalized.Replace("^A", "\u0001");
            
        return normalized;
    }

    private List<FixFieldInfo> ExtractFields(Message message, int indentLevel)
    {
        var fields = new List<FixFieldInfo>();
        
        try
        {
            // Get all fields from the message using reflection-like approach
            var allFields = GetAllMessageFields(message);
            
            foreach (var field in allFields)
            {
                var (name, description) = FixDictionary.GetFieldInfo(field.Tag);
                
                fields.Add(new FixFieldInfo
                {
                    TagNumber = field.Tag.ToString(),
                    FieldName = name,
                    Value = field.Value,
                    Description = description,
                    IndentLevel = indentLevel
                });
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error extracting fields from QuickFIXn message: {ex.Message}");
        }

        // Sort by tag number for better display
        return fields.OrderBy(f => int.TryParse(f.TagNumber, out var tag) ? tag : 9999).ToList();
    }

    private List<(int Tag, string Value)> GetAllMessageFields(Message message)
    {
        var fields = new List<(int Tag, string Value)>();
        
        // Common FIX field tags to check
        var commonTags = new int[]
        {
            8, 9, 10, 11, 14, 15, 17, 20, 21, 30, 31, 32, 34, 35, 36, 37, 38, 39, 40, 
            41, 43, 44, 45, 49, 50, 52, 54, 55, 56, 57, 58, 59, 60, 75, 76, 98, 102, 
            103, 108, 112, 141, 150, 151, 167, 371, 372, 373, 553, 554, 789, 1128
        };

        // Check header fields
        foreach (var tag in commonTags)
        {
            if (message.Header.IsSetField(tag))
            {
                try
                {
                    var value = message.Header.GetString(tag);
                    fields.Add((tag, value));
                }
                catch { }
            }
        }

        // Check body fields
        foreach (var tag in commonTags)
        {
            if (message.IsSetField(tag))
            {
                try
                {
                    var value = message.GetString(tag);
                    fields.Add((tag, value));
                }
                catch { }
            }
        }

        // Check trailer fields
        foreach (var tag in commonTags)
        {
            if (message.Trailer.IsSetField(tag))
            {
                try
                {
                    var value = message.Trailer.GetString(tag);
                    fields.Add((tag, value));
                }
                catch { }
            }
        }

        // Remove duplicates (in case a field appears in multiple sections)
        return fields.GroupBy(f => f.Tag).Select(g => g.First()).ToList();
    }

    private List<FixFieldInfo> ParseManually(string rawMessage)
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
            // Try space separation as last resort
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
                    var (name, description) = FixDictionary.GetFieldInfo(tag);
                    
                    fields.Add(new FixFieldInfo
                    {
                        TagNumber = tag.ToString(),
                        FieldName = name,
                        Value = value,
                        Description = description,
                        IndentLevel = 0
                    });
                }
            }
        }

        if (fields.Count == 0)
        {
            throw new InvalidOperationException("Could not parse any FIX fields from the message");
        }

        // Sort by tag number
        return fields.OrderBy(f => int.TryParse(f.TagNumber, out var tag) ? tag : 9999).ToList();
    }
}
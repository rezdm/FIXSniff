using System;
using System.Linq;
using FIXSniff.Models;

namespace FIXSniff.Services;

public static class FixVersionDetector
{
    /// <summary>
    /// Detects FIX version from raw message by examining BeginString (tag 8)
    /// </summary>
    public static string DetectVersion(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
            return "FIX.4.4"; // Default fallback
        
        try
        {
            // Parse the BeginString field (always tag 8, first field)
            var beginString = ExtractBeginString(rawMessage);
            
            if (string.IsNullOrEmpty(beginString))
                return "FIX.4.4"; // Default fallback
            
            // Check if it's a supported version
            if (FixVersionInfo.SupportedVersions.ContainsKey(beginString))
                return beginString;
            
            // Handle some common variations/mappings
            return beginString switch
            {
                var v when v.StartsWith("FIX.4.0") => "FIX.4.0",
                var v when v.StartsWith("FIX.4.1") => "FIX.4.1", 
                var v when v.StartsWith("FIX.4.2") => "FIX.4.2",
                var v when v.StartsWith("FIX.4.3") => "FIX.4.3",
                var v when v.StartsWith("FIX.4.4") => "FIX.4.4",
                var v when v.StartsWith("FIX.5.0") => "FIX.5.0",
                var v when v.StartsWith("FIXT.1.1") => "FIXT.1.1",
                _ => "FIX.4.4" // Default fallback
            };
        }
        catch
        {
            return "FIX.4.4"; // Default fallback on any error
        }
    }
    
    private static string ExtractBeginString(string rawMessage)
    {
        // Handle different SOH representations
        string[] separators = { "\u0001", "|", "^A", " " };
        
        foreach (var separator in separators)
        {
            var pairs = rawMessage.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);
            
            // Look for tag 8 (BeginString) - should be first field
            foreach (var pair in pairs.Take(3)) // Check first 3 fields max
            {
                if (pair.Contains('='))
                {
                    var parts = pair.Split('=', 2);
                    if (parts.Length == 2 && parts[0].Trim() == "8")
                    {
                        return parts[1].Trim();
                    }
                }
            }
        }
        
        return string.Empty;
    }
    
    /// <summary>
    /// Gets the spec file name for a detected version
    /// </summary>
    public static string GetSpecFileName(string fixVersion)
    {
        if (FixVersionInfo.SupportedVersions.TryGetValue(fixVersion, out var versionInfo))
        {
            return versionInfo.SpecFileName;
        }
        
        return "FIX44.xml"; // Default fallback
    }
    
    /// <summary>
    /// Gets display name for version
    /// </summary>
    public static string GetDisplayName(string fixVersion)
    {
        if (FixVersionInfo.SupportedVersions.TryGetValue(fixVersion, out var versionInfo))
        {
            return versionInfo.DisplayName;
        }
        
        return fixVersion; // Return as-is if not found
    }
}

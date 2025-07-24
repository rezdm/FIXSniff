using System;
using System.Collections.Generic;
using System.IO;

namespace FIXSniff.Models;

public class FixSpecification
{
    public string Version { get; set; } = string.Empty;
    public Dictionary<int, FixFieldSpec> Fields { get; set; } = new();
    public Dictionary<string, FixMessageSpec> Messages { get; set; } = new();
}

public class FixFieldSpec
{
    public int Tag { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, string> Values { get; set; } = new(); // Value -> Description
    public bool IsRequired { get; set; }
    public string? BaseCategory { get; set; } // Header, Body, Trailer
}

public class FixMessageSpec
{
    public string MsgType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<int> RequiredFields { get; set; } = new();
    public List<int> OptionalFields { get; set; } = new();
}

public class FixVersionInfo
{
    public string BeginString { get; set; } = string.Empty;
    public string SpecFileName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    
    public static readonly Dictionary<string, FixVersionInfo> SupportedVersions = new()
    {
        { "FIX.4.0", new FixVersionInfo { BeginString = "FIX.4.0", SpecFileName = "FIX40.xml", DisplayName = "FIX 4.0" } },
        { "FIX.4.1", new FixVersionInfo { BeginString = "FIX.4.1", SpecFileName = "FIX41.xml", DisplayName = "FIX 4.1" } },
        { "FIX.4.2", new FixVersionInfo { BeginString = "FIX.4.2", SpecFileName = "FIX42.xml", DisplayName = "FIX 4.2" } },
        { "FIX.4.3", new FixVersionInfo { BeginString = "FIX.4.3", SpecFileName = "FIX43.xml", DisplayName = "FIX 4.3" } },
        { "FIX.4.4", new FixVersionInfo { BeginString = "FIX.4.4", SpecFileName = "FIX44.xml", DisplayName = "FIX 4.4" } },
        { "FIX.5.0", new FixVersionInfo { BeginString = "FIX.5.0", SpecFileName = "FIX50.xml", DisplayName = "FIX 5.0" } },
        { "FIX.5.0SP1", new FixVersionInfo { BeginString = "FIX.5.0SP1", SpecFileName = "FIX50SP1.xml", DisplayName = "FIX 5.0 SP1" } },
        { "FIX.5.0SP2", new FixVersionInfo { BeginString = "FIX.5.0SP2", SpecFileName = "FIX50SP2.xml", DisplayName = "FIX 5.0 SP2" } },
        { "FIXT.1.1", new FixVersionInfo { BeginString = "FIXT.1.1", SpecFileName = "FIXT11.xml", DisplayName = "FIX Transport 1.1" } }
    };
}

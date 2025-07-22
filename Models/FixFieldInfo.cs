using System.Collections.Generic;

namespace FIXSniff.Models;

public class FixFieldInfo
{
    public string TagNumber { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int IndentLevel { get; set; } = 0;
    public string DisplayTagNumber => new string(' ', IndentLevel * 4) + TagNumber;
}

public class ParsedFixMessage
{
    public string RawMessage { get; set; } = string.Empty;
    public List<FixFieldInfo> Fields { get; set; } = new List<FixFieldInfo>();
    public string ErrorMessage { get; set; } = string.Empty;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
}


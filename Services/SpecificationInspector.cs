using System;
using System.Collections.Generic;
using System.Linq;
using FIXSniff.Models;

namespace FIXSniff.Services;

/// <summary>
/// Utility for inspecting and debugging FIX specifications
/// </summary>
public static class SpecificationInspector {
    /// <summary>
    /// Shows which fields have enum values in the specification
    /// </summary>
    public static void LogFieldsWithValues(FixSpecification spec) {
        Console.WriteLine($"\n=== FIX {spec.Version} Specification Analysis ===");
        Console.WriteLine($"Total fields: {spec.Fields.Count}");
        
        var fieldsWithValues = spec.Fields.Values
            .Where(f => f.Values.Any())
            .OrderBy(f => f.Tag)
            .ToList();
            
        Console.WriteLine($"Fields with enum values: {fieldsWithValues.Count}");
        Console.WriteLine();
        
        foreach (var field in fieldsWithValues.Take(20)) { // Show first 20
            Console.WriteLine($"Tag {field.Tag} ({field.Name}): {field.Values.Count} values");
            foreach (var value in field.Values.Take(5)) { // Show first 5 values
                Console.WriteLine($"  {value.Key} = {value.Value}");
            }
            if (field.Values.Count > 5) {
                Console.WriteLine($"  ... and {field.Values.Count - 5} more");
            }
            Console.WriteLine();
        }
        
        if (fieldsWithValues.Count > 20) {
            Console.WriteLine($"... and {fieldsWithValues.Count - 20} more fields with values");
        }
    }
    
    /// <summary>
    /// Checks if a specific field/value combination exists in spec
    /// </summary>
    public static bool HasSpecValue(FixSpecification spec, int tag, string value) {
        return spec.Fields.TryGetValue(tag, out var field) && 
               field.Values.ContainsKey(value);
    }
    
    /// <summary>
    /// Gets all possible values for a field from spec
    /// </summary>
    public static Dictionary<string, string> GetFieldValues(FixSpecification spec, int tag) {
        if (spec.Fields.TryGetValue(tag, out var field))
        {
            return field.Values;
        }
        return new Dictionary<string, string>();
    }
    
    /// <summary>
    /// Compares hardcoded values vs spec values for debugging
    /// </summary>
    public static void CompareHardcodedVsSpec(FixSpecification spec, int tag, Dictionary<string, string> hardcodedValues) {
        Console.WriteLine($"\n=== Comparison for Tag {tag} ===");
        
        if (spec.Fields.TryGetValue(tag, out var field)) {
            Console.WriteLine($"Spec has {field.Values.Count} values, hardcoded has {hardcodedValues.Count}");
            
            // Values only in spec
            var onlyInSpec = field.Values.Keys.Except(hardcodedValues.Keys).ToList();
            if (onlyInSpec.Any()) {
                Console.WriteLine("Only in spec:");
                foreach (var key in onlyInSpec.Take(10)) {
                    Console.WriteLine($"  {key} = {field.Values[key]}");
                }
            }
            
            // Values only in hardcoded
            var onlyInHardcoded = hardcodedValues.Keys.Except(field.Values.Keys).ToList();
            if (onlyInHardcoded.Any()) {
                Console.WriteLine("Only in hardcoded:");
                foreach (var key in onlyInHardcoded.Take(10)) {
                    Console.WriteLine($"  {key} = {hardcodedValues[key]}");
                }
            }
        } else {
            Console.WriteLine("Field not found in specification");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FIXSniff.Models;
using QuickFix;

namespace FIXSniff.Services;

public class FixParserService {
    public async Task<ParsedFixMessage> ParseMessageAsync(string rawMessage) {
        var result = new ParsedFixMessage {
            RawMessage = rawMessage
        };

        try {
            if (string.IsNullOrWhiteSpace(rawMessage)) {
                result.ErrorMessage = "Message is empty";
                return result;
            }

            // Step 1: Detect FIX version
            var fixVersion = FixVersionDetector.DetectVersion(rawMessage);
            
            // Step 2: Load specification for this version
            var spec = await FixSpecificationLoader.LoadSpecificationAsync(fixVersion);
            
            // Step 3: Debug - show what values are available in spec (optional)
            #if DEBUG
            SpecificationInspector.LogFieldsWithValues(spec);
            #endif
            
            // Step 4: Parse message using specification
            result.Fields = await ParseWithSpecificationAsync(rawMessage, spec);
            
            // Add version info as first field for display
            result.Fields.Insert(0, new FixFieldInfo {
                TagNumber = "VERSION",
                FieldName = "Detected Version",
                Value = FixVersionDetector.GetDisplayName(fixVersion),
                ParsedValue = $"Using {spec.Fields.Count} field definitions",
                Description = $"Auto-detected FIX version: {fixVersion}\nLoaded {spec.Fields.Count} field definitions from specification\nFields with enum values: {spec.Fields.Values.Count(f => f.Values.Any())}",
                IndentLevel = 0
            });
        } catch (Exception ex) {
            result.ErrorMessage = $"Parsing failed: {ex.Message}";
        }

        return result;
    }
    
    private async Task<List<FixFieldInfo>> ParseWithSpecificationAsync(string rawMessage, FixSpecification spec) {
        List<FixFieldInfo> fields;
        
        try {
            // Try QuickFIXn parsing first
            var normalizedMessage = NormalizeFixMessage(rawMessage);
            var message = new Message();
            message.FromString(normalizedMessage, true, null, null, null);
            
            fields = ExtractFieldsWithSpec(message, spec, 0);
        } catch {
            // Fall back to manual parsing
            fields = ParseManuallyWithSpec(rawMessage, spec);
        }
        
        return await Task.Run(() => { return fields.OrderBy(f => GetSortOrder(f.TagNumber)).ToList(); });
    }
    
    private List<FixFieldInfo> ExtractFieldsWithSpec(Message message, FixSpecification spec, int indentLevel) {
        var fields = new List<FixFieldInfo>();
        var processedTags = new HashSet<int>();
        
        // Get all fields that are set in the message
        var allTags = new List<int>();
        
        // Check common field ranges
        for (int tag = 1; tag <= 2000; tag++) {
            try {
                if (message.Header.IsSetField(tag) || message.IsSetField(tag) || message.Trailer.IsSetField(tag)) {
                    allTags.Add(tag);
                }
            } catch {
                // Skip fields that can't be checked
            }
        }
        
        foreach (var tag in allTags.Where(t => !processedTags.Contains(t))) {
            try {
                string value = "";
                
                // Try to get value from different message sections
                if (message.Header.IsSetField(tag))
                    value = message.Header.GetString(tag);
                else if (message.IsSetField(tag))
                    value = message.GetString(tag);
                else if (message.Trailer.IsSetField(tag))
                    value = message.Trailer.GetString(tag);
                
                if (!string.IsNullOrEmpty(value)) {
                    var fieldInfo = CreateFieldInfo(tag, value, spec, indentLevel);
                    fields.Add(fieldInfo);
                    processedTags.Add(tag);
                }
            } catch {
                // Skip problematic fields
            }
        }
        
        return fields;
    }
    
    private List<FixFieldInfo> ParseManuallyWithSpec(string rawMessage, FixSpecification spec) {
        var fields = new List<FixFieldInfo>();
        
        // Handle different SOH representations
        string[] separators = ["\u0001", "|", "^A"];
        string[]? pairs = null;
        
        foreach (var separator in separators) {
            pairs = rawMessage.Split([separator], StringSplitOptions.RemoveEmptyEntries);
            if (pairs.Length > 1) break;
        }
        
        if (pairs == null || pairs.Length <= 1) {
            pairs = rawMessage.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        }

        foreach (var pair in pairs) {
            if (string.IsNullOrWhiteSpace(pair)) continue;
            
            var equalIndex = pair.IndexOf('=');
            if (equalIndex > 0 && equalIndex < pair.Length - 1) {
                var tagStr = pair.Substring(0, equalIndex);
                var value = pair.Substring(equalIndex + 1);
                
                if (int.TryParse(tagStr, out var tag)) {
                    var fieldInfo = CreateFieldInfo(tag, value, spec, 0);
                    fields.Add(fieldInfo);
                }
            }
        }

        if (fields.Count == 0) {
            throw new InvalidOperationException("Could not parse any FIX fields from the message");
        }

        return fields;
    }
    
    private FixFieldInfo CreateFieldInfo(int tag, string value, FixSpecification spec, int indentLevel) {
        var fieldSpec = spec.Fields.GetValueOrDefault(tag);
        
        var fieldInfo = new FixFieldInfo {
            TagNumber = tag.ToString(),
            FieldName = fieldSpec?.Name ?? $"Tag{tag}",
            Value = value,
            IndentLevel = indentLevel,
            // Set parsed value (human-readable meaning)
            ParsedValue = GetParsedValue(tag, value, fieldSpec),
            // Create detailed description including field values
            Description = CreateDetailedDescription(tag, value, fieldSpec)
        };

        return fieldInfo;
    }
    
    private string GetParsedValue(int tag, string value, FixFieldSpec? fieldSpec) {
        // FIRST PRIORITY: Try to get from downloaded specification
        if (fieldSpec?.Values.TryGetValue(value, out var specValue) == true) {
            return specValue;
        }
        
        // SECOND PRIORITY: Try to get from specification using case-insensitive lookup
        if (fieldSpec?.Values != null) {
            var caseInsensitiveMatch = fieldSpec.Values
                .FirstOrDefault(kvp => string.Equals(kvp.Key, value, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(caseInsensitiveMatch.Key)) {
                return caseInsensitiveMatch.Value;
            }
        }
        
        // THIRD PRIORITY: Fall back to hardcoded interpretations for common fields
        var parsedValue = GetSpecialValueMeaning(tag, value);
        
        // If it's the same as the original value, return empty to avoid duplication
        return parsedValue == value ? string.Empty : parsedValue;
    }
    
    private string CreateDetailedDescription(int tag, string value, FixFieldSpec? fieldSpec) {
        if (fieldSpec == null) {
            return $"Unknown field tag {tag}. Value: {value}";
        }
        
        var description = $"Field {tag} ({fieldSpec.Name})\n";
        description += $"Type: {fieldSpec.Type}\n";
        description += $"Current Value: {value}\n\n";
        
        // Add field description
        if (!string.IsNullOrEmpty(fieldSpec.Description)) {
            description += $"Description: {fieldSpec.Description}\n\n";
        }
        
        // Add possible values if available
        if (fieldSpec.Values.Count != 0) {
            description += "Possible Values:\n";
            foreach (var kvp in fieldSpec.Values.OrderBy(x => x.Key)) {
                var marker = kvp.Key == value ? " ← CURRENT" : "";
                description += $"  {kvp.Key} = {kvp.Value}{marker}\n";
            }
        } else if (!string.IsNullOrEmpty(value)) {
            description += $"Current Value Meaning: ";
            description += GetSpecialValueMeaning(tag, value);
        }
        
        return description.Trim();
    }
    
    private string GetSpecialValueMeaning(int tag, string value) {
        // Handle special cases for common fields
        return tag switch {
            22 => GetSecurityIDSourceDescription(value), // SecurityIDSource
            35 => GetMsgTypeDescription(value), // MsgType
            39 => GetOrdStatusDescription(value), // OrdStatus
            40 => GetOrdTypeDescription(value), // OrdType
            54 => GetSideDescription(value), // Side
            59 => GetTimeInForceDescription(value), // TimeInForce
            63 => GetSettlTypeDescription(value), // SettlType
            71 => GetAllocTransTypeDescription(value), // AllocTransType
            98 => GetEncryptMethodDescription(value), // EncryptMethod
            102 => GetCxlRejReasonDescription(value), // CxlRejReason
            103 => GetOrdRejReasonDescription(value), // OrdRejReason
            150 => GetExecTypeDescription(value), // ExecType
            167 => GetSecurityTypeDescription(value), // SecurityType
            201 => GetPutOrCallDescription(value), // PutOrCall
            269 => GetMDEntryTypeDescription(value), // MDEntryType
            373 => GetSessionRejectReasonDescription(value), // SessionRejectReason
            447 => GetPartyIDSourceDescription(value), // PartyIDSource
            461 => GetCFICodeDescription(value), // CFICode
            _ => value // Default: just return the value
        };
    }
    
    private string GetSecurityIDSourceDescription(string source) {
        return source switch {
            "1" => "CUSIP",
            "2" => "SEDOL", 
            "3" => "QUIK",
            "4" => "ISIN_NUMBER",
            "5" => "RIC_CODE",
            "6" => "ISO_CURRENCY_CODE",
            "7" => "ISO_COUNTRY_CODE",
            "8" => "EXCHANGE_SYMBOL",
            "9" => "CONSOLIDATED_TAPE_ASSOCIATION",
            "A" => "BLOOMBERG_SYMBOL",
            "B" => "WERTPAPIER",
            "C" => "DUTCH",
            "D" => "VALOREN",
            "E" => "SICOVAM",
            "F" => "BELGIAN",
            "G" => "COMMON",
            "H" => "CLEARING_HOUSE",
            "I" => "ISDA_FPML_PRODUCT_SPECIFICATION",
            "J" => "OPTIONS_PRICE_REPORTING_AUTHORITY",
            _ => source
        };
    }
    
    private string GetSettlTypeDescription(string settlType) {
        return settlType switch {
            "0" => "REGULAR",
            "1" => "CASH",
            "2" => "NEXT_DAY",
            "3" => "T_PLUS_2", 
            "4" => "T_PLUS_3",
            "5" => "T_PLUS_4",
            "6" => "FUTURE",
            "7" => "WHEN_AND_IF_ISSUED",
            "8" => "SELLERS_OPTION",
            "9" => "T_PLUS_5",
            _ => settlType
        };
    }
    
    private string GetAllocTransTypeDescription(string allocTransType) {
        return allocTransType switch {
            "0" => "NEW",
            "1" => "REPLACE",
            "2" => "CANCEL",
            "3" => "PRELIMINARY",
            "4" => "CALCULATED",
            "5" => "CALCULATED_WITHOUT_PRELIMINARY",
            _ => allocTransType
        };
    }
    
    private string GetEncryptMethodDescription(string encryptMethod) {
        return encryptMethod switch {
            "0" => "NONE_OTHER",
            "1" => "PKCS_DES",
            "2" => "PKCS_1_DES",
            "3" => "PGP_DES_MD5",
            _ => encryptMethod
        };
    }
    
    private string GetCxlRejReasonDescription(string reason) {
        return reason switch {
            "0" => "TOO_LATE_TO_CANCEL",
            "1" => "UNKNOWN_ORDER",
            "2" => "BROKER_CREDIT_EXCHANGE_OPTION",
            "3" => "ORDER_ALREADY_IN_PENDING_CANCEL_OR_PENDING_REPLACE_STATUS",
            "4" => "UNABLE_TO_PROCESS_ORDER_MASS_CANCEL_REQUEST",
            "5" => "ORIGORDMODTIME_DID_NOT_MATCH_LAST_TRANSACTTIME_OF_ORDER",
            "6" => "DUPLICATE_CLORDID_RECEIVED",
            _ => reason
        };
    }
    
    private string GetOrdRejReasonDescription(string reason) {
        return reason switch {
            "0" => "BROKER_CREDIT_EXCHANGE_OPTION",
            "1" => "UNKNOWN_SYMBOL",
            "2" => "EXCHANGE_CLOSED",
            "3" => "ORDER_EXCEEDS_LIMIT",
            "4" => "TOO_LATE_TO_ENTER",
            "5" => "UNKNOWN_ORDER",
            "6" => "DUPLICATE_ORDER",
            "7" => "DUPLICATE_OF_A_VERBALLY_COMMUNICATED_ORDER",
            "8" => "STALE_ORDER",
            "9" => "TRADE_ALONG_REQUIRED",
            "10" => "INVALID_INVESTOR_ID",
            "11" => "UNSUPPORTED_ORDER_CHARACTERISTIC",
            "12" => "SURVEILLANCE_OPTION",
            "13" => "INCORRECT_QUANTITY",
            "14" => "INCORRECT_ALLOCATED_QUANTITY",
            "15" => "UNKNOWN_ACCOUNT",
            _ => reason
        };
    }
    
    private string GetSecurityTypeDescription(string securityType) {
        return securityType switch {
            "FUT" => "FUTURE",
            "OPT" => "OPTION",
            "EUSUPRA" => "EURO_SUPRANATIONAL_COUPONS",
            "FAC" => "FEDERAL_AGENCY_COUPON",
            "FADN" => "FEDERAL_AGENCY_DISCOUNT_NOTE",
            "PEF" => "PRIVATE_EXPORT_FUNDING",
            "SUPRA" => "USD_SUPRANATIONAL_COUPONS",
            "CORP" => "CORPORATE_BOND",
            "CPP" => "CORPORATE_PRIVATE_PLACEMENT",
            "CB" => "CONVERTIBLE_BOND",
            "DUAL" => "DUAL_CURRENCY",
            "EUCORP" => "EURO_CORPORATE_BOND",
            "XLINKD" => "INDEXED_LINKED",
            "STRUCT" => "STRUCTURED_NOTES",
            "YANK" => "YANKEE_CORPORATE_BOND",
            "FOR" => "FOREIGN_EXCHANGE_CONTRACT",
            "CS" => "COMMON_STOCK",
            "PS" => "PREFERRED_STOCK",
            _ => securityType
        };
    }
    
    private string GetPutOrCallDescription(string putOrCall) {
        return putOrCall switch {
            "0" => "PUT",
            "1" => "CALL",
            _ => putOrCall
        };
    }
    
    private string GetMDEntryTypeDescription(string entryType) {
        return entryType switch {
            "0" => "BID",
            "1" => "OFFER",
            "2" => "TRADE",
            "3" => "INDEX_VALUE",
            "4" => "OPENING_PRICE",
            "5" => "CLOSING_PRICE",
            "6" => "SETTLEMENT_PRICE",
            "7" => "TRADING_SESSION_HIGH_PRICE",
            "8" => "TRADING_SESSION_LOW_PRICE",
            "9" => "TRADING_SESSION_VWAP_PRICE",
            "A" => "IMBALANCE",
            "B" => "TRADE_VOLUME",
            "C" => "OPEN_INTEREST",
            _ => entryType
        };
    }
    
    private string GetSessionRejectReasonDescription(string reason) {
        return reason switch {
            "0" => "INVALID_TAG_NUMBER",
            "1" => "REQUIRED_TAG_MISSING",
            "2" => "TAG_NOT_DEFINED_FOR_THIS_MESSAGE_TYPE",
            "3" => "UNDEFINED_TAG",
            "4" => "TAG_SPECIFIED_WITHOUT_A_VALUE",
            "5" => "VALUE_IS_INCORRECT",
            "6" => "INCORRECT_DATA_FORMAT_FOR_VALUE",
            "7" => "DECRYPTION_PROBLEM",
            "8" => "SIGNATURE_PROBLEM",
            "9" => "COMPID_PROBLEM",
            "10" => "SENDINGTIME_ACCURACY_PROBLEM",
            "11" => "INVALID_MSGTYPE",
            _ => reason
        };
    }
    
    private string GetPartyIDSourceDescription(string source) {
        return source switch {
            "1" => "KOREAN_INVESTOR_ID",
            "2" => "TAIWANESE_QUALIFIED_FOREIGN_INVESTOR_ID_QFII_FID",
            "3" => "TAIWANESE_TRADING_ACCT",
            "4" => "MALAYSIAN_CENTRAL_DEPOSITORY",
            "5" => "CHINESE_INVESTOR_ID",
            "6" => "UK_NATIONAL_INSURANCE_OR_PENSION_NUMBER",
            "7" => "US_SOCIAL_SECURITY_NUMBER",
            "8" => "US_EMPLOYER_OR_TAX_ID_NUMBER",
            "9" => "AUSTRALIAN_BUSINESS_NUMBER",
            "A" => "AUSTRALIAN_TAX_FILE_NUMBER",
            "B" => "BIC_BANK_IDENTIFICATION_CODE",
            "C" => "GENERALLY_ACCEPTED_MARKET_PARTICIPANT_IDENTIFIER",
            "D" => "PROPRIETARY_CUSTOM_CODE",
            "E" => "ISO_COUNTRY_CODE",
            "F" => "SETTLEMENT_ENTITY_LOCATION",
            "G" => "MIC_MARKET_IDENTIFIER_CODE",
            "H" => "CSD_PARTICIPANT_MEMBER_CODE",
            _ => source
        };
    }
    
    private string GetCFICodeDescription(string cfiCode) {
        if (string.IsNullOrEmpty(cfiCode) || cfiCode.Length < 1)
            return cfiCode;
            
        var category = cfiCode[0] switch {
            'E' => "EQUITIES",
            'D' => "DEBT_INSTRUMENTS", 
            'R' => "ENTITLEMENTS_RIGHTS",
            'O' => "OPTIONS",
            'F' => "FUTURES",
            'S' => "SWAPS",
            'H' => "NON_LISTED_AND_COMPLEX_LISTED_OPTIONS",
            'I' => "SPOT",
            'J' => "FORWARDS",
            'K' => "STRATEGIES",
            'L' => "FINANCING",
            'M' => "NON_LISTED_DERIVATIVES",
            'T' => "REFERENTIAL_INSTRUMENTS",
            'C' => "COMMODITIES",
            _ => "OTHERS"
        };
        
        return $"{category} ({cfiCode})";
    }
    
    private string GetMsgTypeDescription(string msgType) {
        return msgType switch {
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
    
    private string GetOrdStatusDescription(string status) {
        return status switch {
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
    
    private string GetSideDescription(string side) {
        return side switch {
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
    
    private string GetOrdTypeDescription(string ordType) {
        return ordType switch {
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
    
    private string GetTimeInForceDescription(string tif) {
        return tif switch {
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
    
    private string GetExecTypeDescription(string execType) {
        return execType switch {
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
    
    private string NormalizeFixMessage(string rawMessage) {
        var normalized = rawMessage;
        
        if (normalized.Contains('|'))
            normalized = normalized.Replace('|', '\u0001');
        
        if (normalized.Contains("^A"))
            normalized = normalized.Replace("^A", "\u0001");
            
        return normalized;
    }
    
    private int GetSortOrder(string tagNumber) {
        // Special handling for version info
        if (tagNumber == "VERSION")
            return -1;
            
        if (int.TryParse(tagNumber, out var tag)) {
            // Standard FIX field ordering: Header fields first, then body, then trailer
            return tag switch {
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

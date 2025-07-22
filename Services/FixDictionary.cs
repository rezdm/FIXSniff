using System.Collections.Generic;

namespace FIXSniff.Services;

public static class FixDictionary
{
    private static readonly Dictionary<int, (string Name, string Description)> _fieldDefinitions = new()
    {
        { 1, ("Account", "Account mnemonic as agreed between buy and sell sides, e.g. broker and institution or investor/intermediary and fund manager.") },
        { 6, ("AvgPx", "Calculated average price of all fills on this order.") },
        { 8, ("BeginString", "Identifies beginning of new message and protocol version.") },
        { 9, ("BodyLength", "Message length, in bytes, forward to the CheckSum field.") },
        { 10, ("CheckSum", "Three byte, simple checksum.") },
        { 11, ("ClOrdID", "Unique identifier for Order as assigned by the buy-side (institution, broker, intermediary etc.)") },
        { 14, ("CumQty", "Total quantity filled.") },
        { 15, ("Currency", "Identifies currency used for price.") },
        { 17, ("ExecID", "Unique identifier of execution message as assigned by sell-side (broker, exchange, ECN).") },
        { 20, ("ExecTransType", "Identifies transaction type.") },
        { 21, ("HandlInst", "Instructions for order handling on Broker trading floor.") },
        { 30, ("LastMkt", "Market of execution for last fill, or an indication of the market where an order was routed.") },
        { 31, ("LastPx", "Price of this (last) fill.") },
        { 32, ("LastQty", "Quantity of shares bought/sold on this (last) fill.") },
        { 34, ("MsgSeqNum", "Integer message sequence number.") },
        { 35, ("MsgType", "Defines message type.") },
        { 36, ("NewSeqNo", "New sequence number.") },
        { 37, ("OrderID", "Unique identifier for Order as assigned by sell-side (broker, exchange, ECN).") },
        { 38, ("OrderQty", "Quantity ordered. This represents the number of shares for equities or based on normal convention the number of contracts for derivatives.") },
        { 39, ("OrdStatus", "Identifies current status of order.") },
        { 40, ("OrdType", "Order type.") },
        { 41, ("OrigClOrdID", "ClOrdID of the previous order (NOT the initial order of the day) when canceling or replacing an order.") },
        { 43, ("PossDupFlag", "Indicates possible retransmission of message with this sequence number.") },
        { 44, ("Price", "Price per unit of quantity (e.g. per share).") },
        { 45, ("RefSeqNum", "Reference message sequence number.") },
        { 49, ("SenderCompID", "Assigned value used to identify firm sending message.") },
        { 50, ("SenderSubID", "Assigned value used to identify specific message originator (desk, trader, etc.)") },
        { 52, ("SendingTime", "Time of message transmission (always expressed in UTC (Universal Time Coordinated, also known as \"GMT\"))") },
        { 54, ("Side", "Side of order (1=Buy, 2=Sell, etc.)") },
        { 55, ("Symbol", "Ticker symbol. Common, \"human understood\" representation of the security.") },
        { 56, ("TargetCompID", "Assigned value used to identify receiving firm.") },
        { 57, ("TargetSubID", "Assigned value used to identify specific individual or unit intended to receive message.") },
        { 58, ("Text", "Free format text string.") },
        { 59, ("TimeInForce", "Specifies how long the order remains in effect.") },
        { 60, ("TransactTime", "Timestamp when the business transaction represented by the message occurred.") },
        { 75, ("TradeDate", "Indicates date of trade referenced in this message.") },
        { 76, ("ExecBroker", "Identifies executing / give-up broker.") },
        { 98, ("EncryptMethod", "Method of encryption.") },
        { 102, ("CxlRejReason", "Code to identify reason for cancel rejection.") },
        { 103, ("OrdRejReason", "Code to identify reason for order rejection.") },
        { 108, ("HeartBtInt", "Heartbeat interval (seconds).") },
        { 112, ("TestReqID", "Identifier included in Test Request message to be returned in resulting Heartbeat.") },
        { 141, ("ResetSeqNumFlag", "Indicates that the Sequence Reset message is replacing administrative or application messages which will not be resent.") },
        { 150, ("ExecType", "Describes the type of execution report.") },
        { 151, ("LeavesQty", "Quantity open for further execution.") },
        { 167, ("SecurityType", "Indicates type of security.") },
        { 371, ("RefTagID", "The tag number of the FIX field being referenced.") },
        { 372, ("RefMsgType", "The MsgType of the FIX message being referenced.") },
        { 373, ("SessionRejectReason", "Code to identify reason for a session-level Reject message.") },
        { 553, ("Username", "Username or ID.") },
        { 554, ("Password", "Password.") },
        { 789, ("NextExpectedMsgSeqNum", "The next expected MsgSeqNum value to be received.") },
        { 1128, ("AppVerID", "Specifies the service pack release being applied at message level.") }
    };

    public static (string Name, string Description) GetFieldInfo(int tag)
    {
        if (_fieldDefinitions.TryGetValue(tag, out var info))
        {
            return info;
        }
        return ($"Tag{tag}", $"Unknown field tag {tag}. This may be a custom or newer FIX field not in our dictionary.");
    }

    public static string GetMsgTypeDescription(string msgType)
    {
        return msgType switch
        {
            "0" => "Heartbeat",
            "1" => "Test Request",
            "2" => "Resend Request",
            "3" => "Reject",
            "4" => "Sequence Reset",
            "5" => "Logout",
            "6" => "Indication of Interest",
            "7" => "Advertisement",
            "8" => "Execution Report",
            "9" => "Order Cancel Reject",
            "A" => "Logon",
            "B" => "News",
            "C" => "Email",
            "D" => "New Order - Single",
            "E" => "New Order - List",
            "F" => "Order Cancel Request",
            "G" => "Order Cancel/Replace Request",
            "H" => "Order Status Request",
            "J" => "Allocation Instruction",
            "K" => "List Cancel Request",
            "L" => "List Execute",
            "M" => "List Status Request",
            "N" => "List Status",
            "P" => "Allocation Instruction Ack",
            "Q" => "Don't Know Trade",
            "R" => "Quote Request",
            "S" => "Quote",
            "T" => "Settlement Instructions",
            "V" => "Market Data Request",
            "W" => "Market Data - Snapshot/Full Refresh",
            "X" => "Market Data - Incremental Refresh",
            "Y" => "Market Data Request Reject",
            "Z" => "Quote Cancel",
            "a" => "Quote Status Request",
            "b" => "Mass Quote Acknowledgement",
            "c" => "Security Definition Request",
            "d" => "Security Definition",
            "e" => "Security Status Request",
            "f" => "Security Status",
            "g" => "Trading Session Status Request",
            "h" => "Trading Session Status",
            "i" => "Mass Quote",
            "j" => "Business Message Reject",
            "k" => "Bid Request",
            "l" => "Bid Response",
            "m" => "List Strike Price",
            _ => $"Unknown message type: {msgType}"
        };
    }
}


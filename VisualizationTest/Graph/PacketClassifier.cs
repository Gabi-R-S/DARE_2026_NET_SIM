using System;
using System.Windows;
using System.Windows.Media;

namespace VisualizationTest.Graph
{
    public sealed record PacketInfo(
        bool IsProtocol,
        string Category,
        string Label,
        Brush Brush,
        Brush GlowBrush,
        double Size
    );

    /// <summary>
    /// Classifies network packets based on their actual JSON payload structure.
    ///
    /// Layer wrapping order (outermost to innermost at the simulator level):
    ///
    /// Flood stack:
    ///   FloodMessage { OriginalSender, IntendedTarget, NumberOfHops, Tag, Message }
    ///     └─ HeartbeatMessage { HeartBeat:true/false, Message }   [HeartBeat=true → pure heartbeat]
    ///         └─ EnsureAckMessage { Tag, Message, IsAck:true/false }  [IsAck=true → ACK]
    ///             └─ App CRDT payload  (ORSet Adds/Removes, etc.)
    ///
    /// Distance Vector stack:
    ///   DistanceVectorMessage { OriginalSender, IntendedTarget, IsProtocolMessage, Message, TTLhops }
    ///     IsProtocolMessage=true  → DistanceTableUpdateMessage { Distances }  (DV routing update)
    ///     IsProtocolMessage=false → EnsureAckMessage { Tag, Message, IsAck:true/false }
    ///         └─ App CRDT payload
    ///
    /// Link State stack:
    ///   LinkStateMessage { OriginalSender, IntendedTarget, IsProtocolMessage, Message }
    ///     IsProtocolMessage=true  → LSA { Replica, Number, Neighbours }  (link-state advertisement)
    ///     IsProtocolMessage=false → HeartbeatMessage | EnsureAckMessage → App CRDT payload
    ///
    /// Detection strategy: use Contains() on the full raw JSON string (including nested levels),
    /// prioritising the most specific/rare markers first.
    /// </summary>
    public static class PacketClassifier
    {
        // App-relevant packets: Vibrant Emerald Green
        public static readonly Brush AppBrush = Freeze(new SolidColorBrush(Color.FromRgb(16, 185, 129))); // Emerald 500
        public static readonly Brush AppGlow = Freeze(new SolidColorBrush(Color.FromArgb(120, 52, 211, 153))); // Emerald 400 glow

        // Protocol - Heartbeat: Sky Blue / Cyan
        public static readonly Brush HeartbeatBrush = Freeze(new SolidColorBrush(Color.FromRgb(14, 165, 233))); // Sky 500
        public static readonly Brush HeartbeatGlow = Freeze(new SolidColorBrush(Color.FromArgb(100, 56, 189, 248))); // Sky 400 glow

        // Protocol - Acknowledgements (ACK): Vibrant Purple
        public static readonly Brush AckBrush = Freeze(new SolidColorBrush(Color.FromRgb(168, 85, 247))); // Purple 500
        public static readonly Brush AckGlow = Freeze(new SolidColorBrush(Color.FromArgb(110, 192, 132, 252))); // Purple 400 glow

        // Protocol - Distance Vector Routing Updates: Vibrant Orange
        public static readonly Brush RoutingBrush = Freeze(new SolidColorBrush(Color.FromRgb(249, 115, 22))); // Orange 500
        public static readonly Brush RoutingGlow = Freeze(new SolidColorBrush(Color.FromArgb(110, 251, 146, 60))); // Orange 400 glow

        // Protocol - Link State Advertisements (LSA): Amber / Gold
        public static readonly Brush LsaBrush = Freeze(new SolidColorBrush(Color.FromRgb(245, 158, 11))); // Amber 500
        public static readonly Brush LsaGlow = Freeze(new SolidColorBrush(Color.FromArgb(110, 251, 191, 36))); // Amber 400 glow

        // Protocol - General / Fallback Protocol: Rose / Pink
        public static readonly Brush GeneralProtocolBrush = Freeze(new SolidColorBrush(Color.FromRgb(244, 63, 94))); // Rose 500
        public static readonly Brush GeneralProtocolGlow = Freeze(new SolidColorBrush(Color.FromArgb(100, 251, 113, 133)));

        private static readonly System.Text.RegularExpressions.Regex HeartbeatRegex = new(
            @"""ProtocolTag""\s*:\s*""heartbeat""|""Timestamp""\s*:|""HeartBeat""\s*:\s*(?:true|""true"")\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex AckRegex = new(
            @"""IsAck""\s*:\s*(?:true|""true"")\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex DistancesRegex = new(
            @"""Distances""\s*:",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex NeighboursRegex = new(
            @"""Neighbours""\s*:",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex ProtocolMsgRegex = new(
            @"""IsProtocolMessage""\s*:\s*(?:true|""true"")\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex AddsRegex = new(
            @"""adds""\s*:",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex RemovesRegex = new(
            @"""removes""\s*:",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex AppTagRegex = new(
            @"""ProtocolTag""\s*:\s*""app""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            if (freezable.CanFreeze) freezable.Freeze();
            return freezable;
        }

        /// <summary>
        /// Classifies a NetworkMessage.Payload string into a PacketInfo with the appropriate
        /// visual properties (color, size, label) for the graph visualization.
        /// </summary>
        public static PacketInfo Classify(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new PacketInfo(true, "Protocol", "PROT", GeneralProtocolBrush, GeneralProtocolGlow, 4.0);
            }

            // In nested network layers, payloads are wrapped in JSON string properties (e.g. Message.Payload).
            // Each level of nesting escapes quotes with backslashes (\", \\\", \\\\\\\", etc.).
            // Removing all backslashes produces an unescaped representation where JSON keys
            // and values can be matched reliably regardless of nesting depth.
            string clean = payload.Replace("\\", "");

            // ── 1. Pure heartbeat ──────────────────────────────────────────────────────
            // HeartbeatBehaviour serializes ProtocolTag:"heartbeat" wrapping HeartbeatMessagePayload (with Timestamp).
            if (clean.Contains("\"ProtocolTag\":\"heartbeat\"", StringComparison.OrdinalIgnoreCase)
                || clean.Contains("\"heartbeat\"", StringComparison.OrdinalIgnoreCase)
                || HeartbeatRegex.IsMatch(clean))
            {
                return new PacketInfo(true, "Heartbeat", "HB", HeartbeatBrush, HeartbeatGlow, 4.0);
            }

            // ── 2. ACK packet ──────────────────────────────────────────────────────────
            // EnsureAckNetworkLayer sends IsAck:true for pure acknowledgements (Message payload is empty string "").
            if (AckRegex.IsMatch(clean))
            {
                return new PacketInfo(true, "Ack", "ACK", AckBrush, AckGlow, 4.5);
            }

            // ── 3. Distance Vector routing update ──────────────────────────────────────
            // DistanceVectorMessage with IsProtocolMessage:true wraps DistanceTableUpdateMessagePayload
            // containing "Distances":{...}.
            if (DistancesRegex.IsMatch(clean)
                || clean.Contains("DistanceTableUpdate", StringComparison.OrdinalIgnoreCase)
                || (clean.Contains("\"dv\"", StringComparison.OrdinalIgnoreCase) && ProtocolMsgRegex.IsMatch(clean)))
            {
                return new PacketInfo(true, "Distance Vector Update", "DV", RoutingBrush, RoutingGlow, 5.0);
            }

            // ── 4. Link State Advertisement (LSA flood) ────────────────────────────────
            // LinkStateMessage with IsProtocolMessage:true wraps an LSA containing "Neighbours":[...].
            if (NeighboursRegex.IsMatch(clean)
                || clean.Contains("\"LSA\"", StringComparison.OrdinalIgnoreCase)
                || (clean.Contains("\"ls\"", StringComparison.OrdinalIgnoreCase) && ProtocolMsgRegex.IsMatch(clean)))
            {
                return new PacketInfo(true, "Link State Advertisement", "LSA", LsaBrush, LsaGlow, 5.0);
            }

            // ── 5. App CRDT data ────────────────────────────────────────────────────────
            // InsertBehaviour sends ORSet operations with "adds" or "removes",
            // wrapped in HistoryTrackedMessage with "CausalityTracker", and Message.ProtocolTag == "app".
            bool hasAppContent = AppTagRegex.IsMatch(clean)
                              || AddsRegex.IsMatch(clean)
                              || RemovesRegex.IsMatch(clean)
                              || clean.Contains("CausalityTracker", StringComparison.OrdinalIgnoreCase);

            if (hasAppContent)
            {
                return new PacketInfo(false, "App Data (CRDT)", "APP", AppBrush, AppGlow, 6.5);
            }

            // ── 6. Generic IsProtocolMessage:true (any other routing protocol control msg) ──
            if (ProtocolMsgRegex.IsMatch(clean))
            {
                return new PacketInfo(true, "Routing Protocol", "ROUTE", RoutingBrush, RoutingGlow, 5.0);
            }

            // ── 7. Default: treat remaining as fallback protocol traffic ─────────────────
            return new PacketInfo(true, "Protocol", "PROT", GeneralProtocolBrush, GeneralProtocolGlow, 4.0);
        }
    }
}

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

            // ── 1. Pure heartbeat ──────────────────────────────────────────────────────
            // HeartbeatMessage serializes HeartBeat:true when it is a probe (no data payload).
            // This appears anywhere in the JSON string because Contains() searches recursively.
            if (payload.Contains("\"HeartBeat\":true", StringComparison.OrdinalIgnoreCase))
            {
                return new PacketInfo(true, "Heartbeat", "HB", HeartbeatBrush, HeartbeatGlow, 4.0);
            }

            // ── 2. ACK packet ──────────────────────────────────────────────────────────
            // EnsureAckNetworkLayer sends IsAck:true for pure acknowledgements.
            // These carry no app data (Message payload is empty string "").
            if (payload.Contains("\"IsAck\":true", StringComparison.OrdinalIgnoreCase))
            {
                return new PacketInfo(true, "Ack", "ACK", AckBrush, AckGlow, 4.5);
            }

            // ── 3. Distance Vector routing update ──────────────────────────────────────
            // DistanceVectorMessage with IsProtocolMessage:true wraps a DistanceTableUpdateMessage
            // that contains "Distances":{...}. Both markers appear in the same payload.
            if (payload.Contains("\"IsProtocolMessage\":true", StringComparison.OrdinalIgnoreCase)
                && payload.Contains("\"Distances\":", StringComparison.OrdinalIgnoreCase))
            {
                return new PacketInfo(true, "Distance Vector Update", "DV", RoutingBrush, RoutingGlow, 5.0);
            }

            // ── 4. Link State Advertisement (LSA flood) ────────────────────────────────
            // LinkStateMessage with IsProtocolMessage:true wraps an LSA containing "Neighbours":[...].
            if (payload.Contains("\"IsProtocolMessage\":true", StringComparison.OrdinalIgnoreCase)
                && payload.Contains("\"Neighbours\":", StringComparison.OrdinalIgnoreCase))
            {
                return new PacketInfo(true, "Link State Advertisement", "LSA", LsaBrush, LsaGlow, 5.0);
            }

            // ── 5. Generic IsProtocolMessage:true (any other routing protocol control msg) ──
            if (payload.Contains("\"IsProtocolMessage\":true", StringComparison.OrdinalIgnoreCase))
            {
                return new PacketInfo(true, "Routing Protocol", "ROUTE", RoutingBrush, RoutingGlow, 5.0);
            }

            // ── 6. Standalone Distances or Neighbours field (safety net) ───────────────
            if (payload.Contains("\"Distances\":", StringComparison.OrdinalIgnoreCase))
            {
                return new PacketInfo(true, "Distance Table Update", "DV", RoutingBrush, RoutingGlow, 5.0);
            }
            if (payload.Contains("\"Neighbours\":", StringComparison.OrdinalIgnoreCase))
            {
                return new PacketInfo(true, "Link State LSA", "LSA", LsaBrush, LsaGlow, 5.0);
            }

            // ── 7. App CRDT data ────────────────────────────────────────────────────────
            // InsertBehaviour sends ORSet operations serialized as JSON with "Adds" or "Removes".
            // CausalHistoryTrackerLayer adds "CausalityTracker" metadata.
            // IsAck:false marks a data-carrying EnsureAck envelope, BUT we only treat it as App
            // if there is actual app-level content (Adds/Removes/CausalityTracker) present too,
            // to avoid misclassifying bare routing envelopes that happen to have IsAck:false.
            bool hasAppContent = payload.Contains("\"Adds\":", StringComparison.OrdinalIgnoreCase)
                              || payload.Contains("\"Removes\":", StringComparison.OrdinalIgnoreCase)
                              || payload.Contains("CausalityTracker", StringComparison.OrdinalIgnoreCase);

            bool isDataCarrier = payload.Contains("\"IsAck\":false", StringComparison.OrdinalIgnoreCase);

            if (hasAppContent && isDataCarrier)
            {
                return new PacketInfo(false, "App Data (CRDT)", "APP", AppBrush, AppGlow, 6.5);
            }

            if (hasAppContent)
            {
                return new PacketInfo(false, "App Data (CRDT)", "APP", AppBrush, AppGlow, 6.5);
            }

            // ── 8. Default: treat remaining as protocol traffic ─────────────────────────
            // In practice this catches flood/routing envelopes that carry app messages but
            // don't expose the inner content at this JSON level (e.g. a FloodMessage carrying
            // a HeartbeatMessage carrying an EnsureAck carrying app data – only the flood
            // wrapper is visible without full deserialization).
            return new PacketInfo(true, "Protocol", "PROT", GeneralProtocolBrush, GeneralProtocolGlow, 4.0);
        }
    }
}

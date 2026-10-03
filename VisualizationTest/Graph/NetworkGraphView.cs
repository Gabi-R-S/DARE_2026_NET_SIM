using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace VisualizationTest.Graph
{
    /// <summary>
    /// High-performance, read-only WPF visualization control for network graphs
    /// with animated particles traveling along edges.
    /// </summary>
    public class NetworkGraphView : FrameworkElement
    {
        #region Dependency Properties

        public static readonly DependencyProperty StateProperty =
            DependencyProperty.Register(
                nameof(State),
                typeof(GraphState),
                typeof(NetworkGraphView),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnStateChanged));

        public static readonly DependencyProperty BackgroundBrushProperty =
            DependencyProperty.Register(
                nameof(BackgroundBrush),
                typeof(Brush),
                typeof(NetworkGraphView),
                new FrameworkPropertyMetadata(
                    Freeze(new SolidColorBrush(Color.FromRgb(15, 23, 42))), // Slate 900
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowGridProperty =
            DependencyProperty.Register(
                nameof(ShowGrid),
                typeof(bool),
                typeof(NetworkGraphView),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowNodeLabelsProperty =
            DependencyProperty.Register(
                nameof(ShowNodeLabels),
                typeof(bool),
                typeof(NetworkGraphView),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public GraphState? State
        {
            get => (GraphState?)GetValue(StateProperty);
            set => SetValue(StateProperty, value);
        }

        public Brush BackgroundBrush
        {
            get => (Brush)GetValue(BackgroundBrushProperty);
            set => SetValue(BackgroundBrushProperty, value);
        }

        public bool ShowGrid
        {
            get => (bool)GetValue(ShowGridProperty);
            set => SetValue(ShowGridProperty, value);
        }

        public bool ShowNodeLabels
        {
            get => (bool)GetValue(ShowNodeLabelsProperty);
            set => SetValue(ShowNodeLabelsProperty, value);
        }

        private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NetworkGraphView view)
            {
                view.InvalidateVisual();
            }
        }

        #endregion

        #region Events

        public event EventHandler<GraphParticle>? ParticleArrived;

        #endregion

        #region Transform & Viewport Fields

        private double _zoom = 1.0;
        private Vector _pan = new(0, 0);

        public double Zoom
        {
            get => _zoom;
            set
            {
                _zoom = Math.Clamp(value, MinZoom, MaxZoom);
                InvalidateVisual();
            }
        }

        public Vector Pan
        {
            get => _pan;
            set
            {
                _pan = value;
                InvalidateVisual();
            }
        }

        public double MinZoom { get; set; } = 0.05;
        public double MaxZoom { get; set; } = 25.0;

        private bool _isPanning;
        private Point _lastMousePosition;
        private Point _currentMousePosition;

        // Hover inspection
        private GraphNode? _hoveredNode;

        #endregion

        #region Animation & Performance Fields

        private readonly Stopwatch _stopwatch = new();
        private TimeSpan _lastRenderTime;
        private bool _isAnimating = true;

        // FPS calculation
        private int _frameCount;
        private double _fpsTimer;
        public double CurrentFps { get; private set; }

        #endregion

        #region Cached Brushes & Pens

        private static readonly Typeface NodeFontTypeface =
            new(new FontFamily("Segoe UI, Segoe UI Variable, sans-serif"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        private static readonly Typeface TooltipFontTypeface =
            new(new FontFamily("Segoe UI, Segoe UI Variable, sans-serif"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), 1.0));
        private static readonly Pen DefaultEdgePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(160, 100, 116, 139)), 1.5)); // Slate 500
        private static readonly Pen InactiveEdgePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(60, 148, 163, 184)), 1.0) { DashStyle = DashStyles.Dash });
        private static readonly Pen DefaultNodePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(56, 189, 248)), 2.0)); // Sky 400
        private static readonly Pen HoveredNodePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(250, 204, 21)), 3.0)); // Amber 400

        private static readonly Brush DefaultNodeBrush = Freeze(new SolidColorBrush(Color.FromRgb(30, 41, 59))); // Slate 800
        private static readonly Brush NodeTextBrush = Freeze(new SolidColorBrush(Color.FromRgb(241, 245, 249))); // Slate 100
        private static readonly Brush DefaultParticleCore = Freeze(new SolidColorBrush(Color.FromRgb(245, 158, 11))); // Amber 500
        private static readonly Brush DefaultParticleGlow = Freeze(new SolidColorBrush(Color.FromArgb(100, 251, 191, 36))); // Amber 400 glow
        private static readonly Brush TooltipBackground = Freeze(new SolidColorBrush(Color.FromArgb(235, 15, 23, 42)));
        private static readonly Pen TooltipBorder = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(71, 85, 105)), 1.0));
        private static readonly Brush TooltipTextBrush = Freeze(new SolidColorBrush(Color.FromRgb(226, 232, 240)));

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            if (freezable.CanFreeze) freezable.Freeze();
            return freezable;
        }

        #endregion

        public NetworkGraphView()
        {
            ClipToBounds = true;
            Focusable = true;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            MouseWheel += OnMouseWheel;
            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            MouseLeave += OnMouseLeave;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _stopwatch.Restart();
            _lastRenderTime = _stopwatch.Elapsed;
            StartAnimationLoop();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            StopAnimationLoop();
        }

        public void StartAnimationLoop()
        {
            if (!_isAnimating)
            {
                _isAnimating = true;
                CompositionTarget.Rendering += OnCompositionRendering;
                _stopwatch.Restart();
                _lastRenderTime = _stopwatch.Elapsed;
            }
            else
            {
                CompositionTarget.Rendering -= OnCompositionRendering;
                CompositionTarget.Rendering += OnCompositionRendering;
            }
        }

        public void StopAnimationLoop()
        {
            _isAnimating = false;
            CompositionTarget.Rendering -= OnCompositionRendering;
        }

        private void OnCompositionRendering(object? sender, EventArgs e)
        {
            var now = _stopwatch.Elapsed;
            double delta = (now - _lastRenderTime).TotalSeconds;
            _lastRenderTime = now;

            // Clamp delta to avoid massive jump if window was frozen/paused
            if (delta > 0.1) delta = 0.1;
            if (delta <= 0) return;

            // FPS Counter
            _frameCount++;
            _fpsTimer += delta;
            if (_fpsTimer >= 1.0)
            {
                CurrentFps = _frameCount / _fpsTimer;
                _frameCount = 0;
                _fpsTimer = 0;
            }

            // Advance particles
            if (State != null)
            {
                State.AdvanceParticles(delta, arrived =>
                {
                    ParticleArrived?.Invoke(this, arrived);
                });
            }

            InvalidateVisual();
        }

        #region Rendering

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double width = ActualWidth;
            double height = ActualHeight;

            if (width <= 0 || height <= 0) return;

            // 1. Draw Background
            dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, width, height));

            // 2. Draw subtle grid
            if (ShowGrid)
            {
                DrawBackgroundGrid(dc, width, height);
            }

            var state = State;
            if (state == null) return;

            // 3. World transformation
            dc.PushTransform(new TranslateTransform(_pan.X, _pan.Y));
            dc.PushTransform(new ScaleTransform(_zoom, _zoom));

            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            // Thread-safe drawing of edges, particles, and nodes
            state.ForEachItem(
                edges => DrawEdges(dc, edges, state),
                particles => DrawParticles(dc, particles, state),
                nodes => DrawNodes(dc, nodes, dpi));

            // Pop transformations
            dc.Pop();
            dc.Pop();

            // 4. Screen-space UI Overlays (e.g. Node Tooltip badge)
            if (_hoveredNode != null)
            {
                DrawNodeTooltip(dc, _hoveredNode, dpi);
            }
        }

        private void DrawBackgroundGrid(DrawingContext dc, double width, double height)
        {
            // Grid spacing adjusts with zoom
            double baseGridSize = 40.0;
            double scaledGrid = baseGridSize * _zoom;

            while (scaledGrid < 25.0) scaledGrid *= 2.0;
            while (scaledGrid > 100.0) scaledGrid /= 2.0;

            double startX = _pan.X % scaledGrid;
            if (startX > 0) startX -= scaledGrid;

            double startY = _pan.Y % scaledGrid;
            if (startY > 0) startY -= scaledGrid;

            for (double x = startX; x < width; x += scaledGrid)
            {
                dc.DrawLine(GridPen, new Point(x, 0), new Point(x, height));
            }

            for (double y = startY; y < height; y += scaledGrid)
            {
                dc.DrawLine(GridPen, new Point(0, y), new Point(width, y));
            }
        }

        private void DrawEdges(DrawingContext dc, IEnumerable<GraphEdge> edges, GraphState state)
        {
            var nodes = state.Nodes;

            foreach (var edge in edges)
            {
                if (!nodes.TryGetValue(edge.SourceId, out var sourceNode) ||
                    !nodes.TryGetValue(edge.TargetId, out var targetNode))
                {
                    continue;
                }

                Point p1 = sourceNode.Position;
                Point p2 = targetNode.Position;

                // Pen styling
                Pen pen;
                if (!edge.IsActive)
                {
                    pen = InactiveEdgePen;
                }
                else if (edge.Activity > 0.05)
                {
                    // Glowing active line
                    var activeColor = Color.FromArgb(
                        (byte)(180 + Math.Clamp(edge.Activity, 0, 1) * 75),
                        56, 189, 248);
                    pen = new Pen(new SolidColorBrush(activeColor), edge.Thickness + edge.Activity * 2.5);
                }
                else if (edge.Stroke != null)
                {
                    pen = new Pen(edge.Stroke, edge.Thickness);
                }
                else
                {
                    pen = DefaultEdgePen;
                }

                dc.DrawLine(pen, p1, p2);

                // If directed, draw subtle direction arrow
                if (edge.IsDirected)
                {
                    DrawArrowHead(dc, p1, p2, targetNode.Radius + 6.0, pen.Brush);
                }
            }
        }

        private void DrawArrowHead(DrawingContext dc, Point p1, Point p2, double offsetFromTarget, Brush fill)
        {
            Vector dir = p2 - p1;
            double len = dir.Length;
            if (len < 10) return;

            dir.Normalize();
            Point tip = p2 - dir * offsetFromTarget;

            Vector normal = new(-dir.Y, dir.X);
            double arrowSize = 6.0;

            Point left = tip - dir * (arrowSize * 1.5) + normal * arrowSize;
            Point right = tip - dir * (arrowSize * 1.5) - normal * arrowSize;

            StreamGeometry geom = new();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(tip, true, true);
                ctx.LineTo(left, true, false);
                ctx.LineTo(right, true, false);
            }
            geom.Freeze();

            dc.DrawGeometry(fill, null, geom);
        }

        private void DrawParticles(DrawingContext dc, IEnumerable<GraphParticle> particles, GraphState state)
        {
            var nodes = state.Nodes;

            foreach (var p in particles)
            {
                if (!state.TryGetNode(p.SourceId, out var src) || src == null ||
                    !state.TryGetNode(p.TargetId, out var tgt) || tgt == null)
                {
                    continue;
                }

                double t = Math.Clamp(p.Progress, 0.0, 1.0);
                Point pos = Lerp(src.Position, tgt.Position, t);

                Brush coreBrush = p.Fill ?? DefaultParticleCore;
                Brush glowBrush = p.GlowBrush ?? DefaultParticleGlow;

                double size = p.Size;

                // Motion Trail (small fading trailing segment)
                Vector dir = tgt.Position - src.Position;
                if (dir.LengthSquared > 0.01)
                {
                    dir.Normalize();
                    Point trailPos = pos - dir * (size * 1.6);
                    dc.DrawEllipse(glowBrush, null, trailPos, size * 0.75, size * 0.75);
                }

                // Halo glow
                dc.DrawEllipse(glowBrush, null, pos, size * 1.8, size * 1.8);

                // Core particle
                dc.DrawEllipse(coreBrush, null, pos, size, size);
            }
        }

        private void DrawNodes(DrawingContext dc, IEnumerable<GraphNode> nodes, double dpi)
        {
            foreach (var node in nodes)
            {
                Point pos = node.Position;
                double r = node.Radius;

                bool isHovered = ReferenceEquals(node, _hoveredNode);

                Brush fill = node.Fill ?? DefaultNodeBrush;
                Pen pen = isHovered ? HoveredNodePen : (node.Stroke != null ? new Pen(node.Stroke, node.StrokeThickness) : DefaultNodePen);

                // Outer aura on hover
                if (isHovered)
                {
                    dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(50, 250, 204, 21)), null, pos, r + 6, r + 6);
                }

                // Draw circle
                dc.DrawEllipse(fill, pen, pos, r, r);

                // Draw label
                if (ShowNodeLabels && !string.IsNullOrWhiteSpace(node.Label))
                {
                    double fontSize = Math.Max(9.0, Math.Min(14.0, r * 0.85));
                    Brush textBrush = node.TextBrush ?? NodeTextBrush;

                    var ft = new FormattedText(
                        node.Label,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        NodeFontTypeface,
                        fontSize,
                        textBrush,
                        dpi);

                    Point textOrigin = new(pos.X - ft.Width / 2.0, pos.Y - ft.Height / 2.0);
                    dc.DrawText(ft, textOrigin);
                }
            }
        }

        private void DrawNodeTooltip(DrawingContext dc, GraphNode node, double dpi)
        {
            Point screenPos = WorldToScreen(node.Position);
            double tooltipX = screenPos.X + node.Radius * _zoom + 12;
            double tooltipY = screenPos.Y - 20;

            string text = $"Node: {node.Label ?? node.Id.ToString()}\nPos: ({node.Position.X:F0}, {node.Position.Y:F0})";
            if (node.Tag != null)
            {
                text += $"\nInfo: {node.Tag}";
            }

            var ft = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                TooltipFontTypeface,
                11.0,
                TooltipTextBrush,
                dpi);

            double pad = 6.0;
            Rect bgRect = new(tooltipX, tooltipY, ft.Width + pad * 2, ft.Height + pad * 2);

            // Keep within bounds
            if (bgRect.Right > ActualWidth)
            {
                bgRect.X = screenPos.X - node.Radius * _zoom - 12 - bgRect.Width;
            }

            dc.DrawRoundedRectangle(TooltipBackground, TooltipBorder, bgRect, 4, 4);
            dc.DrawText(ft, new Point(bgRect.X + pad, bgRect.Y + pad));
        }

        private static Point Lerp(Point a, Point b, double t)
        {
            return new Point(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t);
        }

        #endregion

        #region Coordinate Transformations

        public Point ScreenToWorld(Point screen)
        {
            return new Point(
                (screen.X - _pan.X) / _zoom,
                (screen.Y - _pan.Y) / _zoom);
        }

        public Point WorldToScreen(Point world)
        {
            return new Point(
                world.X * _zoom + _pan.X,
                world.Y * _zoom + _pan.Y);
        }

        #endregion

        #region Pan, Zoom & Interaction Handlers

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            Point mousePos = e.GetPosition(this);

            double oldZoom = _zoom;
            double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
            double newZoom = Math.Clamp(oldZoom * factor, MinZoom, MaxZoom);

            if (Math.Abs(newZoom - oldZoom) < 0.0001) return;

            // Keep the world point under the mouse cursor stationary
            Vector before = new((mousePos.X - _pan.X) / oldZoom, (mousePos.Y - _pan.Y) / oldZoom);
            Vector after = new((mousePos.X - _pan.X) / newZoom, (mousePos.Y - _pan.Y) / newZoom);

            _pan += (after - before) * newZoom;
            _zoom = newZoom;

            InvalidateVisual();
            e.Handled = true;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Read-only: Any drag with Left, Middle, or Right pans the viewport
            if (e.ChangedButton == MouseButton.Left ||
                e.ChangedButton == MouseButton.Middle ||
                e.ChangedButton == MouseButton.Right)
            {
                _isPanning = true;
                _lastMousePosition = e.GetPosition(this);
                CaptureMouse();
                Cursor = Cursors.SizeAll;
                e.Handled = true;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            _currentMousePosition = e.GetPosition(this);

            if (_isPanning)
            {
                Vector delta = _currentMousePosition - _lastMousePosition;
                _pan += delta;
                _lastMousePosition = _currentMousePosition;
                InvalidateVisual();
                return;
            }

            // Hit test for hovered node (read-only inspection tooltip)
            UpdateHoveredNode(_currentMousePosition);
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            if (_hoveredNode != null)
            {
                _hoveredNode = null;
                InvalidateVisual();
            }
        }

        private void UpdateHoveredNode(Point mouseScreen)
        {
            if (State == null) return;

            Point worldPos = ScreenToWorld(mouseScreen);
            GraphNode? found = null;

            foreach (var node in State.Nodes.Values)
            {
                Vector d = node.Position - worldPos;
                if (d.LengthSquared <= (node.Radius + 4) * (node.Radius + 4))
                {
                    found = node;
                    break;
                }
            }

            if (!ReferenceEquals(_hoveredNode, found))
            {
                _hoveredNode = found;
                InvalidateVisual();
            }
        }

        #endregion

        #region Public View Helpers

        /// <summary>
        /// Centers and scales the viewport to fit all nodes with a margin.
        /// </summary>
        public void FitToView(double padding = 60.0)
        {
            if (State == null || State.NodeCount == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;

            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            foreach (var node in State.Nodes.Values)
            {
                minX = Math.Min(minX, node.Position.X - node.Radius);
                maxX = Math.Max(maxX, node.Position.X + node.Radius);
                minY = Math.Min(minY, node.Position.Y - node.Radius);
                maxY = Math.Max(maxY, node.Position.Y + node.Radius);
            }

            double graphW = Math.Max(1.0, maxX - minX);
            double graphH = Math.Max(1.0, maxY - minY);

            double availableW = Math.Max(10.0, ActualWidth - padding * 2.0);
            double availableH = Math.Max(10.0, ActualHeight - padding * 2.0);

            double scaleX = availableW / graphW;
            double scaleY = availableH / graphH;
            double targetZoom = Math.Clamp(Math.Min(scaleX, scaleY), MinZoom, MaxZoom);

            Point graphCenter = new((minX + maxX) / 2.0, (minY + maxY) / 2.0);
            Point screenCenter = new(ActualWidth / 2.0, ActualHeight / 2.0);

            _zoom = targetZoom;
            _pan = new Vector(screenCenter.X - graphCenter.X * _zoom, screenCenter.Y - graphCenter.Y * _zoom);

            InvalidateVisual();
        }

        /// <summary>
        /// Resets the zoom to 1.0 and pan to center.
        /// </summary>
        public void ResetView()
        {
            _zoom = 1.0;
            _pan = new Vector(0, 0);
            InvalidateVisual();
        }

        #endregion
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MusicVisualizer.Visualization
{
    /// <summary>
    /// Displays a vertical bar spectrum visualizer with configurable styling, motion tuning, layout tuning, and color system.
    /// </summary>
    public partial class SpectrumVisualizerControl : UserControl
    {
        private const int BarCount = 64;
        private const double DefaultBarSpacing = 4.0;
        private const double DefaultMinBarHeight = 2.0;

        private readonly Rectangle[] _bars = new Rectangle[BarCount];
        private readonly float[] _smoothedValues = new float[BarCount];
        private readonly float[] _peakValues = new float[BarCount];

        private bool _isBuilt;

        // cached brushes to avoid reallocation every frame
        private SolidColorBrush _primaryBrush = new SolidColorBrush(Colors.MediumAquamarine);
        private SolidColorBrush _secondaryBrush = new SolidColorBrush(Colors.DodgerBlue);
        private SolidColorBrush _accentBrush = new SolidColorBrush(Colors.HotPink);

        public SpectrumVisualizerControl()
        {
            InitializeComponent();

            Attack = 0.60f;
            Decay = 0.08f;
            PeakFallSpeed = 0.01f;
            BarSpacing = DefaultBarSpacing;
            MinBarHeight = DefaultMinBarHeight;
            VisualStyle = VisualizerStyle.Solid;

            Loaded += OnLoaded;
            SizeChanged += OnSizeChanged;
        }

        // -----------------------------
        // Public properties (controlled by MainWindow)
        // -----------------------------

        public float Attack { get; set; }
        public float Decay { get; set; }
        public float PeakFallSpeed { get; set; }

        public double BarSpacing { get; set; }
        public double MinBarHeight { get; set; }

        public VisualizerStyle VisualStyle { get; set; }

        public Color PrimaryColor
        {
            set => _primaryBrush = new SolidColorBrush(value);
        }

        public Color SecondaryColor
        {
            set => _secondaryBrush = new SolidColorBrush(value);
        }

        public Color AccentColor
        {
            set => _accentBrush = new SolidColorBrush(value);
        }

        // -----------------------------
        // Rendering
        // -----------------------------

        public void UpdateBars(float[] values)
        {
            if (!_isBuilt || values == null || values.Length == 0)
                return;

            double canvasWidth = BarsCanvas.ActualWidth;
            double canvasHeight = BarsCanvas.ActualHeight;

            if (canvasWidth <= 0 || canvasHeight <= 0)
                return;

            double spacing = Math.Max(0.0, BarSpacing);
            double minHeight = Math.Max(0.0, MinBarHeight);

            double totalSpacing = spacing * (BarCount - 1);
            double availableWidth = canvasWidth - totalSpacing;
            double barWidth = availableWidth / BarCount;

            if (barWidth <= 0)
                return;

            float attack = Clamp(Attack, 0f, 1f);
            float decay = Clamp(Decay, 0f, 1f);
            float peakFall = Math.Max(0f, PeakFallSpeed);

            for (int i = 0; i < BarCount; i++)
            {
                float target = i < values.Length ? Clamp(values[i], 0f, 1f) : 0f;
                float current = _smoothedValues[i];

                if (target > current)
                    current += (target - current) * attack;
                else
                    current -= (current - target) * decay;

                current = Clamp(current, 0f, 1f);
                _smoothedValues[i] = current;

                float peak = _peakValues[i];

                if (current > peak)
                    peak = current;
                else
                    peak -= peakFall;

                _peakValues[i] = Math.Max(0f, peak);

                double height = Math.Max(minHeight, current * canvasHeight);
                double x = i * (barWidth + spacing);
                double y = canvasHeight - height;

                Rectangle bar = _bars[i];
                bar.Width = barWidth;
                bar.Height = height;
                bar.Fill = GetBrush(i);

                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, y);
            }
        }

        // -----------------------------
        // Style Logic
        // -----------------------------

        private Brush GetBrush(int index)
        {
            if (VisualStyle == VisualizerStyle.Solid)
                return _primaryBrush;

            if (VisualStyle == VisualizerStyle.Gradient)
                return GetGradientBrush();

            if (VisualStyle == VisualizerStyle.Custom)
                return GetCustomBrush(index);

            if (VisualStyle == VisualizerStyle.Rainbow)
                return new SolidColorBrush(GetRainbowColor(index));

            return _primaryBrush;
        }

        private Brush GetGradientBrush()
        {
            return new LinearGradientBrush(
                _primaryBrush.Color,
                _secondaryBrush.Color,
                new Point(0.5, 1.0),
                new Point(0.5, 0.0));
        }

        private Brush GetCustomBrush(int index)
        {
            int mod = index % 3;

            if (mod == 0) return _primaryBrush;
            if (mod == 1) return _secondaryBrush;
            return _accentBrush;
        }

        // -----------------------------
        // Setup
        // -----------------------------

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BuildBars();
            UpdateBars(new float[BarCount]);
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_isBuilt)
                return;

            UpdateBars(_smoothedValues);
        }

        private void BuildBars()
        {
            if (_isBuilt)
                return;

            BarsCanvas.Children.Clear();

            for (int i = 0; i < BarCount; i++)
            {
                Rectangle bar = new Rectangle
                {
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = _primaryBrush
                };

                _bars[i] = bar;
                BarsCanvas.Children.Add(bar);
            }

            _isBuilt = true;
        }

        // -----------------------------
        // Helpers
        // -----------------------------

        private static Color GetRainbowColor(int index)
        {
            double hue = (360.0 / BarCount) * index;
            return ColorFromHsv(hue, 0.85, 0.95);
        }

        private static Color ColorFromHsv(double hue, double saturation, double value)
        {
            double c = value * saturation;
            double x = c * (1.0 - Math.Abs(((hue / 60.0) % 2.0) - 1.0));
            double m = value - c;

            double rPrime, gPrime, bPrime;

            if (hue < 60) { rPrime = c; gPrime = x; bPrime = 0; }
            else if (hue < 120) { rPrime = x; gPrime = c; bPrime = 0; }
            else if (hue < 180) { rPrime = 0; gPrime = c; bPrime = x; }
            else if (hue < 240) { rPrime = 0; gPrime = x; bPrime = c; }
            else if (hue < 300) { rPrime = x; gPrime = 0; bPrime = c; }
            else { rPrime = c; gPrime = 0; bPrime = x; }

            byte r = (byte)Math.Round((rPrime + m) * 255);
            byte g = (byte)Math.Round((gPrime + m) * 255);
            byte b = (byte)Math.Round((bPrime + m) * 255);

            return Color.FromRgb(r, g, b);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
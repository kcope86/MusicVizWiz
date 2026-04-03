using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MusicVisualizer.Audio;
using MusicVisualizer.Processing;
using MusicVisualizer.Visualization;
using NAudio.CoreAudioApi;

namespace MusicVisualizer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml.
    /// </summary>
    public partial class MainWindow : Window
    {
        // MVCS_001_CONSTANTS_DEVICE_AND_PIPELINE
        private const string SpotifyWaveLinkDeviceId = "{0.0.0.00000000}.{f1af2cd9-1f6b-4394-a064-40cf2058bdb1}";
        private const int FftSize = 2048;
        private const int BarCount = 64;
        private const int BpmAnalysisWindowSeconds = 3;

        // MVCS_002_CONSTANTS_PRESET_NAMES
        private const string PresetBalanced = "Balanced";
        private const string PresetPunchy = "Punchy";
        private const string PresetClean = "Clean";
        private const string PresetCustom = "Custom";

        // MVCS_002A_CONSTANTS_DEFAULT_VALUES
        private const string DefaultStyleName = "Solid";
        private const double DefaultAttack = 0.60;
        private const double DefaultDecay = 0.08;
        private const double DefaultPeakFallSpeed = 0.010;
        private const double DefaultBarSpacing = 4.0;
        private const double DefaultMinBarHeight = 2.0;
        private const double DefaultGain = 1.00;
        private const double DefaultThreshold = 0.020;
        private const string DefaultPrimaryColorHex = "#6EE7B7";
        private const string DefaultSecondaryColorHex = "#60A5FA";
        private const string DefaultAccentColorHex = "#F472B6";

        // MVCS_003_FIELDS_SERVICES_AND_TIMER
        private readonly AudioDeviceService _audioDeviceService = new();
        private readonly LoopbackCaptureService _loopbackCaptureService = new();
        private readonly DispatcherTimer _uiTimer = new();
        private readonly FftProcessor _fftProcessor = new(FftSize);
        private readonly BpmAnalyzer _bpmAnalyzer = new();

        // MVCS_004_FIELDS_RUNTIME_STATE
        private SpectrumMapper? _spectrumMapper;
        private bool _isApplyingPreset;
        private bool _isUpdatingPresetSelection;
        private int _sampleRate;

        // MVCS_005_CONSTRUCTOR
        public MainWindow()
        {
            InitializeComponent();

            Loaded += OnWindowLoaded;

            _loopbackCaptureService.LevelCalculated += OnLevelCalculated;
            _loopbackCaptureService.StatusChanged += OnStatusChanged;

            _uiTimer.Interval = TimeSpan.FromMilliseconds(16);
            _uiTimer.Tick += OnUiTimerTick;
            _uiTimer.Start();

            ApplyVisualizerSettingsFromControls();
            ShowSelectedDevice();
            StartLoopbackCapture();
        }

        // MVCS_006_WINDOW_LIFECYCLE_ON_CLOSED
        protected override void OnClosed(EventArgs e)
        {
            Loaded -= OnWindowLoaded;

            _uiTimer.Stop();
            _uiTimer.Tick -= OnUiTimerTick;

            _loopbackCaptureService.LevelCalculated -= OnLevelCalculated;
            _loopbackCaptureService.StatusChanged -= OnStatusChanged;
            _loopbackCaptureService.Dispose();

            base.OnClosed(e);
        }

        // MVCS_007_WINDOW_LOADED_INITIALIZATION
        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            RefreshAllColorPreviews();
            ApplyVisualizerSettingsFromControls();
            UpdateSpectrumValueTextBlocks();
            UpdatePresetSelectionFromCurrentValues();

            if (BpmValueTextBlock != null)
            {
                BpmValueTextBlock.Text = "---";
            }

            if (BpmStatusTextBlock != null)
            {
                BpmStatusTextBlock.Text = "Analyzing...";
            }
        }

        // MVCS_008_CAPTURE_AND_FFT_SECTION
        // -----------------------------
        // Capture / FFT
        // -----------------------------

        // MVCS_009_CAPTURE_SHOW_SELECTED_DEVICE
        private void ShowSelectedDevice()
        {
            AudioDeviceInfo? device = _audioDeviceService.GetRenderDeviceById(SpotifyWaveLinkDeviceId);

            if (device is null)
            {
                SelectedDeviceTextBlock.Text =
                    "Configured device was not found. Make sure the Spotify (Elgato Virtual Audio) endpoint is active.";
                return;
            }

            SelectedDeviceTextBlock.Text =
                $"Name: {device.Name}{Environment.NewLine}" +
                $"ID: {device.Id}{Environment.NewLine}" +
                $"State: {device.State}";
        }

        // MVCS_010_CAPTURE_START_LOOPBACK
        private void StartLoopbackCapture()
        {
            try
            {
                MMDevice device = _audioDeviceService.GetMmDeviceById(SpotifyWaveLinkDeviceId);
                _sampleRate = device.AudioClient.MixFormat.SampleRate;

                _spectrumMapper = new SpectrumMapper(
                    BarCount,
                    _sampleRate,
                    FftSize);

                _loopbackCaptureService.Start(device);
            }
            catch (Exception ex)
            {
                CaptureStatusTextBlock.Text = $"Failed to start capture: {ex.Message}";
            }
        }

        // MVCS_011_CAPTURE_LEVEL_EVENT_HANDLER
        private void OnLevelCalculated(object? sender, AudioCaptureLevelEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                PeakValueTextBlock.Text = $"Peak: {e.PeakLevel:F4}";
            });
        }

        // MVCS_012_CAPTURE_STATUS_EVENT_HANDLER
        private void OnStatusChanged(object? sender, string status)
        {
            Dispatcher.Invoke(() =>
            {
                CaptureStatusTextBlock.Text = status;
            });
        }

        // MVCS_013_UI_TIMER_TICK_PIPELINE
        private void OnUiTimerTick(object? sender, EventArgs e)
        {
            BufferedSamplesTextBlock.Text =
                $"Buffered Samples: {_loopbackCaptureService.SampleBuffer.Count:N0}";

            if (_sampleRate <= 0)
            {
                if (BpmStatusTextBlock != null)
                {
                    BpmStatusTextBlock.Text = "No sample rate";
                }

                return;
            }

            int bpmSampleCount = _sampleRate * BpmAnalysisWindowSeconds;
            float[] analysisSamples = _loopbackCaptureService.SampleBuffer.GetLatestSamples(bpmSampleCount);

            if (analysisSamples.Length < _sampleRate)
            {
                if (BpmValueTextBlock != null)
                {
                    BpmValueTextBlock.Text = "---";
                }

                if (BpmStatusTextBlock != null)
                {
                    BpmStatusTextBlock.Text = "Buffering...";
                }

                return;
            }

            var (bpm, confident) = _bpmAnalyzer.Analyze(analysisSamples, _sampleRate);

            if (BpmValueTextBlock != null)
            {
                BpmValueTextBlock.Text = confident ? ((int)Math.Round(bpm)).ToString() : "---";
            }

            if (BpmStatusTextBlock != null)
            {
                BpmStatusTextBlock.Text = confident ? "Locked" : "Analyzing...";
            }

            float[] fftSamples = analysisSamples.Length >= FftSize
                ? analysisSamples.Skip(analysisSamples.Length - FftSize).ToArray()
                : analysisSamples;

            if (fftSamples.Length < FftSize)
            {
                return;
            }

            float[] magnitudes = _fftProcessor.ComputeMagnitude(fftSamples);

            if (magnitudes.Length == 0 || _spectrumMapper is null)
            {
                return;
            }

            float energy = magnitudes.Average();
            FftEnergyTextBlock.Text = $"FFT Energy: {energy:F4}";

            float[] bars = _spectrumMapper.MapToBars(magnitudes);
            ApplySpectrumTuning(bars);

            SpectrumVisualizer.UpdateBars(bars);
        }

        // MVCS_014_SPECTRUM_TUNING_APPLICATION
        private void ApplySpectrumTuning(float[] bars)
        {
            float gain = (float)GainSlider.Value;
            float threshold = (float)ThresholdSlider.Value;

            for (int i = 0; i < bars.Length; i++)
            {
                float value = bars[i];

                value *= gain;

                if (value > 1f)
                {
                    value = 1f;
                }

                if (value < threshold)
                {
                    value = 0f;
                }

                bars[i] = value;
            }
        }

        // MVCS_015_UI_EVENT_HANDLERS_SECTION
        // -----------------------------
        // UI EVENTS
        // -----------------------------

        // MVCS_016_EVENT_PRESET_COMBOBOX_SELECTION_CHANGED
        private void PresetComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingPresetSelection || !IsInitialized)
            {
                return;
            }

            string presetName = GetSelectedPresetName();

            if (string.Equals(presetName, PresetCustom, StringComparison.OrdinalIgnoreCase))
            {
                UpdatePresetStatusText(PresetCustom);
                return;
            }

            ApplyPreset(presetName);
        }

        // MVCS_016A_EVENT_RESET_TO_DEFAULTS_BUTTON_CLICK
        private void ResetToDefaultsButton_OnClick(object sender, RoutedEventArgs e)
        {
            ResetToDefaults();
        }

        // MVCS_017_EVENT_GAIN_SLIDER_CHANGED
        private void GainSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (GainValueTextBlock != null)
            {
                GainValueTextBlock.Text = GainSlider.Value.ToString("F2");
            }

            UpdatePresetSelectionForManualChange();
        }

        // MVCS_018_EVENT_THRESHOLD_SLIDER_CHANGED
        private void ThresholdSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ThresholdValueTextBlock != null)
            {
                ThresholdValueTextBlock.Text = ThresholdSlider.Value.ToString("F3");
            }

            UpdatePresetSelectionForManualChange();
        }

        // MVCS_019_EVENT_STYLE_COMBOBOX_SELECTION_CHANGED
        private void StyleComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyVisualizerSettingsFromControls();
        }

        // MVCS_020_EVENT_ATTACK_SLIDER_CHANGED
        private void AttackSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ApplyVisualizerSettingsFromControls();
            UpdatePresetSelectionForManualChange();
        }

        // MVCS_021_EVENT_DECAY_SLIDER_CHANGED
        private void DecaySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ApplyVisualizerSettingsFromControls();
            UpdatePresetSelectionForManualChange();
        }

        // MVCS_022_EVENT_PEAK_FALL_SPEED_SLIDER_CHANGED
        private void PeakFallSpeedSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ApplyVisualizerSettingsFromControls();
            UpdatePresetSelectionForManualChange();
        }

        // MVCS_023_EVENT_BAR_SPACING_SLIDER_CHANGED
        private void BarSpacingSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ApplyVisualizerSettingsFromControls();
        }

        // MVCS_024_EVENT_MIN_BAR_HEIGHT_SLIDER_CHANGED
        private void MinBarHeightSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ApplyVisualizerSettingsFromControls();
        }

        // MVCS_025_EVENT_PRIMARY_COLOR_TEXT_CHANGED
        private void PrimaryColorTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateColorPreviewSafe(PrimaryColorTextBox, PrimaryColorPreview);
            ApplyVisualizerSettingsFromControls();
        }

        // MVCS_026_EVENT_SECONDARY_COLOR_TEXT_CHANGED
        private void SecondaryColorTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateColorPreviewSafe(SecondaryColorTextBox, SecondaryColorPreview);
            ApplyVisualizerSettingsFromControls();
        }

        // MVCS_027_EVENT_ACCENT_COLOR_TEXT_CHANGED
        private void AccentColorTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateColorPreviewSafe(AccentColorTextBox, AccentColorPreview);
            ApplyVisualizerSettingsFromControls();
        }

        // MVCS_028_SETTINGS_APPLICATION_SECTION
        // -----------------------------
        // SETTINGS APPLICATION
        // -----------------------------

        // MVCS_029_APPLY_VISUALIZER_SETTINGS_FROM_CONTROLS
        private void ApplyVisualizerSettingsFromControls()
        {
            if (!IsInitialized)
            {
                return;
            }

            if (AttackValueTextBlock != null)
            {
                AttackValueTextBlock.Text = AttackSlider.Value.ToString("F2");
            }

            if (DecayValueTextBlock != null)
            {
                DecayValueTextBlock.Text = DecaySlider.Value.ToString("F2");
            }

            if (PeakFallSpeedValueTextBlock != null)
            {
                PeakFallSpeedValueTextBlock.Text = PeakFallSpeedSlider.Value.ToString("F3");
            }

            if (BarSpacingValueTextBlock != null)
            {
                BarSpacingValueTextBlock.Text = BarSpacingSlider.Value.ToString("F1");
            }

            if (MinBarHeightValueTextBlock != null)
            {
                MinBarHeightValueTextBlock.Text = MinBarHeightSlider.Value.ToString("F1");
            }

            SpectrumVisualizer.Attack = (float)AttackSlider.Value;
            SpectrumVisualizer.Decay = (float)DecaySlider.Value;
            SpectrumVisualizer.PeakFallSpeed = (float)PeakFallSpeedSlider.Value;
            SpectrumVisualizer.BarSpacing = BarSpacingSlider.Value;
            SpectrumVisualizer.MinBarHeight = MinBarHeightSlider.Value;
            SpectrumVisualizer.VisualStyle = GetSelectedVisualizerStyle();

            SpectrumVisualizer.PrimaryColor = ParseColorOrDefault(PrimaryColorTextBox?.Text, Colors.MediumAquamarine);
            SpectrumVisualizer.SecondaryColor = ParseColorOrDefault(SecondaryColorTextBox?.Text, Colors.DodgerBlue);
            SpectrumVisualizer.AccentColor = ParseColorOrDefault(AccentColorTextBox?.Text, Colors.HotPink);
        }

        // MVCS_030_GET_SELECTED_VISUALIZER_STYLE
        private VisualizerStyle GetSelectedVisualizerStyle()
        {
            ComboBoxItem? selectedItem = StyleComboBox.SelectedItem as ComboBoxItem;

            if (selectedItem == null || selectedItem.Content == null)
            {
                return VisualizerStyle.Solid;
            }

            string text = selectedItem.Content.ToString() ?? string.Empty;

            if (string.Equals(text, "Gradient", StringComparison.OrdinalIgnoreCase))
            {
                return VisualizerStyle.Gradient;
            }

            if (string.Equals(text, "Rainbow", StringComparison.OrdinalIgnoreCase))
            {
                return VisualizerStyle.Rainbow;
            }

            if (string.Equals(text, "Custom", StringComparison.OrdinalIgnoreCase))
            {
                return VisualizerStyle.Custom;
            }

            return VisualizerStyle.Solid;
        }

        // MVCS_031_PRESETS_SECTION
        // -----------------------------
        // PRESETS
        // -----------------------------

        // MVCS_032_APPLY_PRESET
        private void ApplyPreset(string presetName)
        {
            _isApplyingPreset = true;

            try
            {
                switch (presetName)
                {
                    case PresetPunchy:
                        AttackSlider.Value = 0.85;
                        DecaySlider.Value = 0.14;
                        PeakFallSpeedSlider.Value = 0.018;
                        GainSlider.Value = 1.35;
                        ThresholdSlider.Value = 0.015;
                        break;

                    case PresetClean:
                        AttackSlider.Value = 0.45;
                        DecaySlider.Value = 0.05;
                        PeakFallSpeedSlider.Value = 0.008;
                        GainSlider.Value = 0.90;
                        ThresholdSlider.Value = 0.035;
                        break;

                    case PresetBalanced:
                    default:
                        AttackSlider.Value = DefaultAttack;
                        DecaySlider.Value = DefaultDecay;
                        PeakFallSpeedSlider.Value = DefaultPeakFallSpeed;
                        GainSlider.Value = DefaultGain;
                        ThresholdSlider.Value = DefaultThreshold;
                        break;
                }
            }
            finally
            {
                _isApplyingPreset = false;
            }

            ApplyVisualizerSettingsFromControls();
            UpdateSpectrumValueTextBlocks();
            SetPresetSelection(presetName);
            UpdatePresetStatusText(presetName);
        }

        // MVCS_032A_RESET_TO_DEFAULTS
        private void ResetToDefaults()
        {
            _isApplyingPreset = true;

            try
            {
                SetStyleSelection(DefaultStyleName);

                AttackSlider.Value = DefaultAttack;
                DecaySlider.Value = DefaultDecay;
                PeakFallSpeedSlider.Value = DefaultPeakFallSpeed;

                BarSpacingSlider.Value = DefaultBarSpacing;
                MinBarHeightSlider.Value = DefaultMinBarHeight;

                GainSlider.Value = DefaultGain;
                ThresholdSlider.Value = DefaultThreshold;

                PrimaryColorTextBox.Text = DefaultPrimaryColorHex;
                SecondaryColorTextBox.Text = DefaultSecondaryColorHex;
                AccentColorTextBox.Text = DefaultAccentColorHex;
            }
            finally
            {
                _isApplyingPreset = false;
            }

            RefreshAllColorPreviews();
            ApplyVisualizerSettingsFromControls();
            UpdateSpectrumValueTextBlocks();
            SetPresetSelection(PresetBalanced);
            UpdatePresetStatusText(PresetBalanced);
        }

        // MVCS_033_PRESET_UPDATE_FOR_MANUAL_CHANGE
        private void UpdatePresetSelectionForManualChange()
        {
            if (!IsInitialized || _isApplyingPreset || _isUpdatingPresetSelection)
            {
                return;
            }

            UpdatePresetSelectionFromCurrentValues();
        }

        // MVCS_034_PRESET_UPDATE_FROM_CURRENT_VALUES
        private void UpdatePresetSelectionFromCurrentValues()
        {
            string presetName = GetMatchingPresetNameForCurrentValues();
            SetPresetSelection(presetName);
            UpdatePresetStatusText(presetName);
        }

        // MVCS_035_PRESET_GET_MATCHING_NAME_FOR_CURRENT_VALUES
        private string GetMatchingPresetNameForCurrentValues()
        {
            if (MatchesPreset(DefaultAttack, DefaultDecay, DefaultPeakFallSpeed, DefaultGain, DefaultThreshold))
            {
                return PresetBalanced;
            }

            if (MatchesPreset(0.85, 0.14, 0.018, 1.35, 0.015))
            {
                return PresetPunchy;
            }

            if (MatchesPreset(0.45, 0.05, 0.008, 0.90, 0.035))
            {
                return PresetClean;
            }

            return PresetCustom;
        }

        // MVCS_036_PRESET_MATCHES_PRESET
        private bool MatchesPreset(
            double attack,
            double decay,
            double peakFallSpeed,
            double gain,
            double threshold)
        {
            return AreClose(AttackSlider.Value, attack)
                   && AreClose(DecaySlider.Value, decay)
                   && AreClose(PeakFallSpeedSlider.Value, peakFallSpeed)
                   && AreClose(GainSlider.Value, gain)
                   && AreClose(ThresholdSlider.Value, threshold);
        }

        // MVCS_037_PRESET_ARE_CLOSE_HELPER
        private static bool AreClose(double left, double right)
        {
            return Math.Abs(left - right) < 0.0001;
        }

        // MVCS_038_PRESET_SET_SELECTION
        private void SetPresetSelection(string presetName)
        {
            if (PresetComboBox == null)
            {
                return;
            }

            _isUpdatingPresetSelection = true;

            try
            {
                foreach (ComboBoxItem item in PresetComboBox.Items.OfType<ComboBoxItem>())
                {
                    string itemText = item.Content?.ToString() ?? string.Empty;

                    if (string.Equals(itemText, presetName, StringComparison.OrdinalIgnoreCase))
                    {
                        PresetComboBox.SelectedItem = item;
                        return;
                    }
                }

                PresetComboBox.SelectedIndex = 0;
            }
            finally
            {
                _isUpdatingPresetSelection = false;
            }
        }

        // MVCS_038A_STYLE_SET_SELECTION
        private void SetStyleSelection(string styleName)
        {
            if (StyleComboBox == null)
            {
                return;
            }

            foreach (ComboBoxItem item in StyleComboBox.Items.OfType<ComboBoxItem>())
            {
                string itemText = item.Content?.ToString() ?? string.Empty;

                if (string.Equals(itemText, styleName, StringComparison.OrdinalIgnoreCase))
                {
                    StyleComboBox.SelectedItem = item;
                    return;
                }
            }

            StyleComboBox.SelectedIndex = 0;
        }

        // MVCS_039_PRESET_GET_SELECTED_NAME
        private string GetSelectedPresetName()
        {
            ComboBoxItem? selectedItem = PresetComboBox.SelectedItem as ComboBoxItem;
            return selectedItem?.Content?.ToString() ?? PresetBalanced;
        }

        // MVCS_040_PRESET_UPDATE_STATUS_TEXT
        private void UpdatePresetStatusText(string presetName)
        {
            if (PresetStatusTextBlock == null)
            {
                return;
            }

            switch (presetName)
            {
                case PresetPunchy:
                    PresetStatusTextBlock.Text =
                        "Punchy increases response speed and output energy for stronger, more aggressive motion.";
                    break;

                case PresetClean:
                    PresetStatusTextBlock.Text =
                        "Clean keeps motion tighter and raises the noise floor threshold for a more controlled display.";
                    break;

                case PresetCustom:
                    PresetStatusTextBlock.Text =
                        "Custom indicates the current tuning no longer matches one of the predefined presets.";
                    break;

                case PresetBalanced:
                default:
                    PresetStatusTextBlock.Text =
                        "Balanced uses the current default tuning profile.";
                    break;
            }
        }

        // MVCS_041_SPECTRUM_VALUE_TEXT_UPDATES
        private void UpdateSpectrumValueTextBlocks()
        {
            if (GainValueTextBlock != null)
            {
                GainValueTextBlock.Text = GainSlider.Value.ToString("F2");
            }

            if (ThresholdValueTextBlock != null)
            {
                ThresholdValueTextBlock.Text = ThresholdSlider.Value.ToString("F3");
            }
        }

        // MVCS_042_COLOR_HELPERS_SECTION
        // -----------------------------
        // COLOR HELPERS
        // -----------------------------

        // MVCS_043_COLOR_REFRESH_ALL_PREVIEWS
        private void RefreshAllColorPreviews()
        {
            UpdateColorPreviewSafe(PrimaryColorTextBox, PrimaryColorPreview);
            UpdateColorPreviewSafe(SecondaryColorTextBox, SecondaryColorPreview);
            UpdateColorPreviewSafe(AccentColorTextBox, AccentColorPreview);
        }

        // MVCS_044_COLOR_UPDATE_PREVIEW_SAFE
        private void UpdateColorPreviewSafe(TextBox? textBox, Border? preview)
        {
            if (textBox == null || preview == null)
            {
                return;
            }

            if (TryParseColor(textBox.Text, out Color color))
            {
                preview.Background = new SolidColorBrush(color);

                if (ColorStatusTextBlock != null)
                {
                    ColorStatusTextBlock.Text =
                        "Solid uses Primary. Gradient uses Primary → Secondary. Custom cycles Primary / Secondary / Accent.";
                }
            }
            else
            {
                if (ColorStatusTextBlock != null)
                {
                    ColorStatusTextBlock.Text = "Invalid hex color format.";
                }
            }
        }

        // MVCS_045_COLOR_PARSE_OR_DEFAULT
        private static Color ParseColorOrDefault(string? hex, Color fallback)
        {
            return TryParseColor(hex, out Color color) ? color : fallback;
        }

        // MVCS_046_COLOR_TRY_PARSE
        private static bool TryParseColor(string? hex, out Color color)
        {
            color = Colors.Transparent;

            if (string.IsNullOrWhiteSpace(hex))
            {
                return false;
            }

            hex = hex.Trim();

            if (!hex.StartsWith("#", StringComparison.Ordinal))
            {
                return false;
            }

            string value = hex.Substring(1);

            try
            {
                if (value.Length == 6)
                {
                    byte r = byte.Parse(value.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    byte g = byte.Parse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    byte b = byte.Parse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

                    color = Color.FromRgb(r, g, b);
                    return true;
                }

                if (value.Length == 8)
                {
                    byte a = byte.Parse(value.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    byte r = byte.Parse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    byte g = byte.Parse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    byte b = byte.Parse(value.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

                    color = Color.FromArgb(a, r, g, b);
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
    }
}
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        private const string SpotifyWaveLinkDeviceId = "{0.0.0.00000000}.{f1af2cd9-1f6b-4394-a064-40cf2058bdb1}";
        private const int FftSize = 2048;
        private const int BarCount = 64;
        private const int BpmAnalysisWindowSeconds = 3;

        private const string PresetBalanced = "Balanced";
        private const string PresetPunchy = "Punchy";
        private const string PresetClean = "Clean";
        private const string PresetCustom = "Custom";

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

        private readonly AudioDeviceService _audioDeviceService = new();
        private readonly LoopbackCaptureService _loopbackCaptureService = new();
        private readonly DispatcherTimer _uiTimer = new();
        private readonly FftProcessor _fftProcessor = new(FftSize);
        private readonly BpmAnalyzer _bpmAnalyzer = new();

        private SpectrumMapper? _spectrumMapper;
        private bool _isApplyingPreset;
        private bool _isUpdatingPresetSelection;
        private bool _isRefreshingDeviceSelection;
        private int _sampleRate;
        private string? _selectedDeviceId;

        public MainWindow()
        {
            InitializeComponent();

            Loaded += OnWindowLoaded;

            _loopbackCaptureService.LevelCalculated += OnLevelCalculated;
            _loopbackCaptureService.StatusChanged += OnStatusChanged;

            AudioPanel.AddHandler(
                Selector.SelectionChangedEvent,
                new SelectionChangedEventHandler(DeviceComboBox_OnSelectionChanged));

            BarsSettingsPanel.AddHandler(
                Selector.SelectionChangedEvent,
                new SelectionChangedEventHandler(BarsSettingsPanel_OnSelectionChanged));

            BarsSettingsPanel.AddHandler(
                RangeBase.ValueChangedEvent,
                new RoutedPropertyChangedEventHandler<double>(BarsSettingsPanel_OnSliderValueChanged));

            BarsSettingsPanel.AddHandler(
                TextBox.TextChangedEvent,
                new TextChangedEventHandler(BarsSettingsPanel_OnTextChanged));

            BarsSettingsPanel.AddHandler(
                Button.ClickEvent,
                new RoutedEventHandler(BarsSettingsPanel_OnButtonClick));

            _uiTimer.Interval = TimeSpan.FromMilliseconds(16);
            _uiTimer.Tick += OnUiTimerTick;
            _uiTimer.Start();

            ApplyVisualizerSettingsFromControls();
            PopulateDeviceComboBox();
            ShowSelectedDevice();
            StartLoopbackCapture();
        }

        private SpectrumVisualizerControl SpectrumVisualizer => VisualizerHost.SpectrumVisualizerControl;

        private ComboBox DeviceComboBox => AudioPanel.DeviceSelector;
        private TextBlock CaptureStatusTextBlock => AudioPanel.CaptureStatus;
        private TextBlock PeakValueTextBlock => AudioPanel.PeakValue;
        private TextBlock BufferedSamplesTextBlock => AudioPanel.BufferedSamples;
        private TextBlock FftEnergyTextBlock => AudioPanel.FftEnergy;
        private TextBlock SelectedDeviceTextBlock => AudioPanel.SelectedDevice;

        private ComboBox PresetComboBox => BarsSettingsPanel.PresetSelector;
        private TextBlock PresetStatusTextBlock => BarsSettingsPanel.PresetStatus;
        private Button ResetToDefaultsButton => BarsSettingsPanel.ResetDefaultsButton;

        private ComboBox StyleComboBox => BarsSettingsPanel.StyleSelector;

        private Slider AttackSlider => BarsSettingsPanel.Attack;
        private TextBlock AttackValueTextBlock => BarsSettingsPanel.AttackValue;

        private Slider DecaySlider => BarsSettingsPanel.Decay;
        private TextBlock DecayValueTextBlock => BarsSettingsPanel.DecayValue;

        private Slider PeakFallSpeedSlider => BarsSettingsPanel.PeakFallSpeed;
        private TextBlock PeakFallSpeedValueTextBlock => BarsSettingsPanel.PeakFallSpeedValue;

        private Slider BarSpacingSlider => BarsSettingsPanel.BarSpacing;
        private TextBlock BarSpacingValueTextBlock => BarsSettingsPanel.BarSpacingValue;

        private Slider MinBarHeightSlider => BarsSettingsPanel.MinBarHeight;
        private TextBlock MinBarHeightValueTextBlock => BarsSettingsPanel.MinBarHeightValue;

        private TextBox PrimaryColorTextBox => BarsSettingsPanel.PrimaryColor;
        private Border PrimaryColorPreview => BarsSettingsPanel.PrimaryColorSwatch;

        private TextBox SecondaryColorTextBox => BarsSettingsPanel.SecondaryColor;
        private Border SecondaryColorPreview => BarsSettingsPanel.SecondaryColorSwatch;

        private TextBox AccentColorTextBox => BarsSettingsPanel.AccentColor;
        private Border AccentColorPreview => BarsSettingsPanel.AccentColorSwatch;

        private TextBlock ColorStatusTextBlock => BarsSettingsPanel.ColorStatus;

        private Slider GainSlider => BarsSettingsPanel.Gain;
        private TextBlock GainValueTextBlock => BarsSettingsPanel.GainValue;

        private Slider ThresholdSlider => BarsSettingsPanel.Threshold;
        private TextBlock ThresholdValueTextBlock => BarsSettingsPanel.ThresholdValue;

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _uiTimer.Stop();
            _loopbackCaptureService.Stop();
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            Loaded -= OnWindowLoaded;

            _uiTimer.Tick -= OnUiTimerTick;

            _loopbackCaptureService.LevelCalculated -= OnLevelCalculated;
            _loopbackCaptureService.StatusChanged -= OnStatusChanged;
            _loopbackCaptureService.Dispose();

            base.OnClosed(e);
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            RefreshAllColorPreviews();
            ApplyVisualizerSettingsFromControls();
            UpdateSpectrumValueTextBlocks();
            UpdatePresetSelectionFromCurrentValues();

            BpmValueTextBlock.Text = "---";
            BpmStatusTextBlock.Text = "Analyzing...";
        }

        private void PopulateDeviceComboBox()
        {
            var devices = _audioDeviceService.GetRenderDevices().ToList();

            _isRefreshingDeviceSelection = true;

            try
            {
                DeviceComboBox.ItemsSource = devices;
                DeviceComboBox.DisplayMemberPath = nameof(AudioDeviceInfo.Name);
                DeviceComboBox.SelectedValuePath = nameof(AudioDeviceInfo.Id);

                AudioDeviceInfo? selectedDevice = devices.FirstOrDefault(d =>
                    string.Equals(d.Id, SpotifyWaveLinkDeviceId, StringComparison.OrdinalIgnoreCase));

                selectedDevice ??= devices.FirstOrDefault();

                _selectedDeviceId = selectedDevice?.Id;
                DeviceComboBox.SelectedValue = _selectedDeviceId;
            }
            finally
            {
                _isRefreshingDeviceSelection = false;
            }
        }

        private void ShowSelectedDevice()
        {
            if (string.IsNullOrWhiteSpace(_selectedDeviceId))
            {
                SelectedDeviceTextBlock.Text = "No audio render device selected.";
                return;
            }

            AudioDeviceInfo? device = _audioDeviceService.GetRenderDeviceById(_selectedDeviceId);

            if (device is null)
            {
                SelectedDeviceTextBlock.Text =
                    "The selected device is no longer available. Choose another device.";
                return;
            }

            SelectedDeviceTextBlock.Text =
                $"Name: {device.Name}{Environment.NewLine}" +
                $"ID: {device.Id}{Environment.NewLine}" +
                $"State: {device.State}";
        }

        private void StartLoopbackCapture()
        {
            if (string.IsNullOrWhiteSpace(_selectedDeviceId))
            {
                CaptureStatusTextBlock.Text = "Cannot start capture: no device selected.";
                return;
            }

            try
            {
                _loopbackCaptureService.Stop();
                VisualizerHost.UpdateBars(new float[BarCount]);

                MMDevice device = _audioDeviceService.GetMmDeviceById(_selectedDeviceId);
                _sampleRate = device.AudioClient.MixFormat.SampleRate;

                _spectrumMapper = new SpectrumMapper(
                    BarCount,
                    _sampleRate,
                    FftSize);

                _loopbackCaptureService.Start(device);
                ShowSelectedDevice();
            }
            catch (Exception ex)
            {
                _sampleRate = 0;
                _spectrumMapper = null;
                CaptureStatusTextBlock.Text = $"Failed to start capture: {ex.Message}";
            }
        }

        private void StopLoopbackCapture()
        {
            _loopbackCaptureService.Stop();
            _sampleRate = 0;
            _spectrumMapper = null;

            BpmValueTextBlock.Text = "---";
            BpmStatusTextBlock.Text = "Stopped";
            PeakValueTextBlock.Text = "Peak: 0.0000";
            FftEnergyTextBlock.Text = "FFT Energy: 0.0000";
        }

        private void OnLevelCalculated(object? sender, AudioCaptureLevelEventArgs e)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            Dispatcher.BeginInvoke(() =>
            {
                PeakValueTextBlock.Text = $"Peak: {e.PeakLevel:F4}";
            });
        }

        private void OnStatusChanged(object? sender, string status)
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            Dispatcher.BeginInvoke(() =>
            {
                CaptureStatusTextBlock.Text = status;
            });
        }

        private void OnUiTimerTick(object? sender, EventArgs e)
        {
            BufferedSamplesTextBlock.Text =
                $"Buffered Samples: {_loopbackCaptureService.SampleBuffer.Count:N0}";

            if (!_loopbackCaptureService.IsRunning)
            {
                VisualizerHost.UpdateBars(new float[BarCount]);
                return;
            }

            if (_sampleRate <= 0)
            {
                BpmStatusTextBlock.Text = "No sample rate";
                VisualizerHost.UpdateBars(new float[BarCount]);
                return;
            }

            int bpmSampleCount = _sampleRate * BpmAnalysisWindowSeconds;
            float[] analysisSamples = _loopbackCaptureService.SampleBuffer.GetLatestSamples(bpmSampleCount);

            if (analysisSamples.Length < _sampleRate)
            {
                BpmValueTextBlock.Text = "---";
                BpmStatusTextBlock.Text = "Buffering...";
                VisualizerHost.UpdateBars(new float[BarCount]);
                return;
            }

            var (bpm, confident) = _bpmAnalyzer.Analyze(analysisSamples, _sampleRate);

            BpmValueTextBlock.Text = confident ? ((int)Math.Round(bpm)).ToString() : "---";
            BpmStatusTextBlock.Text = confident ? "Locked" : "Analyzing...";

            float[] fftSamples = analysisSamples.Length >= FftSize
                ? analysisSamples.Skip(analysisSamples.Length - FftSize).ToArray()
                : analysisSamples;

            if (fftSamples.Length < FftSize)
            {
                VisualizerHost.UpdateBars(new float[BarCount]);
                return;
            }

            float[] magnitudes = _fftProcessor.ComputeMagnitude(fftSamples);

            if (magnitudes.Length == 0 || _spectrumMapper is null)
            {
                VisualizerHost.UpdateBars(new float[BarCount]);
                return;
            }

            float energy = magnitudes.Average();
            FftEnergyTextBlock.Text = $"FFT Energy: {energy:F4}";

            float[] bars = _spectrumMapper.MapToBars(magnitudes);
            ApplySpectrumTuning(bars);

            VisualizerHost.UpdateBars(bars);
        }

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

        private void DeviceComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, DeviceComboBox))
            {
                return;
            }

            if (_isRefreshingDeviceSelection)
            {
                return;
            }

            string? newlySelectedDeviceId = DeviceComboBox.SelectedValue as string;

            if (string.IsNullOrWhiteSpace(newlySelectedDeviceId))
            {
                return;
            }

            bool changed = !string.Equals(_selectedDeviceId, newlySelectedDeviceId, StringComparison.OrdinalIgnoreCase);
            _selectedDeviceId = newlySelectedDeviceId;

            ShowSelectedDevice();

            if (changed)
            {
                StartLoopbackCapture();
            }
        }

        private void BarsSettingsPanel_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, PresetComboBox))
            {
                PresetComboBox_OnSelectionChanged(e.OriginalSource, e);
                return;
            }

            if (ReferenceEquals(e.OriginalSource, StyleComboBox))
            {
                StyleComboBox_OnSelectionChanged(e.OriginalSource, e);
            }
        }

        private void BarsSettingsPanel_OnSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ReferenceEquals(e.OriginalSource, GainSlider))
            {
                GainSlider_OnValueChanged(e.OriginalSource, e);
            }
            else if (ReferenceEquals(e.OriginalSource, ThresholdSlider))
            {
                ThresholdSlider_OnValueChanged(e.OriginalSource, e);
            }
            else if (ReferenceEquals(e.OriginalSource, AttackSlider))
            {
                AttackSlider_OnValueChanged(e.OriginalSource, e);
            }
            else if (ReferenceEquals(e.OriginalSource, DecaySlider))
            {
                DecaySlider_OnValueChanged(e.OriginalSource, e);
            }
            else if (ReferenceEquals(e.OriginalSource, PeakFallSpeedSlider))
            {
                PeakFallSpeedSlider_OnValueChanged(e.OriginalSource, e);
            }
            else if (ReferenceEquals(e.OriginalSource, BarSpacingSlider))
            {
                BarSpacingSlider_OnValueChanged(e.OriginalSource, e);
            }
            else if (ReferenceEquals(e.OriginalSource, MinBarHeightSlider))
            {
                MinBarHeightSlider_OnValueChanged(e.OriginalSource, e);
            }
        }

        private void BarsSettingsPanel_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, PrimaryColorTextBox))
            {
                PrimaryColorTextBox_OnTextChanged(e.OriginalSource, e);
            }
            else if (ReferenceEquals(e.OriginalSource, SecondaryColorTextBox))
            {
                SecondaryColorTextBox_OnTextChanged(e.OriginalSource, e);
            }
            else if (ReferenceEquals(e.OriginalSource, AccentColorTextBox))
            {
                AccentColorTextBox_OnTextChanged(e.OriginalSource, e);
            }
        }

        private void BarsSettingsPanel_OnButtonClick(object sender, RoutedEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, ResetToDefaultsButton))
            {
                ResetToDefaultsButton_OnClick(e.OriginalSource, e);
            }
        }

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

        private void ResetToDefaultsButton_OnClick(object sender, RoutedEventArgs e)
        {
            ResetToDefaults();
        }

        private void GainSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            GainValueTextBlock.Text = GainSlider.Value.ToString("F2");
            UpdatePresetSelectionForManualChange();
        }

        private void ThresholdSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ThresholdValueTextBlock.Text = ThresholdSlider.Value.ToString("F3");
            UpdatePresetSelectionForManualChange();
        }

        private void StyleComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyVisualizerSettingsFromControls();
        }

        private void AttackSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ApplyVisualizerSettingsFromControls();
            UpdatePresetSelectionForManualChange();
        }

        private void DecaySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ApplyVisualizerSettingsFromControls();
            UpdatePresetSelectionForManualChange();
        }

        private void PeakFallSpeedSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ApplyVisualizerSettingsFromControls();
            UpdatePresetSelectionForManualChange();
        }

        private void BarSpacingSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ApplyVisualizerSettingsFromControls();
        }

        private void MinBarHeightSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ApplyVisualizerSettingsFromControls();
        }

        private void PrimaryColorTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateColorPreviewSafe(PrimaryColorTextBox, PrimaryColorPreview);
            ApplyVisualizerSettingsFromControls();
        }

        private void SecondaryColorTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateColorPreviewSafe(SecondaryColorTextBox, SecondaryColorPreview);
            ApplyVisualizerSettingsFromControls();
        }

        private void AccentColorTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateColorPreviewSafe(AccentColorTextBox, AccentColorPreview);
            ApplyVisualizerSettingsFromControls();
        }

        private void ApplyVisualizerSettingsFromControls()
        {
            if (!IsInitialized)
            {
                return;
            }

            AttackValueTextBlock.Text = AttackSlider.Value.ToString("F2");
            DecayValueTextBlock.Text = DecaySlider.Value.ToString("F2");
            PeakFallSpeedValueTextBlock.Text = PeakFallSpeedSlider.Value.ToString("F3");
            BarSpacingValueTextBlock.Text = BarSpacingSlider.Value.ToString("F1");
            MinBarHeightValueTextBlock.Text = MinBarHeightSlider.Value.ToString("F1");

            SpectrumVisualizer.Attack = (float)AttackSlider.Value;
            SpectrumVisualizer.Decay = (float)DecaySlider.Value;
            SpectrumVisualizer.PeakFallSpeed = (float)PeakFallSpeedSlider.Value;
            SpectrumVisualizer.BarSpacing = BarSpacingSlider.Value;
            SpectrumVisualizer.MinBarHeight = MinBarHeightSlider.Value;
            SpectrumVisualizer.VisualStyle = GetSelectedVisualizerStyle();

            SpectrumVisualizer.PrimaryColor = ParseColorOrDefault(PrimaryColorTextBox.Text, Colors.MediumAquamarine);
            SpectrumVisualizer.SecondaryColor = ParseColorOrDefault(SecondaryColorTextBox.Text, Colors.DodgerBlue);
            SpectrumVisualizer.AccentColor = ParseColorOrDefault(AccentColorTextBox.Text, Colors.HotPink);
        }

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

        private void UpdatePresetSelectionForManualChange()
        {
            if (!IsInitialized || _isApplyingPreset || _isUpdatingPresetSelection)
            {
                return;
            }

            UpdatePresetSelectionFromCurrentValues();
        }

        private void UpdatePresetSelectionFromCurrentValues()
        {
            string presetName = GetMatchingPresetNameForCurrentValues();
            SetPresetSelection(presetName);
            UpdatePresetStatusText(presetName);
        }

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

        private static bool AreClose(double left, double right)
        {
            return Math.Abs(left - right) < 0.0001;
        }

        private void SetPresetSelection(string presetName)
        {
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

        private void SetStyleSelection(string styleName)
        {
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

        private string GetSelectedPresetName()
        {
            ComboBoxItem? selectedItem = PresetComboBox.SelectedItem as ComboBoxItem;
            return selectedItem?.Content?.ToString() ?? PresetBalanced;
        }

        private void UpdatePresetStatusText(string presetName)
        {
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

        private void UpdateSpectrumValueTextBlocks()
        {
            GainValueTextBlock.Text = GainSlider.Value.ToString("F2");
            ThresholdValueTextBlock.Text = ThresholdSlider.Value.ToString("F3");
        }

        private void RefreshAllColorPreviews()
        {
            UpdateColorPreviewSafe(PrimaryColorTextBox, PrimaryColorPreview);
            UpdateColorPreviewSafe(SecondaryColorTextBox, SecondaryColorPreview);
            UpdateColorPreviewSafe(AccentColorTextBox, AccentColorPreview);
        }

        private void UpdateColorPreviewSafe(TextBox? textBox, Border? preview)
        {
            if (textBox == null || preview == null)
            {
                return;
            }

            if (TryParseColor(textBox.Text, out Color color))
            {
                preview.Background = new SolidColorBrush(color);
                ColorStatusTextBlock.Text =
                    "Solid uses Primary. Gradient uses Primary → Secondary. Custom cycles Primary / Secondary / Accent.";
            }
            else
            {
                ColorStatusTextBlock.Text = "Invalid hex color format.";
            }
        }

        private static Color ParseColorOrDefault(string? hex, Color fallback)
        {
            return TryParseColor(hex, out Color color) ? color : fallback;
        }

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
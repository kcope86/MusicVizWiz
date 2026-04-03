# MusicVisualizer

A real-time music visualizer built with **.NET 8**, **WPF**, and **NAudio**.

This project captures Windows system audio using WASAPI loopback, performs FFT analysis, maps the frequency spectrum into display bars, and renders a customizable visualizer in real time.

---

## Features

- WASAPI loopback audio capture
- Real-time FFT processing
- Logarithmic spectrum mapping
- 64-bar spectrum visualizer
- Live customization controls for:
  - visual style
  - motion
  - layout
  - colors
  - spectrum tuning
- Preset system
- Reset to defaults
- BPM display work in progress

---

## Current Architecture

The core pipeline is:

```text
Audio Capture (NAudio Loopback)
        ↓
Sample Buffer
        ↓
FFT Processor
        ↓
SpectrumMapper
        ↓
MainWindow (post-mapping tuning + orchestration)
        ↓
SpectrumVisualizerControl (rendering)


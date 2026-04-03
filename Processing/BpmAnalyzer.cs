using System;
using System.Collections.Generic;
using System.Linq;

namespace MusicVisualizer.Processing
{
    public sealed class BpmAnalyzer
    {
        private readonly Queue<float> _noveltyHistory = new();
        private readonly List<double> _beatIntervalsSeconds = new();

        private float _previousBandEnergy;
        private DateTime _lastBeatTimeUtc = DateTime.MinValue;

        public (double bpm, bool confident) Analyze(float[] samples, int sampleRate)
        {
            if (samples == null || samples.Length == 0 || sampleRate <= 0)
            {
                return (0, false);
            }

            int blockSize = Math.Max(256, sampleRate / 200);
            int blockCount = samples.Length / blockSize;

            if (blockCount < 16)
            {
                return (0, false);
            }

            float strongestNovelty = 0f;
            bool beatDetected = false;

            for (int blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                int start = blockIndex * blockSize;
                int end = start + blockSize;

                float bandEnergy = 0f;

                for (int i = start; i < end; i++)
                {
                    float sample = samples[i];

                    // Simple low-frequency emphasis:
                    // use adjacent-sample smoothing to make slower waveform motion count more than very fast changes
                    float previous = i > 0 ? samples[i - 1] : 0f;
                    float smoothed = (sample + previous) * 0.5f;

                    bandEnergy += smoothed * smoothed;
                }

                bandEnergy /= blockSize;

                float novelty = Math.Max(0f, bandEnergy - _previousBandEnergy);
                _previousBandEnergy = bandEnergy;

                _noveltyHistory.Enqueue(novelty);
                while (_noveltyHistory.Count > 96)
                {
                    _noveltyHistory.Dequeue();
                }

                strongestNovelty = Math.Max(strongestNovelty, novelty);
            }

            if (_noveltyHistory.Count < 12)
            {
                return (0, false);
            }

            float noveltyAverage = _noveltyHistory.Average();
            float noveltyPeak = _noveltyHistory.Max();

            // Require both "above average" and "meaningfully active"
            if (noveltyAverage > 0f && noveltyPeak > noveltyAverage * 1.8f)
            {
                DateTime nowUtc = DateTime.UtcNow;

                // Cooldown prevents the same beat from being counted repeatedly across frames
                if (_lastBeatTimeUtc == DateTime.MinValue ||
                    (nowUtc - _lastBeatTimeUtc).TotalMilliseconds >= 280)
                {
                    beatDetected = true;

                    if (_lastBeatTimeUtc != DateTime.MinValue)
                    {
                        double intervalSeconds = (nowUtc - _lastBeatTimeUtc).TotalSeconds;
                        double bpm = 60.0 / intervalSeconds;

                        if (bpm >= 60.0 && bpm <= 200.0)
                        {
                            _beatIntervalsSeconds.Add(intervalSeconds);

                            while (_beatIntervalsSeconds.Count > 8)
                            {
                                _beatIntervalsSeconds.RemoveAt(0);
                            }
                        }
                    }

                    _lastBeatTimeUtc = nowUtc;
                }
            }

            if (_beatIntervalsSeconds.Count < 4)
            {
                return (0, false);
            }

            double averageInterval = _beatIntervalsSeconds.Average();
            double averageBpm = 60.0 / averageInterval;

            double meanAbsoluteDeviation = _beatIntervalsSeconds
                .Select(x => Math.Abs(x - averageInterval))
                .Average();

            bool confident = meanAbsoluteDeviation < 0.06;

            if (!confident && beatDetected)
            {
                return (averageBpm, false);
            }

            return confident
                ? (averageBpm, true)
                : (0, false);
        }
    }
}
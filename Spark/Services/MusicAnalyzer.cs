using System.IO;
using NAudio.Wave;
///////////////////////////////////////////////
namespace Spark.Services;

/// <summary>
/// Analysis results for a music track — feeds the visualizer and the
/// prompt-feedback loop for refining generation parameters.
/// </summary>
sealed class MusicAnalysis
{
    public float[] Waveform { get; init; } = [];
    public float[] EnvelopeDb { get; init; } = [];
    public float[][] SpectrogramBands { get; init; } = [];
    public float EstimatedBpm { get; init; }
    public float PeakDb { get; init; }
    public float RmsDb { get; init; }
    public float SpectralCentroid { get; init; }
    public double DurationSeconds { get; init; }
    public int SampleRate { get; init; }

    /// <summary>
    /// Describes the "vibe" of the track for feeding back into the generator prompt.
    /// </summary>
    public string VibeDescription()
    {
        string energy = RmsDb switch
        {
            > -10 => "high-energy",
            > -20 => "moderate-energy",
            _ => "soft, ambient"
        };
        string tempo = EstimatedBpm switch
        {
            > 140 => "fast-tempo",
            > 100 => "mid-tempo",
            > 60 => "relaxed-tempo",
            _ => "very slow"
        };
        string brightness = SpectralCentroid switch
        {
            > 3000 => "bright, trebly",
            > 1500 => "balanced spectrum",
            _ => "dark, bassy"
        };
        return $"{energy}, {tempo} ({EstimatedBpm:F0} BPM), {brightness}";
    }
}

/// <summary>
/// Analyzes WAV/MP3 audio files to extract waveform, spectral, and rhythmic features.
/// All analysis is done in-memory from the decoded PCM stream.
/// </summary>
static class MusicAnalyzer
{
    const int EnvelopeWindowSamples = 2048;
    const int SpectrumBands = 64;
    const int FftSize = 2048;
    const int SpectrumHopSamples = 4096;

    public static MusicAnalysis Analyze(string filePath)
    {
        using AudioFileReader reader = new(filePath);
        int sampleRate = reader.WaveFormat.SampleRate;
        int channels = reader.WaveFormat.Channels;
        long totalSamples = reader.Length / (reader.WaveFormat.BitsPerSample / 8);

        // Read all samples (mono-mix)
        float[] raw = new float[totalSamples];
        int read = reader.Read(raw, 0, raw.Length);
        float[] mono = MixToMono(raw, read, channels);

        // Waveform (downsampled for display — ~2000 points)
        float[] waveform = DownsampleWaveform(mono, 2000);

        // RMS envelope in dB
        float[] envelope = ComputeEnvelope(mono, EnvelopeWindowSamples);

        // Peak / RMS
        float peak = 0;
        double sumSq = 0;
        for (int i = 0; i < mono.Length; i++)
        {
            float abs = Math.Abs(mono[i]);
            if (abs > peak) peak = abs;
            sumSq += mono[i] * (double)mono[i];
        }
        float rms = (float)Math.Sqrt(sumSq / mono.Length);
        float peakDb = peak > 0 ? 20f * MathF.Log10(peak) : -96f;
        float rmsDb = rms > 0 ? 20f * MathF.Log10(rms) : -96f;

        // Spectrogram (band energies over time)
        float[][] spectrogram = ComputeSpectrogram(mono, sampleRate);

        // Estimated BPM from onset energy
        float bpm = EstimateBpm(envelope, sampleRate);

        // Spectral centroid (average brightness)
        float centroid = ComputeSpectralCentroid(mono, sampleRate);

        double duration = mono.Length / (double)sampleRate;

        return new MusicAnalysis
        {
            Waveform = waveform,
            EnvelopeDb = envelope,
            SpectrogramBands = spectrogram,
            EstimatedBpm = bpm,
            PeakDb = peakDb,
            RmsDb = rmsDb,
            SpectralCentroid = centroid,
            DurationSeconds = duration,
            SampleRate = sampleRate,
        };
    }

    static float[] MixToMono(float[] raw, int count, int channels)
    {
        if (channels == 1) return raw[..count];
        int monoLen = count / channels;
        float[] mono = new float[monoLen];
        for (int i = 0; i < monoLen; i++)
        {
            float sum = 0;
            for (int ch = 0; ch < channels; ch++)
                sum += raw[i * channels + ch];
            mono[i] = sum / channels;
        }
        return mono;
    }

    static float[] DownsampleWaveform(float[] mono, int targetPoints)
    {
        if (mono.Length <= targetPoints) return mono;
        float[] result = new float[targetPoints];
        double step = mono.Length / (double)targetPoints;
        for (int i = 0; i < targetPoints; i++)
        {
            int start = (int)(i * step);
            int end = Math.Min((int)((i + 1) * step), mono.Length);
            float max = 0;
            for (int j = start; j < end; j++)
            {
                float abs = Math.Abs(mono[j]);
                if (abs > max) max = abs;
            }
            result[i] = max;
        }
        return result;
    }

    static float[] ComputeEnvelope(float[] mono, int windowSize)
    {
        int count = mono.Length / windowSize;
        float[] env = new float[count];
        for (int i = 0; i < count; i++)
        {
            double sum = 0;
            int offset = i * windowSize;
            for (int j = 0; j < windowSize && offset + j < mono.Length; j++)
            {
                float s = mono[offset + j];
                sum += s * (double)s;
            }
            float rms = (float)Math.Sqrt(sum / windowSize);
            env[i] = rms > 0 ? 20f * MathF.Log10(rms) : -96f;
        }
        return env;
    }

    static float[][] ComputeSpectrogram(float[] mono, int sampleRate)
    {
        int frames = mono.Length / SpectrumHopSamples;
        float[][] bands = new float[frames][];
        float[] window = MakeHannWindow(FftSize);

        for (int f = 0; f < frames; f++)
        {
            int offset = f * SpectrumHopSamples;
            // Simple DFT magnitude for SpectrumBands bands
            float[] bandEnergy = new float[SpectrumBands];
            int binsPerBand = FftSize / 2 / SpectrumBands;
            if (binsPerBand < 1) binsPerBand = 1;

            // Compute magnitudes using real-only DFT for each band center
            for (int b = 0; b < SpectrumBands; b++)
            {
                int binStart = b * binsPerBand;
                int binEnd = Math.Min(binStart + binsPerBand, FftSize / 2);
                double energy = 0;
                for (int bin = binStart; bin < binEnd; bin++)
                {
                    double freq = 2 * Math.PI * bin / FftSize;
                    double re = 0, im = 0;
                    for (int n = 0; n < FftSize && offset + n < mono.Length; n++)
                    {
                        double w = window[n] * mono[offset + n];
                        re += w * Math.Cos(freq * n);
                        im -= w * Math.Sin(freq * n);
                    }
                    energy += Math.Sqrt(re * re + im * im);
                }
                bandEnergy[b] = (float)(energy / (binEnd - binStart));
            }
            bands[f] = bandEnergy;
        }
        return bands;
    }

    static float EstimateBpm(float[] envelope, int sampleRate)
    {
        if (envelope.Length < 4) return 120;
        // Compute onset strength (positive difference in envelope)
        float[] onset = new float[envelope.Length - 1];
        for (int i = 0; i < onset.Length; i++)
            onset[i] = Math.Max(0, envelope[i + 1] - envelope[i]);

        // Autocorrelation on onset signal to find periodicity
        double envelopeRate = sampleRate / (double)EnvelopeWindowSamples;
        int minLag = (int)(envelopeRate * 60.0 / 200); // 200 BPM max
        int maxLag = (int)(envelopeRate * 60.0 / 50);  // 50 BPM min
        maxLag = Math.Min(maxLag, onset.Length / 2);

        double bestCorr = 0;
        int bestLag = minLag;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double corr = 0;
            for (int i = 0; i < onset.Length - lag; i++)
                corr += onset[i] * onset[i + lag];
            if (corr > bestCorr) { bestCorr = corr; bestLag = lag; }
        }

        double bpm = 60.0 * envelopeRate / bestLag;
        return (float)Math.Clamp(bpm, 50, 200);
    }

    static float ComputeSpectralCentroid(float[] mono, int sampleRate)
    {
        // Compute spectral centroid from first FftSize samples
        int n = Math.Min(FftSize * 4, mono.Length);
        float[] window = MakeHannWindow(n);
        double weightedSum = 0, magnitudeSum = 0;

        for (int bin = 1; bin < n / 2; bin++)
        {
            double freq = 2 * Math.PI * bin / n;
            double re = 0, im = 0;
            for (int i = 0; i < n; i++)
            {
                double w = window[i] * mono[i];
                re += w * Math.Cos(freq * i);
                im -= w * Math.Sin(freq * i);
            }
            double mag = Math.Sqrt(re * re + im * im);
            double hzFreq = bin * sampleRate / (double)n;
            weightedSum += hzFreq * mag;
            magnitudeSum += mag;
        }

        return magnitudeSum > 0 ? (float)(weightedSum / magnitudeSum) : 1000f;
    }

    static float[] MakeHannWindow(int size)
    {
        float[] w = new float[size];
        for (int i = 0; i < size; i++)
            w[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (size - 1)));
        return w;
    }
}

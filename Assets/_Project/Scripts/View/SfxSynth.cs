using UnityEngine;

namespace HollowLines.View
{
    /// <summary>
    /// M4 step 4: procedural audio generation. Every clip is synthesized at startup via
    /// AudioClip.Create — same "no art assets" philosophy as BoardView's generated white sprite.
    /// Clips are built once by AudioManager and reused; playback pitch (not regeneration) is how
    /// the chain SFX rises in tone.
    /// </summary>
    public static class SfxSynth
    {
        private const int SampleRate = 44100;

        /// <summary>A single sine tone with a short linear attack/decay to avoid clicks.</summary>
        public static AudioClip Tone(float frequency, float duration, float volume = 0.5f,
                                      float attack = 0.005f, float decay = 0.05f)
        {
            int samples = Mathf.Max(1, (int)(SampleRate * duration));
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * Envelope(i, samples, attack, decay) * volume;
            }
            return BuildClip("Tone", data);
        }

        /// <summary>A linear frequency sweep — descending for a stinger, ascending for a fanfare start.</summary>
        public static AudioClip Sweep(float startFrequency, float endFrequency, float duration, float volume = 0.5f)
        {
            int samples = Mathf.Max(1, (int)(SampleRate * duration));
            var data = new float[samples];
            float phase = 0f;
            for (int i = 0; i < samples; i++)
            {
                float f = Mathf.Lerp(startFrequency, endFrequency, (float)i / samples);
                phase += 2f * Mathf.PI * f / SampleRate;
                data[i] = Mathf.Sin(phase) * Envelope(i, samples, 0.01f, 0.05f) * volume;
            }
            return BuildClip("Sweep", data);
        }

        /// <summary>Low-passed white noise — thuds and impacts (crush, bomb burst) instead of a pure hiss.</summary>
        public static AudioClip Noise(float duration, float volume = 0.5f, float lowPassFactor = 0.2f)
        {
            int samples = Mathf.Max(1, (int)(SampleRate * duration));
            var data = new float[samples];
            float prev = 0f;
            for (int i = 0; i < samples; i++)
            {
                float raw = Random.Range(-1f, 1f);
                prev = Mathf.Lerp(prev, raw, lowPassFactor);
                data[i] = prev * Envelope(i, samples, 0.002f, 0.08f) * volume;
            }
            return BuildClip("Noise", data);
        }

        /// <summary>
        /// Layered noise + tone: the crack of a chunk shattering on impact.
        /// The noise gives the debris texture, the tone underneath gives it weight — AudioManager
        /// plays it back at a lower pitch for bigger chunks so mass reads as depth.
        /// </summary>
        public static AudioClip Shatter(float duration, float toneFrequency, float volume = 0.5f,
                                        float noiseMix = 0.6f, float lowPassFactor = 0.35f)
        {
            int samples = Mathf.Max(1, (int)(SampleRate * duration));
            var data = new float[samples];
            float prev = 0f;
            float toneMix = 1f - noiseMix;

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;

                float raw = Random.Range(-1f, 1f);
                prev = Mathf.Lerp(prev, raw, lowPassFactor);

                // The tone drops an octave across the clip — the "settling" half of the impact.
                float f = Mathf.Lerp(toneFrequency, toneFrequency * 0.5f, (float)i / samples);
                float tone = Mathf.Sin(2f * Mathf.PI * f * t);

                data[i] = (prev * noiseMix + tone * toneMix)
                          * Envelope(i, samples, 0.002f, duration * 0.6f) * volume;
            }
            return BuildClip("Shatter", data);
        }

        /// <summary>A short sequence of tones played back to back into one clip — chimes, fanfares, loops.</summary>
        public static AudioClip Arpeggio(float[] frequencies, float noteDuration, float volume = 0.3f)
        {
            int samplesPerNote = Mathf.Max(1, (int)(SampleRate * noteDuration));
            var data = new float[samplesPerNote * frequencies.Length];
            for (int n = 0; n < frequencies.Length; n++)
            {
                for (int i = 0; i < samplesPerNote; i++)
                {
                    float t = (float)i / SampleRate;
                    float env = Envelope(i, samplesPerNote, 0.01f, 0.15f);
                    data[n * samplesPerNote + i] = Mathf.Sin(2f * Mathf.PI * frequencies[n] * t) * env * volume;
                }
            }
            return BuildClip("Arpeggio", data);
        }

        /// <summary>Linear attack → sustain → linear decay, expressed in seconds of the total clip.</summary>
        private static float Envelope(int i, int totalSamples, float attackSeconds, float decaySeconds)
        {
            int attackSamples = Mathf.Max(1, (int)(attackSeconds * SampleRate));
            int decaySamples = Mathf.Max(1, (int)(decaySeconds * SampleRate));

            if (i < attackSamples)
                return (float)i / attackSamples;

            int fromEnd = totalSamples - i;
            if (fromEnd < decaySamples)
                return Mathf.Max(0f, (float)fromEnd / decaySamples);

            return 1f;
        }

        private static AudioClip BuildClip(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

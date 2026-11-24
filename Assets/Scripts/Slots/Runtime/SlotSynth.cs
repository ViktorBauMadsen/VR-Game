using System;
using System.Collections.Generic;
using UnityEngine;

namespace VideoSlots
{
    // Generated sounds for the video slots - only the ones the project
    // doesn't already have (reel ticks/stops, win jingles, the roulette
    // wheel and the casino chimes are reused). Built once, cached.
    public static class SlotSynth
    {
        const int Rate = 44100;
        const float TwoPi = Mathf.PI * 2f;
        static readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>();

        static AudioClip Clip(string name, float seconds, Action<float[]> fill, bool loop = false, float peak = 0.85f)
        {
            if (_cache.TryGetValue(name, out var c) && c != null) return c;
            var data = new float[Mathf.CeilToInt(seconds * Rate)];
            fill(data);
            float max = 1e-5f;
            for (int i = 0; i < data.Length; i++) max = Mathf.Max(max, Mathf.Abs(data[i]));
            float k = peak / max;
            int fade = loop ? 0 : Mathf.Min(data.Length / 4, 220);
            for (int i = 0; i < data.Length; i++)
            {
                float v = data[i] * k;
                if (fade > 0 && i > data.Length - fade) v *= (data.Length - i) / (float)fade;
                data[i] = v;
            }
            c = AudioClip.Create("Slot " + name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            _cache[name] = c;
            return c;
        }

        static float T(int i) => (float)i / Rate;
        static float Sin(float f, float t) => Mathf.Sin(TwoPi * f * t);

        // Inharmonic struck-metal partials.
        static float Bell(float f, float t, float decay)
        {
            if (t < 0f) return 0f;
            return (Sin(f, t) * 1f + Sin(f * 2.76f, t) * 0.45f * Mathf.Exp(-t * decay * 1.6f)
                + Sin(f * 5.4f, t) * 0.25f * Mathf.Exp(-t * decay * 2.5f) + Sin(f * 8.93f, t) * 0.12f * Mathf.Exp(-t * decay * 4f))
                * Mathf.Exp(-t * decay) * Mathf.Min(1f, t * 2000f);
        }

        // Brassy saw (few harmonics, rolled off).
        static float Brass(float f, float t, float bright = 1f)
        {
            float v = 0f;
            for (int h = 1; h <= 7; h++) v += Sin(f * h, t) / (h * (1f + (h - 1) * (1.2f - bright * 0.6f)));
            return v * 0.6f;
        }

        static float Square(float f, float t)
        {
            float v = 0f;
            for (int h = 1; h <= 9; h += 2) v += Sin(f * h, t) / h;
            return v;
        }

        static float Note(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        // Feedback echo; wrap = for seamless loops.
        static void Echo(float[] d, float delay, float feedback, float mix, bool wrap = false)
        {
            int n = Mathf.Max(1, (int)(delay * Rate));
            var wet = new float[d.Length];
            int passes = wrap ? 2 : 1;
            for (int p = 0; p < passes; p++)
                for (int i = 0; i < d.Length; i++)
                {
                    int j = i - n;
                    float prev = j >= 0 ? wet[j] : (wrap ? wet[j + d.Length] : 0f);
                    wet[i] = d[i] + prev * feedback;
                }
            for (int i = 0; i < d.Length; i++) d[i] = d[i] + (wet[i] - d[i]) * mix;
        }

        // ------------------------------------------------------------ clips
        public static AudioClip OrbLand() => Clip("OrbLand", 1.1f, d =>
        {
            var rng = new System.Random(3);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float n = (float)rng.NextDouble() * 2f - 1f;
                lp += (n - lp) * 0.35f;
                float thump = Mathf.Sin(TwoPi * (55f * t + 9f * (1f - Mathf.Exp(-t * 30f)))) * Mathf.Exp(-t * 8f) * 1.1f;
                float crackle = lp * Mathf.Exp(-t * 7f) * (rng.NextDouble() < 0.02 ? 1.6f : 0.35f);
                float bell = Bell(784f, t - 0.01f, 3.5f) * 0.45f + Bell(1175f, t - 0.03f, 4.5f) * 0.25f;
                d[i] = thump + crackle * 0.5f + bell;
            }
            Echo(d, 0.11f, 0.3f, 0.35f);
        });

        public static AudioClip Anticipation() => Clip("Anticipation", 2.4f, d =>
        {
            var rng = new System.Random(8);
            float hp = 0f, prev = 0f, ph = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i), u = t / 2.4f;
                float f = 180f * Mathf.Pow(4.5f, u);
                ph += f / Rate;
                float trem = 0.6f + 0.4f * Mathf.Sin(TwoPi * (4f + 14f * u) * t);
                float tone = (Mathf.Sin(TwoPi * ph) + 0.4f * Mathf.Sin(TwoPi * ph * 2.005f)) * trem * (0.25f + 0.75f * u);
                float n = (float)rng.NextDouble() * 2f - 1f;
                hp = 0.92f * (hp + n - prev); prev = n;
                float beat = Mathf.Repeat(t, 0.5f - 0.25f * u);
                float heart = Mathf.Sin(TwoPi * 60f * beat) * Mathf.Exp(-beat * 25f) * 0.8f;
                d[i] = tone * 0.5f + hp * 0.35f * u * u + heart;
            }
        });

        public static AudioClip FeatureFanfare() => Clip("FeatureFanfare", 3.0f, d =>
        {
            int[] notes = { 60, 64, 67, 72, 76 };
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i), v = 0f;
                for (int k = 0; k < notes.Length; k++)
                {
                    float s = t - k * 0.085f;
                    if (s < 0f) continue;
                    float env = Mathf.Min(1f, s * 60f) * (k == notes.Length - 1 ? Mathf.Exp(-s * 0.9f) : Mathf.Exp(-s * 1.3f));
                    v += Brass(Note(notes[k]), s, 1f) * env * (1f + 0.05f * Mathf.Sin(TwoPi * 5.5f * s));
                }
                float timp = Mathf.Sin(TwoPi * 65f * t) * Mathf.Exp(-t * 6f) + Mathf.Sin(TwoPi * 65f * (t - 0.42f)) * (t > 0.42f ? Mathf.Exp(-(t - 0.42f) * 6f) : 0f);
                float shimmer = Bell(2093f, t - 0.4f, 3f) * 0.2f + Bell(2637f, t - 0.5f, 3f) * 0.15f + Bell(3136f, t - 0.6f, 3f) * 0.12f;
                d[i] = v * 0.5f + timp * 0.6f + shimmer;
            }
            Echo(d, 0.14f, 0.35f, 0.3f);
        });

        public static AudioClip RespinReset() => Clip("RespinReset", 0.9f, d =>
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                d[i] = Bell(1568f, t, 6f) * 0.6f + Bell(2093f, t - 0.07f, 6f) * 0.6f + Bell(2637f, t - 0.14f, 5f) * 0.5f;
            }
            Echo(d, 0.09f, 0.3f, 0.35f);
        });

        public static AudioClip JackpotFanfare() => Clip("JackpotFanfare", 4.2f, d =>
        {
            int[] scale = { 72, 74, 76, 79, 81, 84, 86, 88, 91, 93, 96 };
            int[] chord = { 48, 60, 64, 67, 72 };
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i), v = 0f;
                for (int k = 0; k < 22; k++)
                {
                    int idx = k < 11 ? k : 21 - k;
                    v += Bell(Note(scale[idx]), t - k * 0.07f, 3.2f) * 0.35f;
                }
                float sw = Mathf.Clamp01((t - 0.3f) * 1.5f) * Mathf.Exp(-Mathf.Max(0f, t - 2.2f) * 1.4f);
                float pad = 0f;
                foreach (int n in chord) pad += Brass(Note(n), t, 0.8f);
                float boom = Mathf.Sin(TwoPi * (45f * t + 6f * (1f - Mathf.Exp(-t * 20f)))) * Mathf.Exp(-t * 3f);
                d[i] = v + pad * sw * 0.22f * (1f + 0.06f * Mathf.Sin(TwoPi * 6f * t)) + boom * 0.8f;
            }
            Echo(d, 0.16f, 0.4f, 0.35f);
        });

        public static AudioClip CoinTick() => Clip("CoinTick", 0.16f, d =>
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                d[i] = Bell(3400f, t, 28f) * 0.7f + Bell(5150f, t - 0.012f, 34f) * 0.45f;
            }
        });

        public static AudioClip Whoosh() => Clip("Whoosh", 0.75f, d =>
        {
            var rng = new System.Random(21);
            float a = 0f, b = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i), u = t / 0.75f;
                float cut = Mathf.Lerp(0.04f, 0.5f, Mathf.Sin(u * Mathf.PI));
                float n = (float)rng.NextDouble() * 2f - 1f;
                a += (n - a) * cut;
                b += (a - b) * cut;
                float env = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 2f);
                d[i] = (a - b * 0.6f) * env;
            }
        }, false, 0.7f);

        public static AudioClip ScatterLand() => Clip("ScatterLand", 1.4f, d =>
        {
            var rng = new System.Random(5);
            float[] sparkT = new float[10], sparkF = new float[10];
            for (int k = 0; k < 10; k++) { sparkT[k] = 0.05f + (float)rng.NextDouble() * 0.5f; sparkF[k] = 2600f + (float)rng.NextDouble() * 2600f; }
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i), v = Bell(1046f, t, 2.6f) * 0.7f + Bell(1568f, t - 0.02f, 3f) * 0.45f + Bell(523f, t, 2.2f) * 0.35f;
                for (int k = 0; k < 10; k++) v += Bell(sparkF[k], t - sparkT[k], 14f) * 0.12f;
                d[i] = v;
            }
            Echo(d, 0.12f, 0.35f, 0.35f);
        });

        public static AudioClip WildLand() => Clip("WildLand", 1.0f, d =>
        {
            int[] chord = { 64, 68, 71, 76 };
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i), v = 0f;
                foreach (int n in chord) v += Brass(Note(n), t, 1f) * Mathf.Exp(-t * 6f);
                v += Bell(2637f, t - 0.05f, 6f) * 0.3f + Bell(3520f, t - 0.1f, 7f) * 0.2f;
                float sweep = Mathf.Sin(TwoPi * (300f * t + 900f * t * t)) * Mathf.Exp(-t * 9f) * 0.3f;
                d[i] = v * 0.4f + sweep;
            }
            Echo(d, 0.1f, 0.3f, 0.3f);
        });

        public static AudioClip Collect() => Clip("Collect", 0.45f, d =>
        {
            float ph = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i), u = Mathf.Clamp01(t / 0.3f);
                ph += 500f * Mathf.Pow(5f, u) / Rate;
                float zip = (Mathf.Sin(TwoPi * ph) + 0.3f * Mathf.Sin(TwoPi * ph * 2f)) * Mathf.Sin(u * Mathf.PI) * (t < 0.3f ? 1f : 0f);
                d[i] = zip * 0.5f + Bell(2794f, t - 0.28f, 12f) * 0.7f;
            }
        });

        public static AudioClip MultiplierHit() => Clip("MultiplierHit", 0.9f, d =>
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float boom = Mathf.Sin(TwoPi * (80f * t + 20f * (1f - Mathf.Exp(-t * 25f)))) * Mathf.Exp(-t * 7f);
                float zap = Square(220f * (1f + 2f * Mathf.Exp(-t * 12f)), t) * Mathf.Exp(-t * 10f) * 0.3f;
                d[i] = boom + zap + Bell(1760f, t - 0.04f, 5f) * 0.4f;
            }
            Echo(d, 0.1f, 0.3f, 0.3f);
        });

        // Coin shower under big wins (loops).
        public static AudioClip CoinShower() => Clip("CoinShower", 2f, d =>
        {
            var rng = new System.Random(77);
            for (int k = 0; k < 70; k++)
            {
                int start = rng.Next(d.Length);
                float f = 2800f + (float)rng.NextDouble() * 2600f, amp = 0.2f + (float)rng.NextDouble() * 0.35f;
                int len = (int)(0.12f * Rate);
                for (int i = 0; i < len; i++)
                {
                    float t = T(i);
                    d[(start + i) % d.Length] += (Bell(f, t, 30f) + Bell(f * 1.5f, t - 0.008f, 36f) * 0.5f) * amp;
                }
            }
        }, true, 0.6f);

        // Hold & Spin music: drums and a low fiery drone (loops, 120 bpm, 2 bars).
        public static AudioClip InfernoBed() => Clip("InfernoBed", 4f, d =>
        {
            var rng = new System.Random(13);
            float lp = 0f;
            float[] kicks = { 0f, 0.75f, 1f, 2f, 2.75f, 3f, 3.5f };
            float[] toms = { 0.5f, 1.5f, 1.75f, 2.5f, 3.25f, 3.75f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i), v = 0f;
                foreach (float k in kicks) { float s = t - k * 0.5f * 2f; if (s >= 0f && s < 0.4f) v += Mathf.Sin(TwoPi * (48f * s + 7f * (1f - Mathf.Exp(-s * 30f)))) * Mathf.Exp(-s * 9f) * 1.1f; }
                foreach (float k in toms) { float s = t - k; if (s >= 0f && s < 0.35f) v += Mathf.Sin(TwoPi * (120f * s + 4f * (1f - Mathf.Exp(-s * 25f)))) * Mathf.Exp(-s * 11f) * 0.6f; }
                float n = (float)rng.NextDouble() * 2f - 1f;
                lp += (n - lp) * 0.08f;
                float hat = Mathf.Repeat(t, 0.25f);
                v += n * Mathf.Exp(-hat * 60f) * 0.12f;
                float drone = (Sin(55f, t) + 0.5f * Sin(82.5f, t) + 0.25f * Sin(110f, t)) * (0.55f + 0.45f * Sin(0.5f, t)) * 0.35f;
                d[i] = v + drone + lp * 0.25f;
            }
            Echo(d, 0.375f, 0.25f, 0.2f, true);
        }, true, 0.75f);

        // Free spins music: bright arpeggio over a pad (loops, 120 bpm, 2 bars).
        public static AudioClip StarBed() => Clip("StarBed", 4f, d =>
        {
            int[][] chords = { new[] { 57, 60, 64, 69 }, new[] { 53, 57, 60, 65 }, new[] { 55, 59, 62, 67 }, new[] { 52, 56, 59, 64 } };
            int[] pattern = { 0, 1, 2, 3, 2, 1, 3, 2 };
            var rng = new System.Random(4);
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i), v = 0f;
                int bar = Mathf.Min(3, (int)(t / 1f));
                var ch = chords[bar];
                float step = Mathf.Repeat(t, 0.125f);
                int si = (int)(t / 0.125f) % 8;
                float f = Note(ch[pattern[si]] + 12);
                v += Square(f, step) * Mathf.Exp(-step * 14f) * 0.22f;
                foreach (int n in ch) v += Sin(Note(n), t) * 0.07f * (0.8f + 0.2f * Sin(0.25f, t));
                float beat = Mathf.Repeat(t, 0.5f);
                v += Mathf.Sin(TwoPi * (50f * beat + 6f * (1f - Mathf.Exp(-beat * 30f)))) * Mathf.Exp(-beat * 10f) * 0.7f;
                float off = Mathf.Repeat(t + 0.25f, 0.5f);
                v += ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-off * 50f) * 0.15f;
                d[i] = v;
            }
            Echo(d, 0.375f, 0.35f, 0.3f, true);
        }, true, 0.75f);

        // Fallback pointer clack if the roulette clips aren't assigned.
        public static AudioClip Clack() => Clip("Clack", 0.08f, d =>
        {
            var rng = new System.Random(9);
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                d[i] = (((float)rng.NextDouble() * 2f - 1f) * 0.6f + Sin(1900f, t) * 0.5f) * Mathf.Exp(-t * 90f);
            }
        });

        // ------------------------------------------------- Fruit Cocktail
        // Diamond scatter lands: a glassy crystal ping with a sparkle run.
        public static AudioClip DiamondLand() => Clip("DiamondLand", 1.5f, d =>
        {
            int[] run = { 84, 88, 91, 96, 100 };
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                float v = Bell(1318.5f, t, 2.2f) * 0.6f + Bell(1975.5f, t - 0.015f, 2.6f) * 0.45f + Sin(659.3f, t) * Mathf.Exp(-t * 3f) * 0.3f * Mathf.Min(1f, t * 400f);
                for (int k = 0; k < run.Length; k++) v += Bell(Note(run[k]), t - 0.03f - k * 0.035f, 9f) * 0.18f;
                d[i] = v;
            }
            Echo(d, 0.13f, 0.4f, 0.4f);
        });

        // Short wooden knock for the special-symbol picker.
        public static AudioClip PickTick() => Clip("PickTick", 0.25f, d =>
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i);
                d[i] = (Sin(880f, t) + 0.5f * Sin(1760f, t) * Mathf.Exp(-t * 30f) + 0.4f * Sin(3520f, t) * Mathf.Exp(-t * 60f)) * Mathf.Exp(-t * 22f) * Mathf.Min(1f, t * 3000f);
            }
        });

        // Fizzy pour: hiss with popping bubbles.
        public static AudioClip Fizz() => Clip("Fizz", 1.6f, d =>
        {
            var rng = new System.Random(17);
            float hp = 0f, prev = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i), env = Mathf.Min(1f, t * 8f) * Mathf.Exp(-t * 1.6f);
                float n = (float)rng.NextDouble() * 2f - 1f;
                hp = 0.85f * (hp + n - prev); prev = n;
                d[i] = hp * 0.22f * env;
            }
            for (int k = 0; k < 90; k++)
            {
                int start = rng.Next((int)(d.Length * 0.85f));
                float f0 = 700f + (float)rng.NextDouble() * 1800f, amp = 0.25f + (float)rng.NextDouble() * 0.4f;
                float fade = Mathf.Exp(-start / (float)Rate * 1.2f);
                int len = (int)(0.03f * Rate);
                float ph = 0f;
                for (int i = 0; i < len && start + i < d.Length; i++)
                {
                    float t = T(i);
                    ph += f0 * (1f + t * 30f) / Rate;
                    d[start + i] += Mathf.Sin(TwoPi * ph) * Mathf.Exp(-t * 120f) * amp * fade;
                }
            }
        });

        // A reel fills with the special symbol: rising swoosh into a sparkling hit.
        public static AudioClip Expand() => Clip("Expand", 1.3f, d =>
        {
            var rng = new System.Random(31);
            float a = 0f, ph = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i), u = Mathf.Clamp01(t / 0.42f);
                float n = (float)rng.NextDouble() * 2f - 1f;
                a += (n - a) * Mathf.Lerp(0.05f, 0.6f, u);
                float rise = t < 0.42f ? a * Mathf.Sin(u * Mathf.PI) * 0.6f : 0f;
                ph += Mathf.Lerp(300f, 1400f, u) / Rate;
                float sweep = Mathf.Sin(TwoPi * ph) * u * (t < 0.42f ? 1f : Mathf.Exp(-(t - 0.42f) * 12f)) * 0.3f;
                float s = t - 0.4f;
                float boom = s > 0f ? Mathf.Sin(TwoPi * (60f * s + 10f * (1f - Mathf.Exp(-s * 25f)))) * Mathf.Exp(-s * 6f) : 0f;
                float shimmer = Bell(2637f, s, 4f) * 0.35f + Bell(3136f, s - 0.04f, 4f) * 0.3f + Bell(3951f, s - 0.08f, 4f) * 0.25f;
                d[i] = rise + sweep + boom + shimmer;
            }
            Echo(d, 0.11f, 0.35f, 0.3f);
        });

        static float Steel(float f, float t)
        {
            if (t < 0f) return 0f;
            return (Sin(f, t) + 0.5f * Sin(f * 2.01f, t) * Mathf.Exp(-t * 6f) + 0.25f * Sin(f * 2.98f, t) * Mathf.Exp(-t * 10f) + 0.15f * Sin(f * 4.1f, t) * Mathf.Exp(-t * 18f))
                * Mathf.Exp(-t * 4.5f) * Mathf.Min(1f, t * 1500f);
        }

        // Free spins music: steel-drum arpeggios, bass, shaker and congas (loops, 120 bpm, 2 bars).
        public static AudioClip CocktailBed() => Clip("CocktailBed", 4f, d =>
        {
            int[][] chords = { new[] { 48, 60, 64, 67 }, new[] { 45, 57, 60, 64 }, new[] { 41, 57, 60, 65 }, new[] { 43, 55, 59, 62 } };
            int[] melody =
            {
                76, 79, 84, 79, 76, 72, 74, 76, 72, 76, 81, 76, 72, 69, 71, 72,
                69, 72, 77, 72, 69, 65, 67, 69, 71, 74, 79, 74, 71, 67, 71, 74,
            };
            var rng = new System.Random(6);
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i), v = 0f;
                int bar = Mathf.Min(3, (int)t);
                var ch = chords[bar];
                int si = (int)(t / 0.125f);
                for (int back = 0; back < 3; back++)
                {
                    int k = si - back;
                    int idx = ((k % 32) + 32) % 32;
                    v += Steel(Note(melody[idx]), t - k * 0.125f) * 0.24f * (k % 2 == 0 ? 1f : 0.7f);
                }
                float bs = Mathf.Repeat(t, 0.5f);
                int beat = (int)(t / 0.5f);
                float bf = Note(ch[0] + (beat % 2 == 1 ? 7 : 0));
                v += (Sin(bf, bs) + 0.3f * Sin(bf * 2f, bs)) * Mathf.Exp(-bs * 6f) * Mathf.Min(1f, bs * 800f) * 0.5f;
                float barT = t - bar;
                float padEnv = Mathf.Min(1f, barT * 20f) * Mathf.Min(1f, (1f - barT) * 20f);
                for (int k = 1; k < ch.Length; k++) v += Sin(Note(ch[k]), t) * 0.045f * padEnv;
                float sh = Mathf.Repeat(t, 0.125f);
                float n = (float)rng.NextDouble() * 2f - 1f;
                v += n * Mathf.Exp(-sh * 45f) * (si % 2 == 1 ? 0.12f : 0.06f);
                float cg = Mathf.Repeat(t - 0.25f, 0.5f);
                v += Mathf.Sin(TwoPi * (190f * cg + 3f * (1f - Mathf.Exp(-cg * 40f)))) * Mathf.Exp(-cg * 18f) * 0.35f;
                float kk = Mathf.Repeat(t, 1f);
                v += Mathf.Sin(TwoPi * (50f * kk + 6f * (1f - Mathf.Exp(-kk * 30f)))) * Mathf.Exp(-kk * 9f) * 0.7f;
                d[i] = v;
            }
            Echo(d, 0.375f, 0.3f, 0.25f, true);
        }, true, 0.75f);

        public static AudioClip BigWinSting() => Clip("BigWinSting", 2.2f, d =>
        {
            int[] notes = { 67, 72, 76, 79, 84 };
            for (int i = 0; i < d.Length; i++)
            {
                float t = T(i), v = 0f;
                for (int k = 0; k < notes.Length; k++) v += Brass(Note(notes[k]), t - k * 0.06f, 1f) * (t - k * 0.06f > 0f ? Mathf.Exp(-(t - k * 0.06f) * 1.6f) : 0f);
                float boom = Mathf.Sin(TwoPi * (40f * t + 8f * (1f - Mathf.Exp(-t * 20f)))) * Mathf.Exp(-t * 4f);
                d[i] = v * 0.35f + boom + Bell(4186f, t - 0.3f, 4f) * 0.15f;
            }
            Echo(d, 0.13f, 0.35f, 0.3f);
        });
    }
}

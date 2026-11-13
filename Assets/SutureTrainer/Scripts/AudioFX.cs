using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Audio 100% procedural (sin assets): ambiente de quirófano con monitor
    /// de signos vitales y efectos por evento. Se auto-instala en runtime.
    /// </summary>
    public static class AudioFX
    {
        static AudioSource _fx;
        static AudioSource _ambient;
        static AudioClip _click, _pop, _error, _success, _complete, _beep, _tension, _room;
        static AudioSource _beepSrc, _chatter;
        static readonly System.Collections.Generic.Dictionary<string, AudioClip> _dist =
            new System.Collections.Generic.Dictionary<string, AudioClip>();

        /// <summary>Frecuencia cardiaca del monitor (lpm). La fija DistractorManager.</summary>
        public static float HeartRate { get; private set; } = 65f;
        /// <summary>Saturación del monitor (%): el tono del bip baja con ella, como en un oxímetro real.</summary>
        public static float SpO2 { get; private set; } = 98f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (_fx != null) return;
            var go = new GameObject("AudioFX");
            Object.DontDestroyOnLoad(go);
            _fx = go.AddComponent<AudioSource>();
            _fx.spatialBlend = 0f;
            _ambient = go.AddComponent<AudioSource>();
            _ambient.spatialBlend = 0f;
            _ambient.loop = true;
            _ambient.volume = 0.16f;
            Generate();
            _ambient.clip = _room;
            _ambient.Play();
            _beepSrc = go.AddComponent<AudioSource>();
            _beepSrc.spatialBlend = 0f;
            _chatter = go.AddComponent<AudioSource>();
            _chatter.spatialBlend = 0f;
            _chatter.loop = true;
            _chatter.volume = 0f;
            _chatter.clip = Clip("murmullo", Murmullo(7f));
            _chatter.Play();
            go.AddComponent<MonitorBeep>();
        }

        // ---------- API ----------
        public static void Click() => Play(_click, 0.5f);
        public static void Pop() => Play(_pop, 0.9f);
        public static void Error() => Play(_error, 0.8f);
        public static void Success() => Play(_success, 0.6f);
        public static void Complete() => Play(_complete, 0.8f);
        public static void Tension() => Play(_tension, 0.7f);
        public static void Beep()
        {
            if (_beepSrc == null || _beep == null) return;
            // ~1 semitono por cada punto de saturación bajo 98 %
            _beepSrc.pitch = Mathf.Pow(2f, (SpO2 - 98f) / 12f);
            _beepSrc.PlayOneShot(_beep, SpO2 < 92f ? 0.16f : 0.10f);
        }

        public static void SetVitals(float lpm, float spo2)
        {
            HeartRate = Mathf.Clamp(lpm, 30f, 200f);
            SpO2 = Mathf.Clamp(spo2, 70f, 100f);
        }

        public static void SetChatter(float volumen)
        {
            if (_chatter != null) _chatter.volume = Mathf.Clamp01(volumen);
        }

        /// <summary>Distractor sonoro: telefono, instrumento, alarma_media, alarma_alta, voz.</summary>
        public static void Distractor(string tipo)
        {
            if (_fx == null) return;
            if (!_dist.TryGetValue(tipo, out var c) || c == null) return;
            float vol = tipo == "alarma_alta" ? 0.55f : tipo == "voz" ? 0.6f : tipo == "estimulo" ? 0.7f : 0.45f;
            _fx.PlayOneShot(c, vol);
        }

        static void Play(AudioClip c, float vol)
        {
            if (_fx != null && c != null) _fx.PlayOneShot(c, vol);
        }

        // ---------- síntesis ----------
        const int SR = 44100;

        static void Generate()
        {
            _click = Clip("click", Tone(1200f, 0.03f, 0.6f, true));
            _pop = Clip("pop", Sweep(260f, 70f, 0.09f, 0.9f));
            _error = Clip("error", Concat(Tone(190f, 0.10f, 0.7f, false), Silence(0.05f), Tone(160f, 0.14f, 0.7f, false)));
            _success = Clip("success", Concat(Tone(660f, 0.08f, 0.5f, true), Tone(990f, 0.12f, 0.5f, true)));
            _complete = Clip("complete", Concat(Tone(523f, 0.11f, 0.5f, true), Tone(659f, 0.11f, 0.5f, true), Tone(784f, 0.22f, 0.5f, true)));
            _beep = Clip("beep", Tone(880f, 0.05f, 0.5f, true));
            _tension = Clip("tension", Tone(140f, 0.22f, 0.8f, false));
            _room = Clip("room", Room(2f));
            _dist["telefono"] = Clip("telefono", Telefono());
            _dist["instrumento"] = Clip("instrumento", Instrumento());
            _dist["alarma_media"] = Clip("alarma_media", Alarma(523f, new[] { 0f, 0.25f, 0.5f, 1.6f, 1.85f, 2.1f }, 0.2f));
            _dist["alarma_alta"] = Clip("alarma_alta", Alarma(880f, new[] { 0f, 0.13f, 0.26f, 0.55f, 0.68f, 1.4f, 1.53f, 1.66f, 1.95f, 2.08f }, 0.11f));
            _dist["voz"] = Clip("voz", Voces(1.6f, 1, 777));
            _dist["estimulo"] = Clip("estimulo", Concat(Tone(1500f, 0.07f, 0.7f, false), Silence(0.05f), Tone(1500f, 0.07f, 0.7f, false)));
        }

        static AudioClip Clip(string name, float[] data)
        {
            var c = AudioClip.Create(name, data.Length, 1, SR, false);
            c.SetData(data, 0);
            return c;
        }

        static float[] Tone(float freq, float dur, float amp, bool expDecay)
        {
            int n = (int)(dur * SR);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                float env = expDecay ? Mathf.Exp(-t * 18f) : Mathf.Sin(Mathf.PI * i / n);
                d[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * amp * env;
            }
            return d;
        }

        static float[] Sweep(float f0, float f1, float dur, float amp)
        {
            int n = (int)(dur * SR);
            var d = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float k = (float)i / n;
                float f = Mathf.Lerp(f0, f1, k);
                phase += 2f * Mathf.PI * f / SR;
                d[i] = Mathf.Sin(phase) * amp * (1f - k);
            }
            return d;
        }

        static float[] Silence(float dur) => new float[(int)(dur * SR)];

        static float[] Concat(params float[][] parts)
        {
            int total = 0;
            foreach (var p in parts) total += p.Length;
            var d = new float[total];
            int o = 0;
            foreach (var p in parts) { p.CopyTo(d, o); o += p.Length; }
            return d;
        }

        /// <summary>Ruido marrón suave + zumbido leve: sala de quirófano.</summary>
        static float[] Room(float dur)
        {
            int n = (int)(dur * SR);
            var d = new float[n];
            float brown = 0f;
            var rnd = new System.Random(1234);
            for (int i = 0; i < n; i++)
            {
                brown += ((float)rnd.NextDouble() * 2f - 1f) * 0.02f;
                brown *= 0.985f; // evita deriva y hace el loop continuo
                float hum = Mathf.Sin(2f * Mathf.PI * 90f * i / SR) * 0.015f;
                d[i] = brown * 0.9f + hum;
            }
            return d;
        }

        // ---------- distractores (sintetizados, sin archivos con licencia) ----------

        /// <summary>Timbre de teléfono: 440+480 Hz, dos ráfagas, dos veces.</summary>
        static float[] Telefono()
        {
            float[] Rafaga()
            {
                int n = (int)(0.4f * SR); var d = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / SR;
                    float env = Mathf.Min(1f, i / 400f) * Mathf.Min(1f, (n - i) / 400f);
                    d[i] = (Mathf.Sin(2f * Mathf.PI * 440f * t) + Mathf.Sin(2f * Mathf.PI * 480f * t)) * 0.25f * env;
                }
                return d;
            }
            return Concat(Rafaga(), Silence(0.2f), Rafaga(), Silence(1.0f), Rafaga(), Silence(0.2f), Rafaga());
        }

        /// <summary>Instrumento metálico que cae a la charola: parciales inarmónicos y un rebote.</summary>
        static float[] Instrumento()
        {
            float[] Golpe(float amp, float dur)
            {
                int n = (int)(dur * SR); var d = new float[n];
                float[] f = { 1180f, 2390f, 3570f, 5110f, 6830f };
                float[] k = { 6f, 9f, 13f, 18f, 25f };
                var rnd = new System.Random(5);
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / SR, s = 0f;
                    for (int j = 0; j < f.Length; j++) s += Mathf.Sin(2f * Mathf.PI * f[j] * t) * Mathf.Exp(-t * k[j]) / (j + 1);
                    float ruido = ((float)rnd.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 120f);
                    d[i] = (s * 0.5f + ruido * 0.6f) * amp;
                }
                return d;
            }
            return Concat(Golpe(0.8f, 0.18f), Golpe(0.45f, 0.14f), Golpe(0.25f, 0.9f));
        }

        /// <summary>Alarma tipo IEC 60601-1-8: pulsos con armónicos en los instantes dados.</summary>
        static float[] Alarma(float f0, float[] inicios, float durPulso)
        {
            float total = inicios[inicios.Length - 1] + durPulso + 0.05f;
            var d = new float[(int)(total * SR)];
            int np = (int)(durPulso * SR);
            foreach (float t0 in inicios)
            {
                int o = (int)(t0 * SR);
                for (int i = 0; i < np && o + i < d.Length; i++)
                {
                    float t = (float)i / SR;
                    float env = Mathf.Min(1f, i / 300f) * Mathf.Min(1f, (np - i) / 300f);
                    float s = Mathf.Sin(2f * Mathf.PI * f0 * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * f0 * 2f * t)
                            + 0.33f * Mathf.Sin(2f * Mathf.PI * f0 * 3f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * f0 * 4f * t);
                    d[o + i] = s * 0.22f * env;
                }
            }
            return d;
        }

        /// <summary>Murmullo de conversación ininteligible (bucle).</summary>
        static float[] Murmullo(float dur)
        {
            var a = Voces(dur, 3, 31);
            // fundido cruzado del final con el principio para que el bucle no corte
            int x = SR / 4;
            for (int i = 0; i < x; i++) { float k = (float)i / x; a[i] = a[i] * k + a[a.Length - x + i] * (1f - k); }
            System.Array.Resize(ref a, a.Length - x);
            return a;
        }

        /// <summary>
        /// Habla sintética sin palabras: pulso glotal a f0 con dos formantes que
        /// cambian por sílaba (~4-6 Hz) y pausas entre frases.
        /// </summary>
        static float[] Voces(float dur, int voces, int semilla)
        {
            int n = (int)(dur * SR);
            var mezcla = new float[n];
            var rnd = new System.Random(semilla);
            float[] F1 = { 300f, 420f, 550f, 700f }, F2 = { 870f, 1150f, 1600f, 2100f };
            for (int v = 0; v < voces; v++)
            {
                float f0Base = 100f + (float)rnd.NextDouble() * 110f;
                float fase = 0f, y1a = 0, y2a = 0, y1b = 0, y2b = 0, env = 0f;
                int finSilaba = 0; float f1 = 500f, f2 = 1200f, objetivo = 0f;
                for (int i = 0; i < n; i++)
                {
                    if (i >= finSilaba)
                    {
                        bool pausa = rnd.NextDouble() < 0.15;
                        objetivo = pausa ? 0f : 0.6f + (float)rnd.NextDouble() * 0.4f;
                        f1 = F1[rnd.Next(F1.Length)]; f2 = F2[rnd.Next(F2.Length)];
                        finSilaba = i + (int)(SR * (pausa ? 0.25f + (float)rnd.NextDouble() * 0.4f : 0.14f + (float)rnd.NextDouble() * 0.12f));
                    }
                    env += (objetivo - env) * 0.002f;
                    float f0 = f0Base * (1f + 0.06f * Mathf.Sin(2f * Mathf.PI * 3f * i / SR + v));
                    fase += f0 / SR;
                    if (fase >= 1f) fase -= 1f;
                    float x = (fase < 0.08f ? 1f : 0f) - 0.08f + ((float)rnd.NextDouble() - 0.5f) * 0.05f;
                    float ya = Resonador(x, f1, 90f, ref y1a, ref y2a);
                    float yb = Resonador(x, f2, 120f, ref y1b, ref y2b);
                    mezcla[i] += (ya + 0.6f * yb) * env;
                }
            }
            float max = 1e-4f;
            foreach (float s in mezcla) max = Mathf.Max(max, Mathf.Abs(s));
            for (int i = 0; i < n; i++) mezcla[i] *= 0.5f / max;
            return mezcla;
        }

        static float Resonador(float x, float f, float bw, ref float y1, ref float y2)
        {
            float r = Mathf.Exp(-Mathf.PI * bw / SR);
            float c = 2f * r * Mathf.Cos(2f * Mathf.PI * f / SR);
            float y = (1f - r) * x + c * y1 - r * r * y2;
            y2 = y1; y1 = y;
            return y;
        }

        /// <summary>Bip periódico del monitor de signos vitales.</summary>
        class MonitorBeep : MonoBehaviour
        {
            float t;
            void Update()
            {
                t += Time.deltaTime;
                if (t >= 60f / AudioFX.HeartRate) { t = 0f; AudioFX.Beep(); }
            }
        }
    }
}

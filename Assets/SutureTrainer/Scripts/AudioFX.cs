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
            go.AddComponent<MonitorBeep>();
        }

        // ---------- API ----------
        public static void Click() => Play(_click, 0.5f);
        public static void Pop() => Play(_pop, 0.9f);
        public static void Error() => Play(_error, 0.8f);
        public static void Success() => Play(_success, 0.6f);
        public static void Complete() => Play(_complete, 0.8f);
        public static void Tension() => Play(_tension, 0.7f);
        public static void Beep() => Play(_beep, 0.10f);

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

        /// <summary>Bip periódico del monitor de signos vitales.</summary>
        class MonitorBeep : MonoBehaviour
        {
            public float period = 0.92f; // ~65 lpm
            float t;
            void Update()
            {
                t += Time.deltaTime;
                if (t >= period) { t = 0f; AudioFX.Beep(); }
            }
        }
    }
}

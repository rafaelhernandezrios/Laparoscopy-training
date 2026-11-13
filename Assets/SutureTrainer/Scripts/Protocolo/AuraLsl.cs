using System;
using System.Collections.Generic;
using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Conexión al EEG AURA por LSL y cálculo en tiempo real de la carga
    /// cognitiva: razón theta frontal / alfa parietal (versión regional,
    /// F3/Fz/F4 frente a P3/Pz/P4).
    ///
    /// · Busca streams LSL (tipo EEG primero) y se conecta al elegido.
    /// · Mapea canales por etiqueta (desc/channels/channel/label); si no hay
    ///   etiquetas, usa índices configurables (se recuerdan entre sesiones).
    /// · Cada <see cref="paso"/> s analiza la última <see cref="ventana"/> s:
    ///   resta la media, ventana de Hann, FFT, potencia en theta (4-8 Hz) y
    ///   alfa (8-13 Hz). Las ventanas con artefacto (amplitud excesiva) no
    ///   cuentan.
    /// · Score = razón / razón de línea base (1 = reposo de esa persona).
    /// · Publica "SutureTrainer_Carga" (thetaF, alfaP, razón, score) por LSL
    ///   para que quede en el mismo XDF que el EEG y los marcadores.
    /// </summary>
    public class AuraLsl : MonoBehaviour
    {
        public static AuraLsl I { get; private set; }

        public static AuraLsl Asegurar()
        {
            if (I != null) return I;
            var go = new GameObject("[AuraLsl]");
            DontDestroyOnLoad(go);
            return I = go.AddComponent<AuraLsl>();
        }

        [Header("Análisis")]
        public float ventana = 2f;
        public float paso = 0.5f;
        public Vector2 bandaTheta = new Vector2(4f, 8f);
        public Vector2 bandaAlfa = new Vector2(8f, 13f);
        [Tooltip("Amplitud máxima (µV, tras quitar la media) para aceptar una ventana.")]
        public float amplitudMax = 150f;
        [Tooltip("Desviación mínima (µV): por debajo el canal se considera plano/desconectado.")]
        public float desviacionMin = 0.5f;

        public struct StreamDisponible
        {
            public string nombre, tipo, host;
            public int canales;
            public double fs;
            public LSL.StreamInfo info;
        }

        public readonly List<StreamDisponible> Disponibles = new List<StreamDisponible>();
        public bool Conectado => _inlet != null;
        public string NombreStream { get; private set; } = "";
        public int Canales { get; private set; }
        public double Fs { get; private set; }
        public string[] Etiquetas { get; private set; } = new string[0];
        public bool EtiquetasDelStream { get; private set; }
        /// <summary>
        /// Montaje del AURA (orden del arreglo de datos crudos):
        /// 1 Fp1 · 2 Fp2 · 3 F3 · 4 Fz · 5 F4 · 6 P3 · 7 Pz · 8 P4.
        /// Theta frontal = F3/Fz/F4 (índices 2,3,4); alfa parietal = P3/Pz/P4 (5,6,7).
        /// </summary>
        public static readonly string[] MontajeAura = { "Fp1", "Fp2", "F3", "Fz", "F4", "P3", "Pz", "P4" };
        public int[] Frontales = { 2, 3, 4 };
        public int[] Parietales = { 5, 6, 7 };
        /// <summary>true si las etiquetas vienen del montaje AURA (el stream no las trae).</summary>
        public bool EtiquetasDelMontaje { get; private set; }

        // resultados
        public float ThetaF { get; private set; }
        public float AlfaP { get; private set; }
        public float Razon { get; private set; }
        public float Score { get; private set; }
        public float Linea { get; set; } = 1f;     // razón de línea base
        public bool VentanaValida { get; private set; }
        public float[] Desviacion { get; private set; } = new float[0];
        public int MuestrasRecibidas { get; private set; }
        public float SegundosSinDatos => Time.realtimeSinceStartup - _ultimoDato;
        public readonly List<float> Historial = new List<float>();   // score, último ~1 min
        public event Action<float> AlCalcular;                        // score de cada paso

        LSL.StreamInlet _inlet;
        LSL.StreamOutlet _salida;
        float[,] _chunk;
        double[] _ts;
        float[][] _buf;
        int _cap, _w, _lleno;
        float _acum, _ultimoDato;
        int _validas, _totales;

        // registro de bloques (línea base, calibración)
        bool _registrando;
        readonly List<float> _regRazon = new List<float>();
        readonly List<float> _regAlfa = new List<float>();
        int _regTotales;

        public float FraccionValida => _totales > 0 ? (float)_validas / _totales : 0f;
        public int VentanasTotales => _totales;
        public int VentanasValidas => _validas;

        // ------------------------------------------------------------ conexión

        public string Buscar()
        {
            Disponibles.Clear();
            try
            {
                var arr = LSL.LSL.resolve_streams(1.5);
                foreach (var s in arr)
                {
                    Disponibles.Add(new StreamDisponible
                    {
                        nombre = s.name(), tipo = s.type(), host = s.hostname(),
                        canales = s.channel_count(), fs = s.nominal_srate(), info = s
                    });
                }
                // EEG primero
                Disponibles.Sort((a, b) => (b.tipo.ToUpperInvariant() == "EEG").CompareTo(a.tipo.ToUpperInvariant() == "EEG"));
                return Disponibles.Count == 0 ? "No se encontró ningún stream LSL." : $"{Disponibles.Count} stream(s) encontrados.";
            }
            catch (Exception ex) { return "Error buscando streams: " + ex.Message; }
        }

        public string Conectar(int idx)
        {
            if (idx < 0 || idx >= Disponibles.Count) return "Índice inválido.";
            Desconectar();
            try
            {
                var d = Disponibles[idx];
                _inlet = new LSL.StreamInlet(d.info, 360);
                _inlet.open_stream(3.0);
                var full = _inlet.info(2.0);
                NombreStream = d.nombre;
                Canales = full.channel_count();
                Fs = full.nominal_srate() > 0 ? full.nominal_srate() : 250.0;
                LeerEtiquetas(full);
                MapearCanales();

                _cap = Mathf.NextPowerOfTwo(Mathf.CeilToInt((float)Fs * Mathf.Max(ventana, 4f)));
                _buf = new float[Canales][];
                for (int c = 0; c < Canales; c++) _buf[c] = new float[_cap];
                _w = 0; _lleno = 0; _acum = 0f;
                _chunk = new float[64, Canales];
                _ts = new double[64];
                Desviacion = new float[Canales];
                Historial.Clear();
                _validas = _totales = 0;
                MuestrasRecibidas = 0;
                _ultimoDato = Time.realtimeSinceStartup;

                if (_salida == null)
                {
                    var info = new LSL.StreamInfo("SutureTrainer_Carga", "CognitiveLoad", 4, 1.0 / paso,
                                                  LSL.channel_format_t.cf_float32, "suturetrainer_carga");
                    var chs = info.desc().append_child("channels");
                    foreach (var n in new[] { "theta_frontal", "alfa_parietal", "razon_theta_alfa", "score_normalizado" })
                        chs.append_child("channel").append_child_value("label", n);
                    _salida = new LSL.StreamOutlet(info);
                }
                EventMarkers.Send($"eeg/conectado|stream={NombreStream}|canales={Canales}|fs={Fs:0}");
                return $"Conectado a '{NombreStream}' ({Canales} canales, {Fs:0} Hz).";
            }
            catch (Exception ex) { Desconectar(); return "No se pudo conectar: " + ex.Message; }
        }

        public void Desconectar()
        {
            if (_inlet != null) { try { _inlet.close_stream(); _inlet.Dispose(); } catch { } }
            _inlet = null;
        }

        void LeerEtiquetas(LSL.StreamInfo full)
        {
            var et = new string[Canales];
            EtiquetasDelStream = false;
            try
            {
                var ch = full.desc().child("channels").child("channel");
                for (int i = 0; i < Canales && !ch.empty(); i++)
                {
                    string l = ch.child_value("label");
                    if (!string.IsNullOrEmpty(l)) { et[i] = l.Trim(); EtiquetasDelStream = true; }
                    ch = ch.next_sibling();
                }
            }
            catch { }
            // Sin etiquetas en el stream: con 8 canales o más se asume el montaje AURA.
            EtiquetasDelMontaje = !EtiquetasDelStream && Canales >= MontajeAura.Length;
            for (int i = 0; i < Canales; i++)
                if (string.IsNullOrEmpty(et[i]))
                    et[i] = EtiquetasDelMontaje && i < MontajeAura.Length ? MontajeAura[i] : $"Ch{i + 1}";
            Etiquetas = et;
        }

        /// <summary>F3/Fz/F4 y P3/Pz/P4 por etiqueta; si no, los índices guardados.</summary>
        public void MapearCanales()
        {
            var f = Buscar(new[] { "F3", "FZ", "F4" });
            var p = Buscar(new[] { "P3", "PZ", "P4" });
            if (f.Count > 0 && p.Count > 0) { Frontales = f.ToArray(); Parietales = p.ToArray(); return; }
            Frontales = Indices(PlayerPrefs.GetString("SutureTrainer.AuraF", "2,3,4"));
            Parietales = Indices(PlayerPrefs.GetString("SutureTrainer.AuraP", Canales >= 8 ? "5,6,7" : $"{Mathf.Max(0, Canales - 1)}"));
        }

        List<int> Buscar(string[] nombres)
        {
            var r = new List<int>();
            for (int i = 0; i < Etiquetas.Length; i++)
                foreach (var n in nombres)
                    if (string.Equals(Etiquetas[i], n, StringComparison.OrdinalIgnoreCase)) r.Add(i);
            return r;
        }

        int[] Indices(string s)
        {
            var r = new List<int>();
            foreach (var t in s.Split(','))
                if (int.TryParse(t.Trim(), out int v) && v >= 0 && v < Mathf.Max(1, Canales)) r.Add(v);
            return r.Count > 0 ? r.ToArray() : new[] { 0 };
        }

        public void GuardarMapeo(string f, string p)
        {
            PlayerPrefs.SetString("SutureTrainer.AuraF", f);
            PlayerPrefs.SetString("SutureTrainer.AuraP", p);
            PlayerPrefs.Save();
            Frontales = Indices(f);
            Parietales = Indices(p);
        }

        // ------------------------------------------------------------ registro de bloques

        public void EmpezarRegistro() { _registrando = true; _regRazon.Clear(); _regAlfa.Clear(); _regTotales = 0; }

        /// <summary>Termina el bloque: media de la razón, media de alfa, ventanas válidas.</summary>
        public (float razon, float alfa, int validas, int totales) TerminarRegistro()
        {
            _registrando = false;
            return (Media(_regRazon), Media(_regAlfa), _regRazon.Count, _regTotales);
        }

        static float Media(List<float> l) { if (l.Count == 0) return 0f; float s = 0; foreach (var x in l) s += x; return s / l.Count; }

        // ------------------------------------------------------------ bucle

        void Update()
        {
            if (_inlet == null) return;
            try
            {
                int n;
                while ((n = _inlet.pull_chunk(_chunk, _ts, 0.0)) > 0)
                {
                    for (int s = 0; s < n; s++)
                    {
                        for (int c = 0; c < Canales; c++) _buf[c][_w] = _chunk[s, c];
                        _w = (_w + 1) % _cap;
                        if (_lleno < _cap) _lleno++;
                    }
                    MuestrasRecibidas += n;
                    _ultimoDato = Time.realtimeSinceStartup;
                    if (n < _chunk.GetLength(0)) break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AURA] Se perdió el stream: " + ex.Message);
                EventMarkers.Send("eeg/perdido");
                Desconectar();
                return;
            }

            _acum += Time.unscaledDeltaTime;
            if (_acum < paso) return;
            _acum = 0f;
            Analizar();
        }

        void Analizar()
        {
            int nVent = Mathf.Min(_lleno, Mathf.RoundToInt((float)Fs * ventana));
            if (nVent < Fs) return;   // menos de 1 s de datos
            int nfft = Mathf.NextPowerOfTwo(nVent);
            var re = new float[nfft];
            var im = new float[nfft];
            bool valida = true;

            float Banda(int c, Vector2 banda)
            {
                // copia la ventana, quita la media, Hann y FFT
                float media = 0f;
                for (int k = 0; k < nVent; k++) media += _buf[c][(_w - nVent + k + _cap) % _cap];
                media /= nVent;
                float maxAbs = 0f, var = 0f;
                for (int k = 0; k < nfft; k++)
                {
                    float x = 0f;
                    if (k < nVent)
                    {
                        x = _buf[c][(_w - nVent + k + _cap) % _cap] - media;
                        maxAbs = Mathf.Max(maxAbs, Mathf.Abs(x));
                        var += x * x;
                        x *= 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * k / (nVent - 1));
                    }
                    re[k] = x; im[k] = 0f;
                }
                Desviacion[c] = Mathf.Sqrt(var / nVent);
                if (maxAbs > amplitudMax || Desviacion[c] < desviacionMin) valida = false;
                Fft(re, im);
                float df = (float)Fs / nfft, p = 0f;
                int k0 = Mathf.CeilToInt(banda.x / df), k1 = Mathf.FloorToInt(banda.y / df);
                for (int k = Mathf.Max(1, k0); k < k1 && k < nfft / 2; k++) p += re[k] * re[k] + im[k] * im[k];
                return p;
            }

            // desviación de todos los canales (para el monitor de calidad)
            for (int c = 0; c < Canales; c++)
            {
                bool usado = Array.IndexOf(Frontales, c) >= 0 || Array.IndexOf(Parietales, c) >= 0;
                if (!usado) { bool v = valida; Banda(c, bandaTheta); valida = v; }
            }
            float th = 0f, al = 0f;
            foreach (int c in Frontales) th += Banda(c, bandaTheta);
            foreach (int c in Parietales) al += Banda(c, bandaAlfa);
            th /= Frontales.Length; al /= Parietales.Length;

            _totales++;
            if (_registrando) _regTotales++;
            VentanaValida = valida && al > 1e-9f;
            if (!VentanaValida) return;
            _validas++;

            ThetaF = th; AlfaP = al;
            Razon = th / al;
            Score = Razon / Mathf.Max(1e-6f, Linea);
            Historial.Add(Score);
            if (Historial.Count > 120) Historial.RemoveAt(0);
            if (_registrando) { _regRazon.Add(Razon); _regAlfa.Add(al); }
            try { _salida?.push_sample(new[] { th, al, Razon, Score }); } catch { }
            AlCalcular?.Invoke(Score);
        }

        /// <summary>FFT radix-2 in situ (Cooley-Tukey). n debe ser potencia de 2.</summary>
        static void Fft(float[] re, float[] im)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                float ang = -2f * Mathf.PI / len;
                float wr = Mathf.Cos(ang), wi = Mathf.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    float cr = 1f, ci = 0f;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        float tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                        float ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
                    }
                }
            }
        }

        void OnDestroy()
        {
            Desconectar();
            try { _salida?.Dispose(); } catch { }
            if (I == this) I = null;
        }
    }
}

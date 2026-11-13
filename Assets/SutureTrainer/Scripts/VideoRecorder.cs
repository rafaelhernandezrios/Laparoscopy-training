using System.Collections;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Graba la vista Game fotograma a fotograma (JPG) en
    /// &lt;proyecto&gt;/Grabaciones/&lt;fecha&gt;_&lt;etiqueta&gt;/ para montar un MP4 con
    /// ffmpeg. Fija Time.captureFramerate: el tiempo de juego avanza 1/30 s
    /// por fotograma, así el vídeo sale fluido aunque el editor vaya lento.
    /// </summary>
    public class VideoRecorder : MonoBehaviour
    {
        public const int Fps = 30;
        public static VideoRecorder Active { get; private set; }

        string _dir;
        int _frame;

        // Audio: AudioRenderer entrega exactamente el audio de cada fotograma
        // de captura, así que sale sincronizado con el vídeo aunque el editor
        // vaya lento. Se escribe como WAV PCM 16 bits junto a los JPG.
        FileStream _wav;
        BinaryWriter _wavW;
        int _canales, _muestras;
        bool _audio;

        public static void Begin(string etiqueta)
        {
            if (Active != null) return;
            Active = new GameObject("[VideoRecorder]").AddComponent<VideoRecorder>();
            Active.Init(etiqueta);
        }

        /// <summary>
        /// Grabación en TIEMPO REAL (demostración del protocolo): no fija
        /// captureFramerate, así los pasos medidos en tiempo real (línea base,
        /// descansos) duran lo que duran; captura ~fps fotogramas por segundo
        /// con su marca de tiempo (tiempos.txt) para montar un vídeo de duración
        /// real. Incluye los paneles OnGUI (investigador) y sin audio.
        /// </summary>
        public static void BeginTiempoReal(string etiqueta, float fps = 15f)
        {
            if (Active != null) return;
            var go = new GameObject("[VideoRecorder]");
            DontDestroyOnLoad(go);
            Active = go.AddComponent<VideoRecorder>();
            Active._tiempoReal = true;
            Active._fpsReal = fps;
            Active.Init(etiqueta);
        }

        public static bool TiempoReal => Active != null && Active._tiempoReal;
        public static string Carpeta => Active != null ? Active._dir : null;

        bool _tiempoReal;
        float _fpsReal = 15f, _t0Real, _proxReal;
        System.Text.StringBuilder _tiempos;

        public static void End() { if (Active != null) Active.Terminar(); }

        void Init(string etiqueta)
        {
            string raiz = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Grabaciones"));
            _dir = Path.Combine(raiz, System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + etiqueta);
            Directory.CreateDirectory(_dir);
            if (_tiempoReal)
            {
                _t0Real = Time.realtimeSinceStartup;
                _tiempos = new System.Text.StringBuilder();
                StartCoroutine(CapturarTiempoReal());
                Debug.Log($"[Grabación] Grabando en tiempo real ({_fpsReal:0} fps) en {_dir}");
                return;
            }
            Time.captureFramerate = Fps;
            KeyboardControls.Grabando = true;
            IniciarAudio();
            StartCoroutine(Capturar());
            Debug.Log($"[Grabación] Grabando en {_dir}");
        }

        IEnumerator Capturar()
        {
            var fin = new WaitForEndOfFrame();
            while (true)
            {
                yield return fin;
                CapturarAudio();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null) continue;
                File.WriteAllBytes(Path.Combine(_dir, $"f_{_frame:D5}.jpg"), tex.EncodeToJPG(92));
                Destroy(tex);
                _frame++;
            }
        }

        IEnumerator CapturarTiempoReal()
        {
            var fin = new WaitForEndOfFrame();
            while (true)
            {
                yield return fin;
                float t = Time.realtimeSinceStartup;
                if (t < _proxReal) continue;
                _proxReal = t + 1f / _fpsReal;
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null) continue;
                File.WriteAllBytes(Path.Combine(_dir, $"f_{_frame:D5}.jpg"), tex.EncodeToJPG(88));
                Destroy(tex);
                _tiempos.AppendLine(((t - _t0Real)).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
                _frame++;
            }
        }

        void IniciarAudio()
        {
            try
            {
                _canales = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
                _wav = new FileStream(Path.Combine(_dir, "audio.wav"), FileMode.Create);
                _wavW = new BinaryWriter(_wav);
                _wavW.Write(new byte[44]);          // cabecera, se rellena al cerrar
                _audio = AudioRenderer.Start();
                if (!_audio) Debug.LogWarning("[Grabación] AudioRenderer no arrancó: vídeo sin sonido.");
            }
            catch (System.Exception ex)
            {
                _audio = false;
                Debug.LogWarning($"[Grabación] Sin audio: {ex.Message}");
            }
        }

        void CapturarAudio()
        {
            if (!_audio) return;
            int n = AudioRenderer.GetSampleCountForCaptureFrame();
            if (n <= 0) return;
            var buf = new NativeArray<float>(n * _canales, Allocator.Temp);
            if (AudioRenderer.Render(buf))
            {
                for (int i = 0; i < buf.Length; i++)
                    _wavW.Write((short)Mathf.Clamp(Mathf.RoundToInt(buf[i] * 32767f), -32768, 32767));
                _muestras += n;
            }
            buf.Dispose();
        }

        void CerrarAudio()
        {
            if (_wavW == null) return;
            if (_audio) AudioRenderer.Stop();
            _audio = false;
            int sr = AudioSettings.outputSampleRate, datos = _muestras * _canales * 2;
            _wav.Seek(0, SeekOrigin.Begin);
            _wavW.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); _wavW.Write(36 + datos);
            _wavW.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); _wavW.Write(16);
            _wavW.Write((short)1); _wavW.Write((short)_canales); _wavW.Write(sr);
            _wavW.Write(sr * _canales * 2); _wavW.Write((short)(_canales * 2)); _wavW.Write((short)16);
            _wavW.Write(System.Text.Encoding.ASCII.GetBytes("data")); _wavW.Write(datos);
            _wavW.Close();
            _wavW = null; _wav = null;
        }

        void Terminar()
        {
            StopAllCoroutines();
            if (_tiempoReal && _tiempos != null) File.WriteAllText(Path.Combine(_dir, "tiempos.txt"), _tiempos.ToString());
            CerrarAudio();
            Debug.Log($"[Grabación] Terminada: {_frame} fotogramas a {Fps} fps en {_dir}" +
                      (_muestras > 0 ? $" (+ audio {_muestras / (float)AudioSettings.outputSampleRate:0.0} s)" : ""));
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (Active != this) return;
            CerrarAudio();
            if (!_tiempoReal)
            {
                Time.captureFramerate = 0;
                KeyboardControls.Grabando = false;
            }
            Active = null;
        }
    }
}

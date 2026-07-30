using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Alimenta al <see cref="AdaptiveDifficultyController"/> con scores de carga.
    ///
    /// Udp    — recibe del proceso de inferencia (eeg_realtime_infer.py). Un
    ///          datagrama de texto por ventana con el score, p. ej. "1.37".
    /// Replay — reproduce un CSV de scores a ritmo de ventana. Permite demostrar
    ///          el lazo completo sin diadema puesta.
    /// Manual — sólo pruebas: el score se fija desde el inspector.
    /// </summary>
    public class LoadSource : MonoBehaviour
    {
        public enum Mode { Udp, Replay, Manual }

        [Header("Fuente")]
        public Mode mode = Mode.Replay;
        [Tooltip("Periodo de ventana del clasificador (stride), en segundos.")]
        public float strideSec = 2f;

        [Header("UDP")]
        public int port = 5005;

        [Header("Replay")]
        [Tooltip("CSV con una columna de scores. Ruta relativa a Application.streamingAssetsPath " +
                 "o absoluta. Acepta la cabecera de ordinal_predictions.csv.")]
        public string replayFile = "load_scores.csv";
        public string replayColumn = "y_pred";
        public bool replayLoop = false;

        [Header("Manual")]
        [Range(0f, 3f)] public float manualScore = 1.2f;

        public float LastScore { get; private set; }
        public int Delivered { get; private set; }

        UdpClient _udp;
        Thread _rx;
        volatile bool _running;
        readonly object _lock = new object();
        readonly Queue<float> _inbox = new Queue<float>();

        List<float> _replay;
        int _replayIdx;
        float _timer;

        void OnEnable()
        {
            if (mode == Mode.Udp) StartUdp();
            else if (mode == Mode.Replay) LoadReplay();
        }

        void OnDisable() => StopUdp();

        void Update()
        {
            if (mode == Mode.Udp)
            {
                // drena lo recibido en el hilo de red
                while (true)
                {
                    float v;
                    lock (_lock)
                    {
                        if (_inbox.Count == 0) break;
                        v = _inbox.Dequeue();
                    }
                    Deliver(v);
                }
                return;
            }

            _timer += Time.deltaTime;
            if (_timer < strideSec) return;
            _timer -= strideSec;

            if (mode == Mode.Manual) { Deliver(manualScore); return; }

            if (_replay == null || _replay.Count == 0) return;
            if (_replayIdx >= _replay.Count)
            {
                if (!replayLoop) return;
                _replayIdx = 0;
            }
            Deliver(_replay[_replayIdx++]);
        }

        void Deliver(float score)
        {
            LastScore = score;
            Delivered++;
            var ctl = AdaptiveDifficultyController.I;
            if (ctl != null) ctl.SubmitLoadScore(score);
        }

        // ---------------- UDP ----------------

        void StartUdp()
        {
            try
            {
                _udp = new UdpClient(port);
                _running = true;
                _rx = new Thread(RxLoop) { IsBackground = true };
                _rx.Start();
                Debug.Log($"[LoadSource] Escuchando scores de carga en UDP :{port}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[LoadSource] No se pudo abrir UDP :{port} — {ex.Message}");
            }
        }

        void RxLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _udp.Receive(ref any);
                    string s = System.Text.Encoding.ASCII.GetString(data).Trim();
                    if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                        lock (_lock) _inbox.Enqueue(v);
                }
                catch (SocketException) { /* cierre normal */ }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[LoadSource] RX: {ex.Message}");
                }
            }
        }

        void StopUdp()
        {
            _running = false;
            try { _udp?.Close(); } catch { }
            _udp = null;
            _rx = null;
        }

        // ---------------- Replay ----------------

        void LoadReplay()
        {
            string path = Path.IsPathRooted(replayFile)
                ? replayFile
                : Path.Combine(Application.streamingAssetsPath, replayFile);

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[LoadSource] No encuentro el CSV de replay: {path}");
                return;
            }

            _replay = new List<float>();
            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0) return;

            // localiza la columna: por nombre si hay cabecera, si no la última
            string[] head = lines[0].Split(',');
            int col = head.Length - 1;
            bool hasHeader = false;
            for (int i = 0; i < head.Length; i++)
            {
                if (head[i].Trim() == replayColumn) { col = i; hasHeader = true; break; }
            }
            if (!hasHeader && !float.TryParse(head[col], NumberStyles.Float,
                                              CultureInfo.InvariantCulture, out _))
                hasHeader = true;   // cabecera con otro nombre de columna

            for (int i = hasHeader ? 1 : 0; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                string[] parts = lines[i].Split(',');
                if (col >= parts.Length) continue;
                if (float.TryParse(parts[col], NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out float v))
                    _replay.Add(v);
            }
            Debug.Log($"[LoadSource] Replay cargado: {_replay.Count} ventanas de {path}");
        }
    }
}

using System.Collections.Generic;
using System.Text;
using AdvancedRope;
using UnityEngine;
using UnityEngine.XR;

namespace SutureTrainer
{
    /// <summary>
    /// PRUEBA AUTOMÁTICA de la sutura en la escena del órgano (modo teclado).
    ///
    /// Conduce la pinza derecha por el MISMO camino que el teclado (empuja la
    /// pose de MasterInput y su gatillo), así que pasa por RoboticArm, el
    /// agarre de la aguja, SutureRig, OrganRig, el hilo Verlet y la malla
    /// deformable sin atajos:
    ///   1. lleva la pinza a la aguja y la agarra;
    ///   2. para cada punto, en orden entrada→salida: se coloca encima, baja
    ///      la PUNTA DE LA AGUJA hasta el marcador y sube para tirar del hilo;
    ///   3. al terminar, aprieta (OrganRig.Tighten) y mide.
    ///
    /// Cada 2 s escribe en consola el hueco de cada par y el estado del hilo
    /// (nodos anclados, estiramiento máximo, NaN). Se arranca/para con P.
    /// </summary>
    public class SutureAutopilot : MonoBehaviour
    {
        public float speed = 0.07f;        // m/s de la punta
        public float hover = 0.03f;        // altura de aproximación sobre el marcador
        public float pullHeight = 0.07f;   // cuánto sube tras cada punto (tirar del hilo)

        enum Fase { IrAguja, Agarrar, Abrir, Encima, Bajar, Subir, Apretar, Fin }
        Fase _fase;
        RoboticArm _arm;
        MasterInput _master;
        SutureNeedle _needle;
        OrganRig _organ;
        VerletRopeGenerator _rope;
        readonly List<SuturePoint> _seq = new List<SuturePoint>();
        readonly Dictionary<int, float> _huecoInicial = new Dictionary<int, float>();
        int _idx;
        float _t, _nextLog, _restSeg;
        Vector3 _ancla;

        public static SutureAutopilot Running { get; private set; }

        // ---- grabación (Shift+P) ----
        bool _grabar;
        string _carpeta;
        int _frame;
        const int FpsVideo = 30;

        public static void Toggle(bool grabar = false)
        {
            if (Running != null) { Running.Detener("parado con P"); return; }
            if (OrganRig.I == null) { Debug.LogWarning("[Piloto] Esta escena no tiene órgano: abre la actividad 6."); return; }
            var ap = new GameObject("[SutureAutopilot]").AddComponent<SutureAutopilot>();
            ap._grabar = grabar;
        }

        void EmpezarGrabacion()
        {
            string vista = LaparoscopicScene.Centro.HasValue ? "laparoscopica" : "anterior";
            VideoRecorder.Begin("actividad6_" + vista);
            _carpeta = "x";
        }

        void PararGrabacion()
        {
            if (!_grabar || _carpeta == null) return;
            VideoRecorder.End();
            _grabar = false;
        }

        void OnDestroy() { PararGrabacion(); }

        void Start()
        {
            Running = this;
            _organ = OrganRig.I;
            _needle = FindAnyObjectByType<SutureNeedle>();
            _rope = FindAnyObjectByType<VerletRopeGenerator>();
            foreach (var a in FindObjectsByType<RoboticArm>())
                if (a.master != null && a.master.node == XRNode.RightHand) { _arm = a; _master = a.master; }

            if (_organ == null || _needle == null || _arm == null) { Detener("faltan referencias"); return; }

            foreach (var p in _organ.pairs) { _seq.Add(p.entry); _seq.Add(p.exit); }
            for (int i = 0; i < _organ.pairs.Count; i++) _huecoInicial[i] = Hueco(i);

            // longitud de reposo del segmento (campo privado del asset; ropeStartPoint
            // ya no vale porque cambia al atar el hilo a la aguja)
            if (_rope != null)
            {
                var f = typeof(VerletRopeGenerator).GetField("_segmentLength",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null) _restSeg = (float)f.GetValue(_rope);
            }

            KeyboardControls.ActiveHand = XRNode.RightHand;
            if (_grabar) EmpezarGrabacion();
            _fase = _needle.Grasp.IsHeld && _needle.Grasp.Holder == _arm ? Fase.Encima : Fase.IrAguja;
            Debug.Log($"[Piloto] Inicio: {_seq.Count} puntos en {_organ.pairs.Count} pares. " + Medicion());
        }

        void Detener(string motivo)
        {
            Debug.Log($"[Piloto] Fin ({motivo}). " + Medicion());
            PararGrabacion();
            if (_master != null) { _master.KbJawOverride = -1f; }
            ReproduccionHaptica.AlTerminarPiloto();
            if (Running == this) Running = null;
            Destroy(gameObject);
        }

        bool Sostiene => _needle != null && _needle.Grasp.IsHeld && _needle.Grasp.Holder == _arm;
        Vector3 Punta => Sostiene ? _needle.TipPos : _arm.TipPos;

        /// <summary>Mueve la punta hacia target a velocidad limitada. true al llegar.</summary>
        bool Ir(Vector3 target, float tol = 0.002f)
        {
            Vector3 d = target - Punta;
            _master.KbNudge(Vector3.ClampMagnitude(d, speed * Time.deltaTime));
            return d.magnitude < tol;
        }

        void Update()
        {
            if (_arm == null) return;
            _t += Time.deltaTime;

            switch (_fase)
            {
                case Fase.IrAguja:
                {
                    _master.KbJawOverride = 0f;
                    var col = _needle.GetComponent<Collider>();
                    Vector3 obj = col != null ? col.ClosestPoint(_arm.TipPos) : _needle.transform.position;
                    if (Ir(obj, 0.004f)) { _fase = Fase.Agarrar; _t = 0f; }
                    break;
                }
                case Fase.Agarrar:
                    _master.KbJawOverride = 1f;
                    // esperar a que la pinza cierre de verdad (no un tiempo fijo:
                    // a pocos fps 0.6 s son dos fotogramas)
                    if (Sostiene || (_arm.JawCloseness > 0.95f && _t > 0.3f) || _t > 3f)
                    {
                        if (Sostiene) { Debug.Log("[Piloto] Aguja agarrada."); _fase = Fase.Encima; _t = 0f; }
                        else
                        {
                            Debug.LogWarning("[Piloto] No agarró la aguja; reintento. " + DiagnosticoAgarre());
                            _master.KbJawOverride = 0f; _fase = Fase.Abrir; _t = 0f;
                        }
                    }
                    break;

                case Fase.Abrir:   // RoboticArm solo agarra en el flanco abierto→cerrado
                    _master.KbJawOverride = 0f;
                    if (_arm.JawCloseness < 0.2f && _t > 0.3f) { _fase = Fase.IrAguja; _t = 0f; }
                    break;

                case Fase.Encima:
                    _master.KbJawOverride = 1f;
                    if (_idx >= _seq.Count) { _fase = Fase.Apretar; break; }
                    _ancla = _seq[_idx].transform.position;
                    if (Ir(_ancla + Vector3.up * hover, 0.003f)) { _fase = Fase.Bajar; _t = 0f; }
                    break;

                case Fase.Bajar:
                {
                    var sp = _seq[_idx];
                    if (sp.used)
                    {
                        Debug.Log($"[Piloto] {sp.name} suturado ({_idx + 1}/{_seq.Count}). Hilo: {EstadoHilo()}");
                        _fase = Fase.Subir; _t = 0f; break;
                    }
                    Ir(sp.transform.position, 0.001f);
                    if (_t > 4f)
                    {
                        Debug.LogWarning($"[Piloto] {sp.name} no se capturó en 4 s (punta a " +
                                         $"{Vector3.Distance(Punta, sp.transform.position) * 1000f:F0} mm). Lo salto.");
                        _fase = Fase.Subir; _t = 0f;
                    }
                    break;
                }
                case Fase.Subir:
                    if (Ir(_ancla + Vector3.up * pullHeight, 0.004f) || _t > 3f)
                    {
                        _idx++;
                        _fase = Fase.Encima; _t = 0f;
                    }
                    break;

                case Fase.Apretar:
                    _organ.Tighten();
                    Debug.Log("[Piloto] Todos los puntos pasados: aprieto la sutura. " + Medicion());
                    _fase = Fase.Fin; _t = 0f;
                    break;

                case Fase.Fin:
                    if (_t > 8f) Detener("terminado");
                    break;
            }

            if (Time.time >= _nextLog)
            {
                _nextLog = Time.time + 2f;
                Debug.Log($"[Piloto] fase={_fase} · {1f / Mathf.Max(1e-4f, Time.smoothDeltaTime):F0} fps · " + Medicion());
            }
        }

        string DiagnosticoAgarre()
        {
            var sb = new StringBuilder();
            sb.Append($"pinza={_arm.JawCloseness:F2} held={(_arm.Held != null ? _arm.Held.name : "nada")} ");
            var col = _needle.GetComponent<Collider>();
            if (col != null)
                sb.Append($"aguja: collider {(col.enabled ? "on" : "OFF")} trigger={col.isTrigger} " +
                          $"dist={Vector3.Distance(col.ClosestPoint(_arm.TipPos), _arm.TipPos) * 1000f:F0}mm " +
                          $"capa={_needle.gameObject.layer} heldPor={(_needle.Grasp.Holder != null ? _needle.Grasp.Holder.name : "-")} ");
            var hits = Physics.OverlapSphere(_arm.TipPos, 0.035f);
            sb.Append($"| solape r35mm: {hits.Length} [");
            foreach (var h in hits) sb.Append(h.name).Append(',');
            return sb.Append(']').ToString();
        }

        float Hueco(int i)
        {
            var p = _organ.pairs[i];
            return Vector3.Distance(p.entry.transform.position, p.exit.transform.position);
        }

        string Medicion()
        {
            if (_organ == null) return "";
            var sb = new StringBuilder("huecos mm [");
            for (int i = 0; i < _organ.pairs.Count; i++)
            {
                float ini = _huecoInicial.TryGetValue(i, out var h) ? h : Hueco(i);
                sb.Append($"{(i > 0 ? " | " : "")}{ini * 1000f:F0}→{Hueco(i) * 1000f:F1}");
            }
            sb.Append("] · hilo: ").Append(EstadoHilo());
            return sb.ToString();
        }

        string EstadoHilo()
        {
            if (_rope == null || _rope.Nodes.Count < 2) return "sin hilo Verlet";
            int pins = 0; float maxSeg = 0f; bool nan = false;
            var idx = new StringBuilder();
            for (int i = 0; i < _rope.Nodes.Count; i++)
            {
                var n = _rope.Nodes[i];
                if (float.IsNaN(n.position.x) || float.IsInfinity(n.position.x)) nan = true;
                if (n.isPinned) { pins++; if (idx.Length < 60) idx.Append(i).Append(','); }
                if (i > 0) maxSeg = Mathf.Max(maxSeg, Vector3.Distance(n.position, _rope.Nodes[i - 1].position));
            }
            float estir = _restSeg > 0f ? maxSeg / _restSeg : 0f;
            return $"{pins} anclados [{idx.ToString().TrimEnd(',')}] · segmento máx {maxSeg * 1000f:F1} mm " +
                   $"(x{estir:F2} del reposo){(nan ? " · ¡NaN!" : "")}";
        }
    }
}

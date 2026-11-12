using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace SutureTrainer
{
    /// <summary>
    /// Reproducción háptica (F10): el robot Touch de la mano derecha recorre
    /// SOLO la trayectoria ideal de la actividad mientras el alumno sostiene
    /// el stylus y siente el movimiento (demostración pasiva).
    ///
    /// 1. Pasa la mano derecha al Touch (sale del modo teclado si hace falta).
    /// 2. Prueba de signos: empuja ~1 N en cada eje del mundo y mira hacia
    ///    dónde se mueve el stylus. Así la guía nunca empuja al revés aunque
    ///    el marco de fuerzas del aparato no coincida con el de posición.
    /// 3. Activa la guía PD en MasterInput y arranca el piloto automático de la
    ///    actividad; el piloto mueve el objetivo y el robot lo sigue.
    /// 4. Al terminar el piloto (o con Esc / F10) suelta todas las fuerzas.
    ///
    /// Marcadores: guia/inicio, guia/signos, guia/fin.
    /// </summary>
    public class ReproduccionHaptica : MonoBehaviour
    {
        public static ReproduccionHaptica Running { get; private set; }

        [Tooltip("Fuerza de la prueba de signos (N de mundo).")]
        public float fuerzaPrueba = 0.6f;
        public float segundosPrueba = 0.2f;

        MasterInput _der, _izq;
        InstrumentHaptics _hapticaDer;
        bool _hapticaEstaba;

        /// <summary>
        /// Las fuerzas de contacto/trócar (InstrumentHaptics) se apagan mientras
        /// guía el robot: el centrado del trócar tira contra la guía y las dos
        /// fuerzas, recalculadas a la frecuencia de Unity, hacían vibrar el stylus.
        /// </summary>
        public static InstrumentHaptics HapticaDe(MasterInput m)
        {
            foreach (var h in FindObjectsByType<InstrumentHaptics>(FindObjectsSortMode.None))
                if (h.master == m) return h;
            return null;
        }
        bool _pilotoArrancado, _terminando;

        bool _grabar;

        /// <summary>F10 = guía; Shift+F10 = guía y graba el vídeo.</summary>
        public static void Alternar(bool grabar = false)
        {
            if (Running != null) { Running.Detener("parada con F10"); return; }
            new GameObject("[ReproduccionHaptica]").AddComponent<ReproduccionHaptica>()._grabar = grabar;
        }

        /// <summary>La llaman los pilotos al terminar.</summary>
        public static void AlTerminarPiloto()
        {
            if (Running != null && Running._pilotoArrancado && !Running._terminando)
                Running.Detener("trayectoria completada");
        }

        IEnumerator Start()
        {
            Running = this;
            foreach (var m in FindObjectsByType<MasterInput>(FindObjectsSortMode.None))
            {
                if (m.node == XRNode.RightHand) _der = m;
                else if (m.node == XRNode.LeftHand) _izq = m;
            }
            if (_der == null) { Fallo("no hay MasterInput derecho en la escena"); yield break; }

            if (KeyboardControls.Active)
            {
                KeyboardControls.Active = false;
                Debug.Log("[Guia] Modo teclado desactivado para usar el robot.");
            }
            _der.Touch.Reintentar();
            _hapticaDer = HapticaDe(_der);
            if (_hapticaDer != null) { _hapticaEstaba = _hapticaDer.enabled; _hapticaDer.enabled = false; }
            // El instrumento virtual se congela desde ya: al conectar el robot y
            // durante la prueba de signos, si no, entra en el tejido y suma errores.
            RoboticArm brazo = null;
            foreach (var a in FindObjectsByType<RoboticArm>(FindObjectsSortMode.None))
                if (a.master == _der) brazo = a;
            bool congelado = brazo != null && brazo.frozen;
            if (brazo != null) brazo.frozen = true;
            if (_der.ActiveBackend != MasterInput.Backend.TouchHaptic)
                _der.SetBackend(MasterInput.Backend.TouchHaptic);

            for (int i = 0; i < 10; i++) yield return null;
            if (!_der.Touch.Available)
            {
                if (brazo != null) brazo.frozen = congelado;
                Fallo($"el Touch \"{_der.touchDeviceName}\" no responde (¿conectado y con ese nombre en Touch Smart Setup?)");
                yield break;
            }

            Aviso("Guía háptica: sostén el stylus con suavidad");
            yield return new WaitForSecondsRealtime(1.5f);
            yield return PruebaDeSignos();
            if (brazo != null) brazo.frozen = congelado;
            if (_terminando) yield break;

            _der.IniciarGuia();
            if (_izq != null && _izq.ActiveBackend != MasterInput.Backend.TouchHaptic) _izq.IniciarGuia();
            EventMarkers.Send("guia/inicio|modo=reproduccion");

            yield return new WaitForSecondsRealtime(0.5f);
            LevelAutopilot.Lentitud = 2f;   // demostración a media velocidad: el robot la sigue
            bool nivel = LevelAutopilot.Toggle(_grabar);
            if (!nivel) SutureAutopilot.Toggle(_grabar);
            _pilotoArrancado = true;
            Aviso("Guía háptica en curso · Esc para detener");
        }

        IEnumerator PruebaDeSignos()
        {
            Vector3 signos = Vector3.one;
            Vector3[] ejes = { Vector3.right, Vector3.up, Vector3.forward };
            string detalle = "";
            for (int i = 0; i < 3; i++)
            {
                Vector3 p0 = _der.TouchPosMundo;
                _der.FuerzaDePrueba(ejes[i] * fuerzaPrueba);
                yield return new WaitForSecondsRealtime(segundosPrueba);
                Vector3 d = _der.TouchPosMundo - p0;
                _der.FuerzaDePrueba(null);
                yield return new WaitForSecondsRealtime(0.35f);

                float a = Vector3.Dot(d, ejes[i]);
                // 3 mm de mundo ≈ 1.2 mm en el stylus
                if (Mathf.Abs(a) > 0.003f) signos[i] = Mathf.Sign(a);
                detalle += $"{"xyz"[i]}:{a * 1000f:+0.0;-0.0}mm ";

                // volver al punto de partida para la siguiente prueba
                _der.FuerzaDePrueba(Vector3.ClampMagnitude(Vector3.Scale((p0 - _der.TouchPosMundo) * 40f, signos), fuerzaPrueba));
                yield return new WaitForSecondsRealtime(0.25f);
                _der.FuerzaDePrueba(null);
                if (_terminando) yield break;
            }
            if (detalle.Contains("x:+0.0mm") && detalle.Contains("y:+0.0mm") && detalle.Contains("z:+0.0mm"))
                Debug.LogWarning("[Guia] El stylus no se movió en la prueba de signos (¿está en su base o sujeto?). " +
                                 "Se usan los signos por defecto.");
            _der.guiaSigno = signos;
            Debug.Log($"[Guia] Prueba de signos: {detalle}-> signos ({signos.x:+0;-0}, {signos.y:+0;-0}, {signos.z:+0;-0})");
            EventMarkers.Send($"guia/signos|x={signos.x:0}|y={signos.y:0}|z={signos.z:0}");
            if (signos.x < 0 || signos.y < 0 || signos.z < 0)
                Debug.LogWarning("[Guia] Algún eje de fuerza sale invertido respecto a la posición: la guía lo corrige, " +
                                 "pero revisa AxisSign en TouchDevice (afecta también a las fuerzas de contacto).");
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Detener("parada de emergencia (Esc)");
            // Seguridad: si el error crece mucho (el robot bloqueado o el alumno lo frena), se suelta.
            if (_der != null && _der.GuiaActiva && _der.GuiaError > _der.GuiaCorreaMundo * 3f)
                Detener("error de seguimiento excesivo");
        }

        void Detener(string motivo)
        {
            if (_terminando) return;
            _terminando = true;
            LevelAutopilot.Lentitud = 1f;
            if (_hapticaDer != null) _hapticaDer.enabled = _hapticaEstaba;
            if (_der != null) { _der.FuerzaDePrueba(null); _der.DetenerGuia(); }
            if (_izq != null) _izq.DetenerGuia();
            if (LevelAutopilot.Running != null) LevelAutopilot.Toggle(false);
            if (SutureAutopilot.Running != null) SutureAutopilot.Toggle(false);
            EventMarkers.Send($"guia/fin|motivo={motivo.Replace(' ', '_')}");
            Debug.Log($"[Guia] Fin: {motivo}.");
            Aviso("Guía háptica detenida: " + motivo);
            if (Running == this) Running = null;
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (Running == this) Running = null;
            if (_hapticaDer != null && !_hapticaDer.enabled) _hapticaDer.enabled = _hapticaEstaba;
            if (!_terminando)
            {
                if (_der != null) { _der.FuerzaDePrueba(null); _der.DetenerGuia(); }
                if (_izq != null) _izq.DetenerGuia();
            }
        }

        void Fallo(string motivo)
        {
            Debug.LogError("[Guia] No se puede iniciar: " + motivo);
            if (_hapticaDer != null) _hapticaDer.enabled = _hapticaEstaba;
            Aviso("Guía háptica: " + motivo);
            _terminando = true;
            if (Running == this) Running = null;
            Destroy(gameObject);
        }

        static void Aviso(string msg)
        {
            var hud = FindAnyObjectByType<HUD>();
            if (hud != null) hud.Flash(msg, new Color(0.5f, 0.85f, 1f));
        }
    }
}

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SutureTrainer
{
    /// <summary>
    /// Eje de DISTRACTORES, independiente de la dificultad de la tarea
    /// (AdaptiveDifficultyController mueve tiempo, precisión y errores; esto
    /// añade carga extrínseca sin cambiar la tarea).
    ///
    /// Nivel 0-3: ninguno, leve, medio, alto. Controla:
    ///   · sangrado en las punciones (probabilidad, tamaño, si para solo),
    ///   · sonidos de quirófano (teléfono, instrumental, alarmas, conversación),
    ///   · signos vitales del monitor (frecuencia cardiaca y tono de SpO2).
    ///
    /// Modo adaptativo: cada <see cref="evalSec"/> s sube o baja un nivel.
    /// Con EEG (LoadSource entregando ventanas) usa la zona de carga del
    /// AdaptiveDifficultyController; sin EEG, los errores recientes.
    ///
    /// F6 = cambiar nivel · F7 = fijo/adaptativo (funciona también con visor).
    /// Cada cambio y cada distractor se marca en LSL y en eventos_*.csv.
    /// </summary>
    public class DistractorManager : MonoBehaviour
    {
        public static DistractorManager I { get; private set; }

        const string KeyNivel = "SutureTrainer.Distractores";
        const string KeyAdapt = "SutureTrainer.DistractoresAdaptativo";

        public static int NivelPref
        {
            get => PlayerPrefs.GetInt(KeyNivel, 0);
            set { PlayerPrefs.SetInt(KeyNivel, Mathf.Clamp(value, 0, 3)); PlayerPrefs.Save(); }
        }

        public static bool AdaptativoPref
        {
            get => PlayerPrefs.GetInt(KeyAdapt, 0) == 1;
            set { PlayerPrefs.SetInt(KeyAdapt, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        [Tooltip("Periodo de decisión del modo adaptativo (s).")]
        public float evalSec = 20f;
        public bool mostrarIndicador = true;

        public int Nivel { get; private set; }
        public bool Adaptativo { get; private set; }

        // ----- parámetros por nivel (0..3) -----
        static readonly float[] ProbSangrado = { 0f, 0.35f, 0.65f, 1f };
        static readonly float[] RadioSangrado = { 0f, 0.011f, 0.017f, 0.025f };
        static readonly float[] AutoStopSangrado = { 0f, 8f, 16f, float.PositiveInfinity };
        static readonly Vector2[] IntervaloSonido = { Vector2.zero, new Vector2(35, 60), new Vector2(18, 32), new Vector2(8, 16) };
        static readonly float[] Fc = { 68f, 78f, 98f, 126f };
        static readonly float[] SpO2 = { 98f, 97f, 94f, 89f };
        static readonly float[] Murmullo = { 0f, 0.02f, 0.06f, 0.12f };
        static readonly string[][] Sonidos =
        {
            new string[0],
            new[] { "telefono", "instrumento" },
            new[] { "telefono", "instrumento", "alarma_media", "voz", "humo", "empanado" },
            new[] { "telefono", "instrumento", "alarma_media", "alarma_alta", "voz", "humo", "empanado" },
        };

        float _proxSonido, _proxEval;
        Vector3? _ultimaPunzada;
        public TareaSecundaria Tarea2 { get; private set; }
        TrainingLevel _nivelActual;
        int _erroresEval;

        // ------------------------------------------------------------ instalación

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded -= AlCargar;
            SceneManager.sceneLoaded += AlCargar;
            Instalar();
        }

        static void AlCargar(Scene s, LoadSceneMode m) => Instalar();

        static void Instalar()
        {
            if (I != null && I.gameObject.scene.isLoaded) return;
            bool actividad = FindAnyObjectByType<TrainingLevel>() != null || FindAnyObjectByType<DeformableMesh>() != null;
            if (!actividad)
            {
                AudioFX.SetVitals(Fc[0], SpO2[0]);
                AudioFX.SetChatter(0f);
                return;
            }
            new GameObject("[Distractores]").AddComponent<DistractorManager>();
        }

        void Awake()
        {
            I = this;
            Nivel = NivelPref;            // antes de que el nivel marque su inicio
            Adaptativo = AdaptativoPref;
            Tarea2 = gameObject.AddComponent<TareaSecundaria>();
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            foreach (var s in Sangrado.Activos.ToArray()) if (s != null) Destroy(s.gameObject);
        }

        void Start()
        {
            Adaptativo = AdaptativoPref;
            Aplicar(NivelPref, "inicio");
        }

        // ------------------------------------------------------------ API

        public void Aplicar(int nivel, string motivo)
        {
            Nivel = Mathf.Clamp(nivel, 0, 3);
            AudioFX.SetVitals(Fc[Nivel], SpO2[Nivel]);
            AudioFX.SetChatter(Murmullo[Nivel]);
            _proxSonido = Time.time + Aleatorio(IntervaloSonido[Nivel]) * 0.5f;
            _proxEval = Time.time + evalSec;
            _erroresEval = MetricsRecorder.I != null ? MetricsRecorder.I.ErrorCount : 0;
            EventMarkers.Send($"distractores/nivel={Nivel}|modo={(Adaptativo ? "adaptativo" : "fijo")}|motivo={motivo}");
        }

        /// <summary>La llaman las dianas y el órgano al registrar una punción.</summary>
        public static void Punzada(Vector3 punto)
        {
            var dm = I;
            if (dm != null) dm._ultimaPunzada = punto;
            if (dm == null || dm.Nivel == 0) return;
            if (Random.value > ProbSangrado[dm.Nivel]) return;
            int vivos = 0;
            foreach (var s in Sangrado.Activos) if (s != null && !s.Detenido) vivos++;
            if (vivos >= 2) return;
            Sangrado.Crear(punto, RadioSangrado[dm.Nivel], AutoStopSangrado[dm.Nivel]);
            EventMarkers.Send($"distractor/sangrado/inicio|nivel={dm.Nivel}");
            var hud = FindAnyObjectByType<HUD>();
            if (hud != null) hud.Flash("Sangrado: comprime con la pinza", new Color(1f, 0.3f, 0.25f));
        }

        // ------------------------------------------------------------ bucle

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f6Key.wasPressedThisFrame)
                {
                    NivelPref = (Nivel + 1) % 4;
                    Aplicar(NivelPref, "manual");
                }
                if (kb.f10Key.wasPressedThisFrame)
                    ReproduccionHaptica.Alternar(kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
                if (kb.f11Key.wasPressedThisFrame) PruebaResorte.Lanzar();
                if (kb.f12Key.wasPressedThisFrame)
                {
                    MasterInput.OrientacionTouch = !MasterInput.OrientacionTouch;
                    string m = MasterInput.OrientacionTouch ? "Orientación del Touch: GIMBAL del stylus"
                                                            : "Orientación del Touch: TECLADO (I/K J/L U/O)";
                    Debug.Log("[Touch] " + m);
                    var hud2 = FindAnyObjectByType<HUD>();
                    if (hud2 != null) hud2.Flash(m, new Color(0.5f, 0.85f, 1f));
                }
                if (kb.f9Key.wasPressedThisFrame)
                {
                    string r = EventMarkers.Verificar();
                    Debug.Log("[Marcadores] Verificación: " + r);
                    var hud = FindAnyObjectByType<HUD>();
                    if (hud != null) hud.Flash("LSL: " + r, r.StartsWith("OK") ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.5f, 0.3f));
                }
                if (kb.f7Key.wasPressedThisFrame)
                {
                    Adaptativo = !Adaptativo;
                    AdaptativoPref = Adaptativo;
                    Aplicar(Nivel, "cambio_modo");
                }
            }

            if (_nivelActual == null) _nivelActual = FindAnyObjectByType<TrainingLevel>();
            bool terminado = _nivelActual != null && _nivelActual.Finished;
            if (terminado) { AudioFX.SetChatter(0f); return; }   // nada en la pantalla de resultados
            if (Nivel > 0 && Time.time >= _proxSonido)
            {
                var lista = Sonidos[Nivel];
                string s = lista[Random.Range(0, lista.Length)];
                if (s == "humo")
                {
                    DistractoresVisuales.Humo(OrigenHumo());
                    EventMarkers.Send($"distractor/visual/humo|nivel={Nivel}");
                }
                else if (s == "empanado")
                    DistractoresVisuales.Empanar(Nivel >= 3 ? 7f : 3.5f);
                else
                {
                    AudioFX.Distractor(s);
                    EventMarkers.Send($"distractor/sonido/{s}|nivel={Nivel}");
                }
                _proxSonido = Time.time + Aleatorio(IntervaloSonido[Nivel]);
            }

            if (Adaptativo && Time.time >= _proxEval) Evaluar();
        }

        void Evaluar()
        {
            _proxEval = Time.time + evalSec;
            var m = MetricsRecorder.I;
            if (m == null || !m.Running) return;

            int nuevos = m.ErrorCount - _erroresEval;
            _erroresEval = m.ErrorCount;

            int delta = 0; string motivo = null;
            var ctl = AdaptiveDifficultyController.I;
            var fuente = FindAnyObjectByType<LoadSource>();
            bool hayEeg = ctl != null && fuente != null && fuente.isActiveAndEnabled && fuente.Delivered > 0;
            if (hayEeg)
            {
                if (ctl.CurrentZone == AdaptiveDifficultyController.Zone.Baja) { delta = +1; motivo = "eeg_baja"; }
                else if (ctl.CurrentZone == AdaptiveDifficultyController.Zone.Alta) { delta = -1; motivo = "eeg_alta"; }
            }
            else
            {
                if (nuevos == 0) { delta = +1; motivo = "sin_errores"; }
                else if (nuevos >= 2) { delta = -1; motivo = $"errores_{nuevos}"; }
            }
            int n = Mathf.Clamp(Nivel + delta, 0, 3);
            if (n != Nivel) Aplicar(n, motivo);
        }

        Vector3 OrigenHumo()
        {
            if (_ultimaPunzada.HasValue) return _ultimaPunzada.Value + Vector3.up * 0.004f;
            if (_nivelActual != null && _nivelActual.fieldRoot != null) return _nivelActual.fieldRoot.position + Vector3.up * 0.01f;
            if (LaparoscopicScene.Centro.HasValue) return LaparoscopicScene.Centro.Value;
            return transform.position;
        }

        static float Aleatorio(Vector2 r) => r == Vector2.zero ? 9999f : Random.Range(r.x, r.y);

        void OnGUI()
        {
            if (!mostrarIndicador || KeyboardControls.Grabando || ProtocoloInvestigador.PanelVisible) return;
            GUI.depth = -100;   // por encima de la viñeta del endoscopio
            string txt = $"Distractores: {Nivel}{(Adaptativo ? " (adaptativo)" : "")}  ·  F6 nivel  F7 modo" +
                         $"  ·  Tarea 2: {(Tarea2 != null && Tarea2.Activa ? "sí" : "no")} (F8)" +
                         (EventMarkers.LslActivo ? "  ·  LSL" : "");
            var st = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperRight };
            st.normal.textColor = Nivel == 0 ? new Color(0.8f, 0.8f, 0.8f) : new Color(1f, 0.6f, 0.4f);
            GUI.Label(new Rect(Screen.width - 610, 8, 600, 22), txt, st);
        }
    }
}

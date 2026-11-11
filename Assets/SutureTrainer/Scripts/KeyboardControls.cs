using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace SutureTrainer
{
    /// <summary>
    /// Modo de PRUEBAS sin visor ni Touch: las pinzas se manejan con teclado y
    /// mouse desde la ventana Game.
    ///
    /// No toca la lógica de niveles: MasterInput sigue publicando WorldPos,
    /// WorldRot, Trigger y Secondary, solo que leídos del teclado en vez del
    /// mando. Por eso todo lo que cuelga de MasterInput (RoboticArm, agarre,
    /// SutureRig, niveles) funciona igual.
    ///
    /// Se activa/desactiva en SutureTrainer → "Controles: teclado y mouse
    /// (pruebas)" (persistente) o con F3 durante el juego. Para volver a los
    /// controles reales basta con desmarcarlo; en una build está apagado.
    /// </summary>
    public class KeyboardControls : MonoBehaviour
    {
        public const string PrefKey = "SutureTrainer.KeyboardControls";

        public static bool Active { get; set; }
        public static XRNode ActiveHand = XRNode.RightHand;

        public static float MoveSpeed = 0.08f;          // m/s
        public static float RotSpeed = 60f;             // grados/s
        public static float MouseSensitivity = 0.0003f; // m por píxel
        public static float ScrollStep = 0.004f;        // m por muesca

        static bool _latchR, _latchL;

        /// <summary>Durante una grabación no se pinta texto en pantalla.</summary>
        public static bool Grabando;

        // ---- cámara de pruebas sobre el campo quirúrgico ----
        static Camera _cam;
        static int _vista;   // 0 = endoscopio, 1 = cerca, 2 = cenital, 3 = cámara del visor
        static readonly string[] NombreVista = { "endoscopio", "cerca", "cenital", "visor" };
        const int VistaVisor = 3;
        static Light _luzEndoscopio;
        static Texture2D _vineta, _negro;

        /// <summary>Cámara con la que se orientan los movimientos del teclado.</summary>
        public static Camera ViewCamera => _cam != null && _cam.enabled ? _cam : Camera.main;
        static bool _showHelp = true;

        public static void ReportLatch(XRNode hand, bool latched)
        {
            if (hand == XRNode.LeftHand) _latchL = latched; else _latchR = latched;
        }

#if UNITY_EDITOR
        public static bool Pref
        {
            get => UnityEditor.EditorPrefs.GetBool(PrefKey, true);
            set => UnityEditor.EditorPrefs.SetBool(PrefKey, value);
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
#if UNITY_EDITOR
            Active = Pref;
#else
            Active = false;
#endif
            ActiveHand = XRNode.RightHand;
            var go = new GameObject("[KeyboardControls]");
            DontDestroyOnLoad(go);
            go.AddComponent<KeyboardControls>();
            if (Active)
                Debug.Log("[Teclado] Modo teclado y mouse ACTIVO (F3 o menú SutureTrainer para volver a los controles).");
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.f3Key.wasPressedThisFrame)
            {
                Active = !Active;
                Debug.Log($"[Teclado] Modo teclado y mouse {(Active ? "ACTIVO" : "apagado: controles reales")}.");
            }
            if (!Active) return;

            if (kb.tabKey.wasPressedThisFrame)
                ActiveHand = ActiveHand == XRNode.RightHand ? XRNode.LeftHand : XRNode.RightHand;
            if (kb.hKey.wasPressedThisFrame) _showHelp = !_showHelp;
            if (kb.vKey.wasPressedThisFrame) { _vista = (_vista + 1) % 4; }
            if (kb.pKey.wasPressedThisFrame)
            {
                bool grabar = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                // actividades 1-5: piloto del nivel; actividad 6: piloto del órgano
                if (!LevelAutopilot.Toggle(grabar)) SutureAutopilot.Toggle(grabar);
            }
            ActualizarCamara();

            // Navegación: sustituye al láser del mando en menú y resultados.
            for (int i = 0; i < 7; i++)
            {
                if (!kb[Key.Digit1 + i].wasPressedThisFrame) continue;
                GameFlow.Load(i < 5 ? GameFlow.Scenes[i + 1] : i == 5 ? GameFlow.OrganScene : GameFlow.BimanualScene);
                return;
            }
            if (kb.lKey.wasPressedThisFrame && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed))
            {
                // Ctrl+L (L sola gira la muñeca): alterna vista laparoscópica / anterior
                LaparoscopicScene.Pref = !LaparoscopicScene.Pref;
                Debug.Log($"[Laparoscopia] Vista laparoscópica {(LaparoscopicScene.Pref ? "activada" : "desactivada")}.");
                GameFlow.Reload();
                return;
            }
            if (kb.mKey.wasPressedThisFrame) GameFlow.LoadMenu();
            else if (kb.f5Key.wasPressedThisFrame) GameFlow.Reload();
            else if (kb.nKey.wasPressedThisFrame) GameFlow.LoadNext();
        }

        /// <summary>
        /// Sin visor, la cámara XR se queda a ras de mesa y apenas se ve la
        /// herida. Esta cámara solo existe en modo teclado y se pinta encima.
        /// </summary>
        static void ActualizarCamara()
        {
            bool quiero = Active && _vista != VistaVisor;
            if (!quiero) { if (_cam != null) _cam.enabled = false; return; }

            if (_cam == null)
            {
                var go = new GameObject("[CamaraPruebas]");
                _cam = go.AddComponent<Camera>();
                _cam.nearClipPlane = 0.01f;
                _cam.fieldOfView = 45f;
                _cam.depth = 100f;          // por encima de la cámara del visor
                _cam.stereoTargetEye = StereoTargetEyeMask.None;
            }
            _cam.enabled = true;

            Vector3 c;
            if (OrganRig.I != null) c = OrganRig.I.WoundCenter;
            else
            {
                var campo = GameObject.Find("SurgicalField");
                // actividades 1-2: el ejercicio ocurre a 10-18 cm sobre el campo
                c = campo != null ? campo.transform.position + Vector3.up * 0.13f : new Vector3(0f, 1.2f, 0.55f);
            }
            // el endoscopio solo tiene sentido donde hay cavidad (actividades 3-6)
            bool endo = _vista == 0 && LaparoscopicScene.Centro.HasValue;
            bool cenital = _vista == 2;
            Vector3 pos = endo ? c + new Vector3(0f, 0.16f, -0.21f)
                        : cenital ? c + new Vector3(0f, 0.45f, -0.01f)
                        : c + new Vector3(0f, 0.30f, -0.32f);
            _cam.fieldOfView = endo ? 72f : 45f;
            _cam.transform.position = pos;
            _cam.transform.rotation = Quaternion.LookRotation(c - pos, cenital ? Vector3.forward : Vector3.up);

            // luz del endoscopio: sale de la propia óptica, como en el monitor real
            if (_luzEndoscopio == null)
            {
                var lg = new GameObject("LuzEndoscopio");
                lg.transform.SetParent(_cam.transform, false);
                _luzEndoscopio = lg.AddComponent<Light>();
                _luzEndoscopio.type = LightType.Spot;
                _luzEndoscopio.spotAngle = 95f;
                _luzEndoscopio.innerSpotAngle = 40f;
                _luzEndoscopio.range = 1.2f;
                _luzEndoscopio.intensity = 1.2f;
                _luzEndoscopio.color = new Color(1f, 0.96f, 0.90f);
                _luzEndoscopio.shadows = LightShadows.Soft;
            }
            _luzEndoscopio.enabled = endo;
        }

        /// <summary>Máscara circular negra de la óptica del endoscopio.</summary>
        static Texture2D CrearVineta()
        {
            const int N = 512;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N - 0.5f, dy = (y + 0.5f) / N - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    // borde ancho y suave: oscurece hacia fuera y corta en negro
                    float k = Mathf.Clamp01((r - 0.36f) / (0.495f - 0.36f));
                    float a = k * k * (3f - 2f * k);   // smoothstep de verdad (Mathf.SmoothStep interpola, no es esto)
                    px[y * N + x] = new Color(0f, 0f, 0f, a);
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        void OnGUI()
        {
            if (!Active) return;

            var estilo = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 13,
                richText = true,
                wordWrap = true
            };
            estilo.normal.textColor = Color.white;

            if (_vista == 0 && LaparoscopicScene.Centro.HasValue && _cam != null && _cam.enabled)
            {
                if (_vineta == null) _vineta = CrearVineta();
                if (_negro == null)
                {
                    _negro = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    _negro.SetPixel(0, 0, Color.black); _negro.Apply();
                }
                // la imagen circular ocupa la altura (algo recortada arriba y
                // abajo), con bandas negras a los lados, como en el monitor
                float lado = Mathf.Min(Screen.width, Screen.height * 1.6f);
                float x0 = (Screen.width - lado) * 0.5f;
                GUI.DrawTexture(new Rect(x0, (Screen.height - lado) * 0.5f, lado, lado), _vineta);
                if (x0 > 0f)
                {
                    GUI.DrawTexture(new Rect(0, 0, x0 + 1, Screen.height), _negro);
                    GUI.DrawTexture(new Rect(Screen.width - x0 - 1, 0, x0 + 1, Screen.height), _negro);
                }
            }

            if (Grabando) return;   // vídeo limpio: solo la imagen (y la viñeta)

            bool der = ActiveHand == XRNode.RightHand;
            bool latch = der ? _latchR : _latchL;
            string cab = $"<b>MODO TECLADO</b> · mano <b>{(der ? "DERECHA" : "IZQUIERDA")}</b> (Tab)" +
                         (latch ? " · pinza <b>CERRADA</b>" : "");

            if (!_showHelp)
            {
                GUI.Box(new Rect(10, 10, 430, 26), cab + " · H ayuda", estilo);
                return;
            }

            string txt = cab + "\n" +
                "Mover: W/S adelante-atrás · A/D lados · E/Q subir-bajar\n" +
                "        clic derecho + arrastrar · rueda = profundidad\n" +
                "Girar muñeca: I/K cabeceo · J/L giro · U/O alabeo\n" +
                "Shift = movimiento fino · Retroceso = reiniciar mano\n" +
                "Pinza: clic izquierdo o F (mantener) · C = dejarla cerrada\n" +
                "Tensar sutura: T o Espacio · V cámara (" + NombreVista[_vista] + ")\n" +
                "P = piloto automático (todas) · Shift+P = piloto + grabar vídeo\n" +
                "1-7 actividades · M menú · F5 reintentar · N siguiente · Ctrl+L vista laparoscópica\n" +
                "F6 nivel de distractores (0-3) · F7 distractores fijos/adaptativos · F8 tarea secundaria (responder: R o grip)\n" +
                "F3 volver a controles reales · H ocultar";
            GUI.Box(new Rect(10, 10, 430, 205), txt, estilo);
        }
    }
}

using TMPro;
using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Panel de mensajes para el PARTICIPANTE dentro del visor (espacio de
    /// mundo): instrucciones, cruz de fijación de la línea base, escalas del
    /// NASA-TLX, descansos. Sigue la mirada con retardo (no va pegado a la
    /// cabeza, que marea). El panel del investigador (OnGUI) no se ve en el
    /// visor; este sí.
    /// </summary>
    public class MensajeParticipante : MonoBehaviour
    {
        static MensajeParticipante _i;
        TextMeshPro _titulo, _cuerpo;
        Transform _panel;
        Camera _cam;
        bool _visible;
        string _ultT = "", _ultC = "";
        bool _ultF;

        public static void Mostrar(string titulo, string cuerpo, bool fijacion = false)
        {
            Asegurar();
            _i._ultT = titulo; _i._ultC = cuerpo; _i._ultF = fijacion;
            if (_i._panel == null) { _i._cam = null; _i.Construir(); }   // la cámara anterior se destruyó con su escena
            _i._titulo.text = titulo;
            _i._cuerpo.text = cuerpo;
            _i._cuerpo.fontSize = fijacion ? 3f : 0.42f;
            _i._visible = true;
            _i._panel.gameObject.SetActive(true);
            _i.Colocar(true);
        }

        public static void Ocultar()
        {
            if (_i == null) return;
            _i._visible = false;
            if (_i._panel != null) _i._panel.gameObject.SetActive(false);
        }

        static void Asegurar()
        {
            if (_i != null) return;
            var go = new GameObject("[MensajeParticipante]");
            DontDestroyOnLoad(go);
            _i = go.AddComponent<MensajeParticipante>();
            _i.Construir();
        }

        void Construir()
        {
            _panel = new GameObject("Panel").transform;
            _panel.SetParent(transform, false);

            var fondo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(fondo.GetComponent<Collider>());
            fondo.transform.SetParent(_panel, false);
            fondo.transform.localScale = new Vector3(0.8f, 0.5f, 1f);
            fondo.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            var mat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.04f, 0.06f, 0.09f, 0.88f) };
            var mr = fondo.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Tamaños de TextMeshPro en mundo: ~0.045 m de letra por punto.
            _titulo = Texto("Titulo", new Vector3(0f, 0.17f, 0f), 0.6f, new Color(0.65f, 0.85f, 1f));
            _cuerpo = Texto("Cuerpo", new Vector3(0f, -0.04f, 0f), 0.42f, Color.white);
            _cuerpo.rectTransform.sizeDelta = new Vector2(0.74f, 0.32f);
            _panel.gameObject.SetActive(false);
        }

        TextMeshPro Texto(string n, Vector3 pos, float tam, Color c)
        {
            var go = new GameObject(n);
            go.transform.SetParent(_panel, false);
            go.transform.localPosition = pos;
            var t = go.AddComponent<TextMeshPro>();
            t.fontSize = tam;
            t.color = c;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = true;
            t.rectTransform.sizeDelta = new Vector2(0.74f, 0.1f);
            return t;
        }

        void LateUpdate()
        {
            if (!_visible) return;
            if (_panel == null) { Mostrar(_ultT, _ultC, _ultF); return; }
            Colocar(false);
        }

        /// <summary>
        /// El panel va hijo de la cámara que se está viendo, 1 m al frente y
        /// centrado: así la cruz de fijación queda fija en el campo visual (lo
        /// que pide una línea base) y se ve igual en el visor que en la vista Game.
        /// </summary>
        void Colocar(bool inmediato)
        {
            var cam = DistractoresVisuales.CamaraActiva();
            if (cam == null) return;
            if (cam != _cam || _panel.parent != cam.transform)
            {
                _cam = cam;
                _panel.SetParent(cam.transform, false);
                _panel.localRotation = Quaternion.identity;
                Debug.Log($"[Participante] Mensajes delante de la cámara '{cam.name}' (profundidad {cam.depth}).");
            }
            // Con visor: a 1 m (distancia cómoda). En el escritorio (modo teclado) la
            // cámara va pegada a la mesa: a 0.35 m y escalado, para que nada lo tape,
            // y corrido a la izquierda si el panel del investigador ocupa la derecha.
            bool escritorio = KeyboardControls.Active;
            float d = escritorio ? 0.35f : 1.0f;
            float dx = escritorio && ProtocoloInvestigador.PanelVisible ? -0.22f * d : 0f;
            _panel.localPosition = new Vector3(dx, 0f, Mathf.Max(cam.nearClipPlane + 0.05f, d));
            _panel.localScale = Vector3.one * d;
        }

        void OnDestroy()
        {
            if (_panel != null) Destroy(_panel.gameObject);   // por si quedó colgado de una cámara
        }
    }
}

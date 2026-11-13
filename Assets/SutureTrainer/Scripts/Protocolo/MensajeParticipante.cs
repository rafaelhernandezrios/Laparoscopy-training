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

        public static void Mostrar(string titulo, string cuerpo, bool fijacion = false)
        {
            Asegurar();
            _i._titulo.text = titulo;
            _i._cuerpo.text = cuerpo;
            _i._cuerpo.fontSize = fijacion ? 14f : 2.2f;
            _i._visible = true;
            _i._panel.gameObject.SetActive(true);
            _i.Colocar(true);
        }

        public static void Ocultar()
        {
            if (_i == null) return;
            _i._visible = false;
            _i._panel.gameObject.SetActive(false);
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
            fondo.transform.localScale = new Vector3(1.3f, 0.8f, 1f);
            fondo.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            var mat = new Material(Shader.Find("Sprites/Default")) { color = new Color(0.04f, 0.06f, 0.09f, 0.88f) };
            var mr = fondo.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _titulo = Texto("Titulo", new Vector3(0f, 0.3f, 0f), 3.2f, new Color(0.65f, 0.85f, 1f));
            _cuerpo = Texto("Cuerpo", new Vector3(0f, -0.04f, 0f), 2.2f, Color.white);
            _cuerpo.rectTransform.sizeDelta = new Vector2(1.2f, 0.6f);
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
            t.rectTransform.sizeDelta = new Vector2(1.2f, 0.15f);
            return t;
        }

        void LateUpdate() { if (_visible) Colocar(false); }

        void Colocar(bool inmediato)
        {
            var cam = DistractoresVisuales.CamaraActiva();
            if (cam == null) return;
            _cam = cam;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = cam.transform.forward;
            fwd.Normalize();
            Vector3 objetivo = cam.transform.position + fwd * 1.4f;
            Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
            float k = inmediato ? 1f : 1f - Mathf.Pow(0.02f, Time.unscaledDeltaTime);
            _panel.position = Vector3.Lerp(_panel.position, objetivo, k);
            _panel.rotation = Quaternion.Slerp(_panel.rotation, rot, k);
        }
    }
}

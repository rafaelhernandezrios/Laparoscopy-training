using System.Collections.Generic;
using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Distractores visuales en espacio de mundo (funcionan igual en el visor
    /// que en pantalla, a diferencia de un overlay de OnGUI):
    ///   · Humo: penacho como el de un cauterio que sube y se disipa.
    ///   · Lente empañada: velo delante de la cámara activa; a los pocos
    ///     segundos "el ayudante limpia la óptica" y desaparece.
    /// Usa el shader Sprites/Default, que siempre se incluye en las builds.
    /// </summary>
    public static class DistractoresVisuales
    {
        static Material _matHumo, _matVaho;

        public static void Humo(Vector3 origen)
        {
            var go = new GameObject("Humo");
            go.transform.position = origen;
            go.AddComponent<Penacho>();
        }

        public static void Empanar(float segundos)
        {
            if (Vaho.Actual != null) { Vaho.Actual.Prolongar(segundos); return; }
            var go = new GameObject("LenteEmpanada");
            go.AddComponent<Vaho>().duracion = segundos;
        }

        /// <summary>Cámara que se está viendo: la habilitada de mayor profundidad.</summary>
        public static Camera CamaraActiva()
        {
            Camera mejor = null;
            foreach (var c in Camera.allCameras)
                if (c.isActiveAndEnabled && c.targetTexture == null && (mejor == null || c.depth > mejor.depth)) mejor = c;
            return mejor != null ? mejor : Camera.main;
        }

        static Material Mat(ref Material m, Texture2D tex, string nombre)
        {
            if (m != null) return m;
            m = new Material(Shader.Find("Sprites/Default")) { name = nombre, mainTexture = tex };
            return m;
        }

        static Texture2D Bocanada()
        {
            const int N = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var c = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x - N / 2f) / (N / 2f), dy = (y - N / 2f) / (N / 2f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float n = Mathf.PerlinNoise(x * 0.11f, y * 0.11f) * 0.6f + Mathf.PerlinNoise(x * 0.27f + 9f, y * 0.27f) * 0.4f;
                    float a = Mathf.Clamp01(1f - r) ; a = a * a * (0.55f + 0.45f * n);
                    c[y * N + x] = new Color(1f, 1f, 1f, a);
                }
            t.SetPixels(c); t.Apply();
            return t;
        }

        static Texture2D Empanado()
        {
            const int N = 256;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var c = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.018f, y * 0.018f) * 0.7f + Mathf.PerlinNoise(x * 0.06f + 3f, y * 0.06f) * 0.3f;
                    float dx = (x - N / 2f) / (N / 2f), dy = (y - N / 2f) / (N / 2f);
                    float borde = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                    // condensación: velo lechoso con grano fino irregular, más denso hacia el borde
                    float grano = Mathf.PerlinNoise(x * 0.22f + 17f, y * 0.22f + 5f);
                    c[y * N + x] = new Color(0.92f, 0.93f, 0.94f,
                        Mathf.Clamp01(0.30f + 0.30f * n + 0.15f * grano + 0.25f * borde));
                }
            t.SetPixels(c); t.Apply();
            return t;
        }

        // ------------------------------------------------------------ humo

        class Penacho : MonoBehaviour
        {
            class B { public Transform t; public Renderer r; public Vector3 v; public float vida, edad, s0, s1, giro; }
            readonly List<B> _b = new List<B>();
            MaterialPropertyBlock _mpb;
            float _emitir = 1.4f, _acum;
            static readonly Color Gris = new Color(0.86f, 0.86f, 0.84f);

            void Start() { _mpb = new MaterialPropertyBlock(); Mat(ref _matHumo, Bocanada(), "Humo"); }

            void Update()
            {
                float dt = Time.deltaTime;
                if (_emitir > 0f)
                {
                    _emitir -= dt; _acum += dt * 10f;
                    while (_acum >= 1f) { _acum -= 1f; Nueva(); }
                }
                var cam = CamaraActiva();
                for (int i = _b.Count - 1; i >= 0; i--)
                {
                    var b = _b[i];
                    b.edad += dt;
                    float k = b.edad / b.vida;
                    if (k >= 1f) { Destroy(b.t.gameObject); _b.RemoveAt(i); continue; }
                    b.v += new Vector3(Random.Range(-1f, 1f) * 0.004f, 0.002f, Random.Range(-1f, 1f) * 0.004f) * dt;
                    b.t.position += b.v * dt;
                    float s = Mathf.Lerp(b.s0, b.s1, Mathf.Sqrt(k));
                    b.t.localScale = Vector3.one * s;
                    if (cam != null) b.t.rotation = Quaternion.LookRotation(b.t.position - cam.transform.position) * Quaternion.Euler(0, 0, b.giro += dt * 8f);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, k * 5f)) * (1f - k) * 0.55f;
                    _mpb.SetColor("_Color", new Color(Gris.r, Gris.g, Gris.b, a));
                    b.r.SetPropertyBlock(_mpb);
                }
                if (_emitir <= 0f && _b.Count == 0) Destroy(gameObject);
            }

            void Nueva()
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.transform.position = transform.position + Random.insideUnitSphere * 0.006f;
                var r = q.GetComponent<MeshRenderer>();
                r.sharedMaterial = _matHumo;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _b.Add(new B
                {
                    t = q.transform, r = r,
                    v = new Vector3(Random.Range(-0.006f, 0.006f), Random.Range(0.012f, 0.022f), Random.Range(-0.006f, 0.006f)),
                    vida = Random.Range(5f, 8f), s0 = Random.Range(0.012f, 0.02f), s1 = Random.Range(0.07f, 0.12f),
                    giro = Random.Range(0f, 360f),
                });
            }
        }

        // ------------------------------------------------------------ lente empañada

        class Vaho : MonoBehaviour
        {
            public static Vaho Actual;
            public float duracion = 4f;
            float _t, _alfa;
            Transform _quad;
            Renderer _r;
            Camera _cam;
            MaterialPropertyBlock _mpb;
            bool _limpiando;

            public void Prolongar(float s) { if (!_limpiando) duracion = Mathf.Max(duracion, _t + s); }

            void Start()
            {
                Actual = this;
                _mpb = new MaterialPropertyBlock();
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.name = "Vaho";
                _r = q.GetComponent<MeshRenderer>();
                _r.sharedMaterial = Mat(ref _matVaho, Empanado(), "Vaho");
                _r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _quad = q.transform;
                _quad.SetParent(transform, false);
                EventMarkers.Send($"distractor/visual/empanado/inicio|duracion={duracion:0.0}");
            }

            void OnDestroy() { if (Actual == this) Actual = null; }

            void LateUpdate()
            {
                _t += Time.deltaTime;
                if (!_limpiando && _t >= duracion)
                {
                    _limpiando = true;
                    EventMarkers.Send("distractor/visual/empanado/fin|causa=limpieza");
                }
                _alfa = _limpiando ? _alfa - Time.deltaTime / 0.6f : Mathf.Min(0.8f, _alfa + Time.deltaTime / 3f);
                if (_limpiando && _alfa <= 0f) { Destroy(gameObject); return; }

                var cam = CamaraActiva();
                if (cam == null) return;
                if (cam != _cam)
                {
                    _cam = cam;
                    transform.SetParent(cam.transform, false);
                    float d = cam.nearClipPlane + 0.01f;
                    float h = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.6f;
                    transform.localPosition = new Vector3(0f, 0f, d);
                    transform.localRotation = Quaternion.identity;
                    _quad.localScale = new Vector3(h * Mathf.Max(1f, cam.aspect) * 1.3f, h, 1f);
                }
                _mpb.SetColor("_Color", new Color(1f, 1f, 1f, Mathf.Clamp01(_alfa)));
                _r.SetPropertyBlock(_mpb);
            }
        }
    }
}

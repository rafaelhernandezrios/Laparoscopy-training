using System.Collections.Generic;
using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Sangrado en un punto de punción: un charco que crece a pulsos (al ritmo
    /// del monitor cardiaco) y tapa el campo. Se detiene comprimiendo con la
    /// punta de cualquier pinza sobre el origen (~1.5 s) o, en los niveles
    /// bajos, solo, al cabo de un tiempo. Después coagula: se oscurece y encoge.
    /// La lógica del ejercicio no cambia: lo que cambia es lo que se ve.
    /// </summary>
    public class Sangrado : MonoBehaviour
    {
        public static readonly List<Sangrado> Activos = new List<Sangrado>();

        public float radioMax = 0.018f;
        public float segundosHastaMax = 7f;
        public float autoStop = 15f;
        public float radioCompresion = 0.014f;
        public float segundosCompresion = 1.5f;

        public bool Detenido { get; private set; }
        public float Duracion => _t;

        Vector3 _origen;
        float _y, _r, _t, _comp, _coag;
        Transform _charco;
        Material _mat;
        RoboticArm[] _brazos;
        static readonly Color Fresca = new Color(0.42f, 0.02f, 0.03f);
        static readonly Color Coagulada = new Color(0.16f, 0.01f, 0.01f);

        public static Sangrado Crear(Vector3 punto, float radioMax, float autoStop)
        {
            var go = new GameObject("Sangrado");
            var s = go.AddComponent<Sangrado>();
            s._origen = punto;
            s.radioMax = radioMax;
            s.autoStop = autoStop;
            return s;
        }

        void Start()
        {
            Activos.Add(this);
            _y = ThreadSurfaces.Altura(_origen, out float y) ? y : _origen.y;
            _brazos = FindObjectsByType<RoboticArm>(FindObjectsSortMode.None);

            var g = new GameObject("Charco");
            g.transform.SetParent(transform, false);
            g.transform.position = new Vector3(_origen.x, _y + 0.0004f, _origen.z);
            g.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            g.AddComponent<MeshFilter>().sharedMesh = Disco(new System.Random(Random.Range(0, 99999)));
            var mr = g.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mat = Material();
            mr.sharedMaterial = _mat;
            _charco = g.transform;
            _r = radioMax * 0.12f;
            Escalar();
        }

        void OnDestroy()
        {
            Activos.Remove(this);
            if (_mat != null) Destroy(_mat);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _t += dt;
            if (!Detenido)
            {
                // crece a pulsos: rápido en la sístole, lento en la diástole
                float fase = Mathf.Repeat(Time.time * AudioFX.HeartRate / 60f, 1f);
                float pulso = fase < 0.25f ? 2.2f : 0.6f;
                float vel = radioMax / Mathf.Max(1f, segundosHastaMax);
                _r = Mathf.Min(radioMax, _r + vel * pulso * dt);

                if (Comprimiendo()) _comp += dt;
                else _comp = Mathf.Max(0f, _comp - dt * 0.5f);

                if (_comp >= segundosCompresion) Detener("hemostasia");
                else if (_t >= autoStop) Detener("espontanea");
            }
            else if (_coag < 1f)
            {
                _coag = Mathf.Min(1f, _coag + dt / 6f);
                _mat.SetColor("_BaseColor", Color.Lerp(Fresca, Coagulada, _coag));
                if (_mat.HasProperty("_Smoothness")) _mat.SetFloat("_Smoothness", Mathf.Lerp(0.96f, 0.6f, _coag));
            }
            Escalar();
        }

        void Escalar()
        {
            if (_charco == null) return;
            float r = Detenido ? _r * Mathf.Lerp(1f, 0.55f, _coag) : _r;
            float grosor = Mathf.Lerp(0.0015f, 0.004f, r / Mathf.Max(0.001f, radioMax));
            _charco.localScale = new Vector3(r, grosor, r);
        }

        bool Comprimiendo()
        {
            if (_brazos == null) return false;
            foreach (var b in _brazos)
            {
                if (b == null) continue;
                Vector3 p = b.TipPos;
                Vector2 h = new Vector2(p.x - _origen.x, p.z - _origen.z);
                if (h.magnitude < radioCompresion && p.y - _y < 0.009f) return true;
            }
            return false;
        }

        public void Detener(string causa)
        {
            if (Detenido) return;
            Detenido = true;
            EventMarkers.Send($"distractor/sangrado/fin|causa={causa}|duracion={_t:0.0}");
            if (causa == "hemostasia") AudioFX.Success();
        }

        // ------------------------------------------------------------ malla y material

        /// <summary>
        /// Charco con menisco: anillos concéntricos con perfil de cúpula suave
        /// (normales que dan brillo de líquido) y borde orgánico de baja frecuencia.
        /// </summary>
        static Mesh Disco(System.Random rnd)
        {
            const int N = 72, K = 7;
            float[] fase = new float[4], amp = { 0.16f, 0.10f, 0.06f, 0.035f };
            int[] arm = { 2, 3, 5, 8 };
            for (int j = 0; j < 4; j++) fase[j] = (float)rnd.NextDouble() * Mathf.PI * 2f;

            var v = new Vector3[1 + N * K];
            v[0] = new Vector3(0f, 1f, 0f);
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                float borde = 0.92f;
                for (int j = 0; j < 4; j++) borde += amp[j] * Mathf.Sin(arm[j] * a + fase[j]);
                for (int k = 1; k <= K; k++)
                {
                    float rho = (float)k / K;
                    float h = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(rho, 4f)));   // meseta + menisco
                    float r = rho * borde;
                    v[1 + i * K + (k - 1)] = new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r);
                }
            }
            var tri = new System.Collections.Generic.List<int>();
            for (int i = 0; i < N; i++)
            {
                int i2 = (i + 1) % N;
                tri.Add(0); tri.Add(1 + i2 * K); tri.Add(1 + i * K);
                for (int k = 0; k < K - 1; k++)
                {
                    int a0 = 1 + i * K + k, a1 = a0 + 1, b0 = 1 + i2 * K + k, b1 = b0 + 1;
                    tri.Add(a0); tri.Add(b0); tri.Add(a1);
                    tri.Add(a1); tri.Add(b0); tri.Add(b1);
                }
            }
            var m = new Mesh { name = "Charco", vertices = v, triangles = tri.ToArray() };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Material Material()
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            Material m = sh != null ? new Material(sh)
                       : new Material(MaterialSet.I != null ? MaterialSet.I.markerIdle : null);
            m.name = "Sangre";
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Fresca);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Fresca);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.96f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            // sin reflejo del cielo, como el resto de la cavidad
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            m.DisableKeyword("_EMISSION");
            return m;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SutureTrainer
{
    /// <summary>
    /// Viste las escenas con órgano (actividades 3-6) como una LAPAROSCOPIA:
    /// cavidad abdominal (cúpula de peritoneo con vasos y grasa), asas
    /// intestinales alrededor y tejido con aspecto húmedo (brillo y relieve).
    ///
    /// Todo se genera en runtime: no toca las escenas ni la física. La cúpula
    /// solo tiene caras hacia DENTRO, así que desde fuera (visor en el
    /// quirófano) es invisible y no tapa nada; desde la cámara endoscópica
    /// (modo teclado, vista "endoscopio") rodea el campo como el abdomen.
    /// No lleva colliders: no interfiere con el agarre ni con la sutura.
    ///
    /// Se activa/desactiva en SutureTrainer → "Vista laparoscópica".
    /// </summary>
    public static class LaparoscopicScene
    {
        public const string PrefKey = "SutureTrainer.VistaLaparoscopica";
#if UNITY_EDITOR
        public static bool Pref
        {
            get => UnityEditor.EditorPrefs.GetBool(PrefKey, true);
            set => UnityEditor.EditorPrefs.SetBool(PrefKey, value);
        }
#else
        static bool _pref = true;
        public static bool Pref { get => _pref; set => _pref = value; }
#endif

        public const float Radio = 0.55f;

        /// <summary>Centro del suelo de la cavidad (null si no hay).</summary>
        public static Vector3? Centro { get; private set; }

        static Texture2D _peritoneo, _peritoneoN, _intestino, _tejidoN, _higadoN;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded -= AlCargar;
            SceneManager.sceneLoaded += AlCargar;
            Construir();
        }

        static void AlCargar(Scene s, LoadSceneMode m) { Centro = null; Construir(); }

        static void Construir()
        {
            if (!Pref) return;
            var deform = Object.FindAnyObjectByType<DeformableMesh>();
            // solo en las actividades (nivel u órgano), nunca en el menú
            if (deform == null && Object.FindAnyObjectByType<TrainingLevel>() == null) return;
            var rend = deform != null ? deform.GetComponent<Renderer>() : null;
            var mesa = GameObject.Find("FieldBase");
            Renderer rm = mesa != null ? mesa.GetComponent<Renderer>() : null;

            // actividades 1-2 no tienen órgano: la cavidad se monta sobre el campo
            Bounds b;
            Material baseMat;
            if (rend != null && rend.sharedMaterial != null) { b = rend.bounds; baseMat = rend.sharedMaterial; }
            else
            {
                var campo = GameObject.Find("SurgicalField");
                if (campo == null || rm == null || rm.sharedMaterial == null) return;
                b = new Bounds(campo.transform.position, new Vector3(0.44f, 0.02f, 0.36f));
                baseMat = rm.sharedMaterial;
            }
            float sueloY = b.min.y - 0.002f;
            if (rm != null) sueloY = rm.bounds.max.y + 0.0015f;
            Vector3 c = new Vector3(b.center.x, sueloY, b.center.z);
            Centro = c;

            var raiz = new GameObject("[CavidadAbdominal]");

            if (rend != null) MejorarHigado(rend);

            var matPer = Hacer(baseMat, "Peritoneo", Peritoneo(), PeritoneoN(), 0.48f, 0.5f, new Vector2(1f, 1f));
            var suelo = Objeto("Suelo", raiz, Disco(Radio + 0.02f, 72), matPer, sombras: false);
            suelo.transform.position = c;
            var cupula = Objeto("Cupula", raiz, Cupula(Radio, 64, 26), matPer, sombras: false);
            cupula.transform.position = c;

            // asas intestinales alrededor del hígado (no delante de la cámara)
            var matInt = Hacer(baseMat, "Intestino", Intestino(), TejidoN(), 0.62f, 0.6f, new Vector2(3f, 1f));
            float anillo = Mathf.Max(b.extents.x, b.extents.z) + 0.045f;
            var rnd = new System.Random(7);
            float[] inicios = { 20f, 75f, 130f, 185f, 330f };
            foreach (float a0 in inicios)
            {
                float r = anillo + (float)rnd.NextDouble() * 0.05f;
                float grosor = 0.017f + (float)rnd.NextDouble() * 0.008f;
                var asa = Objeto("Asa", raiz, Asa(r, a0, 48f + (float)rnd.NextDouble() * 20f, grosor, rnd), matInt, sombras: true);
                asa.transform.position = c;
            }

            Debug.Log("[Laparoscopia] Cavidad abdominal montada alrededor del órgano.");
        }

        // ------------------------------------------------------------ materiales

        static void MejorarHigado(Renderer rend)
        {
            foreach (var m in rend.materials)   // instancias: no se toca el asset
            {
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.46f);
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 1f);
                SinReflejoDelCielo(m);
                if (m.HasProperty("_BumpMap"))
                {
                    m.SetTexture("_BumpMap", HigadoN());
                    if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", 0.45f);
                    m.EnableKeyword("_NORMALMAP");
                }
            }
        }

        static Material Hacer(Material plantilla, string nombre, Texture2D color, Texture2D normal,
                              float brillo, float relieve, Vector2 tiling)
        {
            var m = new Material(plantilla) { name = nombre };
            foreach (var p in new[] { "_BaseMap", "_MainTex" })
                if (m.HasProperty(p)) { m.SetTexture(p, color); m.SetTextureScale(p, tiling); }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", brillo);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", normal);
                if (m.HasProperty("_BumpScale")) m.SetFloat("_BumpScale", relieve);
                m.EnableKeyword("_NORMALMAP");
            }
            m.DisableKeyword("_EMISSION");
            SinReflejoDelCielo(m);
            return m;
        }

        /// <summary>
        /// Dentro del abdomen no hay cielo: el reflejo del skybox teñía de azul
        /// los tejidos húmedos. El brillo húmedo queda solo para las luces.
        /// </summary>
        static void SinReflejoDelCielo(Material m)
        {
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
            m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        }

        static GameObject Objeto(string n, GameObject padre, Mesh mesh, Material mat, bool sombras)
        {
            var go = new GameObject(n);
            go.transform.SetParent(padre.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = sombras ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = true;
            return go;
        }

        // ------------------------------------------------------------ mallas

        static Mesh Disco(float r, int seg)
        {
            var v = new List<Vector3> { Vector3.zero };
            var uv = new List<Vector2> { Vector2.one * 0.5f };
            var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v.Add(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 1.5f, 0.5f + Mathf.Sin(a) * 1.5f));
            }
            for (int i = 1; i <= seg; i++) { t.Add(0); t.Add(i + 1); t.Add(i); }
            var m = new Mesh { name = "SueloCavidad" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Semiesfera con caras hacia dentro y relieve suave.</summary>
        static Mesh Cupula(float r, int seg, int anillos)
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            for (int j = 0; j <= anillos; j++)
            {
                float e = j * (Mathf.PI * 0.5f) / anillos;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    Vector3 d = new Vector3(Mathf.Cos(e) * Mathf.Cos(a), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Sin(a));
                    float ruido = 1f + 0.05f * (Mathf.PerlinNoise(i * 0.35f, j * 0.35f) - 0.5f) * 2f;
                    v.Add(d * r * (j == 0 ? 1f : ruido));
                    uv.Add(new Vector2(i / (float)seg * 5f, j / (float)anillos * 1.6f));
                }
            }
            int w = seg + 1;
            for (int j = 0; j < anillos; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a = j * w + i, b2 = a + 1, c = a + w, d = c + 1;
                    // orden invertido: normales hacia el centro
                    t.Add(a); t.Add(b2); t.Add(c);
                    t.Add(b2); t.Add(d); t.Add(c);
                }
            var m = new Mesh { name = "CupulaCavidad" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        /// <summary>Tubo serpenteante a lo largo de un arco, con los extremos hundidos.</summary>
        static Mesh Asa(float radio, float a0Grados, float largoGrados, float grosor, System.Random rnd)
        {
            int n = 40, lados = 14;
            var camino = new List<Vector3>();
            float fase = (float)rnd.NextDouble() * 6f;
            for (int k = 0; k <= n; k++)
            {
                float t = k / (float)n;
                float a = (a0Grados + largoGrados * t) * Mathf.Deg2Rad;
                float rr = radio + 0.018f * Mathf.Sin(t * 9f + fase);
                float y = grosor * 0.8f - grosor * 1.6f * Mathf.Pow(Mathf.Abs(t * 2f - 1f), 4f);
                camino.Add(new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr));
            }
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            for (int k = 0; k <= n; k++)
            {
                Vector3 fwd = (camino[Mathf.Min(k + 1, n)] - camino[Mathf.Max(k - 1, 0)]).normalized;
                Vector3 der = Vector3.Cross(Vector3.up, fwd).normalized;
                Vector3 arr = Vector3.Cross(fwd, der);
                float g = grosor * (1f + 0.12f * Mathf.Sin(k * 1.7f));   // pliegues
                for (int s = 0; s <= lados; s++)
                {
                    float ang = s * Mathf.PI * 2f / lados;
                    v.Add(camino[k] + (der * Mathf.Cos(ang) + arr * Mathf.Sin(ang)) * g);
                    uv.Add(new Vector2(k / (float)n * 4f, s / (float)lados));
                }
            }
            int w = lados + 1;
            for (int k = 0; k < n; k++)
                for (int s = 0; s < lados; s++)
                {
                    int a = k * w + s, b = a + 1, c = a + w, d = c + 1;
                    tri.Add(a); tri.Add(b); tri.Add(c);   // caras hacia fuera
                    tri.Add(b); tri.Add(d); tri.Add(c);
                }
            var m = new Mesh { name = "AsaIntestinal" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------ texturas

        static float Fbm(float x, float y, int oct)
        {
            float s = 0f, amp = 0.5f, f = 1f;
            for (int i = 0; i < oct; i++) { s += amp * Mathf.PerlinNoise(x * f + 31.7f * i, y * f + 17.3f * i); amp *= 0.5f; f *= 2f; }
            return s;
        }

        static Texture2D Peritoneo()
        {
            if (_peritoneo != null) return _peritoneo;
            const int N = 512;
            var col = new Color[N * N];
            var alto = new float[N * N];
            Color oscuro = new Color(0.62f, 0.30f, 0.30f), claro = new Color(0.88f, 0.56f, 0.52f);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float n = Fbm(x / 64f, y / 64f, 5);
                    float k = Mathf.Clamp01((n - 0.25f) / 0.5f);
                    col[y * N + x] = Color.Lerp(oscuro, claro, k * k * (3f - 2f * k));
                    alto[y * N + x] = n * 0.4f;
                }

            var rnd = new System.Random(3);
            // grasa: manchas amarillentas suaves
            for (int k = 0; k < 14; k++)
            {
                int cx = rnd.Next(N), cy = rnd.Next(N), rr = 18 + rnd.Next(30);
                for (int dy = -rr; dy <= rr; dy++)
                    for (int dx = -rr; dx <= rr; dx++)
                    {
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / rr;
                        if (d > 1f) continue;
                        int px = (cx + dx + N) % N, py = (cy + dy + N) % N, i = py * N + px;
                        float a = (1f - d * d) * 0.45f * Fbm(px / 20f, py / 20f, 3) * 1.6f;
                        col[i] = Color.Lerp(col[i], new Color(0.93f, 0.80f, 0.55f), Mathf.Clamp01(a));
                        alto[i] += a * 0.5f;
                    }
            }
            // vasos: caminos aleatorios que se ramifican
            var pila = new Stack<(float x, float y, float ang, float ancho, int pasos)>();
            for (int k = 0; k < 18; k++)
                pila.Push((rnd.Next(N), rnd.Next(N), (float)rnd.NextDouble() * 6.28f, 1.3f + 0.7f * (float)rnd.NextDouble(), 160 + rnd.Next(120)));
            while (pila.Count > 0)
            {
                var (x, y, ang, ancho, pasos) = pila.Pop();
                for (int p = 0; p < pasos && ancho > 0.5f; p++)
                {
                    ang += ((float)rnd.NextDouble() - 0.5f) * 0.35f;
                    x += Mathf.Cos(ang); y += Mathf.Sin(ang);
                    ancho *= 0.997f;
                    Pintar(col, alto, N, x, y, ancho, new Color(0.58f, 0.16f, 0.17f), 0.45f);
                    if (rnd.NextDouble() < 0.012 && pila.Count < 400)
                        pila.Push((x, y, ang + ((float)rnd.NextDouble() > 0.5 ? 0.7f : -0.7f), ancho * 0.65f, pasos / 2));
                }
            }
            _peritoneo = Crear(col, N, false);
            _peritoneoN = Normal(alto, N, 3.5f);
            return _peritoneo;
        }

        static Texture2D PeritoneoN() { if (_peritoneoN == null) Peritoneo(); return _peritoneoN; }

        static void Pintar(Color[] col, float[] alto, int N, float x, float y, float r, Color c, float fuerza)
        {
            int ri = Mathf.CeilToInt(r);
            for (int dy = -ri; dy <= ri; dy++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > r) continue;
                    int px = ((int)x + dx % N + N) % N, py = ((int)y + dy % N + N) % N, i = py * N + px;
                    float a = fuerza * (1f - d / (r + 0.5f));
                    col[i] = Color.Lerp(col[i], c, a);
                    alto[i] = Mathf.Max(alto[i], 0.4f + 0.6f * (1f - d / (r + 0.5f)));
                }
        }

        static Texture2D Intestino()
        {
            if (_intestino != null) return _intestino;
            const int N = 256;
            var col = new Color[N * N];
            Color a = new Color(0.82f, 0.50f, 0.46f), b = new Color(0.96f, 0.70f, 0.64f);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float n = Fbm(x / 40f, y / 40f, 4);
                    float vaso = Mathf.Pow(Mathf.Abs(Mathf.Sin(x / 9f + Fbm(x / 30f, y / 30f, 2) * 6f)), 30f);
                    col[y * N + x] = Color.Lerp(Color.Lerp(a, b, n), new Color(0.7f, 0.25f, 0.24f), vaso * 0.25f);
                }
            return _intestino = Crear(col, N, false);
        }

        static Texture2D TejidoN()
        {
            if (_tejidoN != null) return _tejidoN;
            const int N = 256;
            var h = new float[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++) h[y * N + x] = Fbm(x / 18f, y / 18f, 4);
            return _tejidoN = Normal(h, N, 2.5f);
        }

        static Texture2D HigadoN()
        {
            if (_higadoN != null) return _higadoN;
            const int N = 512;
            var h = new float[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                    h[y * N + x] = Fbm(x / 22f, y / 22f, 5) + 0.35f * Fbm(x / 6f, y / 6f, 2);
            return _higadoN = Normal(h, N, 2.0f);
        }

        static Texture2D Crear(Color[] col, int N, bool lineal)
        {
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, lineal)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels(col); t.Apply(true, true);
            return t;
        }

        /// <summary>Mapa de normales desde una altura (x en R, y en G, A = 1).</summary>
        static Texture2D Normal(float[] h, int N, float fuerza)
        {
            var col = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = h[y * N + (x + 1) % N] - h[y * N + (x - 1 + N) % N];
                    float dy = h[((y + 1) % N) * N + x] - h[((y - 1 + N) % N) * N + x];
                    Vector3 n = new Vector3(-dx * fuerza, -dy * fuerza, 1f).normalized;
                    col[y * N + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            return Crear(col, N, true);
        }
    }
}

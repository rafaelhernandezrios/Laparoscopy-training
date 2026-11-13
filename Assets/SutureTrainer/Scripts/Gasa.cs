using UnityEngine;

namespace SutureTrainer
{
    /// <summary>Material procedural de gasa quirúrgica: tejido de algodón con banda radiopaca azul.</summary>
    public static class Gasa
    {
        static Material _mat;

        public static Material Material(Material plantilla)
        {
            if (_mat != null) return _mat;
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            _mat = sh != null ? new Material(sh) : new Material(plantilla);
            _mat.name = "Gasa";
            var tex = Textura();
            foreach (var p in new[] { "_BaseMap", "_MainTex" })
                if (_mat.HasProperty(p)) _mat.SetTexture(p, tex);
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", Color.white);
            if (_mat.HasProperty("_Color")) _mat.SetColor("_Color", Color.white);
            if (_mat.HasProperty("_Smoothness")) _mat.SetFloat("_Smoothness", 0.08f);
            if (_mat.HasProperty("_Metallic")) _mat.SetFloat("_Metallic", 0f);
            if (_mat.HasProperty("_EnvironmentReflections")) _mat.SetFloat("_EnvironmentReflections", 0f);
            _mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            return _mat;
        }

        static Texture2D Textura()
        {
            const int N = 256;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "Gasa", wrapMode = TextureWrapMode.Repeat };
            var c = new Color[N * N];
            var rnd = new System.Random(7);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    // trama abierta: hilos cada 8 px en ambos sentidos, huecos más oscuros
                    bool hx = (x % 4) < 2, hy = (y % 4) < 2;   // hilos de ~1 mm
                    float v = hx && hy ? 0.97f : hx || hy ? 0.92f : 0.80f;
                    v += ((float)rnd.NextDouble() - 0.5f) * 0.05f;
                    Color col = new Color(v, v, v * 0.97f);
                    // banda radiopaca azul a lo largo, a un tercio del borde
                    if (y > N * 0.30f && y < N * 0.335f && (hx || hy)) col = new Color(0.15f, 0.35f, 0.75f) * (0.85f + v * 0.15f);
                    c[y * N + x] = col;
                }
            t.SetPixels(c);
            t.Apply(true);
            return t;
        }
    }
}

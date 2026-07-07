using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SutureTrainer
{
    /// <summary>
    /// Post-procesado URP aplicado automáticamente en todas las escenas
    /// (sin necesidad de reconstruirlas): bloom suave, viñeta, ajuste de
    /// color y tonemapping. Valores conservadores para VR/Quest.
    /// </summary>
    public static class PostFX
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            Apply();
            SceneManager.sceneLoaded += (s, m) => Apply();
        }

        static void Apply()
        {
            if (Object.FindFirstObjectByType<Volume>() != null) return; // ya existe

            var cam = Camera.main;
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
            }

            var go = new GameObject("PostFX Volume");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();

            var bloom = profile.Add<Bloom>();
            bloom.intensity.Override(0.4f);
            bloom.threshold.Override(0.9f);   // capta emisivos (dianas, láser) también sin HDR
            bloom.scatter.Override(0.6f);

            var vig = profile.Add<Vignette>();
            vig.intensity.Override(0.22f);
            vig.smoothness.Override(0.45f);

            var ca = profile.Add<ColorAdjustments>();
            ca.postExposure.Override(0.15f);
            ca.contrast.Override(8f);
            ca.saturation.Override(8f);

            var tone = profile.Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.Neutral);

            vol.profile = profile;
        }
    }
}

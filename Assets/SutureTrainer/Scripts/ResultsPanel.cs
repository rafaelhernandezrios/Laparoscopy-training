using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

namespace SutureTrainer
{
    /// <summary>Panel de resultados al final de cada nivel: estrellas, métricas y navegación.</summary>
    public class ResultsPanel : MonoBehaviour
    {
        public TextMeshPro titleText;
        public TextMeshPro statsText;
        public Renderer[] starRenderers;   // 3 estrellas
        public Material starOn, starOff;
        public WorldButton retryButton, nextButton, menuButton;

        void Awake() { gameObject.SetActive(false); }

        public void Show(MetricsRecorder.Score score)
        {
            gameObject.SetActive(true);
            if (titleText != null)
                titleText.text = score.stars >= 3 ? "¡Excelente!" : score.stars == 2 ? "¡Bien hecho!" : "Nivel completado";

            if (statsText != null)
            {
                string prec = score.avgPrecisionMm > 0.01f ? $"\nPrecisión media: {score.avgPrecisionMm:0.0} mm" : "";
                statsText.text =
                    $"Tiempo: {Mathf.FloorToInt(score.time / 60f):00}:{Mathf.FloorToInt(score.time % 60f):00}\n" +
                    $"Recorrido de instrumentos: {score.path:0.0} m\n" +
                    $"Errores: {score.errorCount}{prec}";
            }

            for (int i = 0; i < starRenderers.Length; i++)
                if (starRenderers[i] != null)
                    starRenderers[i].sharedMaterial = i < score.stars ? starOn : starOff;

            if (retryButton != null) retryButton.sceneToLoad = SceneManager.GetActiveScene().name;
            if (nextButton != null) nextButton.sceneToLoad = GameFlow.NextOf(SceneManager.GetActiveScene().name);
            if (menuButton != null) menuButton.sceneToLoad = GameFlow.MenuScene;
        }
    }
}

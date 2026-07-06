using UnityEngine;
using TMPro;

namespace SutureTrainer
{
    /// <summary>Panel de información sobre el campo quirúrgico (título, objetivo, cronómetro, avisos).</summary>
    public class HUD : MonoBehaviour
    {
        public TextMeshPro titleText;
        public TextMeshPro objectiveText;
        public TextMeshPro timerText;
        public TextMeshPro flashText;

        float flashUntil;

        public void SetTitle(string t) { if (titleText != null) titleText.text = t; }
        public void SetObjective(string t) { if (objectiveText != null) objectiveText.text = t; }

        public void Flash(string msg, Color color)
        {
            if (flashText == null) return;
            flashText.text = msg;
            flashText.color = color;
            flashUntil = Time.time + 2.5f;
        }

        void Update()
        {
            if (timerText != null && MetricsRecorder.I != null)
            {
                float t = MetricsRecorder.I.ElapsedTime;
                int errs = MetricsRecorder.I.ErrorCount;
                timerText.text = $"{Mathf.FloorToInt(t / 60f):00}:{Mathf.FloorToInt(t % 60f):00}   ·   Errores: {errs}";
            }
            if (flashText != null && Time.time > flashUntil && !string.IsNullOrEmpty(flashText.text))
                flashText.text = "";
        }
    }
}

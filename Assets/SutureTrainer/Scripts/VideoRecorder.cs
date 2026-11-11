using System.Collections;
using System.IO;
using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Graba la vista Game fotograma a fotograma (JPG) en
    /// &lt;proyecto&gt;/Grabaciones/&lt;fecha&gt;_&lt;etiqueta&gt;/ para montar un MP4 con
    /// ffmpeg. Fija Time.captureFramerate: el tiempo de juego avanza 1/30 s
    /// por fotograma, así el vídeo sale fluido aunque el editor vaya lento.
    /// </summary>
    public class VideoRecorder : MonoBehaviour
    {
        public const int Fps = 30;
        public static VideoRecorder Active { get; private set; }

        string _dir;
        int _frame;

        public static void Begin(string etiqueta)
        {
            if (Active != null) return;
            Active = new GameObject("[VideoRecorder]").AddComponent<VideoRecorder>();
            Active.Init(etiqueta);
        }

        public static void End() { if (Active != null) Active.Terminar(); }

        void Init(string etiqueta)
        {
            string raiz = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Grabaciones"));
            _dir = Path.Combine(raiz, System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + etiqueta);
            Directory.CreateDirectory(_dir);
            Time.captureFramerate = Fps;
            KeyboardControls.Grabando = true;
            StartCoroutine(Capturar());
            Debug.Log($"[Grabación] Grabando en {_dir}");
        }

        IEnumerator Capturar()
        {
            var fin = new WaitForEndOfFrame();
            while (true)
            {
                yield return fin;
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null) continue;
                File.WriteAllBytes(Path.Combine(_dir, $"f_{_frame:D5}.jpg"), tex.EncodeToJPG(92));
                Destroy(tex);
                _frame++;
            }
        }

        void Terminar()
        {
            StopAllCoroutines();
            Debug.Log($"[Grabación] Terminada: {_frame} fotogramas a {Fps} fps en {_dir}");
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (Active != this) return;
            Time.captureFramerate = 0;
            KeyboardControls.Grabando = false;
            Active = null;
        }
    }
}

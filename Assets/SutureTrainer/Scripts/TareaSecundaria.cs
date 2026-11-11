using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SutureTrainer
{
    /// <summary>
    /// Tarea secundaria de detección-respuesta (paradigma de doble tarea, tipo
    /// DRT de ISO 17488, versión auditiva). Cada 7-14 s suena un tono; hay que
    /// responder lo antes posible con el GRIP de cualquier mando o la tecla R.
    /// El tiempo de reacción y las omisiones dan una medida CONDUCTUAL de la
    /// capacidad sobrante, independiente del EEG, para validarlo.
    ///
    /// Marcadores: tarea2/estimulo, tarea2/respuesta|rt=, tarea2/omision,
    /// tarea2/falsa, tarea2/resumen al terminar el ejercicio. F8 activa/desactiva.
    /// </summary>
    public class TareaSecundaria : MonoBehaviour
    {
        const string Key = "SutureTrainer.TareaSecundaria";

        public static bool Pref
        {
            get => PlayerPrefs.GetInt(Key, 0) == 1;
            set { PlayerPrefs.SetInt(Key, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        public Vector2 intervalo = new Vector2(7f, 14f);
        public float ventana = 2.5f;

        public bool Activa { get; private set; }
        public int Estimulos { get; private set; }
        public int Aciertos { get; private set; }
        public int Omisiones { get; private set; }
        public int Falsas { get; private set; }
        public float RtMedio => _rts.Count > 0 ? Suma(_rts) / _rts.Count : 0f;

        readonly List<float> _rts = new List<float>();
        float _prox, _tEstimulo = -1f;
        bool _gripPrevio, _resumido;
        MasterInput[] _mandos;
        TrainingLevel _nivel;

        void Start()
        {
            Activa = Pref;
            _mandos = FindObjectsByType<MasterInput>(FindObjectsSortMode.None);
            _nivel = FindAnyObjectByType<TrainingLevel>();
            Programar();
        }

        public void Alternar()
        {
            Activa = !Activa;
            Pref = Activa;
            _tEstimulo = -1f;
            Programar();
            EventMarkers.Send($"tarea2/{(Activa ? "activada" : "desactivada")}");
        }

        void Programar() => _prox = Time.time + Random.Range(intervalo.x, intervalo.y);

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f8Key.wasPressedThisFrame) Alternar();

            bool terminado = _nivel != null && _nivel.Finished;
            if (terminado) { Resumir(); return; }
            if (!Activa) return;

            bool respuesta = (kb != null && kb.rKey.wasPressedThisFrame);
            bool grip = false;
            if (_mandos != null) foreach (var m in _mandos) if (m != null && m.Grip > 0.7f) grip = true;
            if (grip && !_gripPrevio) respuesta = true;
            _gripPrevio = grip;

            if (_tEstimulo >= 0f)
            {
                float rt = Time.time - _tEstimulo;
                if (respuesta)
                {
                    Aciertos++; _rts.Add(rt); _tEstimulo = -1f; Programar();
                    EventMarkers.Send($"tarea2/respuesta|rt={rt:0.000}");
                }
                else if (rt > ventana)
                {
                    Omisiones++; _tEstimulo = -1f; Programar();
                    EventMarkers.Send("tarea2/omision");
                }
            }
            else
            {
                if (respuesta) { Falsas++; EventMarkers.Send("tarea2/falsa"); }
                if (Time.time >= _prox)
                {
                    Estimulos++;
                    _tEstimulo = Time.time;
                    AudioFX.Distractor("estimulo");
                    EventMarkers.Send("tarea2/estimulo");
                }
            }
        }

        void Resumir()
        {
            if (_resumido || Estimulos == 0) return;
            _resumido = true;
            EventMarkers.Send($"tarea2/resumen|estimulos={Estimulos}|aciertos={Aciertos}|omisiones={Omisiones}" +
                              $"|falsas={Falsas}|rt_medio={RtMedio:0.000}");
        }

        static float Suma(List<float> l) { float s = 0f; foreach (var x in l) s += x; return s; }
    }
}

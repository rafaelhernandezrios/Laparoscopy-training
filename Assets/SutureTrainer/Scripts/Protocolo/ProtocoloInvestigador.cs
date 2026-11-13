using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace SutureTrainer
{
    /// <summary>
    /// Panel del INVESTIGADOR (F4, solo en el monitor del PC) para conducir
    /// los dos estudios del protocolo con el EEG AURA por LSL.
    ///
    /// Estudio 1 · Caracterización: dificultad fija, sin adaptación. Mide la
    ///   carga (EEG, NASA-TLX, tarea secundaria) de cada actividad, primero con
    ///   los mandos de Oculus y al final con el robot Touch.
    /// Estudio 2 · Comparativo: calibración personal de umbrales y luego las
    ///   mismas actividades en grupo ADAPTATIVO (el EEG mueve la dificultad) o
    ///   ACOPLADO (reproduce la secuencia de dificultad de un participante
    ///   adaptativo, sin depender de su propio EEG).
    ///
    /// Datos por sesión en persistentDataPath/SutureTrainer/Sesiones/&lt;ID&gt;_.../
    ///   sesion.txt · pasos.csv · resumen_actividades.csv · carga.csv ·
    ///   dificultad_&lt;clave&gt;.csv (traza para el grupo acoplado)
    /// Todo paso deja marcadores protocolo/... en LSL y en eventos_*.csv.
    /// </summary>
    public class ProtocoloInvestigador : MonoBehaviour
    {
        public static ProtocoloInvestigador I { get; private set; }
        public static bool PanelVisible => I != null && I._visible && !KeyboardControls.Grabando;

        const string KeyVisible = "SutureTrainer.PanelInvestigador";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Instalar()
        {
            if (I != null) return;
            var go = new GameObject("[ProtocoloInvestigador]");
            DontDestroyOnLoad(go);
            I = go.AddComponent<ProtocoloInvestigador>();
        }

        // ------------------------------------------------------------ modelo

        enum Estudio { Caracterizacion, Comparativo }
        enum Grupo { Adaptativo, Acoplado }
        enum Tipo { Senal, BaseAbiertos, BaseCerrados, Calibracion, Actividad, Tlx, Descanso, CambioRobot, Fin }

        class Paso
        {
            public Tipo tipo;
            public string titulo;
            public int actividad;
            public bool robot;
            public bool dificil;           // calibración
            public string estado = "pendiente";
            public string resultado = "";
            public string Clave => $"A{actividad}_{(robot ? "robot" : "oculus")}";
        }

        class Resumen
        {
            public string clave, condicion = "", fin = "";
            public int actividad; public bool robot;
            public float t, recorrido, precision, errores, estrellas;
            public float razon, score, pctOptima, pctValida, dMedia, dFinal;
            public int t2Est, t2Ac, t2Om, t2Fa; public float t2Rt;
            public float[] tlx; public bool escrito;
        }

        static readonly string[] DimTlx = { "Exigencia mental", "Exigencia física", "Exigencia temporal", "Rendimiento", "Esfuerzo", "Frustración" };
        static readonly string[] DescTlx =
        {
            "¿Cuánta actividad mental y de percepción requirió la tarea\n(pensar, decidir, recordar, mirar, buscar)?",
            "¿Cuánta actividad física requirió la tarea\n(mover los brazos, sostener, controlar)?",
            "¿Cuánta presión de tiempo sentiste\npor el ritmo de la tarea?",
            "¿Qué tan bien crees que lograste los objetivos?\n(0 = perfecto · 100 = fracaso)",
            "¿Cuánto tuviste que esforzarte (mental y físicamente)\npara lograr tu nivel de rendimiento?",
            "¿Qué tan inseguro, desanimado, irritado o estresado\nte sentiste durante la tarea?",
        };

        // configuración
        string _id = "P01";
        Estudio _estudio = Estudio.Caracterizacion;
        Grupo _grupo = Grupo.Adaptativo;
        string _actOculus = "1,2,3,4,5,7", _actRobot = "1,3";
        string _sBase = "120", _sDescanso = "30", _sMaxAct = "480", _sCalib = "90", _sDFija = "0.5";
        string _sLo = "1.15", _sHi = "1.6";
        int _distractores;
        bool _tarea2, _modoTeclado, _baseCerrados = true;
        string _fuenteAcoplado = "";

        // sesión
        bool _sesion;
        string _dir;
        readonly List<Paso> _pasos = new List<Paso>();
        int _actual;
        bool _corriendo;
        float _tPaso, _durPaso;
        StreamWriter _carga;
        float _t0Sesion;
        float _alfaAbiertos, _scoreFacil = -1f, _scoreDificil = -1f;
        Resumen _ultimo;
        int _tlxDim;
        readonly float[] _tlx = new float[6];
        readonly List<string> _filasPasos = new List<string>();

        // actividad en curso
        AdaptiveDifficultyController _ctl;
        string _escenaEsperada;
        bool _configurarAlCargar;
        float _tAct;
        readonly List<float> _actScore = new List<float>(), _actRazon = new List<float>();
        int _actOptima, _actMuestras;
        readonly List<string> _trazaD = new List<string>();
        List<(float t, float d)> _replay;
        readonly Queue<float> _ventanaScores = new Queue<float>();
        Paso _pasoAct;

        // interfaz
        bool _visible;
        Vector2 _scrollPasos, _scrollConfig;
        string _estadoEeg = "Sin conectar";
        string _fMap = "", _pMap = "";
        Texture2D _px;
        bool _verConfig = true;

        AuraLsl Aura => AuraLsl.Asegurar();

        /// <summary>En el editor, &lt;proyecto&gt;/Sesiones (junto a Grabaciones); en una build, persistentDataPath.</summary>
        static string RaizSesiones => Application.isEditor
            ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Sesiones"))
            : Path.Combine(Application.persistentDataPath, "SutureTrainer", "Sesiones");

        void Awake()
        {
            _visible = PlayerPrefs.GetInt(KeyVisible, 0) == 1;
            _px = new Texture2D(1, 1); _px.SetPixel(0, 0, Color.white); _px.Apply();
            SceneManager.sceneLoaded += AlCargarEscena;
            _id = PlayerPrefs.GetString("SutureTrainer.Prot.Id", _id);
            _actOculus = PlayerPrefs.GetString("SutureTrainer.Prot.Oculus", _actOculus);
            _actRobot = PlayerPrefs.GetString("SutureTrainer.Prot.Robot", _actRobot);
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= AlCargarEscena;
            CerrarArchivos();
        }

        // ------------------------------------------------------------ armado de pasos

        void ArmarPasos()
        {
            _pasos.Clear();
            _pasos.Add(new Paso { tipo = Tipo.Senal, titulo = "Señal EEG: conectar AURA y revisar electrodos" });
            _pasos.Add(new Paso { tipo = Tipo.BaseAbiertos, titulo = "Línea base · ojos abiertos" });
            if (_baseCerrados) _pasos.Add(new Paso { tipo = Tipo.BaseCerrados, titulo = "Línea base · ojos cerrados" });
            if (_estudio == Estudio.Comparativo)
            {
                _pasos.Add(new Paso { tipo = Tipo.Calibracion, titulo = "Calibración · bloque fácil", actividad = 1 });
                _pasos.Add(new Paso { tipo = Tipo.Descanso, titulo = "Descanso" });
                _pasos.Add(new Paso { tipo = Tipo.Calibracion, titulo = "Calibración · bloque difícil", actividad = 1, dificil = true });
                _pasos.Add(new Paso { tipo = Tipo.Descanso, titulo = "Descanso" });
            }
            foreach (int a in Lista(_actOculus)) AgregarActividad(a, false);
            var robot = Lista(_actRobot);
            if (robot.Count > 0)
            {
                _pasos.Add(new Paso { tipo = Tipo.CambioRobot, titulo = "Cambio al robot Touch" });
                foreach (int a in robot) AgregarActividad(a, true);
            }
            _pasos.Add(new Paso { tipo = Tipo.Fin, titulo = "Fin de la sesión" });
        }

        void AgregarActividad(int a, bool robot)
        {
            string disp = robot ? "robot" : "Oculus";
            _pasos.Add(new Paso { tipo = Tipo.Actividad, titulo = $"Actividad {a} · {disp}", actividad = a, robot = robot });
            _pasos.Add(new Paso { tipo = Tipo.Tlx, titulo = $"NASA-TLX · actividad {a} ({disp})", actividad = a, robot = robot });
            _pasos.Add(new Paso { tipo = Tipo.Descanso, titulo = "Descanso" });
        }

        static List<int> Lista(string s)
        {
            var r = new List<int>();
            foreach (var t in s.Split(','))
                if (int.TryParse(t.Trim(), out int v) && v >= 1 && v <= 7 && v != 6) r.Add(v);
            return r;
        }

        static string Escena(int a) => a == 7 ? GameFlow.BimanualScene : GameFlow.Scenes[Mathf.Clamp(a, 1, 5)];

        float F(string s, float def) =>
            float.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;

        // ------------------------------------------------------------ sesión

        void IniciarSesion()
        {
            PlayerPrefs.SetString("SutureTrainer.Prot.Id", _id);
            PlayerPrefs.SetString("SutureTrainer.Prot.Oculus", _actOculus);
            PlayerPrefs.SetString("SutureTrainer.Prot.Robot", _actRobot);
            PlayerPrefs.Save();

            ArmarPasos();
            string est = _estudio == Estudio.Caracterizacion ? "E1" : (_grupo == Grupo.Adaptativo ? "E2adapt" : "E2acopl");
            _dir = Path.Combine(RaizSesiones, $"{Limpio(_id)}_{est}_{System.DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "sesion.txt"), string.Join("\n", new[]
            {
                $"participante={_id}", $"estudio={_estudio}", $"grupo={(_estudio == Estudio.Comparativo ? _grupo.ToString() : "-")}",
                $"fecha={System.DateTime.Now:yyyy-MM-dd HH:mm:ss}", $"actividades_oculus={_actOculus}", $"actividades_robot={_actRobot}",
                $"base_s={_sBase}", $"descanso_s={_sDescanso}", $"max_actividad_s={_sMaxAct}", $"calibracion_s={_sCalib}",
                $"dificultad_fija={_sDFija}", $"distractores={_distractores}", $"tarea_secundaria={_tarea2}",
                $"umbral_bajo={_sLo}", $"umbral_alto={_sHi}", $"fuente_acoplado={_fuenteAcoplado}",
                $"eeg_stream={Aura.NombreStream}", $"eeg_canales={Aura.Canales}", $"eeg_fs={Aura.Fs:0}",
                $"frontales={string.Join(",", Aura.Frontales.Select(i => Etq(i)))}",
                $"parietales={string.Join(",", Aura.Parietales.Select(i => Etq(i)))}",
            }));
            _carga = new StreamWriter(Path.Combine(_dir, "carga.csv"), false, new UTF8Encoding(false)) { AutoFlush = true };
            _carga.WriteLine("t_sesion;lsl;paso;tipo;actividad;dispositivo;theta_f;alfa_p;razon;score;zona;dificultad");
            File.WriteAllText(Path.Combine(_dir, "resumen_actividades.csv"),
                "clave;actividad;dispositivo;condicion;fin;tiempo_s;recorrido_m;errores;precision_mm;estrellas;" +
                "razon_media;score_medio;pct_zona_optima;pct_ventanas_validas;dificultad_media;dificultad_final;" +
                "t2_estimulos;t2_aciertos;t2_omisiones;t2_falsas;t2_rt_medio;" +
                "tlx_mental;tlx_fisica;tlx_temporal;tlx_rendimiento;tlx_esfuerzo;tlx_frustracion;tlx_total\n");
            _filasPasos.Clear();
            _filasPasos.Add("n;tipo;titulo;inicio_s;fin_s;estado;resultado");
            _t0Sesion = Time.realtimeSinceStartup;
            _actual = 0;
            _sesion = true;
            _verConfig = false;
            _scoreFacil = _scoreDificil = -1f;
            Aura.AlCalcular -= AlCalcular;
            Aura.AlCalcular += AlCalcular;
            EventMarkers.Send($"protocolo/sesion/inicio|id={Limpio(_id)}|estudio={_estudio}|grupo={(_estudio == Estudio.Comparativo ? _grupo.ToString() : "-")}");
            Debug.Log($"[Protocolo] Sesión iniciada: {_dir}");
        }

        void TerminarSesion(string motivo)
        {
            if (!_sesion) return;
            EscribirResumenPendiente();
            File.WriteAllLines(Path.Combine(_dir, "pasos.csv"), _filasPasos);
            EventMarkers.Send($"protocolo/sesion/fin|motivo={motivo}");
            CerrarArchivos();
            _sesion = false;
            _corriendo = false;
            StopAllCoroutines();
            AdaptiveDifficultyController.AdaptacionHabilitada = true;
            MensajeParticipante.Ocultar();
            Debug.Log($"[Protocolo] Sesión terminada ({motivo}). Datos en {_dir}");
        }

        void CerrarArchivos() { try { _carga?.Dispose(); } catch { } _carga = null; }

        static string Limpio(string s) => new string((s ?? "").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());

        string Etq(int i) => i >= 0 && i < Aura.Etiquetas.Length ? Aura.Etiquetas[i] : $"#{i}";

        // ------------------------------------------------------------ ejecución de pasos

        void IniciarPaso()
        {
            if (!_sesion || _corriendo || _actual >= _pasos.Count) return;
            var p = _pasos[_actual];
            p.estado = "en curso";
            p.resultado = "";
            _corriendo = true;
            _tPaso = Time.realtimeSinceStartup;
            EventMarkers.Send($"protocolo/paso/inicio|n={_actual + 1}|tipo={p.tipo}|act={p.actividad}|disp={(p.robot ? "robot" : "oculus")}");
            switch (p.tipo)
            {
                case Tipo.Senal:
                    MensajeParticipante.Mostrar("Preparación", "Estamos preparando la sesión.\nPermanece sentado y relajado.");
                    break;
                case Tipo.BaseAbiertos: StartCoroutine(LineaBase(p, false)); break;
                case Tipo.BaseCerrados: StartCoroutine(LineaBase(p, true)); break;
                case Tipo.Calibracion: StartCoroutine(Actividad(p, true)); break;
                case Tipo.Actividad: StartCoroutine(Actividad(p, false)); break;
                case Tipo.Tlx: IniciarTlx(p); break;
                case Tipo.Descanso: StartCoroutine(Descanso(p)); break;
                case Tipo.CambioRobot:
                    IrAlMenu();
                    MensajeParticipante.Mostrar("Cambio de dispositivo",
                        "Deja los mandos de Oculus.\nAhora usarás el robot háptico con la mano derecha.\nEspera la indicación del investigador.");
                    break;
                case Tipo.Fin:
                    IrAlMenu();
                    MensajeParticipante.Mostrar("¡Gracias!", "La sesión ha terminado.\nEl investigador te ayudará a retirar el equipo.");
                    CompletarPaso("ok");
                    TerminarSesion("completada");
                    break;
            }
        }

        void CompletarPaso(string resultado, string estado = "hecho")
        {
            if (_actual >= _pasos.Count) return;
            var p = _pasos[_actual];
            p.estado = estado;
            p.resultado = resultado;
            _corriendo = false;
            float ini = _tPaso - _t0Sesion, fin = Time.realtimeSinceStartup - _t0Sesion;
            _filasPasos.Add(string.Format(CultureInfo.InvariantCulture, "{0};{1};{2};{3:0.0};{4:0.0};{5};{6}",
                _actual + 1, p.tipo, p.titulo, ini, fin, estado, resultado.Replace(';', ',')));
            if (_sesion) File.WriteAllLines(Path.Combine(_dir, "pasos.csv"), _filasPasos);
            EventMarkers.Send($"protocolo/paso/fin|n={_actual + 1}|tipo={p.tipo}|estado={estado}");
            if (p.tipo != Tipo.Fin) _actual = Mathf.Min(_actual + 1, _pasos.Count - 1);
        }

        void SaltarPaso()
        {
            if (!_sesion) return;
            StopAllCoroutines();
            if (_pasos[_actual].tipo == Tipo.Actividad || _pasos[_actual].tipo == Tipo.Calibracion) FinActividad("saltada");
            _corriendo = true; _tPaso = Time.realtimeSinceStartup;
            CompletarPaso("saltado", "saltado");
            MensajeParticipante.Ocultar();
        }

        void IrAlMenu()
        {
            if (SceneManager.GetActiveScene().name != GameFlow.MenuScene) GameFlow.LoadMenu();
            StartCoroutine(SinLaser());
        }

        IEnumerator SinLaser()
        {
            yield return null; yield return null;
            var laser = FindAnyObjectByType<ControllerLaser>();
            if (laser != null) laser.SetActive(false);   // que el participante no arranque niveles por su cuenta
        }

        // línea base
        IEnumerator LineaBase(Paso p, bool cerrados)
        {
            IrAlMenu();
            float dur = F(_sBase, 120f);
            _durPaso = dur;
            if (cerrados)
                MensajeParticipante.Mostrar("Línea base · ojos cerrados", "Cierra los ojos y relájate.\nNo te muevas.\nÁbrelos cuando escuches el tono.");
            else
                MensajeParticipante.Mostrar("Mira la cruz", "+", true);
            yield return new WaitForSecondsRealtime(1.5f);
            Aura.EmpezarRegistro();
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < dur) yield return null;
            var r = Aura.TerminarRegistro();
            AudioFX.Complete();
            string res;
            if (!cerrados)
            {
                if (r.validas >= 10) { Aura.Linea = r.razon; _alfaAbiertos = r.alfa; }
                res = $"razón base {r.razon:0.000} · {r.validas}/{r.totales} ventanas válidas" + (r.validas < 10 ? " · ¡POCAS VÁLIDAS, no se fijó!" : "");
                EventMarkers.Send($"protocolo/base/abiertos|razon={r.razon:0.0000}|validas={r.validas}|totales={r.totales}");
            }
            else
            {
                float reac = _alfaAbiertos > 0f ? r.alfa / _alfaAbiertos : 0f;
                res = $"alfa OC/OA = {reac:0.00} (esperado > 1.3) · {r.validas}/{r.totales} válidas";
                EventMarkers.Send($"protocolo/base/cerrados|reactividad_alfa={reac:0.000}|validas={r.validas}");
            }
            MensajeParticipante.Mostrar("Bien", "Puedes abrir los ojos.\nEspera la siguiente indicación.");
            CompletarPaso(res);
        }

        // descanso
        IEnumerator Descanso(Paso p)
        {
            IrAlMenu();
            float dur = F(_sDescanso, 30f);
            _durPaso = dur;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < dur)
            {
                MensajeParticipante.Mostrar("Descanso", $"Relájate un momento.\n\n{Mathf.CeilToInt(dur - (Time.realtimeSinceStartup - t0))} s");
                yield return new WaitForSecondsRealtime(0.25f);
            }
            MensajeParticipante.Mostrar("Listo", "Espera la siguiente indicación.");
            CompletarPaso("ok");
        }

        // actividad / calibración
        IEnumerator Actividad(Paso p, bool calibracion)
        {
            MensajeParticipante.Ocultar();
            _pasoAct = p;
            float dFija = F(_sDFija, 0.5f);
            int distr = _distractores;
            string condicion;
            _replay = null;

            if (calibracion)
            {
                dFija = p.dificil ? 1f : 0f;
                distr = p.dificil ? 3 : 0;
                condicion = p.dificil ? "calibracion_dificil" : "calibracion_facil";
                AdaptiveDifficultyController.AdaptacionHabilitada = false;
            }
            else if (_estudio == Estudio.Caracterizacion)
            {
                condicion = "fija";
                AdaptiveDifficultyController.AdaptacionHabilitada = false;
            }
            else if (_grupo == Grupo.Adaptativo)
            {
                condicion = "adaptativa";
                AdaptiveDifficultyController.AdaptacionHabilitada = true;
            }
            else
            {
                condicion = "acoplada";
                AdaptiveDifficultyController.AdaptacionHabilitada = false;
                _replay = CargarReplay(p.Clave);
                if (_replay == null) condicion = "acoplada_sin_fuente(fija)";
            }

            // preferencias que leen los componentes al cargar la escena
            DistractorManager.NivelPref = distr;
            DistractorManager.AdaptativoPref = false;
            TareaSecundaria.Pref = _tarea2 && !calibracion;
            KeyboardControls.Active = _modoTeclado;

            _actScore.Clear(); _actRazon.Clear(); _trazaD.Clear(); _ventanaScores.Clear();
            _actOptima = _actMuestras = 0;
            _ultimo = null;
            EscribirResumenPendiente();
            var res = new Resumen { clave = p.Clave, actividad = p.actividad, robot = p.robot, condicion = condicion };

            _escenaEsperada = Escena(p.actividad);
            _configurarAlCargar = true;
            _dFijaPendiente = _replay != null && _replay.Count > 0 ? _replay[0].d : dFija;
            GameFlow.Load(_escenaEsperada);
            while (_configurarAlCargar) yield return null;
            yield return null;
            if (p.robot) ActivarRobot();
            _tAct = Time.realtimeSinceStartup;
            int vTot0 = Aura.VentanasTotales, vVal0 = Aura.VentanasValidas;
            float max = calibracion ? F(_sCalib, 90f) : F(_sMaxAct, 480f);
            _durPaso = max;
            if (calibracion) Aura.EmpezarRegistro();

            string fin = "completada";
            TrainingLevel lvl = null;
            while (true)
            {
                if (SceneManager.GetActiveScene().name != _escenaEsperada) { fin = "salio_de_la_escena"; break; }
                if (lvl == null) lvl = FindAnyObjectByType<TrainingLevel>();
                if (lvl != null && lvl.Finished) break;
                float t = Time.realtimeSinceStartup - _tAct;
                if (t >= max) { fin = calibracion ? "tiempo_bloque" : "tiempo_maximo"; break; }
                if (_ctl != null)
                {
                    if (_replay != null) _ctl.ObjetivoDificultad(DReplay(t));
                    if (_trazaD.Count == 0 || t - _ultimaTraza >= 0.5f)
                    {
                        _ultimaTraza = t;
                        _trazaD.Add(string.Format(CultureInfo.InvariantCulture, "{0:0.00};{1:0.0000}", t, _ctl.Difficulty));
                    }
                }
                yield return null;
            }

            // métricas
            var m = MetricsRecorder.I;
            if (m != null && lvl != null)
            {
                var sc = m.Evaluate(lvl.parTimeSec, lvl.parPathMeters, lvl.maxErrors);
                res.t = sc.time; res.recorrido = sc.path; res.errores = sc.errorCount; res.precision = sc.avgPrecisionMm; res.estrellas = sc.stars;
            }
            else res.t = Time.realtimeSinceStartup - _tAct;
            res.fin = fin;
            res.razon = Media(_actRazon); res.score = Media(_actScore);
            res.pctOptima = _actMuestras > 0 ? 100f * _actOptima / _actMuestras : 0f;
            int vTot = Aura.VentanasTotales - vTot0, vVal = Aura.VentanasValidas - vVal0;
            res.pctValida = vTot > 0 ? 100f * vVal / vTot : 0f;
            if (_ctl != null) { res.dFinal = _ctl.Difficulty; res.dMedia = MediaD(); }
            var t2 = DistractorManager.I != null ? DistractorManager.I.Tarea2 : null;
            if (t2 != null) { res.t2Est = t2.Estimulos; res.t2Ac = t2.Aciertos; res.t2Om = t2.Omisiones; res.t2Fa = t2.Falsas; res.t2Rt = t2.RtMedio; }

            if (_trazaD.Count > 0)
                File.WriteAllLines(Path.Combine(_dir, $"dificultad_{p.Clave}{(calibracion ? (p.dificil ? "_calibD" : "_calibF") : "")}.csv"),
                                   new[] { "t;d" }.Concat(_trazaD));

            string resultado;
            if (calibracion)
            {
                var r = Aura.TerminarRegistro();
                float s = r.validas > 0 ? r.razon / Mathf.Max(1e-6f, Aura.Linea) : -1f;
                if (p.dificil) _scoreDificil = s; else _scoreFacil = s;
                resultado = $"score medio {s:0.000} ({r.validas}/{r.totales} válidas)";
                if (p.dificil) resultado += " · " + CalcularUmbrales();
                EventMarkers.Send($"protocolo/calibracion/{(p.dificil ? "dificil" : "facil")}|score={s:0.0000}|validas={r.validas}");
            }
            else
            {
                _ultimo = res;
                resultado = $"{fin} · {res.t:0}s · err {res.errores:0} · ★{res.estrellas:0} · score {res.score:0.00} · óptima {res.pctOptima:0}%";
            }
            FinActividad(fin);
            CompletarPaso(resultado, fin == "salio_de_la_escena" ? "incompleto" : "hecho");
        }

        float _ultimaTraza, _dFijaPendiente;

        void FinActividad(string motivo)
        {
            AdaptiveDifficultyController.AdaptacionHabilitada = false;
            _ctl = null;
            _pasoAct = null;
            IrAlMenu();
            MensajeParticipante.Mostrar("Actividad terminada", "Espera la siguiente indicación.");
        }

        string CalcularUmbrales()
        {
            if (_scoreFacil <= 0f || _scoreDificil <= 0f) return "sin datos suficientes: se mantienen los umbrales";
            float dif = _scoreDificil - _scoreFacil;
            if (dif < 0.05f) return $"fácil {_scoreFacil:0.00} y difícil {_scoreDificil:0.00} no se separan: se mantienen los umbrales";
            float lo = _scoreFacil + 0.3f * dif, hi = _scoreFacil + 0.7f * dif;
            _sLo = lo.ToString("0.000", CultureInfo.InvariantCulture);
            _sHi = hi.ToString("0.000", CultureInfo.InvariantCulture);
            File.AppendAllText(Path.Combine(_dir, "sesion.txt"), $"\numbral_bajo_calibrado={_sLo}\numbral_alto_calibrado={_sHi}\n");
            EventMarkers.Send($"protocolo/umbrales|bajo={_sLo}|alto={_sHi}");
            return $"umbrales calibrados: bajo {_sLo} · alto {_sHi}";
        }

        void AlCargarEscena(Scene s, LoadSceneMode m)
        {
            if (!_configurarAlCargar || s.name != _escenaEsperada) return;
            // antes de los Start de la escena: el lazo y la dificultad quedan listos
            foreach (var ls in FindObjectsByType<LoadSource>(FindObjectsSortMode.None)) ls.enabled = false;
            _ctl = FindAnyObjectByType<AdaptiveDifficultyController>();
            if (_ctl != null)
            {
                _ctl.thetaLo = F(_sLo, 1.15f);
                _ctl.thetaHi = F(_sHi, 1.6f);
                _ctl.FijarDificultad(_dFijaPendiente);
            }
            _configurarAlCargar = false;
        }

        void ActivarRobot()
        {
            foreach (var mi in FindObjectsByType<MasterInput>(FindObjectsSortMode.None))
                if (mi.node == XRNode.RightHand && mi.ActiveBackend != MasterInput.Backend.TouchHaptic)
                {
                    mi.Touch.Reintentar();
                    mi.SetBackend(MasterInput.Backend.TouchHaptic);
                }
        }

        List<(float t, float d)> CargarReplay(string clave)
        {
            if (string.IsNullOrEmpty(_fuenteAcoplado)) return null;
            string f = Path.Combine(_fuenteAcoplado, $"dificultad_{clave}.csv");
            if (!File.Exists(f)) return null;
            var l = new List<(float, float)>();
            foreach (var linea in File.ReadAllLines(f).Skip(1))
            {
                var c = linea.Split(';');
                if (c.Length >= 2 &&
                    float.TryParse(c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float t) &&
                    float.TryParse(c[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float d)) l.Add((t, d));
            }
            return l.Count > 0 ? l : null;
        }

        float DReplay(float t)
        {
            float d = _replay[0].d;
            foreach (var x in _replay) { if (x.t > t) break; d = x.d; }
            return d;
        }

        float MediaD()
        {
            if (_trazaD.Count == 0) return 0f;
            float s = 0f;
            foreach (var row in _trazaD) s += float.Parse(row.Split(';')[1], CultureInfo.InvariantCulture);
            return s / _trazaD.Count;
        }

        static float Media(List<float> l) { if (l.Count == 0) return 0f; float s = 0; foreach (var x in l) s += x; return s / l.Count; }

        // cada ventana de EEG (0.5 s)
        void AlCalcular(float score)
        {
            if (!_sesion) return;
            var p = _actual < _pasos.Count ? _pasos[_actual] : null;
            string zona = _ctl != null ? _ctl.CurrentZone.ToString() : "";
            float d = _ctl != null ? _ctl.Difficulty : -1f;
            _carga?.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:0.00};{1:0.000};{2};{3};{4};{5};{6:0.000000};{7:0.000000};{8:0.0000};{9:0.0000};{10};{11:0.000}",
                Time.realtimeSinceStartup - _t0Sesion, EventMarkers.Ahora(), _actual + 1, _corriendo ? p?.tipo.ToString() : "espera",
                p?.actividad, p != null && p.robot ? "robot" : "oculus",
                Aura.ThetaF, Aura.AlfaP, Aura.Razon, score, zona, d));

            if (_pasoAct == null || _ctl == null) return;
            _actScore.Add(score); _actRazon.Add(Aura.Razon);
            // el lazo espera ~1 ventana cada 2 s: se le pasa la media de 4 pasos
            _ventanaScores.Enqueue(score);
            if (_ventanaScores.Count >= 4)
            {
                float s = 0f; foreach (var x in _ventanaScores) s += x;
                _ventanaScores.Clear();
                _ctl.SubmitLoadScore(s / 4f);
                _actMuestras++;
                if (_ctl.CurrentZone == AdaptiveDifficultyController.Zone.Optima) _actOptima++;
            }
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f4Key.wasPressedThisFrame)
            {
                _visible = !_visible;
                PlayerPrefs.SetInt(KeyVisible, _visible ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        // NASA-TLX
        void IniciarTlx(Paso p)
        {
            IrAlMenu();
            _tlxDim = 0;
            for (int i = 0; i < 6; i++) _tlx[i] = 50f;
            MostrarTlx();
        }

        void MostrarTlx()
        {
            MensajeParticipante.Mostrar($"NASA-TLX · {_tlxDim + 1}/6 · {DimTlx[_tlxDim]}",
                DescTlx[_tlxDim] + "\n\nDi un número del 0 al 100\n(de 5 en 5)");
        }

        void TerminarTlx()
        {
            float total = _tlx.Average();
            if (_ultimo != null) { _ultimo.tlx = (float[])_tlx.Clone(); EscribirResumenPendiente(); }
            EventMarkers.Send(string.Format(CultureInfo.InvariantCulture,
                "protocolo/tlx|act={0}|mental={1:0}|fisica={2:0}|temporal={3:0}|rendimiento={4:0}|esfuerzo={5:0}|frustracion={6:0}|total={7:0.0}",
                _pasos[_actual].actividad, _tlx[0], _tlx[1], _tlx[2], _tlx[3], _tlx[4], _tlx[5], total));
            MensajeParticipante.Mostrar("Gracias", "Espera la siguiente indicación.");
            CompletarPaso($"TLX total {total:0.0}");
        }

        void EscribirResumenPendiente()
        {
            if (_ultimo == null || _ultimo.escrito || !_sesion) return;
            var r = _ultimo;
            var ci = CultureInfo.InvariantCulture;
            string tl = r.tlx != null ? string.Join(";", r.tlx.Select(x => x.ToString("0", ci))) + ";" + r.tlx.Average().ToString("0.0", ci) : ";;;;;;";
            File.AppendAllText(Path.Combine(_dir, "resumen_actividades.csv"), string.Format(ci,
                "{0};{1};{2};{3};{4};{5:0.0};{6:0.00};{7:0};{8:0.0};{9:0};{10:0.0000};{11:0.0000};{12:0.0};{13:0.0};{14:0.000};{15:0.000};{16};{17};{18};{19};{20:0.000};{21}\n",
                r.clave, r.actividad, r.robot ? "robot" : "oculus", r.condicion, r.fin, r.t, r.recorrido, r.errores, r.precision, r.estrellas,
                r.razon, r.score, r.pctOptima, r.pctValida, r.dMedia, r.dFinal, r.t2Est, r.t2Ac, r.t2Om, r.t2Fa, r.t2Rt, tl));
            r.escrito = true;
        }

        // ------------------------------------------------------------ interfaz (OnGUI)

        GUIStyle _h, _b, _small, _ok, _warn, _bad, _box;

        void Estilos()
        {
            if (_h != null) return;
            _h = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            _h.normal.textColor = new Color(0.65f, 0.85f, 1f);
            _b = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            _small.normal.textColor = new Color(0.75f, 0.75f, 0.78f);
            _ok = new GUIStyle(_b); _ok.normal.textColor = new Color(0.45f, 1f, 0.5f);
            _warn = new GUIStyle(_b); _warn.normal.textColor = new Color(1f, 0.85f, 0.3f);
            _bad = new GUIStyle(_b); _bad.normal.textColor = new Color(1f, 0.45f, 0.4f);
            _box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(8, 8, 6, 6) };
        }

        void OnGUI()
        {
            if (!PanelVisible) return;
            Estilos();
            float esc = Mathf.Clamp(Screen.height / 900f, 1f, 2f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(esc, esc, 1f));
            float w = 470f, h = Screen.height / esc;
            float x = Screen.width / esc - w - 6f;
            GUI.color = new Color(1f, 1f, 1f, 0.97f);
            GUI.Box(new Rect(x - 4, 2, w + 8, h - 4), GUIContent.none);
            GUILayout.BeginArea(new Rect(x, 6, w, h - 12));
            GUILayout.Label("Protocolo experimental · panel del investigador (F4)", _h);

            PanelEeg();
            GUILayout.Space(4);
            if (!_sesion) PanelConfig(); else PanelPasos();

            GUILayout.EndArea();
            GUI.matrix = Matrix4x4.identity;
        }

        void PanelEeg()
        {
            GUILayout.BeginVertical(_box);
            GUILayout.BeginHorizontal();
            var aura = Aura;
            string est = aura.Conectado
                ? (aura.SegundosSinDatos > 2f ? $"Conectado a {aura.NombreStream} · SIN DATOS {aura.SegundosSinDatos:0}s" : $"{aura.NombreStream} · {aura.Canales} ch · {aura.Fs:0} Hz")
                : _estadoEeg;
            GUILayout.Label("EEG: " + est, aura.Conectado && aura.SegundosSinDatos < 2f ? _ok : _warn);
            if (GUILayout.Button(aura.Conectado ? "Reconectar" : "Buscar AURA", GUILayout.Width(110))) _estadoEeg = aura.Buscar();
            GUILayout.EndHorizontal();

            if (!aura.Conectado || aura.SegundosSinDatos > 2f)
                for (int i = 0; i < aura.Disponibles.Count; i++)
                {
                    var d = aura.Disponibles[i];
                    if (GUILayout.Button($"Conectar: {d.nombre} [{d.tipo}] {d.canales} ch {d.fs:0} Hz · {d.host}"))
                    {
                        _estadoEeg = aura.Conectar(i);
                        _fMap = string.Join(",", aura.Frontales); _pMap = string.Join(",", aura.Parietales);
                    }
                }

            if (aura.Conectado)
            {
                // calidad por canal
                var sb = new StringBuilder();
                GUILayout.BeginHorizontal();
                for (int c = 0; c < aura.Canales && c < aura.Desviacion.Length; c++)
                {
                    float sd = aura.Desviacion[c];
                    bool uso = System.Array.IndexOf(aura.Frontales, c) >= 0 || System.Array.IndexOf(aura.Parietales, c) >= 0;
                    var st = sd < aura.desviacionMin ? _bad : sd > aura.amplitudMax * 0.5f ? _warn : _ok;
                    GUILayout.Label($"{(uso ? "●" : "○")}{Etq(c)}\n{sd:0.0}", st, GUILayout.Width(52));
                    if ((c + 1) % 8 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                }
                GUILayout.EndHorizontal();
                GUILayout.Label($"Frontales: {string.Join(" ", aura.Frontales.Select(Etq))} · Parietales: {string.Join(" ", aura.Parietales.Select(Etq))}" +
                                (aura.EtiquetasDelStream ? "" : aura.EtiquetasDelMontaje ? "  (montaje AURA: Fp1 Fp2 F3 Fz F4 P3 Pz P4)" : "  (sin etiquetas: revisa el mapeo)"), _small);
                if (!aura.EtiquetasDelStream && !aura.EtiquetasDelMontaje && !_sesion)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Índices F:", _small, GUILayout.Width(60)); _fMap = GUILayout.TextField(_fMap, GUILayout.Width(70));
                    GUILayout.Label("P:", _small, GUILayout.Width(18)); _pMap = GUILayout.TextField(_pMap, GUILayout.Width(70));
                    if (GUILayout.Button("Aplicar", GUILayout.Width(70))) aura.GuardarMapeo(_fMap, _pMap);
                    GUILayout.EndHorizontal();
                }
                GUILayout.Label($"θ/α = {aura.Razon:0.000} · score = {aura.Score:0.00} (base {aura.Linea:0.000}) · válidas {aura.FraccionValida * 100f:0}% · " +
                                (aura.VentanaValida ? "ventana OK" : "ARTEFACTO"), aura.VentanaValida ? _b : _warn);
                Grafica(aura.Historial, F(_sLo, 1.15f), F(_sHi, 1.6f));
                if (_ctl != null)
                    GUILayout.Label($"Lazo: zona {_ctl.CurrentZone} · dificultad {_ctl.Difficulty:0.00} · adaptación {(AdaptiveDifficultyController.AdaptacionHabilitada ? "ON" : "OFF")}", _small);
            }
            GUILayout.EndVertical();
        }

        void Grafica(List<float> datos, float lo, float hi)
        {
            Rect r = GUILayoutUtility.GetRect(440, 60);
            GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(r, _px);
            float max = Mathf.Max(hi * 1.4f, 2f);
            float Y(float v) => r.yMax - Mathf.Clamp01(v / max) * r.height;
            GUI.color = new Color(0.3f, 1f, 0.4f, 0.12f); GUI.DrawTexture(new Rect(r.x, Y(hi), r.width, Y(lo) - Y(hi)), _px);
            GUI.color = new Color(1f, 1f, 1f, 0.25f); GUI.DrawTexture(new Rect(r.x, Y(1f), r.width, 1), _px);
            if (datos.Count > 1)
            {
                float dx = r.width / 120f;
                for (int i = 0; i < datos.Count; i++)
                {
                    float v = datos[i];
                    GUI.color = v > hi ? new Color(1f, 0.45f, 0.4f) : v < lo ? new Color(0.5f, 0.7f, 1f) : new Color(0.45f, 1f, 0.5f);
                    GUI.DrawTexture(new Rect(r.x + i * dx, Y(v) - 1, Mathf.Max(1f, dx), 3), _px);
                }
            }
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x + 2, r.y, 300, 16), $"score (zona verde = óptima {lo:0.00}–{hi:0.00}; línea = reposo)", _small);
        }

        void PanelConfig()
        {
            GUILayout.BeginVertical(_box);
            _scrollConfig = GUILayout.BeginScrollView(_scrollConfig, GUILayout.ExpandHeight(true));
            GUILayout.Label("Nueva sesión", _h);
            Campo("ID participante", ref _id);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_estudio == Estudio.Caracterizacion, " Estudio 1 · Caracterización", "Button")) _estudio = Estudio.Caracterizacion;
            if (GUILayout.Toggle(_estudio == Estudio.Comparativo, " Estudio 2 · Comparativo", "Button")) _estudio = Estudio.Comparativo;
            GUILayout.EndHorizontal();
            GUILayout.Label(_estudio == Estudio.Caracterizacion
                ? "Dificultad fija, sin adaptación. Mide la carga que produce cada actividad."
                : "Calibra umbrales personales y compara adaptativo contra acoplado.", _small);

            if (_estudio == Estudio.Comparativo)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Toggle(_grupo == Grupo.Adaptativo, " Grupo ADAPTATIVO", "Button")) _grupo = Grupo.Adaptativo;
                if (GUILayout.Toggle(_grupo == Grupo.Acoplado, " Grupo ACOPLADO (yoked)", "Button")) _grupo = Grupo.Acoplado;
                GUILayout.EndHorizontal();
                if (_grupo == Grupo.Acoplado) SelectorFuente();
            }

            GUILayout.Space(4);
            Campo("Actividades con Oculus", ref _actOculus);
            Campo("Actividades con robot", ref _actRobot);
            GUILayout.Label("Números separados por comas (1-5 y 7; la 6 no tiene final automático).", _small);
            Campo("Línea base (s)", ref _sBase);
            _baseCerrados = GUILayout.Toggle(_baseCerrados, " Incluir línea base con ojos cerrados (control de reactividad alfa)");
            Campo("Descanso (s)", ref _sDescanso);
            Campo("Máximo por actividad (s)", ref _sMaxAct);
            if (_estudio == Estudio.Comparativo) Campo("Bloque de calibración (s)", ref _sCalib);
            Campo("Dificultad fija / inicial (0-1)", ref _sDFija);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Distractores fijos: {_distractores}", _b, GUILayout.Width(170));
            _distractores = Mathf.RoundToInt(GUILayout.HorizontalSlider(_distractores, 0, 3, GUILayout.Width(150)));
            GUILayout.EndHorizontal();
            _tarea2 = GUILayout.Toggle(_tarea2, " Tarea secundaria (tiempo de reacción)");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Umbrales score (bajo/alto):", _b, GUILayout.Width(170));
            _sLo = GUILayout.TextField(_sLo, GUILayout.Width(60)); _sHi = GUILayout.TextField(_sHi, GUILayout.Width(60));
            GUILayout.EndHorizontal();
            if (_estudio == Estudio.Comparativo) GUILayout.Label("En el Estudio 2 la calibración los reemplaza por los de la persona.", _small);
            _modoTeclado = GUILayout.Toggle(_modoTeclado, " Modo prueba con teclado (piloto sin visor)");
            GUILayout.EndScrollView();

            bool listo = !string.IsNullOrEmpty(Limpio(_id));
            if (!Aura.Conectado) GUILayout.Label("Conecta el AURA antes de iniciar (se puede iniciar sin EEG para probar el flujo).", _warn);
            GUI.enabled = listo;
            if (GUILayout.Button("▶ Iniciar sesión", GUILayout.Height(30))) IniciarSesion();
            GUI.enabled = true;
            if (GUILayout.Button("Abrir carpeta de sesiones")) AbrirCarpeta(RaizSesiones);
            GUILayout.EndVertical();
        }

        void SelectorFuente()
        {
            string raiz = RaizSesiones;
            var fuentes = Directory.Exists(raiz) ? Directory.GetDirectories(raiz, "*_E2adapt_*").OrderBy(d => d).ToArray() : new string[0];
            GUILayout.Label("Participante adaptativo a replicar:", _b);
            if (fuentes.Length == 0) GUILayout.Label("No hay sesiones adaptativas todavía: corre primero el grupo adaptativo.", _warn);
            foreach (var f in fuentes)
                if (GUILayout.Toggle(_fuenteAcoplado == f, " " + Path.GetFileName(f))) _fuenteAcoplado = f;
        }

        void Campo(string etiqueta, ref string valor)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(etiqueta, _b, GUILayout.Width(190));
            valor = GUILayout.TextField(valor);
            GUILayout.EndHorizontal();
        }

        void PanelPasos()
        {
            GUILayout.BeginVertical(_box);
            string grupo = _estudio == Estudio.Comparativo ? $" · grupo {_grupo}" : "";
            GUILayout.Label($"Sesión {_id} · {(_estudio == Estudio.Caracterizacion ? "Estudio 1" : "Estudio 2")}{grupo} · " +
                            $"{(Time.realtimeSinceStartup - _t0Sesion) / 60f:0.0} min", _b);

            _scrollPasos = GUILayout.BeginScrollView(_scrollPasos, GUILayout.Height(200));
            for (int i = 0; i < _pasos.Count; i++)
            {
                var p = _pasos[i];
                string marca = p.estado == "hecho" ? "✔" : p.estado == "en curso" ? "▶" : p.estado == "saltado" ? "↷" : p.estado == "incompleto" ? "✖" : "·";
                var st = i == _actual ? _ok : p.estado == "pendiente" ? _small : _b;
                GUILayout.Label($"{marca} {i + 1}. {p.titulo}" + (string.IsNullOrEmpty(p.resultado) ? "" : $"  — {p.resultado}"), st);
            }
            GUILayout.EndScrollView();

            var actual = _pasos[_actual];
            GUILayout.Label($"Paso actual: {_actual + 1}. {actual.titulo}", _h);
            GUILayout.Label(Instruccion(actual), _small);
            if (_corriendo && _durPaso > 0f && (actual.tipo == Tipo.BaseAbiertos || actual.tipo == Tipo.BaseCerrados ||
                                                actual.tipo == Tipo.Descanso || actual.tipo == Tipo.Actividad || actual.tipo == Tipo.Calibracion))
            {
                float t = Time.realtimeSinceStartup - (actual.tipo == Tipo.Actividad || actual.tipo == Tipo.Calibracion ? _tAct : _tPaso);
                GUILayout.Label($"Tiempo: {t:0} / {_durPaso:0} s", _b);
            }

            if (actual.tipo == Tipo.Tlx && _corriendo) PanelTlx();
            else
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = !_corriendo && actual.tipo != Tipo.Fin || (actual.tipo == Tipo.Fin && _sesion && !_corriendo);
                if (GUILayout.Button(_corriendo ? "En curso…" : "▶ Iniciar paso", GUILayout.Height(28))) IniciarPaso();
                GUI.enabled = _corriendo && (actual.tipo == Tipo.Senal || actual.tipo == Tipo.CambioRobot);
                if (GUILayout.Button("✔ Listo", GUILayout.Height(28)))
                {
                    if (actual.tipo == Tipo.Senal)
                        CompletarPaso(Aura.Conectado ? $"{Aura.NombreStream}, válidas {Aura.FraccionValida * 100f:0}%" : "SIN EEG");
                    else CompletarPaso("ok");
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Saltar paso")) SaltarPaso();
            if (GUILayout.Button("◀ Paso anterior") && !_corriendo) _actual = Mathf.Max(0, _actual - 1);
            if (GUILayout.Button("Carpeta")) AbrirCarpeta(_dir);
            if (GUILayout.Button("Terminar sesión")) { StopAllCoroutines(); TerminarSesion("terminada_por_investigador"); IrAlMenu(); }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        string Instruccion(Paso p)
        {
            switch (p.tipo)
            {
                case Tipo.Senal: return "Coloca el AURA, revisa que los canales usados (●) estén en verde y que la gráfica sea estable. Pide al participante que parpadee y apriete la mandíbula para ver el artefacto. Pulsa Listo.";
                case Tipo.BaseAbiertos: return "El participante mira la cruz sin moverse. Al terminar se fija su razón de reposo (score = 1).";
                case Tipo.BaseCerrados: return "Ojos cerrados. Sirve de control: el alfa debería subir respecto a ojos abiertos.";
                case Tipo.Calibracion: return p.dificil ? "Actividad 1 a dificultad máxima con distractores nivel 3. Fija el ancla alta de carga." : "Actividad 1 a dificultad mínima, sin distractores. Fija el ancla baja de carga.";
                case Tipo.Actividad: return (p.robot ? "El participante usa el robot Touch con la mano derecha (embrague: Ctrl o clic derecho; pinza: F o clic izquierdo). " : "El participante usa los mandos de Oculus. ") + "La actividad termina sola al completarse o por tiempo máximo.";
                case Tipo.Tlx: return "Pregunta cada escala en voz alta (el texto aparece en el visor) y registra la respuesta.";
                case Tipo.Descanso: return "Pausa con cuenta atrás. Se puede saltar.";
                case Tipo.CambioRobot: return "Retira los mandos, coloca la mano derecha en el stylus del Touch y comprueba que se mueve el instrumento. Pulsa Listo.";
                default: return "Revisa que los archivos estén en la carpeta de la sesión.";
            }
        }

        void PanelTlx()
        {
            GUILayout.Label($"{_tlxDim + 1}/6 · {DimTlx[_tlxDim]}", _h);
            GUILayout.Label(DescTlx[_tlxDim].Replace("\n", " "), _small);
            GUILayout.BeginHorizontal();
            _tlx[_tlxDim] = Mathf.Round(GUILayout.HorizontalSlider(_tlx[_tlxDim], 0, 100, GUILayout.Width(330)) / 5f) * 5f;
            GUILayout.Label($"{_tlx[_tlxDim]:0}", _h, GUILayout.Width(40));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("−5")) _tlx[_tlxDim] = Mathf.Max(0, _tlx[_tlxDim] - 5);
            if (GUILayout.Button("+5")) _tlx[_tlxDim] = Mathf.Min(100, _tlx[_tlxDim] + 5);
            GUI.enabled = _tlxDim > 0;
            if (GUILayout.Button("◀ Anterior")) { _tlxDim--; MostrarTlx(); }
            GUI.enabled = true;
            if (GUILayout.Button(_tlxDim < 5 ? "Siguiente ▶" : "✔ Guardar TLX", GUILayout.Height(26)))
            {
                if (_tlxDim < 5) { _tlxDim++; MostrarTlx(); } else TerminarTlx();
            }
            GUILayout.EndHorizontal();
        }

        static void AbrirCarpeta(string ruta)
        {
            if (string.IsNullOrEmpty(ruta)) return;
            Directory.CreateDirectory(ruta);
            Application.OpenURL("file:///" + ruta.Replace('\\', '/'));
        }
    }
}

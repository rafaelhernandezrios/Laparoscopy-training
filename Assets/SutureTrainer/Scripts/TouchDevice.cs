using System.Runtime.InteropServices;
using UnityEngine;

namespace SutureTrainer
{
    /// <summary>
    /// Único punto de contacto con el dispositivo 3D Systems Touch vía OpenHaptics.
    ///
    /// Habla directamente con HapticsDirect.dll (el plugin nativo que trae el
    /// "Touch Device Drivers" de 3D Systems, el mismo que usa su HapticDemo).
    /// La API real se verificó decompilando el demo instalado:
    ///
    ///   int  initDevice(string configName)                 -> "Default Device"
    ///   void startSchedulers()
    ///   void getPosition(string dev, double[3] pos_mm)
    ///   void getTransform(string dev, double[16] colMajor)
    ///   void getButtons(string dev, int[] btn, int[] chg, ref int n)
    ///   void setForce(string dev, double[3] force_N, double[3] torque)
    ///   void disconnectAllDevices()
    ///
    /// El proyecto compila SIN el dispositivo: las llamadas nativas están detrás
    /// del símbolo OPENHAPTICS_TOUCH. Para activarlo:
    ///   1) Copia HapticsDirect.dll (y hd.dll) en Assets/Plugins/x86_64/
    ///   2) Edit > Project Settings > Player > Scripting Define Symbols
    ///      -> añade  OPENHAPTICS_TOUCH
    /// </summary>
    public class TouchDevice
    {
        /// <summary>Nombre de configuración en OpenHaptics ("Default Device",
        /// "Right Device", "Left Device"...). Lo fija Touch Setup.</summary>
        public string DeviceName { get; private set; }

        TouchDevice(string nombre) { DeviceName = nombre; }

        // ---- Registro: un objeto por aparato fisico -------------------------
        //
        // Antes esto era una clase estatica cableada a "Default Device", con la
        // calibracion (ancla, origen, escala) tambien estatica. Con dos robots
        // eso no vale: compartirian ancla y punto de reposo. La API nativa si
        // es por aparato -toda funcion recibe configName-, asi que el objeto
        // pasa a ser uno por nombre.
        static readonly System.Collections.Generic.Dictionary<string, TouchDevice> _porNombre =
            new System.Collections.Generic.Dictionary<string, TouchDevice>();

        public static TouchDevice For(string nombre)
        {
            if (string.IsNullOrEmpty(nombre)) nombre = "Default Device";
            if (!_porNombre.TryGetValue(nombre, out var d))
            {
                d = new TouchDevice(nombre);
                _porNombre[nombre] = d;
            }
            return d;
        }

        public static System.Collections.Generic.IEnumerable<TouchDevice> Todos => _porNombre.Values;

        // startSchedulers() NO recibe nombre: arranca el bucle de servo de
        // OpenHaptics para todo el proceso. Se llama una sola vez, tras
        // inicializar el primer aparato; los que se conecten despues entran en
        // el mismo bucle.
        static bool _schedulersArrancados;

        /// <summary>
        /// Cierra TODOS los aparatos. disconnectAllDevices() es global, asi que
        /// no puede colgar del OnDestroy de cada mano: la primera en morir
        /// desconectaria tambien a la otra.
        /// </summary>
        public static void ShutdownAll()
        {
            foreach (var d in _porNombre.Values) d.SilenciarYMarcarCerrado();
            _porNombre.Clear();
            _schedulersArrancados = false;
#if OPENHAPTICS_TOUCH
            try { disconnectAllDevices(); } catch { }
#endif
        }

        public bool Available { get; private set; }

        // ---- Calibración dispositivo -> mundo -------------------------------
        // getPosition entrega milímetros en el marco del dispositivo
        // (right-handed: +X derecha, +Y arriba, +Z hacia el usuario).
        // La pose del stylus se ancla a un punto de mundo (AnchorPos/AnchorRot,
        // normalmente el centro del campo quirúrgico) y se auto-recentra:
        //   worldPos = AnchorPos + AnchorRot * (LocalOffset + (devM - devHome) * scale)
        public Vector3 AnchorPos;
        public Quaternion AnchorRot = Quaternion.identity;
        public Vector3 LocalOffset = new Vector3(0f, 0f, 0f);
        public float WorkspaceScale = 2.5f;
        public Vector3 AxisSign = new Vector3(-1f, 1f, -1f); // right-handed -> Unity (X invertido: izq/der)
        public float ForceScale = 1f;                        // N mundo -> N dispositivo (~3.3 N máx)

        /// <summary>
        /// Invierte el sentido del ÚLTIMO grado del gimbal (el giro sobre el
        /// propio eje del stylus). Cabeceo y guiñada se dejan como están.
        ///
        /// Es el residuo del desajuste que ya se compensa en
        /// RoboticArm.touchRotationOffsetEuler: la conversión de orientación
        /// cambia de mano (det -1) y la de posición no (det +1). Al conjugar por
        /// una reflexión, el sentido de giro se invierte —D·R(n,θ)·D⁻¹ =
        /// R(D·n, −θ) cuando det(D) = −1—, y eso se nota justo en el eje que no
        /// arregló el giro de 180°.
        ///
        /// Se corrige por descomposición swing-twist en vez de con más ángulos
        /// de Euler: así se toca SOLO el giro sobre el eje y no se mueve nada
        /// de la orientación que ya está bien.
        ///
        /// APAGADO tras probarlo: dejó la orientación peor, lo que estaba bien
        /// pasó a estar del revés. O el eje no es el que se supone, o el
        /// residuo no era un giro propio. Se queda implementado para medirlo
        /// eje por eje en vez de seguir tanteando.
        /// </summary>
        public bool InvertRoll = false;

        /// <summary>Eje del stylus sobre el que se mide el giro a invertir.</summary>
        public Vector3 RollAxis = Vector3.forward;

        // Auto-recentrado: la posición del stylus al entrar se toma como origen.
        Vector3 _devHome;
        bool _hasHome;
        public void Recenter() { _hasHome = false; }

        // ---------------------------------------------------------------
        // Resorte del propio aparato (bucle de servo a ~1 kHz). Unity solo
        // mueve el ancla; la fuerza se calcula dentro de OpenHaptics, así que
        // no vibra por la baja frecuencia de Unity. Unidades por calibrar con
        // PruebaResorte (F11): el ancla va en el marco crudo de getPosition.
        // ---------------------------------------------------------------
        readonly double[] _ancla = new double[3];

        /// <summary>Última posición cruda leída (unidades de getPosition, mm).</summary>
        public Vector3 RawPos => new Vector3((float)_pos[0], (float)_pos[1], (float)_pos[2]);

        /// <summary>Punto de mundo -> posición cruda del aparato (mm), inversa de TryReadPose.</summary>
        public Vector3 MundoARaw(Vector3 mundo)
        {
            Vector3 local = Quaternion.Inverse(AnchorRot) * (mundo - AnchorPos);
            Vector3 devM = _devHome + (local - LocalOffset) / Mathf.Max(0.01f, WorkspaceScale);
            return new Vector3(devM.x * AxisSign.x, devM.y * AxisSign.y, devM.z * AxisSign.z) * 1000f;
        }

        /// <summary>Resorte del aparato hacia un ancla cruda. magnitud 0 lo apaga.</summary>
        public bool SetSpringRaw(Vector3 anclaRaw, float magnitud)
        {
#if OPENHAPTICS_TOUCH
            if (!_inited) return false;
            _ancla[0] = anclaRaw.x; _ancla[1] = anclaRaw.y; _ancla[2] = anclaRaw.z;
            try { setSpringValues(DeviceName, _ancla, Mathf.Max(0f, magnitud)); return true; }
            catch (System.EntryPointNotFoundException) { return false; }
#else
            return false;
#endif
        }

        bool _initTried, _inited;

        // Diagnóstico: log de posición y orientación (ponlo en true para verlo).
        public bool VerboseLog = false;
        float _nextLog;

        /// <summary>Permite volver a intentar la conexión (p. ej. tras enchufar el aparato).</summary>
        public void Reintentar() { if (!_inited) _initTried = false; }

        string ReadErrorRaw()
        {
#if OPENHAPTICS_TOUCH
            try
            {
                var sb = new System.Text.StringBuilder(256);
                int c = getHDError(sb, sb.Capacity);
                return c != 0 || sb.Length > 0 ? $"HD error {c}: {sb}" : "sin código de error";
            }
            catch { return "sin detalle"; }
#else
            return "";
#endif
        }

        void EnsureInit()
        {
            if (_initTried) return;
            _initTried = true;
#if OPENHAPTICS_TOUCH
            try
            {
                int r = initDevice(DeviceName);   // >=0 = ok en HapticsDirect
                Debug.Log($"[TouchDevice] initDevice(\"{DeviceName}\") -> {r}");
                // Con un solo Touch, Touch Smart Setup suele llamarlo "Default
                // Device" aunque la escena pida "Right Device": se prueba solo.
                if (r < 0 && DeviceName != "Default Device")
                {
                    string err = ReadErrorRaw();
                    int r2 = initDevice("Default Device");
                    Debug.Log($"[TouchDevice] \"{DeviceName}\" falló ({err}); initDevice(\"Default Device\") -> {r2}");
                    if (r2 >= 0) { DeviceName = "Default Device"; r = r2; }
                }
                if (r >= 0)
                {
                    if (!_schedulersArrancados)
                    {
                        startSchedulers();
                        _schedulersArrancados = true;
                    }
                    _inited = true;
                    Debug.Log($"[TouchDevice] \"{DeviceName}\" inicializado. Schedulers activos.");
                }
                else
                {
                    Debug.LogError($"[TouchDevice] initDevice devolvió {r} ({ReadErrorRaw()}). ¿El Touch está " +
                        "conectado, encendido y calibrado (Touch Smart Setup / Diagnostic)?");
                }
            }
            catch (System.DllNotFoundException e)
            {
                Debug.LogError("[TouchDevice] No se pudo cargar HapticsDirect.dll o su dependencia " +
                    "hd.dll. Deben estar en Assets/Plugins/x86_64 y el driver del Touch instalado. " + e.Message);
            }
            catch (System.EntryPointNotFoundException e)
            {
                Debug.LogError("[TouchDevice] Función no encontrada en HapticsDirect.dll: " + e.Message);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[TouchDevice] Error inicializando OpenHaptics: {e.GetType().Name}: {e.Message}");
                _inited = false;
            }
#else
            _inited = false;
#endif
        }

        /// <summary>Lee la pose del stylus ya convertida a espacio de mundo.</summary>
        public bool TryReadPose(out Vector3 worldPos, out Quaternion worldRot,
                                       out float trigger, out bool secondary)
        {
            worldPos = default; worldRot = Quaternion.identity;
            trigger = 0f; secondary = false;

            EnsureInit();
            if (!_inited) { Available = false; return false; }

#if OPENHAPTICS_TOUCH
            // posición (mm) -> metros -> local -> mundo
            getPosition(DeviceName, _pos);
            Vector3 devM = new Vector3(
                (float)_pos[0] * 0.001f * AxisSign.x,
                (float)_pos[1] * 0.001f * AxisSign.y,
                (float)_pos[2] * 0.001f * AxisSign.z);

            // primer frame válido = origen (el instrumento arranca en el ancla)
            if (!_hasHome) { _devHome = devM; _hasHome = true; }
            Vector3 local = LocalOffset + (devM - _devHome) * WorkspaceScale;

            // orientación (4x4 column-major) -> quaternion
            getTransform(DeviceName, _mat);
            Quaternion devRot = InvertirGiroPropio(MatrixToQuaternion(_mat));

            worldPos = AnchorPos + AnchorRot * local;
            worldRot = AnchorRot * devRot;

            // botones: buttons[i] = estado (1/0) del botón i
            int n = _btn.Length;
            getButtons(DeviceName, _btn, _btnChg, ref n);
            trigger   = _btn[0] != 0 ? 1f : 0f;   // botón delantero -> mandíbulas
            secondary = _btn.Length > 1 && _btn[1] != 0; // botón trasero -> clutch

            // --- diagnóstico: posición cruda (mm) y botones cada 0.5 s ---
            if (VerboseLog && Time.realtimeSinceStartup >= _nextLog)
            {
                _nextLog = Time.realtimeSinceStartup + 0.5f;
                Vector3 e = devRot.eulerAngles;
                Debug.Log($"[TouchDevice] pos(mm)=({_pos[0]:F1}, {_pos[1]:F1}, {_pos[2]:F1})  " +
                          $"rot(euler)=({e.x:F0}, {e.y:F0}, {e.z:F0})  btn0={_btn[0]} btn1={_btn[1]}");
            }

            Available = true;
            return true;
#else
            Available = false;
            return false;
#endif
        }

        /// <summary>Envía una fuerza (espacio de MUNDO, newtons) al stylus.</summary>
        /// <summary>Última fuerza que se PIDIÓ al aparato, en newtons de dispositivo.</summary>
        public Vector3 LastRequested { get; private set; }

        /// <summary>
        /// Fuerza que el aparato dice estar renderizando AHORA. Comparada con
        /// <see cref="LastRequested"/> distingue los dos fallos que se parecen:
        /// que no estemos mandando nada, o que mandemos y no salga.
        /// </summary>
        public Vector3 ReadCurrentForce()
        {
#if OPENHAPTICS_TOUCH
            if (!_inited) return Vector3.zero;
            try
            {
                getCurrentForce(DeviceName, _forceRead);
                return new Vector3((float)_forceRead[0], (float)_forceRead[1], (float)_forceRead[2]);
            }
            catch (System.EntryPointNotFoundException) { return Vector3.zero; }
#else
            return Vector3.zero;
#endif
        }

        /// <summary>
        /// Renderizar por el efecto de fuerza constante en vez de por setForce.
        ///
        /// El paquete oficial HapticsDirect declara setForce y NO lo llama
        /// nunca: toda su salida de fuerza va por efectos. Su propia
        /// documentación lo dice — "Adds an additional force... but in most
        /// cases using an Effect is preferable". Y midiendo con getCurrentForce
        /// se vio por qué: pidiendo 0.45 N por setForce, el aparato renderizaba
        /// 0.000 N. El bucle de servo del wrapper reescribe la fuerza en cada
        /// tick a partir de SU estado de efectos, así que lo que se escribe
        /// desde el hilo principal se pierde.
        ///
        /// Se deja el camino viejo detrás de un interruptor para poder
        /// comparar los dos con getCurrentForce.
        /// </summary>
        public bool UseConstantForceEffect = true;

        public void SetForceWorld(Vector3 worldForce)
        {
            if (!_inited) return;
#if OPENHAPTICS_TOUCH
            Vector3 devForce = Quaternion.Inverse(AnchorRot) * worldForce;

            if (UseConstantForceEffect)
            {
                // Con AxisSign, igual que la posición. Antes se dejaba sin él
                // (getPosition está documentado como "(Unity CSys)"), pero la
                // posición SÍ se convierte con AxisSign en TryReadPose, así que
                // fuerza y posición quedaban en marcos distintos. Medido el
                // 12-11-2026 con la prueba de signos de ReproduccionHaptica:
                // +x de mundo movía el stylus a -x de mundo, y lo mismo en z.
                devForce = Vector3.Scale(devForce, AxisSign);
                float mag = devForce.magnitude * ForceScale;
                Vector3 dir = mag > 1e-5f ? devForce.normalized : Vector3.zero;

                _force[0] = dir.x; _force[1] = dir.y; _force[2] = dir.z;
                LastRequested = dir * mag;

                // Magnitud 0 apaga el efecto: es como lo desactiva el propio
                // plugin (DisableContantForce), no hay llamada aparte.
                setConstantForceValues(DeviceName, _force, mag);
                return;
            }

            _force[0] = devForce.x * AxisSign.x * ForceScale;
            _force[1] = devForce.y * AxisSign.y * ForceScale;
            _force[2] = devForce.z * AxisSign.z * ForceScale;
            LastRequested = new Vector3((float)_force[0], (float)_force[1], (float)_force[2]);
            setForce(DeviceName, _force, _torqueZero);
#endif
        }

        /// <summary>
        /// Aviso breve (acierto, error, tope) por el canal de VIBRACIÓN.
        ///
        /// Va por un efecto distinto al de la fuerza constante a propósito.
        /// Antes los avisos se mandaban con SetForceWorld, o sea escribiendo en
        /// el MISMO efecto que usa el lazo de contacto: un aviso de error metía
        /// 2.25 N por el eje del stylus y pisaba la fuerza del tejido hasta que
        /// el siguiente FixedUpdate la reescribía. Eso es el tirón que se
        /// notaba en cada error.
        ///
        /// La vibración es lo que el aparato ofrece para notificar: no compite
        /// con la fuerza sostenida y se apaga sola al cumplirse el tiempo.
        /// </summary>
        public void Pulse(Vector3 dir, float magnitude, float freqHz, float seconds)
        {
#if OPENHAPTICS_TOUCH
            if (!_inited) return;
            if (dir.sqrMagnitude < 1e-8f) dir = Vector3.forward;
            dir.Normalize();

            _vibDir[0] = dir.x; _vibDir[1] = dir.y; _vibDir[2] = dir.z;
            try { setVibrationValues(DeviceName, _vibDir, magnitude, freqHz, seconds); }
            catch (System.EntryPointNotFoundException) { }
#endif
        }

        /// <summary>Último error del driver, o cadena vacía.</summary>
        public string ReadError()
        {
#if OPENHAPTICS_TOUCH
            if (!_inited) return "";
            try
            {
                var sb = new System.Text.StringBuilder(256);
                int r = getHDError(sb, sb.Capacity);
                return r == 0 ? "" : $"({r}) {sb}";
            }
            catch (System.EntryPointNotFoundException) { return ""; }
#else
            return "";
#endif
        }

        /// <summary>
        /// Deja este aparato en silencio (fuerza y vibración a cero) sin
        /// desconectar a nadie.
        ///
        /// Antes esto llamaba a disconnectAllDevices(), que es global, y colgaba
        /// del OnDestroy de cada mano. Con un solo robot pasaba desapercibido;
        /// con dos, la primera mano en destruirse desconectaría también la otra.
        /// Desconectar es cosa de <see cref="ShutdownAll"/>.
        /// </summary>
        void SilenciarYMarcarCerrado()
        {
#if OPENHAPTICS_TOUCH
            if (_inited)
            {
                try
                {
                    if (UseConstantForceEffect)
                        setConstantForceValues(DeviceName, _force, 0.0);
                    else
                        setForce(DeviceName, _torqueZero, _torqueZero);
                    setVibrationValues(DeviceName, _vibDir, 0.0, 0.0, 0.0);
                    try { setSpringValues(DeviceName, _ancla, 0.0); } catch (System.EntryPointNotFoundException) { }
                }
                catch { }
            }
#endif
            _inited = false; _initTried = false; Available = false;
        }

        public void Shutdown() => SilenciarYMarcarCerrado();

        // buffers reutilizados
        readonly double[] _pos = new double[3];
        readonly double[] _mat = new double[16];
        readonly double[] _force = new double[3];
        readonly double[] _forceRead = new double[3];
        readonly double[] _vibDir = new double[3];
        readonly double[] _torqueZero = new double[3];
        readonly int[] _btn = new int[8];
        readonly int[] _btnChg = new int[8];

        // Invierte los ejes de orientación que se toman del gimbal (para tuning).
        public Vector3 RotAxisSign = new Vector3(1f, 1f, -1f);

        /// <summary>
        /// Devuelve la misma orientación pero con el giro sobre
        /// <see cref="RollAxis"/> en sentido contrario.
        ///
        /// Descomposición swing-twist: la parte vectorial del cuaternión se
        /// proyecta sobre el eje, lo que da el "twist" (giro propio); el resto
        /// es el "swing" (hacia dónde apunta). Invertir solo el twist deja el
        /// apuntado intacto, que es justo lo que hace falta aquí.
        /// </summary>
        Quaternion InvertirGiroPropio(Quaternion q)
        {
            if (!InvertRoll) return q;

            Vector3 eje = RollAxis.sqrMagnitude > 1e-8f ? RollAxis.normalized : Vector3.forward;
            Vector3 proyeccion = Vector3.Project(new Vector3(q.x, q.y, q.z), eje);

            var twist = new Quaternion(proyeccion.x, proyeccion.y, proyeccion.z, q.w);
            float n = twist.x * twist.x + twist.y * twist.y +
                      twist.z * twist.z + twist.w * twist.w;

            // Stylus perpendicular al eje: el giro propio es indefinido y no hay
            // nada que invertir.
            if (n < 1e-8f) return q;

            float inv = 1f / Mathf.Sqrt(n);
            twist = new Quaternion(twist.x * inv, twist.y * inv, twist.z * inv, twist.w * inv);

            // q = swing * twist  ->  swing * twist⁻¹ = q * twist⁻²
            Quaternion twistInv = Quaternion.Inverse(twist);
            return q * twistInv * twistInv;
        }

        Quaternion MatrixToQuaternion(double[] m)
        {
            // OpenHaptics entrega 4x4 column-major, right-handed. Convertimos a
            // la mano de Unity con S = diag(1,1,-1):  R' = S * R * S  (negar las
            // entradas donde fila==2 XOR col==2). Las columnas son los ejes.
            float[,] R = new float[3, 3];
            for (int c = 0; c < 3; c++)
                for (int r = 0; r < 3; r++)
                {
                    float v = (float)m[c * 4 + r];
                    if ((r == 2) ^ (c == 2)) v = -v;   // cambio de mano
                    R[r, c] = v;
                }
            Vector3 right = new Vector3(R[0, 0], R[1, 0], R[2, 0]) * RotAxisSign.x;
            Vector3 up    = new Vector3(R[0, 1], R[1, 1], R[2, 1]) * RotAxisSign.y;
            Vector3 fwd   = new Vector3(R[0, 2], R[1, 2], R[2, 2]) * RotAxisSign.z;
            if (fwd.sqrMagnitude < 1e-6f || up.sqrMagnitude < 1e-6f) return Quaternion.identity;
            return Quaternion.LookRotation(fwd.normalized, up.normalized);
        }

        // =====================================================================
        //  HapticsDirect.dll  —  firmas verificadas contra el demo instalado.
        // =====================================================================
#if OPENHAPTICS_TOUCH
        const string DLL = "HapticsDirect";

        [DllImport(DLL)] static extern int  initDevice(string configName);
        [DllImport(DLL)] static extern void startSchedulers();
        [DllImport(DLL)] static extern void getPosition(string deviceName, double[] position);
        [DllImport(DLL)] static extern void getTransform(string deviceName, double[] transform);
        [DllImport(DLL)] static extern void getButtons(string deviceName, int[] buttons, int[] buttonsChanged, ref int numButtons);
        [DllImport(DLL)] static extern void setForce(string deviceName, double[] force, double[] torque);
        // Misma forma que getPosition. La usa el propio HapticDemo de 3D Systems,
        // y es la única manera de distinguir "no mando fuerza" de "la mando y el
        // aparato no la renderiza".
        [DllImport(DLL)] static extern void getCurrentForce(string deviceName, double[] force);

        // La vía BUENA de renderizar fuerza. Firmas tomadas del paquete oficial
        // HapticsDirect (HapticPlugin.cs), no deducidas.
        [DllImport(DLL)] static extern void setConstantForceValues(string deviceName, double[] direction, double magnitude);
        [DllImport(DLL)] static extern void setSpringValues(string deviceName, double[] anchor, double magnitude);
        [DllImport(DLL)] static extern void setVibrationValues(string deviceName, double[] direction, double magnitude, double frequency, double time);
        [DllImport(DLL)] static extern int getHDError(System.Text.StringBuilder info, int len);
        [DllImport(DLL)] static extern void disconnectAllDevices();
#endif
    }
}

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
    public static class TouchDevice
    {
        // Nombre de configuración por defecto de OpenHaptics (el del demo).
        const string DeviceName = "Default Device";

        public static bool Available { get; private set; }

        // ---- Calibración dispositivo -> mundo -------------------------------
        // getPosition entrega milímetros en el marco del dispositivo
        // (right-handed: +X derecha, +Y arriba, +Z hacia el usuario).
        // La pose del stylus se ancla a un punto de mundo (AnchorPos/AnchorRot,
        // normalmente el centro del campo quirúrgico) y se auto-recentra:
        //   worldPos = AnchorPos + AnchorRot * (LocalOffset + (devM - devHome) * scale)
        public static Vector3 AnchorPos;
        public static Quaternion AnchorRot = Quaternion.identity;
        public static Vector3 LocalOffset = new Vector3(0f, 0f, 0f);
        public static float WorkspaceScale = 2.5f;
        public static Vector3 AxisSign = new Vector3(-1f, 1f, -1f); // right-handed -> Unity (X invertido: izq/der)
        public static float ForceScale = 1f;                        // N mundo -> N dispositivo (~3.3 N máx)

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
        public static bool InvertRoll = false;

        /// <summary>Eje del stylus sobre el que se mide el giro a invertir.</summary>
        public static Vector3 RollAxis = Vector3.forward;

        // Auto-recentrado: la posición del stylus al entrar se toma como origen.
        static Vector3 _devHome;
        static bool _hasHome;
        public static void Recenter() { _hasHome = false; }

        static bool _initTried, _inited;

        // Diagnóstico: log de posición y orientación (ponlo en true para verlo).
        public static bool VerboseLog = false;
        static float _nextLog;

        static void EnsureInit()
        {
            if (_initTried) return;
            _initTried = true;
#if OPENHAPTICS_TOUCH
            try
            {
                int r = initDevice(DeviceName);   // >=0 = ok en HapticsDirect
                Debug.Log($"[TouchDevice] initDevice(\"{DeviceName}\") -> {r}");
                if (r >= 0)
                {
                    startSchedulers();
                    _inited = true;
                    Debug.Log("[TouchDevice] Touch inicializado. Schedulers activos.");
                }
                else
                {
                    Debug.LogError($"[TouchDevice] initDevice devolvió {r}. ¿El Touch está " +
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
        public static bool TryReadPose(out Vector3 worldPos, out Quaternion worldRot,
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
        public static Vector3 LastRequested { get; private set; }

        /// <summary>
        /// Fuerza que el aparato dice estar renderizando AHORA. Comparada con
        /// <see cref="LastRequested"/> distingue los dos fallos que se parecen:
        /// que no estemos mandando nada, o que mandemos y no salga.
        /// </summary>
        public static Vector3 ReadCurrentForce()
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
        public static bool UseConstantForceEffect = true;

        public static void SetForceWorld(Vector3 worldForce)
        {
            if (!_inited) return;
#if OPENHAPTICS_TOUCH
            Vector3 devForce = Quaternion.Inverse(AnchorRot) * worldForce;

            if (UseConstantForceEffect)
            {
                // Sin AxisSign: getPosition y getTransform están documentados
                // como "(Unity CSys)" y el plugin oficial pasa los Vector3 tal
                // cual, sin tocar ejes (Vector3ToDoubleArray solo copia).
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

        /// <summary>Último error del driver, o cadena vacía.</summary>
        public static string ReadError()
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

        public static void Shutdown()
        {
            if (!_inited) return;
#if OPENHAPTICS_TOUCH
            try { setForce(DeviceName, _torqueZero, _torqueZero); disconnectAllDevices(); }
            catch { }
#endif
            _inited = false; _initTried = false; Available = false;
        }

        // buffers reutilizados
        static readonly double[] _pos = new double[3];
        static readonly double[] _mat = new double[16];
        static readonly double[] _force = new double[3];
        static readonly double[] _forceRead = new double[3];
        static readonly double[] _torqueZero = new double[3];
        static readonly int[] _btn = new int[8];
        static readonly int[] _btnChg = new int[8];

        // Invierte los ejes de orientación que se toman del gimbal (para tuning).
        public static Vector3 RotAxisSign = new Vector3(1f, 1f, -1f);

        /// <summary>
        /// Devuelve la misma orientación pero con el giro sobre
        /// <see cref="RollAxis"/> en sentido contrario.
        ///
        /// Descomposición swing-twist: la parte vectorial del cuaternión se
        /// proyecta sobre el eje, lo que da el "twist" (giro propio); el resto
        /// es el "swing" (hacia dónde apunta). Invertir solo el twist deja el
        /// apuntado intacto, que es justo lo que hace falta aquí.
        /// </summary>
        static Quaternion InvertirGiroPropio(Quaternion q)
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

        static Quaternion MatrixToQuaternion(double[] m)
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
        [DllImport(DLL)] static extern int getHDError(System.Text.StringBuilder info, int len);
        [DllImport(DLL)] static extern void disconnectAllDevices();
#endif
    }
}

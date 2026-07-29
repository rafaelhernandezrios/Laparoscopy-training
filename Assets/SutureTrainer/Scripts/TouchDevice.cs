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
            Quaternion devRot = MatrixToQuaternion(_mat);

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
        public static void SetForceWorld(Vector3 worldForce)
        {
            if (!_inited) return;
#if OPENHAPTICS_TOUCH
            Vector3 devForce = Quaternion.Inverse(AnchorRot) * worldForce;
            _force[0] = devForce.x * AxisSign.x * ForceScale;
            _force[1] = devForce.y * AxisSign.y * ForceScale;
            _force[2] = devForce.z * AxisSign.z * ForceScale;
            setForce(DeviceName, _force, _torqueZero);
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
        static readonly double[] _torqueZero = new double[3];
        static readonly int[] _btn = new int[8];
        static readonly int[] _btnChg = new int[8];

        // Invierte los ejes de orientación que se toman del gimbal (para tuning).
        public static Vector3 RotAxisSign = new Vector3(1f, 1f, -1f);

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
        [DllImport(DLL)] static extern void disconnectAllDevices();
#endif
    }
}

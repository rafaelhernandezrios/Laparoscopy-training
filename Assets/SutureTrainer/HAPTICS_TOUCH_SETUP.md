# Mano derecha con 3D Systems Touch (retroalimentación háptica de fuerza)

La mano derecha es **conmutable**: mando de Quest (como antes) o el **3D Systems
Touch** vía OpenHaptics, con **fuerza real** en el stylus.

## Estado actual (ya hecho)

- **`HapticsDirect.dll` ya está copiado** en `Assets/Plugins/x86_64/`.
  Es el plugin nativo de tu "Touch Device Drivers" (el mismo que usa su HapticDemo).
- El runtime `hd.dll` **no** hace falta copiarlo: lo aporta el driver del Touch ya
  instalado en el sistema (está en el PATH de Windows).
- `Scripts/TouchDevice.cs` habla directo con ese DLL. La API se **verificó
  decompilando** el demo instalado, así que los nombres y firmas son los reales:
  `initDevice("Default Device")`, `startSchedulers()`, `getPosition`,
  `getTransform`, `getButtons`, `setForce`, `disconnectAllDevices`.
- `Scripts/InstrumentHaptics.cs` calcula la fuerza de **tejido** y de **trócar**.
- `Scripts/MasterInput.cs` añade el `backend` (Quest/Touch), lee el Touch y expone
  `SendForce`. `Editor/SceneBuilder.cs` cuelga los hápticos del brazo derecho y deja
  la conmutación en **F1**.

## Único paso que falta: activar el símbolo de compilación

`Edit > Project Settings > Player > Other Settings > Scripting Define Symbols`
→ añade **`OPENHAPTICS_TOUCH`** y aplica.

Sin ese símbolo el proyecto compila igual y la mano derecha usa el mando (la fuerza
se siente como vibración). Con el símbolo + el Touch encendido, se activa la fuerza
real. (Si Unity estaba abierto, deja que reimporte el DLL al volver a enfocarlo.)

## Uso

- Ejecuta y pulsa **F1** para conmutar la mano derecha Quest ↔ Touch, o fija
  `backend = TouchHaptic` en el inspector del `GameManager` (componente MasterInput
  derecho).
- Botón delantero del stylus = cierre de mandíbulas; botón trasero = clutch.

## Calibración (inspector del `MasterInput` derecho)

- El Touch se ancla donde está el instrumento al conmutar (posición del mando).
  Para fijar un punto concreto, asigna un `Transform` a `touchHome`.
- `touchNudge` — ajuste fino opcional del origen respecto al ancla (por defecto 0).
- `touchWorkspaceScale` — amplía el volumen (~16 cm) del Touch al campo quirúrgico.
- `touchForceScale` — N de mundo → N del dispositivo (el Touch da ~3.3 N máx).
- Si los ejes salen invertidos, ajusta `AxisSign` en `TouchDevice.cs`.

## Ajuste de fuerzas (inspector de `InstrumentHaptics` en `Arm_R`)

- **Tejido:** `tissueStiffness`, `tissueDamping`, `puncturePassFactor`
  (más blando al pasar por una diana válida), `maxTissueForce`.
- **Trócar:** `trocarStiffness`, `trocarDamping`, `maxTrocarForce`.
- `maxTotalForce` limita la suma al máximo seguro del Touch (~3.3 N).

Empieza con rigideces bajas y súbelas hasta que se sienta firme pero estable
(si vibra o "zumba", baja la rigidez o sube la amortiguación).

## Notas técnicas (API real de HapticsDirect.dll)

Cada función recibe primero el nombre del dispositivo (`"Default Device"`):
`initDevice(string)`→int, `startSchedulers()`, `getPosition(dev, double[3] mm)`,
`getTransform(dev, double[16] colMajor)`,
`getButtons(dev, int[] estados, int[] cambios, ref int n)`,
`setForce(dev, double[3] fuerzaN, double[3] torque)`, `disconnectAllDevices()`.
El dispositivo es right-handed (mm); `TouchDevice` hace el cambio de mano a Unity.

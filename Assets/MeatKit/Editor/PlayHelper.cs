using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

// Bootstraps the minimum FistVR manager singletons needed for items to initialise in
// editor play mode.  All types resolved from H3VRCode-CSharp at runtime. 
public static class PlayHelper
{
#if UNITY_EDITOR

    // -------------------------------------------------------------------------
    // Modder opt-out toggle
    // -------------------------------------------------------------------------
    // If your mod supplies its own FistVR manager singletons (GM, AM, SM, FXM,
    // FVRSceneSettings) and/or its own VR camera setup, you can disable
    // PlayHelper entirely so the two bootstraps don't conflict.
    //
    // Usage — create an Editor script (any .cs file inside an Editor/ folder)
    // with an [InitializeOnLoad] class and set the flag in its static constructor,
    // BEFORE Unity enters play mode:
    //
    //   using UnityEditor;
    //
    //   [InitializeOnLoad]
    //   public static class MyModPlayHelperOverride
    //   {
    //       static MyModPlayHelperOverride()
    //       {
    //           PlayHelper.Enabled = false;
    //       }
    //   }
    //
    // With Enabled = false, both OnBeforeSceneLoad (singleton bootstrap + Harmony
    // patches) and OnAfterSceneLoad (VR camera creation) are completely skipped.
    // -------------------------------------------------------------------------
    public static bool Enabled = true;

    private static Harmony _harmony;

    // Harmony prefix — returning false skips the patched method entirely.
    private static bool SkipMethod() { return false; }

    // Harmony Finalizer — swallows any exception thrown by the patched method.
    private static Exception SwallowException(Exception __exception) { return null; }

    // =========================================================================
    // RECURSIVE NULL-HEALER
    // =========================================================================
    // Harmony Prefix on FVRPhysicalObject.Awake.  Fires on the actual derived type
    // and heals null [Serializable] fields, arrays, and List<T>s that are stripped
    // after a MeatKit build.  UnityEngine.Object refs and [NonSerialized] fields
    // are never touched.  Type-level caching amortises the reflection walk.
    // =========================================================================

    private const int HealMaxDepth = 6;

    // Per-type cache of healable fields; built once per type, reused each Awake.
    private static readonly Dictionary<Type, FieldInfo[]> _healCache
        = new Dictionary<Type, FieldInfo[]>();
    private static readonly object _healCacheLock = new object();

    // Harmony Prefix entry-point — __instance is the full derived object.
    private static void HealSerializedNulls(object __instance)
    {
        if (__instance == null) return;
        HealObject(__instance, 0);
    }

    // Walk own type + every base type up to (but not including) MonoBehaviour.
    private static void HealObject(object obj, int depth)
    {
        if (obj == null || depth >= HealMaxDepth) return;
        Type t = obj.GetType();
        while (t != null
               && t != typeof(MonoBehaviour)
               && t != typeof(Behaviour)
               && t != typeof(Component)
               && t != typeof(UnityEngine.Object)
               && t != typeof(object))
        {
            HealFields(obj, t, depth);
            t = t.BaseType;
        }
    }

    private static void HealFields(object obj, Type declaredType, int depth)
    {
        FieldInfo[] fields = GetHealableFields(declaredType);
        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo f = fields[i];
            Type ft     = f.FieldType;
            object val  = f.GetValue(obj);

            if (ft.IsArray)
            {
                if (val == null)
                {
                    // Empty array satisfies any .Length check.
                    f.SetValue(obj, Array.CreateInstance(ft.GetElementType(), 0));
                }
                else if (depth + 1 < HealMaxDepth)
                {
                    // Recurse into populated arrays of healable elements.
                    Type et = ft.GetElementType();
                    if (IsHealableInlineType(et))
                    {
                        Array arr = (Array)val;
                        for (int ai = 0; ai < arr.Length; ai++)
                        {
                            object elem = arr.GetValue(ai);
                            if (elem == null)
                            {
                                try { elem = Activator.CreateInstance(et); arr.SetValue(elem, ai); }
                                catch { continue; }
                            }
                            if (elem != null) HealObject(elem, depth + 1);
                        }
                    }
                }
            }
            else if (IsSerializedListType(ft))
            {
                // Ensure the List itself is non-null; elements are runtime data.
                if (val == null)
                {
                    try { f.SetValue(obj, Activator.CreateInstance(ft)); }
                    catch { /* ignore if List<T> ctor fails */ }
                }
            }
            else if (IsHealableInlineType(ft))
            {
                if (val == null)
                {
                    try { val = Activator.CreateInstance(ft); f.SetValue(obj, val); }
                    catch { val = null; }
                }
                if (val != null) HealObject(val, depth + 1);
            }
        }
    }

    // Returns cached list of fields on this declaring type that require healing.
    private static FieldInfo[] GetHealableFields(Type t)
    {
        FieldInfo[] cached;
        lock (_healCacheLock)
        {
            if (_healCache.TryGetValue(t, out cached)) return cached;
        }

        FieldInfo[] all = t.GetFields(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        List<FieldInfo> result = new List<FieldInfo>();
        for (int i = 0; i < all.Length; i++)
        {
            FieldInfo f = all[i];
            if (f.IsDefined(typeof(NonSerializedAttribute), false)) continue;
            Type ft = f.FieldType;
            if (ft.IsArray || IsSerializedListType(ft) || IsHealableInlineType(ft))
                result.Add(f);
        }

        cached = result.ToArray();
        lock (_healCacheLock) { _healCache[t] = cached; }
        return cached;
    }

    // True for a [Serializable] concrete class that Activator.CreateInstance can build.
    private static bool IsHealableInlineType(Type t)
    {
        if (t == null || t.IsValueType || t == typeof(string)) return false;
        if (t.IsAbstract || t.IsInterface || t.IsGenericType) return false;
        if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return false;
        return t.IsDefined(typeof(SerializableAttribute), false);
    }

    // True for any closed List<T> — Unity serialises these as variable-length arrays.
    private static bool IsSerializedListType(Type t)
    {
        return t.IsGenericType
            && t.GetGenericTypeDefinition() == typeof(List<>);
    }

    private static void Log(string msg) { }

    private static void Warn(string msg)
    {
        Debug.LogWarning("[PlayHelper] " + msg);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void OnBeforeSceneLoad()
    {
        if (!Enabled) return;

        Type gmType  = ResolveFromH3VR("FistVR.GM");
        Type amType  = ResolveFromH3VR("FistVR.AM");
        Type smType  = ResolveFromH3VR("FistVR.SM");
        Type fxmType = ResolveFromH3VR("FistVR.FXM");
        Type ssType  = ResolveFromH3VR("FistVR.FVRSceneSettings");

        if (gmType == null)
        {
            // H3VRCode-CSharp not imported yet; nothing to bootstrap.
            return;
        }

        // Patch crashing methods before AddComponent fires; patches stay alive for the session.
        _harmony = new Harmony("com.bitwizrd.playhelper.editorsafety");
        ApplyInitPatches(gmType, ssType, amType, smType, fxmType);

        // GM first — FVRSceneSettings.Awake needs IsAsyncLoading already set.
        GameObject gmGO = CreateEditorManager(gmType, "[Editor GM]");
        if (gmGO != null) SetManagerSingletonInstance(gmType, gmGO);

        // FVRSceneSettings — must exist before FVRFireArm.Awake reads GM.CurrentSceneSettings.
        if (ssType != null && gmGO != null)
        {
            GameObject ssGO = CreateEditorManager(ssType, "[Editor FVRSceneSettings]");
            if (ssGO != null)
            {
                SetCurrentSceneSettings(gmType, ssType, ssGO);
            }
        }

        // AM — needed for AM.GetFireArmMechanicalSpread; dict seeded after creation.
        // AM.Awake is not no-op'd, but on re-entry no new component fires Awake so
        // Instance must be set manually like the other managers.
        if (amType != null)
        {
            GameObject amGO = CreateEditorManager(amType, "[Editor AM]");
            if (amGO != null) SetManagerSingletonInstance(amType, amGO);
            PopulateAMAccuracyDictionary(amType);
        }

        // SM — audio pool dicts seeded manually.
        if (smType != null)
        {
            GameObject smGO = CreateEditorManager(smType, "[Editor SM]");
            if (smGO != null) SetManagerSingletonInstance(smType, smGO);
            PopulateSMPrefabBindingDictionary(smType);
            WarmupSMGenericPools(smType);
            SeedSMImpactDictionary(smType);
        }

        // FXM — muzzle dict seeded with dummy configs; component disabled to prevent Update NPE.
        if (fxmType != null)
        {
            GameObject fxmGO = CreateEditorManager(fxmType, "[Editor FXM]");
            if (fxmGO != null)
            {
                SetManagerSingletonInstance(fxmType, fxmGO);
                DisableComponent(fxmGO, fxmType);
                PopulateFXMMuzzleDictionary(fxmType);
            }
        }

        Log("Harmony patches active, all managers bootstrapped.");
    }

    // Spawns [EditorVRCamera] at the SceneView eye position for HMD rendering and
    // disconnects the scene-view camera from VR stereo so scene navigation works normally.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void OnAfterSceneLoad()
    {
        if (!Enabled) return;

        Camera[] existing = UnityEngine.Object.FindObjectsOfType<Camera>();
        for (int i = 0; i < existing.Length; i++)
        {
            existing[i].enabled = false;
            Log("Disabled existing camera: " + existing[i].gameObject.name);
        }

        // Scene cameras can carry their own AudioListener; only [EditorVRCamera] should have one.
        // FindObjectsOfTypeAll (not FindObjectsOfType) -- a leftover [EditorVRCamera] from an
        // earlier session is HideFlags.DontSave, which FindObjectsOfType silently can't see,
        // letting its enabled listener go undetected and accumulate session after session.
        AudioListener[] existingListeners = Resources.FindObjectsOfTypeAll<AudioListener>();
        for (int i = 0; i < existingListeners.Length; i++)
        {
            // FindObjectsOfTypeAll can also return a component sitting on a loaded prefab
            // asset (e.g. a weapon scope prefab with its own AudioListener) rather than a
            // scene instance; IsPersistent tells them apart. Never touch a project asset.
            if (UnityEditor.EditorUtility.IsPersistent(existingListeners[i].gameObject)) continue;

            existingListeners[i].enabled = false;
            Log("Disabled existing AudioListener: " + existingListeners[i].gameObject.name);
        }

        // FVRSceneSettings' real Start() (vault scenario load, etc.) can spawn more objects
        // with their own AudioListener after this one-time pass already ran; keep enforcing
        // exactly one for the rest of the session, see EnforceSingleAudioListener.
        UnityEditor.EditorApplication.update += EnforceSingleAudioListener;

        // Capture scene-view position, then detach the scene-view camera from VR tracking.
        Vector3 spawnPos;
        Quaternion spawnRot;
        UnityEditor.SceneView sv = UnityEditor.SceneView.lastActiveSceneView;
        if (sv != null && sv.camera != null)
        {
            spawnPos = sv.camera.transform.position;
            spawnRot = sv.camera.transform.rotation;
            sv.camera.stereoTargetEye = StereoTargetEyeMask.None;
            Log("[EditorVRCamera] placed at SceneView position " + spawnPos);
        }
        else
        {
            spawnPos = new Vector3(0f, 1.8f, 0f);
            spawnRot = Quaternion.identity;
            Log("[EditorVRCamera] placed at default standing origin.");
        }

        GameObject camGO = new GameObject("[EditorVRCamera]");
        camGO.hideFlags = HideFlags.DontSave;
        Camera vrCam = camGO.AddComponent<Camera>();
        vrCam.tag             = "MainCamera";
        vrCam.nearClipPlane   = 0.01f;
        vrCam.farClipPlane    = 1000f;
        vrCam.clearFlags      = CameraClearFlags.Skybox;
        vrCam.depth           = 10;
        vrCam.stereoTargetEye = StereoTargetEyeMask.None;  // enabled after a warm-up delay, see EnableVrStereoWhenReady
        _vrCamListener = camGO.AddComponent<AudioListener>();
        camGO.transform.position = spawnPos;
        camGO.transform.rotation = spawnRot;

        // Enabling stereo immediately can race Unity's own native OpenVR handshake and crash
        // inside vrclient_x64.dll on a NULL this-pointer; wait a moment before enabling it.
        _pendingVrCam = vrCam;
        _vrCamStereoEnableAt = UnityEditor.EditorApplication.timeSinceStartup + VrStereoWarmupDelaySeconds;
        UnityEditor.EditorApplication.update += EnableVrStereoWhenReady;

        // Unity's VR system can re-enable stereo on the scene-view camera each frame.
        // Register an editor-update callback to keep it detached for the entire play session.
        UnityEditor.EditorApplication.update += KeepSceneViewDetachedFromVR;
        UnityEditor.EditorApplication.playmodeStateChanged += OnPlaymodeChanged;
    }

    private static Camera _pendingVrCam;
    private static double _vrCamStereoEnableAt;
    private const double VrStereoWarmupDelaySeconds = 1.0;

    // Delays the [EditorVRCamera]'s stereo output by VrStereoWarmupDelaySeconds so Unity's
    // native OpenVR init has time to finish before the first stereo render is attempted.
    private static void EnableVrStereoWhenReady()
    {
        if (_pendingVrCam == null)
        {
            UnityEditor.EditorApplication.update -= EnableVrStereoWhenReady;
            return;
        }
        if (UnityEditor.EditorApplication.timeSinceStartup < _vrCamStereoEnableAt) return;

        _pendingVrCam.stereoTargetEye = StereoTargetEyeMask.Both;
        Log("[EditorVRCamera] stereo enabled after warm-up delay.");
        _pendingVrCam = null;
        UnityEditor.EditorApplication.update -= EnableVrStereoWhenReady;
    }

    // Unsubscribes the per-frame scene-view guard when play mode ends.
    private static void OnPlaymodeChanged()
    {
        if (!UnityEditor.EditorApplication.isPlaying &&
            !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
        {
            UnityEditor.EditorApplication.update -= KeepSceneViewDetachedFromVR;
            UnityEditor.EditorApplication.update -= EnableVrStereoWhenReady;
            UnityEditor.EditorApplication.update -= EnforceSingleAudioListener;
            UnityEditor.EditorApplication.playmodeStateChanged -= OnPlaymodeChanged;

            // [EditorVRCamera] uses HideFlags.DontSave, so it survives Stop instead of being
            // cleaned up -- disable its listener here or it's left enabled indefinitely (and
            // the next Play session's [EditorVRCamera] would just add another one on top).
            if (_vrCamListener != null) _vrCamListener.enabled = false;

            _pendingVrCam = null;
            _vrCamListener = null;
        }
    }

    private static AudioListener _vrCamListener;

    // Disables every AudioListener except [EditorVRCamera]'s own. Checked every frame rather
    // than throttled -- a scenario/vault load can spawn a burst of new listeners at once, and
    // a throttled check left a window where Unity would log its warning before the next pass.
    // Uses FindObjectsOfTypeAll -- see the matching note in OnAfterSceneLoad.
    private static void EnforceSingleAudioListener()
    {
        AudioListener[] listeners = Resources.FindObjectsOfTypeAll<AudioListener>();
        for (int i = 0; i < listeners.Length; i++)
        {
            if (listeners[i] != _vrCamListener && listeners[i].enabled &&
                !UnityEditor.EditorUtility.IsPersistent(listeners[i].gameObject))
            {
                listeners[i].enabled = false;
                Log("Disabled newly-spawned AudioListener: " + listeners[i].gameObject.name);
            }
        }
    }

    // Reverts stereo eye if the VR system re-enables it on the scene-view camera each frame.
    private static void KeepSceneViewDetachedFromVR()
    {
        UnityEditor.SceneView sv = UnityEditor.SceneView.lastActiveSceneView;
        if (sv != null && sv.camera != null &&
            sv.camera.stereoTargetEye != StereoTargetEyeMask.None)
        {
            sv.camera.stereoTargetEye = StereoTargetEyeMask.None;
            Log("Re-detached SceneView camera from VR stereo tracking.");
        }
    }

    // Patches Awake with a Finalizer so base.Awake() runs (setting Instance) and
    // any subsequent exceptions in the body are swallowed.
    private static void PatchAwakeWithSwallow(Type managerType)
    {
        try
        {
            MethodInfo awake = AccessTools.Method(managerType, "Awake");
            if (awake == null)
            {
                Warn("PatchAwakeWithSwallow: Awake not found on " + managerType.Name);
                return;
            }
            HarmonyMethod swallow = new HarmonyMethod(
                typeof(PlayHelper).GetMethod("SwallowException",
                    BindingFlags.Static | BindingFlags.NonPublic));
            _harmony.Patch(awake, finalizer: swallow);
            Log("Finalizer-patched " + managerType.Name + ".Awake (base.Awake sets Instance; exceptions suppressed).");
        }
        catch (Exception ex)
        {
            Warn("PatchAwakeWithSwallow failed for "
                + managerType.Name + ": " + ex.Message);
        }
    }

    // No-ops or Finalizer-patches methods that crash due to missing editor resources.
    private static void ApplyInitPatches(
        Type gmType, Type ssType, Type amType, Type smType, Type fxmType)
    {
        try
        {
            HarmonyMethod skip = new HarmonyMethod(
                typeof(PlayHelper).GetMethod("SkipMethod",
                    BindingFlags.Static | BindingFlags.NonPublic));

            if (gmType != null)
            {
                // GM.Start requires Steam; suppress it.
                PatchSafely(AccessTools.Method(gmType, "Start"), skip);

                // Swallow ES2 save-data exceptions that fire before managed catch can intercept.
                PatchAwakeWithSwallow(gmType);
            }

            if (ssType != null)
            {
                Type mbType = ResolveFromH3VR("FistVR.ManagerBootStrap");
                if (mbType != null)
                    PatchSafely(AccessTools.Method(mbType, "BootStrap"), skip);
            }

            if (amType != null)
                PatchSafely(AccessTools.Method(amType, "GenerateFireArmRoundDictionaries"), skip);

            if (smType != null)
                PatchAwakeWithSwallow(smType);

            if (fxmType != null)
                PatchAwakeWithSwallow(fxmType);

            // SetReverbEnvironment needs a real AudioMixer this bootstrap doesn't provide.
            // SM.Update no longer needs a skip patch -- confirmed against the full log file
            // (not just a tail window, which is too small to catch a fast-repeating exception)
            // that the current game version's own null-guards let it run for real, with zero
            // exceptions.
            if (smType != null)
                PatchSafely(AccessTools.Method(smType, "SetReverbEnvironment"), skip);

            // AchCheck() reads GM.CurrentMovementManager.transform with no null-guard; this
            // bootstrap has no real MovementManager, so it NREs every single Update() tick
            // (10,100 times in one 60s session) unless skip-patched.
            if (ssType != null)
                PatchSafely(AccessTools.Method(ssType, "Update"), skip);

            // CurrentPlayerBody is null in editor.
            Type aicType = ResolveFromH3VR("FistVR.AudioImpactController");
            if (aicType != null)
                PatchSafely(AccessTools.Method(aicType, "OnCollisionEnter"), skip);

            // DllNotFoundException from haptic feedback DLL; no-op if not installed.
            Type forceTubeType = ResolveFromH3VR("ForceTubeVRInterface");
            if (forceTubeType != null)
            {
                PatchSafely(AccessTools.Method(forceTubeType, "OnLoadRuntimeMethod"), skip);
                PatchSafely(AccessTools.Method(forceTubeType, "InitAsync"), skip);
            }

            // PlayAudioEvent requires CurrentPlayerBody which is null in editor.
            Type fireArmType = ResolveFromH3VR("FistVR.FVRFireArm");
            if (fireArmType != null)
            {
                Type eType = ResolveFromH3VR("FistVR.FirearmAudioEventType");
                if (eType != null)
                    PatchSafely(AccessTools.Method(fireArmType, "PlayAudioEvent",
                        new Type[] { eType, typeof(float) }), skip);

                // Serialized effect arrays stripped by MeatKit build; swallow the Awake crash.
                PatchAwakeWithSwallow(fireArmType);
            }

            // Attach the recursive null-healer to FVRPhysicalObject.Awake.
            Type fpoType = ResolveFromH3VR("FistVR.FVRPhysicalObject");
            if (fpoType != null)
            {
                MethodInfo fpoAwake = AccessTools.Method(fpoType, "Awake");
                if (fpoAwake != null)
                {
                    HarmonyMethod healPrefix = new HarmonyMethod(
                        typeof(PlayHelper).GetMethod("HealSerializedNulls",
                            BindingFlags.Static | BindingFlags.NonPublic));
                    _harmony.Patch(fpoAwake, prefix: healPrefix);
                    Log("Prefixed FVRPhysicalObject.Awake with recursive null-healer.");
                }
                else
                {
                    Warn("FVRPhysicalObject.Awake not found — recursive healer skipped.");
                }
            }

            // Serialized magazine data stripped by MeatKit build; swallow the Awake crash.
            Type magType = ResolveFromH3VR("FistVR.FVRFireArmMagazine");
            if (magType != null)
                PatchAwakeWithSwallow(magType);
        }
        catch (Exception ex)
        {
            Warn("ApplyInitPatches failed: " + ex.Message
                + ". Managers will still be created but Awake exceptions may be logged.");
        }
    }

    private static void PatchSafely(MethodInfo method, HarmonyMethod prefix)
    {
        try
        {
            if (method == null)
            {
                Warn("PatchSafely: resolved method is null, skipping.");
                return;
            }
            _harmony.Patch(method, prefix: prefix);
            Log("Patched " + method.DeclaringType.Name + "." + method.Name);
        }
        catch (Exception ex)
        {
            Warn("Failed to patch "
                + (method != null ? method.DeclaringType.Name + "." + method.Name : "null")
                + ": " + ex.Message);
        }
    }

    // Sets ManagerSingleton<T>.Instance via reflection.  Tries three approaches to
    // work around a Mono 2.0 bug where GetProperty on closed generic static props returns null.
    private static void SetManagerSingletonInstance(Type managerType, GameObject go)
    {
        try
        {
            Type genericMs = ResolveFromH3VR("FistVR.ManagerSingleton`1");
            if (genericMs == null)
            {
                Warn("ManagerSingleton`1 not found in H3VRCode-CSharp.");
                return;
            }

            Type closedMs = genericMs.MakeGenericType(managerType);
            Component comp = go.GetComponent(managerType);
            if (comp == null) return;

            bool instanceSet = false;

            // Approach 1 — backing field by exact name (normal C# compiler output)
            FieldInfo backingField = closedMs.GetField("<Instance>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (backingField != null)
            {
                backingField.SetValue(null, comp);
                instanceSet = true;
                Log("Set ManagerSingleton<" + managerType.Name + ">.Instance via backing field (exact name).");
            }

            // Approach 2 — scan all static fields for one matching T.
            if (!instanceSet)
            {
                FieldInfo[] allStatic = closedMs.GetFields(
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                for (int i = 0; i < allStatic.Length; i++)
                {
                    FieldInfo f = allStatic[i];
                    if (f.FieldType == managerType || managerType.IsAssignableFrom(f.FieldType))
                    {
                        f.SetValue(null, comp);
                        instanceSet = true;
                        Log("Set ManagerSingleton<" + managerType.Name + ">.Instance via field scan ('" + f.Name + "').");
                        break;
                    }
                }
            }

            // Approach 3 — property reflection
            if (!instanceSet)
            {
                PropertyInfo prop = closedMs.GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Static);
                if (prop == null)
                    prop = AccessTools.Property(closedMs, "Instance");
                if (prop != null)
                {
                    prop.SetValue(null, comp, null);
                    instanceSet = true;
                    Log("Set ManagerSingleton<" + managerType.Name + ">.Instance via property.");
                }
            }

            // Verify — GetProperty returns null on Mono 2.0; use field scan to read back.
            object readBack = null;
            FieldInfo[] verifyFields = closedMs.GetFields(
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            for (int i = 0; i < verifyFields.Length; i++)
            {
                FieldInfo vf = verifyFields[i];
                if (vf.FieldType == managerType || managerType.IsAssignableFrom(vf.FieldType))
                {
                    readBack = vf.GetValue(null);
                    break;
                }
            }

            if (readBack == null && !instanceSet)
            {
                Warn("SetManagerSingletonInstance: all approaches failed for "
                    + managerType.Name + " — Instance is null; FVRPhysicalObject.Awake will NullRef.");
            }
            else if (readBack == null)
            {
                // Approaches reported success but field reads null (Mono 2.0 static generic
                // field write bug); compiled Awake path may still work.
                Warn("SetManagerSingletonInstance: Instance reads back null for "
                    + managerType.Name + " (Mono 2.0 static generic field bug — compiled Awake path may still work).");
            }
            else
            {
                Log("Verified ManagerSingleton<" + managerType.Name + ">.Instance is non-null.");
            }
        }
        catch (Exception ex)
        {
            Warn("SetManagerSingletonInstance failed for "
                + managerType.Name + ": " + ex.Message);
        }
    }

    // Creates a manager GO or reuses an existing one if the scene already has it.
    private static GameObject CreateEditorManager(Type componentType, string goName)
    {
        try
        {
            UnityEngine.Object existing = FindObjectOfType(componentType);
            if (existing != null)
            {
                Log(componentType.Name + " already in scene, reusing.");
                return ((Component)existing).gameObject;
            }

            var go = new GameObject(goName);
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent(componentType);

            Log("Created " + componentType.Name + " (" + componentType.Assembly.GetName().Name + ")");
            return go;
        }
        catch (Exception ex)
        {
            Warn("Failed to create " + componentType.Name
                + ": " + ex.Message);
            return null;
        }
    }

    // Wires GM.m_currentSceneSettings and initialises event signal collections.
    private static void SetCurrentSceneSettings(Type gmType, Type ssType, GameObject ssGO)
    {
        try
        {
            Component ssComp = ssGO.GetComponent(ssType);
            if (ssComp == null) return;

            UnityEngine.Object gmInst = FindObjectOfType(gmType);
            if (gmInst == null) return;

            FieldInfo field = gmType.GetField("m_currentSceneSettings",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return;

            field.SetValue(gmInst, ssComp);
            Log("Set GM.m_currentSceneSettings.");

            MethodInfo init = ssType.GetMethod("InitEventSignalCollections",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (init != null)
            {
                init.Invoke(ssComp, null);
                Log("Called InitEventSignalCollections().");
            }
            else
            {
                Warn("InitEventSignalCollections not found on FVRSceneSettings.");
            }
        }
        catch (Exception ex)
        {
            Warn("SetCurrentSceneSettings failed: " + ex.Message);
        }
    }

    // Unity 5.6 FindObjectOfType skips inactive objects; this variant includes them.
    private static UnityEngine.Object FindObjectOfType(Type type)
    {
        UnityEngine.Object[] found = Resources.FindObjectsOfTypeAll(type);
        if (found == null) return null;
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null) return found[i];
        }
        return null;
    }

    // Seeds SM.m_prefabBindingDic with a dummy AudioSource per pool type.
    private static void PopulateSMPrefabBindingDictionary(Type smType)
    {
        try
        {
            UnityEngine.Object smInst = FindObjectOfType(smType);
            if (smInst == null) return;

            FieldInfo dictField = smType.GetField("m_prefabBindingDic",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (dictField == null)
            {
                Warn("m_prefabBindingDic field not found on SM.");
                return;
            }

            object dict = dictField.GetValue(smInst);
            if (dict == null) return;

            Type audioTypeEnum  = ResolveFromH3VR("FistVR.FVRPooledAudioType");
            Type bindingType    = ResolveFromH3VR("FistVR.PoolTypePrefabBinding");
            if (audioTypeEnum == null || bindingType == null) return;

            FieldInfo fType   = bindingType.GetField("Type",
                BindingFlags.Public | BindingFlags.Instance);
            FieldInfo fPrefab = bindingType.GetField("Prefab",
                BindingFlags.Public | BindingFlags.Instance);

            GameObject dummyAudio = new GameObject("[EditorAudioPool]");
            dummyAudio.hideFlags = HideFlags.HideAndDontSave;
            dummyAudio.AddComponent<AudioSource>();
            UnityEngine.Object.DontDestroyOnLoad(dummyAudio);

            MethodInfo addMethod   = dict.GetType().GetMethod("Add");
            MethodInfo clearMethod = dict.GetType().GetMethod("Clear");
            if (addMethod == null) return;

            // Clear first so stale entries from previous sessions don't reference dead GOs.
            if (clearMethod != null) clearMethod.Invoke(dict, null);

            Array enumValues = Enum.GetValues(audioTypeEnum);
            int added = 0;
            for (int i = 0; i < enumValues.Length; i++)
            {
                object enumVal = enumValues.GetValue(i);
                int intKey = Convert.ToInt32(enumVal);

                object binding = Activator.CreateInstance(bindingType);
                if (fType   != null) fType.SetValue(binding, enumVal);
                if (fPrefab != null) fPrefab.SetValue(binding, dummyAudio);

                addMethod.Invoke(dict, new object[] { intKey, binding });
                added++;
            }
            Log("Seeded SM.m_prefabBindingDic with " + added + " entries.");
        }
        catch (Exception ex)
        {
            Exception inner = ex.InnerException != null ? ex.InnerException : ex;
            Warn("PopulateSMPrefabBindingDictionary failed: " + inner.Message);
        }
    }

    // Calls SM.WarmupGenericPools so pool fields are non-null when play exits.
    private static void WarmupSMGenericPools(Type smType)
    {
        try
        {
            MethodInfo warmup = smType.GetMethod("WarmupGenericPools",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
            if (warmup == null)
            {
                Warn("WarmupGenericPools not found on SM.");
                return;
            }
            warmup.Invoke(null, null);
            Log("SM.WarmupGenericPools completed.");
        }
        catch (Exception ex)
        {
            // Unwrap TargetInvocationException to get the real error.
            Exception inner = ex.InnerException != null ? ex.InnerException : ex;
            Warn("WarmupSMGenericPools failed: " + inner.GetType().Name + ": " + inner.Message);
        }
    }

    // Disables a component's Update loop without removing the GO or nulling Instance.
    private static void DisableComponent(GameObject go, Type componentType)
    {
        try
        {
            Component comp = go.GetComponent(componentType);
            if (comp != null)
            {
                ((MonoBehaviour)comp).enabled = false;
                Log("Disabled " + componentType.Name + " Update loop.");
            }
        }
        catch (Exception ex)
        {
            Warn("DisableComponent failed for "
                + componentType.Name + ": " + ex.Message);
        }
    }

    // Seeds AM.MechanicalAccuracyDic with zero-spread entries for all accuracy classes.
    private static void PopulateAMAccuracyDictionary(Type amType)
    {
        try
        {
            UnityEngine.Object amInst = FindObjectOfType(amType);
            if (amInst == null) return;

            FieldInfo dictField = amType.GetField("MechanicalAccuracyDic",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (dictField == null)
            {
                Warn("MechanicalAccuracyDic field not found on AM.");
                return;
            }

            object dict = dictField.GetValue(amInst);
            if (dict == null) return;

            Type accuracyClassType = ResolveFromH3VR("FistVR.FVRFireArmMechanicalAccuracyClass");
            // Nested type — reflection uses '+' as separator.
            Type entryType = ResolveFromH3VR(
                "FistVR.FVRFireArmMechanicalAccuracyChart+MechanicalAccuracyEntry");
            if (accuracyClassType == null || entryType == null) return;

            MethodInfo addMethod   = dict.GetType().GetMethod("Add");
            MethodInfo clearMethod = dict.GetType().GetMethod("Clear");
            if (addMethod == null) return;

            if (clearMethod != null) clearMethod.Invoke(dict, null);

            Array enumValues = Enum.GetValues(accuracyClassType);
            int added = 0;
            for (int i = 0; i < enumValues.Length; i++)
            {
                object key   = enumValues.GetValue(i);
                object entry = Activator.CreateInstance(entryType);
                addMethod.Invoke(dict, new object[] { key, entry });
                added++;
            }
            Log("Seeded AM.MechanicalAccuracyDic with " + added + " entries.");
        }
        catch (Exception ex)
        {
            Warn("PopulateAMAccuracyDictionary failed: " + ex.Message);
        }
    }

    // Seeds SM.m_impactDic with empty AudioEvents for every ImpactType/Mat/Intensity combo.
    private static void SeedSMImpactDictionary(Type smType)
    {
        try
        {
            UnityEngine.Object smInst = FindObjectOfType(smType);
            if (smInst == null) return;

            Type impactType   = ResolveFromH3VR("FistVR.ImpactType");
            Type matType      = ResolveFromH3VR("FistVR.MatSoundType");
            Type intensityType = ResolveFromH3VR("FistVR.AudioImpactIntensity");
            Type audioEvType  = ResolveFromH3VR("FistVR.AudioEvent");
            if (impactType == null || matType == null || intensityType == null || audioEvType == null)
            {
                Warn("SeedSMImpactDictionary: could not resolve enum/AudioEvent types.");
                return;
            }

            // Build the three-level nested generic Dictionary types at runtime.
            Type genericDic = typeof(Dictionary<,>);
            Type d3 = genericDic.MakeGenericType(intensityType, audioEvType);
            Type d2 = genericDic.MakeGenericType(matType, d3);
            Type d1 = genericDic.MakeGenericType(impactType, d2);

            MethodInfo add1 = d1.GetMethod("Add");
            MethodInfo add2 = d2.GetMethod("Add");
            MethodInfo add3 = d3.GetMethod("Add");

            object outerDict = Activator.CreateInstance(d1);

            Array impactValues    = Enum.GetValues(impactType);
            Array matValues       = Enum.GetValues(matType);
            Array intensityValues = Enum.GetValues(intensityType);

            for (int i = 0; i < impactValues.Length; i++)
            {
                object impactKey = impactValues.GetValue(i);
                object mid = Activator.CreateInstance(d2);
                for (int m = 0; m < matValues.Length; m++)
                {
                    object matKey = matValues.GetValue(m);
                    object inner = Activator.CreateInstance(d3);
                    for (int k = 0; k < intensityValues.Length; k++)
                    {
                        object intKey = intensityValues.GetValue(k);
                        object ae    = Activator.CreateInstance(audioEvType);
                        add3.Invoke(inner, new[] { intKey, ae });
                    }
                    add2.Invoke(mid, new[] { matKey, inner });
                }
                add1.Invoke(outerDict, new[] { impactKey, mid });
            }

            FieldInfo field = smType.GetField("m_impactDic",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(smInst, outerDict);
                Log("Seeded SM.m_impactDic with " + impactValues.Length + " ImpactType entries.");
            }
        }
        catch (Exception ex)
        {
            Warn("SeedSMImpactDictionary failed: " + ex.Message);
        }
    }

    // Seeds FXM.muzzleDic with dummy MuzzleEffectConfig entries for all muzzle effect types.
    private static void PopulateFXMMuzzleDictionary(Type fxmType)
    {
        try
        {
            UnityEngine.Object fxmInst = FindObjectOfType(fxmType);
            if (fxmInst == null) return;

            FieldInfo dictField = fxmType.GetField("muzzleDic",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (dictField == null)
            {
                Warn("muzzleDic field not found on FXM.");
                return;
            }

            object dict = dictField.GetValue(fxmInst);
            if (dict == null) return;

            Type entryEnumType = ResolveFromH3VR("FistVR.MuzzleEffectEntry");
            Type configType    = ResolveFromH3VR("FistVR.MuzzleEffectConfig");
            if (entryEnumType == null || configType == null) return;

            FieldInfo fEntry     = configType.GetField("Entry",
                BindingFlags.Public | BindingFlags.Instance);
            FieldInfo fHighlight = configType.GetField("Prefabs_Highlight",
                BindingFlags.Public | BindingFlags.Instance);
            FieldInfo fLowlight  = configType.GetField("Prefabs_Lowlight",
                BindingFlags.Public | BindingFlags.Instance);
            FieldInfo fNumHigh   = configType.GetField("NumParticles_Highlight",
                BindingFlags.Public | BindingFlags.Instance);
            FieldInfo fNumLow    = configType.GetField("NumParticles_Lowlight",
                BindingFlags.Public | BindingFlags.Instance);

            const int numSizes = 5; // one per MuzzleEffectSize: Subdued/Standard/Large/Huge/Oversize
            var dummyPrefabs = new List<GameObject>(numSizes);
            var dummyInts    = new List<int>(numSizes);
            for (int s = 0; s < numSizes; s++)
            {
                GameObject ps = new GameObject("[EditorMuzzleFX]");
                ps.hideFlags = HideFlags.HideAndDontSave;
                ps.AddComponent<ParticleSystem>();
                // Disable renderer to prevent pink blobs from missing material in editor.
                ParticleSystemRenderer psr = ps.GetComponent<ParticleSystemRenderer>();
                if (psr != null) psr.enabled = false;
                UnityEngine.Object.DontDestroyOnLoad(ps);
                dummyPrefabs.Add(ps);
                dummyInts.Add(0);
            }

            MethodInfo addMethod   = dict.GetType().GetMethod("Add");
            MethodInfo clearMethod = dict.GetType().GetMethod("Clear");
            if (addMethod == null) return;

            // Clear stale entries from previous sessions.
            if (clearMethod != null) clearMethod.Invoke(dict, null);

            object noneValue = null;
            try { noneValue = Enum.Parse(entryEnumType, "None"); }
            catch (Exception) { /* enum may not define None */ }

            Array enumValues = Enum.GetValues(entryEnumType);
            int added = 0;
            for (int i = 0; i < enumValues.Length; i++)
            {
                object key = enumValues.GetValue(i);
                if (noneValue != null && key.Equals(noneValue)) continue;

                ScriptableObject config = ScriptableObject.CreateInstance(configType);
                if (fEntry     != null) fEntry.SetValue(config, key);
                if (fHighlight != null) fHighlight.SetValue(config, dummyPrefabs);
                if (fLowlight  != null) fLowlight.SetValue(config, dummyPrefabs);
                if (fNumHigh   != null) fNumHigh.SetValue(config, dummyInts);
                if (fNumLow    != null) fNumLow.SetValue(config, dummyInts);

                try
                {
                    addMethod.Invoke(dict, new object[] { key, config });
                    added++;
                }
                catch (Exception)
                {
                    // Key already present from Resources.LoadAll — fine
                }
            }
            Log("Seeded FXM.muzzleDic with " + added + " entries.");
        }
        catch (Exception ex)
        {
            Warn("PopulateFXMMuzzleDictionary failed: " + ex.Message);
        }
    }

    // Searches H3VRCode-CSharp (and firstpass) assemblies for a named type.
    private static Type ResolveFromH3VR(string fullTypeName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Assembly asm = assemblies[i];
            if (asm == null) continue;

            string name = asm.GetName().Name;
            if (name != "H3VRCode-CSharp" && name != "H3VRCode-CSharp-firstpass")
                continue;

            Type t = asm.GetType(fullTypeName);
            if (t != null) return t;
        }
        return null;
    }

    // Searches project and mod assemblies for a named type (excludes H3VRCode and system libs).
    private static Type ResolveFromProjectAssembly(string fullTypeName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Assembly asm = assemblies[i];
            if (asm == null) continue;
            string name = asm.GetName().Name;
            if (name.StartsWith("System") || name.StartsWith("mscorlib")
                || name.StartsWith("UnityEngine") || name.StartsWith("UnityEditor")
                || name == "H3VRCode-CSharp" || name == "H3VRCode-CSharp-firstpass"
                || name == "0Harmony" || name == "HarmonyLib")
                continue;
            Type t = asm.GetType(fullTypeName);
            if (t != null) return t;
        }
        return null;
    }

#endif // UNITY_EDITOR
}

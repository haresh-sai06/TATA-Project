#if UNITY_EDITOR
using Aura;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click wiring for the Aura cinematic camera system. Run
/// <b>Tools ▸ Aura ▸ Setup Cinematic Cameras</b> and it will:
///   • add <see cref="AuraCameraDirector"/> to the Main Camera (disabling a legacy CameraFollow),
///   • create an "Aura HUD" object with <see cref="AuraCockpitHud"/>,
///   • verify the AuraClient / AuraDemoReactor takeover rig is present.
/// Everything else auto-wires at runtime (the director finds the onnxcontroller car itself).
/// </summary>
public static class AuraCinematicSetup
{
    [MenuItem("Tools/Aura/Setup Cinematic Cameras")]
    public static void Setup()
    {
        // 1. Main display camera → AuraCameraDirector.
        Camera cam = Camera.main;
        if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
        if (cam == null)
        {
            EditorUtility.DisplayDialog("Aura", "No Camera found in the open scene. Open your gameplay scene and try again.", "OK");
            return;
        }

        var follow = cam.GetComponent<CameraFollow>();
        if (follow != null) follow.enabled = false;

        if (cam.GetComponent<AuraCameraDirector>() == null)
        {
            Undo.AddComponent<AuraCameraDirector>(cam.gameObject);
            Debug.Log($"[Aura] Added AuraCameraDirector to '{cam.name}'.");
        }

        // 2. Cockpit HUD object (+ speed-limit alerts).
        var hud = Object.FindFirstObjectByType<AuraCockpitHud>();
        if (hud == null)
        {
            var go = new GameObject("Aura HUD");
            Undo.RegisterCreatedObjectUndo(go, "Create Aura HUD");
            go.AddComponent<AuraCockpitHud>();
            go.AddComponent<AuraSpeedLimit>();
            Debug.Log("[Aura] Created 'Aura HUD' with AuraCockpitHud + AuraSpeedLimit.");
        }
        else if (Object.FindFirstObjectByType<AuraSpeedLimit>() == null)
        {
            Undo.AddComponent<AuraSpeedLimit>(hud.gameObject);
            Debug.Log("[Aura] Added AuraSpeedLimit to the existing Aura HUD.");
        }

        // 3. Takeover rig sanity-check (AuraClient + AuraDemoReactor).
        var reactor = Object.FindFirstObjectByType<AuraDemoReactor>();
        if (reactor == null)
            Debug.LogWarning("[Aura] No AuraDemoReactor found. Add AuraClient + AuraDemoReactor to a scene object to get the drowsiness takeover + Aura Core link.");
        else if (reactor.GetComponent<AuraClient>() == null)
            Debug.LogWarning("[Aura] AuraDemoReactor is present but AuraClient is missing on the same object.");

        var onnx = Object.FindFirstObjectByType<onnxcontroller>();
        if (onnx == null)
            Debug.LogWarning("[Aura] No onnxcontroller (self-driving car) found — the cockpit camera will have nothing to ride until one is in the scene.");

        EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);
        Debug.Log("[Aura] Cinematic setup complete. Press Play, then [V] to cycle views (Cockpit is the first-person seat).");
        EditorUtility.DisplayDialog("Aura",
            "Cinematic cameras + speed-limit alerts set up.\n\nPress Play, then:\n• [V] cycle views (or [1]-[5])\n• Cockpit = first-person seat\n• [K] simulate a drowsiness takeover\n• Speed-limit sign shows top-left; the car warns (flash + beep) when over\n• [ and ] lower / raise the speed limit\n\nTip: nudge 'Cockpit Offset' on the camera until the hood sits low in frame.",
            "Got it");
    }
}
#endif

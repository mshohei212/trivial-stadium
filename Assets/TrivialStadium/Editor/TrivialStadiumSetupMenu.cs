#if UNITY_EDITOR
using System.IO;
using TrivialStadium.Battle;
using TrivialStadium.Stats;
using TrivialStadium.UI;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TrivialStadium.Editor
{
    /// <summary>
    /// Unity Editor menu extensions for setting up the sample object in the active scene,
    /// configuring the Google Play Project ID, applying Release AAB keystore signing, and bumping version codes.
    /// </summary>
    public static class TrivialStadiumSetupMenu
    {
        private const string RootObjectName = "TrivialStadium_SampleRoot";
        private const string GpgsDefineSymbol = "GPGS_ENABLED";

        [MenuItem("Tools/Trivial Stadium (GPGS Stats)/1. Setup Sample in Current Scene", false, 10)]
        public static void SetupSampleInCurrentScene()
        {
#if UNITY_2022_2_OR_NEWER
            Camera anyCam = Object.FindFirstObjectByType<Camera>();
            TrivialStadiumUI existing = Object.FindFirstObjectByType<TrivialStadiumUI>();
#else
            Camera anyCam = Object.FindObjectOfType<Camera>();
            TrivialStadiumUI existing = Object.FindObjectOfType<TrivialStadiumUI>();
#endif
            if (Camera.main == null && anyCam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.08f, 0.10f, 0.14f);
            }

            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[TrivialStadium] Sample object already exists in the current scene. Press Play to run.");
                return;
            }

            var rootGo = new GameObject(RootObjectName);
            Undo.RegisterCreatedObjectUndo(rootGo, "Create Trivial Stadium Sample");

            rootGo.AddComponent<GpgsStatsService>();
            rootGo.AddComponent<BattleEngine>();
            rootGo.AddComponent<TrivialStadiumUI>();

            Selection.activeGameObject = rootGo;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log("[TrivialStadium] Scene setup complete! Press Play to launch the battle arena and GPGS stats viewer.");
        }

        [MenuItem("Tools/Trivial Stadium (GPGS Stats)/2. Enable GPGS_ENABLED Symbol (Android)", false, 30)]
        public static void EnableGpgsSymbol()
        {
            SetAndroidDefineSymbol(GpgsDefineSymbol, true);
        }

        [MenuItem("Tools/Trivial Stadium (GPGS Stats)/3. Disable GPGS_ENABLED Symbol (Android)", false, 31)]
        public static void DisableGpgsSymbol()
        {
            SetAndroidDefineSymbol(GpgsDefineSymbol, false);
        }

        [MenuItem("Tools/Trivial Stadium (GPGS Stats)/4. Configure Release AAB & Keystore Settings", false, 50)]
        public static void ConfigureReleaseAabAndKeystore()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string keystorePath = Path.Combine(projectRoot, "Keystores/trivial_stadium_release.keystore");

            if (!File.Exists(keystorePath))
            {
                Debug.LogError($"[TrivialStadium] Keystore not found at: {keystorePath}");
                return;
            }

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystorePath;
            PlayerSettings.Android.keystorePass = "trivial123";
            PlayerSettings.Android.keyaliasName = "release";
            PlayerSettings.Android.keyaliasPass = "trivial123";

#if UNITY_2021_2_OR_NEWER
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
#else
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
#endif
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.buildAppBundle = true;

            AssetDatabase.SaveAssets();

            Debug.Log(
                "[TrivialStadium] Applied Release AAB build settings and custom Keystore signing!\n" +
                $"Keystore: {keystorePath}\n" +
                "Alias: release / Pass: trivial123\n" +
                "SHA-1: 90:6E:D1:EA:39:74:F9:E9:C9:F2:8A:5D:BA:1D:F5:AE:73:6F:FE:27\n" +
                "You can now build from File > Build Profiles.");
        }

        [MenuItem("Tools/Trivial Stadium (GPGS Stats)/5. Set Google Play Project ID (App ID)", false, 51)]
        public static void OpenProjectIdWindow()
        {
            GpgsProjectIdWindow.ShowWindow();
        }

        [MenuItem("Tools/Trivial Stadium (GPGS Stats)/6. Increment Android Version Code (+1)", false, 52)]
        public static void IncrementAndroidVersionCode()
        {
            int nextCode = PlayerSettings.Android.bundleVersionCode + 1;
            PlayerSettings.Android.bundleVersionCode = nextCode;
            PlayerSettings.bundleVersion = $"1.{nextCode - 1}";
            AssetDatabase.SaveAssets();
            Debug.Log($"[TrivialStadium] Updated Android Version: Version = {PlayerSettings.bundleVersion}, Bundle Version Code = {nextCode}");
        }

        private static void SetAndroidDefineSymbol(string symbol, bool enabled)
        {
#if UNITY_2021_2_OR_NEWER
            NamedBuildTarget target = NamedBuildTarget.Android;
            string defines = PlayerSettings.GetScriptingDefineSymbols(target);
#else
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android);
#endif
            var list = new System.Collections.Generic.List<string>(
                defines.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries));

            if (enabled && !list.Contains(symbol))
            {
                list.Add(symbol);
#if UNITY_2021_2_OR_NEWER
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", list));
#else
                PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android, string.Join(";", list));
#endif
                Debug.Log($"[TrivialStadium] Added `{symbol}` to Android Scripting Define Symbols.");
            }
            else if (!enabled && list.Contains(symbol))
            {
                list.Remove(symbol);
#if UNITY_2021_2_OR_NEWER
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", list));
#else
                PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android, string.Join(";", list));
#endif
                Debug.Log($"[TrivialStadium] Removed `{symbol}` from Android Scripting Define Symbols.");
            }
            else
            {
                Debug.Log($"[TrivialStadium] `{symbol}` is already {(enabled ? "enabled" : "disabled")}.");
            }
        }
    }

    public class GpgsProjectIdWindow : EditorWindow
    {
        private string _projectId = "";

        public static void ShowWindow()
        {
            var win = GetWindow<GpgsProjectIdWindow>(true, "Set Google Play Project ID", true);
            win.minSize = new Vector2(440, 150);
            win.maxSize = new Vector2(440, 150);
            win._projectId = GpgsAndroidBuildProcessor.GetConfiguredProjectId();
            if (win._projectId == "000000000000") win._projectId = "";
            win.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(
                "Enter the 11-13 digit numeric Project ID (App ID) shown under\n" +
                "Play Console > Play Games Services > Setup and management > Configuration:",
                EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space(8);
            _projectId = EditorGUILayout.TextField("Project ID (digits only)", _projectId?.Trim() ?? "");

            EditorGUILayout.Space(14);
            if (GUILayout.Button("Save Project ID", GUILayout.Height(32)))
            {
                EditorPrefs.SetString(GpgsAndroidBuildProcessor.ProjectIdEditorPrefKey, _projectId);

#if UNITY_2022_2_OR_NEWER
                var service = Object.FindFirstObjectByType<GpgsStatsService>();
#else
                var service = Object.FindObjectOfType<GpgsStatsService>();
#endif
                if (service != null)
                {
                    Undo.RecordObject(service, "Set Google Play Project ID");
                    service.GooglePlayProjectId = _projectId;
                    EditorUtility.SetDirty(service);
                    EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                }

                Debug.Log($"[TrivialStadium] Configured Google Play Project ID to `{_projectId}`.");
                Close();
            }
        }
    }
}
#endif

#if UNITY_EDITOR
using System.IO;
using System.Text;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TrivialStadium.Editor
{
    /// <summary>
    /// Android build processor that automatically configures and injects:
    /// 0. Custom Keystore credentials (trivial123) and Portrait screen orientation before build
    /// 1. Gradle dependency: `com.google.android.gms:play-services-games-v2:22.0.0`
    /// 2. AndroidManifest.xml: `android:isGame="true"`, `android:appCategory="game"`, `android:screenOrientation="portrait"`, and `com.google.android.gms.games.APP_ID`
    /// 3. res/values/games-ids.xml: Google Play Console Project ID (App ID)
    /// 4. GpgsNativeBridge.java: JNI bridge for PlayGamesSdk.initialize / SignIn / PlayerStatsClient / GameStatsClient
    /// </summary>
    public class GpgsAndroidBuildProcessor : IPreprocessBuildWithReport, IPostGenerateGradleAndroidProject
    {
        public const string ProjectIdEditorPrefKey = "TrivialStadium_GpgsProjectId";
        private const string DefaultPlaceholderProjectId = "000000000000";

        public int callbackOrder => 100;

        [InitializeOnLoadMethod]
        private static void EnsureKeystorePasswordsOnLoad()
        {
            ApplyCustomKeystoreCredentials();
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.Android)
            {
                ApplyCustomKeystoreCredentials();
            }
        }

        private static void ApplyCustomKeystoreCredentials()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string keystorePath = Path.Combine(projectRoot, "Keystores/trivial_stadium_release.keystore");
            if (File.Exists(keystorePath))
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = keystorePath;
                PlayerSettings.Android.keystorePass = "trivial123";
                PlayerSettings.Android.keyaliasName = "release";
                PlayerSettings.Android.keyaliasPass = "trivial123";
            }

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            if (PlayerSettings.Android.bundleVersionCode < 6)
            {
                PlayerSettings.Android.bundleVersionCode = 6;
                PlayerSettings.bundleVersion = "1.5";
                Debug.Log("[GpgsAndroidBuildProcessor] Updated Android Bundle Version Code to 6 (Version 1.5).");
            }
        }

        public static string GetConfiguredProjectId()
        {
            string id = EditorPrefs.GetString(ProjectIdEditorPrefKey, "").Trim();
            if (string.IsNullOrEmpty(id))
            {
                // Fallback to GpgsStatsService component in the active scene if present
#if UNITY_2022_2_OR_NEWER
                var service = Object.FindFirstObjectByType<TrivialStadium.Stats.GpgsStatsService>();
#else
                var service = Object.FindObjectOfType<TrivialStadium.Stats.GpgsStatsService>();
#endif
                if (service != null && !string.IsNullOrEmpty(service.GooglePlayProjectId))
                {
                    id = service.GooglePlayProjectId.Trim();
                }
            }
            return string.IsNullOrEmpty(id) ? DefaultPlaceholderProjectId : id;
        }

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string projectId = GetConfiguredProjectId();
            if (projectId == DefaultPlaceholderProjectId)
            {
                Debug.LogWarning(
                    "[GpgsAndroidBuildProcessor] Google Play Games Project ID (11-13 digits) is not configured. " +
                    "Set it via Tools > Trivial Stadium (GPGS Stats) > Set Google Play Project ID.");
            }
            else
            {
                Debug.Log($"[GpgsAndroidBuildProcessor] Applying Google Play Project ID = {projectId} to Android Gradle project.");
            }

            PatchBuildGradle(path);
            WriteGamesIdsXml(path, projectId);
            PatchAndroidManifest(Path.Combine(path, "src/main/AndroidManifest.xml"), includeAppIdMeta: true);

            string rootGradleDir = Directory.GetParent(path)?.FullName;
            if (!string.IsNullOrEmpty(rootGradleDir))
            {
                string launcherManifest = Path.Combine(rootGradleDir, "launcher/src/main/AndroidManifest.xml");
                PatchAndroidManifest(launcherManifest, includeAppIdMeta: false);
            }

            WriteJavaNativeBridge(path);
        }

        private static void PatchBuildGradle(string unityLibraryPath)
        {
            string buildGradlePath = Path.Combine(unityLibraryPath, "build.gradle");
            if (!File.Exists(buildGradlePath)) return;

            string content = File.ReadAllText(buildGradlePath);
            const string gpgsDep = "implementation 'com.google.android.gms:play-services-games-v2:22.0.0'";

            if (!content.Contains("play-services-games-v2"))
            {
                int depIdx = content.IndexOf("dependencies {");
                if (depIdx >= 0)
                {
                    int insertPos = content.IndexOf('\n', depIdx) + 1;
                    content = content.Insert(insertPos, $"    {gpgsDep}\n");
                    File.WriteAllText(buildGradlePath, content, new UTF8Encoding(false));
                    Debug.Log("[GpgsAndroidBuildProcessor] Added play-services-games-v2:22.0.0 to unityLibrary/build.gradle.");
                }
            }
        }

        private static void WriteGamesIdsXml(string unityLibraryPath, string projectId)
        {
            string valuesDir = Path.Combine(unityLibraryPath, "src/main/res/values");
            Directory.CreateDirectory(valuesDir);

            string xmlPath = Path.Combine(valuesDir, "games-ids.xml");
            string xmlContent =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                "<resources>\n" +
                $"    <string name=\"game_services_project_id\" translatable=\"false\">{projectId}</string>\n" +
                "</resources>\n";

            File.WriteAllText(xmlPath, xmlContent, new UTF8Encoding(false));
        }

        private static void PatchAndroidManifest(string manifestPath, bool includeAppIdMeta)
        {
            if (!File.Exists(manifestPath)) return;

            var doc = new XmlDocument();
            doc.Load(manifestPath);

            const string androidNs = "http://schemas.android.com/apk/res/android";
            XmlNode appNode = doc.SelectSingleNode("/manifest/application");
            if (appNode is XmlElement appElem)
            {
                // Required attributes for Sidekick and Google Play Games to recognize the app as a game
                appElem.SetAttribute("isGame", androidNs, "true");
                appElem.SetAttribute("appCategory", androidNs, "game");

                foreach (XmlNode child in appElem.ChildNodes)
                {
                    if (child is XmlElement actElem && actElem.Name == "activity")
                    {
                        actElem.SetAttribute("screenOrientation", androidNs, "portrait");
                    }
                }

                if (includeAppIdMeta)
                {
                    bool hasAppIdMeta = false;
                    foreach (XmlNode child in appElem.ChildNodes)
                    {
                        if (child is XmlElement meta && meta.Name == "meta-data")
                        {
                            string metaName = meta.GetAttribute("name", androidNs);
                            if (metaName == "com.google.android.gms.games.APP_ID")
                            {
                                meta.SetAttribute("value", androidNs, "@string/game_services_project_id");
                                hasAppIdMeta = true;
                            }
                        }
                    }

                    if (!hasAppIdMeta)
                    {
                        XmlElement metaElem = doc.CreateElement("meta-data");
                        metaElem.SetAttribute("name", androidNs, "com.google.android.gms.games.APP_ID");
                        metaElem.SetAttribute("value", androidNs, "@string/game_services_project_id");
                        appElem.AppendChild(metaElem);
                    }
                }

                doc.Save(manifestPath);
                Debug.Log($"[GpgsAndroidBuildProcessor] Configured android:isGame=\"true\" and portrait orientation in {manifestPath}.");
            }
        }

        private static void WriteJavaNativeBridge(string unityLibraryPath)
        {
            string javaDir = Path.Combine(unityLibraryPath, "src/main/java/com/trivialstadium/gpgs");
            Directory.CreateDirectory(javaDir);

            string javaPath = Path.Combine(javaDir, "GpgsNativeBridge.java");
            const string javaCode = @"package com.trivialstadium.gpgs;

import android.app.Activity;
import android.util.Log;
import com.google.android.gms.games.GameStatsClient;
import com.google.android.gms.games.GamesSignInClient;
import com.google.android.gms.games.PlayGames;
import com.google.android.gms.games.PlayGamesSdk;
import com.google.android.gms.games.PlayerStatsClient;
import com.google.android.gms.games.playergameevent.PlayerGameEvent;
import com.google.android.gms.games.stats.PlayerStats;
import com.unity3d.player.UnityPlayer;
import java.util.Arrays;
import org.json.JSONObject;

public class GpgsNativeBridge {
    private static final String TAG = ""GpgsNativeBridge"";
    private static boolean sInitialized = false;

    private static void ensureInitialized(Activity activity) {
        if (!sInitialized && activity != null) {
            PlayGamesSdk.initialize(activity);
            sInitialized = true;
            Log.i(TAG, ""PlayGamesSdk.initialize() completed"");
        }
    }

    public static void initializeAndSignIn(final String callbackGameObject) {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) {
            sendToUnity(callbackGameObject, ""OnNativeAuthResult"", ""false|Activity is null"");
            return;
        }

        activity.runOnUiThread(() -> {
            try {
                ensureInitialized(activity);

                final GamesSignInClient signInClient = PlayGames.getGamesSignInClient(activity);
                signInClient.isAuthenticated().addOnCompleteListener(task -> {
                    boolean isAuth = task.isSuccessful() && task.getResult() != null && task.getResult().isAuthenticated();
                    if (isAuth) {
                        fetchPlayerNameAndNotify(activity, callbackGameObject);
                    } else {
                        signInClient.signIn().addOnCompleteListener(signInTask -> {
                            boolean signedIn = signInTask.isSuccessful() && signInTask.getResult() != null && signInTask.getResult().isAuthenticated();
                            if (signedIn) {
                                fetchPlayerNameAndNotify(activity, callbackGameObject);
                            } else {
                                String err = signInTask.getException() != null ? signInTask.getException().getMessage() : ""SignIn Canceled/Failed"";
                                sendToUnity(callbackGameObject, ""OnNativeAuthResult"", ""false|"" + err);
                            }
                        });
                    }
                });
            } catch (Exception e) {
                Log.e(TAG, ""initializeAndSignIn exception"", e);
                sendToUnity(callbackGameObject, ""OnNativeAuthResult"", ""false|"" + e.getMessage());
            }
        });
    }

    private static void fetchPlayerNameAndNotify(final Activity activity, final String callbackGameObject) {
        PlayGames.getPlayersClient(activity).getCurrentPlayer().addOnCompleteListener(playerTask -> {
            String displayName = ""PlayGamesUser"";
            if (playerTask.isSuccessful() && playerTask.getResult() != null) {
                displayName = playerTask.getResult().getDisplayName();
            }
            sendToUnity(callbackGameObject, ""OnNativeAuthResult"", ""true|"" + displayName);
        });
    }

    public static void loadPlayerStats(final String callbackGameObject) {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) return;

        activity.runOnUiThread(() -> {
            try {
                ensureInitialized(activity);
                final PlayerStatsClient statsClient = PlayGames.getPlayerStatsClient(activity);
                statsClient.loadPlayerStats(true).addOnCompleteListener(task -> {
                    if (task.isSuccessful() && task.getResult() != null && task.getResult().get() != null) {
                        emitPlayerStatsJson(callbackGameObject, task.getResult().get(), task.getResult().isStale());
                    } else {
                        // If force reload returns 26504 (NETWORK_ERROR_NO_DATA / UNAVAILABLE), try cached data (false)
                        statsClient.loadPlayerStats(false).addOnCompleteListener(fallbackTask -> {
                            if (fallbackTask.isSuccessful() && fallbackTask.getResult() != null && fallbackTask.getResult().get() != null) {
                                emitPlayerStatsJson(callbackGameObject, fallbackTask.getResult().get(), fallbackTask.getResult().isStale());
                            } else {
                                String err = task.getException() != null ? task.getException().getMessage() : ""PlayerStats load failed"";
                                sendToUnity(callbackGameObject, ""OnNativePlayerStatsJson"", ""{\""ok\"":false,\""error\"":\"""" + err + ""\""}"");
                            }
                        });
                    }
                });
            } catch (Exception e) {
                sendToUnity(callbackGameObject, ""OnNativePlayerStatsJson"", ""{\""ok\"":false,\""error\"":\"""" + e.getMessage() + ""\""}"");
            }
        });
    }

    private static void emitPlayerStatsJson(String callbackGameObject, PlayerStats stats, boolean isStale) {
        try {
            JSONObject json = new JSONObject();
            json.put(""ok"", true);
            json.put(""isStale"", isStale);
            json.put(""numberOfSessions"", stats.getNumberOfSessions());
            json.put(""avgSessionLength"", (double) stats.getAverageSessionLength());
            json.put(""daysSinceLastPlayed"", stats.getDaysSinceLastPlayed());
            json.put(""numberOfPurchases"", stats.getNumberOfPurchases());
            json.put(""sessPercentile"", (double) stats.getSessionPercentile());
            json.put(""spendPercentile"", (double) stats.getSpendPercentile());
            json.put(""spendProbability"", (double) stats.getSpendProbability());
            json.put(""churnProbability"", (double) stats.getChurnProbability());
            json.put(""highSpenderProbability"", (double) stats.getHighSpenderProbability());
            json.put(""totalSpendNext28Days"", (double) stats.getTotalSpendNext28Days());
            sendToUnity(callbackGameObject, ""OnNativePlayerStatsJson"", json.toString());
        } catch (Exception je) {
            sendToUnity(callbackGameObject, ""OnNativePlayerStatsJson"", ""{\""ok\"":false,\""error\"":\"""" + je.getMessage() + ""\""}"");
        }
    }

    public static void recordBattleStartedEvent(final int waveNumber, final String enemyName) {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) return;

        activity.runOnUiThread(() -> {
            try {
                ensureInitialized(activity);
                GameStatsClient client = PlayGames.getGameStatsClient(activity);
                PlayerGameEvent event = new PlayerGameEvent.Builder(""battle_started"")
                    .addProperty(""wave_number"", (long) waveNumber)
                    .addProperty(""enemy_name"", enemyName != null ? enemyName : """")
                    .build();
                client.recordEvent(event);
                client.requestEventsUpload();
                Log.i(TAG, ""GameStatsClient.recordEvent(battle_started) + requestEventsUpload() sent"");
            } catch (Exception e) {
                Log.w(TAG, ""recordBattleStartedEvent failed: "" + e.getMessage());
            }
        });
    }

    public static void recordDamageDealtEvent(final int damageAmount, final String skillName, final boolean isCritical) {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) return;

        activity.runOnUiThread(() -> {
            try {
                ensureInitialized(activity);
                GameStatsClient client = PlayGames.getGameStatsClient(activity);
                PlayerGameEvent dmgEvent = new PlayerGameEvent.Builder(""damage_dealt"")
                    .addProperty(""damage_amount"", (long) damageAmount)
                    .addProperty(""skill_name"", skillName != null ? skillName : """")
                    .addProperty(""is_critical"", isCritical)
                    .build();
                if (isCritical) {
                    PlayerGameEvent critEvent = new PlayerGameEvent.Builder(""critical_hit_landed"")
                        .addProperty(""damage_amount"", (long) damageAmount)
                        .addProperty(""skill_name"", skillName != null ? skillName : """")
                        .build();
                    client.recordEvents(Arrays.asList(dmgEvent, critEvent));
                } else {
                    client.recordEvent(dmgEvent);
                }
                client.requestEventsUpload();
                Log.i(TAG, ""GameStatsClient.recordEvent(damage_dealt) + requestEventsUpload() sent"");
            } catch (Exception e) {
                Log.w(TAG, ""recordDamageDealtEvent failed: "" + e.getMessage());
            }
        });
    }

    public static void recordPotionUsedEvent(final int healedAmount, final int remainingPotions) {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) return;

        activity.runOnUiThread(() -> {
            try {
                ensureInitialized(activity);
                GameStatsClient client = PlayGames.getGameStatsClient(activity);
                PlayerGameEvent event = new PlayerGameEvent.Builder(""potion_used"")
                    .addProperty(""healed_amount"", (long) healedAmount)
                    .addProperty(""remaining_potions"", (long) remainingPotions)
                    .build();
                client.recordEvent(event);
                client.requestEventsUpload();
                Log.i(TAG, ""GameStatsClient.recordEvent(potion_used) + requestEventsUpload() sent"");
            } catch (Exception e) {
                Log.w(TAG, ""recordPotionUsedEvent failed: "" + e.getMessage());
            }
        });
    }

    public static void recordBattleWonEvent(final int waveNumber, final int remainingHp, final String enemyName) {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) return;

        activity.runOnUiThread(() -> {
            try {
                ensureInitialized(activity);
                GameStatsClient client = PlayGames.getGameStatsClient(activity);
                PlayerGameEvent defeatedEvent = new PlayerGameEvent.Builder(""monster_defeated"")
                    .addProperty(""enemy_name"", enemyName != null ? enemyName : """")
                    .addProperty(""wave_number"", (long) waveNumber)
                    .build();
                PlayerGameEvent wonEvent = new PlayerGameEvent.Builder(""battle_won"")
                    .addProperty(""wave_number"", (long) waveNumber)
                    .addProperty(""remaining_hp"", (long) remainingHp)
                    .build();
                client.recordEvents(Arrays.asList(defeatedEvent, wonEvent));
                client.requestEventsUpload();
                Log.i(TAG, ""GameStatsClient.recordEvents(monster_defeated, battle_won) + requestEventsUpload() sent"");
            } catch (Exception e) {
                Log.w(TAG, ""recordBattleWonEvent failed: "" + e.getMessage());
            }
        });
    }

    private static void sendToUnity(String goName, String methodName, String payload) {
        try {
            UnityPlayer.UnitySendMessage(goName, methodName, payload != null ? payload : """");
        } catch (Exception e) {
            Log.e(TAG, ""UnitySendMessage failed"", e);
        }
    }
}
";

            File.WriteAllText(javaPath, javaCode, new UTF8Encoding(false));
        }
    }
}
#endif

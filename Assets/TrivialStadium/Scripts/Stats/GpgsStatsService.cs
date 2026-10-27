using System;
using System.Collections.Generic;
using UnityEngine;

#if GPGS_ENABLED && UNITY_ANDROID
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using GooglePlayGames.BasicApi.Events;
#endif

namespace TrivialStadium.Stats
{
    /// <summary>
    /// Service class managing Google Play Games Services (GPGS v2) sign-in,
    /// PlayerStats API (PlayerStatsClient.loadPlayerStats), and Game Stats (GameStatsClient / PlayerGameEvent).
    ///
    /// - On Android devices, calls the native Google Play Games Services v2 SDK via
    ///   `GpgsNativeBridge` injected automatically by `GpgsAndroidBuildProcessor` (no external plugin required).
    /// - In the Unity Editor (or when `forceMockMode = true`), operates using simulated stats.
    /// </summary>
    public class GpgsStatsService : MonoBehaviour
    {
        public static GpgsStatsService Instance { get; private set; }

        [Header("Google Play Console Project ID (11-13 digits)")]
        [Tooltip("Enter the numeric Project ID from Play Console > Play Games Services > Setup and management > Configuration")]
        [SerializeField] private string googlePlayProjectId = "";

        [Header("Mode Settings")]
        [Tooltip("When enabled, uses mock stats simulation even on Android devices (always enabled in the Unity Editor).")]
        [SerializeField] private bool forceMockMode = false;

        [Header("Play Console Game Stats / Event ID Config")]
        [SerializeField] private GpgsEventIdConfig eventIdConfig = new GpgsEventIdConfig();

        public string GooglePlayProjectId
        {
            get => googlePlayProjectId;
            set => googlePlayProjectId = value;
        }

        public bool IsAuthenticated { get; private set; }
        public string AuthStatusText { get; private set; } = "Not Authenticated";
        public PlayerStatsSnapshot CurrentPlayerStats { get; private set; } = new PlayerStatsSnapshot();

        private readonly Dictionary<BattleStatEventType, BattleEventStatItem> _battleEvents =
            new Dictionary<BattleStatEventType, BattleEventStatItem>();

        // Internal state for mock simulation
        private float _sessionStartRealtime;
        private int _mockNumberOfSessions = 3;
        private float _mockBaseAvgSessionMinutes = 12.5f;
        private int _mockDaysSinceLastPlayed = 1;
        private int _mockNumberOfPurchases = 0;
        private float _mockCumulativeSpendUsd = 0f;
        private ulong _mockBattlesPlayedCount = 0;

        public event Action<PlayerStatsSnapshot> OnPlayerStatsUpdated;
        public event Action<IReadOnlyDictionary<BattleStatEventType, BattleEventStatItem>> OnBattleEventsUpdated;
        public event Action<string> OnApiLogMessage;

        public IReadOnlyDictionary<BattleStatEventType, BattleEventStatItem> BattleEvents => _battleEvents;

        public bool IsUsingMockMode
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return forceMockMode;
#else
                return true;
#endif
            }
        }

        [Serializable]
        private class NativePlayerStatsPayload
        {
            public bool ok;
            public bool isStale;
            public string error;
            public int numberOfSessions = -1;
            public float avgSessionLength = -1f;
            public int daysSinceLastPlayed = -1;
            public int numberOfPurchases = -1;
            public float sessPercentile = -1f;
            public float spendPercentile = -1f;
            public float spendProbability = -1f;
            public float churnProbability = -1f;
            public float highSpenderProbability = -1f;
            public float totalSpendNext28Days = -1f;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _sessionStartRealtime = Time.realtimeSinceStartup;
            InitializeBattleEventDictionary();
        }

        private void Start()
        {
            AuthenticateAndFetchAll();
        }

        private void InitializeBattleEventDictionary()
        {
            _battleEvents.Clear();

            RegisterStatDefinition(
                BattleStatEventType.MaxWaveReached,
                "battle_won.wave_number",
                "MAX",
                "Highest Wave Reached (max_wave_reached)",
                "Highest battle wave reached by the player");

            RegisterStatDefinition(
                BattleStatEventType.TotalDamageDealt,
                "damage_dealt.damage_amount",
                "SUM",
                "Total Damage Dealt (total_damage_dealt)",
                "Total cumulative damage dealt to monsters");

            RegisterStatDefinition(
                BattleStatEventType.MaxCriticalDamage,
                "critical_hit_landed.damage_amount",
                "MAX",
                "Max Critical Damage (max_critical_damage)",
                "Maximum damage dealt in a single critical hit");

            RegisterStatDefinition(
                BattleStatEventType.BestVictoryHp,
                "battle_won.remaining_hp",
                "MAX",
                "Best Victory HP (best_victory_hp)",
                "Highest remaining HP at battle victory");

            RegisterStatDefinition(
                BattleStatEventType.TotalHealedHp,
                "potion_used.healed_amount",
                "SUM",
                "Total HP Healed (total_healed_hp)",
                "Total cumulative HP recovered using potions");
        }

        private void RegisterStatDefinition(
            BattleStatEventType type,
            string sourceEventAndProp,
            string aggregationType,
            string displayName,
            string description)
        {
            _battleEvents[type] = new BattleEventStatItem
            {
                EventType = type,
                EventId = eventIdConfig.GetEventId(type),
                SourceEventAndProp = sourceEventAndProp,
                AggregationType = aggregationType,
                DisplayName = displayName,
                Description = description,
                CurrentCount = 0
            };
        }

        /// <summary>
        /// Initializes and signs in to GPGS v2, then fetches both PlayerStats and Battle Events.
        /// </summary>
        public void AuthenticateAndFetchAll()
        {
            if (IsUsingMockMode)
            {
                IsAuthenticated = true;
                AuthStatusText = "Authenticated (Mock / Editor Simulation)";
                EmitApiLog("[Auth] Authenticated in Mock Mode (PlayGamesPlatform Simulation)");
                FetchPlayerStats();
                FetchAllBattleEvents();
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                AuthStatusText = "Signing in to Google Play Games...";
                EmitApiLog("[Auth] Calling PlayGamesSdk.initialize() & GamesSignInClient.signIn()...");
                using (var bridge = new AndroidJavaClass("com.trivialstadium.gpgs.GpgsNativeBridge"))
                {
                    bridge.CallStatic("initializeAndSignIn", gameObject.name);
                }
            }
            catch (Exception e)
            {
                IsAuthenticated = false;
                AuthStatusText = "Native Bridge Error -> Mock Fallback";
                EmitApiLog($"[Auth] NativeBridge call failed ({e.Message}). Falling back to mock mode.");
                RebuildMockPlayerStats("Bridge Error (Fallback Mock)");
                OnPlayerStatsUpdated?.Invoke(CurrentPlayerStats);
            }
#endif
        }

        /// <summary>
        /// Callback invoked via UnitySendMessage from GpgsNativeBridge.java with sign-in results.
        /// </summary>
        public void OnNativeAuthResult(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return;

            string[] parts = payload.Split(new[] { '|' }, 2);
            bool success = parts.Length > 0 && parts[0] == "true";
            string detail = parts.Length > 1 ? parts[1] : "";

            if (success)
            {
                IsAuthenticated = true;
                AuthStatusText = $"Authenticated (GPGS: {detail})";
                EmitApiLog($"[Auth] Google Play Games sign-in succeeded: {detail}");
                FetchPlayerStats();
                FetchAllBattleEvents();
            }
            else
            {
                IsAuthenticated = false;
                AuthStatusText = $"SignIn Failed ({detail}) -> Fallback Mock";
                EmitApiLog($"[Auth] GPGS sign-in failed ({detail}). Verify Project ID, SHA-1, and tester account.");
                RebuildMockPlayerStats($"SignIn Failed: {detail}");
                OnPlayerStatsUpdated?.Invoke(CurrentPlayerStats);
            }
        }

        /// <summary>
        /// Calls the Google Play Games PlayerStats API (`PlayerStatsClient.loadPlayerStats`).
        /// </summary>
        public void FetchPlayerStats(Action<PlayerStatsSnapshot> onComplete = null)
        {
            if (IsUsingMockMode || !IsAuthenticated)
            {
                RebuildMockPlayerStats("CommonStatusCodes.Success (Mock)");
                EmitApiLog(
                    $"[GetPlayerStats] Fetched (Mock): Sessions={CurrentPlayerStats.NumberOfSessions}, " +
                    $"AvgLen={CurrentPlayerStats.AvgSessionLength:F1}m, " +
                    $"ChurnProb={CurrentPlayerStats.ChurnProbability:P0}, " +
                    $"SpendProb={CurrentPlayerStats.SpendProbability:P0}");
                OnPlayerStatsUpdated?.Invoke(CurrentPlayerStats);
                onComplete?.Invoke(CurrentPlayerStats);
                return;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                EmitApiLog("[GetPlayerStats] Requesting PlayGames.getPlayerStatsClient().loadPlayerStats(true)...");
                using (var bridge = new AndroidJavaClass("com.trivialstadium.gpgs.GpgsNativeBridge"))
                {
                    bridge.CallStatic("loadPlayerStats", gameObject.name);
                }
            }
            catch (Exception e)
            {
                EmitApiLog($"[GetPlayerStats] Exception: {e.Message}");
            }
#endif
        }

        /// <summary>
        /// Callback invoked via UnitySendMessage from GpgsNativeBridge.java with PlayerStats JSON.
        /// </summary>
        public void OnNativePlayerStatsJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            var parsed = JsonUtility.FromJson<NativePlayerStatsPayload>(json);
            if (parsed != null && parsed.ok)
            {
                CurrentPlayerStats = new PlayerStatsSnapshot
                {
                    IsValid = true,
                    IsFromRealGpgs = true,
                    StatusMessage = parsed.isStale ? "GPGS Success (Stale Cache)" : "GPGS Success (Live)",
                    FetchedAtTimestamp = DateTime.Now.ToString("HH:mm:ss"),

                    NumberOfSessions = parsed.numberOfSessions,
                    AvgSessionLength = parsed.avgSessionLength,
                    DaysSinceLastPlayed = parsed.daysSinceLastPlayed,
                    NumberOfPurchases = parsed.numberOfPurchases,
                    SessPercentile = parsed.sessPercentile,
                    SpendPercentile = parsed.spendPercentile,
                    SpendProbability = parsed.spendProbability,
                    ChurnProbability = parsed.churnProbability,
                    HighSpenderProbability = parsed.highSpenderProbability,
                    TotalSpendNext28Days = parsed.totalSpendNext28Days
                };

                EmitApiLog(
                    $"[GetPlayerStats] GPGS live fetch succeeded: Sessions={CurrentPlayerStats.NumberOfSessions}, " +
                    $"DaysSinceLast={CurrentPlayerStats.DaysSinceLastPlayed}");
            }
            else
            {
                string err = parsed != null ? parsed.error : "Invalid JSON";
                if (err != null && err.Contains("26504"))
                {
                    RebuildMockPlayerStats("GPGS Auth OK (PlayerStats=26504: New App Pending Aggregation)");
                    EmitApiLog("[GetPlayerStats] Legacy PlayerStatsClient returned 26504 (UNAVAILABLE for new app). Sending events via GameStatsClient.");
                }
                else
                {
                    EmitApiLog($"[GetPlayerStats] GPGS fetch failed: {err}");
                }
            }

            OnPlayerStatsUpdated?.Invoke(CurrentPlayerStats);
        }

        // =====================================================================
        // Play Console Event Configuration Recording Methods (GameStatsClient)
        // =====================================================================

        public void RecordBattleStartedEvent(int waveNumber, string enemyName)
        {
            _mockBattlesPlayedCount++;
            EmitApiLog($"[GameStatsClient: battle_started] wave_number={waveNumber} (INT64), enemy_name=\"{enemyName}\" (STRING)");

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsUsingMockMode)
            {
                try
                {
                    using (var bridge = new AndroidJavaClass("com.trivialstadium.gpgs.GpgsNativeBridge"))
                    {
                        bridge.CallStatic("recordBattleStartedEvent", waveNumber, enemyName ?? "");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[GpgsStatsService] recordBattleStartedEvent error: {e.Message}");
                }
            }
#endif
            OnBattleEventsUpdated?.Invoke(_battleEvents);
        }

        public void RecordDamageDealtEvent(int damageAmount, string skillName, bool isCritical)
        {
            uint dmg = (uint)Mathf.Max(0, damageAmount);
            ApplyStatAggregation(BattleStatEventType.TotalDamageDealt, dmg);
            EmitApiLog($"[GameStatsClient: damage_dealt] damage_amount={dmg} (INT64), skill_name=\"{skillName}\" (STRING), is_critical={isCritical.ToString().ToLower()} (BOOL)");

            if (isCritical)
            {
                ApplyStatAggregation(BattleStatEventType.MaxCriticalDamage, dmg);
                EmitApiLog($"[GameStatsClient: critical_hit_landed] damage_amount={dmg} (INT64), skill_name=\"{skillName}\" (STRING)");
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsUsingMockMode)
            {
                try
                {
                    using (var bridge = new AndroidJavaClass("com.trivialstadium.gpgs.GpgsNativeBridge"))
                    {
                        bridge.CallStatic("recordDamageDealtEvent", (int)dmg, skillName ?? "", isCritical);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[GpgsStatsService] recordDamageDealtEvent error: {e.Message}");
                }
            }
#endif
            OnBattleEventsUpdated?.Invoke(_battleEvents);
        }

        public void RecordPotionUsedEvent(int healedAmount, int remainingPotions)
        {
            uint healed = (uint)Mathf.Max(0, healedAmount);
            ApplyStatAggregation(BattleStatEventType.TotalHealedHp, healed);
            EmitApiLog($"[GameStatsClient: potion_used] healed_amount={healed} (INT64), remaining_potions={remainingPotions} (INT64)");

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsUsingMockMode)
            {
                try
                {
                    using (var bridge = new AndroidJavaClass("com.trivialstadium.gpgs.GpgsNativeBridge"))
                    {
                        bridge.CallStatic("recordPotionUsedEvent", (int)healed, remainingPotions);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[GpgsStatsService] recordPotionUsedEvent error: {e.Message}");
                }
            }
#endif
            OnBattleEventsUpdated?.Invoke(_battleEvents);
        }

        public void RecordBattleWonEvent(int waveNumber, int remainingHp, string enemyName)
        {
            ApplyStatAggregation(BattleStatEventType.MaxWaveReached, (uint)Mathf.Max(1, waveNumber));
            ApplyStatAggregation(BattleStatEventType.BestVictoryHp, (uint)Mathf.Max(0, remainingHp));

            EmitApiLog($"[GameStatsClient: monster_defeated] enemy_name=\"{enemyName}\" (STRING), wave_number={waveNumber} (INT64)");
            EmitApiLog($"[GameStatsClient: battle_won] wave_number={waveNumber} (INT64), remaining_hp={remainingHp} (INT64)");

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsUsingMockMode)
            {
                try
                {
                    using (var bridge = new AndroidJavaClass("com.trivialstadium.gpgs.GpgsNativeBridge"))
                    {
                        bridge.CallStatic("recordBattleWonEvent", waveNumber, remainingHp, enemyName ?? "");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[GpgsStatsService] recordBattleWonEvent error: {e.Message}");
                }
            }
#endif
            OnBattleEventsUpdated?.Invoke(_battleEvents);
        }

        private void ApplyStatAggregation(BattleStatEventType statType, uint value)
        {
            if (!_battleEvents.TryGetValue(statType, out BattleEventStatItem item)) return;

            if (item.AggregationType == "MAX")
            {
                if (value > item.CurrentCount)
                {
                    item.CurrentCount = value;
                }
            }
            else
            {
                item.CurrentCount += value;
            }
        }

        public void FetchAllBattleEvents()
        {
            EmitApiLog("[GameStats] Synchronized RepetitiveStatsConfig aggregated values.");
            OnBattleEventsUpdated?.Invoke(_battleEvents);
        }

        // =====================================================================
        // Mock / Simulation Control Methods
        // =====================================================================

        private void RebuildMockPlayerStats(string statusText)
        {
            float currentSessionMinutes = Mathf.Max(0.5f, (Time.realtimeSinceStartup - _sessionStartRealtime) / 60f);

            float avgSessionLen = Mathf.Clamp(
                _mockBaseAvgSessionMinutes + (currentSessionMinutes * 0.8f) + (_mockBattlesPlayedCount * 1.4f),
                1.0f,
                120.0f);

            float sessPercentile = Mathf.Clamp01(0.15f + (_mockNumberOfSessions * 0.055f) + (_mockBattlesPlayedCount * 0.03f));
            float spendPercentile = _mockNumberOfPurchases == 0
                ? 0f
                : Mathf.Clamp01(0.35f + (_mockNumberOfPurchases * 0.14f));

            float churnProb = Mathf.Clamp01(
                0.12f + (_mockDaysSinceLastPlayed * 0.085f) - (Mathf.Min(_mockNumberOfSessions, 15) * 0.015f));

            float spendProb = Mathf.Clamp01(
                0.08f + (sessPercentile * 0.25f) + (_mockNumberOfPurchases * 0.18f) - (churnProb * 0.15f));

            float highSpenderProb = Mathf.Clamp01(
                (_mockNumberOfPurchases >= 3 ? 0.35f : 0.03f) + (_mockNumberOfPurchases * 0.12f));

            float predictedSpend28Days = Mathf.Round((spendProb * 12f + highSpenderProb * 36f) * 10f) / 10f;

            CurrentPlayerStats = new PlayerStatsSnapshot
            {
                IsValid = true,
                IsFromRealGpgs = false,
                StatusMessage = statusText,
                FetchedAtTimestamp = DateTime.Now.ToString("HH:mm:ss"),

                NumberOfSessions = _mockNumberOfSessions,
                AvgSessionLength = (float)Math.Round(avgSessionLen, 1),
                DaysSinceLastPlayed = _mockDaysSinceLastPlayed,
                NumberOfPurchases = _mockNumberOfPurchases,
                SessPercentile = (float)Math.Round(sessPercentile, 2),
                SpendPercentile = (float)Math.Round(spendPercentile, 2),
                SpendProbability = (float)Math.Round(spendProb, 2),
                ChurnProbability = (float)Math.Round(churnProb, 2),
                HighSpenderProbability = (float)Math.Round(highSpenderProb, 2),
                TotalSpendNext28Days = predictedSpend28Days
            };
        }

        public void SimulateNewSession()
        {
            _mockNumberOfSessions += 1;
            _mockDaysSinceLastPlayed = 0;
            _mockBaseAvgSessionMinutes += 1.8f;
            EmitApiLog($"[Sim] Simulated new session (NumberOfSessions -> {_mockNumberOfSessions}, DaysSinceLastPlayed -> 0)");
            FetchPlayerStats();
        }

        public void SimulateDaysAbsent(int daysToAdd = 7)
        {
            _mockDaysSinceLastPlayed += daysToAdd;
            EmitApiLog($"[Sim] Simulated +{daysToAdd} days absent (DaysSinceLastPlayed -> {_mockDaysSinceLastPlayed}d)");
            FetchPlayerStats();
        }

        public void SimulateInAppPurchase(float priceUsd = 4.99f)
        {
            _mockNumberOfPurchases += 1;
            _mockCumulativeSpendUsd += priceUsd;
            EmitApiLog($"[Sim] Simulated in-app purchase (NumberOfPurchases -> {_mockNumberOfPurchases}, Total ${ _mockCumulativeSpendUsd:F2})");
            FetchPlayerStats();
        }

        public void ResetMockStats()
        {
            _sessionStartRealtime = Time.realtimeSinceStartup;
            _mockNumberOfSessions = 3;
            _mockBaseAvgSessionMinutes = 12.5f;
            _mockDaysSinceLastPlayed = 1;
            _mockNumberOfPurchases = 0;
            _mockCumulativeSpendUsd = 0f;
            _mockBattlesPlayedCount = 0;

            foreach (var item in _battleEvents.Values)
            {
                item.CurrentCount = 0;
            }

            EmitApiLog("[Sim] Reset mock PlayerStats and battle event counters.");
            FetchPlayerStats();
            FetchAllBattleEvents();
        }

        private void EmitApiLog(string message)
        {
            Debug.Log($"[GpgsStatsService] {message}");
            OnApiLogMessage?.Invoke(message);
        }
    }
}

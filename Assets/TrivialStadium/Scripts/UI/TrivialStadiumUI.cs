using System;
using System.Collections.Generic;
using TrivialStadium.Battle;
using TrivialStadium.Stats;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TrivialStadium.UI
{
    /// <summary>
    /// Automatically builds and updates the Portrait-locked battle UI and
    /// Google Play Game Stats API dashboard UI entirely from script.
    /// Designed for mobile screens (SafeArea-aware) with large readable typography,
    /// top-bottom vertical split, and tab switching.
    /// </summary>
    [RequireComponent(typeof(GpgsStatsService))]
    [RequireComponent(typeof(BattleEngine))]
    public class TrivialStadiumUI : MonoBehaviour
    {
        private GpgsStatsService _statsService;
        private BattleEngine _battleEngine;
        private Font _uiFont;

        // SafeArea tracking
        private RectTransform _safeAreaRect;
        private Rect _lastSafeArea = Rect.zero;
        private Vector2Int _lastScreenSize = Vector2Int.zero;

        // Battle UI References
        private Text _waveHeaderText;
        private Text _perkBannerText;
        private Text _playerNameText;
        private Text _playerStatsSubText;
        private Image _playerHpFill;
        private Text _playerHpText;

        private Text _enemyNameText;
        private Text _enemyStatsSubText;
        private Image _enemyHpFill;
        private Text _enemyHpText;

        private Button _attackButton;
        private Button _heavyStrikeButton;
        private Text _heavyStrikeButtonText;
        private Button _potionButton;
        private Text _potionButtonText;
        private Button _nextWaveButton;
        private Text _nextWaveButtonText;
        private Text _battleLogText;

        // GPGS Stats UI References (Tab Switchable)
        private RectTransform _gameStatsTabRoot;
        private RectTransform _playerStatsTabRoot;
        private Image _gameStatsTabBtnBg;
        private Image _playerStatsTabBtnBg;
        private Text _gameStatsTabBtnText;
        private Text _playerStatsTabBtnText;

        private Text _authStatusText;
        private Text _segmentSummaryText;
        private Text _playerStatsTableLeftText;
        private Text _playerStatsTableRightText;
        private Text _battleEventsTableText;
        private Text _apiTraceLogText;

        private readonly List<string> _apiTraceLogs = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrapIfNeeded()
        {
#if UNITY_2022_2_OR_NEWER
            if (FindFirstObjectByType<TrivialStadiumUI>() != null) return;
#else
            if (FindObjectOfType<TrivialStadiumUI>() != null) return;
#endif
            var rootGo = new GameObject("TrivialStadium_SampleRoot (Auto)");
            rootGo.AddComponent<GpgsStatsService>();
            rootGo.AddComponent<BattleEngine>();
            rootGo.AddComponent<TrivialStadiumUI>();
        }

        private void Awake()
        {
            LockPortraitOrientation();

            _statsService = GetComponent<GpgsStatsService>();
            _battleEngine = GetComponent<BattleEngine>();
            _uiFont = LoadCompatibleFont();

            EnsureEventSystem();
            BuildDashboardUI();
            ApplySafeArea(force: true);
        }

        private static void LockPortraitOrientation()
        {
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.orientation = ScreenOrientation.Portrait;
        }

        private void Update()
        {
            ApplySafeArea(force: false);
        }

        private void ApplySafeArea(bool force)
        {
            if (_safeAreaRect == null) return;

            Rect safe = Screen.safeArea;
            Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
            if (!force && safe == _lastSafeArea && screenSize == _lastScreenSize) return;

            _lastSafeArea = safe;
            _lastScreenSize = screenSize;

            if (screenSize.x <= 0 || screenSize.y <= 0) return;

            Vector2 anchorMin = safe.position;
            Vector2 anchorMax = safe.position + safe.size;
            anchorMin.x /= screenSize.x;
            anchorMin.y /= screenSize.y;
            anchorMax.x /= screenSize.x;
            anchorMax.y /= screenSize.y;

            _safeAreaRect.anchorMin = anchorMin;
            _safeAreaRect.anchorMax = anchorMax;
            _safeAreaRect.offsetMin = Vector2.zero;
            _safeAreaRect.offsetMax = Vector2.zero;
        }

        private void OnEnable()
        {
            if (_statsService != null)
            {
                _statsService.OnPlayerStatsUpdated += HandlePlayerStatsUpdated;
                _statsService.OnBattleEventsUpdated += HandleBattleEventsUpdated;
                _statsService.OnApiLogMessage += HandleApiLogMessage;
            }

            if (_battleEngine != null)
            {
                _battleEngine.OnBattleStateChanged += RefreshBattleUI;
            }
        }

        private void OnDisable()
        {
            if (_statsService != null)
            {
                _statsService.OnPlayerStatsUpdated -= HandlePlayerStatsUpdated;
                _statsService.OnBattleEventsUpdated -= HandleBattleEventsUpdated;
                _statsService.OnApiLogMessage -= HandleApiLogMessage;
            }

            if (_battleEngine != null)
            {
                _battleEngine.OnBattleStateChanged -= RefreshBattleUI;
            }
        }

        private void Start()
        {
            RefreshBattleUI();
            if (_statsService != null)
            {
                HandlePlayerStatsUpdated(_statsService.CurrentPlayerStats);
                HandleBattleEventsUpdated(_statsService.BattleEvents);
            }
        }

        // =====================================================================
        // UI Update Handlers
        // =====================================================================

        private void RefreshBattleUI()
        {
            if (_battleEngine == null || _waveHeaderText == null) return;

            var player = _battleEngine.Player;
            var enemy = _battleEngine.Enemy;

            _waveHeaderText.text = $"TRIVIAL STADIUM  —  WAVE {_battleEngine.CurrentWave}";
            _perkBannerText.text = $"Active Perk: {_battleEngine.ActivePerkDescription}";

            _playerNameText.text = $"{player.Name}  <color=#74B9FF>({player.Title})</color>";
            float effectiveAtk = player.BaseAttack * (_battleEngine.HasComebackBuff ? 1.30f : 1.0f);
            float effectiveCrit = (player.CritChance + (_battleEngine.HasVeteranBuff ? 0.15f : 0f)) * 100f;
            _playerStatsSubText.text = $"ATK: {Mathf.RoundToInt(effectiveAtk)}   |   CRIT: {effectiveCrit:F0}%   |   Potions: {_battleEngine.PotionCount}";
            _playerHpFill.fillAmount = player.HpRatio;
            _playerHpFill.color = player.HpRatio > 0.35f
                ? new Color(0.18f, 0.80f, 0.44f)
                : new Color(0.95f, 0.35f, 0.35f);
            _playerHpText.text = $"HP {player.CurrentHp} / {player.MaxHp}";

            _enemyNameText.text = $"{enemy.Name}  <color=#FFEAA7>({enemy.Title})</color>";
            _enemyStatsSubText.text = $"ATK: {enemy.BaseAttack}   |   CRIT: {enemy.CritChance * 100f:F0}%";
            _enemyHpFill.fillAmount = enemy.HpRatio;
            _enemyHpFill.color = enemy.ThemeColor;
            _enemyHpText.text = $"HP {enemy.CurrentHp} / {enemy.MaxHp}";

            bool canAct = !_battleEngine.IsBattleOver;
            _attackButton.interactable = canAct;
            _heavyStrikeButton.interactable = canAct && _battleEngine.HeavyStrikeCooldown == 0;
            _heavyStrikeButtonText.text = _battleEngine.HeavyStrikeCooldown > 0
                ? $"★ Heavy Strike (CD:{_battleEngine.HeavyStrikeCooldown})"
                : "★ Heavy Strike (Skill)";

            _potionButton.interactable = canAct && _battleEngine.PotionCount > 0;
            _potionButtonText.text = $"✚ Use Potion ({_battleEngine.PotionCount})";

            _nextWaveButton.interactable = _battleEngine.IsBattleOver;
            _nextWaveButtonText.text = player.IsAlive
                ? $"Next Wave ({_battleEngine.CurrentWave + 1}) ▶"
                : "Retry from Wave 1 ↻";

            // Display latest 3 log lines for clean readability
            var logs = _battleEngine.BattleLogs;
            int startIdx = Mathf.Max(0, logs.Count - 3);
            var recentLogs = new List<string>();
            for (int i = startIdx; i < logs.Count; i++)
            {
                recentLogs.Add(logs[i]);
            }
            _battleLogText.text = string.Join("\n", recentLogs);
        }

        private void HandlePlayerStatsUpdated(PlayerStatsSnapshot s)
        {
            if (s == null || _playerStatsTableLeftText == null) return;

            _authStatusText.text =
                $"<b>Auth:</b> {_statsService.AuthStatusText}   |   " +
                $"<b>Updated:</b> {s.FetchedAtTimestamp} ({s.StatusMessage})";

            _segmentSummaryText.text = $"<b>Player Segment:</b> {s.GetPlayerSegmentSummary()}";

            string FormatInt(int val, string unit = "") =>
                PlayerStatsSnapshot.IsStatAvailable(val) ? $"{val:N0}{unit}" : "<color=#636E72>N/A (-1)</color>";

            string FormatFloat(float val, string format = "F1", string unit = "") =>
                PlayerStatsSnapshot.IsStatAvailable(val) ? $"{val.ToString(format)}{unit}" : "<color=#636E72>N/A (-1)</color>";

            string FormatPercent(float val) =>
                PlayerStatsSnapshot.IsStatAvailable(val) ? $"{val * 100f:F0}% ({val:F2})" : "<color=#636E72>N/A (-1)</color>";

            string churnColor = s.ChurnProbability >= 0.5f ? "#FF7675" : "#55EFC4";

            _playerStatsTableLeftText.text =
                $"<b>1. Sessions:</b> <color=#74B9FF>{FormatInt(s.NumberOfSessions)}</color>\n" +
                $"<b>2. Avg Session:</b> <color=#74B9FF>{FormatFloat(s.AvgSessionLength, "F1", "m")}</color>\n" +
                $"<b>3. Days Since Last:</b> <color={churnColor}>{FormatInt(s.DaysSinceLastPlayed, "d")}</color>\n" +
                $"<b>4. Purchases:</b> <color=#FD79A8>{FormatInt(s.NumberOfPurchases)}</color>\n" +
                $"<b>5. Session Pct:</b> <color=#55EFC4>{FormatPercent(s.SessPercentile)}</color>";

            _playerStatsTableRightText.text =
                $"<b>6. Spend Pct:</b> <color=#FD79A8>{FormatPercent(s.SpendPercentile)}</color>\n" +
                $"<b>7. Spend Prob:</b> <color=#FFEAA7>{FormatPercent(s.SpendProbability)}</color>\n" +
                $"<b>8. Churn Prob:</b> <color={churnColor}>{FormatPercent(s.ChurnProbability)}</color>\n" +
                $"<b>9. High Spender:</b> <color=#FD79A8>{FormatPercent(s.HighSpenderProbability)}</color>\n" +
                $"<b>10. 28d Forecast:</b> <color=#FFEAA7>${FormatFloat(s.TotalSpendNext28Days, "F2")}</color>";
        }

        private void HandleBattleEventsUpdated(IReadOnlyDictionary<BattleStatEventType, BattleEventStatItem> events)
        {
            if (events == null || _battleEventsTableText == null) return;

            var lines = new List<string>();
            foreach (var kvp in events)
            {
                BattleEventStatItem item = kvp.Value;
                string shortTitle = item.DisplayName;
                int parenIdx = shortTitle.IndexOf(" (");
                if (parenIdx > 0) shortTitle = shortTitle.Substring(0, parenIdx);

                string eventName = item.SourceEventAndProp;
                int dotIdx = eventName.IndexOf('.');
                if (dotIdx > 0) eventName = eventName.Substring(0, dotIdx);

                lines.Add(
                    $"• <b>{shortTitle}</b> <color=#8395A7>[{eventName} {item.AggregationType}]</color> :  " +
                    $"<color=#55EFC4><b>{item.CurrentCount:N0}</b></color>");
            }

            _battleEventsTableText.text = string.Join("\n", lines);
        }

        private void HandleApiLogMessage(string msg)
        {
            _apiTraceLogs.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
            while (_apiTraceLogs.Count > 3)
            {
                _apiTraceLogs.RemoveAt(0);
            }

            if (_apiTraceLogText != null)
            {
                _apiTraceLogText.text = string.Join("\n", _apiTraceLogs);
            }
        }

        private void SwitchStatsTab(bool showGameStats)
        {
            if (_gameStatsTabRoot != null) _gameStatsTabRoot.gameObject.SetActive(showGameStats);
            if (_playerStatsTabRoot != null) _playerStatsTabRoot.gameObject.SetActive(!showGameStats);

            Color activeBg = new Color(0.16f, 0.56f, 0.68f, 1f);
            Color inactiveBg = new Color(0.16f, 0.20f, 0.26f, 1f);

            if (_gameStatsTabBtnBg != null) _gameStatsTabBtnBg.color = showGameStats ? activeBg : inactiveBg;
            if (_playerStatsTabBtnBg != null) _playerStatsTabBtnBg.color = !showGameStats ? activeBg : inactiveBg;

            if (_gameStatsTabBtnText != null)
                _gameStatsTabBtnText.color = showGameStats ? Color.white : new Color(0.70f, 0.78f, 0.86f);
            if (_playerStatsTabBtnText != null)
                _playerStatsTabBtnText.color = !showGameStats ? Color.white : new Color(0.70f, 0.78f, 0.86f);
        }

        // =====================================================================
        // Runtime UI Generation (Portrait 1080x1920 Reference)
        // =====================================================================

        private void BuildDashboardUI()
        {
            // Create Canvas
            var canvasGo = new GameObject("TrivialStadium_Canvas");
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.25f;

            canvasGo.AddComponent<GraphicRaycaster>();

            // Full-screen background panel
            RectTransform rootBg = CreatePanel(canvasGo.transform, "Background", new Color(0.07f, 0.09f, 0.13f, 1f));
            StretchFull(rootBg);

            // SafeArea container (avoids camera cutout & bottom gesture bar)
            var safeGo = new GameObject("SafeAreaRoot");
            safeGo.transform.SetParent(rootBg, false);
            _safeAreaRect = safeGo.AddComponent<RectTransform>();
            StretchFull(_safeAreaRect);

            // Top Half: Battle Arena (0.440 ~ 0.992)
            RectTransform topArena = CreatePanel(_safeAreaRect, "BattleArenaSection", new Color(0.11f, 0.14f, 0.19f, 1f));
            SetAnchors(topArena, new Vector2(0.018f, 0.440f), new Vector2(0.982f, 0.992f));

            // Bottom Half: GPGS Stats & Events Dashboard (0.008 ~ 0.430)
            RectTransform bottomStats = CreatePanel(_safeAreaRect, "GpgsStatsSection", new Color(0.11f, 0.14f, 0.19f, 1f));
            SetAnchors(bottomStats, new Vector2(0.018f, 0.008f), new Vector2(0.982f, 0.430f));

            BuildTopBattleArena(topArena);
            BuildBottomStatsDashboard(bottomStats);
            SwitchStatsTab(showGameStats: true);
        }

        private void BuildTopBattleArena(RectTransform parent)
        {
            // 1. Header (0.865 ~ 0.985)
            RectTransform headerBox = CreatePanel(parent, "Header", new Color(0.16f, 0.21f, 0.29f, 1f));
            SetAnchors(headerBox, new Vector2(0.02f, 0.865f), new Vector2(0.98f, 0.985f));

            _waveHeaderText = CreateText(
                headerBox, "WaveTitle", "TRIVIAL STADIUM  —  WAVE 1",
                40, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white,
                new Vector2(0.025f, 0.46f), new Vector2(0.975f, 0.95f),
                bestFit: true, minSize: 24, maxSize: 42);

            _perkBannerText = CreateText(
                headerBox, "PerkBanner", "Active Perk: -",
                26, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.33f, 0.94f, 0.77f),
                new Vector2(0.025f, 0.06f), new Vector2(0.975f, 0.46f),
                bestFit: true, minSize: 18, maxSize: 28);

            // 2. Enemy Fighter Card (0.665 ~ 0.850)
            RectTransform enemyCard = CreatePanel(parent, "EnemyCard", new Color(0.19f, 0.14f, 0.18f, 1f));
            SetAnchors(enemyCard, new Vector2(0.02f, 0.665f), new Vector2(0.98f, 0.850f));
            BuildFighterCard(
                enemyCard,
                "ENEMY",
                new Color(0.92f, 0.30f, 0.35f),
                out _enemyNameText,
                out _enemyStatsSubText,
                out _enemyHpFill,
                out _enemyHpText);

            // 3. Player Fighter Card (0.465 ~ 0.650)
            RectTransform playerCard = CreatePanel(parent, "PlayerCard", new Color(0.14f, 0.19f, 0.25f, 1f));
            SetAnchors(playerCard, new Vector2(0.02f, 0.465f), new Vector2(0.98f, 0.650f));
            BuildFighterCard(
                playerCard,
                "PLAYER",
                new Color(0.18f, 0.80f, 0.44f),
                out _playerNameText,
                out _playerStatsSubText,
                out _playerHpFill,
                out _playerHpText);

            // 4. Command Buttons 2x2 Grid (0.220 ~ 0.450)
            RectTransform actionArea = CreatePanel(parent, "ActionButtons", new Color(0f, 0f, 0f, 0f));
            SetAnchors(actionArea, new Vector2(0.02f, 0.220f), new Vector2(0.98f, 0.450f));

            _attackButton = CreateButton(
                actionArea, "AttackBtn", "⚔ Normal Attack",
                new Color(0.20f, 0.52f, 0.85f),
                new Vector2(0.00f, 0.53f), new Vector2(0.49f, 1.00f),
                32, () => _battleEngine.ExecuteNormalAttack(), out _);

            _heavyStrikeButton = CreateButton(
                actionArea, "HeavyBtn", "★ Heavy Strike (Skill)",
                new Color(0.82f, 0.45f, 0.16f),
                new Vector2(0.51f, 0.53f), new Vector2(1.00f, 1.00f),
                32, () => _battleEngine.ExecuteHeavyStrike(), out _heavyStrikeButtonText);

            _potionButton = CreateButton(
                actionArea, "PotionBtn", "✚ Use Potion",
                new Color(0.16f, 0.64f, 0.45f),
                new Vector2(0.00f, 0.00f), new Vector2(0.49f, 0.47f),
                32, () => _battleEngine.ExecuteUsePotion(), out _potionButtonText);

            _nextWaveButton = CreateButton(
                actionArea, "NextWaveBtn", "Next Wave ▶",
                new Color(0.48f, 0.35f, 0.78f),
                new Vector2(0.51f, 0.00f), new Vector2(1.00f, 0.47f),
                32, () => _battleEngine.AdvanceToNextWave(), out _nextWaveButtonText);

            // 5. Battle Log (0.015 ~ 0.205)
            RectTransform logBox = CreatePanel(parent, "BattleLogBox", new Color(0.07f, 0.09f, 0.12f, 1f));
            SetAnchors(logBox, new Vector2(0.02f, 0.015f), new Vector2(0.98f, 0.205f));

            CreateText(
                logBox, "LogHeader", "■ BATTLE LOG (Real-Time Combat & GPGS Events)",
                22, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.65f, 0.76f, 0.86f),
                new Vector2(0.02f, 0.73f), new Vector2(0.98f, 0.96f),
                bestFit: true, minSize: 16, maxSize: 24);

            _battleLogText = CreateText(
                logBox, "BattleLogContent", "",
                22, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.92f, 0.95f, 0.98f),
                new Vector2(0.02f, 0.04f), new Vector2(0.98f, 0.71f),
                bestFit: false);
            _battleLogText.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private void BuildFighterCard(
            RectTransform card,
            string badgeLabel,
            Color barColor,
            out Text nameText,
            out Text subText,
            out Image hpFill,
            out Text hpText)
        {
            nameText = CreateText(
                card, "FighterName", "-",
                34, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white,
                new Vector2(0.025f, 0.64f), new Vector2(0.80f, 0.96f),
                bestFit: true, minSize: 20, maxSize: 36);

            CreateText(
                card, "Badge", badgeLabel,
                22, FontStyle.Bold, TextAnchor.MiddleRight, new Color(0.68f, 0.76f, 0.85f),
                new Vector2(0.81f, 0.64f), new Vector2(0.975f, 0.96f),
                bestFit: true, minSize: 16, maxSize: 24);

            subText = CreateText(
                card, "FighterSub", "-",
                26, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.82f, 0.88f, 0.94f),
                new Vector2(0.025f, 0.39f), new Vector2(0.975f, 0.64f),
                bestFit: true, minSize: 18, maxSize: 28);

            // HP Bar Background
            RectTransform hpBg = CreatePanel(card, "HpBarBg", new Color(0.07f, 0.08f, 0.11f, 1f));
            SetAnchors(hpBg, new Vector2(0.025f, 0.08f), new Vector2(0.975f, 0.37f));

            // HP Bar Fill
            var fillGo = new GameObject("HpBarFill");
            fillGo.transform.SetParent(hpBg, false);
            hpFill = fillGo.AddComponent<Image>();
            hpFill.sprite = CreateSolidSprite();
            hpFill.type = Image.Type.Filled;
            hpFill.fillMethod = Image.FillMethod.Horizontal;
            hpFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            hpFill.fillAmount = 1f;
            hpFill.color = barColor;
            StretchFull(hpFill.rectTransform);

            hpText = CreateText(
                hpBg, "HpValueText", "HP 100 / 100",
                28, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white,
                Vector2.zero, Vector2.one,
                bestFit: true, minSize: 18, maxSize: 30);
        }

        private void BuildBottomStatsDashboard(RectTransform parent)
        {
            // 1. Tab Bar + Refresh Button (0.855 ~ 0.980)
            Button gameStatsTabBtn = CreateButton(
                parent, "TabGameStatsBtn", "📊 Game Stats (5)",
                new Color(0.16f, 0.56f, 0.68f),
                new Vector2(0.02f, 0.855f), new Vector2(0.42f, 0.980f),
                28, () => SwitchStatsTab(showGameStats: true), out _gameStatsTabBtnText);
            _gameStatsTabBtnBg = gameStatsTabBtn.GetComponent<Image>();

            Button playerStatsTabBtn = CreateButton(
                parent, "TabPlayerStatsBtn", "👤 PlayerStats (10)",
                new Color(0.16f, 0.20f, 0.26f),
                new Vector2(0.43f, 0.855f), new Vector2(0.81f, 0.980f),
                28, () => SwitchStatsTab(showGameStats: false), out _playerStatsTabBtnText);
            _playerStatsTabBtnBg = playerStatsTabBtn.GetComponent<Image>();

            CreateButton(
                parent, "RefreshStatsBtn", "↻ Refresh",
                new Color(0.18f, 0.62f, 0.50f),
                new Vector2(0.82f, 0.855f), new Vector2(0.98f, 0.980f),
                26, () =>
                {
                    _statsService.FetchPlayerStats();
                    _statsService.FetchAllBattleEvents();
                }, out _);

            // 2. Auth Status Row (0.765 ~ 0.845)
            RectTransform authBar = CreatePanel(parent, "AuthStatusBar", new Color(0.14f, 0.19f, 0.24f, 1f));
            SetAnchors(authBar, new Vector2(0.02f, 0.765f), new Vector2(0.98f, 0.845f));

            _authStatusText = CreateText(
                authBar, "AuthStatus", "Auth: Initializing...",
                22, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.88f, 0.93f, 0.97f),
                new Vector2(0.02f, 0.05f), new Vector2(0.98f, 0.95f),
                bestFit: true, minSize: 15, maxSize: 24);

            // 3A. Tab 1: Game Stats (5) + API Trace Log (0.015 ~ 0.750)
            var tab1Go = new GameObject("GameStatsTabRoot");
            tab1Go.transform.SetParent(parent, false);
            _gameStatsTabRoot = tab1Go.AddComponent<RectTransform>();
            SetAnchors(_gameStatsTabRoot, new Vector2(0.02f, 0.015f), new Vector2(0.98f, 0.750f));

            RectTransform eventsBox = CreatePanel(_gameStatsTabRoot, "EventsCard", new Color(0.08f, 0.11f, 0.15f, 1f));
            SetAnchors(eventsBox, new Vector2(0.00f, 0.35f), new Vector2(1.00f, 1.00f));

            CreateText(
                eventsBox, "EvHeader",
                "■ Play Console Game Stats (GameStatsClient — 5 Metrics)",
                24, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.33f, 0.94f, 0.77f),
                new Vector2(0.025f, 0.81f), new Vector2(0.975f, 0.97f),
                bestFit: true, minSize: 17, maxSize: 26);

            _battleEventsTableText = CreateText(
                eventsBox, "EvTable", "",
                28, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.94f, 0.96f, 0.99f),
                new Vector2(0.025f, 0.04f), new Vector2(0.975f, 0.79f),
                bestFit: true, minSize: 20, maxSize: 30);
            _battleEventsTableText.lineSpacing = 1.12f;

            RectTransform apiLogBox = CreatePanel(_gameStatsTabRoot, "ApiTraceBox", new Color(0.07f, 0.08f, 0.11f, 1f));
            SetAnchors(apiLogBox, new Vector2(0.00f, 0.00f), new Vector2(1.00f, 0.33f));

            CreateText(
                apiLogBox, "ApiLogHeader", "■ GPGS API CALL TRACE LOG",
                20, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.65f, 0.76f, 0.86f),
                new Vector2(0.02f, 0.71f), new Vector2(0.98f, 0.96f),
                bestFit: true, minSize: 15, maxSize: 22);

            _apiTraceLogText = CreateText(
                apiLogBox, "ApiLogContent", "",
                19, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.82f, 0.89f, 0.95f),
                new Vector2(0.02f, 0.04f), new Vector2(0.98f, 0.69f),
                bestFit: false);
            _apiTraceLogText.verticalOverflow = VerticalWrapMode.Truncate;

            // 3B. Tab 2: PlayerStats (10) + Simulation Controls (0.015 ~ 0.750)
            var tab2Go = new GameObject("PlayerStatsTabRoot");
            tab2Go.transform.SetParent(parent, false);
            _playerStatsTabRoot = tab2Go.AddComponent<RectTransform>();
            SetAnchors(_playerStatsTabRoot, new Vector2(0.02f, 0.015f), new Vector2(0.98f, 0.750f));

            RectTransform simBar = CreatePanel(_playerStatsTabRoot, "SimControlsBar", new Color(0f, 0f, 0f, 0f));
            SetAnchors(simBar, new Vector2(0.00f, 0.82f), new Vector2(1.00f, 1.00f));

            CreateButton(
                simBar, "SimSessionBtn", "+1 Session",
                new Color(0.24f, 0.45f, 0.72f),
                new Vector2(0.00f, 0.0f), new Vector2(0.24f, 1.0f),
                24, () => _statsService.SimulateNewSession(), out _);

            CreateButton(
                simBar, "SimChurnBtn", "+7d Absent",
                new Color(0.75f, 0.34f, 0.34f),
                new Vector2(0.255f, 0.0f), new Vector2(0.505f, 1.0f),
                24, () => _statsService.SimulateDaysAbsent(7), out _);

            CreateButton(
                simBar, "SimPurchaseBtn", "Buy $4.99 Pack",
                new Color(0.75f, 0.35f, 0.60f),
                new Vector2(0.52f, 0.0f), new Vector2(0.77f, 1.0f),
                24, () => _battleEngine.PurchaseSupplyPack(), out _);

            CreateButton(
                simBar, "ResetSimBtn", "Reset Sim",
                new Color(0.35f, 0.40f, 0.46f),
                new Vector2(0.785f, 0.0f), new Vector2(1.00f, 1.0f),
                24, () => _statsService.ResetMockStats(), out _);

            RectTransform playerStatsBox = CreatePanel(_playerStatsTabRoot, "PlayerStatsCard", new Color(0.08f, 0.11f, 0.15f, 1f));
            SetAnchors(playerStatsBox, new Vector2(0.00f, 0.00f), new Vector2(1.00f, 0.79f));

            _segmentSummaryText = CreateText(
                playerStatsBox, "SegmentSummary", "Player Segment: -",
                23, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white,
                new Vector2(0.025f, 0.81f), new Vector2(0.975f, 0.97f),
                bestFit: true, minSize: 16, maxSize: 25);

            _playerStatsTableLeftText = CreateText(
                playerStatsBox, "PsTableLeft", "",
                25, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.90f, 0.93f, 0.96f),
                new Vector2(0.025f, 0.04f), new Vector2(0.49f, 0.78f),
                bestFit: true, minSize: 18, maxSize: 27);
            _playerStatsTableLeftText.lineSpacing = 1.18f;

            _playerStatsTableRightText = CreateText(
                playerStatsBox, "PsTableRight", "",
                25, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.90f, 0.93f, 0.96f),
                new Vector2(0.51f, 0.04f), new Vector2(0.975f, 0.78f),
                bestFit: true, minSize: 18, maxSize: 27);
            _playerStatsTableRightText.lineSpacing = 1.18f;
        }

        // =====================================================================
        // uGUI Builder Utilities
        // =====================================================================

        private void EnsureEventSystem()
        {
#if UNITY_2022_2_OR_NEWER
            if (FindFirstObjectByType<EventSystem>() != null) return;
#else
            if (FindObjectOfType<EventSystem>() != null) return;
#endif

            var esGo = new GameObject("EventSystem");
            esGo.transform.SetParent(transform, false);
            esGo.AddComponent<EventSystem>();

            Type newInputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (newInputModuleType != null)
            {
                esGo.AddComponent(newInputModuleType);
            }
            else
            {
                esGo.AddComponent<StandaloneInputModule>();
            }
        }

        private static Font LoadCompatibleFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            if (font == null)
            {
                font = Font.CreateDynamicFontFromOSFont("Arial", 24);
            }
            return font;
        }

        private static Sprite _cachedWhiteSprite;
        private static Sprite CreateSolidSprite()
        {
            if (_cachedWhiteSprite != null) return _cachedWhiteSprite;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            tex.Apply();
            _cachedWhiteSprite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
            return _cachedWhiteSprite;
        }

        private static RectTransform CreatePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.sprite = CreateSolidSprite();
            img.color = color;
            return rect;
        }

        private Text CreateText(
            Transform parent,
            string name,
            string content,
            int fontSize,
            FontStyle style,
            TextAnchor alignment,
            Color color,
            Vector2 anchorMin,
            Vector2 anchorMax,
            bool bestFit = false,
            int minSize = 14,
            int maxSize = 32)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            SetAnchors(rect, anchorMin, anchorMax);

            var txt = go.AddComponent<Text>();
            txt.font = _uiFont;
            txt.text = content;
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.alignment = alignment;
            txt.color = color;
            txt.supportRichText = true;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            if (bestFit)
            {
                txt.resizeTextForBestFit = true;
                txt.resizeTextMinSize = minSize;
                txt.resizeTextMaxSize = maxSize;
            }
            return txt;
        }

        private Button CreateButton(
            Transform parent,
            string name,
            string label,
            Color bgColor,
            Vector2 anchorMin,
            Vector2 anchorMax,
            int fontSize,
            UnityEngine.Events.UnityAction onClick,
            out Text labelText)
        {
            RectTransform btnRect = CreatePanel(parent, name, bgColor);
            SetAnchors(btnRect, anchorMin, anchorMax);

            var btn = btnRect.gameObject.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            cb.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            btn.colors = cb;
            btn.onClick.AddListener(onClick);

            labelText = CreateText(
                btnRect, "Label", label,
                fontSize, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.92f),
                bestFit: true, minSize: 15, maxSize: fontSize);
            return btn;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}

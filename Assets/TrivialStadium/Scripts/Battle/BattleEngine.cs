using System;
using System.Collections.Generic;
using TrivialStadium.Stats;
using UnityEngine;

namespace TrivialStadium.Battle
{
    [Serializable]
    public class FighterState
    {
        public string Name;
        public string Title;
        public int Level;
        public int MaxHp;
        public int CurrentHp;
        public int BaseAttack;
        public float CritChance;
        public Color ThemeColor;

        public bool IsAlive => CurrentHp > 0;
        public float HpRatio => MaxHp > 0 ? Mathf.Clamp01((float)CurrentHp / MaxHp) : 0f;
    }

    /// <summary>
    /// Turn-based arena battle engine for "Trivial Stadium".
    /// - Records battle actions (damage, critical hits, monster defeats, potion usage) via GPGS GameStatsClient.
    /// - Dynamically applies gameplay buffs based on GPGS PlayerStats metrics (churn risk, session count, etc.).
    /// </summary>
    public class BattleEngine : MonoBehaviour
    {
        public FighterState Player { get; private set; } = new FighterState();
        public FighterState Enemy { get; private set; } = new FighterState();

        public int CurrentWave { get; private set; } = 1;
        public int PotionCount { get; private set; } = 3;
        public int HeavyStrikeCooldown { get; private set; } = 0;
        public bool IsBattleOver { get; private set; }

        // PlayerStats-driven buff state
        public bool HasComebackBuff { get; private set; }
        public bool HasVeteranBuff { get; private set; }
        public string ActivePerkDescription { get; private set; } = "";

        private readonly List<string> _battleLogs = new List<string>();
        public IReadOnlyList<string> BattleLogs => _battleLogs;

        public event Action OnBattleStateChanged;

        private struct EnemyTemplate
        {
            public string Name;
            public string Title;
            public int BaseHp;
            public int BaseAtk;
            public Color Color;
        }

        private static readonly EnemyTemplate[] EnemyRoster =
        {
            new EnemyTemplate { Name = "Slime Gladiator", Title = "Vanguard Ooze Fighter", BaseHp = 95, BaseAtk = 14, Color = new Color(0.25f, 0.78f, 0.55f) },
            new EnemyTemplate { Name = "Iron Minotaur", Title = "Armored Labyrinth Bull", BaseHp = 135, BaseAtk = 19, Color = new Color(0.90f, 0.55f, 0.22f) },
            new EnemyTemplate { Name = "Crimson Wyvern", Title = "Flame Drake", BaseHp = 175, BaseAtk = 24, Color = new Color(0.92f, 0.30f, 0.35f) },
            new EnemyTemplate { Name = "Abyss Lord", Title = "Overlord of the Void (BOSS)", BaseHp = 240, BaseAtk = 30, Color = new Color(0.62f, 0.40f, 0.95f) }
        };

        private void Start()
        {
            if (GpgsStatsService.Instance != null)
            {
                GpgsStatsService.Instance.OnPlayerStatsUpdated += OnPlayerStatsUpdated;
            }

            StartNewBattle(resetWave: true);
        }

        private void OnDestroy()
        {
            if (GpgsStatsService.Instance != null)
            {
                GpgsStatsService.Instance.OnPlayerStatsUpdated -= OnPlayerStatsUpdated;
            }
        }

        private void OnPlayerStatsUpdated(PlayerStatsSnapshot stats)
        {
            EvaluatePlayerStatsPerks(stats, logChange: false);
            OnBattleStateChanged?.Invoke();
        }

        /// <summary>
        /// Evaluates dynamic player buffs based on GPGS PlayerStats metrics.
        /// </summary>
        private void EvaluatePlayerStatsPerks(PlayerStatsSnapshot stats, bool logChange)
        {
            if (stats == null) return;

            bool prevComeback = HasComebackBuff;
            bool prevVeteran = HasVeteranBuff;

            HasComebackBuff = stats.IsHighChurnRisk;
            HasVeteranBuff = stats.IsVeteranPlayer;

            var perks = new List<string>();
            if (HasComebackBuff)
            {
                perks.Add("Comeback Buff (ATK +30% / DMG Taken -20%)");
            }
            if (HasVeteranBuff)
            {
                perks.Add("Veteran Bonus (CRIT +15%)");
            }

            ActivePerkDescription = perks.Count > 0
                ? string.Join(" & ", perks)
                : "Normal State (No Special Buffs)";

            if (logChange && (prevComeback != HasComebackBuff || prevVeteran != HasVeteranBuff))
            {
                AppendLog($"<color=#FFEAA7>[Stats Perk] {ActivePerkDescription}</color>");
            }
        }

        /// <summary>
        /// Starts a new battle or advances to the next wave.
        /// </summary>
        public void StartNewBattle(bool resetWave)
        {
            if (resetWave)
            {
                CurrentWave = 1;
                PotionCount = 3;
            }

            var stats = GpgsStatsService.Instance != null
                ? GpgsStatsService.Instance.CurrentPlayerStats
                : null;
            EvaluatePlayerStatsPerks(stats, logChange: false);

            int playerLevel = CurrentWave;
            Player = new FighterState
            {
                Name = "Hero Gladiator",
                Title = $"Arena Rank {CurrentWave}",
                Level = playerLevel,
                MaxHp = 140 + (playerLevel - 1) * 20,
                CurrentHp = 140 + (playerLevel - 1) * 20,
                BaseAttack = 24 + (playerLevel - 1) * 4,
                CritChance = 0.20f,
                ThemeColor = new Color(0.25f, 0.62f, 0.96f)
            };

            if (HasComebackBuff && resetWave)
            {
                PotionCount += 1;
            }

            EnemyTemplate tpl = EnemyRoster[(CurrentWave - 1) % EnemyRoster.Length];
            int loopTier = (CurrentWave - 1) / EnemyRoster.Length;
            float scale = 1f + loopTier * 0.35f;

            Enemy = new FighterState
            {
                Name = tpl.Name,
                Title = $"Wave {CurrentWave} - {tpl.Title}",
                Level = CurrentWave,
                MaxHp = Mathf.RoundToInt(tpl.BaseHp * scale),
                CurrentHp = Mathf.RoundToInt(tpl.BaseHp * scale),
                BaseAttack = Mathf.RoundToInt(tpl.BaseAtk * scale),
                CritChance = 0.10f,
                ThemeColor = tpl.Color
            };

            HeavyStrikeCooldown = 0;
            IsBattleOver = false;

            AppendLog($"=== Wave {CurrentWave}: <color=#FF7675>{Enemy.Name}</color> appeared! ===");
            if (HasComebackBuff || HasVeteranBuff)
            {
                AppendLog($"<color=#55EFC4>★ PlayerStats Perk Active: {ActivePerkDescription}</color>");
            }

            // GPGS Game Stats Event: battle_started (wave_number, enemy_name)
            if (GpgsStatsService.Instance != null)
            {
                GpgsStatsService.Instance.RecordBattleStartedEvent(CurrentWave, Enemy.Name);
            }

            OnBattleStateChanged?.Invoke();
        }

        /// <summary>
        /// Player action: Normal Attack
        /// </summary>
        public void ExecuteNormalAttack()
        {
            if (IsBattleOver) return;
            PerformPlayerStrike(damageMultiplier: 1.0f, bonusCritChance: 0f, skillName: "Normal Attack");
        }

        /// <summary>
        /// Player action: Heavy Strike (High damage + bonus crit chance, 2-turn cooldown)
        /// </summary>
        public void ExecuteHeavyStrike()
        {
            if (IsBattleOver) return;
            if (HeavyStrikeCooldown > 0)
            {
                AppendLog($"<color=#FDCB6E>Heavy Strike is on cooldown for {HeavyStrikeCooldown} more turn(s).</color>");
                OnBattleStateChanged?.Invoke();
                return;
            }

            HeavyStrikeCooldown = 2;
            PerformPlayerStrike(damageMultiplier: 1.65f, bonusCritChance: 0.25f, skillName: "Brave Slash");
        }

        /// <summary>
        /// Player action: Use healing potion
        /// </summary>
        public void ExecuteUsePotion()
        {
            if (IsBattleOver) return;
            if (PotionCount <= 0)
            {
                AppendLog("<color=#FF7675>No potions remaining! (Refill via the Supply Pack simulation button)</color>");
                OnBattleStateChanged?.Invoke();
                return;
            }

            PotionCount--;
            int healAmount = Mathf.RoundToInt(Player.MaxHp * 0.45f);
            int beforeHp = Player.CurrentHp;
            Player.CurrentHp = Mathf.Min(Player.MaxHp, Player.CurrentHp + healAmount);
            int actualHealed = Player.CurrentHp - beforeHp;

            AppendLog($"<color=#55EFC4>Used a Potion! Recovered {actualHealed} HP ({PotionCount} left)</color>");

            if (GpgsStatsService.Instance != null)
            {
                GpgsStatsService.Instance.RecordPotionUsedEvent(actualHealed, PotionCount);
            }

            if (HeavyStrikeCooldown > 0) HeavyStrikeCooldown--;
            ExecuteEnemyTurn();
            OnBattleStateChanged?.Invoke();
        }

        /// <summary>
        /// Shop purchase simulation: Refills potions + full heal + updates GPGS spend metrics
        /// </summary>
        public void PurchaseSupplyPack()
        {
            PotionCount += 3;
            Player.CurrentHp = Player.MaxHp;
            AppendLog("<color=#FD79A8>[Shop] Purchased Gladiator Supply Pack! Potions +3 & Full HP restored!</color>");

            if (GpgsStatsService.Instance != null)
            {
                GpgsStatsService.Instance.SimulateInAppPurchase(4.99f);
            }

            OnBattleStateChanged?.Invoke();
        }

        private void PerformPlayerStrike(float damageMultiplier, float bonusCritChance, string skillName)
        {
            float atkMultiplier = HasComebackBuff ? 1.30f : 1.0f;
            float totalCritChance = Player.CritChance + bonusCritChance + (HasVeteranBuff ? 0.15f : 0f);

            bool isCrit = UnityEngine.Random.value < totalCritChance;
            float randomVariance = UnityEngine.Random.Range(0.90f, 1.12f);
            int rawDamage = Mathf.Max(1, Mathf.RoundToInt(
                Player.BaseAttack * damageMultiplier * atkMultiplier * randomVariance * (isCrit ? 1.75f : 1.0f)));

            Enemy.CurrentHp = Mathf.Max(0, Enemy.CurrentHp - rawDamage);

            if (isCrit)
            {
                AppendLog($"<color=#FFEAA7>★CRITICAL!★</color> {Player.Name}'s [{skillName}] dealt <b>{rawDamage}</b> massive damage to {Enemy.Name}!");
            }
            else
            {
                AppendLog($"{Player.Name}'s [{skillName}] dealt <b>{rawDamage}</b> damage to {Enemy.Name}!");
            }

            // GPGS Game Stats Event: damage_dealt & critical_hit_landed
            if (GpgsStatsService.Instance != null)
            {
                GpgsStatsService.Instance.RecordDamageDealtEvent(rawDamage, skillName, isCrit);
            }

            if (HeavyStrikeCooldown > 0 && skillName == "Normal Attack")
            {
                HeavyStrikeCooldown--;
            }

            if (!Enemy.IsAlive)
            {
                HandleVictory();
            }
            else
            {
                ExecuteEnemyTurn();
            }

            OnBattleStateChanged?.Invoke();
        }

        private void ExecuteEnemyTurn()
        {
            if (IsBattleOver || !Enemy.IsAlive) return;

            bool enemyCrit = UnityEngine.Random.value < Enemy.CritChance;
            float defenseFactor = HasComebackBuff ? 0.80f : 1.0f;
            float variance = UnityEngine.Random.Range(0.88f, 1.12f);

            int enemyDamage = Mathf.Max(1, Mathf.RoundToInt(
                Enemy.BaseAttack * defenseFactor * variance * (enemyCrit ? 1.5f : 1.0f)));

            Player.CurrentHp = Mathf.Max(0, Player.CurrentHp - enemyDamage);

            string critTag = enemyCrit ? "<color=#FF7675>[CRIT]</color> " : "";
            AppendLog($"{critTag}{Enemy.Name} counterattacked! {Player.Name} took <b>{enemyDamage}</b> damage!");

            if (!Player.IsAlive)
            {
                HandleDefeat();
            }
        }

        private void HandleVictory()
        {
            IsBattleOver = true;
            PotionCount += 1;
            AppendLog($"<color=#55EFC4>★ VICTORY! ★ Defeated {Enemy.Name}! (Loot: Potion +1)</color>");

            if (GpgsStatsService.Instance != null)
            {
                GpgsStatsService.Instance.RecordBattleWonEvent(CurrentWave, Player.CurrentHp, Enemy.Name);
                // Refresh PlayerStats after battle ends
                GpgsStatsService.Instance.FetchPlayerStats();
            }
        }

        private void HandleDefeat()
        {
            IsBattleOver = true;
            AppendLog($"<color=#FF7675>× DEFEAT... × {Player.Name} fell in battle. Tap 'Retry from Wave 1' to continue.</color>");

            if (GpgsStatsService.Instance != null)
            {
                GpgsStatsService.Instance.FetchPlayerStats();
            }
        }

        public void AdvanceToNextWave()
        {
            if (!IsBattleOver) return;

            if (Player.IsAlive)
            {
                CurrentWave++;
                StartNewBattle(resetWave: false);
            }
            else
            {
                StartNewBattle(resetWave: true);
            }
        }

        private void AppendLog(string message)
        {
            string timestamped = $"<color=#8395A7>[{DateTime.Now:HH:mm:ss}]</color> {message}";
            _battleLogs.Add(timestamped);
            while (_battleLogs.Count > 9)
            {
                _battleLogs.RemoveAt(0);
            }
        }
    }
}

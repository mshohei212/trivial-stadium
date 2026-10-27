using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrivialStadium.Stats
{
    /// <summary>
    /// Snapshot model holding all 10 metrics returned by the
    /// Google Play Games Services (GPGS) PlayerStats API (loadPlayerStats).
    /// </summary>
    [Serializable]
    public class PlayerStatsSnapshot
    {
        [Header("Source Info")]
        public bool IsValid = true;
        public bool IsFromRealGpgs;
        public string StatusMessage = "OK";
        public string FetchedAtTimestamp = "-";

        [Header("Google Play Games PlayerStats Metrics")]
        public int NumberOfSessions;
        public float AvgSessionLength;
        public int DaysSinceLastPlayed;
        public int NumberOfPurchases;
        public float SessPercentile;
        public float SpendPercentile;
        public float SpendProbability;
        public float ChurnProbability;
        public float HighSpenderProbability;
        public float TotalSpendNext28Days;

        public static bool IsStatAvailable(float value) => value >= 0f;
        public static bool IsStatAvailable(int value) => value >= 0;

        public bool IsHighChurnRisk =>
            (IsStatAvailable(ChurnProbability) && ChurnProbability >= 0.50f) ||
            (IsStatAvailable(DaysSinceLastPlayed) && DaysSinceLastPlayed >= 7);

        public bool IsVeteranPlayer =>
            (IsStatAvailable(NumberOfSessions) && NumberOfSessions >= 10) ||
            (IsStatAvailable(SessPercentile) && SessPercentile >= 0.70f);

        public string GetPlayerSegmentSummary()
        {
            var tags = new List<string>();

            if (IsHighChurnRisk)
            {
                tags.Add("<color=#FF7675>[High Churn Risk: Comeback Buff Active]</color>");
            }
            else
            {
                tags.Add("<color=#55EFC4>[Active Player]</color>");
            }

            if (IsVeteranPlayer)
            {
                tags.Add("<color=#74B9FF>[Veteran Gladiator: Crit Bonus Unlocked]</color>");
            }
            else
            {
                tags.Add("<color=#FFEAA7>[Rookie Gladiator]</color>");
            }

            if (IsStatAvailable(NumberOfPurchases) && NumberOfPurchases > 0)
            {
                tags.Add($"<color=#FD79A8>[Supporter ({NumberOfPurchases} Purchases)]</color>");
            }
            else if (IsStatAvailable(SpendProbability) && SpendProbability >= 0.4f)
            {
                tags.Add("<color=#FDCB6E>[Starter Pack Recommended]</color>");
            }

            return string.Join(" ", tags);
        }
    }

    /// <summary>
    /// The 5 Game Stats types defined in Play Console's RepetitiveStatsConfig.csv.
    /// </summary>
    public enum BattleStatEventType
    {
        MaxWaveReached,      // max_wave_reached     (battle_won.wave_number MAX)
        TotalDamageDealt,    // total_damage_dealt   (damage_dealt.damage_amount SUM)
        MaxCriticalDamage,   // max_critical_damage  (critical_hit_landed.damage_amount MAX)
        BestVictoryHp,       // best_victory_hp      (battle_won.remaining_hp MAX)
        TotalHealedHp        // total_healed_hp      (potion_used.healed_amount SUM)
    }

    /// <summary>
    /// Stat ID configuration matching RepetitiveStatsConfig.csv / Event Configuration in Play Console.
    /// </summary>
    [Serializable]
    public class GpgsEventIdConfig
    {
        public string MaxWaveReachedStatId = "max_wave_reached";
        public string TotalDamageDealtStatId = "total_damage_dealt";
        public string MaxCriticalDamageStatId = "max_critical_damage";
        public string BestVictoryHpStatId = "best_victory_hp";
        public string TotalHealedHpStatId = "total_healed_hp";

        public string GetEventId(BattleStatEventType type)
        {
            switch (type)
            {
                case BattleStatEventType.MaxWaveReached: return MaxWaveReachedStatId;
                case BattleStatEventType.TotalDamageDealt: return TotalDamageDealtStatId;
                case BattleStatEventType.MaxCriticalDamage: return MaxCriticalDamageStatId;
                case BattleStatEventType.BestVictoryHp: return BestVictoryHpStatId;
                case BattleStatEventType.TotalHealedHp: return TotalHealedHpStatId;
                default: return string.Empty;
            }
        }
    }

    /// <summary>
    /// Data model for a single Game Stat entry in RepetitiveStatsConfig.csv.
    /// </summary>
    [Serializable]
    public class BattleEventStatItem
    {
        public BattleStatEventType EventType;
        public string EventId;
        public string SourceEventAndProp;
        public string AggregationType;
        public string DisplayName;
        public string Description;
        public ulong CurrentCount;
    }
}

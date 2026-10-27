# Trivial Stadium — Unity Google Play Game Stats API Sample

A Unity (`6000.3.15f1`) sample project demonstrating **Google Play Games Services (GPGS) v2 SDK (`com.google.android.gms:play-services-games-v2:22.0.0`)** **Game Stats API (`GameStatsClient` / `PlayerGameEvent`)** and **PlayerStats API (`PlayerStatsClient.loadPlayerStats`)** integrated into a turn-based arena battle game.

No external GPGS Unity plugin installation is required. During Android builds, `GpgsAndroidBuildProcessor` automatically injects the Gradle dependency, `AndroidManifest.xml` metadata, `games-ids.xml`, and the native JNI bridge (`GpgsNativeBridge.java`).

---

## Directory Structure

```text
├── Assets/TrivialStadium/
│   ├── Scripts/
│   │   ├── Stats/
│   │   │   ├── GpgsStatsModels.cs         # Data models for Game Stats (5 stats) & PlayerStats (10 metrics)
│   │   │   └── GpgsStatsService.cs        # GPGS v2 sign-in, GameStatsClient event recording, PlayerStats fetching & Editor mock
│   │   ├── Battle/
│   │   │   └── BattleEngine.cs            # Turn-based battle logic (stats-driven dynamic buffs & GameStats event triggers)
│   │   └── UI/
│   │       └── TrivialStadiumUI.cs        # Runtime UI builder (Portrait-locked, SafeArea-aware, large mobile layout)
│   └── Editor/
│       ├── GpgsAndroidBuildProcessor.cs   # Automatically injects GPGS v2 SDK / Manifest / JNI bridge during Android builds
│       └── TrivialStadiumSetupMenu.cs     # Editor menu utilities (Scene setup, Project ID, Keystore signing, Version bump)
├── Keystores/
│   └── trivial_stadium_release.keystore   # Custom keystore for Release builds
└── PlayConsole/
    ├── gpgs_events_import.csv             # Play Console "Events configuration" import CSV (6 events)
    ├── gpgs_stats_import.zip              # Play Console "Game Stats Configuration" import ZIP (5 stats + icons)
    ├── stats_zip_contents/                # Unpacked contents of gpgs_stats_import.zip (RepetitiveStatsConfig.csv + 5 PNGs)
    └── store_assets/                      # Store listing assets for Google Play Console
```

---

## Quick Start (Running in Unity Editor)

In the Unity Editor, the sample runs out-of-the-box in mock simulation mode. When built for an Android device, it automatically calls the native GPGS v2 SDK.

1. From the Unity top menu, select:
   - **`Tools > Trivial Stadium (GPGS Stats) > 1. Setup Sample in Current Scene`**
2. Press **Play** to automatically generate the Portrait (`1080x1920` reference) UI:
   - **Top Section (Battle Arena)**: Wave header, enemy & player status cards, 2x2 command buttons, and real-time battle log.
   - **Bottom Section (GPGS Dashboard)**: One-tap tab switcher between **`📊 Game Stats (5)`** and **`👤 PlayerStats (10)`**.

---

## Implemented Features & APIs

### 1. Game Stats API (`PlayGames.getGameStatsClient()` / `PlayerGameEvent`)

Uses `GameStatsClient` from GPGS v2 (`play-services-games-v2:22.0.0`) to record gameplay actions as structured events (`PlayerGameEvent`) and immediately flushes them to Google Play servers via `requestEventsUpload()`.

#### 6 Events Recorded (`PlayConsole/gpgs_events_import.csv`)

| Event Name | Properties (`Name` : `Type`) | Trigger Timing |
| :--- | :--- | :--- |
| `battle_started` | `wave_number` (`INT64`), `enemy_name` (`STRING`) | When each wave starts |
| `damage_dealt` | `damage_amount` (`INT64`), `skill_name` (`STRING`), `is_critical` (`BOOL`) | When a player attack hits |
| `critical_hit_landed` | `damage_amount` (`INT64`), `skill_name` (`STRING`) | When a critical hit lands |
| `potion_used` | `healed_amount` (`INT64`), `remaining_potions` (`INT64`) | When a healing potion is used |
| `monster_defeated` | `enemy_name` (`STRING`), `wave_number` (`INT64`) | When an enemy monster is defeated |
| `battle_won` | `wave_number` (`INT64`), `remaining_hp` (`INT64`) | When a wave is cleared |

#### 5 Game Stats Aggregated in Play Console (`RepetitiveStatsConfig.csv`)

| Stat ID | Display Name | Source Event & Property | Aggregation | Icon | Description |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `max_wave_reached` | **Highest Wave Reached** | `battle_won.wave_number` | `MAX` | `icon_wave.png` | Highest battle wave reached by the player |
| `total_damage_dealt` | **Total Damage Dealt** | `damage_dealt.damage_amount` | `SUM` | `icon_damage.png` | Total cumulative damage dealt to monsters |
| `max_critical_damage` | **Max Critical Damage** | `critical_hit_landed.damage_amount` | `MAX` | `icon_crit.png` | Maximum damage dealt in a single critical hit |
| `best_victory_hp` | **Best Victory HP** | `battle_won.remaining_hp` | `MAX` | `icon_hp.png` | Highest remaining HP at battle victory |
| `total_healed_hp` | **Total HP Healed** | `potion_used.healed_amount` | `SUM` | `icon_potion.png` | Total cumulative HP recovered using potions |

---

### 2. PlayerStats API (`PlayGames.getPlayerStatsClient().loadPlayerStats`)

Fetches all 10 behavioral and predictive metrics from `com.google.android.gms.games.stats.PlayerStats`, displays them in the **`👤 PlayerStats (10)`** tab, and applies dynamic in-game buffs based on player engagement.
*(Note: For newly created apps where Google's backend predictive model has not yet generated player stats and returns `STATUS_CLIENT_HIDDEN (26504)`, the UI notes this status while preserving simulated values for testing dynamic buffs.)*

| Metric | Description | In-Game Usage / Notes |
| :--- | :--- | :--- |
| `NumberOfSessions` | Total number of sessions | Grants **Veteran Bonus (CRIT +15%)** at `>= 10` sessions |
| `AvgSessionLength` | Average session length (minutes) | Tracks session engagement duration |
| `DaysSinceLastPlayed` | Days since last played | Grants **Comeback Buff** when `>= 7` days |
| `NumberOfPurchases` | Total in-app purchases | Identifies supporter status |
| `SessPercentile` | Session count percentile (`0.0–1.0`) | Grants Veteran status for top 30% (`>= 0.70`) |
| `SpendPercentile` | Spend percentile (`0.0–1.0`) | Spender tier classification |
| `SpendProbability` | Probability of future spend (`0.0–1.0`) | Recommends starter pack when `>= 0.40` |
| `ChurnProbability` | Churn probability (`0.0–1.0`) | Grants **Comeback Buff (ATK +30% / DMG Taken -20%)** when `>= 0.50` |
| `HighSpenderProbability` | High spender probability (`0.0–1.0`) | VIP offer targeting |
| `TotalSpendNext28Days` | Predicted spend over next 28 days | Predicted 28-day LTV metric |

---

## Android Build & Google Play Console Setup

### 1. Unity Editor Configuration
1. **`Tools > Trivial Stadium (GPGS Stats) > 4. Configure Release AAB & Keystore Settings`**
   - Configures signing with `Keystores/trivial_stadium_release.keystore`, IL2CPP, and ARM64 (also automatically enforced during builds by `GpgsAndroidBuildProcessor`).
2. **`Tools > Trivial Stadium (GPGS Stats) > 5. Set Google Play Project ID (App ID)`**
   - Enter the 11–13 digit numeric Project ID from **Play Console > Play Games Services > Setup and management > Configuration** (e.g., `1042772214186`).
3. **`Tools > Trivial Stadium (GPGS Stats) > 6. Increment Android Version Code (+1)`**
   - Increments the Android Bundle Version Code before uploading a new AAB to Play Console (current: `versionCode: 6`, `bundleVersion: 1.5`).

### 2. Google Play Console Configuration (Important)
1. **Register and Publish OAuth Clients (SHA-1 Certificate Fingerprints)**
   - When using Play App Signing, register Android OAuth clients for **both** the **Upload key certificate SHA-1** and the **App signing key certificate SHA-1**, then **Publish** your Play Games Services configuration.
2. **Import and Publish Events & Game Stats Configurations**
   - Import `PlayConsole/gpgs_events_import.csv` under **Events configuration**.
   - Import `PlayConsole/gpgs_stats_import.zip` under **Game Stats Configuration**.
   - Make sure both configurations are **Published (`Available to everyone`)**. Events recorded while configurations are in `Draft` state are rejected by Google Play Services on the device.

---

## How to View Game Stats on an Android Device (Google Play Store)

By design in the Google Play Store (`com.android.vending`), the in-game **Sidekick initial drawer** only displays four fixed modules: *Quick Tools*, *Captures*, *Play Streaks*, and *Play Points*. **Game Stats cards are displayed in the "You" tab of the Google Play Store app.**

*(Note: Tapping "View full profile" from Sidekick opens your public gamer profile `my.play/...`, which only displays play time. Follow the steps below to view individual Game Stats.)*

1. Open the **Google Play Store** app on your Android device.
2. Tap the **"You" (マイページ)** tab (person icon at the bottom-right of the navigation bar) to open the root page showing **"Hello, [Your Name]"**.
   - *If you are currently on the public profile sub-page (`my.play/...`), tap the bottom-right **"You"** tab icon (or the top-left `←` back arrow) once more to return to the root "You" page.*
3. Scroll down slightly to the **"Game stats" (ゲームの統計データ)** section, where your recent stat cards appear in a horizontal carousel.
4. Tap the **`Trivial Stadium`** card (or the **Weekly Recap** banner) inside **"Game stats"** to open the **"Game data" (ゲームのデータ)** detail screen, where all 5 stats (*Highest Wave Reached*, *Total Damage Dealt*, *Max Critical Damage*, *Best Victory HP*, and *Total HP Healed*) are displayed and updated in real time.

# Total War: ROME REMASTERED — Save Manager

A friendly, robust save manager and backup utility for **Total War: ROME REMASTERED**. Available as both an intuitive **Desktop GUI** (WPF) and an **Interactive CLI tool** (.NET 10).

---

## Table of Contents

- [Why Do I Need This?](#why-do-i-need-this)
- [Key Features](#key-features)
- [Prerequisites](#prerequisites)
- [Quick Start Guide](#quick-start-guide)
- [Campaign & Faction Recognition](#campaign--faction-recognition)
- [Using the Desktop Application (GUI)](#using-the-desktop-application-gui)
  - [First Launch & Setup](#first-launch--setup)
  - [Creating Backups](#creating-backups)
  - [Restoring a Past Save](#restoring-a-past-save)
  - [Filtering & Searching by Faction](#filtering--searching-by-faction)
  - [Autosave Sentinel & Background Monitoring](#autosave-sentinel--background-monitoring)
  - [Campaign Chronologer & AAR Generator (New!)](#campaign-chronologer--aar-generator-new)
  - [Configuring Settings](#configuring-settings)
- [Using the Command-Line Interface (CLI)](#using-the-command-line-interface-cli)
  - [Interactive Menu Mode](#interactive-menu-mode)
  - [One-Line Headless Commands (Automation & Shortcuts)](#one-line-headless-commands-automation--shortcuts)
- [Where Are Files Stored?](#where-are-files-stored)
- [Frequently Asked Questions & Troubleshooting](#frequently-asked-questions--troubleshooting)
- [Development & Building from Source](#development--building-from-source)

---

## Why Do I Need This?

In *Total War: ROME REMASTERED*, campaigns span dozens of hours and hundreds of turns. However, managing game saves comes with hazards:
- **Autosaves get overwritten** every turn. If an unexpected faction betrayal, assassination, or catastrophic battle occurs, your autosave will already be gone.
- **Accidental overwrites**: Saving over the wrong campaign slot can destroy hours of progress.
- **Multiple campaigns clash**: Juggling active saves between a Roman campaign, a Greek city-state, and a barbarian faction easily gets cluttered.
- **Experimenting with risky strategies**: Want to see what happens if you incite the Roman Civil War early or declare war on three empires at once?
- **Game updates & mods**: Updates or testing mods can occasionally corrupt active saves.

**Rome Remastered Save Manager** acts like a personal time machine for your campaigns. With one click, it snapshots your current game saves, tags them with meaningful notes (like `"Turn_50_Before_Civil_War"`), and lets you roll back whenever you want — with an automatic safety net so you can never accidentally lose your active game.

---

## Key Features

- **Campaign Chronologer & AAR Generator (New!)**: Turn your gameplay history into an epic saga. Aggregates active saves and backups into a unified chronological timeline, lets you write journal entries, title milestones, add tags, and export publication-ready After Action Reports in styled HTML or Markdown.
- **Autosave Sentinel (Background Watcher)**: Automatically creates snapshots in real time whenever Rome Remastered writes or updates a save to disk, complete with debouncing and file-lock protection.
- **System Tray & Desktop Integration**: Minimizes or closes to the Windows notification area, keeps the Autosave Sentinel running quietly while you game, provides balloon notifications, and includes a one-click Steam game launcher (`⚔ Launch Game`).
- **Automatic Campaign & Faction Recognition**: Automatically identifies the faction or campaign name directly from savefile names (supporting Rome Remastered autosaves, manual hyphenated saves, direct faction names, and smart quicksave association).
- **Faction-Sorted Backup Folders**: Backups are organized on disk into dedicated faction subfolders (e.g. `Rome Remastered Backups/Kingdom of Macedon/Backup_...`).
- **Per-Campaign Retention**: When retention limits are enabled, snapshots are managed on a per-campaign basis — starting a new campaign will **not** purge backups of your older campaigns!
- **Instant Active Campaign Backup**: One-click button to snapshot the campaign you are currently playing.
- **Backup All Campaigns**: Back up every active campaign detected in your save folder in a single click.
- **Automatic Detection**: Discovers your Rome Remastered save directory out-of-the-box (supports standard Feral Interactive folders and Steam userdata paths).
- **Custom Named Checkpoints**: Label your backups (e.g. `Julii_Turn30_Invading_Gaul`, `Brutii_Before_Senate_Demands`).
- **Safety-First Restore**: Restoring a backup automatically creates a pre-restore safety copy first. You never risk losing your current save by rolling back.
- **Optional ZIP Compression**: Save disk space by storing snapshots as `.zip` archives or keep them as plain folders.
- **Accessible UI Polish**: Fully supports high-contrast accessible controls with customized dropdowns and responsive keyboard shortcuts.
- **CLI & Scripting Ready**: Run unattended backups via commands or automate them before launching the game.

---

## Prerequisites

- **Operating System**: Windows 10 or Windows 11 (64-bit)
- **Runtime**: [.NET 10.0 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (or [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) if building/running from source code)
  - *Note for GUI*: Ensure the **.NET Desktop Runtime 10.0** is installed on Windows.
- **Game**: Total War: ROME REMASTERED installed on PC (Steam or Feral Interactive release).

---

## Quick Start Guide

### Option 1: Run via .NET CLI

If you have the .NET 10 SDK installed:

```bash
# Launch the Desktop GUI
dotnet run --project "Rome Remastered Save Manager/RRM-SM.UI/RRM-SM.UI.csproj"

# Launch the interactive terminal menu
dotnet run --project "Rome Remastered Save Manager/RRM-SM/RRM-SM.csproj"
```

### Option 2: Run the Compiled App

Navigate to the project build folder and double-click:
```text
Rome Remastered Save Manager\RRM-SM.UI\bin\Debug\net10.0-windows\RRM-SM.UI.exe
```

---

## Campaign & Faction Recognition

Total War: ROME REMASTERED generates distinct save file names that encode faction and campaign metadata:

| Savefile Pattern | Example | Parsed Faction |
| :--- | :--- | :--- |
| `save_Autosave   {Faction}   Turn {X}.sav` | `save_Autosave   Kingdom of Scotland   Turn 6.sav` | `Kingdom of Scotland` (Turn 6) |
| `save_Autosave   {Faction}   Turn {X} [Start/End].sav` | `save_Autosave   Pergamon   Turn 20 Start.sav` | `Pergamon` (Turn 20) |
| `save_{Faction} - {Turn/Details}.sav` | `save_Kingdom of Macedon - 101.sav` | `Kingdom of Macedon` (Turn 101) |
| `save_{Faction} - Battle.sav` | `save_Kingdom of Macedon - Battle.sav` | `Kingdom of Macedon` (Battle) |
| `save_{Faction}.sav` | `save_Bactria.sav` | `Bactria` |
| `save_Quicksave.sav` | `save_Quicksave.sav` | Associated with the active campaign |

The Save Manager uses these patterns to group your saves automatically.

---

## Using the Desktop Application (GUI)

### First Launch & Setup

1. Launch `RRM-SM.UI.exe`.
2. Click the **Settings** tab.
3. Look at the **Game Save Directory** indicator:
   - **Directory found**: The application automatically detected where your game stores saves!
   - **Directory NOT found**: Click **Auto-Detect** to scan again. If you installed the game in a custom location, click **Browse...** and select your game's `saves` folder.
4. Verify your **Backup Storage Directory** (defaults to `Documents\Rome Remastered Backups`).
5. Click **Save Settings**.

---

### Creating Backups

Switch to the **Backups Manager** tab:

1. **Active Campaign Selector**: The app inspects your save folder and highlights which faction was played most recently in the **Active Campaign** dropdown.
2. **Quick Backup**: Snapshots the selected campaign with current timestamp (e.g. `Pergamon/Backup_2026-09-18_19-30-00`).
3. **Named Backup**: Prompts you for a descriptive tag (e.g. `Siege_Of_Carthage`) and backs up the selected campaign.
4. **Backup All Campaigns**: One-click button that scans all detected factions and creates individual, organized backups for each active campaign.

---

### Restoring a Past Save

1. In the **Backups Manager** list, click on the backup you want to return to.
2. Click **Restore Selected**.
3. A confirmation dialog will appear:
   - The app will ask if you would like to create a **Safety Backup** of your currently active saves before proceeding.
   - Click **Yes** (recommended). The app saves your current campaign state as `SafetyBackup_PreRestore_[Timestamp]` and restores the chosen snapshot into the game's folder.
4. Start or switch back to Rome Remastered and load the save file!

---

### Filtering & Searching by Faction

- **Filter by Faction**: Use the faction dropdown above the data grid (`[All Campaigns]`, `Kingdom of Macedon`, `Pergamon`, `Rome`, etc.) to view only snapshots for a specific campaign.
- **Search**: Use the search box to filter backups by custom tag, turn number, or date.
- **Campaign Column**: The data grid clearly displays the **Campaign / Faction** associated with every backup.

---

### Autosave Sentinel & Background Monitoring

The **Autosave Sentinel** monitors your Rome Remastered game save folder in real time (`FileSystemWatcher`) and automatically takes backup snapshots without any manual intervention.

1. **How It Works**:
   - Whenever Rome Remastered saves the game (end of turn autosave, quicksave, or manual battle save), the Sentinel detects the file write.
   - It utilizes a customizable **debounce buffer** (default `1500 ms`) to let the game finish its multi-stage disk flush cleanly.
   - Built-in file lock retry logic (`WaitForFileAvailable`) safely waits for the game engine to release exclusive file locks before copying.
   - The backup is immediately filed under the detected campaign folder and labeled `AutosaveSentinel_[Timestamp]`.
2. **Interactive Header Pill & System Tray**:
   - Click the **`🛡 Sentinel: Active` / `🛡 Sentinel: Off`** badge in the window header to quickly toggle monitoring on and off.
   - When **Minimize to System Tray** is enabled, minimizing or clicking `[X]` to close the window will send the app to the Windows system tray. The Sentinel continues protecting your saves while you game in full screen!
   - Right-click the tray icon to toggle the Sentinel, perform an instant quick backup, launch the game, or restore the window.
   - When **Windows Notifications** are enabled, balloon/toast messages notify you whenever an automated background backup succeeds.

---

### Campaign Chronologer & AAR Generator (New!)

The **Campaign Chronicle & AAR** tab turns your collection of saves into an interactive campaign timeline and lets you write your own After Action Reports (AARs) or historical lore journals.

#### What Does the Chronologer Do?
- **Unified Timeline Aggregation**: Combines active game saves from your game folder with all archived snapshots from your backup directories into one cohesive chronological timeline ordered by date and turn number.
- **De-duplication**: Identifies saves across folders by name and timestamp so your timeline remains clean and accurate without redundant duplicate entries.
- **Per-Turn Milestone Journaling**: Click on any milestone/turn on the timeline to write custom event headlines, lore notes, battle summaries, and tags.
- **Portable Note Storage (`chronicle.json`)**: All your journal entries and tags are saved inside a lightweight `chronicle.json` file inside that specific campaign's backup directory. Your notes naturally travel alongside your backups if you move or sync folders!
- **One-Click Publishing (HTML & Markdown)**: Generates complete, styled After Action Reports with a single click.

#### Step-by-Step Instructions:
1. **Select a Campaign**:
   - Switch to the **📜 Campaign Chronicle & AAR** tab.
   - In the **Chronicle Campaign** dropdown at the top, select the campaign or faction you want to view (e.g., `Republic of Rome` or `Kingdom of Macedon`).
   - The summary badge will immediately display total milestones detected and the highest reached turn (e.g., `42 Milestones | Max Turn 185`).
2. **Browse the Timeline**:
   - The left pane displays all historical milestones sorted chronologically.
   - Each entry shows the turn badge (`T6`, `T20`, etc.), save category (`Autosave`, `Manual`, `Battle`, `Quicksave`), timestamp, and custom event headline.
3. **Record Journal Notes & Milestones**:
   - Click any milestone in the timeline. The right pane will open the **Journal Editor**.
   - **Event Title / Headline**: Give this turn a memorable name (e.g., *The Siege of Syracuse*, *Defeat of the Gallic Horde*, *First Senate Triumph*).
   - **Journal Notes**: Write your in-character roleplay lore, tactical notes, thoughts, or battle summaries. Supports multi-line paragraphs.
   - **Quick Tags**: Enter comma-separated tags (e.g., `Battle, Expansion, Crisis, Economy`) to categorize the event.
   - Click **💾 Save Journal Notes** to commit the notes to `chronicle.json`.
4. **Export After Action Reports (AAR)**:
   - **📜 Export HTML**: Compiles your entire campaign timeline, faction summary, and notes into an elegant, styled standalone HTML report and automatically launches it in your default web browser for viewing or printing to PDF.
   - **📝 Copy Markdown**: Formats the entire campaign chronicle into clean GitHub/forum-compatible Markdown and copies it directly to your clipboard, ready to paste into Reddit, Discord, Steam Guides, or Total War community forums!
   - **🔄 Refresh**: Re-scans active saves and backups at any time to immediately pull in new turns played during your gaming session.

---

### Configuring Settings

Under the **Settings** tab, you can customize:

| Setting | Description | Default |
| :--- | :--- | :--- |
| **Game Save Directory** | Path where Rome Remastered writes saves. | Auto-detected |
| **Backup Storage Directory** | Where all your snapshots are stored. | `Documents\Rome Remastered Backups` |
| **Compress backups (.zip)** | Toggle between folder snapshots or compressed `.zip` archives to save disk space. | Unchecked (Folder snapshots) |
| **Max Backups to Keep** | Limits total stored backups per campaign to avoid consuming too much disk space. Set to `0` for unlimited. | `0` (Unlimited) |
| **Autosave Sentinel** | Automatically triggers a backup snapshot when the game writes to disk. | Unchecked (`false`) |
| **Windows Notifications** | Displays balloon notifications when an automated background backup occurs. | Checked (`true`) |
| **System Tray** | Minimizes/closes the app to the Windows tray so background monitoring continues uninterrupted. | Checked (`true`) |
| **Save Detection Buffer** | Debounce delay in milliseconds before copying saves to guarantee file flushes are completed. | `1500 ms` |

---

## Using the Command-Line Interface (CLI)

Prefer terminal commands or want to automate backups with Windows Task Scheduler or a batch script? The CLI (`RRM-SM`) is built for you.

### Interactive Menu Mode

Run without arguments:
```bash
dotnet run --project "Rome Remastered Save Manager/RRM-SM/RRM-SM.csproj"
```
Or run `RRM-SM.exe` directly in your terminal. You will see an interactive menu:

```text
================================================================================
           TOTAL WAR: ROME REMASTERED - SAVE MANAGER & BACKUP TOOL              
================================================================================
  Game Save Folder : C:\Users\...\Rome\saves [OK]
  Backup Directory : C:\Users\...\Rome Remastered Backups [OK]
  Mode             : Folder Snapshots | Max Backups (per campaign): Unlimited
  Active Factions  : Kingdom of Macedon, Rome, Pergamon, Byzantium
  Most Recent Play : Kingdom of Macedon
================================================================================
  [1] Quick Backup       - Snapshot most recent campaign [Kingdom of Macedon]
  [2] Backup All         - Snapshot all active campaigns into faction folders
  [3] Named Backup       - Snapshot a chosen campaign with a custom label
  [4] Restore Backup     - Restore past campaign state with safety backup
  [5] List All Backups   - View existing snapshots grouped by faction
  [6] Open Active Saves  - Reveal save folder in File Explorer
  [7] Open Backup Folder - Reveal backup folder in File Explorer
  [8] Settings           - Configure folders, compression, retention
  [0] Exit
================================================================================
Select an option [0-8]:
```

---

### One-Line Headless Commands (Automation & Shortcuts)

```bash
# 1. Quick backup of the most recently played campaign
RRM-SM --backup

# 2. Backup a specific campaign
RRM-SM --backup --campaign "Kingdom of Macedon"

# 3. Backup a specific campaign with a custom label
RRM-SM --backup "Turn101_Invasion" --campaign "Kingdom of Macedon"

# 4. Backup all detected campaigns into their respective faction folders
RRM-SM --backup-all

# 5. List all backups (or filter by campaign)
RRM-SM --list
RRM-SM --list --campaign "Pergamon"

# 6. View all available flags
RRM-SM --help
```

---

## Where Are Files Stored?

### Active Game Saves (Rome Remastered)
Depending on your platform and Steam settings, the game typically stores saves in:
- **Standard Feral Local Path**:
  ```text
  %LOCALAPPDATA%\Feral Interactive\Total War ROME REMASTERED\VFS\Local\Rome\saves
  ```
- **Steam Cloud Userdata**:
  ```text
  C:\Program Files (x86)\Steam\userdata\<YourSteamID>\885970\remote
  ```

### Organized Backups Directory
Backups are organized by faction under your backup directory:
```text
Rome Remastered Backups/
├── Kingdom of Macedon/
│   ├── Backup_2026-09-18_18-00-00/
│   └── Backup_2026-09-18_19-30-00_Turn101/
├── Pergamon/
│   ├── Backup_2026-09-18_20-15-00/
│   └── SafetyBackup_PreRestore_2026-09-18_20-45-00/
└── General/
    └── Backup_2026-09-18_21-00-00/
```

---

## Frequently Asked Questions & Troubleshooting

### 1. How does campaign sorting work if I play two different campaigns with the same faction?
Because the game uses the faction name in the autosave format, saves from two separate campaigns playing the same faction (e.g., two Julii campaigns started at different times) will be placed in the same faction folder (`The House of Julii/`). You can use **Named Backup** to add custom tags (e.g. `Campaign2_Turn10`) to distinguish them.

### 2. What happens to `save_Quicksave.sav`?
`save_Quicksave.sav` does not include a faction name in its filename. The Save Manager intelligently checks the timestamps of active campaign saves in your folder and associates the quicksave with your current active campaign. If no active campaign can be determined, it is saved under `General`.

### 3. Will my older campaigns be deleted when retention limit is reached?
No! Retention limits are applied **per campaign**. If your retention limit is set to 5, you can have 5 backups for Macedon, 5 for Pergamon, and 5 for Rome without them overwriting each other.

### 4. Where are my Campaign Chronicle notes and AAR journals stored?
Chronicle notes are saved in a JSON file (`chronicle.json`) directly inside that campaign's backup folder (`<BackupDirectory>/<CampaignName>/chronicle.json`). Because the file resides right alongside your snapshots, your lore, milestone headlines, and custom notes automatically travel with your backups if you copy or sync them to another machine.

---

## Development & Building from Source

```bash
# Build entire solution
dotnet build "Rome Remastered Save Manager/Rome Remastered Save Manager.sln"

# Run automated tests
dotnet test "Rome Remastered Save Manager/Rome Remastered Save Manager.sln"
```

---

## License

This project is licensed under the MIT License — feel free to use, modify, and distribute.

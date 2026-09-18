# Total War: ROME REMASTERED — Save Manager

A friendly, robust save manager and backup utility for **Total War: ROME REMASTERED**. Available as both an intuitive **Desktop GUI** (WPF) and an **Interactive CLI tool** (.NET 8).

---

## Table of Contents

- [Why Do I Need This?](#why-do-i-need-this)
- [Key Features](#key-features)
- [Prerequisites](#prerequisites)
- [Quick Start Guide](#quick-start-guide)
- [Using the Desktop Application (GUI)](#using-the-desktop-application-gui)
  - [First Launch & Setup](#first-launch--setup)
  - [Creating Backups](#creating-backups)
  - [Restoring a Past Save](#restoring-a-past-save)
  - [Browsing & Deleting Backups](#browsing--deleting-backups)
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
- **Experimenting with risky strategies**: Want to see what happens if you incite the Roman Civil War early or declare war on three empires at once?
- **Game updates & mods**: Updates or installing/testing mods can occasionally corrupt active saves.

**Rome Remastered Save Manager** acts like a personal time machine for your campaigns. With one click, it snapshots your current game saves, tags them with meaningful notes (like `"Turn_50_Before_Civil_War"`), and lets you roll back whenever you want — with an automatic safety net so you can never accidentally lose your active game.

---

## Key Features

- 🔍 **Automatic Detection**: Finds your Rome Remastered save directory out-of-the-box (supports standard Feral Interactive folders and Steam userdata paths).
- ⚡ **Instant Snapshots**: Save your entire active campaign folder in a second.
- 🏷 **Custom Named Checkpoints**: Label your backups (e.g. `Julii_Turn30_Invading_Gaul`, `Brutii_Before_Senate_Demands`).
- 🛡 **Safety-First Restore**: Restoring a backup automatically creates a pre-restore safety copy first. You never risk losing your current save by rolling back.
- 🗜 **Optional ZIP Compression**: Save disk space by storing snapshots as `.zip` archives or keep them as plain folders.
- 🧹 **Retention Limit**: Automatically keep your most recent snapshots (e.g., keep the last 15) or keep unlimited backups.
- 📂 **Quick Folder Access**: One-click buttons to reveal your save folder or backup directory directly in Windows File Explorer.
- 💻 **CLI & Scripting Ready**: Run unattended backups via commands or automate them before launching the game.

---

## Prerequisites

- **Operating System**: Windows 10 or Windows 11 (64-bit)
- **Runtime**: [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (or [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) if building/running from source code)
  - *Note for GUI*: Ensure the **.NET Desktop Runtime 8.0** is installed on Windows.
- **Game**: Total War: ROME REMASTERED installed on PC (Steam or Feral Interactive release).

---

## Quick Start Guide

### Option 1: Run via .NET CLI

If you have the .NET 8 SDK installed:

```bash
# Launch the Desktop GUI
dotnet run --project "Rome Remastered Save Manager/RRM-SM.UI/RRM-SM.UI.csproj"

# Launch the interactive terminal menu
dotnet run --project "Rome Remastered Save Manager/RRM-SM/RRM-SM.csproj"
```

### Option 2: Run the Compiled App

Navigate to the project build folder and double-click:
```text
Rome Remastered Save Manager\RRM-SM.UI\bin\Debug\net8.0-windows\RRM-SM.UI.exe
```

---

## Using the Desktop Application (GUI)

The GUI is designed to be clean, dark-themed, and easy to use without any technical background.

### First Launch & Setup

1. Launch `RRM-SM.UI.exe`.
2. Click the **⚙ Settings** tab.
3. Look at the **Game Save Directory** indicator:
   - 🟢 **Directory found ✔**: The application automatically detected where your game stores saves!
   - 🔴 **Directory NOT found**: Click **Auto-Detect** to scan again. If you installed the game in a custom location, click **Browse...** and select your game's `saves` folder.
   > [!TIP]
   > If you just purchased or installed the game and haven't played yet, start the game once and create at least one manual save or start a campaign so the game creates its save directory on disk.
4. Verify your **Backup Storage Directory** (defaults to `Documents\Rome Remastered Backups`). You can change this to any drive or folder you prefer.
5. Click **💾 Save Settings**.

---

### Creating Backups

Switch to the **⚔ Backups Manager** tab:

- **⚡ Quick Backup**: 
  - Click this button anytime (e.g., between turns or after winning a major battle).
  - Creates a timestamped snapshot such as `Backup_2026-09-18_19-30-00`.
- **🏷 Named Backup**:
  - Click this button to enter a descriptive tag (e.g. `Scipii_Siege_Of_Carthage`).
  - The snapshot will be saved with your custom label attached for easy identification later.

---

### Restoring a Past Save

1. In the **⚔ Backups Manager** list, click on the backup you want to return to.
2. Click **↩ Restore Selected**.
3. A confirmation dialog will appear:
   - The app will ask if you would like to create a **Safety Backup** of your currently active saves before proceeding.
   - Click **Yes** (recommended). The app saves your current campaign as `SafetyBackup_PreRestore_[Timestamp]` and immediately restores the chosen snapshot into the game's folder.
4. Start or switch back to Rome Remastered and load the save file!

> [!NOTE]
> **Best Practice**: We recommend being on the game's **Main Menu** or having the game closed when restoring a backup. This ensures the game refreshes its save list from disk cleanly.

---

### Browsing & Deleting Backups

- **Search**: Use the search box (`🔍`) at the top of the list to filter your backups in real time by faction name, turn number, or date.
- **Open in Explorer**: Select any backup and click **📂 Open in Explorer** to view the snapshot folder or zip file directly in Windows File Explorer.
- **Delete Selected**: Click **🗑 Delete Selected** to permanently remove backups you no longer need.

---

### Configuring Settings

Under the **⚙ Settings** tab, you can customize:

| Setting | Description | Default |
| :--- | :--- | :--- |
| **Game Save Directory** | Path where Rome Remastered writes saves. | Auto-detected |
| **Backup Storage Directory** | Where all your snapshots are stored. | `Documents\Rome Remastered Backups` |
| **Compress backups (.zip)** | Toggle between folder snapshots or compressed `.zip` archives to save disk space. | Unchecked (Folder snapshots) |
| **Max Backups to Keep** | Limits total stored backups to avoid consuming too much disk space. Oldest backups are purged when a new one is made. Set to `0` for unlimited. | `0` (Unlimited) |

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
  Compression      : Disabled (Folder Snapshots)
  Retention Limit  : Unlimited (0)
================================================================================
  [1] Quick Backup       - Snapshot saves with current timestamp
  [2] Named Backup       - Snapshot saves with custom tag/label
  [3] Restore Backup     - Restore a past snapshot into the game folder
  [4] List Backups       - View existing snapshots with size and date
  [5] Open Save Folder   - Reveal active saves in File Explorer
  [6] Open Backup Folder - Reveal backup directory in File Explorer
  [7] Settings           - Configure folders, compression, retention
  [0] Exit
================================================================================
Select an option [0-7]:
```

Simply type the number of your choice and press <kbd>Enter</kbd>.

---

### One-Line Headless Commands (Automation & Shortcuts)

You can pass arguments to execute tasks directly without entering interactive mode:

```bash
# 1. Immediate quick backup
RRM-SM --backup

# 2. Backup with a custom label
RRM-SM --backup "Brutii_Macedon_Campaign"

# 3. List all current backups with sizes and dates
RRM-SM --list

# 4. View all available flags
RRM-SM --help
```

#### Handy Tip: Create a "Play Game with Auto-Backup" Script

You can create a simple `play_rome.bat` file on your desktop that backs up your saves right before launching the game:

```bat
@echo off
"C:\Path\To\RRM-SM.exe" --backup "Auto_Session_Start"
start steam://rungameid/885970
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

### Backups Directory
By default, backups are saved in:
```text
C:\Users\<YourUsername>\Documents\Rome Remastered Backups
```
Inside this folder, you will find each backup organized by timestamp:
```text
Rome Remastered Backups/
├── Backup_2026-09-18_18-00-00/
├── Backup_2026-09-18_19-15-22_Turn50_Julii_CivilWar/
└── SafetyBackup_PreRestore_2026-09-18_19-30-00/
```

### Application Settings (`config.json`)
The application stores its configuration file in `config.json` next to the executable:
```json
{
  "GameSaveDirectory": "C:\\Users\\Username\\AppData\\Local\\Feral Interactive\\Total War ROME REMASTERED\\VFS\\Local\\Rome\\saves",
  "BackupDirectory": "C:\\Users\\Username\\Documents\\Rome Remastered Backups",
  "CompressBackups": false,
  "MaxBackupsToKeep": 0
}
```

---

## Frequently Asked Questions & Troubleshooting

### 1. The status says "Directory NOT found". What should I do?
1. Launch Total War: ROME REMASTERED at least once. Start a campaign and manually save the game so the game engine creates its save folders.
2. In the Save Manager, click **⚙ Settings** and press **Auto-Detect**.
3. If it still cannot find the folder, open Windows Explorer, find your `saves` folder (using the paths listed above), copy the path, paste it into the **Game Save Directory** field, and click **💾 Save Settings**.

### 2. Can I restore a backup while the game is running?
Yes, but it is recommended to be at the **Main Menu** or close the game first. If you restore while loaded inside an active battle or campaign map, the game might overwrite the newly restored files when autosaving on exit.

### 3. Will this work if I play with mods?
Yes! The Save Manager creates copies of all save files inside the target save directory, regardless of whether you are playing vanilla Rome, Barbarian Invasion, Alexander, or complete overhaul mods.

### 4. What is a "Safety Backup"?
Whenever you restore an older snapshot, the app automatically makes a quick backup of whatever is currently in your game save folder first. That way, if you restore the wrong turn by mistake, you can simply restore the safety backup to get back to where you were.

---

## Development & Building from Source

### Repository Structure

```text
Rome Remastered Save Manager/
├── RRM-SM.Core/          # Shared library: business logic, models, backup/config services
├── RRM-SM.UI/            # Modern dark-themed WPF desktop application
├── RRM-SM/               # Terminal-based CLI tool (interactive menu & command flags)
└── RRM-SM.Tests/         # Automated unit test suite (xUnit)
```

### Building the Entire Solution

```bash
dotnet build "Rome Remastered Save Manager/Rome Remastered Save Manager.sln"
```

### Running Tests

```bash
dotnet test "Rome Remastered Save Manager/Rome Remastered Save Manager.sln"
```

---

## License

This project is licensed under the MIT License — feel free to use, modify, and distribute.

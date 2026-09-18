# Total War: ROME REMASTERED - Save Manager

A lightweight, robust Save Manager and Backup utility for **Total War: ROME REMASTERED** (.NET 8).

## Features

- **Auto-Detection**: Automatically discovers your Rome Remastered save directory (`%LOCALAPPDATA%\Feral Interactive\Total War ROME REMASTERED\VFS\Local\Rome\saves`).
- **Snapshot Versioning**: Never overwrite or lose past campaign states. Create timestamped snapshots or custom-labeled checkpoints (e.g. `Turn_50_Before_Civil_War`).
- **Full Restore with Safety Net**: Restore any previous snapshot directly into the game folder. Creates an automatic safety backup before overwriting active saves.
- **Compression**: Toggle between folder snapshots or `.zip` archives.
- **Retention Management**: Set maximum snapshots to keep (or unlimited).
- **Direct Explorer Integration**: Open active saves and backup folders in File Explorer with one key.
- **CLI & Scripting Support**: Run unattended backups via flags (e.g., in scheduled tasks or desktop shortcuts).

---

## Interactive Menu Options

Launch `RRM-SM.exe` or `dotnet run` to open the interactive console UI:

```
================================================================================
           TOTAL WAR: ROME REMASTERED - SAVE MANAGER & BACKUP TOOL              
================================================================================
  Game Save Folder : ...\AppData\Local\Feral Interactive\...\saves [OK]
  Backup Directory : ...\Documents\Rome Remastered Backups
  Mode             : Folder Snapshots | Max Backups: Unlimited
--------------------------------------------------------------------------------
  [1] Quick Backup (Timestamped snapshot of all active saves)
  [2] Named Backup (Tag snapshot e.g. 'Turn 50 Before Siege')
  [3] Restore Backup (Restore a saved state with safety fallback)
  [4] List All Backups
  [5] Open Active Saves in File Explorer
  [6] Open Backup Directory in File Explorer
  [7] Configuration & Settings
  [0] Exit
```

---

## Command Line Arguments

Run non-interactively for automation or batch scripts:

```bash
# Create an immediate quick backup
RRM-SM --backup

# Create a backup with a custom tag
RRM-SM --backup "Brutii_Macedon_Campaign"

# List existing backups
RRM-SM --list

# Show help
RRM-SM --help
```

---

## Configuration (`config.json`)

On first launch, `config.json` is automatically created in the application directory:

```json
{
  "GameSaveDirectory": "C:\\Users\\<Username>\\AppData\\Local\\Feral Interactive\\Total War ROME REMASTERED\\VFS\\Local\\Rome\\saves",
  "BackupDirectory": "C:\\Users\\<Username>\\Documents\\Rome Remastered Backups",
  "CompressBackups": false,
  "MaxBackupsToKeep": 0
}
```

- `CompressBackups`: Set to `true` to store backups as `.zip` files.
- `MaxBackupsToKeep`: `0` preserves all backups indefinitely. Set to `5`, `10`, etc. to keep only the newest N user backups.

---

## Running Tests

Automated xUnit tests are included in `RRM-SM.Tests`:

```bash
dotnet test "Rome Remastered Save Manager/Rome Remastered Save Manager.sln"
```


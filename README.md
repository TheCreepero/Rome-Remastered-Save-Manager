# Total War: ROME REMASTERED - Save Manager

A robust Save Manager and Backup utility for **Total War: ROME REMASTERED** (.NET 8), available as both a **Desktop GUI** (WPF) and a **CLI tool**.

## Features

- **Auto-Detection**: Automatically discovers your Rome Remastered save directory (`%LOCALAPPDATA%\Feral Interactive\Total War ROME REMASTERED\VFS\Local\Rome\saves`).
- **Snapshot Versioning**: Never overwrite or lose past campaign states. Create timestamped snapshots or custom-labeled checkpoints (e.g. `Turn_50_Before_Civil_War`).
- **Full Restore with Safety Net**: Restore any previous snapshot directly into the game folder. Creates an automatic safety backup before overwriting active saves.
- **Compression**: Toggle between folder snapshots or `.zip` archives.
- **Retention Management**: Set maximum snapshots to keep (or unlimited).
- **Direct Explorer Integration**: Open active saves and backup folders in File Explorer.
- **CLI & Scripting Support**: Run unattended backups via flags (e.g., in scheduled tasks).

---

## Running the Application

### Desktop GUI (Recommended)

```bash
dotnet run --project "Rome Remastered Save Manager/RRM-SM.UI/RRM-SM.UI.csproj"
```

Or run the compiled executable:
```
Rome Remastered Save Manager/RRM-SM.UI/bin/Debug/net8.0-windows/RRM-SM.UI.exe
```

### Interactive CLI

```bash
dotnet run --project "Rome Remastered Save Manager/RRM-SM/RRM-SM.csproj"
```

### CLI Flags (Headless / Scripting)

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

On first launch, `config.json` is automatically created:

```json
{
  "GameSaveDirectory": "C:\\Users\\<Username>\\AppData\\Local\\Feral Interactive\\Total War ROME REMASTERED\\VFS\\Local\\Rome\\saves",
  "BackupDirectory": "C:\\Users\\<Username>\\Documents\\Rome Remastered Backups",
  "CompressBackups": false,
  "MaxBackupsToKeep": 0
}
```

All settings are configurable from both the GUI Settings tab and the CLI Settings menu.

---

## Solution Structure

```
Rome Remastered Save Manager/
├── RRM-SM.Core/          # Shared business logic (Models & Services)
├── RRM-SM.UI/            # WPF Desktop Application
├── RRM-SM/               # Interactive CLI & scripting entrypoint
└── RRM-SM.Tests/         # Automated xUnit tests
```

---

## Running Tests

```bash
dotnet test "Rome Remastered Save Manager/Rome Remastered Save Manager.sln"
```

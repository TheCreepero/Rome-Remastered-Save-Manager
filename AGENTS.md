# Repository Guidelines & Operational Rules

## 1. Solution & Build Commands
The Visual Studio solution file is located in the nested subdirectory `Rome Remastered Save Manager/`.
- Always pass the solution path when running `dotnet build` or `dotnet test`:
  ```powershell
  dotnet build "Rome Remastered Save Manager\Rome Remastered Save Manager.sln"
  dotnet test "Rome Remastered Save Manager\Rome Remastered Save Manager.sln"
  ```

## 2. Release Packaging & Distribution
- **Standalone Distribution Archive**: The release zip (`RomeRemasteredSaveManager-v1.0.0-win-x64.zip`) must contain **exclusively** the single-file self-contained executable (`RomeRemasteredSaveManager.exe`).
- **No Helper Scripts in Release Zip**: Do NOT include `Launch-GUI.bat` or other dev scripts in the public release zip; `Launch-GUI.bat` is strictly a developer convenience for local repository checkouts.
- **Automated Packaging**: Always use `Build-Release.ps1` to produce and package standalone releases.
- **Release Guardrail**: Do not create or draft GitHub releases or git tags unless explicitly requested by the user. Standard commits and pushes to `main` must not trigger release packaging unless commanded.

## 3. Testing, UI Automation & Process Lifecycle
- **PowerShell Runner**: When executing UI automation or PowerShell verification scripts, use `powershell` (Windows PowerShell 5.1). Do not assume `pwsh` (PowerShell 7) is on `PATH`.
- **System Tray & Mutex Lifecycle**:
  - `RRM-SM.UI` enforces a single-instance mutex (`RomeRemasteredSaveManager_SingleInstance_Mutex`) and minimizes to the system tray on close (`MinimizeToTray = true`).
  - Before building `RRM-SM.UI.csproj` or running UI automation (`tests/Verify-UIUsability.ps1`), terminate any existing `RRM-SM.UI` or `RomeRemasteredSaveManager` processes (`Get-Process RRM-SM* -ErrorAction SilentlyContinue | Stop-Process -Force`) to avoid MSBuild file lock errors (`MSB3021`) or mutex redirection to stale background instances.
  - Do NOT run UI executables synchronously with `-Wait` in non-interactive tasks, as the message loop will block execution.
- **Always Verify**: Run both `dotnet test "Rome Remastered Save Manager\Rome Remastered Save Manager.sln"` and `powershell -File "tests\Verify-UIUsability.ps1"` when modifying UI components or core logic.

## 4. WPF Accessibility & UI Standards
- **Automation Properties**: Every user-interactive control (Buttons, ComboBoxes, DataGrids, CheckBoxes, ListBoxes, TextBoxes) must include:
  - `AutomationProperties.Name`: Clear, screen-reader friendly identifier.
  - `AutomationProperties.HelpText`: Contextual shortcut/action hint.
  - `FocusVisualStyle`: Bind to `{StaticResource AccessibleFocusVisual}`.
- **Sync with Test Harness**: When UI controls or action bar buttons are added, modified, or removed, immediately update `tests/Verify-UIUsability.ps1` to reflect the expected element list.

## 5. Documentation & Style Invariants
- **Emoji Prohibition**: Zero emojis in all commits, documentation (`README.md`), and code comments.
- **Automated Verification**: Ensure all tests (specifically `Readme_ContainsNoEmojis`) pass with `dotnet test`.

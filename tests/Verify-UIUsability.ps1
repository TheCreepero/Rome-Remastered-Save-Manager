# Automated UI & Usability Verification Script for Rome: Remastered Save Manager
# Uses Windows UI Automation to inspect controls, names, help text, and accessibility properties.

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exePath = Join-Path $PSScriptRoot "..\Rome Remastered Save Manager\RRM-SM.UI\bin\Debug\net10.0-windows\RRM-SM.UI.exe"
if (-not (Test-Path $exePath)) {
    Write-Error "Could not find RRM-SM.UI.exe at: $exePath"
    exit 1
}

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host " STARTING AUTOMATED UI & USABILITY VERIFICATION TEST SUITE       " -ForegroundColor Cyan
Write-Host " Target: $exePath" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

$process = Start-Process -FilePath $exePath -PassThru
$passCount = 0
$failCount = 0

function Assert-Condition($condition, $message) {
    if ($condition) {
        Write-Host "  [PASS] $message" -ForegroundColor Green
        $script:passCount++
    } else {
        Write-Host "  [FAIL] $message" -ForegroundColor Red
        $script:failCount++
    }
}

try {
    # 1. Wait for window to appear
    Write-Host "`n[1/4] Connecting to Application Window..." -ForegroundColor Yellow
    $timeout = [DateTime]::Now.AddSeconds(10)
    $window = $null

    while ([DateTime]::Now -lt $timeout -and $null -eq $window) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.MainWindowHandle -ne 0) {
            try {
                $candidate = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
                if ($null -ne $candidate -and $candidate.Current.BoundingRectangle.Width -gt 200) {
                    $window = $candidate
                    break
                }
            } catch { }
        }

        $condition = New-Object Windows.Automation.PropertyCondition(
            [Windows.Automation.AutomationElement]::ProcessIdProperty,
            $process.Id
        )
        $all = [Windows.Automation.AutomationElement]::RootElement.FindAll(
            [Windows.Automation.TreeScope]::Children,
            $condition
        )
        foreach ($el in $all) {
            if ($el.Current.Name -like "*Total War: ROME REMASTERED*" -or $el.Current.BoundingRectangle.Width -ge 500) {
                $window = $el
                break
            }
        }
    }

    Assert-Condition ($null -ne $window) "Application window opened and was found by UI Automation"
    if ($null -eq $window) {
        Write-Error "Application window failed to load within timeout."
        exit 1
    }

    # 2. Window Title & Dimensions
    Write-Host "`n[2/4] Verifying Window Properties & Minimum Dimensions..." -ForegroundColor Yellow
    $windowName = $window.Current.Name
    Assert-Condition ($windowName -like "*Total War: ROME REMASTERED*") "Window title matches: '$windowName'"

    $bounds = $window.Current.BoundingRectangle
    $width = $bounds.Width
    $height = $bounds.Height
    Assert-Condition ($width -ge 750) "Window Width ($width px) satisfies minimum threshold (750 px)"
    Assert-Condition ($height -ge 500) "Window Height ($height px) satisfies minimum threshold (500 px)"

    # 3. Inspect UI Automation Tree for Controls & Accessibility Properties
    Write-Host "`n[3/4] Inspecting UI Controls, Automation Properties & Access Keys..." -ForegroundColor Yellow

    # Helper to find elements by Name or ControlType
    function Find-Descendants($root, $controlType) {
        $cond = New-Object Windows.Automation.PropertyCondition(
            [Windows.Automation.AutomationElement]::ControlTypeProperty,
            $controlType
        )
        return $root.FindAll([Windows.Automation.TreeScope]::Descendants, $cond)
    }

    # Verify buttons
    $buttons = Find-Descendants $window ([Windows.Automation.ControlType]::Button)
    Write-Host "  Found $($buttons.Count) button controls in window tree." -ForegroundColor Gray
    
    $expectedButtons = @(
        "Launch Game via Steam",
        "Toggle Autosave Sentinel",
        "Quick Backup",
        "Backup All Campaigns",
        "Restore Selected Backup",
        "Pin or Unpin Backup Milestone",
        "Edit Save Details",
        "Clean Unpinned Sentinel Saves",
        "Delete Selected Backup",
        "Open Selected Backup in File Explorer",
        "Refresh Backups List"
    )

    foreach ($expected in $expectedButtons) {
        $found = $false
        foreach ($btn in $buttons) {
            if ($btn.Current.Name -eq $expected -or $btn.Current.Name -like "*$expected*") {
                $found = $true
                $accessKey = $btn.Current.AccessKey
                $helpText = $btn.Current.HelpText
                Assert-Condition $true "Button '$expected' found with Name='$($btn.Current.Name)' | AccessKey='$accessKey' | HelpText='$helpText'"
                break
            }
        }
        if (-not $found) {
            Assert-Condition $false "Button '$expected' was not found with expected AutomationProperties.Name"
        }
    }

    # Verify comboboxes
    $combos = Find-Descendants $window ([Windows.Automation.ControlType]::ComboBox)
    Write-Host "  Found $($combos.Count) ComboBox controls in window tree." -ForegroundColor Gray
    
    $expectedCombos = @(
        "Active Campaign Faction",
        "Filter Backups by Faction"
    )

    foreach ($expected in $expectedCombos) {
        $found = $false
        foreach ($cmb in $combos) {
            if ($cmb.Current.Name -like "*$expected*") {
                $found = $true
                $helpText = $cmb.Current.HelpText
                Assert-Condition $true "ComboBox '$expected' found with Name='$($cmb.Current.Name)' | HelpText='$helpText'"
                break
            }
        }
        if (-not $found) {
            Assert-Condition $false "ComboBox '$expected' was not found with expected AutomationProperties.Name"
        }
    }

    # Verify Search Box
    $edits = Find-Descendants $window ([Windows.Automation.ControlType]::Edit)
    $searchFound = $false
    foreach ($edit in $edits) {
        if ($edit.Current.Name -like "*Search Backups*") {
            $searchFound = $true
            Assert-Condition $true "Search input found with Name='$($edit.Current.Name)' | HelpText='$($edit.Current.HelpText)'"
            break
        }
    }
    Assert-Condition $searchFound "Search input was found with AutomationProperties.Name"

    # Verify DataGrid
    $grids = Find-Descendants $window ([Windows.Automation.ControlType]::DataGrid)
    $gridFound = $false
    foreach ($grid in $grids) {
        if ($grid.Current.Name -like "*Backups List*") {
            $gridFound = $true
            Assert-Condition $true "DataGrid found with Name='$($grid.Current.Name)' | HelpText='$($grid.Current.HelpText)'"
            break
        }
    }
    Assert-Condition $gridFound "Backups DataGrid was found with AutomationProperties.Name"

    # Verify Status bar
    $texts = Find-Descendants $window ([Windows.Automation.ControlType]::Text)
    $statusFound = $false
    foreach ($txt in $texts) {
        if ($txt.Current.Name -like "*Status*") {
            $statusFound = $true
            Assert-Condition $true "Status message found with Name='$($txt.Current.Name)'"
            break
        }
    }
    Assert-Condition $statusFound "Status message element was found with AutomationProperties.Name"

    # 4. Tab Navigation & Settings Tab Inspection
    Write-Host "`n[4/5] Testing Tab Navigation & Settings Controls..." -ForegroundColor Yellow
    $tabItems = Find-Descendants $window ([Windows.Automation.ControlType]::TabItem)
    $settingsTab = $null
    foreach ($tab in $tabItems) {
        if ($tab.Current.Name -like "*Settings*") {
            $settingsTab = $tab
            break
        }
    }
    Assert-Condition ($null -ne $settingsTab) "Settings Tab was found in UI"
    if ($null -ne $settingsTab) {
        try {
            $selPattern = $settingsTab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)
            $selPattern.Select()
            Start-Sleep -Milliseconds 400
            Assert-Condition $true "Selected Settings Tab successfully"

            # Verify settings controls
            $settingsButtons = Find-Descendants $window ([Windows.Automation.ControlType]::Button)
            $saveSettingsFound = $false
            foreach ($b in $settingsButtons) {
                if ($b.Current.Name -like "*Save Settings*") {
                    $saveSettingsFound = $true
                    Assert-Condition $true "Button 'Save Settings' found with Name='$($b.Current.Name)'"
                    break
                }
            }
            Assert-Condition $saveSettingsFound "Save Settings button was found on Settings tab"

            # Verify Automation & Desktop checkboxes
            $checkBoxes = Find-Descendants $window ([Windows.Automation.ControlType]::CheckBox)
            $expectedCheckBoxes = @(
                "Enable Autosave Sentinel background watcher",
                "Show Windows notifications on automated backup",
                "Minimize or close application to System Tray"
            )

            foreach ($expectedCb in $expectedCheckBoxes) {
                $cbFound = $false
                foreach ($cb in $checkBoxes) {
                    if ($cb.Current.Name -like "*$expectedCb*") {
                        $cbFound = $true
                        Assert-Condition $true "CheckBox '$expectedCb' found with Name='$($cb.Current.Name)'"
                        break
                    }
                }
                Assert-Condition $cbFound "CheckBox '$expectedCb' was found on Settings tab"
            }

            # Verify Debounce buffer edit box
            $settingsEdits = Find-Descendants $window ([Windows.Automation.ControlType]::Edit)
            $debounceFound = $false
            foreach ($ed in $settingsEdits) {
                if ($ed.Current.Name -like "*debounce buffer*") {
                    $debounceFound = $true
                    Assert-Condition $true "Edit control found with Name='$($ed.Current.Name)'"
                    break
                }
            }
            Assert-Condition $debounceFound "Debounce buffer edit control was found on Settings tab"

            # Verify Save Vault Maintenance tools
            $maintenanceButtons = @(
                "Rebuild Vault Index",
                "Rebuild Campaign Assignments",
                "Clean Unpinned Sentinel Saves (Settings)"
            )
            foreach ($expectedMb in $maintenanceButtons) {
                $mbFound = $false
                foreach ($b in $settingsButtons) {
                    if ($b.Current.Name -like "*$expectedMb*") {
                        $mbFound = $true
                        Assert-Condition $true "Maintenance button '$expectedMb' found with Name='$($b.Current.Name)'"
                        break
                    }
                }
                Assert-Condition $mbFound "Maintenance button '$expectedMb' was found on Settings tab"
            }

        } catch {
            Assert-Condition $false "Failed to interact with Settings Tab: $_"
        }
    }

    # 5. Chronicle Tab Inspection
    Write-Host "`n[5/6] Testing Campaign Chronicle Tab Navigation & Controls..." -ForegroundColor Yellow
    $chronicleTab = $null
    foreach ($tab in $tabItems) {
        if ($tab.Current.Name -like "*Chronicle*") {
            $chronicleTab = $tab
            break
        }
    }
    Assert-Condition ($null -ne $chronicleTab) "Campaign Chronicle Tab was found in UI"
    if ($null -ne $chronicleTab) {
        try {
            $selPattern = $chronicleTab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)
            $selPattern.Select()
            Start-Sleep -Milliseconds 400
            Assert-Condition $true "Selected Campaign Chronicle Tab successfully"

            # Check Chronicle controls
            $chronicleButtons = Find-Descendants $window ([Windows.Automation.ControlType]::Button)
            $expectedChronicleButtons = @(
                "Export Chronicle HTML Report",
                "Copy Chronicle Markdown Report",
                "Refresh Chronicle"
            )
            foreach ($expectedCb in $expectedChronicleButtons) {
                $cbFound = $false
                foreach ($b in $chronicleButtons) {
                    if ($b.Current.Name -like "*$expectedCb*") {
                        $cbFound = $true
                        Assert-Condition $true "Chronicle button '$expectedCb' found with Name='$($b.Current.Name)'"
                        break
                    }
                }
                Assert-Condition $cbFound "Chronicle button '$expectedCb' was found on Chronicle tab"
            }

            # Check Timeline ListBox
            $chronicleLists = Find-Descendants $window ([Windows.Automation.ControlType]::List)
            $listFound = $false
            foreach ($l in $chronicleLists) {
                if ($l.Current.Name -like "*Chronicle Milestones Timeline*") {
                    $listFound = $true
                    Assert-Condition $true "Timeline ListBox found with Name='$($l.Current.Name)'"
                    break
                }
            }
            Assert-Condition $listFound "Timeline ListBox was found on Chronicle tab"

        } catch {
            Assert-Condition $false "Failed to interact with Chronicle Tab: $_"
        }
    }

    # 6. System Tray Minimization on Close
    Write-Host "`n[6/6] Testing System Tray Minimization & Protection on Window Close..." -ForegroundColor Yellow
    $windowPattern = $null
    try {
        $windowPattern = $window.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern)
    } catch { }

    if ($null -ne $windowPattern) {
        $windowPattern.Close()
    } else {
        $process.CloseMainWindow()
    }

    Start-Sleep -Milliseconds 800
    # Process should NOT have terminated (protected by MinimizeToTray)
    Assert-Condition (-not $process.HasExited) "Process remained alive after window close (MinimizeToTray active)"
    Assert-Condition ($window.Current.IsOffscreen) "Window is hidden from view into System Tray"

} finally {
    if (-not $process.HasExited) {
        Write-Host "  Terminating remaining test process..." -ForegroundColor Gray
        $process.Kill()
        $process.WaitForExit(2000)
    }
}

Write-Host "`n=================================================================" -ForegroundColor Cyan
Write-Host " AUTOMATED TEST RESULTS: $passCount PASSED, $failCount FAILED" -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Red" })
Write-Host "=================================================================" -ForegroundColor Cyan

if ($failCount -gt 0) {
    exit 1
} else {
    exit 0
}

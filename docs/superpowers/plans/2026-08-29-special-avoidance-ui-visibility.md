# Special Avoidance UI Visibility Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the independent special dual-arm avoidance setting visible in Parameter 1 without hiding jig-installation protection settings.

**Architecture:** Restore the five damaged FrSys UI files to the committed source selected by the user. Move the dynamically created dual-arm safety-distance group to a new scrollable row below existing Parameter 1 controls, expand the tab's scroll extent, and explicitly bring the group to the front.

**Tech Stack:** C# WinForms, PowerShell source-level regression scripts, Git.

---

### Task 1: Add the failing layout regression

**Files:**
- Create: `tests/special-avoidance-ui-visibility-regression.ps1`
- Test: `tests/special-avoidance-ui-visibility-regression.ps1`

- [ ] **Step 1: Write the failing test**

```powershell
$ErrorActionPreference = 'Stop'
$dualArmSource = & rg -n -A 55 --fixed-strings 'private void InitDualArmSafetyDistanceControls()' 'UI/FormView/FrSys.DualArmSafetyDistance.cs'
$designerSource = & rg -n --fixed-strings 'this.tp_setparm_1.AutoScrollMinSize = new System.Drawing.Size(1098, 1015);' 'UI/FormView/FrSys.Designer.cs'

if ($dualArmSource -notmatch 'Location = new Point\(435, 895\);') { throw 'The dual-arm safety group must use the new scroll row.' }
if ($dualArmSource -notmatch 'groupBoxDualArmSafetyDistance\.BringToFront\(\);') { throw 'The dual-arm safety group must explicitly be brought to the front.' }
if ([string]::IsNullOrWhiteSpace($designerSource)) { throw 'Parameter 1 scroll range must include the dual-arm safety group.' }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `powershell -ExecutionPolicy Bypass -File tests\special-avoidance-ui-visibility-regression.ps1`

Expected: failure because the group is at `(590, 704)`, has no explicit `BringToFront`, and Parameter 1 scroll height is `882`.

### Task 2: Restore UI source and move the group

**Files:**
- Restore: `UI/FormView/FrSys.Designer.cs`
- Restore: `UI/FormView/FrSys.DualArmSafetyDistance.cs`
- Restore: `UI/FormView/FrSys.EnvironmentSchedule.cs`
- Restore: `UI/FormView/FrSys.OtpDarkInspection.cs`
- Restore: `UI/FormView/FrSys.cs`
- Modify: `UI/FormView/FrSys.Designer.cs:1401-1403`
- Modify: `UI/FormView/FrSys.DualArmSafetyDistance.cs:24-30,64-69`

- [ ] **Step 1: Restore the user-approved UI files from HEAD**

```powershell
git restore --source=HEAD -- `
  UI/FormView/FrSys.Designer.cs `
  UI/FormView/FrSys.DualArmSafetyDistance.cs `
  UI/FormView/FrSys.EnvironmentSchedule.cs `
  UI/FormView/FrSys.OtpDarkInspection.cs `
  UI/FormView/FrSys.cs
```

Expected: the five files no longer appear as modified and their committed Chinese UI text is restored.

- [ ] **Step 2: Move and raise the dynamic group**

```csharp
groupBoxDualArmSafetyDistance.Location = new Point(435, 895);
// ... add the four child controls ...
tp_setparm_1.Controls.Add(groupBoxDualArmSafetyDistance);
groupBoxDualArmSafetyDistance.BringToFront();
```

Change the designer scroll extent to:

```csharp
this.tp_setparm_1.AutoScrollMinSize = new System.Drawing.Size(1098, 1015);
```

### Task 3: Verify and commit

**Files:**
- Test: `tests/special-avoidance-ui-visibility-regression.ps1`
- Modify: `UI/FormView/FrSys.Designer.cs`
- Modify: `UI/FormView/FrSys.DualArmSafetyDistance.cs`

- [ ] **Step 1: Run the new regression**

Run: `powershell -ExecutionPolicy Bypass -File tests\special-avoidance-ui-visibility-regression.ps1`

Expected: `PASS: special avoidance controls are visible in the Parameter 1 scroll area.`

- [ ] **Step 2: Run all regressions and whitespace validation**

Run:

```powershell
Get-ChildItem tests -Filter *.ps1 | Sort-Object Name | ForEach-Object { & powershell -ExecutionPolicy Bypass -File $_.FullName; if ($LASTEXITCODE -ne 0) { throw $_.Name } }
git diff --check
```

Expected: every available regression script passes and `git diff --check` is silent.

- [ ] **Step 3: Commit only the UI visibility repair**

```powershell
git add UI/FormView/FrSys.Designer.cs UI/FormView/FrSys.DualArmSafetyDistance.cs tests/special-avoidance-ui-visibility-regression.ps1
git commit -m "修复：显示特殊避障设置"
```

Expected: no pre-existing deleted assets or tests are staged.

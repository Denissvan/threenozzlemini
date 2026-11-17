param([string]$RepoRoot = (Split-Path -Parent $PSScriptRoot), [switch]$DoorChecks, [switch]$UiChecks)
$ErrorActionPreference = 'Stop'
# Compile the real application policy and actual axis/UI decision snippets with IO-only stubs.
# No controller DLL is loaded and no hardware commands are issued.
$mt = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $RepoRoot 'UI/Class/MT.cs')
$axis = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $RepoRoot 'MotionCtrl/AXIS.cs')
$card = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $RepoRoot 'MotionCtrl/CARD.cs')
$form = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $RepoRoot 'UI/FormView/FrSafetyDebugMode.cs')
$policy = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $RepoRoot 'UI/Class/SafetyDebugMode.cs')
$cards = [regex]::Matches($mt, '(?m)^\s*public static CARD CARD_ECI(?:2400|2600)_[0-3] = .*;\r?$').Value -join "`n"
$inputs = [regex]::Matches($mt, '(?m)^\s*public static GPIO GPIO_IN_EMG[0-3] = .*;\r?$').Value -join "`n"
$axisDecision = [regex]::Match($axis, '(?m)^\s*if \(!IsZmcBusEnableCard && card.GetIOFrBuff\(0, GPIO.IO_DIR.IN\).*iosta \|= MTIO.EMG;').Value
$defaultPolarity = [regex]::Match($card, 'public GPIO.IO_STA EmgActiveState = GPIO.IO_STA.IN_OFF;').Value
if (!$defaultPolarity) { throw 'Default board emergency polarity must remain IN_OFF' }
$rawRead = [regex]::Match($form, '(?s)private static bool ReadPressed\(GPIO input, out GPIO.IO_STA raw\).*?(?=\s*private void tmr_refresh_Tick)').Value
if (!$cards -or !$inputs -or !$axisDecision -or !$rawRead) { throw 'Required production decision snippets not found' }
$rawRead = $rawRead.Replace('private static bool ReadPressed', 'public static bool ReadPressed')
$harness = @"
using System;
using System.Collections.Generic;
using System.Threading;
using MotionCtrl;
namespace MotionCtrl {
    public enum EM_RES { OK }
    public class CARD {
        public enum BRAND { ZMOTION }
        public enum TYPE { MOTION }
        public CARD(params object[] args) { }
        $defaultPolarity
        public GPIO.IO_STA Input;
        public GPIO.IO_STA GetIOFrBuff(int n, GPIO.IO_DIR d) { return Input; }
    }
    public class GPIO {
        public enum IO_STA { IN_OFF, IN_ON, ERR }
        public enum IO_DIR { IN }
        public enum IO_TYPE { MT_CARD }
        public CARD card;
        public string str_disc;
        public GPIO(int n, CARD c, IO_DIR d, IO_TYPE t, string name, string english) { card=c; str_disc=name; }
        public IO_STA Status { get { return card.Input; } }
        public bool isOFF { get { return Status == IO_STA.IN_OFF; } }
    }
}
namespace UI {
    public static class MT { $cards $inputs }
    public enum EM_SYS_STA { STANDBY, RUN, PAUSE }
    public class SystemState { public EM_SYS_STA status; }
    public static class VAR { public static SystemState gsys_set = new SystemState(); public static Msg msg = new Msg(); }
    public class Msg { public enum EM_MSGTYPE { WAR } public void AddMsg(EM_MSGTYPE t, string s) { } }
    public static class BaseAction { public static bool bRuning, brun; }
    public static class WS { public static int DemoResultMode; }
    public static class COM { public static UpDownLoad UDLoad1, UDLoad2; public static EM_RES ThreeNozzleResetWithoutHome() { throw new InvalidOperationException("Hardware actions forbidden in offline test"); } }
    public class Cam { public class VisionTask { public string TaskName; } public List<VisionTask> List_vs_task; }
    public class UpDownLoad {
        public Cam upcam;
        public static bool brun, ThreeNozzleBlindTrayPickEnabled;
        public enum ThreeNozzlePrePlaceFlyMode { Off, Compare, Use }
        public static ThreeNozzlePrePlaceFlyMode ThreeNozzlePrePlaceFly;
        public static string ThreeNozzlePrePlaceFlyTask = "WsUp_Shp";
        public static bool ThreeNozzleFlyActive { get { return false; } }
        public static string ThreeNozzleFlyDisabledReason() { return "offline test"; }
    }
    public static partial class PT_SET {
        public enum RUN_PATTERN { RUN_EMPTY, RUN_UPDW }
        public static bool LbDebugMasked;
        public static bool LbEnConfigured = true;
        public static bool LbEn { get { return LbEnConfigured && !LbDebugMasked; } }
        public static void SetLbDebugMask(bool masked) { LbDebugMasked = masked; }
    }
    public static class EmgPolarityHarness {
        enum MTIO { EMG = 8 }
        static bool AxisPressed(CARD card, bool IsZmcBusEnableCard = false) { MTIO iosta = 0; $axisDecision return (iosta & MTIO.EMG) != 0; }
        $rawRead
        static bool ReadPressed(GPIO input) { GPIO.IO_STA raw; return ReadPressed(input, out raw); }
        static void Equal(bool actual, bool expected, string label) {
            if (actual != expected) throw new Exception(label + ": expected=" + expected + ", actual=" + actual);
        }
        public static void Run() {
            int checks = 0;
            foreach (var pair in SafetyDebugMode.EmgInputs) {
                foreach (GPIO.IO_STA state in new[] { GPIO.IO_STA.IN_ON, GPIO.IO_STA.IN_OFF }) {
                    pair.Value.card.Input = state;
                    bool expected = state == GPIO.IO_STA.IN_ON;
                    Equal(AxisPressed(pair.Value.card), expected, pair.Key + " axis " + state); checks++;
                    Equal(SafetyDebugMode.IsPressed(pair.Value, pair.Key), expected, pair.Key + " system " + state); checks++;
                    Equal(ReadPressed(pair.Value), expected, pair.Key + " UI " + state); checks++;
                    GPIO.IO_STA raw;
                    Equal(ReadPressed(pair.Value, out raw), expected, pair.Key + " sampled decision " + state); checks++;
                    Equal(raw == state, true, pair.Key + " raw IO sample " + state); checks++;
                }
                pair.Value.card.Input = GPIO.IO_STA.ERR;
                GPIO.IO_STA invalid;
                ReadPressed(pair.Value, out invalid);
                Equal(invalid == GPIO.IO_STA.ERR, true, pair.Key + " read error preserved for UI"); checks++;
            }
            string error;
            foreach (var pair in SafetyDebugMode.EmgInputs) {
                pair.Value.card.Input = GPIO.IO_STA.IN_ON;
                if (!SafetyDebugMode.Enable(pair.Key, "test", out error)) throw new Exception(error);
                Equal(SafetyDebugMode.IsPressed(pair.Value, pair.Key), false, pair.Key + " masked system"); checks++;
                Equal(ReadPressed(pair.Value), true, pair.Key + " UI preserves raw triggered state when masked"); checks++;
                Equal(AxisPressed(pair.Value.card), true, pair.Key + " debug mask must not erase axis EMG"); checks++;
                SafetyDebugMode.Disable("test");
                Equal(SafetyDebugMode.IsPressed(pair.Value, pair.Key), true, pair.Key + " restored system"); checks++;
            }
            foreach (GPIO.IO_STA state in new[] { GPIO.IO_STA.IN_ON, GPIO.IO_STA.IN_OFF }) {
                var zmc = new CARD(); zmc.Input = state;
                Equal(AxisPressed(zmc, true), false, "ZMC432 IN0 is vacuum, not emergency " + state); checks++;
            }
            Console.WriteLine("PASS: " + checks + " emergency input polarity checks (no hardware).");
        }
    }
}
"@
$doorSource = ''
if ($DoorChecks -or $UiChecks) {
    $gates = ''
    foreach ($name in @('UpDownDr', 'TrayDr', 'RBoxDr', 'LBoxDr')) {
        $line = [regex]::Match($mt, '(?m)^\s*if \(([^\r\n]*PT_SET\.bEn' + $name + '[^\r\n]*)\)\r?$')
        if (!$line.Success) { throw "Door gate missing: $name" }
        $gates += "public static bool Gate$name(int ChkEn) { return " + $line.Groups[1].Value + "; }`n"
    }
    $doorSource = (Get-Content -Raw -Encoding UTF8 (Join-Path $PSScriptRoot 'DoorDebugHarness.cs')).Replace('/*ACTUAL_DOOR_GATES*/', $gates).Replace('public static class PT_SET', 'public static partial class PT_SET')
}
$source = $harness + "`n" + ($policy -replace '(?m)^using .*;\r?$', '') + "`n" + $doorSource
if ($UiChecks) {
    $source = "using System.Windows.Forms;`nusing System.Drawing;`n" + $source
    $source += "`n" + ($form -replace '(?m)^using .*;\r?$', '')
    $source += "`n" + (Get-Content -Raw -Encoding UTF8 (Join-Path $RepoRoot 'UI/FormView/FrSafetyDebugMode.Designer.cs'))
    $source += "`n" + (Get-Content -Raw -Encoding UTF8 (Join-Path $PSScriptRoot 'DoorDebugUiHarness.cs'))
    Add-Type -TypeDefinition $source -ReferencedAssemblies System.Windows.Forms,System.Drawing
} else { Add-Type -TypeDefinition $source }
[UI.EmgPolarityHarness]::Run()
if ($DoorChecks -or $UiChecks) { [UI.DoorDebugHarness]::Run() }
if ($UiChecks) { [UI.DoorDebugUiHarness]::Run((Join-Path $env:TEMP 'mini-door-debug-preview.png')) }

param([string]$RepoRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$source = Join-Path $RepoRoot 'UI/Class/ThreeNozzleSequence.cs'
if (!(Test-Path -LiteralPath $source)) { throw 'FAIL: production ThreeNozzleSequence implementation is missing.' }
Add-Type -TypeDefinition ((Get-Content -LiteralPath $source -Raw) + @'
namespace SequenceTests {
using System;
using System.Collections.Generic;
using UI;
public static class ThreeNozzleSequenceRegression
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static string Run(ThreeNozzleSequence sequence)
    {
        var actions = new List<string>();
        while (!sequence.IsComplete)
            sequence.Step(a => { actions.Add(a.ToString()); return true; });
        return String.Join(",", actions.ToArray());
    }
    public static void RunAll()
    {
        var normal = new ThreeNozzleSequence(true, true, true, true);
        Check(Run(normal) == "PrepareIncoming,WaitFixtureReady,PickA,PlaceA,PickB,PlaceB,LeaveFixture,UnloadFinished", "Normal ordering");
        Check(normal.ExchangeDone && normal.OutsideFixture, "Completion requires successful leave");
        Check(normal.Step(a => { throw new Exception("Completed action replayed"); }), "Complete is idempotent");
        Check(Run(new ThreeNozzleSequence(false, true, false, true)) == "PrepareIncoming,WaitFixtureReady,PlaceA,PlaceB,LeaveFixture", "Empty fixture must not pick");
        Check(Run(new ThreeNozzleSequence(true, false, true, false)) == "WaitFixtureReady,PickA,PickB,LeaveFixture,UnloadFinished", "Drain must not prefetch or place");
        Check(Run(new ThreeNozzleSequence(false, false, true, true)) == "PrepareIncoming,WaitFixtureReady,PickB,PlaceB,LeaveFixture,UnloadFinished", "Disabled A must not execute A");
        Check(Run(new ThreeNozzleSequence(true, true, true, false)) == "PrepareIncoming,WaitFixtureReady,PickA,PlaceA,PickB,LeaveFixture,UnloadFinished", "Missing incoming B must not place B");
        var retry = new ThreeNozzleSequence(true, true, true, true);
        while (retry.Current != ThreeNozzleAction.PickB) retry.Step(a => true);
        Check(!retry.ExchangeDone && !retry.OutsideFixture, "Partial exchange is not released");
        Check(!retry.Step(a => false) && retry.Current == ThreeNozzleAction.PickB, "Failed PickB stays pending");
        Check(Run(retry) == "PickB,PlaceB,LeaveFixture,UnloadFinished", "Retry must not replay successful A");
        var leave = new ThreeNozzleSequence(false, true, false, false);
        while (leave.Current != ThreeNozzleAction.LeaveFixture) leave.Step(a => true);
        Check(leave.ExchangeDone && !leave.OutsideFixture, "Exchange does not imply outside");
        leave.Step(a => false);
        Check(!leave.OutsideFixture && !leave.IsComplete, "Failed leave must not release");
        var uncertain = new ThreeNozzleSequence(true, true, true, true);
        while (uncertain.Current != ThreeNozzleAction.PlaceA) uncertain.Step(a => true);
        Check(!uncertain.Step(a => { uncertain.RequireRecovery(); return true; }), "Uncertain success cannot advance");
        Check(uncertain.RecoveryRequired && uncertain.Current == ThreeNozzleAction.PlaceA, "Uncertain action retained");
        Check(!uncertain.Step(a => { throw new Exception("Recovery gate bypassed"); }), "Recovery blocks callbacks");
        uncertain.ResolveRecovery(true);
        Check(uncertain.Current == ThreeNozzleAction.PickB && !uncertain.RecoveryRequired, "Confirmed committed action advances once");
        uncertain.RequireRecovery();
        uncertain.ResolveRecovery(false);
        Check(uncertain.Current == ThreeNozzleAction.PickB, "Confirmed uncommitted action retries");
        try { uncertain.Step(a => { throw new InvalidOperationException("injected"); }); }
        catch (InvalidOperationException) { }
        Check(uncertain.RecoveryRequired && uncertain.Current == ThreeNozzleAction.PickB, "Exception must latch uncertainty");
        var noRecovery = new ThreeNozzleSequence(false, true, false, false);
        bool rejected = false;
        try { noRecovery.ResolveRecovery(true); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && noRecovery.Current == ThreeNozzleAction.PrepareIncoming, "Cannot skip action using recovery without latch");
        Check(ThreeNozzleSequence.IncomingNozzle(0) == 1 && ThreeNozzleSequence.IncomingNozzle(1) == 2, "Incoming mapping");
        Check(ThreeNozzleSequence.FinishedNozzle(0) == 0 && ThreeNozzleSequence.FinishedNozzle(1) == 1, "Finished mapping");
        rejected = false;
        try { ThreeNozzleSequence.IncomingNozzle(2); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Invalid pair slot rejected");
    }
}
}
'@)
[SequenceTests.ThreeNozzleSequenceRegression]::RunAll()
Write-Host 'PASS: real three-nozzle sequence ordering, retry, drain, empty/single slots and recovery gates.'

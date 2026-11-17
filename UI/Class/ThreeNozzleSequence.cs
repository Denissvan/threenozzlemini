using System;
using System.Collections.Generic;

namespace UI
{
    public enum ThreeNozzleAction
    {
        PrepareIncoming,
        WaitFixtureReady,
        PickA,
        PlaceA,
        PickB,
        PlaceB,
        LeaveFixture,
        UnloadFinished,
        Complete
    }

    /// <summary>
    /// Single-executor progress for one two-pocket exchange. This class does not
    /// grant station/index permissions or own material data. The executor must
    /// verify each transfer and commit its material data before returning true.
    /// Prepare/unload callbacks must retain their own per-nozzle progress.
    /// </summary>
    public sealed class ThreeNozzleSequence
    {
        private readonly List<ThreeNozzleAction> actions = new List<ThreeNozzleAction>();
        private readonly int leaveIndex;
        private int next;
        private bool executing;

        public ThreeNozzleSequence(bool removeA, bool loadA, bool removeB, bool loadB)
        {
            if (loadA || loadB) actions.Add(ThreeNozzleAction.PrepareIncoming);
            actions.Add(ThreeNozzleAction.WaitFixtureReady);
            if (removeA) actions.Add(ThreeNozzleAction.PickA);
            if (loadA) actions.Add(ThreeNozzleAction.PlaceA);
            if (removeB) actions.Add(ThreeNozzleAction.PickB);
            if (loadB) actions.Add(ThreeNozzleAction.PlaceB);
            leaveIndex = actions.Count;
            actions.Add(ThreeNozzleAction.LeaveFixture);
            if (removeA || removeB) actions.Add(ThreeNozzleAction.UnloadFinished);
        }

        public ThreeNozzleAction Current { get { return IsComplete ? ThreeNozzleAction.Complete : actions[next]; } }
        public bool ExchangeDone { get { return next >= leaveIndex; } }
        public bool OutsideFixture { get { return next > leaveIndex; } }
        public bool IsComplete { get { return next == actions.Count; } }
        public bool RecoveryRequired { get; private set; }

        /// <summary>
        /// Execute at most one action. False means a known incomplete action and
        /// permits retry; uncertain physical outcomes MUST call RequireRecovery.
        /// Exceptions latch recovery before propagating to the existing alarm path.
        /// Check cancellation before invoking Step; cancellation never clears progress.
        /// </summary>
        public bool Step(Func<ThreeNozzleAction, bool> execute)
        {
            if (execute == null) throw new ArgumentNullException("execute");
            if (executing) throw new InvalidOperationException("A sequence action is already executing.");
            if (RecoveryRequired) return false;
            if (IsComplete) return true;
            executing = true;
            try
            {
                if (!execute(Current) || RecoveryRequired) return false;
                next++;
                return true;
            }
            catch
            {
                RecoveryRequired = true;
                throw;
            }
            finally
            {
                executing = false;
            }
        }

        public void RequireRecovery()
        {
            RecoveryRequired = true;
        }

        /// <summary>
        /// Call only after reconciling physical positions and material records.
        /// True confirms the entire current action, including its data commit.
        /// False confirms it is safe to invoke the same action again.
        /// </summary>
        public void ResolveRecovery(bool actionCommitted)
        {
            if (executing || !RecoveryRequired)
                throw new InvalidOperationException("Recovery requires a stopped action and an active recovery latch.");
            if (actionCommitted && !IsComplete) next++;
            RecoveryRequired = false;
        }

        public static int IncomingNozzle(int pairSlot)
        {
            ValidatePairSlot(pairSlot);
            return pairSlot + 1;
        }

        public static int FinishedNozzle(int pairSlot)
        {
            ValidatePairSlot(pairSlot);
            return pairSlot;
        }

        private static void ValidatePairSlot(int pairSlot)
        {
            if (pairSlot < 0 || pairSlot > 1) throw new ArgumentOutOfRangeException("pairSlot");
        }
    }
}

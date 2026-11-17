using System;
using System.Collections.Generic;
using System.Linq;
using MotionCtrl;
using UI.ModuleCheck;

namespace UI
{
    public partial class UpDownLoad
    {
        // Modes are captured once per station visit. A clear request may turn on,
        // but changing the selected nozzle or mode while holding parts is not safe.
        private enum ThreeNozzleMode { Exchange, GrrReload, SingleNozzle }
        private ThreeNozzleMode threeNozzleMode;
        private readonly List<WS.MdDat> threeNozzleModePockets = new List<WS.MdDat>();
        private int threeNozzleModeCursor;
        private int threeNozzleModeStage;
        private int threeNozzleSelected;
        private bool threeNozzleModeDone;
        private int threeNozzleGrrPendingResult;
        private ThreeNozzleRuntimePair threeNozzleModePair;
        private readonly Queue<WS.MdDat> threeNozzleGrrEmpty = new Queue<WS.MdDat>();
        private readonly Dictionary<WS.MdDat, int> threeNozzleGrrReloadResults = new Dictionary<WS.MdDat, int>();

        private bool ThreeNozzleHasSpecialMode { get { return threeNozzleMode != ThreeNozzleMode.Exchange; } }

        private bool ThreeNozzleCanLoadPocket(WS.MdDat md)
        {
            // GetWsOrderPosTeam excludes already-passed auto-inspection pockets.
            return md.benable && !(VAR.isAutoChkMode && md.bAutoChkOk);
        }

        private bool ThreeNozzleModeOwnsPocket(int index)
        {
            return FindMod != EM_FIN_MOD.NONE &&
                !(FindMod == EM_FIN_MOD.LOW && index % 8 >= 4) &&
                !(FindMod == EM_FIN_MOD.HIGH && index % 8 < 4);
        }

        private void ThreeNozzleResetModesRuntime()
        {
            threeNozzleMode = ThreeNozzleMode.Exchange;
            threeNozzleModePockets.Clear();
            threeNozzleGrrReloadResults.Clear();
            threeNozzleGrrEmpty.Clear();
            threeNozzleModeCursor = 0;
            threeNozzleModeStage = 0;
            threeNozzleModePair = null;
            threeNozzleModeDone = false;
        }

        private void ThreeNozzleInitializeMode(WS ws)
        {
            ThreeNozzleResetModesRuntime();
            bool single = id == 0 ? PT_SET.bUpDn1XtOnlyOne : PT_SET.bUpDn2XtOnlyOne;
            // Initial GRR feed and final clear use the usual tray route. Repeated
            // GRR visits rotate the same batch without charging production counts.
            // 模组自动点检整批换料：复用 GRR 换位动作，只在本臂负责组内有样品的穴之间循环（N1 暂存组首，N2 依次前移）
            bool moduleCheckSwap = ModuleCheckHooks.IsSwapping(ws) && !VAR.ClearMt;
            if (moduleCheckSwap)
                threeNozzleMode = ThreeNozzleMode.GrrReload;
            else if (PT_SET.bGrrFlow && ws.GrrUDLcnt > 0 && !VAR.ClearMt)
                threeNozzleMode = ThreeNozzleMode.GrrReload;
            else if (single)
                threeNozzleMode = ThreeNozzleMode.SingleNozzle;
            if (!ThreeNozzleHasSpecialMode) return;
            threeNozzleSelected = PT_SET.xt1firsten ? 0 : 1;
            for (int i = 0; i < ws.list_md.Count; i++)
            {
                // GRR uses ascending pocket order; single-nozzle production uses
                // the same column order as the old GetWsPosTeam selector.
                int n = threeNozzleMode == ThreeNozzleMode.GrrReload ? i :
                    (i % 2 == 0 ? i - i / 2 : i + (ws.list_md.Count - 1 - i) / 2);
                if (!ThreeNozzleModeOwnsPocket(n)) continue;
                WS.MdDat md = ws.list_md[n];
                if (threeNozzleMode == ThreeNozzleMode.GrrReload)
                {
                    if (!md.benable) continue;
                    if (moduleCheckSwap)
                    {
                        // 只排有样品的穴，不把空穴放进换位队列，保证严格按组内循环映射
                        if (md.res >= 0 && !string.IsNullOrEmpty(md.ModuleCheckSampleId)) threeNozzleModePockets.Add(md);
                        continue;
                    }
                    if (md.res >= 0) threeNozzleModePockets.Add(md);
                    else if (md.res == (int)Product.EM_CM_RES.EMPTY && ThreeNozzleCanLoadPocket(md))
                        threeNozzleGrrEmpty.Enqueue(md);
                }
                else if (md.res >= 0 || (md.res == (int)Product.EM_CM_RES.EMPTY && ThreeNozzleCanLoadPocket(md)))
                    threeNozzleModePockets.Add(md);
            }
        }

        private EM_RES ThreeNozzleRunSpecialMode(ref bool quit, WS ws)
        {
            if (threeNozzleModeDone) return EM_RES.OK;
            if (ws.FeedStatus != WS.EM_STA.REDAYFORUPDOWNLOAD && !Demo) return EM_RES.ABORT;
            if (threeNozzleDrain && !VAR.ClearMt)
                return ThreeNozzleError("清料过程中取消清料，须先完成本轮", true);
            EM_RES result = threeNozzleMode == ThreeNozzleMode.GrrReload
                ? ThreeNozzleRunGrrReload(ref quit) : ThreeNozzleRunSingle(ref quit);
            if (result == EM_RES.OK)
            {
                result = GoZero(ref quit);
                if (result == EM_RES.OK) threeNozzleModeDone = true;
            }
            return result;
        }

        private ThreeNozzleRuntimePair ThreeNozzleSinglePair(WS.MdDat md, int nozzle)
        {
            var pair = new ThreeNozzleRuntimePair();
            pair.Pockets[0] = md;
            pair.Incoming[0] = true;
            pair.IncomingNozzles[0] = nozzle;
            pair.FinishedNozzles[0] = nozzle;
            return pair;
        }

        private EM_RES ThreeNozzleRunSingle(ref bool quit)
        {
            XT xt = list_xt[threeNozzleSelected];
            while (threeNozzleModeCursor < threeNozzleModePockets.Count)
            {
                if (quit || VAR.gsys_set.bquit) return EM_RES.QUIT;
                WS.MdDat md = threeNozzleModePockets[threeNozzleModeCursor];
                EM_RES result;
                if (threeNozzleModeStage == 0)
                {
                    if (md.res >= 0)
                    {
                        result = ThreeNozzlePickMaterial(ref quit, md, xt);
                        if (result != EM_RES.OK) return result;
                    }
                    threeNozzleModeStage = 1;
                }
                if (threeNozzleModeStage == 1)
                {
                    if (xt.XtMd != null)
                    {
                        result = GoZero(ref quit);
                        if (result != EM_RES.OK) return result;
                        result = ThreeNozzleUnloadCore(ref quit, xt);
                        if (result != EM_RES.OK) return result;
                    }
                    threeNozzleModeStage = 2;
                }
                if (threeNozzleModeStage == 2)
                {
                    if (!VAR.ClearMt && ThreeNozzleCanLoadPocket(md))
                    {
                        if (threeNozzleModePair == null) threeNozzleModePair = ThreeNozzleSinglePair(md, threeNozzleSelected);
                        result = ThreeNozzlePreparePair(ref quit, threeNozzleModePair);
                        if (result != EM_RES.OK) return result;
                        threeNozzleModeStage = 3;
                    }
                    // If interrupted after acquiring a part, finish placing that
                    // known part even when ClearMt was asserted during the pause.
                    else if (threeNozzleModePair != null && threeNozzleModePair.Prepared[0] != null)
                    {
                        result = ThreeNozzlePreparePair(ref quit, threeNozzleModePair);
                        if (result != EM_RES.OK) return result;
                        threeNozzleModeStage = 3;
                    }
                    else threeNozzleModeStage = 4;
                }
                if (threeNozzleModeStage == 3)
                {
                    result = ThreeNozzlePlaceOne(ref quit, threeNozzleModePair, 0);
                    if (result != EM_RES.OK) return result;
                    threeNozzleModeStage = 4;
                }
                if (threeNozzleModeStage == 4)
                {
                    threeNozzleModeCursor++;
                    threeNozzleModeStage = 0;
                    threeNozzleModePair = null;
                }
            }
            return EM_RES.OK;
        }

        private EM_RES ThreeNozzleGrrPlace(ref bool quit, XT xt)
        {
            if (xt.XtMd == null && threeNozzleModePair == null) return EM_RES.OK;
            if (threeNozzleGrrEmpty.Count == 0)
                return ThreeNozzleError("GRR换位无空穴，保留吸嘴物料", true);
            if (threeNozzleModePair == null)
            {
                // Use the earliest physically empty eligible pocket, matching
                // GetWsOrderPosTeam rather than blindly using the picked pocket.
                WS.MdDat target = threeNozzleGrrEmpty.OrderBy(md => md.Num).First();
                threeNozzleModePair = ThreeNozzleSinglePair(target, list_xt.IndexOf(xt));
                threeNozzleModePair.Prepared[0] = xt.XtMd;
                threeNozzleGrrPendingResult = xt.XtMd.res;
            }
            if (!threeNozzleModePair.Placed[0] && !threeNozzleModePair.DownReady[0])
            {
                Cam.VisionOutPutData vision = new Cam.VisionOutPutData();
                EM_RES result = DwCamAction(ref quit, xt, CONST.ModDwFw[list_xt.IndexOf(xt)], ref vision);
                if (result != EM_RES.OK) return result;
                threeNozzleModePair.DownVision[0] = vision;
                threeNozzleModePair.DownReady[0] = true;
            }
            int previousResult = threeNozzleGrrPendingResult;
            EM_RES placed = ThreeNozzlePlaceOne(ref quit, threeNozzleModePair, 0, false);
            if (placed != EM_RES.OK) return placed;
            WS.MdDat filled = threeNozzleModePair.Pockets[0];
            if (filled.res == (int)Product.EM_CM_RES.UNTEST && !threeNozzleModePair.CountAsNew[0])
                threeNozzleGrrReloadResults[filled] = previousResult;
            var remaining = threeNozzleGrrEmpty.Where(md => !Object.ReferenceEquals(md, filled)).ToArray();
            threeNozzleGrrEmpty.Clear();
            foreach (WS.MdDat md in remaining) threeNozzleGrrEmpty.Enqueue(md);
            threeNozzleModePair = null;
            return EM_RES.OK;
        }

        private EM_RES ThreeNozzleRunGrrReload(ref bool quit)
        {
            // Old GRR holds the first module on nozzle 1 and repeatedly uses
            // nozzle 2 for the following modules. The third nozzle stays empty.
            while (threeNozzleModeCursor < threeNozzleModePockets.Count)
            {
                if (quit || VAR.gsys_set.bquit) return EM_RES.QUIT;
                if (VAR.ClearMt) return ThreeNozzleDrainGrr(ref quit);
                int nozzle = threeNozzleModeCursor == 0 ? 0 : 1;
                WS.MdDat md = threeNozzleModePockets[threeNozzleModeCursor];
                if (threeNozzleModeStage == 0)
                {
                    EM_RES picked = ThreeNozzlePickMaterial(ref quit, md, list_xt[nozzle], false);
                    if (picked != EM_RES.OK) return picked;
                    if (ThreeNozzleCanLoadPocket(md)) threeNozzleGrrEmpty.Enqueue(md);
                    threeNozzleModeStage = 1;
                }
                if (nozzle == 1)
                {
                    EM_RES placed = ThreeNozzleGrrPlace(ref quit, list_xt[1]);
                    if (placed != EM_RES.OK) return placed;
                }
                threeNozzleModeCursor++;
                threeNozzleModeStage = 0;
            }
            if (VAR.ClearMt) return ThreeNozzleDrainGrr(ref quit);
            return ThreeNozzleGrrPlace(ref quit, list_xt[0]);
        }

        private EM_RES ThreeNozzleDrainGrr(ref bool quit)
        {
            // Stop GRR reloads as soon as possible. Held tested modules keep their
            // original result and route to the same OK/NG trays as normal clear.
            if (threeNozzleModePair != null && threeNozzleModePair.Placed[0] && !threeNozzleModePair.CountAsNew[0])
                threeNozzleGrrReloadResults[threeNozzleModePair.Pockets[0]] = threeNozzleGrrPendingResult;
            threeNozzleModePair = null;
            foreach (var reloaded in threeNozzleGrrReloadResults)
            {
                if (reloaded.Key.res == (int)Product.EM_CM_RES.UNTEST)
                    reloaded.Key.res = reloaded.Value;
                if (!threeNozzleModePockets.Skip(threeNozzleModeCursor).Contains(reloaded.Key))
                    threeNozzleModePockets.Add(reloaded.Key);
            }
            threeNozzleGrrReloadResults.Clear();
            EM_RES result = GoZero(ref quit);
            if (result != EM_RES.OK) return result;
            result = ThreeNozzleUnloadCore(ref quit);
            if (result != EM_RES.OK) return result;
            while (threeNozzleModeCursor < threeNozzleModePockets.Count)
            {
                if (quit || VAR.gsys_set.bquit) return EM_RES.QUIT;
                WS.MdDat md = threeNozzleModePockets[threeNozzleModeCursor];
                if (md.res >= 0)
                {
                    result = ThreeNozzlePickMaterial(ref quit, md, list_xt[0]);
                    if (result != EM_RES.OK) return result;
                    result = GoZero(ref quit);
                    if (result != EM_RES.OK) return result;
                    result = ThreeNozzleUnloadCore(ref quit, list_xt[0]);
                    if (result != EM_RES.OK) return result;
                }
                threeNozzleModeCursor++;
                threeNozzleModeStage = 0;
            }
            return EM_RES.OK;
        }
    }
}






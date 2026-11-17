using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MotionCtrl;
using UI.Class;
using UI.ModuleCheck;

namespace UI
{
    /// <summary>
    /// 三吸嘴上下料中与模组自动点检相关的部分：
    /// 1. 装样品轮：按样品原穴从点检盘（借用待测盘时为待测盘的屏蔽区）取样品；
    /// 2. 样品无论在哪一轮被卸下（归还、清料、放料后检查返工），都按身份放回点检盘原穴，不进 OK/NG 盘。
    /// 组内循环换料复用 GRR 换位动作（见 ThreeNozzleInitializeMode）。
    /// </summary>
    public partial class UpDownLoad
    {
        /// <summary>本臂当前未完成的换料计划所属工站（停机打断后仍保留）；null=没有未完成的计划</summary>
        public WS ThreeNozzlePlanStation
        {
            get { return threeNozzleSource; }
        }

        /// <summary>同一颗点检样品下视觉/扫码失败的最多尝试次数（含第一次）</summary>
        private const int ModuleCheckMaxVisionTries = 3;
        private readonly System.Collections.Generic.Dictionary<string, int> moduleCheckVisionFails =
            new System.Collections.Generic.Dictionary<string, int>();

        private int ModuleCheckNoteVisionFail(string sampleId)
        {
            lock (moduleCheckVisionFails)
            {
                int n;
                moduleCheckVisionFails.TryGetValue(sampleId ?? "", out n);
                moduleCheckVisionFails[sampleId ?? ""] = ++n;
                return n;
            }
        }

        private void ModuleCheckClearVisionFail(string sampleId)
        {
            if (string.IsNullOrEmpty(sampleId)) return;
            lock (moduleCheckVisionFails) moduleCheckVisionFails.Remove(sampleId);
        }

        /// <summary>
        /// 取点检样品前确保待测盘已出到工作位。只出盘不换盘：借用待测盘时样品就在当前盘的点检区，
        /// 生产区取空与否都不影响取样品（ThreeNozzleEnsureTray 在生产区取空时会换盘，这里不能用）。
        /// </summary>
        private EM_RES ModuleCheckEnsureFeedTrayPresent(ref bool quit)
        {
            TrayBox tray = traybox_fd;
            while (tray.trayActionRunning || tray.ChgML)
            {
                if (quit) return EM_RES.QUIT;
                EM_RES yieldResult = ProcessPositioningYield(ref quit);
                if (yieldResult != EM_RES.OK) return yieldResult;
                Thread.Sleep(20);
            }
            if (tray.IsReady && tray.tray_cur != null) return EM_RES.OK;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, disc + " 三吸嘴: 待测盘未出料，先出盘再取点检样品");
            EM_RES result = ModuleCheckConfirmPresentBorrowedTray(ref quit, "装点检样品");
            if (result != EM_RES.OK) return result;
            result = GoZero(ref quit);
            if (result != EM_RES.OK) return result;
            result = tray.Tray_Action(Demo);
            if (result != EM_RES.OK) return result;
            ThreeNozzleInvalidateTrayCompensation(tray);
            if (!tray.IsReady || tray.tray_cur == null) return ThreeNozzleError("待测盘出料后状态无效，不能取点检样品");
            return EM_RES.OK;
        }

        /// <summary>
        /// 借用待测盘、点检任务进行中、待测盘未出料时，出盘前让员工确认料仓出来的就是放点检样品的那一盘。
        /// 返回 OK 表示可以出盘；员工按停止返回 ERR（不上锁，人工处理后重新运行即可）。
        /// </summary>
        private EM_RES ModuleCheckConfirmPresentBorrowedTray(ref bool quit, string reason)
        {
            EM_RES home = GoZero(ref quit);
            if (home != EM_RES.OK) return home;
            string pockets = ModuleCheckHooks.CheckTrayPocketsText();
            VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, "点检中待测盘出盘确认", 10, true);
            MT.ST_WARN warn = new MT.ST_WARN();
            warn.ok_txt = MultiLanguage.TxtSelct("确认，出盘", "Confirm", "Xác nhận");
            warn.cancle_txt = MultiLanguage.TxtSelct("停止", "Stop", "Ngừng lại");
            warn.title = "提示:点检进行中，待测盘出盘确认";
            warn.msg = "借用待测盘做点检盘（" + reason + "），待测盘当前未出到工作位";
            warn.lb_msg = warn.msg + "。\r\n1.料仓接下来出的必须是放点检样品的那一盘（点检区穴位 " + pockets +
                "）。确认无误按“确认，出盘”。\r\n2.如果料仓里先出的会是别的盘，请按停止，人工把样品盘放到料仓出盘位置后再运行。";
            DialogResult choice = MT.Display_frwarn(new warning(), warn, ERR_ALM.EmErrItem.MaterialPosErr);
            VAR.sys_inf.Set(EM_ALM_STA.NOR_GREEN, "运行", 0, true);
            if (choice != DialogResult.OK)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, disc + " 三吸嘴: 点检中待测盘出盘确认，员工选择停止");
                // 本臂已回原点；吸嘴无料时按“可安全停止”处理，不锁三吸嘴故障（与料盘取料失败归位确认一致）
                if (list_xt.All(x => x.XtMd == null && (Demo || !x.cy_zk.isONByChkSen))) threeNozzleSafeStop = true;
                return EM_RES.ERR;
            }
            ModuleCheckHooks.NoteRisk(ModuleCheckRisk.FeedTraySwapBlocked, "员工确认料仓出的是点检样品盘后出盘（" + reason + "）");
            return EM_RES.OK;
        }

        private static bool IsModuleCheckSample(Product.MdDat material)
        {
            return material != null && !string.IsNullOrEmpty(material.ModuleCheckSampleId);
        }

        /// <summary>装样品轮：该穴位要装的样品及其在点检盘上的穴位。不是装样品轮返回 false。</summary>
        private bool ModuleCheckSourceFor(WS.MdDat pocket, out ModuleCheckSample sample, out Product.Tray.PosInf source, out string error)
        {
            sample = null;
            source = null;
            error = "";
            if (pocket == null || !ModuleCheckHooks.IsCheckLoading(threeNozzleSource)) return false;
            sample = ModuleCheckHooks.SampleToLoad(threeNozzleSource, pocket.Num);
            if (sample == null)
            {
                error = string.Format("穴{0}没有待装的点检样品", pocket.Num);
                return true;
            }
            if (!traybox_fd.IsReady || traybox_fd.tray_cur == null)
            {
                error = "待测盘未就绪，不能取点检样品";
                return true;
            }
            int idx = sample.TrayPocketIdx;
            source = traybox_fd.tray_cur.list_pos.FirstOrDefault(p => p.idx == idx);
            if (source == null) error = string.Format("待测盘没有点检样品穴位 {0}", idx);
            return true;
        }

        /// <summary>
        /// 从点检盘原穴取样品到预取嘴（调用方持有 picktray）。不走待测盘换盘、不绑定扫码、不计取料数。
        /// </summary>
        private EM_RES ModuleCheckPickSample(ref bool quit, XT xt, ThreeNozzleRuntimePair pair, int slot,
            ModuleCheckSample sample, Product.Tray.PosInf source)
        {
            if (xt.XtMd != null || (!Demo && xt.cy_zk.isONByChkSen)) return ThreeNozzleError("预取目标嘴不为空", true);
            if (source.md != null && IsModuleCheckSample(source.md) && source.md.ModuleCheckSampleId != sample.Id)
                return ThreeNozzleError(string.Format("点检盘穴{0}记录的是样品{1}，不是{2}，请核对", source.idx, source.md.ModuleCheckSampleId, sample.Id), true);
            string barcode;
            ThreeNozzleDryRun.NoteLiveAction(id, xt.SlotIndex, "feed", source.idx,
                string.Format("{0} 从点检盘穴{1}取点检样品{2}", xt.disc, source.idx, sample.Id));
            Camcfgset(0U);
            ST_XYZA qr = PT_SET.bAddCapQrcode && traybox_fd.tray_cur.list_AddCapQrcodepos != null &&
                source.idx >= 0 && source.idx < traybox_fd.tray_cur.list_AddCapQrcodepos.Count
                ? traybox_fd.tray_cur.list_AddCapQrcodepos[source.idx].Pos[id] : new ST_XYZA();
            EM_RES result = xt.XtCapMovMod(ref quit, out barcode, source.Pos[id], CONST.ModUpFw,
                pair.Pockets[slot].st_pos[id].x, XT.EM_XTFLOW.PICKMOD, true, Demo, capQrcodePos: qr);
            if (result == EM_RES.QUIT)
            {
                // 被停止打断：用真空判定样品在嘴上还是仍在点检盘原穴
                ThreeNozzlePickRecoveryAction action;
                EM_RES judged = ThreeNozzleResolveInterruptedPick(xt, string.Format("点检样品{0}取料", sample.Id), out action);
                if (judged != EM_RES.OK) return judged;
                result = EM_RES.OK;
            }
            if (result != EM_RES.OK)
            {
                if (!Demo && xt.cy_zk.isONByChkSen)
                    return ThreeNozzleError(string.Format("点检样品{0}从点检盘穴{1}取料失败({2})但{3}真空有料，请人工确认样品位置", sample.Id, source.idx, result, xt.disc), true);
                return ThreeNozzleError(string.Format("点检样品{0}从点检盘穴{1}取料失败({2})，请人工确认样品后再运行", sample.Id, source.idx, result));
            }
            Product.MdDat material = source.md ?? new Product.MdDat();
            material.res = (int)Product.EM_CM_RES.UNTEST;
            material.idx = source.idx;
            material.ModuleCheckSampleId = sample.Id;
            material.bardcode = null;
            material.motor_barcode = null;
            xt.XtMd = material;
            source.md = null;
            pair.Prepared[slot] = material;
            pair.SourceTrays[slot] = traybox_fd.tray_cur;
            pair.SourcePositions[slot] = source;
            pair.PickedPositions[slot] = xt.xt_pos_pick_mod.clone();
            pair.PickedTrayXs[slot] = xt.xt_pos_pick_tray_x;
            ModuleCheckHooks.OnSamplePicked(sample.Id, id, xt.SlotIndex, "点检盘穴" + source.idx);
            return EM_RES.OK;
        }

        /// <summary>
        /// 借用待测盘时的换盘保护（ThreeNozzleEnsureTray 准备换待测盘前调用）。
        /// 返回 OK=可以换盘；RETRY=员工已人工补料，不换盘、重新检查；其它=停止。
        /// </summary>
        private EM_RES ModuleCheckGuardFeedTraySwap(ref bool quit)
        {
            string reason;
            if (ModuleCheckHooks.BlocksFeedTraySwap(out reason))
            {
                if (traybox_fd.tray_cur == null || !traybox_fd.IsReady)
                {
                    // 停机/重启后待测盘还没出到工作位：这是“出盘”不是“换盘”，确认出的是放样品的那一盘后继续
                    return ModuleCheckConfirmPresentBorrowedTray(ref quit, reason);
                }
                ModuleCheckHooks.NoteRisk(ModuleCheckRisk.FeedTraySwapBlocked, reason + "，待测盘生产区已取空，禁止自动换盘");
                EM_RES home = GoZero(ref quit);
                if (home != EM_RES.OK) return home;
                VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, "点检中待测盘已空", 10, true);
                MT.ST_WARN warn = new MT.ST_WARN();
                warn.ok_txt = MultiLanguage.TxtSelct("已补料，继续", "Refilled", "Đã bổ sung");
                warn.cancle_txt = MultiLanguage.TxtSelct("停止", "Stop", "Ngừng lại");
                warn.title = "提示:点检进行中，待测盘不能换盘";
                warn.msg = "借用待测盘做点检盘，" + reason;
                warn.lb_msg = warn.msg + "。\r\n1.请不要换盘，人工把当前待测盘的生产区补满待测料（不要动点检区样品），按“已补料，继续”。\r\n2.按停止则停机。";
                DialogResult choice = MT.Display_frwarn(new warning(), warn, ERR_ALM.EmErrItem.MaterialPosErr);
                if (choice != DialogResult.OK) return EM_RES.ERR;
                int refilled = 0;
                foreach (Product.Tray.PosInf pos in traybox_fd.tray_cur.list_pos)
                {
                    if (pos.isDisable || pos.isDisableTemp || pos.md != null) continue;
                    pos.md = new Product.MdDat();
                    pos.md.res = (int)Product.EM_CM_RES.UNTEST;
                    pos.md.idx = pos.idx;
                    refilled++;
                }
                ModuleCheckHooks.NoteRisk(ModuleCheckRisk.FeedTraySwapBlocked, string.Format("员工确认已人工补料，待测盘生产区 {0} 个空穴改为待测", refilled));
                VAR.sys_inf.Set(EM_ALM_STA.NOR_GREEN, "运行", 0, true);
                return EM_RES.RETRY;
            }
            string pockets;
            if (ModuleCheckHooks.SamplesParkedOnFeedTray(out pockets))
            {
                EM_RES home = GoZero(ref quit);
                if (home != EM_RES.OK) return home;
                VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, "待测盘换盘", 10, true);
                MT.ST_WARN warn = new MT.ST_WARN();
                warn.ok_txt = MultiLanguage.TxtSelct("已取出样品，换盘", "Swap", "Đổi khay");
                warn.cancle_txt = MultiLanguage.TxtSelct("停止", "Stop", "Ngừng lại");
                warn.title = "提示:待测盘即将换盘";
                warn.msg = "待测盘点检区放有点检样品（穴位 " + pockets + "）";
                warn.lb_msg = warn.msg + "。\r\n1.请先取出这些样品，换盘后放回新待测盘的相同穴位，再按“已取出样品，换盘”。\r\n2.按停止则停机。";
                DialogResult choice = MT.Display_frwarn(new warning(), warn, ERR_ALM.EmErrItem.MaterialPosErr);
                if (choice != DialogResult.OK) return EM_RES.ERR;
                ModuleCheckHooks.NoteRisk(ModuleCheckRisk.FeedTraySwapBlocked, "员工确认已取出点检样品后换待测盘（穴位 " + pockets + "）");
                VAR.sys_inf.Set(EM_ALM_STA.NOR_GREEN, "运行", 0, true);
            }
            return EM_RES.OK;
        }

        /// <summary>
        /// 样品放回点检盘原穴（调用方持有 placetray；这里再取 picktray 保护待测盘X轴）。
        /// </summary>
        private EM_RES ModuleCheckReturnSample(ref bool quit, XT xt)
        {
            Product.MdDat material = xt.XtMd;
            string sampleId = material.ModuleCheckSampleId;
            if (!Demo && !xt.cy_zk.isONByChkSen) return ThreeNozzleError("归还点检样品时真空无料", true);
            ModuleCheckSample sample = ModuleCheckHooks.FindSample(sampleId);
            int idx = sample != null ? sample.TrayPocketIdx : material.idx;
            if (sample == null)
                ModuleCheckHooks.NoteRisk(ModuleCheckRisk.SampleIdentityMismatch, string.Format("{0} 上的样品{1}不在当前任务里，按记录的原穴{2}放回", xt.disc, sampleId, idx));
            EM_RES result = ThreeNozzleTakeLock(ref quit, picktray);
            if (result != EM_RES.OK) return result;
            try
            {
                if (!traybox_fd.IsReady || traybox_fd.tray_cur == null)
                    return ThreeNozzleError("待测盘未就绪，点检样品无法放回原穴，保留吸嘴物料", true);
                Product.Tray.PosInf target = traybox_fd.tray_cur.list_pos.FirstOrDefault(p => p.idx == idx);
                if (target == null) return ThreeNozzleError(string.Format("待测盘没有点检样品原穴 {0}，保留吸嘴物料", idx), true);
                if (target.md != null && IsModuleCheckSample(target.md))
                    return ThreeNozzleError(string.Format("点检盘穴{0}已有样品{1}，样品{2}无法放回，保留吸嘴物料", idx, target.md.ModuleCheckSampleId, sampleId), true);
                int started = Environment.TickCount;
                while (traybox_fd.runin || !traybox_fd.ax_x.isStop)
                {
                    if (quit) return EM_RES.QUIT;
                    if (unchecked(Environment.TickCount - started) > 5000)
                        return ThreeNozzleError("等待待测盘停止超过5秒，保留点检样品");
                    Thread.Sleep(10);
                }
                // 待测盘（点检盘）示教的是上相机拍照位，不是吸嘴落料位：按取样品/盲取同一套换算
                // （CaliPos 定料盘 X，XtCalPos 从上相机位换到本吸嘴，上相机偏移按 0），料放回取料时的同一个穴。
                // 原来按 OK/NG 盘落料位公式算，吸嘴落在拍照位上，样品偏了一个“上相机到吸嘴”的距离（吸嘴1 约 180mm）。
                ST_XYZA camPos = xt.CaliPos(target.Pos[id]);
                ST_XYZA pos = xt.XtCalPos(camPos.ToXY(), new ST_XYA(), xt.st_vs_pos_xtshp, XT.EM_XTFLOW.PICKMOD).ToXYZA();
                pos.a = camPos.a;   // 待测盘 X
                try { pos.z = NozzleZCalibration.ToAxisZ(id, xt.SlotIndex, target.Pos[id].z, xt.ZApproachDirection); }
                catch (Exception ex) { return ThreeNozzleError("点检样品归还Z标定不可用：" + ex.Message); }
                ThreeNozzleDryRun.NoteLiveAction(id, xt.SlotIndex, "feed", idx,
                    string.Format("{0} 把点检样品{1}放回点检盘穴{2}", xt.disc, sampleId, idx));
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} {1} 归还点检样品{2}到待测盘穴{3}：目标 X={4:F3} Y={5:F3} Z={6:F3}，待测盘X 目标={7:F3}，结果={8}",
                    disc, xt.disc, sampleId, idx, pos.x, pos.y, pos.z, pos.a, material.res));
                result = MT.ZupMove(ref quit, ref ax_x, pos.x, ref ax_y, pos.y, ref xt.ax_u,
                    PT_SET.isopen_degree ? -PT_SET.degree : 0, ref traybox_fd.ax_x, pos.a);
                if (result != EM_RES.OK) return result;
                traybox_fd.MoveToLastPos[id] = true;   // 与取料（XtCapMovMod/盲取）的待测盘状态记录一致
                pos.a = PT_SET.isopen_degree ? -PT_SET.degree : 0;
                result = xt.Place(ref quit, pos, true, Demo);
                if (result != EM_RES.OK) return ThreeNozzleError("点检样品归还结果待核对: " + result, true);
                target.md = material;
                threeNozzleFinishedBarcodeChecks.Remove(material);
                threeNozzlePendingBarcodeWarnings.Remove(material);
                xt.XtMd = null;
                ModuleCheckHooks.OnSampleReturned(sampleId, idx);
                return EM_RES.OK;
            }
            finally { Monitor.Exit(picktray); }
        }
    }
}

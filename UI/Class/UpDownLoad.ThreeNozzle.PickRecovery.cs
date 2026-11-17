using System;
using System.Threading;
using System.Windows.Forms;
using MotionCtrl;

namespace UI
{
    public partial class UpDownLoad
    {
        private enum ThreeNozzlePickRecoveryAction
        {
            Stop, RetrySame, NextTrayPocket, ReplaceTray, Clear, ConfirmedEmpty,
            /// <summary>取料被停止打断，但真空判定料已在吸嘴上：调用方按取料成功提交身份</summary>
            PickedOnInterrupt
        }

        /// <summary>
        /// 停止键/另一臂报错打断取料（Pick / PickOrPlace 返回 QUIT）后，用真空自动判定物料去向。
        /// 依据：两个取料函数的 finally 都会把本嘴 Z 抬回 0（不受停止影响），且取料时 XY 停在源穴上方，
        /// 没吸住的料只会留在（或落回）源穴。
        /// 返回 1=料在本嘴上；0=料不在本嘴上（仍在源穴）；-1=无法判定（交人工对账），detail 为原因。
        /// </summary>
        private int ThreeNozzleJudgeInterruptedPick(XT xt, out string detail)
        {
            detail = "";
            if (Demo) { detail = "空跑不判定"; return -1; }
            if (xt.ax_z.isALM || Math.Abs(xt.ax_z.fenc_pos) > 0.5)
            {
                detail = string.Format("{0} Z 未回到安全高度（Z={1:F3}{2}）", xt.disc, xt.ax_z.fenc_pos, xt.ax_z.isALM ? "，轴报警" : "");
                return -1;
            }
            if (xt.XtMd != null) { detail = xt.disc + " 已有物料记录"; return -1; }
            // 同臂其它吸嘴记录与真空不一致：按双吸嘴规则自动校正，不再因此判“无法判定”
            foreach (XT other in list_xt)
            {
                if (Object.ReferenceEquals(other, xt)) continue;
                ThreeNozzleAutoCorrectVacuum(other, "取料被打断后检查同臂吸嘴");
            }
            // 抬起后稍等，再连续采样，读数全部一致才采信
            Thread.Sleep(150);
            const int samples = 6;
            int on = 0;
            for (int i = 0; i < samples; i++)
            {
                if (xt.cy_zk.isONByChkSen) on++;
                Thread.Sleep(40);
            }
            if (on == samples) return 1;
            if (on == 0) return 0;
            detail = string.Format("{0} 真空读数不稳定（{1}/{2} 次有料）", xt.disc, on, samples);
            return -1;
        }

        /// <summary>
        /// 取料被打断后的统一处理。返回 OK+PickedOnInterrupt=料在嘴上（调用方提交）；
        /// 返回 QUIT=料仍在源穴（记录不动、不上锁，恢复运行后重新取）；其它=无法判定，已上锁。
        /// </summary>
        private EM_RES ThreeNozzleResolveInterruptedPick(XT xt, string where, out ThreeNozzlePickRecoveryAction action)
        {
            action = ThreeNozzlePickRecoveryAction.Stop;
            string detail;
            int judged = ThreeNozzleJudgeInterruptedPick(xt, out detail);
            if (judged == 1)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 三吸嘴: {1}被停止打断，Z 已抬起、{2}真空稳定有料，自动判定已取起（不需人工对账）",
                    disc, where, xt.disc));
                threeNozzleSafeStop = true;
                action = ThreeNozzlePickRecoveryAction.PickedOnInterrupt;
                return EM_RES.OK;
            }
            if (judged == 0)
            {
                xt.cy_zk.SetOff();
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 三吸嘴: {1}被停止打断，Z 已抬起、{2}真空稳定无料，自动判定料仍在原穴，恢复运行后重新取（不需人工对账）",
                    disc, where, xt.disc));
                threeNozzleSafeStop = true;
                return EM_RES.QUIT;
            }
            threeNozzlePendingVacuum = xt;
            threeNozzlePendingVacuumExpected = false;
            threeNozzlePendingVacuumWhere = where + "停止后真空状态未明确，请处理本嘴及原穴";
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, disc + " " + where + " 真空状态未明确，下次启动先弹窗检查：" + detail);
            return EM_RES.QUIT;
        }

        private int threeNozzleTrayFailures;

        // 与原双嘴一致：料盘连续取料失败选“归位确认”时，只停止运行，不锁三吸嘴故障（不用系统复位），
        // 再按开始从下一个料穴接着取。只在吸嘴确认无料、已回到原点后才置位；协调器取走后清零。
        private volatile bool threeNozzleSafeStop;

        /// <summary>取走“可安全停止”标记（读后清零）。</summary>
        internal bool ThreeNozzleTakeSafeStop()
        {
            bool value = threeNozzleSafeStop;
            threeNozzleSafeStop = false;
            return value;
        }

        private void ThreeNozzleTrayPickSucceeded()
        {
            threeNozzleTrayFailures = 0;
        }

        // An OK return means only that an explicit recovery decision is available.
        // It NEVER means that the pick succeeded. Only the pick caller transfers
        // source identity after a real successful XT result.
        private EM_RES ThreeNozzleHandleTrayPickFailure(ref bool quit, XT xt,
            Product.MdDat source, EM_RES failure, out ThreeNozzlePickRecoveryAction action)
        {
            action = ThreeNozzlePickRecoveryAction.Stop;
            if (failure == EM_RES.QUIT && source != null)
                return ThreeNozzleResolveInterruptedPick(xt, "料盘取料", out action);
            if (failure != EM_RES.CAM_ERR && failure != EM_RES.TIMEOUT && failure != EM_RES.PICK_ERR)
            {
                ThreeNozzleError("取盘动作异常，物理结果待核对: " + failure, true);
                return failure == EM_RES.OK ? EM_RES.ERR : failure;
            }
            // Camera errors can occur after motion. Never skip a tray pocket or
            // change its record when the physical outcome is still uncertain.
            if (xt.XtMd != null || source == null)
            {
                ThreeNozzleError("取盘失败后吸嘴有料或身份不明，保留源盘记录待核对", true);
                return failure;
            }

            // 普通 CAM_ERR / TIMEOUT / PICK_ERR 沿用双嘴连续五次失败处理。
            // 与双吸嘴一致：取料未确认但真空有料时，关真空、清记录（料落回原穴），再按普通吸取失败处理：
            // 该穴标视觉NG跳过，连续 5 次失败才弹窗。不再弹“吸取/真空异常”窗。
            if (!Demo && xt.cy_zk.isONByChkSen)
            {
                EM_RES corrected = ThreeNozzleRequireVacuumCorrection(ref quit, xt, false, "料盘吸取失败（取料未确认但真空有料）");
                if (corrected != EM_RES.OK) return corrected;
            }

            if (quit || VAR.gsys_set.bquit) return EM_RES.QUIT;

            source.res = (int)Product.EM_CM_RES.CAMERR;
            threeNozzleTrayFailures++;
            COUNT_DATA.SuctionAllcnt++;
            COUNT_DATA.SuctionErrcnt++;
            if (threeNozzleTrayFailures < 5)
            {
                action = ThreeNozzlePickRecoveryAction.NextTrayPocket;
                return EM_RES.OK;
            }

            VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, "吸料异常 / Draw ERR", 20, true);
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, disc + " " + xt.disc + " 连续取料5次失败 / Five consecutive pick failures");
            var warn = new MT.ST_WARN();
            warn.ok_txt = "归位确认 / Home";
            warn.cancle_txt = "更换料盘 / Replace tray";
            warn.abort_txt = "清料 / Clear";
            warn.ws = null;
            warn.title = "提示:吸料异常 / Abnormal suction";
            warn.msg = xt.disc + " 连续取料5次模组失败 / Five consecutive pick failures";
            warn.lb_msg = warn.msg + "\r\n请确认料盘是否放反。\r\n1.放反请更换料盘。\r\n" +
                "2.归位确认后分析原因，等轴归位完成再开门。\r\n3.清料停止取新料。\r\n" +
                "Check tray orientation. Replace the tray, return home before opening the door, or clear material.";
            EM_RES recovery;
            DialogResult answer = DisplayWarningWithDualArmRecoveryGate(new warning(), warn,
                ERR_ALM.EmErrItem.UpDownLoadAbnormal, "料盘吸料异常", true, out recovery);
            if (recovery != EM_RES.OK) return recovery;
            if (answer == DialogResult.OK)
            {
                // 原双嘴：归位后返回失败，上下料线程停止；再按开始时重新计 5 次（原流程的计数是局部变量）
                EM_RES home = GoZero(ref quit, true);
                if (home != EM_RES.OK) return home;
                threeNozzleTrayFailures = 0;
                if (xt.XtMd == null && (Demo || !xt.cy_zk.isONByChkSen))
                {
                    threeNozzleSafeStop = true;
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format(
                        "{0} {1} 连续取料失败，归位确认后停止运行（吸嘴无料，不锁故障）；处理后按开始从下一个料穴继续", disc, xt.disc));
                }
                return failure;
            }
            if (answer == DialogResult.Cancel)
            {
                action = ThreeNozzlePickRecoveryAction.ReplaceTray;
                // Reset only on an explicit tray replacement decision or a real
                // successful pick; ordinary pause/resume does not reset failures.
                threeNozzleTrayFailures = 0;
            }
            else if (answer == DialogResult.Abort)
            {
                VAR.ClearMt = true;
                action = ThreeNozzlePickRecoveryAction.Clear;
            }
            else return failure;
            VAR.sys_inf.Set(EM_ALM_STA.NOR_GREEN, "运行 / RUN", 0, true);
            return EM_RES.OK;
        }

        private EM_RES ThreeNozzleHandleFixturePickFailure(ref bool quit, WS ws,
            WS.MdDat md, XT xt, EM_RES failure, out ThreeNozzlePickRecoveryAction action)
        {
            // 两臂可能已同时吸料失败。把最新穴位检查和弹窗放在同一把锁内，
            // 等待另一臂人工“置空→更新状态执行”后，不再显示已经失效的报警。
            // DisplayWarningWithDualArmRecoveryGate 内部使用同一把锁，Monitor 可重入。
            lock (dualArmRecoveryDialogLock)
            {
                if (failure == EM_RES.PICK_ERR && md != null && md.res == (int)Product.EM_CM_RES.EMPTY)
                {
                    action = ThreeNozzlePickRecoveryAction.Stop;
                    if (quit || VAR.gsys_set.bquit) return EM_RES.QUIT;
                    if (xt.XtMd != null) return ThreeNozzleError("人工置空后吸嘴仍有物料记录，须核对身份", true);
                    if (!Demo && xt.cy_zk.isONByChkSen)
                    {
                        EM_RES corrected = ThreeNozzleRequireVacuumCorrection(ref quit, xt, false, "人工置空后本嘴真空仍有料");
                        if (corrected != EM_RES.OK) return corrected;
                    }
                    action = ThreeNozzlePickRecoveryAction.ConfirmedEmpty;
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format(
                        "{0} 三吸嘴: {1}穴{2}已由员工置空，{3}确认无料，跳过重复吸料异常弹窗",
                        disc, ws == null ? "工站" : ws.disc, md.Num, xt.disc));
                    return EM_RES.OK;
                }
                return ThreeNozzleHandleFixturePickFailureCore(ref quit, ws, md, xt, failure, out action);
            }
        }

        private EM_RES ThreeNozzleHandleFixturePickFailureCore(ref bool quit, WS ws,
            WS.MdDat md, XT xt, EM_RES failure, out ThreeNozzlePickRecoveryAction action)
        {
            action = ThreeNozzlePickRecoveryAction.Stop;
            if (failure == EM_RES.QUIT)
                return ThreeNozzleResolveInterruptedPick(xt, string.Format("{0}穴{1}取料", ws == null ? "工站" : ws.disc, md == null ? 0 : md.Num), out action);
            string zBlockedNote = "";
            if (failure == EM_RES.RETRY && md != null)
            {
                // Z 下降到取料高度时定位偏差超 1.3mm（被顶住）。XT.Pick 的 finally 已把 Z 抬回 0，按真空判定：
                // 有料 → 已吸起，按取料成功；无料 → 料仍在穴里，按“吸料异常”弹窗让员工检查后选择，不锁对账
                string detail;
                int judged = ThreeNozzleJudgeInterruptedPick(xt, out detail);
                if (judged == 1)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 三吸嘴: {1}穴{2}取料时 Z 定位偏差超限，但 Z 已抬起、{3}真空稳定有料，按已取起处理（请留意该穴物料是否翘起/叠料）",
                        disc, ws == null ? "工站" : ws.disc, md.Num, xt.disc));
                    action = ThreeNozzlePickRecoveryAction.PickedOnInterrupt;
                    return EM_RES.OK;
                }
                if (judged != 0)
                {
                    EM_RES corrected = ThreeNozzleRequireVacuumCorrection(ref quit, xt, false, "夹具取料Z偏差后真空状态未明确：" + detail);
                    if (corrected != EM_RES.OK) return corrected;
                    action = ThreeNozzlePickRecoveryAction.RetrySame;
                    return EM_RES.OK;
                }
                xt.cy_zk.SetOff();
                zBlockedNote = string.Format("取料时 {0} 的 Z 下降被顶住（定位偏差超过 1.3mm），料没有吸起。\r\n" +
                    "可能是穴{1}里叠料、物料翘起/没放平，或取料高度过深，请先检查穴{1}。\r\n", xt.disc, md.Num);
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 三吸嘴: {1}穴{2}取料 Z 被顶住，Z 已抬起、{3}真空无料，料仍在穴内，弹窗由员工检查处理",
                    disc, ws == null ? "工站" : ws.disc, md.Num, xt.disc));
                failure = EM_RES.PICK_ERR;
            }
            if (failure == EM_RES.TIMEOUT || (failure == EM_RES.ERR && xt.XtMd == null && !Demo && xt.cy_zk.isONByChkSen))
                failure = EM_RES.PICK_ERR;
            if (failure != EM_RES.PICK_ERR)
            {
                ThreeNozzleError("工站取料动作异常，物理结果待核对: " + failure, true);
                return failure == EM_RES.OK ? EM_RES.ERR : failure;
            }
            if (xt.XtMd != null)
            {
                ThreeNozzleError("工站取料失败后已有物料身份，须核对", true);
                return failure;
            }
            if (!Demo && xt.cy_zk.isONByChkSen)
            {
                EM_RES corrected = ThreeNozzleRequireVacuumCorrection(ref quit, xt, false, "工站取料失败后真空有料但记录为空");
                if (corrected != EM_RES.OK) return corrected;
                action = ThreeNozzlePickRecoveryAction.RetrySame;
                return EM_RES.OK;
            }
            if (quit || VAR.gsys_set.bquit) return EM_RES.QUIT;

            VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, "吸料异常 / Draw ERR", 20, true);
            var warn = new MT.ST_WARN();
            warn.ok_txt = "放弃吸取 / Give up";
            warn.abort_txt = "重新吸取 / Try again";
            warn.cancle_txt = "停止运行 / Stop";
            warn.ws = ws;
            warn.title = "提示:吸料异常 / Abnormal suction";
            warn.msg = disc + " " + xt.disc + " 无法吸起工站模组 " + md.Num;
            warn.lb_msg = warn.msg + "\r\n" + zBlockedNote + "1.只有确认工站穴位无物料才可放弃吸取。\r\n" +
                "2.如仍有料请停止运行。\r\n3.重新吸取会使用原吸嘴再次取同一穴。\r\n" +
                "Give up only after confirming that the fixture pocket is empty. Otherwise stop or retry the same nozzle.";
            EM_RES recovery;
            DialogResult answer = DisplayWarningWithDualArmRecoveryGate(new warning(), warn,
                ERR_ALM.EmErrItem.UpDownLoadAbnormal, "工站吸料异常", true, out recovery, keepPositionOnUpdate: true);
            if (recovery != EM_RES.OK) return recovery;
            if (answer == DialogResult.Cancel)
                return StopBackCheckAndReturnHome(ref quit, null, "工站吸料异常");
            if (answer == DialogResult.Abort)
                action = ThreeNozzlePickRecoveryAction.RetrySame;
            else if (answer == DialogResult.OK)
            {
                if (xt.XtMd != null) return ThreeNozzleError("放弃吸取确认后物料身份变化，禁止清空穴位记录", true);
                if (!Demo && xt.cy_zk.isONByChkSen)
                {
                    EM_RES corrected = ThreeNozzleRequireVacuumCorrection(ref quit, xt, false, "放弃吸取时真空状态发生变化，请重新检查原穴");
                    if (corrected != EM_RES.OK) return corrected;
                    action = ThreeNozzlePickRecoveryAction.RetrySame;
                    return EM_RES.OK;
                }
                // The caller clears fixture/barcode/provenance only. No XtMd is
                // manufactured and no finished-part unload is booked.
                action = ThreeNozzlePickRecoveryAction.ConfirmedEmpty;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format(
                    "{0} 人工确认空穴放弃吸取,模组={1},吸头={2},原结果={3},条码={4}",
                    ws.disc, md.Num, xt.disc, md.res, md.bardcode));
            }
            else if (answer == DialogResult.Retry)
            {
                // 弹窗穴位面板“置空 → 更新状态执行”：warning 已把选中的穴改成空料(-2)，返回 Retry（原双嘴按新状态重新规划取料）。
                // 当前穴被置空 = 人工确认空穴（同“放弃吸取”）；当前穴没被置空 = 重新吸取。
                // 同一工站其它被置空的穴，轮到取料时由 ThreeNozzlePickOne 跳过。
                if (md.res == (int)Product.EM_CM_RES.EMPTY)
                {
                    if (xt.XtMd != null) return ThreeNozzleError("置空确认后物料身份变化，禁止清空穴位记录", true);
                    if (!Demo && xt.cy_zk.isONByChkSen)
                    {
                        EM_RES corrected = ThreeNozzleRequireVacuumCorrection(ref quit, xt, false, "置空确认后本嘴真空仍有料");
                        if (corrected != EM_RES.OK) return corrected;
                    }
                    action = ThreeNozzlePickRecoveryAction.ConfirmedEmpty;
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format(
                        "{0} 人工置空(更新状态执行),模组={1},吸头={2}，按空穴处理", ws.disc, md.Num, xt.disc));
                }
                else
                {
                    action = ThreeNozzlePickRecoveryAction.RetrySame;
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format(
                        "{0} 更新状态执行,模组{1}未置空(结果={2})，{3}重新吸取", ws.disc, md.Num, md.res, xt.disc));
                }
            }
            else return failure;
            VAR.sys_inf.Set(EM_ALM_STA.NOR_GREEN, "运行 / RUN", 0, true);
            return EM_RES.OK;
        }
    }
}



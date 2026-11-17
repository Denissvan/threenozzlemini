using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MotionCtrl;

namespace UI.ModuleCheck
{
    /// <summary>
    /// 运行流程（myfunc / WS / UpDownLoad / 协调器）调用的接入点。
    /// 和 ModuleCheckMachineAdapter 一样属于本机程序相关部分，移植时按对方代码重写。
    /// 原则：没有进行中的模组点检任务时，每个接入点都直接返回“不处理”，原流程完全不变。
    /// </summary>
    public static class ModuleCheckHooks
    {
        private static ModuleCheckManager M { get { return ModuleCheckManager.Instance; } }

        /// <summary>进行中的任务（任何工站）</summary>
        private static ModuleCheckTask Active
        {
            get
            {
                var t = M.ActiveTask;
                return t != null && t.IsActive ? t : null;
            }
        }

        /// <summary>该工站是本次任务的承载工站时返回任务，否则 null</summary>
        private static ModuleCheckTask ActiveFor(WS ws)
        {
            var t = Active;
            if (t == null || ws == null || ws.num != t.Snapshot.TargetStation) return null;
            return t;
        }

        /// <summary>有进行中的任务（用于关闭提前预取等）</summary>
        public static bool IsTaskActive
        {
            get { return Active != null; }
        }

        /// <summary>有进行中的点检任务、且承载工站就是 ws（提前预取等只对这个工站让路）</summary>
        public static bool AffectsStation(WS ws)
        {
            return ActiveFor(ws) != null;
        }

        /// <summary>该工站本轮是“从点检盘装样品”的上料</summary>
        public static bool IsCheckLoading(WS ws)
        {
            var t = ActiveFor(ws);
            return t != null && t.Stage == ModuleCheckStage.Loading && !t.NeedsReconcile;
        }

        /// <summary>该工站本次到上下料面要做组内循环换料</summary>
        public static bool IsSwapping(WS ws)
        {
            var t = ActiveFor(ws);
            return t != null && t.Stage == ModuleCheckStage.Swapping && !t.NeedsReconcile;
        }

        /// <summary>该工站正在测点检样品（结果不进生产统计/MES，不做同排NG提示）</summary>
        public static bool IsTestingSamples(WS ws)
        {
            var t = ActiveFor(ws);
            return t != null && t.Stage == ModuleCheckStage.Testing;
        }

        /// <summary>装样品轮：该穴位要不要上料（只给配置了样品的穴上料，其它穴保持空）</summary>
        public static bool AllowLoad(WS ws, int pocketNum)
        {
            if (!IsCheckLoading(ws)) return true;
            return SampleToLoad(ws, pocketNum) != null;
        }

        /// <summary>装样品轮：该穴位应装的、仍在点检盘上的样品</summary>
        public static ModuleCheckSample SampleToLoad(WS ws, int pocketNum)
        {
            var t = ActiveFor(ws);
            if (t == null || t.Stage != ModuleCheckStage.Loading) return null;
            // 放料后检查返工时样品会先放回原穴再重新取，所以这里不看 Returned
            return t.Samples.FirstOrDefault(s => s.HomeFixturePocket == pocketNum && s.Location == ModuleCheckSampleLocation.CheckTray);
        }

        public static ModuleCheckSample FindSample(string sampleId)
        {
            var t = Active;
            return t == null || string.IsNullOrEmpty(sampleId) ? null : t.FindSample(sampleId);
        }

        /// <summary>
        /// 借用待测盘时，任务进行中（有样品不在盘上）禁止待测盘自动换盘，否则样品会被连盘带走。
        /// </summary>
        public static bool BlocksFeedTraySwap(out string reason)
        {
            reason = "";
            var t = Active;
            if (t == null || !t.Snapshot.BorrowFeedTray) return false;
            var away = t.Samples.Where(s => s.Location != ModuleCheckSampleLocation.CheckTray).Select(s => s.Id).ToList();
            if (away.Count == 0 && t.Stage == ModuleCheckStage.WaitTargetBatch) return false;
            reason = away.Count > 0
                ? string.Format("点检样品 {0} 还未归还待测盘", string.Join(",", away))
                : "点检任务进行中";
            return true;
        }

        /// <summary>
        /// 工站当前这批普通料是否还没做完：有启用穴放着未测的普通料（已上料未测试），
        /// 或上下料臂对本工站还有未完成的换料计划（中途停机）。
        /// </summary>
        private static bool StationBatchUnfinished(WS ws, out string detail)
        {
            var parts = new List<string>();
            if (ws != null && ws.list_md != null)
            {
                var untested = ws.list_md.Where(m => m._benable && m.res == (int)Product.EM_CM_RES.UNTEST && string.IsNullOrEmpty(m.ModuleCheckSampleId))
                    .Select(m => m.Num).ToList();
                if (untested.Count > 0) parts.Add("穴" + string.Join(",", untested) + " 为未测普通料");
            }
            if (COM.List_UDLoad != null)
            {
                foreach (UpDownLoad ud in COM.List_UDLoad)
                    if (ud != null && ws != null && Object.ReferenceEquals(ud.ThreeNozzlePlanStation, ws))
                        parts.Add(ud.disc + " 换料未完成");
            }
            detail = string.Join("；", parts);
            return parts.Count > 0;
        }

        /// <summary>当前任务的点检区穴位（提示用）</summary>
        public static string CheckTrayPocketsText()
        {
            var t = Active;
            if (t == null) return "";
            return string.Join(",", t.Samples.Select(s => s.TrayPocketIdx).OrderBy(x => x));
        }

        /// <summary>没有进行中的任务、但借用待测盘的点检区放着样品（换盘前要人工转移）</summary>
        public static bool SamplesParkedOnFeedTray(out string pockets)
        {
            pockets = "";
            if (Active != null) return false;
            var s = M.Settings;
            if (s == null || !s.Enabled || !s.BorrowFeedTray || s.Samples.Count == 0) return false;
            pockets = string.Join(",", s.Samples.Select(x => x.TrayPocketIdx).OrderBy(x => x));
            return true;
        }

        /// <summary>卸料时发现吸嘴上是点检样品（改送点检盘原穴）</summary>
        public static void NoteSampleDiverted(string nozzle, string sampleId, int res)
        {
            var t = Active;
            if (t != null && t.Stage == ModuleCheckStage.Returning && !VAR.ClearMt)
            {
                ModuleCheckLog.Info(ModuleCheckLogCat.Material, t, string.Format("{0} 卸下样品{1}(结果{2})，放回点检盘原穴", nozzle, sampleId, res));
                return;
            }
            ModuleCheckLog.Risk(ModuleCheckRisk.SampleToProductTrayBlocked, t, string.Format("{0} 卸料时遇到点检样品{1}(结果{2})，阶段={3}{4}，已改为放回点检盘原穴，不进 OK/NG 盘",
                nozzle, sampleId, res, t == null ? "无任务" : ModuleCheckText.Stage(t.Stage), VAR.ClearMt ? "，清料中" : ""));
        }

        public static void NoteRisk(ModuleCheckRisk risk, string msg)
        {
            ModuleCheckLog.Risk(risk, Active, msg);
        }

        /// <summary>
        /// 三吸嘴人工对账清除了某个吸嘴上的点检样品记录（样品已被人工取下）：
        /// 提示员工把样品放回点检盘原穴，确认后记为已归还，任务按原穴重新取。UI 线程调用。
        /// </summary>
        public static void ConfirmSampleHomedByOperator(string sampleId, string note)
        {
            var t = Active;
            var s = t == null ? null : t.FindSample(sampleId);
            if (s == null) return;
            System.Windows.Forms.MessageBox.Show(
                string.Format("吸嘴上的点检样品 {0} 已人工取下。\r\n\r\n请把它放回待测盘原穴：穴位 {1}（点检区）。\r\n放好后点“确定”，本次点检会从该穴重新取这颗样品。", sampleId, s.TrayPocketIdx),
                "模组点检样品归位", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
            M.MarkSamplesManuallyHomed(new[] { sampleId }, note);
        }

        public static void OnSamplePicked(string sampleId, int arm, int nozzle, string from)
        {
            if (string.IsNullOrEmpty(sampleId)) return;
            M.OnSamplePicked(sampleId, arm, nozzle, from);
        }

        public static void OnSamplePlaced(string sampleId, int pocket)
        {
            if (string.IsNullOrEmpty(sampleId)) return;
            M.OnSamplePlaced(sampleId, pocket);
        }

        public static void OnSampleReturned(string sampleId, int trayPocketIdx)
        {
            if (string.IsNullOrEmpty(sampleId)) return;
            M.OnSampleReturned(sampleId, trayPocketIdx);
        }

        /// <summary>
        /// 旧的 279/276“点检时间到”弹窗是否屏蔽：模组点检任务进行中屏蔽并记风险。
        /// </summary>
        public static bool SuppressOldTrigger(WS ws, int code)
        {
            var t = Active;
            if (t == null) return false;
            ModuleCheckLog.Risk(ModuleCheckRisk.ClearOrOldTriggerDuringTask, t, string.Format("{0} 测试端返回旧点检码 {1}，模组点检进行中已忽略", ws == null ? "" : ws.disc, code));
            return true;
        }

        /// <summary>
        /// myfunc：工站到上下料面、确认要上下料、启动预取之前调用。
        /// 等待批次的任务在这里预约本工站本轮上料，并在后台切点检参数（与上下料并行）。
        /// 返回 false 表示不能继续（error 为原因）。
        /// </summary>
        public static bool OnStationAtFeed(WS ws, out string error)
        {
            error = "";
            var t = ActiveFor(ws);
            if (t == null) return true;
            if (t.NeedsReconcile)
            {
                error = "模组点检任务待人工核对（程序重启后），请在“模组自动点检设置”里核对后再运行";
                return false;
            }
            if (VAR.ClearMt)
            {
                if (t.Stage != ModuleCheckStage.WaitTargetBatch)
                    ModuleCheckLog.Risk(ModuleCheckRisk.ClearOrOldTriggerDuringTask, t, "点检进行中员工清料：样品按身份放回待测盘原穴，不进 OK/NG 盘");
                return true;
            }
            if (t.Stage == ModuleCheckStage.WaitTargetBatch)
            {
                if (t.CancelRequested) { M.Finish(ModuleCheckOutcome.Cancelled, "取消（尚未取料）"); return true; }
                string unfinished;
                if (StationBatchUnfinished(ws, out unfinished))
                {
                    // 本批普通料还没做完（上料中途停机后重新运行等）：这次上下料先把这批补齐、测完，
                    // 下一次到上下料面（这批测完下料时）再装样品，避免把未测的普通料下掉或用点检参数测
                    ModuleCheckLog.Info(ModuleCheckLogCat.Task, t, string.Format("{0} 本批普通料未做完（{1}），本次继续做完这批，下一批再装点检样品", ws.disc, unfinished), true);
                    return true;
                }
                M.SetStage(ModuleCheckStage.SwitchingToCheck, ws.disc + " 本轮普通料测试已结束，预约本轮上料");
                M.BeginSwitchToCheck();
                M.SetStage(ModuleCheckStage.Loading, "本轮从点检盘装样品，测试电脑切点检参数（后台）");
            }
            else if ((t.Stage == ModuleCheckStage.Loading || t.Stage == ModuleCheckStage.SwitchingToCheck) && t.CancelRequested)
            {
                // 装样品过程中员工取消（常见于停机/重启后）：本次到上下料面不再装样品，
                // 已在夹具里的样品按身份放回点检盘原穴，其余穴装普通料，测试电脑切回生产
                M.BeginReturn(ModuleCheckOutcome.Cancelled, "员工取消（装样品阶段）");
            }
            else if (t.Stage == ModuleCheckStage.Swapping && t.CancelRequested)
            {
                // 员工已取消：本次到上下料面不再换料，直接换下样品
                M.BeginReturn(ModuleCheckOutcome.Cancelled, "员工取消");
            }
            else if (t.Stage == ModuleCheckStage.Returning)
            {
                M.ProgressReturning();
            }
            return true;
        }

        /// <summary>
        /// WS.StartTestFlow 开头调用：装样品/换料完成后开始新一轮，归还阶段确认生产参数恢复后才测普通料。
        /// 返回 false 表示本次不能启动测试。
        /// </summary>
        public static bool BeforeStartTest(WS ws, out string error)
        {
            error = "";
            var t = ActiveFor(ws);
            if (t == null) return true;
            if (t.NeedsReconcile)
            {
                error = "模组点检任务待人工核对";
                return false;
            }
            switch (t.Stage)
            {
                case ModuleCheckStage.Loading:
                    {
                        var expected = t.Samples.ToDictionary(s => s.HomeFixturePocket, s => s.Id);
                        if (!VerifyFixture(ws, t, expected, "装样品后", out error)) return false;
                        bool ok;
                        int timeoutMs = (t.Snapshot.ModeSwitchTimeoutSec + 10) * 1000 * Math.Max(1, t.Pcs.Count);
                        if (!M.IsModeSwitchRunning && t.Pcs.Any(p => !p.CheckConfirmed))
                        {
                            // 上次切换失败（例如测试电脑异常后人工处理完重新运行）：重新切一次
                            ModuleCheckLog.Warn(ModuleCheckLogCat.Mode, t, "点检参数未确认，重新切换");
                            M.BeginSwitchToCheck();
                        }
                        if (!M.WaitModeSwitch(timeoutMs, () => VAR.gsys_set.bquit, out ok))
                        {
                            error = "等待测试电脑切点检参数超时";
                            ModuleCheckLog.Risk(ModuleCheckRisk.ModeSwitchProblem, t, error);
                            return false;
                        }
                        if (!ok || t.Pcs.Any(p => !p.CheckConfirmed))
                        {
                            error = "测试电脑切点检参数未确认：" + string.Join("；", t.Pcs.Where(p => !p.CheckConfirmed).Select(p => string.Format("PC{0} {1}", p.PcId, p.LastError)));
                            return false;
                        }
                        if (!WaitHandshakeEnd(t, out error)) return false;
                        M.BeginRound();
                        M.SetStage(ModuleCheckStage.Testing, ws.disc + " 样品已装好，开始第1轮");
                        return true;
                    }
                case ModuleCheckStage.Swapping:
                    {
                        if (!VerifyFixture(ws, t, t.ExpectedAfterSwap, "换料后", out error))
                        {
                            // 排列与计划不同，但仍是一次有效换料（例如计划按双臂分组、实际单臂一个大循环）时按实际排列继续
                            string why;
                            if (!SwapStillValid(ws, t, out why))
                            {
                                error += "；" + why;
                                return false;
                            }
                            ModuleCheckLog.Warn(ModuleCheckLogCat.Material, t, string.Format(
                                "换料后排列与计划不同，但样品齐全、与任务记录一致且每颗都换了穴，按实际排列继续：{0}",
                                ModuleCheckSwapPlan.FormatMap(t.CurrentFixtureMap())));
                            t.ExpectedAfterSwap = t.CurrentFixtureMap();
                            error = "";
                        }
                        if (!WaitHandshakeEnd(t, out error)) return false;
                        var r = M.BeginRound();
                        M.SetStage(ModuleCheckStage.Testing, string.Format("{0} 换料完成，开始第{1}轮", ws.disc, r == null ? 0 : r.RoundNo));
                        return true;
                    }
                case ModuleCheckStage.Returning:
                    {
                        // 样品已换下（最后一组可能还在吸嘴上等待尾部卸料），工站里是普通料：生产参数确认后才能测
                        int waited = 0;
                        int limit = (t.Snapshot.ModeSwitchTimeoutSec + 10) * 1000 * Math.Max(1, t.Pcs.Count) * 3;
                        while (true)
                        {
                            bool gaveUp;
                            if (M.IsProductionRestored(out gaveUp)) break;
                            if (gaveUp)
                            {
                                error = "测试电脑恢复生产参数失败（已重试3次），禁止用点检参数测普通料，请检查测试电脑后重新运行";
                                ModuleCheckLog.Risk(ModuleCheckRisk.ModeSwitchProblem, t, error);
                                M.ResetRestoreAttempts();   // 人工处理后重新运行时再试
                                return false;
                            }
                            if (VAR.gsys_set.bquit || waited > limit)
                            {
                                error = "等待测试电脑恢复生产参数超时";
                                return false;
                            }
                            M.ProgressReturning();
                            Thread.Sleep(200);
                            waited += 200;
                        }
                        if (FixtureHasSample(ws))
                        {
                            error = "工站里还有点检样品，不能按生产测试";
                            ModuleCheckLog.Risk(ModuleCheckRisk.SampleIdentityMismatch, t, error);
                            return false;
                        }
                        ModuleCheckLog.Info(ModuleCheckLogCat.Mode, t, ws.disc + " 生产参数已确认，开始测试普通料", true);
                        M.ProgressReturning();
                        return true;
                    }
                case ModuleCheckStage.Testing:
                    // 同一轮启动失败后的重发
                    ModuleCheckLog.Warn(ModuleCheckLogCat.Test, t, ws.disc + " 第" + t.CurrentRoundNo + "轮重新启动测试");
                    return true;
                default:
                    // 等待批次/切参数阶段：本工站测的是普通料
                    return true;
            }
        }

        /// <summary>
        /// WS 收到工站本轮最终结果（sta==0，各 PC 结果已写入 md.res）时调用。
        /// 返回 true 表示这是点检样品的结果，已由点检模块处理，调用方跳过生产结果保存/计数/MES。
        /// </summary>
        public static bool HandleStationFinalResult(WS ws)
        {
            var t = ActiveFor(ws);
            if (t == null || t.Stage != ModuleCheckStage.Testing) return false;
            var codes = new Dictionary<int, int>();
            var pcs = new Dictionary<int, int>();
            var idMismatch = new List<string>();
            var round = t.CurrentRound;
            foreach (WS.MdDat md in ws.list_md)
            {
                codes[md.Num] = md.res;
                pcs[md.Num] = md.PC_ID;
                string expect;
                if (round != null && round.Mapping.TryGetValue(md.Num, out expect) && expect != md.ModuleCheckSampleId)
                    idMismatch.Add(string.Format("穴{0} 记录{1} 实际{2}", md.Num, expect, md.ModuleCheckSampleId ?? "空"));
            }
            if (idMismatch.Count > 0)
                ModuleCheckLog.Risk(ModuleCheckRisk.SampleIdentityMismatch, t, "出结果时夹具样品身份与本轮记录不一致：" + string.Join("；", idMismatch));
            var v = M.SubmitRoundResults(t.CurrentRoundNo, codes, pcs);

            if (t.CancelRequested)
            {
                M.BeginReturn(ModuleCheckOutcome.Cancelled, "员工取消");
                return true;
            }
            switch (v)
            {
                case ModuleCheckVerdict.Pass:
                    M.BeginReturn(ModuleCheckOutcome.Pass, "");
                    break;
                case ModuleCheckVerdict.FailOverLimit:
                    {
                        string msg = string.Format("模组自动点检超过重试次数仍NG（{0}），样品放回点检盘、恢复生产，请检查", t.CurrentRound == null ? "" : t.CurrentRound.NgSummary);
                        ModuleCheckLog.Error(ModuleCheckLogCat.Alarm, t, msg);
                        // 只报警提示，不阻塞其它工站
                        VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, "模组点检NG", 20, true);
                        M.BeginReturn(ModuleCheckOutcome.FailOverLimit, "超过重试次数：" + (t.CurrentRound == null ? "" : t.CurrentRound.NgSummary));
                        break;
                    }
                case ModuleCheckVerdict.Retest:
                    {
                        string err;
                        var plan = M.PlanSwap(out err, CurrentSwapCycles());
                        if (plan == null)
                        {
                            M.BeginReturn(ModuleCheckOutcome.Aborted, "无法生成换料计划：" + err);
                            break;
                        }
                        t.ExpectedAfterSwap = plan.After;
                        M.SetStage(ModuleCheckStage.Swapping, string.Format("第{0}轮NG（{1}），下一次到上下料面组内循环换料后重测",
                            t.CurrentRoundNo, t.CurrentRound == null ? "" : t.CurrentRound.NgSummary));
                        break;
                    }
                default:
                    ModuleCheckLog.Error(ModuleCheckLogCat.Alarm, t, "本轮结果不完整，结束本次点检");
                    VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, "模组点检结果不完整", 20, true);
                    M.BeginReturn(ModuleCheckOutcome.Aborted, "结果不完整");
                    break;
            }
            return true;
        }

        /// <summary>WS.GetStatus 报联机错误时调用：切参数窗口内的掉线是预期的，单独记一条</summary>
        public static void OnLinkError(int pcId)
        {
            if (M.IsPcSwitching(pcId))
                ModuleCheckLog.Risk(ModuleCheckRisk.LinkErrDuringSwitch, Active, string.Format("PC{0} 在切参数（重启）期间联机错误，属预期，仅记录", pcId));
        }

        /// <summary>
        /// 按两条臂当前的穴位分工（myfunc.GetRunMode / GRR 流程设置的 FindMod）生成换位循环，
        /// 与 ThreeNozzleInitializeMode / ThreeNozzleRunGrrReload 的实际动作一致：
        /// 双臂 = LOW、HIGH 各一个循环；单臂运行或 GRR 流程（一条臂 ALL）= 穴 1-16 一个循环。
        /// </summary>
        private static List<ModuleCheckSwapCycle> CurrentSwapCycles()
        {
            var cycles = new List<ModuleCheckSwapCycle>();
            foreach (UpDownLoad ud in new[] { COM.UDLoad1, COM.UDLoad2 })
            {
                if (ud == null) continue;
                ModuleCheckGroup g;
                switch (ud.FindMod)
                {
                    case UpDownLoad.EM_FIN_MOD.ALL: g = ModuleCheckGroup.All; break;
                    case UpDownLoad.EM_FIN_MOD.LOW: g = ModuleCheckGroup.Low; break;
                    case UpDownLoad.EM_FIN_MOD.HIGH: g = ModuleCheckGroup.High; break;
                    default: continue;
                }
                if (!cycles.Any(c => c.Group == g))
                    cycles.Add(new ModuleCheckSwapCycle { Group = g, Order = ModuleCheckGroups.Order(g) });
            }
            return cycles;
        }

        /// <summary>
        /// 换料后排列与计划不一致时的放宽判断：夹具物料记录与任务记录完全一致、只有样品没有普通料、
        /// 样品与上一轮是同一批，并且每颗样品都不在上一轮的穴上。满足时这次换料仍然有效。
        /// </summary>
        private static bool SwapStillValid(WS ws, ModuleCheckTask t, out string why)
        {
            why = "";
            var task = t.CurrentFixtureMap();
            var actual = new Dictionary<int, string>();
            foreach (WS.MdDat md in ws.list_md)
            {
                if (md.res == (int)Product.EM_CM_RES.EMPTY) continue;
                if (string.IsNullOrEmpty(md.ModuleCheckSampleId)) { why = string.Format("穴{0} 是普通料", md.Num); return false; }
                actual[md.Num] = md.ModuleCheckSampleId;
            }
            if (actual.Count != task.Count || actual.Any(kv => { string v; return !task.TryGetValue(kv.Key, out v) || v != kv.Value; }))
            {
                why = string.Format("夹具物料记录[{0}]与任务记录[{1}]不一致", ModuleCheckSwapPlan.FormatMap(actual), ModuleCheckSwapPlan.FormatMap(task));
                return false;
            }
            var last = t.Rounds.Count == 0 ? null : t.Rounds[t.Rounds.Count - 1].Mapping;
            if (last == null || last.Count == 0) { why = "没有上一轮排列"; return false; }
            if (!new HashSet<string>(last.Values).SetEquals(actual.Values))
            {
                why = string.Format("样品与上一轮不是同一批：上一轮[{0}]", ModuleCheckSwapPlan.FormatMap(last));
                return false;
            }
            var stayed = actual.Where(kv => { string v; return last.TryGetValue(kv.Key, out v) && v == kv.Value; })
                .Select(kv => string.Format("穴{0}({1})", kv.Key, kv.Value)).ToList();
            if (stayed.Count > 0) { why = "样品没有换穴：" + string.Join("、", stayed); return false; }
            return true;
        }

        private static bool FixtureHasSample(WS ws)
        {
            return ws.list_md.Any(m => !string.IsNullOrEmpty(m.ModuleCheckSampleId));
        }

        private static bool VerifyFixture(WS ws, ModuleCheckTask t, Dictionary<int, string> expected, string when, out string error)
        {
            error = "";
            var diff = new List<string>();
            foreach (WS.MdDat md in ws.list_md)
            {
                string want;
                expected.TryGetValue(md.Num, out want);
                string have = md.ModuleCheckSampleId;
                bool hasPart = md.res != (int)Product.EM_CM_RES.EMPTY;
                if (want != null && (have != want || !hasPart))
                    diff.Add(string.Format("穴{0} 应为{1} 实际{2}", md.Num, want, hasPart ? (have ?? "普通料") : "空"));
                else if (want == null && hasPart)
                    diff.Add(string.Format("穴{0} 应为空 实际{1}", md.Num, have ?? "普通料"));
            }
            if (diff.Count > 0)
            {
                error = when + "夹具物料与计划不一致：" + string.Join("；", diff);
                ModuleCheckLog.Risk(ModuleCheckRisk.SampleIdentityMismatch, t, error);
                return false;
            }
            ModuleCheckLog.Info(ModuleCheckLogCat.Material, t, when + "夹具核对一致：" + ModuleCheckSwapPlan.FormatMap(expected), true);
            return true;
        }

        /// <summary>启动前确认每台 PC 上一轮握手已结束（结果标志 END），最多等 10 秒</summary>
        private static bool WaitHandshakeEnd(ModuleCheckTask t, out string error)
        {
            error = "";
            foreach (var pc in t.Pcs)
            {
                int waited = 0;
                string reason = "";
                while (true)
                {
                    int ret, startMark, resultMark;
                    ModuleCheckMachineAdapter.ReadHandshake(pc.PcId, out ret, out startMark, out resultMark);
                    if (ModuleCheckHandshake.IsReadyForStart(ret, resultMark, out reason)) break;
                    if (waited == 0)
                        ModuleCheckLog.Risk(ModuleCheckRisk.StartWhileBusy, t, string.Format("PC{0} 启动前握手未结束：{1}，等待", pc.PcId, reason));
                    if (waited >= 10000 || VAR.gsys_set.bquit)
                    {
                        error = string.Format("PC{0} 上一轮握手未结束（{1}），不能启动点检测试", pc.PcId, reason);
                        return false;
                    }
                    Thread.Sleep(200);
                    waited += 200;
                }
            }
            return true;
        }
    }
}

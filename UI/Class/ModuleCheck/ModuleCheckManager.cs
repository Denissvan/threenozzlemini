using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UI.ModuleCheck
{
    /// <summary>机台侧需要提供的能力（实现见 ModuleCheckMachineAdapter）</summary>
    public interface IModuleCheckMachine
    {
        /// <summary>工站 (0~3) 每个穴位(1~16) 的 PC_ID 与 IP；enabledOnly=true 时只返回启用穴位所属的 PC</summary>
        List<KeyValuePair<int, string>> GetStationPcPairs(int station, bool enabledOnly = false);
        /// <summary>点检盘穴位数；&lt;=0 表示未知</summary>
        int GetCheckTrayPocketCount(bool borrowFeedTray);
        IModuleCheckModeGateway ModeGateway { get; }
        /// <summary>与机台配置相关的校验（工站穴位启用、借用待测盘的穴位屏蔽、旧点检开关等）</summary>
        List<string> ValidateForTask(ModuleCheckSettings s);
        /// <summary>程序重启人工核对后，按任务记录恢复夹具穴位上的样品身份</summary>
        void RestoreFixtureIdentities(ModuleCheckTask t);
        /// <summary>整机退出/急停标志</summary>
        bool IsQuit { get; }
        string CurrentUser { get; }
    }

    /// <summary>
    /// 模组自动点检总控（单例）。负责：设置、定时调度、任务生命周期、持久化、日志、模式切换。
    /// 运行流程里的接入点（预约工站、取放、换位、归还、拦截清料等）在第二批代码中调用这里的方法。
    /// </summary>
    public sealed class ModuleCheckManager
    {
        private static readonly ModuleCheckManager instance = new ModuleCheckManager();
        public static ModuleCheckManager Instance { get { return instance; } }

        /// <summary>
        /// 运行流程是否已接入。未接入时调度只记录“到点”，不创建任务，避免产生无人处理的任务。
        /// 第二批接入 WS/myfunc/UpDownLoad 后改为 true。
        /// </summary>
        public static bool FlowIntegrated = true;

        private readonly object sync = new object();
        private IModuleCheckMachine machine;
        private System.Threading.Timer timer;
        private int ticking;
        private volatile Task<bool> modeTask;
        private readonly HashSet<string> notIntegratedLogged = new HashSet<string>();

        public ModuleCheckSettings Settings { get; private set; }
        public ModuleCheckPlanRegistry Registry { get; private set; }
        public ModuleCheckTask ActiveTask { get; private set; }
        public bool Started { get; private set; }

        /// <summary>任务或设置变化（可能在后台线程触发）</summary>
        public event Action Changed;

        private ModuleCheckManager()
        {
            Settings = new ModuleCheckSettings();
            Registry = new ModuleCheckPlanRegistry();
        }

        #region 启动 / 设置

        public void Start(IModuleCheckMachine m)
        {
            lock (sync)
            {
                if (Started) return;
                machine = m;
                Settings = ModuleCheckStore.LoadSettings();
                Registry = ModuleCheckStore.LoadRegistry();
                if (Registry.ActiveSince == DateTime.MinValue)
                {
                    Registry.ActiveSince = DateTime.Now;
                    string err;
                    ModuleCheckStore.SaveRegistry(Registry, out err);
                }
                ActiveTask = ModuleCheckStore.LoadActiveTask();
                if (ActiveTask != null && ActiveTask.IsActive)
                {
                    // 只有样品记录在夹具/吸嘴上才需要人工核对：程序不知道停机期间样品有没有被动过，
                    // 夹具穴位上的样品身份也要按记录恢复。样品都在点检盘原穴时直接继续，不打扰员工。
                    var away = ActiveTask.Samples.Where(s => s.Location != ModuleCheckSampleLocation.CheckTray).ToList();
                    if (away.Count > 0)
                    {
                        ActiveTask.NeedsReconcile = true;
                        ModuleCheckLog.Risk(ModuleCheckRisk.RecoveryMismatch, ActiveTask,
                            "程序启动时发现未完成的点检任务，以下样品不在点检盘，需人工核对位置后再继续：" +
                            string.Join(",", away.Select(s => s.Id + "@" + s.DescribeLocation())) + "。" + ActiveTask.Summary() +
                            " 最后确认动作=" + ActiveTask.LastConfirmedAction + " 未确认动作=" + ActiveTask.PendingAction);
                    }
                    else
                    {
                        ActiveTask.NeedsReconcile = false;
                        ModuleCheckLog.Warn(ModuleCheckLogCat.Task, ActiveTask,
                            "程序启动时发现未完成的点检任务，样品都在点检盘原穴，无需核对，继续执行。" + ActiveTask.Summary());
                    }
                    Persist();
                }
                else if (ActiveTask != null)
                {
                    ActiveTask = null;
                }
                Started = true;
                ModuleCheckLog.Info(ModuleCheckLogCat.Config, ActiveTask, string.Format("模组自动点检模块启动，启用={0}，计划={1}，提前{2}分钟，重试{3}次，工站{4}，借用待测盘={5}，流程接入={6}",
                    Settings.Enabled, string.Join("/", Settings.PlanTimes), Settings.AdvanceMinutes, Settings.RetryCount,
                    Settings.TargetStation + 1, Settings.BorrowFeedTray, FlowIntegrated), true);
                timer = new System.Threading.Timer(_ => Tick(), null, 2000, 2000);
            }
            RaiseChanged();
        }

        public bool SaveSettings(ModuleCheckSettings s, out List<string> errors)
        {
            errors = s.Validate(machine == null ? 0 : machine.GetCheckTrayPocketCount(s.BorrowFeedTray));
            if (s.Enabled && machine != null) errors.AddRange(machine.ValidateForTask(s));
            if (errors.Count > 0) return false;
            string err;
            lock (sync)
            {
                bool wasEnabled = Settings.Enabled;
                if (!ModuleCheckStore.SaveSettings(s, out err)) { errors.Add("保存失败：" + err); return false; }
                var old = Settings;
                Settings = s.Clone();
                if (!wasEnabled && s.Enabled)
                {
                    // 刚启用：之前的计划不补做
                    Registry.ActiveSince = DateTime.Now;
                    ModuleCheckStore.SaveRegistry(Registry, out err);
                }
                ModuleCheckLog.Info(ModuleCheckLogCat.Config, ActiveTask, string.Format("设置已保存（{0}）：启用{1}→{2}，计划[{3}]→[{4}]，提前{5}→{6}，重试{7}→{8}，工站{9}→{10}，参数{11}→{12}，超时{13}→{14}s，借用待测盘{15}→{16}，样品{17}→{18}颗",
                    CurrentUser(), old.Enabled, s.Enabled, string.Join("/", old.PlanTimes), string.Join("/", s.PlanTimes),
                    old.AdvanceMinutes, s.AdvanceMinutes, old.RetryCount, s.RetryCount, old.TargetStation + 1, s.TargetStation + 1,
                    old.CheckModeTemp, s.CheckModeTemp, old.ModeSwitchTimeoutSec, s.ModeSwitchTimeoutSec,
                    old.BorrowFeedTray, s.BorrowFeedTray, old.Samples.Count, s.Samples.Count), true);
                if (ActiveTask != null && ActiveTask.IsActive)
                    ModuleCheckLog.Info(ModuleCheckLogCat.Config, ActiveTask, "当前任务按创建时的参数快照继续，新设置从下一次任务生效");
            }
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// 只保存某台测试电脑的 IP（不校验其它设置）。ip 为空表示删除，改用工站配置里的 PC_IP。
        /// </summary>
        public bool SavePcIp(int pcId, string ip, out string error)
        {
            error = "";
            ip = (ip ?? "").Trim();
            System.Net.IPAddress parsed;
            if (ip.Length > 0 && (!System.Net.IPAddress.TryParse(ip, out parsed) || ip.Count(ch => ch == '.') != 3))
            {
                error = "IP 格式不正确：" + ip;
                return false;
            }
            lock (sync)
            {
                var s = Settings.Clone();
                string old;
                s.PcIps.TryGetValue(pcId, out old);
                if (ip.Length == 0) s.PcIps.Remove(pcId);
                else s.PcIps[pcId] = ip;
                if (!ModuleCheckStore.SaveSettings(s, out error)) return false;
                Settings = s;
                ModuleCheckLog.Info(ModuleCheckLogCat.Config, ActiveTask, string.Format("员工 {0} 设置 PC{1} 的 IP：{2} → {3}", CurrentUser(), pcId,
                    string.IsNullOrEmpty(old) ? "(未设置)" : old, ip.Length == 0 ? "(删除，改用工站配置)" : ip), true);
                if (ActiveTask != null && ActiveTask.IsActive)
                    ModuleCheckLog.Info(ModuleCheckLogCat.Config, ActiveTask, "当前任务仍用创建时的 IP，新 IP 从下一次任务生效");
            }
            RaiseChanged();
            return true;
        }

        #endregion

        #region 调度

        private void Tick()
        {
            if (Interlocked.Exchange(ref ticking, 1) == 1) return;
            try
            {
                ModuleCheckDuePlan due;
                lock (sync)
                {
                    if (ActiveTask != null && ActiveTask.IsActive)
                    {
                        if (ActiveTask.Stage == ModuleCheckStage.Returning) ProgressReturningLocked();
                        return;
                    }
                    if (!Settings.Enabled) return;
                    due = ModuleCheckScheduler.FindDue(Settings, DateTime.Now, Registry);
                    if (due == null) return;
                    if (!FlowIntegrated)
                    {
                        if (notIntegratedLogged.Add(due.PlanId))
                            ModuleCheckLog.Warn(ModuleCheckLogCat.Schedule, null, string.Format("计划 {0:MM-dd HH:mm} 已到（{1}），运行流程尚未接入，本版本不创建任务", due.PlanTime, ModuleCheckText.Trigger(due.Trigger)));
                        return;
                    }
                }
                string err;
                CreateTask(due.Trigger, due, out err);
            }
            catch (Exception ex)
            {
                ModuleCheckLog.Error(ModuleCheckLogCat.Schedule, ActiveTask, "调度异常：" + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref ticking, 0);
            }
        }

        /// <summary>人工立即点检</summary>
        public ModuleCheckTask CreateManualTask(out string error)
        {
            return CreateTask(ModuleCheckTrigger.Manual, null, out error);
        }

        private ModuleCheckTask CreateTask(ModuleCheckTrigger trigger, ModuleCheckDuePlan due, out string error)
        {
            error = "";
            ModuleCheckTask t;
            lock (sync)
            {
                if (ActiveTask != null && ActiveTask.IsActive) { error = "已有未结束的点检任务：" + ActiveTask.TaskId; return null; }
                if (!FlowIntegrated) { error = "运行流程尚未接入，本版本不能执行点检"; return null; }
                var snap = Settings.Clone();
                t = new ModuleCheckTask();
                t.TaskId = "MC" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                t.Trigger = trigger;
                t.CreatedTime = DateTime.Now;
                t.CreatedBy = CurrentUser();
                t.Snapshot = snap;
                t.MaxRounds = snap.MaxRounds;
                if (due != null)
                {
                    t.PlanId = due.PlanId;
                    t.PlanTime = due.PlanTime;
                }
                int n = 1;
                foreach (var slot in snap.Samples.OrderBy(s => s.FixturePocket))
                {
                    t.Samples.Add(new ModuleCheckSample
                    {
                        Id = string.Format("S{0:00}", n++),
                        TrayPocketIdx = slot.TrayPocketIdx,
                        HomeFixturePocket = slot.FixturePocket,
                        Location = ModuleCheckSampleLocation.CheckTray,
                    });
                }

                // 校验设置与 PC 映射
                var errs = snap.Validate(machine == null ? 0 : machine.GetCheckTrayPocketCount(snap.BorrowFeedTray));
                if (machine != null) errs.AddRange(machine.ValidateForTask(snap));
                List<string> mapErrs;
                var pcs = ModuleCheckPcMap.Build(machine == null ? new List<KeyValuePair<int, string>>() : machine.GetStationPcPairs(snap.TargetStation, true), out mapErrs);
                if (pcs.Count == 0) mapErrs.Add(string.Format("工站{0}没有可用的测试电脑", snap.TargetStation + 1));
                foreach (var pc in pcs) t.Pcs.Add(new ModuleCheckPcState { PcId = pc.PcId, Ip = pc.Ip });

                if (due != null)
                {
                    Registry.MarkHandled(due.PlanId, string.Format("{0} 任务{1}", ModuleCheckText.Trigger(trigger), t.TaskId));
                    foreach (var m in due.MergedPlanIds)
                        Registry.MarkHandled(m, string.Format("错过，合并到任务{0}补做", t.TaskId));
                    string e2;
                    ModuleCheckStore.SaveRegistry(Registry, out e2);
                }

                ActiveTask = t;
                ModuleCheckLog.Info(ModuleCheckLogCat.Task, t, string.Format("创建任务：{0}，计划时间={1}，创建人={2}，工站{3}，总轮数上限{4}（重试{5}次），参数{6}，借用待测盘={7}，PC=[{8}]，样品=[{9}]{10}",
                    ModuleCheckText.Trigger(trigger),
                    t.PlanTime.HasValue ? t.PlanTime.Value.ToString("yyyy-MM-dd HH:mm") : "-",
                    t.CreatedBy, snap.TargetStation + 1, t.MaxRounds, snap.RetryCount, snap.CheckModeTemp, snap.BorrowFeedTray,
                    string.Join(",", t.Pcs.Select(p => string.Format("PC{0}={1}", p.PcId, p.Ip))),
                    string.Join(",", t.Samples.Select(s => string.Format("{0}:盘{1}→穴{2}", s.Id, s.TrayPocketIdx, s.HomeFixturePocket))),
                    due != null && due.MergedPlanIds.Count > 0 ? "，合并错过的计划：" + string.Join("/", due.MergedPlanIds) : ""), true);

                if (errs.Count > 0 || mapErrs.Count > 0)
                {
                    if (mapErrs.Count > 0) ModuleCheckLog.Risk(ModuleCheckRisk.PcMappingMismatch, t, string.Join("；", mapErrs));
                    error = string.Join("；", errs.Concat(mapErrs));
                    FinishLocked(ModuleCheckOutcome.Aborted, "任务创建校验失败：" + error);
                    return null;
                }
                Persist();
            }
            RaiseChanged();
            return t;
        }

        #endregion

        #region 任务过程（供运行流程调用）

        public void SetStage(ModuleCheckStage stage, string note)
        {
            lock (sync)
            {
                var t = ActiveTask;
                if (t == null || !t.IsActive) return;
                if (t.Stage == stage) return;
                long ms = (long)(DateTime.Now - t.StageEnterTime).TotalMilliseconds;
                t.StageTimings.Add(string.Format("{0}={1}ms", ModuleCheckText.Stage(t.Stage), ms));
                ModuleCheckLog.Info(ModuleCheckLogCat.Timing, t, string.Format("阶段 {0} 用时 {1}ms", ModuleCheckText.Stage(t.Stage), ms));
                ModuleCheckLog.Info(ModuleCheckLogCat.Task, t, string.Format("阶段 {0} → {1}{2}", ModuleCheckText.Stage(t.Stage), ModuleCheckText.Stage(stage), string.IsNullOrEmpty(note) ? "" : "：" + note), true);
                t.Stage = stage;
                t.StageEnterTime = DateTime.Now;
                Persist();
            }
            RaiseChanged();
        }

        /// <summary>动作开始前登记（未确认动作），用于中断后核对</summary>
        public void BeginAction(string action)
        {
            lock (sync)
            {
                var t = ActiveTask;
                if (t == null) return;
                t.PendingAction = action;
                Persist();
                ModuleCheckLog.Info(ModuleCheckLogCat.Material, t, "开始：" + action);
            }
        }

        /// <summary>动作完成后提交</summary>
        public void ConfirmAction(string action)
        {
            lock (sync)
            {
                var t = ActiveTask;
                if (t == null) return;
                t.LastConfirmedAction = action;
                t.PendingAction = "";
                Persist();
                ModuleCheckLog.Info(ModuleCheckLogCat.Material, t, "完成：" + action);
            }
        }

        /// <summary>样品被取到嘴上</summary>
        public void OnSamplePicked(string sampleId, int arm, int nozzle, string from)
        {
            UpdateSample(sampleId, s =>
            {
                s.Location = ModuleCheckSampleLocation.Nozzle;
                s.Returned = false;
                s.Arm = arm;
                s.Nozzle = nozzle;
                s.FixturePocket = 0;
            }, string.Format("取起 {{0}}：{0} → 臂{1}嘴N{2}", from, arm + 1, nozzle + 1));
        }

        /// <summary>样品放入夹具穴</summary>
        public void OnSamplePlaced(string sampleId, int pocket)
        {
            UpdateSample(sampleId, s =>
            {
                s.Location = ModuleCheckSampleLocation.Fixture;
                s.FixturePocket = pocket;
                s.Arm = -1;
                s.Nozzle = -1;
            }, string.Format("放入 {{0}} → 夹具穴{0}", pocket));
        }

        /// <summary>样品放回点检盘原穴</summary>
        public void OnSampleReturned(string sampleId, int trayPocketIdx)
        {
            var t = ActiveTask;
            var s0 = t == null ? null : t.FindSample(sampleId);
            if (s0 != null && s0.TrayPocketIdx != trayPocketIdx)
                ModuleCheckLog.Risk(ModuleCheckRisk.SampleIdentityMismatch, t, string.Format("{0} 原穴为点检盘{1}，实际放回{2}", sampleId, s0.TrayPocketIdx, trayPocketIdx));
            UpdateSample(sampleId, s =>
            {
                s.Location = ModuleCheckSampleLocation.CheckTray;
                s.Returned = true;
                s.FixturePocket = 0;
                s.Arm = -1;
                s.Nozzle = -1;
            }, string.Format("归还 {{0}} → 点检盘{0}", trayPocketIdx));
        }

        /// <summary>
        /// 员工确认样品已人工放回点检盘原穴（人工对账清掉吸嘴记录、或在设置界面人工归位时）。
        /// 只改记录在吸嘴上/未知位置的样品；在夹具里的样品不动。
        /// </summary>
        public void MarkSamplesManuallyHomed(IEnumerable<string> sampleIds, string note)
        {
            lock (sync)
            {
                var t = ActiveTask;
                if (t == null || !t.IsActive || sampleIds == null) return;
                var done = new List<string>();
                foreach (var id in sampleIds)
                {
                    var s = t.FindSample(id);
                    if (s == null || s.Location == ModuleCheckSampleLocation.CheckTray || s.Location == ModuleCheckSampleLocation.Fixture) continue;
                    done.Add(string.Format("{0}（原记录 {1}）→ 点检盘{2}", s.Id, s.DescribeLocation(), s.TrayPocketIdx));
                    s.Location = ModuleCheckSampleLocation.CheckTray;
                    s.Returned = true;
                    s.FixturePocket = 0;
                    s.Arm = -1;
                    s.Nozzle = -1;
                }
                if (done.Count == 0) return;
                ModuleCheckLog.Risk(ModuleCheckRisk.ManualSampleFix, t, string.Format("员工 {0} 确认样品已人工放回点检盘原穴（{1}）：{2}",
                    CurrentUser(), note ?? "", string.Join("；", done)));
                Persist();
                if (t.Stage == ModuleCheckStage.Returning) ProgressReturningLocked();
            }
            RaiseChanged();
        }

        /// <summary>
        /// 员工人工结束本次点检：样品已全部人工放回点检盘原穴、夹具和吸嘴里都没有样品（常见于取消后重启、员工手动取料）。
        /// 样品全部记为已归还、清掉夹具穴位上的样品身份，然后只剩“测试电脑恢复生产参数”：
        /// 后台恢复并回读确认后任务结束；pcProductionConfirmedByOperator=true 表示员工已用联调工具确认是生产参数，直接结束。
        /// </summary>
        public void ForceEndByOperator(bool pcProductionConfirmedByOperator)
        {
            bool alreadyReturning;
            lock (sync)
            {
                var t = ActiveTask;
                if (t == null || !t.IsActive) return;
                var moved = t.Samples.Where(s => s.Location != ModuleCheckSampleLocation.CheckTray)
                    .Select(s => s.Id + "@" + s.DescribeLocation()).ToList();
                foreach (var s in t.Samples)
                {
                    if (s.Location != ModuleCheckSampleLocation.CheckTray) s.Returned = true;
                    s.Location = ModuleCheckSampleLocation.CheckTray;
                    s.FixturePocket = 0;
                    s.Arm = -1;
                    s.Nozzle = -1;
                }
                if (!t.CancelRequested)
                {
                    t.CancelRequested = true;
                    t.CancelBy = CurrentUser();
                }
                t.NeedsReconcile = false;
                t.RestoreAttempts = 0;
                ModuleCheckLog.Risk(ModuleCheckRisk.ManualSampleFix, t, string.Format("员工 {0} 人工结束本次点检：确认样品已全部人工放回点检盘原穴、夹具和吸嘴无样品{1}",
                    CurrentUser(), moved.Count > 0 ? "（原记录：" + string.Join(",", moved) + "）" : ""));
                if (machine != null) machine.RestoreFixtureIdentities(t);
                if (pcProductionConfirmedByOperator)
                {
                    foreach (var pc in t.Pcs.Where(p => p.CheckRequested && !p.ProductionRestored))
                    {
                        pc.ProductionRestored = true;
                        ModuleCheckLog.Risk(ModuleCheckRisk.PcMappingMismatch, t, string.Format("员工 {0} 确认 PC{1} 已是生产参数（人工确认，未经回读）", CurrentUser(), pc.PcId));
                    }
                }
                Persist();
                alreadyReturning = t.Stage == ModuleCheckStage.Returning;
                if (alreadyReturning)
                {
                    t.PendingOutcome = ModuleCheckOutcome.Cancelled;
                    ProgressReturningLocked();
                }
            }
            if (!alreadyReturning) BeginReturn(ModuleCheckOutcome.Cancelled, "员工人工结束（样品已人工放回）");
            RaiseChanged();
        }

        /// <summary>记录在吸嘴上或位置未知的样品（设置界面“人工归位”用）</summary>
        public List<ModuleCheckSample> SamplesOffTrayAndFixture()
        {
            lock (sync)
            {
                var t = ActiveTask;
                if (t == null || !t.IsActive) return new List<ModuleCheckSample>();
                return t.Samples.Where(s => s.Location == ModuleCheckSampleLocation.Nozzle || s.Location == ModuleCheckSampleLocation.Unknown).ToList();
            }
        }

        private void UpdateSample(string sampleId, Action<ModuleCheckSample> apply, string fmt)
        {
            lock (sync)
            {
                var t = ActiveTask;
                if (t == null) return;
                var s = t.FindSample(sampleId);
                if (s == null)
                {
                    ModuleCheckLog.Risk(ModuleCheckRisk.SampleIdentityMismatch, t, "任务中没有样品 " + sampleId);
                    return;
                }
                apply(s);
                Persist();
                ModuleCheckLog.Info(ModuleCheckLogCat.Material, t, string.Format(fmt, sampleId));
            }
            RaiseChanged();
        }

        /// <summary>开始新一轮测试（装料或换位核对完成后、StartTestFlow 之前调用）</summary>
        public ModuleCheckRound BeginRound()
        {
            ModuleCheckRound r;
            lock (sync)
            {
                var t = ActiveTask;
                if (t == null) return null;
                r = new ModuleCheckRound();
                r.RoundNo = t.CurrentRoundNo + 1;
                r.StartTime = DateTime.Now;
                r.Mapping = t.CurrentFixtureMap();
                int rep = ModuleCheckSwapPlanner.FindRepeatedRound(t.Rounds, r.Mapping);
                if (rep > 0) ModuleCheckLog.Warn(ModuleCheckLogCat.Judge, t, string.Format("第{0}轮排列与第{1}轮相同（组内样品已循环一圈）", r.RoundNo, rep));
                t.Rounds.Add(r);
                Persist();
                ModuleCheckLog.Info(ModuleCheckLogCat.Test, t, string.Format("第{0}/{1}轮开始，映射={2}", r.RoundNo, t.MaxRounds, ModuleCheckSwapPlan.FormatMap(r.Mapping)), true);
            }
            RaiseChanged();
            return r;
        }

        /// <summary>
        /// 两台 PC 都到 sta==0 后提交本轮逐工位结果。返回本轮结论。
        /// 同一轮重复提交只记日志、返回首次结论（防止重复读到同一份最终结果多算一轮）。
        /// codes：穴位(1~16)→结果码；pcOfPocket：穴位→PC_ID。
        /// </summary>
        public ModuleCheckVerdict SubmitRoundResults(int roundNo, Dictionary<int, int> codes, Dictionary<int, int> pcOfPocket)
        {
            ModuleCheckVerdict v;
            lock (sync)
            {
                var t = ActiveTask;
                if (t == null) return ModuleCheckVerdict.Pending;
                var r = t.Rounds.FirstOrDefault(x => x.RoundNo == roundNo);
                if (r == null)
                {
                    ModuleCheckLog.Error(ModuleCheckLogCat.Judge, t, string.Format("提交第{0}轮结果，但该轮没有开始记录", roundNo));
                    return ModuleCheckVerdict.Pending;
                }
                if (r.Verdict != ModuleCheckVerdict.Pending && r.Verdict != ModuleCheckVerdict.Incomplete)
                {
                    ModuleCheckLog.Risk(ModuleCheckRisk.DuplicateResult, t, string.Format("第{0}轮结果重复提交，已忽略（首次结论 {1}）", roundNo, ModuleCheckText.Verdict(r.Verdict)));
                    return r.Verdict;
                }
                r.Results.Clear();
                for (int p = 1; p <= ModuleCheckSettings.FixturePocketCount; p++)
                {
                    int code;
                    if (!codes.TryGetValue(p, out code)) code = ModuleCheckJudge.CodeNoResult;
                    string sid;
                    r.Mapping.TryGetValue(p, out sid);
                    int pc;
                    pcOfPocket.TryGetValue(p, out pc);
                    if (sid != null || (code != ModuleCheckJudge.CodeNoResult && code != ModuleCheckJudge.CodeEmpty))
                        r.Results.Add(new ModuleCheckPocketResult { Pocket = p, SampleId = sid ?? "", PcId = pc, Code = code });
                }
                var o = ModuleCheckJudge.Judge(r.Mapping, codes, roundNo, t.MaxRounds);
                r.Verdict = o.Verdict;
                r.NgSummary = o.NgSummary;
                r.EndTime = DateTime.Now;
                Persist();

                string detail = string.Join(",", r.Results.OrderBy(x => x.Pocket).Select(x => string.Format("{0}:{1}={2}", x.Pocket, string.IsNullOrEmpty(x.SampleId) ? "空" : x.SampleId, x.Code)));
                long ms = r.StartTime.HasValue ? (long)(r.EndTime.Value - r.StartTime.Value).TotalMilliseconds : 0;
                if (o.Verdict == ModuleCheckVerdict.Incomplete)
                    ModuleCheckLog.Error(ModuleCheckLogCat.Judge, t, string.Format("第{0}轮结果不完整，缺结果的穴：{1}；结果={2}", roundNo, string.Join(",", o.MissingPockets), detail));
                else
                    ModuleCheckLog.Write(o.Verdict == ModuleCheckVerdict.Pass ? ModuleCheckLogLevel.Info : ModuleCheckLogLevel.Warn, ModuleCheckLogCat.Judge, t,
                        string.Format("第{0}/{1}轮判定：{2}{3}，用时{4}ms，结果={5}", roundNo, t.MaxRounds, ModuleCheckText.Verdict(o.Verdict),
                        string.IsNullOrEmpty(o.NgSummary) ? "" : "，NG=" + o.NgSummary, ms, detail), true);
                if (o.UnexpectedPockets.Count > 0)
                    ModuleCheckLog.Risk(ModuleCheckRisk.SampleIdentityMismatch, t, string.Format("第{0}轮无样品的穴收到了结果：{1}", roundNo, string.Join(",", o.UnexpectedPockets)));
                if (o.Verdict != ModuleCheckVerdict.Incomplete) ModuleCheckLog.AppendResultRows(t, r);
                v = o.Verdict;
            }
            RaiseChanged();
            return v;
        }

        /// <summary>生成本次换料计划（不改变样品位置）</summary>
        /// <param name="cycles">按当前运行模式的臂分工生成的换位循环；null = 双臂默认 LOW/HIGH</param>
        public ModuleCheckSwapPlan PlanSwap(out string error, IList<ModuleCheckSwapCycle> cycles = null)
        {
            error = "";
            lock (sync)
            {
                var t = ActiveTask;
                if (t == null) { error = "没有任务"; return null; }
                var plan = ModuleCheckSwapPlanner.Plan(t.CurrentFixtureMap(), cycles, out error);
                if (plan == null)
                {
                    ModuleCheckLog.Error(ModuleCheckLogCat.Material, t, "生成换料计划失败：" + error);
                    return null;
                }
                ModuleCheckLog.Info(ModuleCheckLogCat.Material, t, string.Format("换料计划（臂分工：{3}）：换前[{0}] 换后[{1}] 步骤：{2}",
                    ModuleCheckSwapPlan.FormatMap(plan.Before), ModuleCheckSwapPlan.FormatMap(plan.After),
                    string.Join("；", plan.AllMoves.Select(m => m.ToString())),
                    cycles == null || cycles.Count == 0 ? "双臂 LOW/HIGH（默认）" : string.Join("、", cycles.Select(c => ModuleCheckGroups.Name(c.Group)))), true);
                return plan;
            }
        }

        /// <summary>
        /// 换料完成后核对：actual 为实际夹具 穴位→样品（由机台物料记录得到）。
        /// 与计划不一致时记风险并返回 false。
        /// </summary>
        public bool VerifySwap(ModuleCheckSwapPlan plan, Dictionary<int, string> actual)
        {
            lock (sync)
            {
                var t = ActiveTask;
                var diff = new List<string>();
                foreach (var kv in plan.After)
                {
                    string a;
                    if (!actual.TryGetValue(kv.Key, out a)) a = "空";
                    if (a != kv.Value) diff.Add(string.Format("穴{0} 计划{1} 实际{2}", kv.Key, kv.Value, a));
                }
                foreach (var kv in actual)
                    if (!plan.After.ContainsKey(kv.Key)) diff.Add(string.Format("穴{0} 计划空 实际{1}", kv.Key, kv.Value));
                if (diff.Count > 0)
                {
                    ModuleCheckLog.Risk(ModuleCheckRisk.SampleIdentityMismatch, t, "换料后核对不一致：" + string.Join("；", diff));
                    return false;
                }
                ModuleCheckLog.Info(ModuleCheckLogCat.Material, t, "换料后核对一致：" + ModuleCheckSwapPlan.FormatMap(actual), true);
                return true;
            }
        }

        /// <summary>员工点“取消本次点检”</summary>
        public void RequestCancel()
        {
            lock (sync)
            {
                var t = ActiveTask;
                if (t == null || !t.IsActive) return;
                if (t.CancelRequested) return;
                t.CancelRequested = true;
                t.CancelBy = CurrentUser();
                ModuleCheckLog.Warn(ModuleCheckLogCat.Task, t, string.Format("员工 {0} 请求取消本次点检，当前阶段 {1}", t.CancelBy, ModuleCheckText.Stage(t.Stage)));
                bool nothingPhysical = t.Stage == ModuleCheckStage.WaitTargetBatch && t.Pcs.All(p => !p.CheckRequested)
                    && t.Samples.All(s => s.Location == ModuleCheckSampleLocation.CheckTray);
                if (nothingPhysical) FinishLocked(ModuleCheckOutcome.Cancelled, "取消（尚未切参数、未取料）");
                else Persist();
            }
            RaiseChanged();
        }

        public void Finish(ModuleCheckOutcome outcome, string reason)
        {
            lock (sync) { FinishLocked(outcome, reason); }
            RaiseChanged();
        }

        private void FinishLocked(ModuleCheckOutcome outcome, string reason)
        {
            var t = ActiveTask;
            if (t == null) return;
            long ms = (long)(DateTime.Now - t.StageEnterTime).TotalMilliseconds;
            t.StageTimings.Add(string.Format("{0}={1}ms", ModuleCheckText.Stage(t.Stage), ms));
            t.Stage = ModuleCheckStage.Finished;
            t.Outcome = outcome;
            t.FailReason = reason ?? "";
            t.FinishedTime = DateTime.Now;
            var notReturned = t.Samples.Where(s => !s.Returned && s.Location != ModuleCheckSampleLocation.CheckTray).Select(s => s.Id + "@" + s.DescribeLocation()).ToList();
            var notRestored = t.Pcs.Where(p => p.CheckRequested && !p.ProductionRestored).Select(p => "PC" + p.PcId).ToList();
            ModuleCheckLog.Write(outcome == ModuleCheckOutcome.Pass ? ModuleCheckLogLevel.Info : ModuleCheckLogLevel.Warn, ModuleCheckLogCat.Task, t,
                string.Format("任务结束：{0}{1}；总用时{2:0}s；阶段耗时[{3}]{4}{5}",
                    ModuleCheckText.Outcome(outcome), string.IsNullOrEmpty(reason) ? "" : "（" + reason + "）",
                    (t.FinishedTime.Value - t.CreatedTime).TotalSeconds, string.Join(",", t.StageTimings),
                    notReturned.Count > 0 ? "；未归还：" + string.Join(",", notReturned) : "",
                    notRestored.Count > 0 ? "；未恢复生产：" + string.Join(",", notRestored) : ""), true);
            if (notReturned.Count > 0) ModuleCheckLog.Risk(ModuleCheckRisk.SampleIdentityMismatch, t, "任务结束时仍有样品不在点检盘：" + string.Join(",", notReturned));
            if (notRestored.Count > 0) ModuleCheckLog.Risk(ModuleCheckRisk.PcMappingMismatch, t, "任务结束时仍有电脑未确认恢复生产：" + string.Join(",", notRestored));
            string err;
            if (!ModuleCheckStore.ArchiveTask(t, out err))
                ModuleCheckLog.Error(ModuleCheckLogCat.Task, t, "任务归档失败：" + err);
            ActiveTask = null;
        }

        /// <summary>人工核对完成（程序重启后的未完成任务）</summary>
        public void ConfirmReconciled(string note)
        {
            lock (sync)
            {
                var t = ActiveTask;
                if (t == null || !t.NeedsReconcile) return;
                t.NeedsReconcile = false;
                ModuleCheckLog.Warn(ModuleCheckLogCat.Task, t, string.Format("员工 {0} 确认已核对：{1}；样品位置={2}", CurrentUser(), note,
                    string.Join(",", t.Samples.Select(s => s.Id + "@" + s.DescribeLocation()))));
                if (machine != null) machine.RestoreFixtureIdentities(t);
                Persist();
            }
            RaiseChanged();
        }

        #endregion

        #region 结束阶段：归还样品 + 恢复生产

        /// <summary>本轮判定后进入归还阶段：记下结论，后台开始恢复生产参数</summary>
        public void BeginReturn(ModuleCheckOutcome outcome, string reason)
        {
            lock (sync)
            {
                var t = ActiveTask;
                if (t == null || !t.IsActive) return;
                t.PendingOutcome = outcome;
                if (!string.IsNullOrEmpty(reason)) t.FailReason = reason;
                ModuleCheckLog.Write(outcome == ModuleCheckOutcome.Pass ? ModuleCheckLogLevel.Info : ModuleCheckLogLevel.Warn, ModuleCheckLogCat.Task, t,
                    string.Format("点检结论：{0}{1}，开始归还样品并恢复生产参数", ModuleCheckText.Outcome(outcome), string.IsNullOrEmpty(reason) ? "" : "（" + reason + "）"), true);
            }
            SetStage(ModuleCheckStage.Returning, "归还样品、恢复生产参数");
            lock (sync) { ProgressReturningLocked(); }
        }

        /// <summary>归还阶段推进：先确保生产参数恢复（最多尝试 3 次），样品全部归还后结束任务</summary>
        public void ProgressReturning()
        {
            lock (sync) { ProgressReturningLocked(); }
        }

        private void ProgressReturningLocked()
        {
            var t = ActiveTask;
            if (t == null || !t.IsActive || t.Stage != ModuleCheckStage.Returning) return;
            if (IsModeSwitchRunning) return;
            bool needRestore = t.Pcs.Any(p => p.CheckRequested && !p.ProductionRestored);
            if (needRestore)
            {
                if (t.RestoreAttempts < 3)
                {
                    t.RestoreAttempts++;
                    ModuleCheckLog.Info(ModuleCheckLogCat.Mode, t, string.Format("恢复生产参数，第{0}次尝试", t.RestoreAttempts));
                    Persist();
                    BeginRestoreProductionLocked();
                }
                return;
            }
            if (t.Samples.All(s => s.Returned || s.Location == ModuleCheckSampleLocation.CheckTray))
                FinishLocked(t.PendingOutcome == ModuleCheckOutcome.None ? ModuleCheckOutcome.Aborted : t.PendingOutcome, t.FailReason);
        }

        public void ResetRestoreAttempts()
        {
            lock (sync)
            {
                var t = ActiveTask;
                if (t == null) return;
                t.RestoreAttempts = 0;
                Persist();
            }
        }

        /// <summary>归还阶段：目标工站的电脑是否都已确认恢复生产参数</summary>
        public bool IsProductionRestored(out bool gaveUp)
        {
            lock (sync)
            {
                var t = ActiveTask;
                gaveUp = false;
                if (t == null || !t.IsActive) return true;
                bool restored = t.Pcs.All(p => !p.CheckRequested || p.ProductionRestored);
                gaveUp = !restored && t.RestoreAttempts >= 3 && !IsModeSwitchRunning;
                return restored;
            }
        }

        /// <summary>等待当前模式切换完成；返回是否在超时内完成，ok 为切换结果</summary>
        public bool WaitModeSwitch(int timeoutMs, Func<bool> quit, out bool ok)
        {
            ok = false;
            var mt = modeTask;
            if (mt == null) return true;
            int waited = 0;
            while (!mt.IsCompleted)
            {
                if (quit != null && quit()) return false;
                if (waited >= timeoutMs) return false;
                Thread.Sleep(100);
                waited += 100;
            }
            ok = mt.Result;
            return true;
        }

        #endregion

        #region 模式切换（后台线程）

        /// <summary>某台 PC 是否处于切参数窗口（用于屏蔽/标注这段时间的联机错误）</summary>
        public bool IsPcSwitching(int pcId)
        {
            var mt = modeTask;
            var t = ActiveTask;
            if (mt == null || mt.IsCompleted || t == null) return false;
            return t.Pcs.Any(p => p.PcId == pcId);
        }

        public bool IsModeSwitchRunning
        {
            get { var mt = modeTask; return mt != null && !mt.IsCompleted; }
        }

        /// <summary>后台切点检；完成后 Task.Result 为是否成功</summary>
        public Task<bool> BeginSwitchToCheck()
        {
            return BeginModeTask(true);
        }

        /// <summary>后台恢复生产</summary>
        public Task<bool> BeginRestoreProduction()
        {
            return BeginModeTask(false);
        }

        private void BeginRestoreProductionLocked()
        {
            BeginModeTask(false);
        }

        private Task<bool> BeginModeTask(bool toCheck)
        {
            lock (sync)
            {
                var t = ActiveTask;
                if (t == null || machine == null) return Task.FromResult(false);
                if (IsModeSwitchRunning) return modeTask;
                var switcher = new ModuleCheckModeSwitcher(machine.ModeGateway, () => machine.IsQuit);
                modeTask = Task.Run(() =>
                {
                    string err;
                    bool ok;
                    try
                    {
                        ok = toCheck ? switcher.SwitchToCheck(t, () => { lock (sync) Persist(); }, out err)
                                     : switcher.RestoreProduction(t, () => { lock (sync) Persist(); }, out err);
                    }
                    catch (Exception ex)
                    {
                        ok = false;
                        err = ex.Message;
                    }
                    if (!ok) ModuleCheckLog.Error(ModuleCheckLogCat.Mode, t, (toCheck ? "切点检失败：" : "恢复生产失败：") + err);
                    lock (sync) Persist();
                    RaiseChanged();
                    return ok;
                });
                return modeTask;
            }
        }

        #endregion

        private void Persist()
        {
            var t = ActiveTask;
            if (t == null) return;
            string err;
            if (!ModuleCheckStore.SaveActiveTask(t, out err))
                ModuleCheckLog.Error(ModuleCheckLogCat.Task, t, "任务持久化失败：" + err);
        }

        private string CurrentUser()
        {
            try { return machine == null ? "" : machine.CurrentUser; }
            catch { return ""; }
        }

        private void RaiseChanged()
        {
            try
            {
                var h = Changed;
                if (h != null) h();
            }
            catch { }
        }
    }
}

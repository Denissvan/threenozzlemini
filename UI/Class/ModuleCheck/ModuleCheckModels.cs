using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace UI.ModuleCheck
{
    // 模组自动点检（定时、点检盘供料、整批循环换料重测）的数据模型。
    // 与“环境点检”(AutoInspectionParameter) 以及旧人工模组点检 (VAR.isAutoChkMode) 无关。
    // 本目录下的类不引用 WS/COM 等机台对象，机台相关的部分全部放在 ModuleCheckMachineAdapter。

    /// <summary>任务阶段</summary>
    public enum ModuleCheckStage
    {
        /// <summary>已创建，等待目标工站可预约的完整上料轮次</summary>
        WaitTargetBatch = 0,
        /// <summary>目标测试电脑切换点检参数中（含重启等待与回读）</summary>
        SwitchingToCheck = 1,
        /// <summary>等待上料（模式已确认）</summary>
        WaitLoad = 2,
        /// <summary>从点检盘装入样品中</summary>
        Loading = 3,
        /// <summary>整轮测试中</summary>
        Testing = 4,
        /// <summary>组内循环换料中</summary>
        Swapping = 5,
        /// <summary>样品归还点检盘中</summary>
        Returning = 6,
        /// <summary>测试电脑恢复生产参数中</summary>
        RestoringProduction = 7,
        /// <summary>任务已结束（见 Outcome）</summary>
        Finished = 8,
    }

    /// <summary>任务结果</summary>
    public enum ModuleCheckOutcome
    {
        None = 0,
        /// <summary>某一轮全部工位结果为 0</summary>
        Pass = 1,
        /// <summary>超过重试次数仍有 NG</summary>
        FailOverLimit = 2,
        /// <summary>员工取消</summary>
        Cancelled = 3,
        /// <summary>配置/通讯/设备故障导致无法继续</summary>
        Aborted = 4,
    }

    /// <summary>任务触发来源</summary>
    public enum ModuleCheckTrigger
    {
        /// <summary>在触发窗口 [T-提前分钟, T) 内按时触发</summary>
        OnTime = 0,
        /// <summary>错过窗口后开机补做</summary>
        MakeUp = 1,
        /// <summary>人工立即点检</summary>
        Manual = 2,
    }

    /// <summary>样品当前位置</summary>
    public enum ModuleCheckSampleLocation
    {
        CheckTray = 0,
        Nozzle = 1,
        Fixture = 2,
        Unknown = 3,
    }

    /// <summary>一轮判定结论</summary>
    public enum ModuleCheckVerdict
    {
        Pending = 0,
        /// <summary>全部为 0，点检通过</summary>
        Pass = 1,
        /// <summary>存在非 0 码（289 或其它 NG），换料后重测</summary>
        Retest = 2,
        /// <summary>存在非 0 码且已达到总轮数上限</summary>
        FailOverLimit = 3,
        /// <summary>有样品的工位缺结果，属于通讯/测试异常，不计入轮次</summary>
        Incomplete = 4,
    }

    /// <summary>一颗点检样品</summary>
    public class ModuleCheckSample
    {
        /// <summary>样品号，如 S01</summary>
        public string Id = "";
        /// <summary>点检盘穴位序号（Product.Tray.PosInf.idx），归还按此原穴</summary>
        public int TrayPocketIdx = -1;
        /// <summary>首轮装入的夹具穴位（1~16）</summary>
        public int HomeFixturePocket = 0;
        public ModuleCheckSampleLocation Location = ModuleCheckSampleLocation.CheckTray;
        /// <summary>当前所在夹具穴位（1~16），不在夹具时为 0</summary>
        public int FixturePocket = 0;
        /// <summary>在嘴上时：臂序号（0/1）和嘴序号（0~2），否则 -1</summary>
        public int Arm = -1;
        public int Nozzle = -1;
        public bool Returned = false;

        public string DescribeLocation()
        {
            switch (Location)
            {
                case ModuleCheckSampleLocation.CheckTray: return Returned ? string.Format("点检盘{0}(已归还)", TrayPocketIdx) : string.Format("点检盘{0}", TrayPocketIdx);
                case ModuleCheckSampleLocation.Nozzle: return string.Format("臂{0}嘴N{1}", Arm + 1, Nozzle + 1);
                case ModuleCheckSampleLocation.Fixture: return string.Format("夹具穴{0}", FixturePocket);
                default: return "未知";
            }
        }
    }

    /// <summary>一个工位一轮的测试结果</summary>
    public class ModuleCheckPocketResult
    {
        /// <summary>夹具穴位 1~16</summary>
        public int Pocket;
        public string SampleId = "";
        public int PcId;
        /// <summary>测试端逐工位结果码；0=OK，-1=未收到结果</summary>
        public int Code = -1;
    }

    /// <summary>一轮测试记录</summary>
    public class ModuleCheckRound
    {
        public int RoundNo;
        public DateTime? StartTime;
        public DateTime? EndTime;
        /// <summary>本轮开始时 穴位→样品 的映射（换位后重建）</summary>
        public Dictionary<int, string> Mapping = new Dictionary<int, string>();
        public List<ModuleCheckPocketResult> Results = new List<ModuleCheckPocketResult>();
        public ModuleCheckVerdict Verdict = ModuleCheckVerdict.Pending;
        /// <summary>例如 "穴4:289,穴7:512"</summary>
        public string NgSummary = "";
    }

    /// <summary>目标工站一台测试电脑的模式状态</summary>
    public class ModuleCheckPcState
    {
        /// <summary>dllforcomv8 的 PC_ID（从站号）</summary>
        public int PcId;
        /// <summary>PUCF 接口使用的 IP</summary>
        public string Ip = "";
        /// <summary>已发送切点检请求（恢复生产时据此逐台恢复）</summary>
        public bool CheckRequested;
        public bool CheckConfirmed;
        public bool ProductionRestored;
        public string LastError = "";
    }

    /// <summary>一次模组自动点检任务（持久化到 syscfg\ModuleCheck\active_task.json）</summary>
    public class ModuleCheckTask
    {
        public string TaskId = "";
        public string PlanId = "";
        public ModuleCheckTrigger Trigger = ModuleCheckTrigger.OnTime;
        public DateTime CreatedTime = DateTime.Now;
        public DateTime? PlanTime;
        public string CreatedBy = "";
        public ModuleCheckStage Stage = ModuleCheckStage.WaitTargetBatch;
        public ModuleCheckOutcome Outcome = ModuleCheckOutcome.None;
        /// <summary>任务创建时的参数快照，任务内只读这份，员工改设置只影响后续任务</summary>
        public ModuleCheckSettings Snapshot = new ModuleCheckSettings();
        public List<ModuleCheckSample> Samples = new List<ModuleCheckSample>();
        public List<ModuleCheckRound> Rounds = new List<ModuleCheckRound>();
        public List<ModuleCheckPcState> Pcs = new List<ModuleCheckPcState>();
        /// <summary>总轮数上限 = 1 + 重试次数（创建时计算）</summary>
        public int MaxRounds = 1;
        public bool CancelRequested;
        public string CancelBy = "";
        /// <summary>程序重启后发现的未完成任务，需人工核对后才能继续</summary>
        public bool NeedsReconcile;
        /// <summary>最后一个已确认完成的动作（续做/核对用）</summary>
        public string LastConfirmedAction = "";
        /// <summary>已开始但结果未确认的动作</summary>
        public string PendingAction = "";
        public DateTime? FinishedTime;
        public string FailReason = "";
        /// <summary>进入归还阶段时确定的任务结论，样品全部归还且生产参数确认后生效</summary>
        public ModuleCheckOutcome PendingOutcome = ModuleCheckOutcome.None;
        /// <summary>本次换料后应有的 夹具穴位→样品</summary>
        public Dictionary<int, string> ExpectedAfterSwap = new Dictionary<int, string>();
        /// <summary>恢复生产参数已尝试次数</summary>
        public int RestoreAttempts;
        /// <summary>阶段耗时记录：阶段名→毫秒</summary>
        public List<string> StageTimings = new List<string>();
        public DateTime StageEnterTime = DateTime.Now;

        [JsonIgnore]
        public int CurrentRoundNo
        {
            get { return Rounds.Count == 0 ? 0 : Rounds[Rounds.Count - 1].RoundNo; }
        }

        [JsonIgnore]
        public ModuleCheckRound CurrentRound
        {
            get { return Rounds.Count == 0 ? null : Rounds[Rounds.Count - 1]; }
        }

        [JsonIgnore]
        public bool IsActive
        {
            get { return Stage != ModuleCheckStage.Finished; }
        }

        public ModuleCheckSample FindSample(string id)
        {
            return Samples.FirstOrDefault(s => s.Id == id);
        }

        public ModuleCheckSample FindSampleAtPocket(int pocket)
        {
            return Samples.FirstOrDefault(s => s.Location == ModuleCheckSampleLocation.Fixture && s.FixturePocket == pocket);
        }

        /// <summary>当前 夹具穴位→样品号</summary>
        public Dictionary<int, string> CurrentFixtureMap()
        {
            var map = new Dictionary<int, string>();
            foreach (var s in Samples)
                if (s.Location == ModuleCheckSampleLocation.Fixture && s.FixturePocket > 0)
                    map[s.FixturePocket] = s.Id;
            return map;
        }

        public string Summary()
        {
            int inFixture = Samples.Count(s => s.Location == ModuleCheckSampleLocation.Fixture);
            int returned = Samples.Count(s => s.Returned);
            return string.Format("任务{0} 工站{1} 阶段={2} 结果={3} 轮次={4}/{5} 装入{6}/{7} 归还{8}/{7}",
                TaskId, Snapshot.TargetStation + 1, ModuleCheckText.Stage(Stage), ModuleCheckText.Outcome(Outcome),
                CurrentRoundNo, MaxRounds, inFixture, Samples.Count, returned);
        }
    }

    /// <summary>中文显示文本</summary>
    public static class ModuleCheckText
    {
        public static string Stage(ModuleCheckStage s)
        {
            switch (s)
            {
                case ModuleCheckStage.WaitTargetBatch: return "等待目标批次";
                case ModuleCheckStage.SwitchingToCheck: return "切换点检参数";
                case ModuleCheckStage.WaitLoad: return "等待上料";
                case ModuleCheckStage.Loading: return "装料中";
                case ModuleCheckStage.Testing: return "测试中";
                case ModuleCheckStage.Swapping: return "换料中";
                case ModuleCheckStage.Returning: return "归还中";
                case ModuleCheckStage.RestoringProduction: return "恢复生产参数";
                case ModuleCheckStage.Finished: return "已结束";
                default: return s.ToString();
            }
        }

        public static string Outcome(ModuleCheckOutcome o)
        {
            switch (o)
            {
                case ModuleCheckOutcome.None: return "-";
                case ModuleCheckOutcome.Pass: return "通过";
                case ModuleCheckOutcome.FailOverLimit: return "超过重试次数失败";
                case ModuleCheckOutcome.Cancelled: return "已取消";
                case ModuleCheckOutcome.Aborted: return "异常中止";
                default: return o.ToString();
            }
        }

        public static string Trigger(ModuleCheckTrigger t)
        {
            switch (t)
            {
                case ModuleCheckTrigger.OnTime: return "准时触发";
                case ModuleCheckTrigger.MakeUp: return "错过窗口补做";
                case ModuleCheckTrigger.Manual: return "人工开启";
                default: return t.ToString();
            }
        }

        public static string Verdict(ModuleCheckVerdict v)
        {
            switch (v)
            {
                case ModuleCheckVerdict.Pending: return "未判定";
                case ModuleCheckVerdict.Pass: return "OK";
                case ModuleCheckVerdict.Retest: return "NG-换料重测";
                case ModuleCheckVerdict.FailOverLimit: return "NG-超过重试次数";
                case ModuleCheckVerdict.Incomplete: return "结果不完整";
                default: return v.ToString();
            }
        }
    }
}

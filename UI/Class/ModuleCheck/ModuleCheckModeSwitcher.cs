using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace UI.ModuleCheck
{
    /// <summary>PUCF 模式接口抽象（实现见 ModuleCheckMachineAdapter.PucfModeGateway）</summary>
    public interface IModuleCheckModeGateway
    {
        /// <summary>check=true 切点检，false 切生产；返回 0 成功，其它失败</summary>
        int SetMode(string ip, bool check, int checkModeTemp);
        /// <summary>返回 0 成功；isCheck/isProduct 为当前模式，temp 为点检参数</summary>
        int GetMode(string ip, out bool isCheck, out bool isProduct, out int temp);
    }

    /// <summary>目标工站的一台测试电脑（dllforcomv8 用 PC_ID，PUCF 用 IP）</summary>
    public class ModuleCheckPcInfo
    {
        public int PcId;
        public string Ip = "";
    }

    public static class ModuleCheckPcMap
    {
        /// <summary>
        /// 校验 PC_ID 与 IP 一一对应：同一 PC_ID 只能对应一个 IP，同一 IP 只能对应一个 PC_ID，IP 不能为空。
        /// pairs 为工站每个穴位的 (PC_ID, PC_IP)。
        /// </summary>
        public static List<ModuleCheckPcInfo> Build(IEnumerable<KeyValuePair<int, string>> pairs, out List<string> errors)
        {
            errors = new List<string>();
            var byId = new Dictionary<int, HashSet<string>>();
            foreach (var p in pairs)
            {
                HashSet<string> ips;
                if (!byId.TryGetValue(p.Key, out ips)) { ips = new HashSet<string>(); byId[p.Key] = ips; }
                ips.Add((p.Value ?? "").Trim());
            }
            var list = new List<ModuleCheckPcInfo>();
            foreach (var kv in byId.OrderBy(k => k.Key))
            {
                if (kv.Value.Count != 1) errors.Add(string.Format("PC{0} 对应多个 IP：{1}", kv.Key, string.Join("/", kv.Value)));
                string ip = kv.Value.FirstOrDefault() ?? "";
                if (string.IsNullOrEmpty(ip)) errors.Add(string.Format("PC{0} 没有配置 IP", kv.Key));
                list.Add(new ModuleCheckPcInfo { PcId = kv.Key, Ip = ip });
            }
            foreach (var g in list.GroupBy(x => x.Ip).Where(g => g.Count() > 1 && !string.IsNullOrEmpty(g.Key)))
                errors.Add(string.Format("IP {0} 同时对应 {1}", g.Key, string.Join("、", g.Select(x => "PC" + x.PcId))));
            return list;
        }
    }

    /// <summary>
    /// 切点检 / 恢复生产。只发给目标工站的电脑；每台都回读确认。
    /// SetFactoryMode 会让测试软件重启（约 5 s），DLL 可能阻塞到登录 Ack，所以调用方应在后台线程执行。
    /// 切点检时把实际发过请求的 PC 记在任务里，恢复生产按这份记录逐台发，不依赖当时工站有没有启用物料。
    /// </summary>
    public class ModuleCheckModeSwitcher
    {
        private readonly IModuleCheckModeGateway gateway;
        private readonly Func<bool> isQuit;
        public int PollIntervalMs = 1000;

        public ModuleCheckModeSwitcher(IModuleCheckModeGateway gateway, Func<bool> isQuit)
        {
            this.gateway = gateway;
            this.isQuit = isQuit ?? (() => false);
        }

        public bool SwitchToCheck(ModuleCheckTask t, Action persist, out string error)
        {
            error = "";
            int temp = t.Snapshot.CheckModeTemp;
            int timeoutMs = Math.Max(5, t.Snapshot.ModeSwitchTimeoutSec) * 1000;
            foreach (var pc in t.Pcs)
            {
                if (pc.CheckConfirmed) continue;
                var sw = Stopwatch.StartNew();
                int ret;
                try { ret = gateway.SetMode(pc.Ip, true, temp); }
                catch (Exception ex) { ret = -9999; pc.LastError = ex.Message; }
                pc.CheckRequested = true;   // 无论返回值如何都记下：可能已经开始重启，恢复时也要发
                if (persist != null) persist();
                ModuleCheckLog.Info(ModuleCheckLogCat.Mode, t, string.Format("PC{0}({1}) SetFactoryMode(点检,{2}) ret={3} 耗时{4}ms", pc.PcId, pc.Ip, temp, ret, sw.ElapsedMilliseconds), true);
                if (ret != 0)
                {
                    pc.LastError = string.Format("SetFactoryMode(点检) 返回 {0}", ret);
                    error = string.Format("PC{0} 切点检失败：{1}", pc.PcId, pc.LastError);
                    ModuleCheckLog.Risk(ModuleCheckRisk.ModeSwitchProblem, t, error);
                    return false;
                }
                string detail;
                if (!WaitMode(t, pc, true, temp, timeoutMs, sw, out detail))
                {
                    pc.LastError = detail;
                    error = string.Format("PC{0} 切点检后回读未确认：{1}", pc.PcId, detail);
                    ModuleCheckLog.Risk(ModuleCheckRisk.ModeSwitchProblem, t, error);
                    return false;
                }
                pc.CheckConfirmed = true;
                pc.LastError = "";
                if (persist != null) persist();
                ModuleCheckLog.Info(ModuleCheckLogCat.Mode, t, string.Format("PC{0}({1}) 点检参数已确认，共耗时{2}ms", pc.PcId, pc.Ip, sw.ElapsedMilliseconds), true);
            }
            return true;
        }

        public bool RestoreProduction(ModuleCheckTask t, Action persist, out string error)
        {
            error = "";
            int timeoutMs = Math.Max(5, t.Snapshot.ModeSwitchTimeoutSec) * 1000;
            var errs = new List<string>();
            foreach (var pc in t.Pcs)
            {
                if (!pc.CheckRequested || pc.ProductionRestored) continue;
                var sw = Stopwatch.StartNew();
                int ret;
                try { ret = gateway.SetMode(pc.Ip, false, 0); }
                catch (Exception ex) { ret = -9999; pc.LastError = ex.Message; }
                ModuleCheckLog.Info(ModuleCheckLogCat.Mode, t, string.Format("PC{0}({1}) SetFactoryMode(生产) ret={2} 耗时{3}ms", pc.PcId, pc.Ip, ret, sw.ElapsedMilliseconds), true);
                if (ret != 0)
                {
                    pc.LastError = string.Format("SetFactoryMode(生产) 返回 {0}", ret);
                    errs.Add(string.Format("PC{0}：{1}", pc.PcId, pc.LastError));
                    continue;   // 继续恢复其它电脑
                }
                string detail;
                if (!WaitMode(t, pc, false, 0, timeoutMs, sw, out detail))
                {
                    pc.LastError = detail;
                    errs.Add(string.Format("PC{0} 回读未确认：{1}", pc.PcId, detail));
                    continue;
                }
                pc.ProductionRestored = true;
                pc.LastError = "";
                if (persist != null) persist();
                ModuleCheckLog.Info(ModuleCheckLogCat.Mode, t, string.Format("PC{0}({1}) 生产参数已确认，共耗时{2}ms", pc.PcId, pc.Ip, sw.ElapsedMilliseconds), true);
            }
            var missed = t.Pcs.Where(p => p.CheckRequested && !p.ProductionRestored).Select(p => "PC" + p.PcId).ToList();
            if (missed.Count > 0)
            {
                error = "恢复生产未完成：" + string.Join("；", errs);
                ModuleCheckLog.Risk(ModuleCheckRisk.PcMappingMismatch, t, error + "，未恢复：" + string.Join("、", missed));
                return false;
            }
            return true;
        }

        private bool WaitMode(ModuleCheckTask t, ModuleCheckPcState pc, bool wantCheck, int temp, int timeoutMs, Stopwatch sw, out string detail)
        {
            detail = "";
            string last = "";
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (isQuit()) { detail = "外部取消"; return false; }
                bool isCheck, isProduct;
                int curTemp;
                int ret;
                try { ret = gateway.GetMode(pc.Ip, out isCheck, out isProduct, out curTemp); }
                catch (Exception ex) { ret = -9999; isCheck = false; isProduct = false; curTemp = 0; last = ex.Message; }
                if (ret == 0)
                {
                    last = string.Format("回读 点检={0} 生产={1} 参数={2}", isCheck, isProduct, curTemp);
                    if (wantCheck && isCheck && (temp == 0 || curTemp == temp)) return true;
                    if (!wantCheck && isProduct) return true;
                    if (wantCheck && isCheck && temp != 0 && curTemp != temp)
                        ModuleCheckLog.Warn(ModuleCheckLogCat.Mode, t, string.Format("PC{0} 已是点检模式但参数 {1} 与期望 {2} 不一致，继续等待", pc.PcId, curTemp, temp), false);
                }
                else last = string.Format("GetFactoryMode 返回 {0}（重启中可能连不上）", ret);
                Thread.Sleep(PollIntervalMs);
            }
            detail = string.Format("等待 {0}s 超时，最后一次：{1}", timeoutMs / 1000, last);
            return false;
        }
    }

    /// <summary>dllforcomv8 握手标志</summary>
    public static class ModuleCheckHandshake
    {
        public const int MarkReady = 1;
        public const int MarkAck = 2;
        public const int MarkNext = 3;
        public const int MarkEnd = 4;
        public const int MarkReset = 5;
        public const int MarkResetTest = 6;
        /// <summary>新版 dllforcomv8 在结果标志不是 END 时 StartTestFlow 返回 -6</summary>
        public const int RetBusy = -6;

        public static string Describe(int mark)
        {
            switch (mark)
            {
                case MarkReady: return "READY";
                case MarkAck: return "ACK";
                case MarkNext: return "NEXT";
                case MarkEnd: return "END";
                case MarkReset: return "RESET";
                case MarkResetTest: return "RESETTEST";
                default: return mark.ToString();
            }
        }

        /// <summary>
        /// 启动前检查：结果标志必须是 END（上一轮最终 NextTest(0) 握手已完成）。
        /// 旧版 DLL 此时调用 StartTestFlow 会直接返回 OK 却不启动，新版返回 -6，两种都在这里拦住。
        /// </summary>
        public static bool IsReadyForStart(int getInfoRet, int resultMark, out string reason)
        {
            if (getInfoRet != 0) { reason = string.Format("GetInfo 返回 {0}", getInfoRet); return false; }
            if (resultMark != MarkEnd) { reason = string.Format("结果标志={0}，上一轮握手未结束", Describe(resultMark)); return false; }
            reason = "";
            return true;
        }
    }
}

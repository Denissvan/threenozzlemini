using System;
using System.Collections.Generic;
using System.Linq;
using MotionCtrl;
using UI.Class;

namespace UI.ModuleCheck
{
    /// <summary>
    /// 模组自动点检与本机程序（WS/COM/VAR/AutoChkDLL/TestPC）之间唯一的连接层。
    /// 移植到 12 工位 MINI Q200 / Pre-Mini 时，只需按对方代码重写这个文件。
    /// </summary>
    public class ModuleCheckMachineAdapter : IModuleCheckMachine
    {
        private readonly PucfModeGateway gateway = new PucfModeGateway();

        /// <summary>程序启动时调用一次（FrMain 加载时）</summary>
        public static void StartModule()
        {
            try
            {
                ModuleCheckLog.Mirror = (text, level) =>
                {
                    Msg.EM_MSGTYPE t = level == ModuleCheckLogLevel.Error ? Msg.EM_MSGTYPE.ERR
                        : (level == ModuleCheckLogLevel.Warn ? Msg.EM_MSGTYPE.WAR : Msg.EM_MSGTYPE.DBG);
                    VAR.msg.AddMsg(t, text);
                };
                ModuleCheckManager.Instance.Start(new ModuleCheckMachineAdapter());
            }
            catch (Exception ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, "[点检] 模组自动点检模块启动失败：" + ex.Message);
            }
        }

        /// <summary>
        /// enabledOnly=true：只取启用(_benable)穴位所属的 PC。点检任务用这个——
        /// 例如工站3只接了 PC3 时，把穴9-16 取消启用，PC7 就不会被切点检/切回生产。
        /// </summary>
        public List<KeyValuePair<int, string>> GetStationPcPairs(int station, bool enabledOnly = false)
        {
            var list = new List<KeyValuePair<int, string>>();
            if (COM.list_ws == null || station < 0 || station >= COM.list_ws.Count) return list;
            WS ws = COM.list_ws[station];
            if (ws == null || ws.list_md == null) return list;
            var ips = ModuleCheckManager.Instance.Settings.PcIps;
            var connected = new Dictionary<int, string>();
            foreach (WS.MdDat md in ws.list_md)
            {
                if (enabledOnly && !md._benable) continue;
                string ip;
                // IP 优先级：点检模块手动保存 > dllforcomv8 当前连接的对方IP > 工站配置 WS*Cfg.ini 的 PC_IP
                if (ips == null || !ips.TryGetValue(md.PC_ID, out ip) || string.IsNullOrWhiteSpace(ip))
                {
                    if (!connected.TryGetValue(md.PC_ID, out ip))
                    {
                        int ret;
                        ip = ReadConnectedIp(md.PC_ID, out ret);
                        connected[md.PC_ID] = ip;
                    }
                    if (string.IsNullOrWhiteSpace(ip)) ip = md.PC_IP;
                }
                list.Add(new KeyValuePair<int, string>(md.PC_ID, ip));
            }
            return list;
        }

        /// <summary>工站启用(_benable)的夹具穴位号；读不到返回 null（不限制）</summary>
        public static List<int> EnabledFixturePockets(int station)
        {
            if (COM.list_ws == null || station < 0 || station >= COM.list_ws.Count) return null;
            WS ws = COM.list_ws[station];
            if (ws == null || ws.list_md == null) return null;
            return ws.list_md.Where(m => m._benable).Select(m => m.Num).OrderBy(x => x).ToList();
        }

        /// <summary>借用待测盘时，点检盘就是当前待测盘（同一份料盘配置）</summary>
        public static Product.Tray CheckTray(bool borrowFeedTray)
        {
            if (!borrowFeedTray || COM.traybox_fd == null) return null;
            if (COM.traybox_fd.tray_cur != null) return COM.traybox_fd.tray_cur;
            return COM.traybox_fd.list_tray == null ? null : COM.traybox_fd.list_tray.FirstOrDefault();
        }

        /// <summary>借用待测盘时：待测盘配置里取消了“使用”的穴位编号（即点检区）</summary>
        public static List<int> DisabledFeedTrayPockets()
        {
            var tray = CheckTray(true);
            if (tray == null || tray.list_pos == null) return new List<int>();
            return tray.list_pos.Where(p => p.isDisable).Select(p => p.idx).OrderBy(x => x).ToList();
        }

        public int GetCheckTrayPocketCount(bool borrowFeedTray)
        {
            var tray = CheckTray(borrowFeedTray);
            return tray == null || tray.list_pos == null ? 0 : tray.list_pos.Count;
        }

        public List<string> ValidateForTask(ModuleCheckSettings s)
        {
            var errs = new List<string>();
            if (PT_SET.AutoChkEn)
                errs.Add("旧的“自动点检模式”（系统设置-自动点检）处于开启状态，请先关闭，两者不能同时使用");
            if (!s.BorrowFeedTray)
                errs.Add("独立点检盘尚未接入，当前只能借用待测盘（请勾选“借用待测盘”）");
            if (COM.list_ws == null || s.TargetStation < 0 || s.TargetStation >= COM.list_ws.Count || COM.list_ws[s.TargetStation] == null)
            {
                errs.Add(string.Format("工站{0}不存在", s.TargetStation + 1));
                return errs;
            }
            WS ws = COM.list_ws[s.TargetStation];
            foreach (var slot in s.Samples)
            {
                WS.MdDat md = ws.list_md.FirstOrDefault(m => m.Num == slot.FixturePocket);
                if (md == null) errs.Add(string.Format("工站{0}没有穴{1}", s.TargetStation + 1, slot.FixturePocket));
                else if (!md._benable) errs.Add(string.Format("工站{0}穴{1}未启用，不能放点检样品", s.TargetStation + 1, slot.FixturePocket));
            }
            if (s.BorrowFeedTray)
            {
                var tray = CheckTray(true);
                if (tray == null || tray.list_pos == null) errs.Add("读取不到待测盘配置");
                else
                {
                    foreach (var slot in s.Samples)
                    {
                        var pos = tray.list_pos.FirstOrDefault(p => p.idx == slot.TrayPocketIdx);
                        if (pos == null) errs.Add(string.Format("待测盘没有穴位 {0}", slot.TrayPocketIdx));
                        else if (!pos.isDisable)
                            errs.Add(string.Format("借用待测盘：穴位 {0} 放点检样品，必须在产品配置的待测盘穴位里取消勾选“使用”，否则会被当作普通料取走", slot.TrayPocketIdx));
                    }
                }
            }
            return errs;
        }

        public void RestoreFixtureIdentities(ModuleCheckTask t)
        {
            if (t == null || COM.list_ws == null || t.Snapshot.TargetStation < 0 || t.Snapshot.TargetStation >= COM.list_ws.Count) return;
            WS ws = COM.list_ws[t.Snapshot.TargetStation];
            if (ws == null) return;
            foreach (WS.MdDat md in ws.list_md) md.ModuleCheckSampleId = null;
            foreach (var s in t.Samples.Where(x => x.Location == ModuleCheckSampleLocation.Fixture && x.FixturePocket > 0))
            {
                WS.MdDat md = ws.list_md.FirstOrDefault(m => m.Num == s.FixturePocket);
                if (md != null) md.ModuleCheckSampleId = s.Id;
            }
            ModuleCheckLog.Info(ModuleCheckLogCat.Material, t, "已按任务记录恢复夹具穴位上的样品身份：" + ModuleCheckSwapPlan.FormatMap(t.CurrentFixtureMap()), true);
        }

        public IModuleCheckModeGateway ModeGateway
        {
            get { return gateway; }
        }

        /// <summary>程序正在退出（需要时由主窗体关闭时置位）</summary>
        public static volatile bool AppClosing;

        /// <summary>
        /// 模式切换（切点检/恢复生产）的中断条件。不能用 VAR.gsys_set.bquit：每次停机都会置位，
        /// 归还阶段停机时恢复生产的回读会被当成“外部取消”，3 次重试瞬间用完。
        /// 切换本身有超时（ModeSwitchTimeoutSec），停机期间让它在后台跑完。
        /// </summary>
        public bool IsQuit
        {
            get { return AppClosing; }
        }

        public string CurrentUser
        {
            get { return VAR.CurUserName ?? ""; }
        }

        /// <summary>
        /// 从 dllforcomv8 读取某台测试电脑当前连接的对方 IP（电控是 Modbus 服务端，测试电脑连上来后 DLL 记录其地址）。
        /// 未连接或读取失败返回空字符串。
        /// </summary>
        public static string ReadConnectedIp(int pcId, out int ret)
        {
            ret = -1;
            if (WS.Demo) return "";
            try
            {
                TestPC.InfoData info = new TestPC.InfoData();
                ret = TestPC.GetInfo(pcId, ref info);
                if (ret != 0 || info.ip == null) return "";
                string ip = new string(info.ip);
                int end = ip.IndexOf('\0');
                if (end >= 0) ip = ip.Substring(0, end);
                ip = ip.Trim();
                System.Net.IPAddress parsed;
                return System.Net.IPAddress.TryParse(ip, out parsed) && ip.Count(ch => ch == '.') == 3 ? ip : "";
            }
            catch (Exception)
            {
                ret = -9999;
                return "";
            }
        }

        /// <summary>
        /// 读取某台 PC 的 dllforcomv8 握手状态，供 StartTestFlow 前检查（结果标志必须为 END）。
        /// </summary>
        public static bool ReadHandshake(int pcId, out int ret, out int startMark, out int resultMark)
        {
            ret = -1;
            startMark = -1;
            resultMark = -1;
            if (WS.Demo)
            {
                ret = 0;
                startMark = ModuleCheckHandshake.MarkAck;
                resultMark = ModuleCheckHandshake.MarkEnd;
                return true;
            }
            try
            {
                TestPC.InfoData info = new TestPC.InfoData();
                ret = TestPC.GetInfo(pcId, ref info);
                if (ret != 0) return false;
                startMark = info.start_mark;
                resultMark = info.result_mark;
                return true;
            }
            catch (Exception)
            {
                ret = -9999;
                return false;
            }
        }
    }

    /// <summary>PUCFTestServiceCall.dll 模式接口</summary>
    public class PucfModeGateway : IModuleCheckModeGateway
    {
        // 空跑(Demo)时不调用 DLL，按最后一次设置的模式回读
        private readonly Dictionary<string, KeyValuePair<bool, int>> demoModes = new Dictionary<string, KeyValuePair<bool, int>>();

        public int SetMode(string ip, bool check, int checkModeTemp)
        {
            if (WS.Demo)
            {
                lock (demoModes) demoModes[ip ?? ""] = new KeyValuePair<bool, int>(check, check ? checkModeTemp : 0);
                return 0;
            }
            PUCFFactoryMode mode = check ? PUCFFactoryMode.PUCFFactoryMode_Check : PUCFFactoryMode.PUCFFactoryMode_Product;
            return AutoChkDLL.iPUCFTestServiceSetFactoryMode(ip, mode, check ? checkModeTemp : 0);
        }

        public int GetMode(string ip, out bool isCheck, out bool isProduct, out int temp)
        {
            if (WS.Demo)
            {
                KeyValuePair<bool, int> m;
                lock (demoModes)
                {
                    if (!demoModes.TryGetValue(ip ?? "", out m)) m = new KeyValuePair<bool, int>(false, 0);
                }
                isCheck = m.Key;
                isProduct = !m.Key;
                temp = m.Value;
                return 0;
            }
            PUCFFactoryMode mode;
            int ret = AutoChkDLL.iPUCFTestServiceGetFactoryMode(ip, out mode, out temp);
            isCheck = mode == PUCFFactoryMode.PUCFFactoryMode_Check;
            isProduct = mode == PUCFFactoryMode.PUCFFactoryMode_Product;
            return ret;
        }
    }
}

using System;
using System.IO;
using System.Text;
using MotionCtrl;

namespace UI
{
    /// <summary>
    /// 上下料计时：每个工站每次到上下料面记一条。
    ///   主日志：一行 [上下料计时] 汇总；
    ///   ..\log\UpDownTiming\yyyy-MM-dd_上下料计时.csv：每次一行，Excel 可直接打开对比。
    /// 计时点：
    ///   到位   = 主流程“工站X等待上下料...”（工站已到上下料面、已启动首组预取）
    ///   开始   = 上下料线程“工站X开始上下料...”
    ///   完成   = “完成工站X上下料动作...”（两臂换料结束）
    ///   就绪   = “上下料完成用时”（合夹具、翻转、启动测试之后，通知 REDAYFORTEST）
    /// </summary>
    public static class UpDownTimingLog
    {
        private static readonly object fileLock = new object();
        private static int seq;

        public static string Dir
        {
            get { return Path.Combine(Path.GetFullPath(".."), "log", "UpDownTiming"); }
        }

        /// <summary>主流程：工站到上下料面、开始等待上下料</summary>
        public static void MarkArrive(WS ws)
        {
            if (ws == null) return;
            ws.UdTimingArrive = DateTime.Now;
            CtStepLog.NewCycle(ws.disc);   // CT 分解日志：工站到位开始新周期
            ws.UdTimingStart = DateTime.MinValue;
            ws.UdTimingDone = DateTime.MinValue;
        }

        /// <summary>上下料线程：开始换料</summary>
        public static void MarkStart(WS ws)
        {
            if (ws == null) return;
            ws.UdTimingStart = DateTime.Now;
            ws.UdTimingDone = DateTime.MinValue;
        }

        /// <summary>两臂换料结束</summary>
        public static void MarkDone(WS ws)
        {
            if (ws == null) return;
            ws.UdTimingDone = DateTime.Now;
        }

        private static readonly object compareLock = new object();

        /// <summary>
        /// 放料前上相机：进夹具飞拍（有料任务）与取后定拍（WsUp_Shp）的对比，每穴一行：
        /// ..\log\UpDownTiming\yyyy-MM-dd_放料前飞拍对比.csv
        /// </summary>
        public static void WritePrePlaceCompare(string arm, string station, int pocket, string task, string note,
            ST_XYA fly, ST_XYA fixedShot, bool demo)
        {
            try
            {
                lock (compareLock)
                {
                    Directory.CreateDirectory(Dir);
                    string path = Path.Combine(Dir, DateTime.Now.ToString("yyyy-MM-dd") + "_放料前飞拍对比.csv");
                    bool isNew = !File.Exists(path);
                    var sb = new StringBuilder();
                    if (isNew)
                        sb.Append("时间,臂,工站,穴,飞拍任务,说明,飞拍X,飞拍Y,飞拍A,定拍X,定拍Y,定拍A,差X,差Y,差A,空跑\r\n");
                    sb.AppendFormat("{0:yyyy-MM-dd HH:mm:ss.fff},{1},{2},{3},{4},{5},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4},{12:F4},{13:F4},{14:F4},{15}\r\n",
                        DateTime.Now, arm, station, pocket, task, note, fly.x, fly.y, fly.a, fixedShot.x, fixedShot.y, fixedShot.a,
                        fly.x - fixedShot.x, fly.y - fixedShot.y, fly.a - fixedShot.a, demo ? "是" : "否");
                    using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    {
                        if (isNew)
                        {
                            byte[] bom = Encoding.UTF8.GetPreamble();
                            fs.Write(bom, 0, bom.Length);
                        }
                        byte[] data = new UTF8Encoding(false).GetBytes(sb.ToString());
                        fs.Write(data, 0, data.Length);
                    }
                }
            }
            catch
            {
                // 记录失败不影响流程
            }
        }

        /// <summary>写一条记录。result：OK / 未完成原因</summary>
        public static void Write(WS ws, string result)
        {
            try
            {
                if (ws == null || ws.UdTimingStart == DateTime.MinValue) return;
                DateTime now = DateTime.Now;
                DateTime arrive = ws.UdTimingArrive != DateTime.MinValue && ws.UdTimingArrive <= ws.UdTimingStart ? ws.UdTimingArrive : ws.UdTimingStart;
                DateTime done = ws.UdTimingDone != DateTime.MinValue ? ws.UdTimingDone : now;
                bool finished = result == "OK";
                double waitStart = (ws.UdTimingStart - arrive).TotalSeconds;
                double exchange = (done - ws.UdTimingStart).TotalSeconds;
                double toTest = finished ? (now - done).TotalSeconds : 0;
                double total = (now - arrive).TotalSeconds;
                double arm1 = COM.UDLoad1 == null ? 0 : COM.UDLoad1.LastUdTaskMs / 1000.0;
                double arm2 = COM.UDLoad2 == null ? 0 : COM.UDLoad2.LastUdTaskMs / 1000.0;
                int n = System.Threading.Interlocked.Increment(ref seq);
                string mode = WS.Demo ? (COM.UDLoad1 != null && COM.UDLoad1.Demo ? "无料空跑" : "有料空跑") : "正常生产";
                string demoRes = WS.Demo ? (WS.DemoResultMode == 1 ? "全OK" : WS.DemoResultMode == 2 ? "全NG" : "一排OK一排NG") : "-";
                string blind = UpDownLoad.ThreeNozzleBlindTrayPickEnabled ? "开" : "关";
                string split = UpDownLoad.ThreeNozzleSplitPlaceTrayLock ? "开" : "关";
                string prePlaceFly = UpDownLoad.ThreeNozzlePrePlaceFlyText;

                VAR.msg.AddMsg(Msg.EM_MSGTYPE.NOR, string.Format(
                    "[上下料计时] {0} 第{1}次：到位→开始 {2:F1}s，上下料(夹具占用) {3:F1}s（臂1 {4:F1}s / 臂2 {5:F1}s），完成→启动测试 {6:F1}s，合计 {7:F1}s，结果={8}；{9}{10}，盲取={11}，分料盘锁={12}，放料前飞拍={13}",
                    ws.disc, n, waitStart, exchange, arm1, arm2, toTest, total, result,
                    mode, WS.Demo ? "/" + demoRes : "", blind, split, prePlaceFly));

                lock (fileLock)
                {
                    Directory.CreateDirectory(Dir);
                    // 列有增加时换新文件名，避免和当天旧表头混在一起
                    string path = Path.Combine(Dir, DateTime.Now.ToString("yyyy-MM-dd") + "_上下料计时v2.csv");
                    bool isNew = !File.Exists(path);
                    var sb = new StringBuilder();
                    if (isNew)
                        sb.Append("时间,工站,序号,到位→开始(s),上下料夹具占用(s),臂1(s),臂2(s),完成→启动测试(s),合计(s),结果,运行模式,空跑结果,盲取,分料盘锁,放料前飞拍\r\n");
                    sb.AppendFormat("{0:yyyy-MM-dd HH:mm:ss.fff},{1},{2},{3:F2},{4:F2},{5:F2},{6:F2},{7:F2},{8:F2},{9},{10},{11},{12},{13},{14}\r\n",
                        now, ws.disc, n, waitStart, exchange, arm1, arm2, toTest, total, (result ?? "").Replace(",", "；"),
                        mode, demoRes, blind, split, prePlaceFly);
                    using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    {
                        if (isNew)
                        {
                            byte[] bom = Encoding.UTF8.GetPreamble();
                            fs.Write(bom, 0, bom.Length);
                        }
                        byte[] data = new UTF8Encoding(false).GetBytes(sb.ToString());
                        fs.Write(data, 0, data.Length);
                    }
                }
            }
            catch
            {
                // 计时记录失败不影响流程
            }
            finally
            {
                if (ws != null) ws.UdTimingStart = DateTime.MinValue;   // 一次上下料只记一条
            }
        }
    }
}

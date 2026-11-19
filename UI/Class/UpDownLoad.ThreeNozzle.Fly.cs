using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MotionCtrl;
using UI.Class;

namespace UI
{
    /// <summary>
    /// 三吸嘴飞拍公共部分（第 1 步：只提供底层，不接入流程）。
    /// 原双嘴飞拍（FlyCfg / FlyToWs / FlyPlaceWsMod / FlyCamToTray / FlyTrayPickMod / FlyCamToLastPos）不改动、不调用。
    /// 这里只复用相机按序配置任务 Cam.ListTaskCfg、Y 轴位置比较触发 SetHcmp / DisableHcmp。
    /// </summary>
    public partial class UpDownLoad
    {
        /// <summary>一趟飞拍里的一张图。</summary>
        internal sealed class ThreeNozzleFlyShot
        {
            /// <summary>拍照相机（dwcam 或 upcam）。</summary>
            public Cam Camera;
            /// <summary>视觉任务名，例如 ModDw2_Shp、WsMod_Shp、ModUp_Shp。</summary>
            public string Task;
            /// <summary>定拍位置 X（补拍时用；下相机只走 Y，不用 X）。</summary>
            public double X;
            /// <summary>定拍位置 Y（标定的拍照位）。</summary>
            public double NominalY;
            /// <summary>飞拍触发 Y（已按方向扣除飞拍偏差，见 ThreeNozzleFlyTrigger）。</summary>
            public double TriggerY;
            /// <summary>写入 TriPos.n 的身份：吸嘴槽位 0..2，或穴号-1，或料盘穴号。用于核对图和对象是否对得上。</summary>
            public int Identity;
            /// <summary>日志里显示的名称。</summary>
            public string Label;
            /// <summary>结果拷贝；null = 飞拍没拿到可用的图（需要补拍）。bOK=false 表示拍到了但视觉判 NG。</summary>
            public Cam.VisionOutPutData Result;
            /// <summary>是否经过定拍补拍。</summary>
            public bool Recaptured;
        }

        /// <summary>三吸嘴飞拍不生效的原因；null = 生效。</summary>
        public static string ThreeNozzleFlyDisabledReason()
        {
            if (!ThreeNozzleHardwareConfig.FlyCaptureEnabled) return "系统页未勾选三吸嘴飞拍";
            // 与原流程 PlaceMdToWs 一致：这两项开着时原流程也走定拍
            if (PT_SET.bDwAddCapQrcode) return "开了下相机附加扫码（bDwAddCapQrcode），按定拍运行";
            if (PT_SET.OvenCheck) return "开了 OvenCheck，按定拍运行";
            return null;
        }

        public static bool ThreeNozzleFlyActive { get { return ThreeNozzleFlyDisabledReason() == null; } }

        /// <summary>调试页“放料前上相机进夹具飞拍”。</summary>
        public enum ThreeNozzlePrePlaceFlyMode
        {
            /// <summary>关闭：取完旧料后退回定拍 WsUp_Shp（原流程）</summary>
            Off = 0,
            /// <summary>对比：进夹具时飞拍，取完旧料后仍定拍，放料用定拍结果；两者差值写日志和 CSV</summary>
            Compare = 1,
            /// <summary>启用：进夹具时飞拍，取完旧料后直接放料，不再退回拍照（只用 X/Y，角度按 0）</summary>
            Use = 2,
        }

        /// <summary>软件启动默认启用；调试页可临时关闭或对比，不写配置，重启后恢复启用。</summary>
        public static ThreeNozzlePrePlaceFlyMode ThreeNozzlePrePlaceFly = ThreeNozzlePrePlaceFlyMode.Use;
        /// <summary>放料前飞拍用的上相机视觉任务，默认与放后定拍相同的 WsUp_Shp（有料/空穴都能认的新 VPP）。</summary>
        public static string ThreeNozzlePrePlaceFlyTask = CONST.WsUpFw;
        /// <summary>
        /// 放料前飞拍：同一趟两个穴示教 X 允许相差的最大值（mm）。一趟只能走一列，第二个穴在第一个穴的 X 上拍，
        /// 用结果时按实际拍照 X 计算（与原流程 FlyToWs 用 TriPos 作为拍照位相同），超过此值按取后定拍。
        /// </summary>
        /// 模块2 夹具上下两排拍照位示教 X 实测差 0.50~0.54mm，0.5 时几乎都被拒；放宽到 1.0（视野远大于此，结果按实际拍照 X 换算）。
        public static double ThreeNozzlePrePlaceMaxColumnDx = 1.0;

        public static string ThreeNozzlePrePlaceFlyText
        {
            get
            {
                switch (ThreeNozzlePrePlaceFly)
                {
                    case ThreeNozzlePrePlaceFlyMode.Compare: return "对比(" + ThreeNozzlePrePlaceFlyTask + ")";
                    case ThreeNozzlePrePlaceFlyMode.Use: return "启用(" + ThreeNozzlePrePlaceFlyTask + ")";
                    default: return "关";
                }
            }
        }

        /// <summary>
        /// 飞拍触发位置。飞拍标定（RotAndFly.Calibration）按 Y 增大方向测得 offset 并用“拍照位 - offset”验证残差；
        /// Y 减小方向按同一延迟反向补偿：“拍照位 + offset”。
        /// </summary>
        internal static double ThreeNozzleFlyTrigger(double nominalY, double offset, bool increasing)
        {
            return increasing ? nominalY - offset : nominalY + offset;
        }

        private static bool FlyFinite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }

        /// <summary>相机少收图时，多久没有新图/新结果就判定丢帧（ms）。0=关闭，恢复固定等满 ThreeNozzleFlyWaitMs。</summary>
        public static int ThreeNozzleFlyLostFrameGraceMs = 500;

        /// <summary>最长等多久飞拍结果（ms）。</summary>
        private const int ThreeNozzleFlyWaitMs = 3000;
        private static int threeNozzleFlyTraceSequence;

        /// <summary>
        /// 走一趟 Y 飞拍：X、Y 先到起点 (x, startY)，按触发顺序配置相机任务和位置比较输出，Y 匀速走到 endY，
        /// 等结果并逐张核对张数、任务名和触发身份。
        /// 返回 OK：每张 shot.Result 为结果拷贝，没对上的为 null（由调用方决定补拍，见 ThreeNozzleFlyRecapture）。
        /// 返回非 OK：运动/配置失败或触发关闭失败，所有结果作废。
        /// 无论成功失败，退出前一定关闭本趟用到的相机触发输出。
        /// </summary>
        internal EM_RES ThreeNozzleFlyPass(ref bool quit, string what, double x, double startY, double endY, List<ThreeNozzleFlyShot> shots)
        {
            if (shots == null || shots.Count == 0) return EM_RES.OK;
            if (quit) return EM_RES.QUIT;
            if (!FlyFinite(x) || !FlyFinite(startY) || !FlyFinite(endY) || Math.Abs(endY - startY) < 1)
                return ThreeNozzleError(string.Format("{0} 飞拍起终点无效 X={1} Y {2}→{3}", what, x, startY, endY));
            bool increasing = endY > startY;
            foreach (ThreeNozzleFlyShot shot in shots)
            {
                shot.Result = null;
                shot.Recaptured = false;
                if (shot.Camera == null || string.IsNullOrEmpty(shot.Task) || !FlyFinite(shot.NominalY) || !FlyFinite(shot.TriggerY))
                    return ThreeNozzleError(string.Format("{0} 飞拍参数无效：{1}", what, shot.Label));
                bool inside = increasing ? shot.TriggerY > startY && shot.TriggerY < endY : shot.TriggerY < startY && shot.TriggerY > endY;
                if (!inside)
                    return ThreeNozzleError(string.Format("{0} 飞拍触发点 {1:F3}（{2}）不在 Y {3:F3}→{4:F3} 之间", what, shot.TriggerY, shot.Label, startY, endY));
                if (!shot.Camera.List_vs_task.Any(t => t.TaskName == shot.Task))
                    return ThreeNozzleError(string.Format("{0} {1} 缺少视觉任务 {2}", what, shot.Camera.disc, shot.Task));
            }
            Cam[] cameras = shots.Select(s => s.Camera).Distinct().ToArray();
            Dictionary<Cam, List<ThreeNozzleFlyShot>> ordered = new Dictionary<Cam, List<ThreeNozzleFlyShot>>();
            foreach (Cam camera in cameras)
            {
                List<ThreeNozzleFlyShot> list = shots.Where(s => s.Camera == camera)
                    .OrderBy(s => increasing ? s.TriggerY : -s.TriggerY).ToList();
                for (int i = 1; i < list.Count; i++)
                    if (Math.Abs(list[i].TriggerY - list[i - 1].TriggerY) < 0.001)
                        return ThreeNozzleError(string.Format("{0} {1} 飞拍触发点重合：{2} / {3}", what, camera.disc, list[i - 1].Label, list[i].Label));
                ordered[camera] = list;
            }

            EM_RES res = ThreeNozzleDisableFlyTriggers(cameras);
            if (res != EM_RES.OK) return ThreeNozzleError(what + " 飞拍前关闭触发失败");
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 三吸嘴飞拍[{1}] X={2:F3} Y {3:F3}→{4:F3}，触发：{5}", disc, what, x, startY, endY,
                string.Join("，", cameras.Select(c => c.disc + "[" + string.Join(" ", ordered[c].Select(s => s.Label + "@" + s.TriggerY.ToString("F3"))) + "]"))));

            string traceId = DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + "-" + Interlocked.Increment(ref threeNozzleFlyTraceSequence);
            EM_RES passResult = EM_RES.ERR;
            bool disableFailed = false;
            try
            {
                foreach (Cam camera in cameras)
                    camera.BeginFlyDiagnostic(traceId, string.Format("模块={0} 环节={1} Demo={2} 预期={3} Y={4:F3}->{5:F3} 工作速度={6:F3} 脉冲每毫米={7:F3} 触发计划={8}",
                        id + 1, what, Demo, ordered[camera].Count, startY, endY, ax_y.spd_work, ax_y.pul_per_mm,
                        string.Join(";", ordered[camera].Select(s => string.Format("{0}/{1}/身份{2}/名义Y{3:F3}/触发Y{4:F3}", s.Label, s.Task, s.Identity, s.NominalY, s.TriggerY)))));
                passResult = ThreeNozzleFlyPassCore(ref quit, what, x, startY, endY, ordered);
            }
            finally
            {
                // 比较状态必须在 DisableHcmp 清空点表之前读；只读诊断不得阻止原有关闭触发。
                foreach (Cam camera in cameras)
                {
                    try { camera.FlyDiagnosticRecord("关闭前板卡 " + (camera.TriIO == null ? "无触发IO" : ax_y.ReadHcmpDiagnostic(camera.TriIO.num))); }
                    catch { }
                }
                if (ThreeNozzleDisableFlyTriggers(cameras) != EM_RES.OK) disableFailed = true;
                foreach (Cam camera in cameras)
                    camera.EndFlyDiagnostic(string.Format("模块={0} 环节={1} 流程返回={2} 触发关闭失败={3} Ycmd={4:F3} Yenc={5:F3} 结果={6}",
                        id + 1, what, passResult, disableFailed, ax_y.fcmd_pos, ax_y.fenc_pos,
                        string.Join(";", ordered[camera].Select(s => s.Label + "=" + (s.Result == null ? "无可用结果/待补拍" : s.Result.ToString())))));
            }
            if (disableFailed)
            {
                foreach (ThreeNozzleFlyShot shot in shots) shot.Result = null;
                return ThreeNozzleError(what + " 飞拍触发关闭失败，禁止继续动作", true);
            }
            return passResult;
        }

        private EM_RES ThreeNozzleFlyPassCore(ref bool quit, string what, double x, double startY, double endY,
            Dictionary<Cam, List<ThreeNozzleFlyShot>> ordered)
        {
            bool increasing = endY > startY;
            double firstTrigger = ordered.Values.SelectMany(l => l).Select(s => s.TriggerY).Aggregate(increasing ? double.MaxValue : double.MinValue,
                (a, b) => increasing ? Math.Min(a, b) : Math.Max(a, b));
            double curY = ax_y.fcmd_pos;
            // 当前 Y 已在所有触发点的助跑一侧：先配好触发再走，到起点不用再停下来配相机；
            // X 已在这一列且当前 Y 比起点还靠后（助跑更长）时，直接从当前位置飞，不单独停在起点。
            bool preConfigure = increasing ? curY < firstTrigger - 1 : curY > firstTrigger + 1;
            bool behindStart = increasing ? curY <= startY + 0.5 : curY >= startY - 0.5;
            bool xAligned = Math.Abs(ax_x.fcmd_pos - x) < 0.5;
            bool skipStart = preConfigure && behindStart && xAligned;
            // X 还没到这一列时，X 与 Y 同时出发、不在起点停：MT 的多轴运动是逐轴独立下发，Y 的速度曲线与单走 Y 相同；
            // 只要 Y 从起点之前起跑（助跑 ≥ 标定时的 180mm），且 X 能在第一个上相机触发点之前到位（下相机装在 X 上，不受 X 影响）。
            string mergeWhy = null;
            bool mergeX = !ThreeNozzleHardwareConfig.FlyTwoSpeedEnabled && preConfigure && behindStart && !xAligned && ThreeNozzleFlyCanMergeX(ordered, x, curY, increasing, out mergeWhy);
            EM_RES res;
            if (preConfigure)
            {
                res = ThreeNozzleFlyConfigure(what, ordered);
                if (res != EM_RES.OK) return res;
                if (mergeX)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 三吸嘴飞拍[{1}] X 与 Y 同时起飞，从 Y={2:F3} 直接飞（{3}）", disc, what, curY, mergeWhy));
                    res = MT.ZupMove(ref quit, ref ax_x, x, ref ax_y, endY);
                    if (res != EM_RES.OK) return res;
                    return ThreeNozzleFlyCollect(ref quit, what, ordered);
                }
                if (!skipStart)
                {
                    if (mergeWhy != null)
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 三吸嘴飞拍[{1}] X 先到位再起飞（{2}）", disc, what, mergeWhy));
                    if (ThreeNozzleHardwareConfig.FlyTwoSpeedEnabled && behindStart)
                        res = MT.ZupMove(ref quit, ref ax_x, x);
                    else
                        res = MT.ZupMove(ref quit, ref ax_x, x, ref ax_y, startY);
                    if (res != EM_RES.OK) return res;
                }
            }
            else
            {
                // 当前位置在触发点另一侧（例如停在下相机拍照位）：先到起点再配触发，避免回退途中误触发
                res = MT.ZupMove(ref quit, ref ax_x, x, ref ax_y, startY);
                if (res != EM_RES.OK) return res;
                res = ThreeNozzleFlyConfigure(what, ordered);
                if (res != EM_RES.OK) return res;
            }
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 三吸嘴飞拍[{1}] 起飞 Y={2:F3}（{3}）", disc, what, ax_y.fcmd_pos,
                skipStart ? "从当前位置直接飞" : preConfigure ? "触发已提前配置" : "到起点后配置触发"));

            bool usable;
            res = ThreeNozzleFlySpeedMove(ref quit, what, endY, ordered, out usable);
            if (res != EM_RES.OK) return res;
            if (!usable) return EM_RES.OK; // Leave every result null for existing static recapture.
            return ThreeNozzleFlyCollect(ref quit, what, ordered);
        }

        /// <summary>
        /// X 同时运动是否来得及：X 走完（按工作速度 + 加减速时间，偏保守）要早于 Y 到第一个上相机触发点（按匀速估，偏保守）至少 0.15s。
        /// 没有上相机触发点时 X 不影响拍照，直接允许。参数读不到时不合并。
        /// </summary>
        private bool ThreeNozzleFlyCanMergeX(Dictionary<Cam, List<ThreeNozzleFlyShot>> ordered, double x, double curY, bool increasing, out string why)
        {
            List<ThreeNozzleFlyShot> upShots = ordered.Where(kv => kv.Key != dwcam).SelectMany(kv => kv.Value).ToList();
            if (upShots.Count == 0) { why = "本趟只有下相机，X 不影响拍照"; return true; }
            double vx = ax_x.mspd_cur > 0 ? ax_x.mspd_cur : ax_x.spd_work;
            double vy = ax_y.mspd_cur > 0 ? ax_y.mspd_cur : ax_y.spd_work;
            double accX = ax_x.tacc + ax_x.tdec;
            if (accX > 10) accX /= 1000.0;   // 兼容按 ms 填写
            if (!(vx > 0) || !(vy > 0) || !FlyFinite(accX) || accX < 0) { why = "读不到 X/Y 速度参数"; return false; }
            double firstUp = increasing ? upShots.Min(s => s.TriggerY) : upShots.Max(s => s.TriggerY);
            // X 用梯形/三角形速度曲线估算（原来按“距离/速度 + 加速时间 + 减速时间”，1mm 也算成 0.4s，从来合并不了），
            // 再加 0.05s 留给 S 曲线和伺服整定；Y 仍按匀速估（比实际快，偏保守）
            double ta = ax_x.tacc, td = ax_x.tdec;
            if (ta > 10) ta /= 1000.0;
            if (td > 10) td /= 1000.0;
            double tx = ThreeNozzleMoveTimeEstimate(Math.Abs(x - ax_x.fcmd_pos), vx, ta, td) + 0.05;
            // Y 从静止起跑（合并只发生在上一段运动走完之后）：按加速段估算到第一个上相机触发点的时间。
            // 原来按匀速算，比实际少约 0.1s（Tacc/2），进夹具时 X 只差零点几毫米也合并不了（18 号晚上 99 次只合并 8 次）。
            double tay = ax_y.tacc;
            if (tay > 10) tay /= 1000.0;
            double ty = ThreeNozzleTimeFromRest(Math.Abs(firstUp - curY), vy, FlyFinite(tay) && tay > 0 ? tay : 0);
            why = string.Format("[R5] X 约 {0:F2}s 到位，Y 约 {1:F2}s 到第一个上相机触发点", tx, ty);
            return tx + 0.15 < ty;
        }

        /// <summary>从静止加速走 distance 所需时间（不含减速），参数异常时按匀速。</summary>
        internal static double ThreeNozzleTimeFromRest(double distance, double v, double tacc)
        {
            if (!(distance > 0) || !(v > 0)) return 0;
            if (!(tacc > 1e-6)) return distance / v;
            double rampDistance = v * tacc / 2;
            return distance >= rampDistance ? distance / v + tacc / 2 : Math.Sqrt(2 * distance * tacc / v);
        }

        /// <summary>单轴点到点运动时间估算（梯形速度曲线，走不到全速时按三角形）。参数异常时返回 +∞。</summary>
        internal static double ThreeNozzleMoveTimeEstimate(double distance, double v, double tacc, double tdec)
        {
            if (!(distance >= 0) || !(v > 0) || !FlyFinite(tacc) || !FlyFinite(tdec) || tacc < 0 || tdec < 0) return double.PositiveInfinity;
            if (distance < 1e-6) return 0;
            if (tacc < 1e-6 && tdec < 1e-6) return distance / v;
            double rampDistance = v * (tacc + tdec) / 2;
            if (distance >= rampDistance) return distance / v + (tacc + tdec) / 2;
            // 三角形：峰值速度 p，加速段 p*tacc/v、减速段 p*tdec/v，距离 p²(tacc+tdec)/(2v)
            double peak = Math.Sqrt(2 * distance * v / (tacc + tdec));
            return peak * (tacc + tdec) / v;
        }

        /// <summary>飞拍走完后等结果并逐张核对。</summary>
        private EM_RES ThreeNozzleFlyCollect(ref bool quit, string what, Dictionary<Cam, List<ThreeNozzleFlyShot>> ordered)
        {
            foreach (KeyValuePair<Cam, List<ThreeNozzleFlyShot>> item in ordered)
            {
                Cam camera = item.Key;
                List<ThreeNozzleFlyShot> list = item.Value;
                camera.FlyDiagnosticRecord("收集结果开始 Demo=" + Demo + " 登记=" + camera.inputImageCnt);
                if (Demo)
                {
                    // 无料空跑：相机按演示处理，不核对触发计数，只验证轨迹和触发点配置
                    foreach (ThreeNozzleFlyShot shot in list)
                        shot.Result = new Cam.VisionOutPutData { TaskName = shot.Task, bOK = true, bUpdate = true, PosMM = new ST_XYA(0, 0, 0), BarCode = "" };
                    continue;
                }
                // 运动已走完才进到这里：图像在触发后几十毫秒内就到，视觉 CT 最长百来毫秒。
                // 相机收到的张数少于配置、且 ThreeNozzleFlyLostFrameGraceMs 内既没有新图也没有新结果，就判定丢帧，不再死等满 3 秒
                // （18 号日志：进夹具张数不符时臂停在夹具上方约 3.5 秒才去定拍补拍）。
                int lastInput = camera.inputImageCnt, lastDone = camera.List_vs_task_cur.Count(t => t.ResData.bUpdate), idle = 0;
                for (int waited = 0; waited < ThreeNozzleFlyWaitMs && camera.List_vs_task_cur.Any(t => !t.ResData.bUpdate); waited += 10)
                {
                    if (quit || VAR.gsys_set.bquit) return EM_RES.QUIT;
                    Thread.Sleep(10);
                    int input = camera.inputImageCnt, done = camera.List_vs_task_cur.Count(t => t.ResData.bUpdate);
                    if (input != lastInput || done != lastDone) { lastInput = input; lastDone = done; idle = 0; continue; }
                    idle += 10;
                    if (ThreeNozzleFlyLostFrameGraceMs > 0 && input < list.Count && done >= input && idle >= ThreeNozzleFlyLostFrameGraceMs)
                    {
                        camera.FlyDiagnosticRecord(string.Format("[R6] 判定丢帧：已收 {0}/{1} 张，{2}ms 无新图，提前结束等待（共等 {3}ms）",
                            input, list.Count, idle, waited + 10));
                        break;
                    }
                }
                bool countOk = camera.inputImageCnt == list.Count && camera.List_vs_task_cur.Count == list.Count;
                if (!countOk)
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 三吸嘴飞拍[{1}] {2} 张数不符：配置 {3}，相机输入 {4}，全部按飞拍失败处理（定拍补拍）",
                        disc, what, camera.disc, list.Count, camera.inputImageCnt));
                for (int i = 0; i < list.Count; i++)
                {
                    ThreeNozzleFlyShot shot = list[i];
                    if (i >= camera.List_vs_task_cur.Count) { shot.Result = null; continue; }
                    Cam.VisionTask task = camera.List_vs_task_cur[i];
                    camera.FlyDiagnosticRecord(string.Format("任务结果 序号={0} 实际任务={1} 实际身份={2} 更新={3} OK={4} 帧={5} 时间戳={6} CT={7}ms 信息={8}",
                        i, task.TaskName, task.TriPos.n, task.ResData.bUpdate, task.ResData.bOK, task.ResData.TriNum, task.ResData.TimeStamp, task.ResData.CTms, task.ResData.Message));
                    if (!countOk) { shot.Result = null; continue; }
                    bool match = (task.TaskName == shot.Task || task.TaskName == shot.Task + i.ToString())
                        && task.TriPos.n == shot.Identity && task.ResData.bUpdate;
                    if (!match)
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 三吸嘴飞拍[{1}] {2} 第{3}张对不上：任务 {4}/{5}，身份 {6}/{7}，已更新 {8}",
                            disc, what, camera.disc, i + 1, task.TaskName, shot.Task, task.TriPos.n, shot.Identity, task.ResData.bUpdate));
                        shot.Result = null;
                        continue;
                    }
                    shot.Result = task.ResData.Clone();
                }
            }
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 三吸嘴飞拍[{1}] 结果：{2}", disc, what,
                string.Join("，", ordered.Values.SelectMany(l => l).Select(s => s.Label + "=" + (s.Result == null ? "无图" : s.Result.ToString())))));
            return EM_RES.OK;
        }

        private EM_RES ThreeNozzleFlyConfigure(string what, Dictionary<Cam, List<ThreeNozzleFlyShot>> ordered)
        {
            foreach (KeyValuePair<Cam, List<ThreeNozzleFlyShot>> item in ordered)
            {
                Cam camera = item.Key;
                List<ThreeNozzleFlyShot> list = item.Value;
                EM_RES res = camera.ListTaskCfg(list.Select(s => s.Task).ToList(),
                    list.Select(s => new ST_XYN(s.X, s.NominalY, s.Identity)).ToList());
                camera.FlyDiagnosticRecord("任务配置返回=" + res + " 已配置任务=" + camera.List_vs_task_cur.Count);
                if (res != EM_RES.OK || camera.List_vs_task_cur.Count != list.Count)
                    return ThreeNozzleError(string.Format("{0} {1} 配置飞拍任务失败：{2}", what, camera.disc, res));
                res = ax_y.SetHcmp(list.Select(s => s.TriggerY).ToArray(), camera.TriIO.num);
                camera.FlyDiagnosticRecord("位置比较配置返回=" + res + " " + ax_y.ReadHcmpDiagnostic(camera.TriIO.num));
                if (res != EM_RES.OK)
                    return ThreeNozzleError(string.Format("{0} {1} 设置飞拍触发点失败：{2}", what, camera.disc, res));
            }
            return EM_RES.OK;
        }

        /// <summary>
        /// 飞拍没拿到可用的图（Result 为 null 或视觉 NG）时退回定拍补拍一次，与原流程 FlyPlaceWsMod
        /// “飞拍失败，定位到拍照位置→重拍”一致。下相机只走 Y，上相机走 X/Y。
        /// 返回 OK 时 shot.Result 为补拍结果（可能仍为 NG，由调用方按现有定拍规则处理）。
        /// </summary>
        internal EM_RES ThreeNozzleFlyRecapture(ref bool quit, ThreeNozzleFlyShot shot)
        {
            if (shot == null || shot.Camera == null) return EM_RES.PARA_ERR;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} {1} 飞拍{2}，定位到拍照位置 Y={3:F3} 定拍补拍",
                disc, shot.Label, shot.Result == null ? "无图" : "视觉NG", shot.NominalY));
            EM_RES res = shot.Camera == dwcam
                ? MT.ZupMove(ref quit, ref ax_y, shot.NominalY)
                : MT.ZupMove(ref quit, ref ax_x, shot.X, ref ax_y, shot.NominalY);
            if (res != EM_RES.OK) return res;
            Thread.Sleep(50);
            res = shot.Camera.FindTaskTriAndWait(shot.Task, Demo);
            if (res != EM_RES.OK && res != EM_RES.CAM_ERR) return res;
            shot.Result = shot.Camera.curTask == null ? null : shot.Camera.curTask.ResData.Clone();
            shot.Recaptured = true;
            return EM_RES.OK;
        }

        #region 第 2 步：进夹具飞拍（下相机 + 取料前检查）
        private const double ThreeNozzleFlyRunUp = 180.0;   // 与原流程 FlyToWs 的助跑距离一致
        private const double ThreeNozzleFlyRunOut = 50.0;   // 与原流程 FlyToWs 的冲出距离一致

        /// <summary>进夹具后第一个动作的 X（与 ThreeNozzleApproachTargetY 对应）。算不出返回 NaN。</summary>
        private double ThreeNozzleApproachTargetX(ThreeNozzleRuntimePair pair)
        {
            for (int slot = 0; slot < 2; slot++)
            {
                WS.MdDat md = pair.Pockets[slot];
                if (md == null) continue;
                if (md.res >= 0)
                {
                    XT xt = list_xt[pair.FinishedNozzles[slot]];
                    return md.st_pickpos[id].x + xt.st_rol_cap.x - list_xt[0].st_rol_cap.x + xt.st_vs_pos_xtshp.x;
                }
                if (pair.Incoming[slot]) return md.st_pos[id].x;
            }
            return double.NaN;
        }

        /// <summary>进夹具后第一个动作的 Y：有旧料=N1 取旧料位，空穴=上相机放料拍照位。算不出返回 NaN。</summary>
        private double ThreeNozzleApproachTargetY(ThreeNozzleRuntimePair pair)
        {
            for (int slot = 0; slot < 2; slot++)
            {
                WS.MdDat md = pair.Pockets[slot];
                if (md == null) continue;
                if (md.res >= 0)
                {
                    XT xt = list_xt[pair.FinishedNozzles[slot]];
                    return md.st_pickpos[id].y + xt.st_rol_cap.y - list_xt[0].st_rol_cap.y + xt.st_vs_pos_xtshp.y;
                }
                if (pair.Incoming[slot]) return md.st_pos[id].y;
            }
            return double.NaN;
        }

        /// <summary>
        /// 提前预取后回待机：下相机推迟飞拍时停在飞拍起点（拍照位 - 180mm，在料盘安全区内），
        /// 不停在下相机拍照位上，进夹具时不用先倒回去再起飞。否则按原 GoZero。
        /// </summary>
        private EM_RES ThreeNozzleParkAfterPrefetch(ref bool quit, ThreeNozzleRuntimePair pair)
        {
            double minTrigger = double.MaxValue;
            for (int slot = 0; slot < 2; slot++)
            {
                if (pair == null || !pair.DownDeferred[slot] || pair.Prepared[slot] == null) continue;
                XT xt = list_xt[pair.IncomingNozzles[slot]];
                minTrigger = Math.Min(minTrigger, ThreeNozzleFlyTrigger(xt.st_cap_pos.y, xt.xt_cap_ofs, true));
            }
            if (minTrigger == double.MaxValue || !FlyFinite(minTrigger)) return GoZero(ref quit);
            double y = Math.Max(minTrigger - ThreeNozzleFlyRunUp, ax_y.sln);
            // X 直接停在这一组的飞拍列上（Y 仍在料盘安全区内），工站到位后不用先走 X 再起飞
            double parkX = 0;
            if (ThreeNozzleParkAtColumnEnabled)
            {
                WS.MdDat column = pair.Pockets[0] ?? pair.Pockets[1];
                if (column != null && FlyFinite(column.st_pos[id].x)) parkX = column.st_pos[id].x;
                // 停车位不能挡住另一臂：另一臂还在换料/取料时，若本臂停在外侧列，两臂合计外伸超限，
                // 另一臂去外侧料盘列/夹具列会一直等（ZupMove 10 秒超时报“等待…进入安全位置超时”，换料中断）。
                string reachSource;
                double otherReach = ThreeNozzleOtherArmReach(out reachSource);
                double allowed = MT.EffectiveDualArmCombinedExtentLimit - otherReach - 2.0;
                if (Math.Abs(parkX) > allowed)
                {
                    double clamped = allowed > 0 ? Math.Sign(parkX) * allowed : 0;
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format(
                        "{0} [R5] 三吸嘴飞拍: 停车列 X={1:F3} 会挡住另一臂（另一臂最远工作 X 约 {2:F3}，来自 {5}，合计上限 {3:F3}），改停 X={4:F3}",
                        disc, parkX, otherReach, MT.EffectiveDualArmCombinedExtentLimit, clamped, reachSource));
                    parkX = clamped;
                }
            }
            EM_RES res = MT.ZupMove(ref quit, ref ax_x, parkX, ref ax_y, y, ref ax_u1, 0, ref ax_u2, 0);
            if (res != EM_RES.OK) return res;
            res = MT.Move(ref quit, ref list_xt[2].ax_u, 0);
            if (res == EM_RES.OK)
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 三吸嘴飞拍: 提前预取后停在飞拍起点 X={1:F3} Y={2:F3}", disc, parkX, y));
            return res;
        }

        /// <summary>
        /// 另一臂在各工站夹具上可能用到的最远 |X|（拍照位/取料位），用来限制本臂停车位不挡路。料盘列在夹具列范围内。
        /// 只算启用的穴（未启用的工站/穴不会去）；source 写明最远值来自哪个配置键，便于核对示教数据。
        /// </summary>
        private double ThreeNozzleOtherArmReach(out string source)
        {
            int other = id == 0 ? 1 : 0;
            double reach = 0;
            source = "另一臂当前位置";
            AXIS otherX = other == 0 ? MT.AXIS_UDL1_X : MT.AXIS_UDL2_X;
            if (otherX != null && FlyFinite(otherX.fenc_pos)) reach = Math.Abs(otherX.fenc_pos);
            // 只算另一臂真正会去的穴：与 ThreeNozzleEnsurePlan 相同，按穴在列表中的序号，LOW 管 序号%8<4，HIGH 管 序号%8≥4。
            // （18 号晚上日志：WS2Cfg/MD9/PX1=343.439、WS2Cfg/MD8/PX2=-342.017 是对臂负责的穴里本臂那一套坐标，本臂从来不去，
            //  却被算成“最远工作位置”，两臂提前预取后都被压到外侧 X=26/-25 停车。）
            UpDownLoad otherArm = other == 0 ? COM.UDLoad1 : COM.UDLoad2;
            EM_FIN_MOD otherMode = otherArm == null ? EM_FIN_MOD.ALL : otherArm.FindMod;
            foreach (WS ws in COM.list_ws)
            {
                if (ws == null || ws.list_md == null) continue;
                for (int k = 0; k < ws.list_md.Count; k++)
                {
                    WS.MdDat md = ws.list_md[k];
                    if (md == null) continue;
                    if (otherMode == EM_FIN_MOD.NONE || (otherMode == EM_FIN_MOD.LOW && k % 8 >= 4) ||
                        (otherMode == EM_FIN_MOD.HIGH && k % 8 < 4)) continue;
                    bool enabled;
                    try { enabled = md.benable; } catch { enabled = true; }
                    if (!enabled) continue;
                    if (md.st_pos != null && md.st_pos.Length > other && FlyFinite(md.st_pos[other].x) &&
                        Math.Abs(md.st_pos[other].x) < 1000 && Math.Abs(md.st_pos[other].x) > reach)
                    {
                        reach = Math.Abs(md.st_pos[other].x);
                        source = string.Format("WS{0}Cfg/MD{1}/X{2}={3:F3}", ws.num + 1, md.Num, other + 1, md.st_pos[other].x);
                    }
                    if (md.st_pickpos != null && md.st_pickpos.Length > other && FlyFinite(md.st_pickpos[other].x) &&
                        Math.Abs(md.st_pickpos[other].x) < 1000 && Math.Abs(md.st_pickpos[other].x) > reach)
                    {
                        reach = Math.Abs(md.st_pickpos[other].x);
                        source = string.Format("WS{0}Cfg/MD{1}/PX{2}={3:F3}", ws.num + 1, md.Num, other + 1, md.st_pickpos[other].x);
                    }
                }
            }
            return reach;
        }

        /// <summary>
        /// 开关：提前预取后停在本组飞拍列的 X 上（默认开）。关掉恢复停在 X=0。
        /// 停的位置 Y 仍是飞拍起点（料盘安全区内），转盘转动、开合盖时臂不进夹具区。
        /// </summary>
        public static bool ThreeNozzleParkAtColumnEnabled = true;

        /// <summary>开关：进夹具飞拍按第一个取料位的 X 飞（默认开）。关掉恢复按穴拍照列 X 飞、到位后再横移到取料位。</summary>
        public static bool ThreeNozzleFlyAtApproachXEnabled = true;

        /// <summary>预取时是否把下相机拍照推迟到进夹具飞拍。</summary>
        internal bool ThreeNozzleCanDeferDown()
        {
            if (!ThreeNozzleFlyActive) return false;
            if (ThreeNozzleHasSpecialMode) return false;   // GRR/单嘴等特殊模式保持定拍
            if (PT_SET.bdownqr) return false;              // 需要下相机单独扫码时按定拍
            return true;
        }

        /// <summary>取料前检查（WsMod_Shp）能否用飞拍结果：开关条件与 ThreeNozzleCheckBeforePick 一致；要附加拍 WsMod_Shp2 时必须停在穴上，按定拍。</summary>
        private static bool ThreeNozzleBeforePickFlyable()
        {
            return PT_SET.BarcodeMode == (int)PT_SET.BAR_SCAN.UP_SCAN && PT_SET.bOkCheck && !PT_SET.bWsModShp2PhotoAfterCheck;
        }

        /// <summary>
        /// 夹具就绪后进夹具：预取时推迟的下相机（N2/N3 上的新料）和取料前检查（A/B 穴旧料 WsMod_Shp）一趟 Y 飞拍，停在本组穴位这一列。
        /// tryFly=false 或飞拍不可用时，推迟的下相机按原定拍补上（取料前检查在取料时照常定拍）。
        /// 下相机飞拍和补拍都失败时，按原规则把料退回料盘、重新取料并定拍。
        /// </summary>
        private EM_RES ThreeNozzleResolveDeferredDown(ref bool quit, ThreeNozzleRuntimePair pair, bool tryFly)
        {
            if (pair == null) return EM_RES.OK;
            List<int> downSlots = new List<int>();
            for (int slot = 0; slot < 2; slot++)
            {
                if (!pair.Incoming[slot] || pair.Placed[slot] || pair.Prepared[slot] == null) continue;
                if (!pair.DownDeferred[slot] || pair.DownReady[slot]) continue;
                XT xt = list_xt[pair.IncomingNozzles[slot]];
                if (!Object.ReferenceEquals(xt.XtMd, pair.Prepared[slot]))
                    return ThreeNozzleError(xt.disc + " 进夹具飞拍前物料身份变化", true);
                downSlots.Add(slot);
            }

            ThreeNozzleFlyShot[] downShot = new ThreeNozzleFlyShot[2];
            ThreeNozzleFlyShot[] checkShot = new ThreeNozzleFlyShot[2];
            ThreeNozzleFlyShot[] placeShot = new ThreeNozzleFlyShot[2];
            bool[] placeFromCheck = new bool[2];
            List<ThreeNozzleFlyShot> shots = new List<ThreeNozzleFlyShot>();
            string flyBlock = !tryFly ? "放料前补拍" : ThreeNozzleFlyDisabledReason();
            if (flyBlock == null)
            {
                foreach (int slot in downSlots)
                {
                    XT xt = list_xt[pair.IncomingNozzles[slot]];
                    int nozzle = list_xt.IndexOf(xt);
                    downShot[slot] = new ThreeNozzleFlyShot
                    {
                        Camera = dwcam, Task = CONST.ModDwFw[nozzle], X = ax_x.fcmd_pos,
                        NominalY = xt.st_cap_pos.y,
                        TriggerY = ThreeNozzleFlyTrigger(xt.st_cap_pos.y, xt.xt_cap_ofs, true),
                        Identity = nozzle, Label = xt.disc,
                    };
                    shots.Add(downShot[slot]);
                }
                if (ThreeNozzleBeforePickFlyable())
                {
                    for (int slot = 0; slot < 2; slot++)
                    {
                        WS.MdDat md = pair.Pockets[slot];
                        if (md == null || md.res < 0 || pair.FlyBeforePick[slot] != null) continue;   // 空穴/待测：不取旧料，不检查
                        checkShot[slot] = new ThreeNozzleFlyShot
                        {
                            Camera = upcam, Task = CONST.WsModUpFw, X = md.st_pos[id].x,
                            NominalY = md.st_pos[id].y,
                            TriggerY = ThreeNozzleFlyTrigger(md.st_pos[id].y,
                                md.Num <= 8 ? list_xt[0].ws_cap_near_ofs : list_xt[0].ws_cap_far_ofs, true),
                            Identity = md.Num - 1, Label = "穴" + md.Num,
                        };
                    }
                    if (checkShot[0] != null && checkShot[1] != null && Math.Abs(checkShot[0].X - checkShot[1].X) > 0.01)
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 三吸嘴飞拍: 穴{1}/穴{2} X 不同，取料前检查改定拍",
                            disc, pair.Pockets[0].Num, pair.Pockets[1].Num));
                        checkShot[0] = checkShot[1] = null;
                    }
                    foreach (ThreeNozzleFlyShot shot in checkShot) if (shot != null) shots.Add(shot);
                }
                ThreeNozzleAddPrePlaceShots(pair, checkShot, placeShot, placeFromCheck, shots);
            }

            if (shots.Count > 0)
            {
                // X 停在本组穴位这一列（同原流程 FlyToWs）；只有下相机时也先到这一列，进夹具顺路
                WS.MdDat column = pair.Pockets[0] ?? pair.Pockets[1];
                // 有上相机检查时 X 必须在穴的拍照位；只有下相机时 X 不影响拍照，直接用下一个动作的 X，飞完不用再横移
                double nextX = ThreeNozzleApproachTargetX(pair);
                ThreeNozzleFlyShot firstUp = shots.FirstOrDefault(s => s.Camera == upcam);
                double x = firstUp != null ? firstUp.X
                    : FlyFinite(nextX) ? nextX : column != null ? column.st_pos[id].x : ax_x.fcmd_pos;
                // 上相机只有“放料前”时：直接在进夹具第一个动作（取旧料）的 X 上飞，飞完不再为零点几毫米单独横移一次。
                // 放料前结果按这一趟实际拍照 X 换算（FlyBeforePlaceX），与原来同列 X 差的处理相同；有取料前检查时不改。
                if (ThreeNozzleFlyAtApproachXEnabled && firstUp != null && FlyFinite(nextX) && Math.Abs(nextX - x) > 0.001 &&
                    checkShot[0] == null && checkShot[1] == null &&
                    shots.Where(s => s.Camera == upcam).All(s => Object.ReferenceEquals(s, placeShot[0]) || Object.ReferenceEquals(s, placeShot[1])) &&
                    Enumerable.Range(0, 2).All(s => placeShot[s] == null ||
                        (pair.Pockets[s] != null && Math.Abs(nextX - pair.Pockets[s].st_pos[id].x) <= ThreeNozzlePrePlaceMaxColumnDx)))
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} [R1] 三吸嘴飞拍[进夹具] 按第一个取料位 X={1:F3} 飞（拍照列 X={2:F3}，差 {3:F3}mm），到位后不再横移",
                        disc, nextX, x, nextX - x));
                    foreach (ThreeNozzleFlyShot shot in placeShot) if (shot != null) shot.X = nextX;
                    x = nextX;
                }
                double minT = shots.Min(s => s.TriggerY), maxT = shots.Max(s => s.TriggerY);
                double start = Math.Max(minT - ThreeNozzleFlyRunUp, ax_y.sln);
                // 终点直接走到本组第一个动作的 Y（取旧料位或放料拍照位），不在相机后面停一下再进夹具
                double target = ThreeNozzleApproachTargetY(pair);
                double end = maxT + ThreeNozzleFlyRunOut;
                if (FlyFinite(target) && target > end) end = target;
                end = Math.Min(end, ax_y.slp);
                if (minT - start < 20 || end - maxT < 5)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 三吸嘴飞拍: 进夹具 Y 行程不够（起点 {1:F3} / 首触发 {2:F3} / 末触发 {3:F3} / 终点 {4:F3}，软限位 {5:F3}~{6:F3}），改定拍",
                        disc, start, minT, maxT, end, ax_y.sln, ax_y.slp));
                    foreach (ThreeNozzleFlyShot shot in shots) shot.Result = null;
                }
                else
                {
                    EM_RES pass = ThreeNozzleFlyPass(ref quit, "进夹具", x, start, end, shots);
                    if (pass == EM_RES.QUIT || quit) return EM_RES.QUIT;
                    if (threeNozzleRecovery) return pass == EM_RES.OK ? EM_RES.ERR : pass;   // 触发关闭失败
                    if (pass != EM_RES.OK)
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 三吸嘴飞拍: 进夹具飞拍未完成（{1}），改定拍", disc, pass));
                        foreach (ThreeNozzleFlyShot shot in shots) shot.Result = null;
                    }
                }
            }
            else if (downSlots.Count > 0)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 三吸嘴: 推迟的下相机按定拍补上（{1}）", disc, flyBlock ?? "无飞拍对象"));
            }

            // 下相机：飞拍结果 → 不行就定拍补拍 → 还不行就退回料盘重新取
            bool needReturn = false;
            foreach (int slot in downSlots)
            {
                XT xt = list_xt[pair.IncomingNozzles[slot]];
                ThreeNozzleFlyShot shot = downShot[slot];
                Cam.VisionOutPutData vision = null;
                string source;
                if (shot != null && shot.Result != null && shot.Result.bOK)
                {
                    vision = shot.Result;
                    source = "飞拍";
                }
                else if (shot != null)
                {
                    EM_RES r = ThreeNozzleFlyRecapture(ref quit, shot);
                    if (r != EM_RES.OK) return quit ? EM_RES.QUIT : r;
                    if (shot.Result != null && shot.Result.bOK) vision = shot.Result;
                    source = "飞拍失败后定拍补拍";
                }
                else
                {
                    Cam.VisionOutPutData fixedVision = new Cam.VisionOutPutData();
                    EM_RES r = DwCamAction(ref quit, xt, CONST.ModDwFw[list_xt.IndexOf(xt)], ref fixedVision);
                    if (r == EM_RES.OK) vision = fixedVision;
                    else if (r != EM_RES.CAM_ERR_PLACE_BACK) return quit ? EM_RES.QUIT : r;
                    source = "定拍";
                }
                if (vision != null)
                {
                    pair.DownVision[slot] = vision;
                    ThreeNozzleApplyDeferredDownVision(xt, vision);
                    pair.DownReady[slot] = true;
                    pair.DownDeferred[slot] = false;
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 三吸嘴: {1} 下相机结果来自{2}：{3}", disc, xt.disc, source, vision));
                }
                else
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0} 三吸嘴: {1} 下相机{2}失败，退回料盘重新取料", disc, xt.disc, source));
                    BackXtid = xt.id;
                    pair.ReturnCauses[slot] = EM_RES.CAM_ERR_PLACE_BACK;
                    pair.DownDeferred[slot] = false;
                    needReturn = true;
                }
            }

            // 取料前检查：飞拍 OK 的结果交给取旧料时用；没拿到的取料时照常定拍
            for (int slot = 0; slot < 2; slot++)
            {
                ThreeNozzleFlyShot shot = checkShot[slot];
                if (shot == null) continue;
                if (shot.Result != null && shot.Result.bOK) pair.FlyBeforePick[slot] = shot.Result;
                else VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 三吸嘴: {1} 取料前检查飞拍{2}，取料时定拍检查",
                    disc, shot.Label, shot.Result == null ? "无图" : "NG"));
            }

            // 放料前上相机：飞拍 OK 的结果留给放新料时用（启用=直接用，对比=与定拍比较）；没拿到的放料时照常定拍
            for (int slot = 0; slot < 2; slot++)
            {
                ThreeNozzleFlyShot shot = placeFromCheck[slot] ? checkShot[slot] : placeShot[slot];
                if (shot == null) continue;
                if (shot.Result != null && shot.Result.bOK && shot.Result.bUpdate)
                {
                    pair.FlyBeforePlace[slot] = shot.Result;
                    pair.FlyBeforePlaceX[slot] = shot.X;
                    pair.FlyBeforePlaceEmpty[slot] = !placeFromCheck[slot] && shot.Task == CONST.WsUpFw &&
                        pair.Pockets[slot] != null && pair.Pockets[slot].res == (int)Product.EM_CM_RES.EMPTY;
                }
                else VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 放料前飞拍: 穴{1} 飞拍{2}，放料时定拍",
                    disc, pair.Pockets[slot].Num, shot.Result == null ? "无图" : "NG"));
            }

            if (needReturn)
            {
                EM_RES r = ThreeNozzlePreparePair(ref quit, pair, false);
                if (r != EM_RES.OK) return r;
            }
            return EM_RES.OK;
        }
        #endregion

        /// <summary>
        /// 调试页开启“放料前上相机进夹具飞拍”时，给本组要换料（有旧料、要放新料）的穴加上相机触发。
        /// 穴里还有旧料，用所选的有料视觉任务；与取料前检查同一个穴同一个任务时直接共用那张图，
        /// 任务不同时触发点重合，这个穴按取后定拍。上相机各穴 X 必须相同（同一列），不同的穴按取后定拍。
        /// </summary>
        private void ThreeNozzleAddPrePlaceShots(ThreeNozzleRuntimePair pair, ThreeNozzleFlyShot[] checkShot,
            ThreeNozzleFlyShot[] placeShot, bool[] placeFromCheck, List<ThreeNozzleFlyShot> shots)
        {
            // 有旧料的穴：按调试页“放料前上相机进夹具飞拍”模式和所选任务。
            bool modeOn = ThreeNozzlePrePlaceFly != ThreeNozzlePrePlaceFlyMode.Off;
            string task = (ThreeNozzlePrePlaceFlyTask ?? "").Trim();
            if (modeOn && (task.Length == 0 || upcam == null || !upcam.List_vs_task.Any(t => t.TaskName == task)))
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 放料前飞拍: {1}没有视觉任务“{2}”，有料穴按取后定拍",
                    disc, upcam == null ? "上相机" : upcam.disc, task));
                modeOn = false;
            }
            // 空穴：与原双嘴 FlyToWs 一样，进夹具这一趟飞拍空穴 WsUp_Shp，放料直接用，不再一颗一颗停下来定拍。
            bool emptyTaskOk = upcam != null && upcam.List_vs_task.Any(t => t.TaskName == CONST.WsUpFw);
            ThreeNozzleFlyShot existing = shots.FirstOrDefault(s => s.Camera == upcam);
            double? columnX = existing == null ? (double?)null : existing.X;
            // 进夹具第一个动作的 Y：触发点 + 冲出距离超过它时，飞拍会让 Y 冲过头再退回来，反而更慢，这个穴按取后定拍
            double approachY = ThreeNozzleApproachTargetY(pair);
            for (int slot = 0; slot < 2; slot++)
            {
                WS.MdDat md = pair.Pockets[slot];
                if (md == null || !pair.Incoming[slot] || pair.Placed[slot]) continue;
                bool emptyPocket = md.res == (int)Product.EM_CM_RES.EMPTY;
                if (emptyPocket ? !emptyTaskOk : (md.res < 0 || !modeOn)) continue;
                string shotTask = emptyPocket ? CONST.WsUpFw : task;
                if (!emptyPocket && checkShot[slot] != null)
                {
                    if (checkShot[slot].Task == task) placeFromCheck[slot] = true;
                    else VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 放料前飞拍: 穴{1} 已有取料前检查（{2}），触发点重合，按取后定拍",
                        disc, md.Num, checkShot[slot].Task));
                    continue;
                }
                double x = md.st_pos[id].x;
                if (columnX.HasValue && Math.Abs(columnX.Value - x) > ThreeNozzlePrePlaceMaxColumnDx)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 放料前飞拍: 穴{1} X={2:F3} 与本趟上相机 X={3:F3} 相差超过 {4:F3}mm，按取后定拍",
                        disc, md.Num, x, columnX.Value, ThreeNozzlePrePlaceMaxColumnDx));
                    continue;
                }
                double shotX = columnX.HasValue ? columnX.Value : x;
                if (Math.Abs(shotX - x) > 0.001)
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 放料前飞拍: 穴{1} 示教 X={2:F3}，在本趟 X={3:F3} 上拍（差 {4:F3}mm，用结果时按实际拍照位计算）",
                        disc, md.Num, x, shotX, shotX - x));
                double trigger = ThreeNozzleFlyTrigger(md.st_pos[id].y,
                    md.Num <= 8 ? list_xt[0].ws_cap_near_ofs : list_xt[0].ws_cap_far_ofs, true);
                // 空穴拍完下一步是放料，放料位 Y 在拍照位之后（吸嘴在相机后方约 110~120mm），终点冲过拍照位不用退回，不做这项检查
                if (!emptyPocket && FlyFinite(approachY) && trigger + ThreeNozzleFlyRunOut > approachY)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 放料前飞拍: 穴{1} 触发点 {2:F3} 离进夹具终点 {3:F3} 太近，按取后定拍",
                        disc, md.Num, trigger, approachY));
                    continue;
                }
                columnX = shotX;
                placeShot[slot] = new ThreeNozzleFlyShot
                {
                    Camera = upcam, Task = shotTask, X = shotX,
                    NominalY = md.st_pos[id].y,
                    TriggerY = trigger,
                    Identity = md.Num - 1, Label = "穴" + md.Num + "放料前",
                };
                shots.Add(placeShot[slot]);
            }
        }

        /// <summary>
        /// 放新料前上相机：调试页“启用”且进夹具飞拍拿到结果时直接用（只用 X/Y，角度按 0），不再退回拍照；
        /// 否则按原流程定拍 WsUp_Shp。“对比”或启用但超限时，定拍后把飞拍与定拍的差值写日志和 CSV。
        /// upPos/upOffset 是交给 XtPickOrPlaceMod 的上相机拍照位和补偿：定拍时拍照位就是穴示教位；
        /// 用飞拍结果时拍照位是那一趟实际走的 X（可能与示教 X 差零点几毫米）+ 穴示教 Y。
        /// </summary>
        private EM_RES ThreeNozzlePrePlaceUpVision(ref bool quit, ThreeNozzleRuntimePair pair, int slot, XT xt, WS.MdDat md,
            ref Cam.VisionOutPutData up, out ST_XYA upOffset, out ST_XY upPos)
        {
            upOffset = new ST_XYA();
            upPos = md.st_pos[id].ToXY();
            Cam.VisionOutPutData fly = pair.FlyBeforePlace[slot];
            double flyDx = fly == null ? 0 : pair.FlyBeforePlaceX[slot] - md.st_pos[id].x;   // 实际拍照 X − 示教 X
            bool emptyFly = pair.FlyBeforePlaceEmpty[slot];
            pair.FlyBeforePlace[slot] = null;   // 只用一次：重试/返工时按定拍
            pair.FlyBeforePlaceEmpty[slot] = false;
            // 换算到示教拍照位的偏移，用于超限判断和与定拍对比
            ST_XYA flyAtNominal = fly == null ? new ST_XYA() : new ST_XYA(fly.PosMM.x + flyDx, fly.PosMM.y, fly.PosMM.a);
            ThreeNozzlePrePlaceFlyMode mode = ThreeNozzlePrePlaceFly;
            string flyTask = emptyFly ? CONST.WsUpFw : ThreeNozzlePrePlaceFlyTask;
            string why = "对比";
            // 空穴飞拍（同原双嘴）不管调试页模式都直接用；有料穴只有“启用”才直接用
            if (fly != null && (mode == ThreeNozzlePrePlaceFlyMode.Use || emptyFly))
            {
                double limit = PT_SET.Vs_XYofs;
                if (limit > 0 && (Math.Abs(flyAtNominal.x) > limit || Math.Abs(flyAtNominal.y) > limit))
                {
                    why = "启用但飞拍超限改定拍";
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 放料前飞拍: 穴{1} 飞拍偏移(换算到示教位) X{2:F3} Y{3:F3} 超出 Vs_XYofs={4}，改定拍",
                        disc, md.Num, flyAtNominal.x, flyAtNominal.y, limit));
                }
                else
                {
                    up = fly;
                    upPos = new ST_XY(pair.FlyBeforePlaceX[slot], md.st_pos[id].y);
                    upOffset = new ST_XYA(fly.PosMM.x, fly.PosMM.y, 0);
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 放料前飞拍: 穴{1} 用进夹具飞拍结果（{2}{7}）拍照X={3:F3}(示教差{4:F3}) X{5:F3} Y{6:F3}，角度按0，不退回拍照",
                        disc, md.Num, flyTask, upPos.x, flyDx, fly.PosMM.x, fly.PosMM.y, emptyFly ? "，空穴" : ""));
                    return EM_RES.OK;
                }
            }
            Camcfgset();
            EM_RES result = xt.UpCam(ref quit, md.st_pos[id].ToXY(), CONST.WsUpFw, ref up, Demo);
            // 只对视觉失败（CAM_ERR）重拍/弹窗；运动或避障等待超时等其它错误照原样返回，不当成拍照失败
            if (result == EM_RES.CAM_ERR && !quit && !VAR.gsys_set.bquit)
            {
                // 同原双嘴：空穴定位失败先原地重拍一次，仍失败则弹窗提示检查该穴异物/残料，不再静默返回
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0} 放料前定拍: {1}穴{2} WsUp_Shp 定位失败({3})，原地重拍",
                    disc, threeNozzleSource == null ? "" : threeNozzleSource.disc, md.Num, result));
                Thread.Sleep(50);
                result = xt.UpCam(ref quit, md.st_pos[id].ToXY(), CONST.WsUpFw, ref up, Demo);
                if (result == EM_RES.CAM_ERR && !quit && !VAR.gsys_set.bquit)
                {
                    ThreeNozzlePrePlaceCamWarning(md.Num, result);
                    return result;
                }
            }
            if (result != EM_RES.OK) return result;
            upOffset = up.PosMM;
            if (fly != null)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 放料前飞拍[{1}]: 穴{2} 飞拍({3}，拍照X差{13:F3}，已换算到示教位) X{4:F3} Y{5:F3} A{6:F3}，定拍(WsUp_Shp) X{7:F3} Y{8:F3} A{9:F3}，差 X{10:F3} Y{11:F3} A{12:F3}",
                    disc, why, md.Num, ThreeNozzlePrePlaceFlyTask, flyAtNominal.x, flyAtNominal.y, flyAtNominal.a, up.PosMM.x, up.PosMM.y, up.PosMM.a,
                    flyAtNominal.x - up.PosMM.x, flyAtNominal.y - up.PosMM.y, flyAtNominal.a - up.PosMM.a, flyDx));
                UpDownTimingLog.WritePrePlaceCompare(disc, threeNozzleSource == null ? "" : threeNozzleSource.disc, md.Num,
                    ThreeNozzlePrePlaceFlyTask, why, flyAtNominal, up.PosMM, Demo);
            }
            return EM_RES.OK;
        }

        /// <summary>放料前空穴定拍重拍仍失败：提示检查该穴（同原双嘴“工站位置N重拍失败”）。吸嘴上的料保持不动。</summary>
        private void ThreeNozzlePrePlaceCamWarning(int num, EM_RES result)
        {
            string wsName = threeNozzleSource == null ? "" : threeNozzleSource.disc;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0} 放料前定拍: {1}穴{2} 重拍仍失败({3})，请检查该穴是否有残留物料/异物，或人工置空后实物仍在穴内",
                disc, wsName, num, result));
            VAR.sys_inf.Set(EM_ALM_STA.WAR_YELLOW_FLASH, VAR.IsChinese ? "拍照错误!" : "Cam ERR", 20, true);
            MT.ST_WARN warn = new MT.ST_WARN();
            warning form = new warning();
            warn.ok_txt = MultiLanguage.TxtSelct("确定", "OK", "VÂNG");
            warn.ws = null;
            warn.title = MultiLanguage.TxtSelct("提示:拍照错误!", "Tip: Failed to take a picture!", "Mẹo: Không chụp được ảnh!");
            warn.msg = MultiLanguage.TxtSelct(
                string.Format("{0} {1}工站位置{2}重拍失败,请检查对应的工站位置是否有异物或物料!", disc, wsName, num),
                string.Format("{0} {1} location {2} failed to retake, please check for foreign objects or material!", disc, wsName, num),
                string.Format("{0} {1} vị trí {2} chụp lại thất bại, vui lòng kiểm tra vật lạ hoặc vật liệu!", disc, wsName, num));
            warn.lb_msg = warn.msg + MultiLanguage.TxtSelct(
                "\r\n吸嘴上的料保持不动。按确定停止，取出该穴残料/异物后再按运行键继续（会重新拍该穴再放料）。",
                "\r\nThe material on the nozzle is kept. Press OK to stop, remove the leftover/foreign object, then press Run to continue.",
                "\r\nVật liệu trên đầu hút được giữ nguyên. Nhấn OK để dừng, lấy vật lạ ra rồi nhấn Run để tiếp tục.");
            MT.Display_frwarn(form, warn, ERR_ALM.EmErrItem.CaptureAbnomal);
        }

        /// <summary>关闭这些相机在本臂 Y 轴上的位置比较触发输出。</summary>
        private EM_RES ThreeNozzleDisableFlyTriggers(IEnumerable<Cam> cameras)
        {
            EM_RES result = EM_RES.OK;
            foreach (Cam camera in cameras)
            {
                if (camera == null || camera.TriIO == null) continue;
                EM_RES r = ax_y.DisableHcmp(camera.TriIO.num);
                if (r != EM_RES.OK)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0} 关闭{1}飞拍触发失败：{2}", disc, camera.disc, r));
                    result = r;
                }
            }
            return result;
        }
    }
}

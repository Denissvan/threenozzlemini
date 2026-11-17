using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using MotionCtrl;
using System.Drawing;
using Cognex.VisionPro;
using Cognex.VisionPro.Display;
using System.Reflection;
using System.ComponentModel;
using DevReport;
using UI.Class;

namespace UI
{
    // BEGIN FLY_DIAGNOSTIC_BUFFER: pure, bounded, hardware-independent trace buffer.
    internal sealed class FlyDiagnosticBuffer
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> events = new System.Collections.Concurrent.ConcurrentQueue<string>();
        private readonly System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        private int recorded, omitted;
        internal volatile bool Ended;
        internal readonly string Id;
        internal FlyDiagnosticBuffer(string id) { Id = id; }
        internal void Record(string message)
        {
            if (Interlocked.Increment(ref recorded) > 128) { Interlocked.Increment(ref omitted); return; }
            events.Enqueue(string.Format("+{0:F3}ms {1}{2}", watch.Elapsed.TotalMilliseconds, Ended ? "[飞拍结束后] " : "", message));
        }
        internal void Drain(Action<string> write)
        {
            string message;
            while (events.TryDequeue(out message)) write(message);
            int skipped = Interlocked.Exchange(ref omitted, 0);
            if (skipped > 0) write("缓冲达到128条上限，省略=" + skipped);
        }
    }
    // END FLY_DIAGNOSTIC_BUFFER

    /// <summary>
    /// Cam 的海康 USB3/GigE 后端。syscfg.ini 中 [相机名] TYPE=HIK 时生效，其余流程（视觉任务、标定、
    /// CogDisplayer 的触发/实况按钮、飞拍）都走原来的 Cam 接口，只是取图从 VisionPro 换成海康 MVS SDK。
    /// syscfg.ini 键：
    ///   TYPE        = HIK / 空（空=原 VisionPro GigE 取图）
    ///   HIK_SOURCE  = SOFT / LINE0 / LINE1 / LINE2 / LINE3   触发源
    ///   HIK_EDGE    = RISING / FALLING                        硬触发沿
    ///   HIK_TRG_IO  = MT 里的输出 IO 字段名（如 GPIO_OUT_UL_CAM_UP1），硬触发时由软件打一个脉冲；空=外部设备自己触发
    ///   HIK_ADDED   = 1 表示在「吸嘴Z测高-相机」页手动添加的相机
    /// </summary>
    public partial class Cam
    {
        public enum CAM_BACKEND { VisionPro, Hik }
        public enum HIK_SOURCE { SOFT, LINE0, LINE1, LINE2, LINE3 }

        public CAM_BACKEND Backend = CAM_BACKEND.VisionPro;
        public HIK_SOURCE HikSource = HIK_SOURCE.SOFT;
        public bool HikRisingEdge = true;
        public string HikTriggerIoName = "";
        /// <summary>曝光输出线（驱动光源），LINE1 / LINE2 / NONE。原巴斯勒是 Out1 曝光输出，对应海康默认 LINE1。</summary>
        public string HikStrobeLine = "LINE1";
        public bool UserAdded;

        public HikCamera hik;
        /// <summary>最近一次连接海康相机失败的原因（界面显示用），成功时为空。</summary>
        public string LastHikError = "";
        private volatile bool hikLive;
        // 控件「触发」按钮（手动调试）：海康相机一律软触发拍一张，拍完恢复为相机页设置的硬触发源
        private volatile bool manualSoft;
        private volatile bool restoreSource;
        private FlyDiagnosticBuffer flyDiagnostic;
        private int flySdkStart, flyDropStart;

        internal void FlyDiagnosticRecord(string message)
        {
            try { FlyDiagnosticBuffer trace = flyDiagnostic; if (trace != null) trace.Record(message); }
            catch { }
        }

        internal void BeginFlyDiagnostic(string id, string plan)
        {
            try
            {
                FlushFlyDiagnostic(); // 上一趟结束后到达的帧也保留原趟号。
                flySdkStart = hik == null ? 0 : hik.FramesReceived;
                flyDropStart = hik == null ? 0 : hik.FramesDropped;
                flyDiagnostic = new FlyDiagnosticBuffer(id);
                if (hik != null) hik.FrameDiagnostic = FlyDiagnosticRecord;
                FlyDiagnosticRecord("开始 " + plan + " Backend=" + Backend + " 配置触发源=" + HikSource + " 实况=" + hikLive + " 运行视觉=" + bRunImage);
                FlyDiagnosticRecord("开始相机状态 " + (hik == null ? "非海康/未连接" : hik.DumpState(true)));
            }
            catch (Exception ex) { FlyDiagnosticRecord("开始诊断异常:" + ex.Message); }
        }

        internal void EndFlyDiagnostic(string summary)
        {
            try
            {
                if (flyDiagnostic == null) return;
                FlyDiagnosticRecord("结束 " + summary + " SDK收帧增量=" + (hik == null ? -1 : hik.FramesReceived - flySdkStart)
                    + " SDK转换丢弃增量=" + (hik == null ? -1 : hik.FramesDropped - flyDropStart)
                    + " 程序登记=" + inputImageCnt + " 当前任务数=" + List_vs_task_cur.Count + " 当前任务序号=" + curTaskIdx);
                FlyDiagnosticRecord("结束相机状态 " + (hik == null ? "非海康/未连接" : hik.DumpState(true)));
                flyDiagnostic.Ended = true;
                FlushFlyDiagnostic();
                FlyDiagnosticBuffer endedTrace = flyDiagnostic;
                System.Threading.Tasks.Task.Delay(500).ContinueWith(_ => FlushFlyDiagnostic(endedTrace));
            }
            catch { } // 诊断不可影响运动/回退流程。
        }

        private void FlushFlyDiagnostic(FlyDiagnosticBuffer trace = null)
        {
            try
            {
                trace = trace ?? flyDiagnostic;
                if (trace == null) return;
                trace.Drain(message => VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, "[飞拍诊断] 趟=" + trace.Id + " 相机=" + mName + " " + message));
            }
            catch { }
        }

        /// <summary>界面上的手动触发。海康相机不论设置为软/硬触发，都用软触发拍一张，不依赖 IO 和接线。</summary>
        public EM_RES TrigerManual(bool bcontinue = false, bool bWaitImg = false)
        {
            if (!IsHik) return Triger(bcontinue, bWaitImg);
            // 光源是由触发 IO 的上升沿闪亮的：设成硬触发的相机，手动拍照也走 IO 脉冲（和巴斯勒时期、生产流程一样），
            // 同一个上升沿同时触发相机和光源；只有设成软触发的相机才用软触发命令。
            manualSoft = HikSource == HIK_SOURCE.SOFT;
            Interlocked.Increment(ref hikDiagSeq);
            hikDiag = true;
            Diag(string.Format("① 点击触发：实况中={0} 设置触发源={1} 运行视觉={2} 流程={3} 当前任务数={4} 显示控件={5} 触发曝光={6}ms 实况曝光={7}ms",
                hikLive, HikSource, bRunImage, curTask != null ? curTask.TaskName : "(无)", List_vs_task_cur.Count,
                DisplayInfo(mCogRecDisplay), curCapCfg.Exposure, liveCapCfg.Exposure));
            EM_RES res = TrigerHik(bcontinue, bWaitImg);
            Diag("② 触发函数返回 " + res);
            return res;
        }

        // ===== 手动触发诊断日志（[海康诊断] 开头，SYS 级别）=====
        private volatile bool hikDiag;
        private int hikDiagSeq, hikDiagDoneSeq;   // 每次点击一个序号，超时检测只看自己那一次

        private string TriIoText()
        {
            if (TriIO == null) return "无";
            try { return TriIO.Status.ToString(); } catch (Exception ex) { return "读取失败:" + ex.Message; }
        }
        private volatile bool hikDiagTask;   // 下一次视觉流程运行完记一条结果
        private void Diag(string text)
        {
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format("[海康诊断]{0} {1}", disc, text));
        }

        private static string DisplayInfo(CogRecordDisplay d)
        {
            if (d == null) return "null";
            try { return string.Format("{0}(可见={1},句柄={2},已释放={3})", d.Name, d.Visible, d.IsHandleCreated, d.IsDisposed); }
            catch (Exception ex) { return "读取失败:" + ex.Message; }
        }

        /// <summary>触发后 1 秒内没收到新帧时，记一条超时并读回相机参数。</summary>
        private void DiagWatch(int framesBefore)
        {
            HikCamera cam = hik;
            int seq = Volatile.Read(ref hikDiagSeq);
            var t = new Thread(() =>
            {
                Thread.Sleep(1000);
                // 这次点击的图已经处理过，或者已经又点了下一次：不报
                if (cam == null || Volatile.Read(ref hikDiagDoneSeq) == seq || Volatile.Read(ref hikDiagSeq) != seq) return;
                int now = cam.FramesReceived;
                if (now == framesBefore)
                    Diag("✗ 触发后 1 秒内相机没有回任何帧。相机状态：" + cam.DumpState());
                else
                    Diag(string.Format("✗ 触发后收到 {0} 帧，但没有进入图像处理（可能被丢弃）。相机状态：{1}", now - framesBefore, cam.DumpState()));
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>流程里的正常触发（Cam.Triger 调用），按相机页设置的触发源。</summary>
        private EM_RES TrigerHikAuto(bool bcontinue, bool bWaitImg)
        {
            manualSoft = false;
            return TrigerHik(bcontinue, bWaitImg);
        }

        private void RestoreHwSource()
        {
            if (!restoreSource || hik == null) return;
            restoreSource = false;
            string error;
            if (!hik.SetTrigger(true, HikSourceValue, HikRisingEdge, out error))
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0} 恢复硬触发源失败：{1}", disc, error), DReport.EmErrCode.SetParamError, (int)DReport.EmHareware.Cam);
        }
        private int hikLiveBusy;
        private bool hikLiveMarked;
        // 实况开始时的显示控件。切换电控页面时 FrMain 会改 mCogRecDisplay，实况帧仍画在原控件上，不会串到别的页面
        private CogRecordDisplay hikLiveDisplay;
        private CAM_HW_TRIGER originalHwTriger;
        private bool originalHwTrigerSaved;

        public bool IsHik { get { return Backend == CAM_BACKEND.Hik; } }

        /// <summary>是否已经能取图（VisionPro FIFO 或海康相机已打开）。</summary>
        public bool HasAcquisition { get { return mAcqFifo != null || (hik != null && hik.IsOpen); } }

        public string BackendText
        {
            get
            {
                if (!IsHik) return "VisionPro(GigE)";
                return HikSource == HIK_SOURCE.SOFT ? "软触发" : "硬触发";
            }
        }

        /// <summary>清空取图缓存（原来直接调用 mAcqFifo.Flush() 的地方统一走这里）。</summary>
        public void FlushAcq()
        {
            if (mAcqFifo != null) mAcqFifo.Flush();
        }

        #region 配置
        private void ReadBackendCfg(IniFile inf)
        {
            // 本机台相机全部为海康，接线固定：硬触发 = Line0 上升沿，光源输出 = Line1，触发 IO 由程序按相机位置绑定。
            // 界面只让选 软触发/硬触发，其余按固定值处理（忽略 ini 里之前保存的 Line1/Line2/下降沿/IO 等旧选项）。
            Backend = CAM_BACKEND.Hik;
            string source = inf.ReadString(mName, "HIK_SOURCE", "SOFT").Trim('\0', ' ');
            HikSource = source.Equals("SOFT", StringComparison.OrdinalIgnoreCase) || source.Length == 0 ? HIK_SOURCE.SOFT : HIK_SOURCE.LINE0;
            HikRisingEdge = true;
            HikTriggerIoName = "";
            HikStrobeLine = "LINE1";
            UserAdded = inf.ReadInteger(mName, "HIK_ADDED", 0) == 1;
            ApplyTriggerIo();
        }

        private void WriteBackendCfg(IniFile inf, ref bool ischange, string filename)
        {
            inf.WriteString(mName, "TYPE", IsHik ? "HIK" : "VISIONPRO", ref ischange, true, filename);
            inf.WriteString(mName, "HIK_SOURCE", HikSource.ToString(), ref ischange, true, filename);
            inf.WriteInteger(mName, "HIK_ADDED", UserAdded ? 1 : 0, ref ischange, true, filename);
        }

        /// <summary>
        /// 原有相机保留构造时给的硬触发委托；配置了 HIK_TRG_IO 时改为给该输出 IO 打脉冲（与原 CamXXTriger 相同：先 OFF，延时后 ON）。
        /// </summary>
        private void ApplyTriggerIo()
        {
            if (!originalHwTrigerSaved) { originalHwTriger = HwTrigerHandle; originalHwTrigerSaved = true; }
            GPIO io = FindOutputIo(HikTriggerIoName);
            if (io == null)
            {
                HwTrigerHandle = originalHwTriger;
                return;
            }
            if (TriIO == null) TriIO = io;
            HwTrigerHandle = dly =>
            {
                EM_RES res = io.SetOff();
                Thread.Sleep(dly);
                io.SetOn();
                return res;
            };
        }

        /// <summary>MT 中所有 GPIO_OUT_ 开头的输出 IO 字段名，供界面选择硬触发 IO。</summary>
        public static string[] OutputIoNames()
        {
            return typeof(MT).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(GPIO) && f.Name.StartsWith("GPIO_OUT", StringComparison.Ordinal))
                .Select(f => f.Name).OrderBy(n => n).ToArray();
        }

        public static GPIO FindOutputIo(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            FieldInfo f = typeof(MT).GetField(name.Trim(), BindingFlags.Public | BindingFlags.Static);
            return f != null && f.FieldType == typeof(GPIO) ? f.GetValue(null) as GPIO : null;
        }

        private uint HikSourceValue
        {
            get
            {
                switch (HikSource)
                {
                    case HIK_SOURCE.LINE0: return HikCamera.TRIGGER_SOURCE_LINE0;
                    case HIK_SOURCE.LINE1: return HikCamera.TRIGGER_SOURCE_LINE0 + 1;
                    case HIK_SOURCE.LINE2: return HikCamera.TRIGGER_SOURCE_LINE0 + 2;
                    case HIK_SOURCE.LINE3: return HikCamera.TRIGGER_SOURCE_LINE0 + 3;
                    default: return HikCamera.TRIGGER_SOURCE_SOFTWARE;
                }
            }
        }
        #endregion

        #region 初始化 / 释放
        private EM_RES InitHik()
        {
            try { return InitHikCore(); }
            catch (Exception ex)
            {
                // 没装海康 MVS / 缺 MvCameraControl.dll 时在这里报错，不影响其他相机初始化
                DisposeHik();
                bInit = false;
                LastHikError = ex.Message;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0}(海康 SN={1}) 初始化异常：{2}（请确认机台已安装海康 MVS 运行库（MvCameraControl.dll））",
                    disc, mSerialNumber, ex.Message), DReport.EmErrCode.ConnectFailed, (int)DReport.EmHareware.Cam);
                return EM_RES.ERR;
            }
        }

        private EM_RES InitHikCore()
        {
            DisposeHik();
            hik = new HikCamera();
            hik.FrameArrived += OnHikFrame;
            hik.Disconnected += msg =>
            {
                bInit = false;
                status = CAM_STA.DISCONNECT;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, disc + "：" + msg, DReport.EmErrCode.ConnectFailed, (int)DReport.EmHareware.Cam);
            };
            string error;
            if (!hik.Open(mSerialNumber, HikSourceValue, HikRisingEdge, out error))
            {
                LastHikError = error ?? "";
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0}(海康 SN={1}) 初始化失败：{2}", disc, mSerialNumber, error),
                    DReport.EmErrCode.ConnectFailed, (int)DReport.EmHareware.Cam);
                DisposeHik();
                bInit = false;
                return EM_RES.ERR;
            }
            LastHikError = "";
            if (!string.IsNullOrEmpty(hik.LastWarning))
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0}(海康 SN={1})：{2}", disc, mSerialNumber, hik.LastWarning));
            if (bReverseX) hik.SetReverseX(true);
            // 光源：曝光期间由相机输出线点亮（同原巴斯勒 Out1 = ExposureActive）
            if (!string.IsNullOrEmpty(HikStrobeLine) && HikStrobeLine != "NONE")
            {
                string line = "Line" + HikStrobeLine.Substring(HikStrobeLine.Length - 1);
                if (!hik.ConfigureStrobe(line, out error))
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0}(海康 SN={1}) 光源曝光输出 {2} 设置失败：{3}（光源可能不亮，请检查光源线是否接在相机 Line1）",
                        disc, mSerialNumber, line, error, mName));
                else
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format("{0} 光源曝光输出：{1}", disc, line));
            }
            hik.SetExposureMs(curCapCfg.Exposure > 0 ? curCapCfg.Exposure : 10, out error);
            bInit = true;
            status = CAM_STA.READY;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format("{0}(海康 {1} SN={2}) 初始化成功，{3}", disc, hik.Model, mSerialNumber, BackendText));
            return EM_RES.OK;
        }

        /// <summary>改了 SN / 触发方式后重新连接（界面「连接」按钮用）。</summary>
        public EM_RES Reconnect()
        {
            try { if (isLive) StopLive(); } catch { }
            Dispose();          // 两种后端都释放
            mAcqFifo = null;
            bInit = false;
            status = CAM_STA.INIT;
            EM_RES res = Init(string.Format("{0}\\product\\{1}\\Camera\\{2}\\", System.IO.Path.GetFullPath(".."), VAR.gsys_set.cur_product_name, mName));
            status = res == EM_RES.OK ? CAM_STA.READY : CAM_STA.DISCONNECT;
            return res;
        }

        private void DisposeHik()
        {
            HikCamera old = hik;
            hik = null;
            hikLive = false;
            restoreSource = false;
            manualSoft = false;
            if (old == null) return;
            old.FrameArrived -= OnHikFrame;
            old.Dispose();
        }
        #endregion

        #region 拍照参数 / 触发 / 实况
        private EM_RES HikApplyCapCfg(ST_CAP_CFG cfg)
        {
            if (hik == null || !hik.IsOpen)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0} 海康相机未连接!", disc), DReport.EmErrCode.InitFailed, (int)DReport.EmHareware.Cam);
                return EM_RES.CAM_INIT_ERR;
            }
            if (cfg.Exposure > 0)
            {
                string error;
                if (!hik.SetExposureMs(cfg.Exposure, out error))
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0} 设置曝光 {1}ms 失败：{2}", disc, cfg.Exposure, error), DReport.EmErrCode.SetParamError, (int)DReport.EmHareware.Cam);
            }
            // 亮度 → 黑电平、对比度 → 增益（与原 VisionPro 驱动对巴斯勒的处理方式一致）；与上次相同时不重复下发
            if (cfg.HikBcValid)
            {
                string applied, bcError;
                if (!hik.SetBrightnessContrast(cfg.Brightness, cfg.Contrast, out applied, out bcError))
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0} 设置亮度{1:F3}/对比度{2:F3}失败：{3}", disc, cfg.Brightness, cfg.Contrast, bcError),
                        DReport.EmErrCode.SetParamError, (int)DReport.EmHareware.Cam);
                else if (applied != null)
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("[任务曝光] {0} 亮度={1:F3} 对比度={2:F3} → {3}", disc, cfg.Brightness, cfg.Contrast, applied));
            }
            return EM_RES.OK;
        }

        private EM_RES TrigerHik(bool bcontinue, bool bWaitImg)
        {
            FlyDiagnosticRecord("单次/连续触发入口 bcontinue=" + bcontinue + " bWaitImg=" + bWaitImg);
            FlushFlyDiagnostic();
            try
            {
                if (hik == null || !hik.IsOpen)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0} 海康相机未连接，无法触发", disc), DReport.EmErrCode.InitFailed, (int)DReport.EmHareware.Cam);
                    return EM_RES.CAM_INIT_ERR;
                }
                bNewImage = false;
                EM_RES res = StopLive();
                if (res != EM_RES.OK) return res;
                res = ApplyTaskCapCfg(curTask, "触发");
                if (res != EM_RES.OK) return res;
                if (curTask != null) curTask.ResData.Clear();
                this.bcontinue = bcontinue;

                string error;
                if (HikSource == HIK_SOURCE.SOFT || manualSoft)
                {
                    if (HikSource != HIK_SOURCE.SOFT && !restoreSource)
                    {
                        // 手动调试：临时切到软触发源，出图后恢复
                        if (!hik.SetTrigger(true, HikCamera.TRIGGER_SOURCE_SOFTWARE, HikRisingEdge, out error))
                        {
                            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0} 切换软触发失败：{1}", disc, error), DReport.EmErrCode.CaptureFailed, (int)DReport.EmHareware.Cam);
                            return EM_RES.CAM_ERR;
                        }
                        restoreSource = true;
                    }
                    // 本机台光源由相机触发 IO 的上升沿闪亮（巴斯勒时期每次拍照都打 OFF→延时→ON 脉冲）。
                    // 软触发也先打同样的脉冲让光源闪，脉冲一结束立刻发软触发，赶在光源闪亮期间曝光。
                    // 此时相机触发源是软触发，IO 的上升沿不会被当成一次硬触发。
                    if (TriIO != null)
                    {
                        TriIO.SetOff();
                        Thread.Sleep(10);
                        TriIO.SetOn();
                    }
                    if (hikDiag) Diag("③ 发软触发前相机状态：" + hik.DumpState() + " 触发IO=" + TriIoText());
                    int framesBefore = hik.FramesReceived;
                    if (hikDiag) DiagWatch(framesBefore);
                    if (!hik.SoftwareTrigger(out error))
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0} 软触发失败：{1}", disc, error), DReport.EmErrCode.CaptureFailed, (int)DReport.EmHareware.Cam);
                        return EM_RES.CAM_ERR;
                    }
                }
                else if (HwTrigerHandle != null)
                {
                    RestoreHwSource();
                    if (hikDiag) { Diag("③ 打硬触发 IO 脉冲前相机状态：" + hik.DumpState() + " 触发IO=" + TriIoText()); DiagWatch(hik.FramesReceived); }
                    res = HwTrigerHandle();
                    if (hikDiag) Diag("③ IO 脉冲返回 " + res);
                    if (res != EM_RES.OK) return res;
                }
                else if (hikDiag) Diag("③ 硬触发但没有绑定触发 IO，等待外部设备触发");
                // 硬触发且没有配置 IO：等待外部设备（如板卡位置比较输出）触发

                int t = 0;
                while (bWaitImg)
                {
                    if (bNewImage) break;
                    Thread.Sleep(2);
                    Application.DoEvents();
                    if (++t > 500)
                    {
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0}触发等待图像更新超时(1000ms)", mName), DReport.EmErrCode.Timeout, (int)DReport.EmHareware.Cam);
                        return EM_RES.TIMEOUT;
                    }
                }
            }
            catch (Exception ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0}触发异常，{1}", mName, ex.Message), DReport.EmErrCode.CaptureFailed, (int)DReport.EmHareware.Cam, ERR_ALM.EmErrItem.CaptureAbnomal);
                return EM_RES.CAM_ERR;
            }
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0}触发", mName));
            return EM_RES.OK;
        }

        private EM_RES LiveHik(CogRecordDisplay display, bool blive, bool bclear)
        {
            if (display != null) mCogRecDisplay = display;
            if (mCogRecDisplay == null) return EM_RES.CAM_PARA_ERR;
            if (hik == null || !hik.IsOpen)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0} 海康相机未连接，无法实况", disc), DReport.EmErrCode.InitFailed, (int)DReport.EmHareware.Cam);
                return EM_RES.CAM_INIT_ERR;
            }
            string error;
            if (blive && !hikLive)
            {
                bIsLive = true;
                if (TriIO != null) TriIO.SetOn();
                bRunImage = false;
                if (bclear)
                {
                    mCogRecDisplay.StaticGraphics.Clear();
                    mCogRecDisplay.InteractiveGraphics.Clear();
                }
                ST_CAP_CFG liveCfg = LiveCapCfgEffective;
                HikApplyCapCfg(liveCfg.Exposure > 0 ? liveCfg : curCapCfg);
                hikLiveMarked = false;
                hikLiveDisplay = mCogRecDisplay;
                hikLive = true;
                if (!hik.SetTrigger(false, HikSourceValue, HikRisingEdge, out error))
                {
                    hikLive = false;
                    bIsLive = false;
                    bRunImage = true;
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0} 开启实况失败：{1}", disc, error), DReport.EmErrCode.CaptureFailed, (int)DReport.EmHareware.Cam);
                    return EM_RES.CAM_ERR;
                }
            }
            else if (!blive && hikLive)
            {
                hikLive = false;
                bIsLive = false;
                if (TriIO != null) TriIO.SetOff();
                restoreSource = false;
                bool ok = hik.SetTrigger(true, HikSourceValue, HikRisingEdge, out error);
                Thread.Sleep(50);   // 让最后几帧实况图走完，不进入视觉任务
                bRunImage = true;
                CogRecordDisplay liveDisplay = hikLiveDisplay ?? mCogRecDisplay;
                hikLiveDisplay = null;
                try { liveDisplay.StaticGraphics.Remove("CrossMark"); } catch { }
                try { liveDisplay.InteractiveGraphics.Clear(); } catch { }
                HikApplyCapCfg(GetTaskCapCfg(curTask));
                if (!ok)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0} 关闭实况失败：{1}", disc, error), DReport.EmErrCode.CaptureFailed, (int)DReport.EmHareware.Cam);
                    return EM_RES.CAM_ERR;
                }
            }
            return EM_RES.OK;
        }
        #endregion

        #region 取图回调
        private void OnHikFrame(CogImage8Grey frame, int frameNum, long timeStamp)
        {
            FlyDiagnosticRecord(string.Format("应用收到 帧={0} 设备时间={1} 实况={2} 运行视觉={3} 任务序号={4} 登记前={5}", frameNum, timeStamp, hikLive, bRunImage, curTaskIdx, inputImageCnt));
            if (hikLive)
            {
                FlyDiagnosticRecord("分流实况 帧=" + frameNum);
                ShowHikLiveFrame(frame);
                return;
            }
            bool diag = hikDiag;
            if (diag)
            {
                hikDiag = false;
                Volatile.Write(ref hikDiagDoneSeq, Volatile.Read(ref hikDiagSeq));
                string stats;
                try { stats = HikCamera.GrayStats(frame); } catch (Exception ex) { stats = "统计失败:" + ex.Message; }
                Diag(string.Format("④ 收到图像 帧号={0} {1}；运行视觉={2} 流程={3} 显示控件={4}",
                    frameNum, stats, bRunImage, curTask != null ? curTask.TaskName : "(无，直接显示原图)", DisplayInfo(mCogRecDisplay)));
            }
            // 手动软触发的这张已经拍到，恢复硬触发源（连续手动触发时保持软触发）
            if (restoreSource && !(manualSoft && bcontinue)) RestoreHwSource();
            VisionTask vstask = null;
            int taskid = -1;
            CogImage8Grey curImage = null;
            try
            {
                lock (obj)
                {
                    var transform = new CogTransform2DLinear { TranslationX = 0, TranslationY = 0, Scaling = 1, Rotation = 0 };
                    frame.PixelFromRootTransform = transform;
                    Image = frame;
                    inputImageCnt++;
                    bNewImage = true;
                    if (!bRunImage) { FlyDiagnosticRecord("停止处理:运行视觉=false 帧=" + frameNum); if (diag) Diag("✗ 运行视觉=false，这张图被丢弃，没有显示"); return; }
                    vstask = curTask;
                    if (vstask == null)
                    {
                        FlyDiagnosticRecord("仅显示原图:无任务 帧=" + frameNum);
                        // 没有选视觉流程（例如新加的相机还没做任务）：只显示原图
                        RecordDisplay(frame, true);
                        if (diag) Diag("⑤ 已把原图交给显示控件");
                        return;
                    }
                    taskid = curTaskIdx;
                    NextTask();
                    curImage = new CogImage8Grey(frame);
                    curImage.SetPixel(0, 0, (byte)taskid);
                    curImage.SetPixel(1, 0, (byte)(frameNum & 0xFF));
                    if (vstask.ResData.bOK && vstask.ResData.bUpdate)
                    {
                        FlyDiagnosticRecord("丢弃:任务已有OK结果 帧=" + frameNum + " 任务=" + vstask.TaskName);
                        if (diag) Diag("✗ 任务已有结果，这张图按误触发丢弃");
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("当前相机任务已经更新且有结果可能是误触发{0},{1},TimeSamp:{2},FrameNum:{3}",
                            disc, vstask.TaskName, timeStamp, frameNum));
                        return;
                    }
                    vstask.Image = curImage;
                    vstask.ResData.TimeStamp = timeStamp;
                    vstask.ResData.TriNum = frameNum;
                    FlyDiagnosticRecord(string.Format("分配 帧={0} 任务序号={1} 任务={2} 身份={3} 登记后={4}", frameNum, taskid, vstask.TaskName, vstask.TriPos.n, inputImageCnt));
                }
                if (diag) { hikDiagTask = true; Diag(string.Format("⑤ 交给视觉流程 {0}（任务序号 {1}）运行，结果图由流程显示", vstask.TaskName, taskid)); }
                TaskRunImage(taskid, curImage);

                numAcqs++;
                if (numAcqs > 16)
                {
                    GC.Collect();
                    numAcqs = 0;
                }
                if (bcontinue) TrigerHik(bcontinue, false);   // 连续触发沿用本次的触发方式（手动=软触发）
            }
            catch (Exception ex)
            {
                FlyDiagnosticRecord("应用处理异常 帧=" + frameNum + " " + ex.Message);
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format("{0} 图像处理出错,{1}", disc, ex.Message), DReport.EmErrCode.CaptureFailed, (int)DReport.EmHareware.Cam);
                if (Image != null && vstask != null) RecordDisplay(Image, vstask.ResData.bOK);
            }
        }

        /// <summary>实况：只在界面空闲时显示最新一帧，处理不过来的帧直接丢弃。</summary>
        private void ShowHikLiveFrame(CogImage8Grey frame)
        {
            CogRecordDisplay display = hikLiveDisplay;
            if (display == null || display.IsDisposed || !display.IsHandleCreated) return;
            if (Interlocked.Exchange(ref hikLiveBusy, 1) == 1) return;
            try
            {
                display.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (!hikLive || display.IsDisposed) return;
                        display.Image = frame;
                        if (!hikLiveMarked)
                        {
                            hikLiveMarked = true;
                            display.AutoFit = true;
                            try { display.StaticGraphics.Remove("CrossMark"); } catch { }
                            ShowCrossMark(display);
                        }
                    }
                    catch { }
                    finally { Interlocked.Exchange(ref hikLiveBusy, 0); }
                }));
            }
            catch
            {
                Interlocked.Exchange(ref hikLiveBusy, 0);
            }
        }
        #endregion
    }
}

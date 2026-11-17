using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MotionCtrl;

namespace UI
{
    public static class NozzleZCalibrationMotion
    {
        private static int busy, owner, cancelled;
        private static readonly object MotionSync = new object();
        private static readonly HashSet<AXIS> ownedAxes = new HashSet<AXIS>();
        private static readonly Dictionary<AXIS, Exception> stopFailures = new Dictionary<AXIS, Exception>();
        public static bool IsBusy { get { return Volatile.Read(ref busy) != 0; } }
        // 空跑双臂协同时，两个臂各在一个工作线程里运动，都算本次独占动作的所有者。
        private static readonly HashSet<int> workerOwners = new HashSet<int>();
        public static bool IsOwnerThread
        {
            get
            {
                if (!IsBusy) return false;
                int id = Thread.CurrentThread.ManagedThreadId;
                if (Volatile.Read(ref owner) == id) return true;
                lock (workerOwners) return workerOwners.Contains(id);
            }
        }

        /// <summary>
        /// 在 RunExclusive 内部并行执行多个动作（每个一个线程，都登记为所有者），全部结束后返回。
        /// 任一动作失败或取消：立即 Cancel()，停下所有已登记轴，其余动作随之退出；最后抛出第一个真实错误。
        /// </summary>
        public static void RunParallel(params Action[] actions)
        {
            if (!IsOwnerThread) throw new InvalidOperationException("并行动作只能在独占动作内部启动");
            var errors = new List<Exception>();
            var threads = new List<Thread>();
            foreach (Action action in actions)
            {
                Action work = action;
                var t = new Thread(() =>
                {
                    int id = Thread.CurrentThread.ManagedThreadId;
                    lock (workerOwners) workerOwners.Add(id);
                    try { work(); }
                    catch (Exception ex)
                    {
                        lock (errors) errors.Add(ex);
                        Cancel();
                    }
                    finally { lock (workerOwners) workerOwners.Remove(id); }
                });
                t.IsBackground = true;
                threads.Add(t);
            }
            foreach (Thread t in threads) t.Start();
            foreach (Thread t in threads) t.Join();
            if (errors.Count == 0) return;
            Exception first = errors.FirstOrDefault(e => !(e is OperationCanceledException)) ?? errors[0];
            if (first is OperationCanceledException) throw new OperationCanceledException(first.Message, first);
            throw new InvalidOperationException(first.Message, first);
        }
        public static void Cancel()
        {
            lock (MotionSync)
            {
                if (!IsBusy) return;
                Interlocked.Exchange(ref cancelled, 1); VAR.gsys_set.bquit = true;
                StopOwnedAxes();
            }
        }
        // Called under MotionSync. Keep trying all acquired axes even if one driver throws.
        private static void StopOwnedAxes()
        {
            foreach (var axis in ownedAxes)
            {
                try { Require(axis.Stop(), "停止标定运动轴"); }
                catch (Exception ex) { if (!stopFailures.ContainsKey(axis)) stopFailures.Add(axis, ex); }
            }
        }
        public static void RunExclusive(Action action)
        {
            lock (MotionSync)
            {
                CheckIdle();
                if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) throw new InvalidOperationException("已有标定动作正在执行");
                owner = Thread.CurrentThread.ManagedThreadId; cancelled = 0;
                stopFailures.Clear();
                VAR.gsys_set.bquit = false;
            }
            Exception failure = null;
            // AXIS.WaitForStop does not stop an in-flight command on bquit. Also close
            // the race where cancellation arrives between the driver's check and dispatch.
            using (var cancellationWatch = new Timer(_ =>
            {
                lock (MotionSync)
                    if (IsBusy && (Volatile.Read(ref cancelled) != 0 || VAR.gsys_set.bquit)) StopOwnedAxes();
            }, null, 20, 20))
            try
            {
                CheckIdle();
                Check();
                action();
            }
            catch (Exception ex) { failure = ex; throw; }
            finally
            {
                List<Exception> failures;
                lock (MotionSync)
                {
                    StopOwnedAxes(); failures = stopFailures.Values.ToList();
                    stopFailures.Clear(); ownedAxes.Clear(); owner = 0;
                    Interlocked.Exchange(ref busy, 0);
                }
                if (failures.Count > 0)
                {
                    if (failure != null) failures.Insert(0, failure);
                    throw new AggregateException("标定运动停止失败，请核对轴状态", failures);
                }
            }
        }
        public static void CheckIdle()
        {
            if (VAR.gsys_set.status == EM_SYS_STA.RUN || VAR.gsys_set.status == EM_SYS_STA.PAUSE || VAR.gsys_set.status == EM_SYS_STA.RESET ||
                BaseAction.bRuning || BaseAction.brun || UpDownLoad.brun || !ThreeNozzleCoordinator.WorkersStopped ||
                (UpDownLoad.UpDownLoadTask != null && !UpDownLoad.UpDownLoadTask.IsCompleted))
                throw new InvalidOperationException("请退出自动、测试和复位流程后标定");
            foreach (var arm in COM.List_UDLoad)
                if (arm.brun_ud || (arm.UpDownTask != null && !arm.UpDownTask.IsCompleted)) throw new InvalidOperationException("上下料任务尚未退出");
        }
        private static void Check()
        {
            CheckIdle();
            if (!IsOwnerThread || Volatile.Read(ref cancelled) != 0 || VAR.gsys_set.bquit) throw new OperationCanceledException("标定已取消");
        }
        public static void RegisterAxes(params AXIS[] axes)
        {
            lock (MotionSync)
            {
                Check();
                if (axes == null || axes.Any(axis => axis == null)) throw new ArgumentException("标定运动轴不能为空");
                foreach (var axis in axes) ownedAxes.Add(axis);
            }
        }
        private static void Cleanup(Exception originalFailure, params Action[] actions)
        {
            var failures = new List<Exception>();
            foreach (var action in actions)
                try { action(); } catch (Exception ex) { failures.Add(ex); }
            if (failures.Count == 0) return;
            if (originalFailure != null) failures.Insert(0, originalFailure);
            throw new AggregateException("标定清理失败，请核对轴和真空状态", failures);
        }
        private static void Require(EM_RES result, string operation)
        {
            if (result != EM_RES.OK) throw new InvalidOperationException(operation + "失败：" + result);
        }
        private static void CheckAxis(AXIS axis, double target)
        {
            if (axis == null || axis.card == null || !axis.card.isReady || axis.home_status != AXIS.HOME_STA.OK ||
                !axis.isStop || !NozzleZCalibrationData.Finite(target) || target < axis.sln || target > axis.slp)
                throw new InvalidOperationException("轴未就绪/未回零/未停止，或目标超出软限位");
        }
        private static XT GetNozzle(int arm, int nozzle)
        {
            if (!ThreeNozzleHardwareConfig.IsConfigured) throw new InvalidOperationException("请先完成三吸嘴轴卡与IO配置");
            if (arm < 0 || arm > 1 || nozzle < 0 || nozzle > 2 || COM.List_UDLoad[arm].list_xt.Count != 3)
                throw new InvalidOperationException("机械臂/吸嘴配置无效");
            var xt = COM.List_UDLoad[arm].list_xt[nozzle];
            if (xt.ZApproachDirection != 1 && xt.ZApproachDirection != -1) throw new InvalidOperationException("Z方向无效");
            return xt;
        }
        private static bool ReadContact(XT xt)
        {
            var sensor = xt.cy_zk == null ? null : xt.cy_zk.io_sen_on;
            if (sensor == null || sensor.card == null || !sensor.card.isReady)
                throw new InvalidOperationException("真空检测未就绪");
            GPIO.IO_STA state = sensor.Status;
            if (state != GPIO.IO_STA.IN_ON && state != GPIO.IO_STA.IN_OFF)
                throw new InvalidOperationException("真空输入读取失败，停止测高");
            GPIO.IO_STA active = xt.cy_zk.io_sen_on_active;
            if (active != GPIO.IO_STA.IN_ON && active != GPIO.IO_STA.IN_OFF)
                throw new InvalidOperationException("真空输入有效电平配置无效");
            return state == active;
        }
        public static NozzleZCalibrationDraft.PointDraft Learn(int arm, int nozzle)
        {
            if (IsBusy) throw new InvalidOperationException("测高执行中不能学习");
            CheckIdle(); var xt = GetNozzle(arm, nozzle);
            foreach (var axis in new[] { xt.ax_x, xt.ax_y, xt.ax_z, xt.ax_u }) CheckAxis(axis, axis.fenc_pos);
            return new NozzleZCalibrationDraft.PointDraft { X = xt.ax_x.fenc_pos, Y = xt.ax_y.fenc_pos,
                U = xt.ax_u.fenc_pos, Approach = xt.ZApproachDirection * xt.ax_z.fenc_pos };
        }
        public static void Position(NozzleZCalibrationData data, int arm, int nozzle)
        {
            var xt = GetNozzle(arm, nozzle);
            double previousSpeed = xt.ax_z.spd_cur;
            bool speedChanged = false;
            Exception failure = null;
            try { PositionCore(data, arm, nozzle, () => speedChanged = true); }
            catch (Exception ex) { failure = ex; throw; }
            finally { if (speedChanged && previousSpeed > 0) Cleanup(failure, () => Require(xt.ax_z.SetSpeed(previousSpeed), "恢复Z速度")); }
        }
        private static void PositionCore(NozzleZCalibrationData data, int arm, int nozzle, Action changingSpeed)
        {
            Check(); data.ValidateProbe(arm, nozzle); var xt = GetNozzle(arm, nozzle);
            var p = data.Arms[arm].Nozzles[nozzle].Point;
            CheckAxis(xt.ax_x, p.X.Value); CheckAxis(xt.ax_y, p.Y.Value); CheckAxis(xt.ax_u, p.U.Value);
            CheckAxis(xt.ax_z, xt.ZApproachDirection * p.End.Value);
            foreach (var a in COM.List_UDLoad)
                foreach (var n in a.list_xt)
                {
                    CheckAxis(n.ax_z, 0);
                    if (n.XtMd != null || ReadContact(n))
                        throw new InvalidOperationException("测高前六嘴须空嘴且真空检测就绪");
                }
            // ZupMove's avoidance can also move the other arm. Own all sixteen
            // distinct axes before dispatch so cancellation covers that motion too.
            RegisterAxes(COM.List_UDLoad.SelectMany(a => a.list_xt.SelectMany(n =>
                new[] { n.ax_x, n.ax_y, n.ax_z, n.ax_u })).ToArray());
            Require(MT.RaiseAllArmZ(ref VAR.gsys_set.bquit, 0), "臂1避让");
            Require(MT.RaiseAllArmZ(ref VAR.gsys_set.bquit, 1), "臂2避让"); Check();
            Require(MT.ZupMove(ref VAR.gsys_set.bquit, ref xt.ax_x, p.X.Value, ref xt.ax_y, p.Y.Value), "基准XY定位"); Check();
            Require(MT.Move(ref VAR.gsys_set.bquit, ref xt.ax_u, p.U.Value), "基准U定位"); Check();
            changingSpeed();
            Require(xt.ax_z.SetSpeed(data.Arms[arm].Speed), "设置接近速度");
            Require(MT.Move(ref VAR.gsys_set.bquit, ref xt.ax_z, xt.ZApproachDirection * p.Approach.Value), "接近高度定位");
        }
        public static void Measure(NozzleZCalibrationData data, int arm, int nozzle)
        {
            var xt = GetNozzle(arm, nozzle);
            double previousSpeed = xt.ax_z.spd_cur;
            bool speedChanged = false, ownsVacuum = false;
            Exception failure = null;
            try
            {
                PositionCore(data, arm, nozzle, () => speedChanged = true);
                var p = data.Arms[arm].Nozzles[nozzle].Point;
                var timer = Stopwatch.StartNew();
                Action check = () => { Check(); if (timer.Elapsed.TotalSeconds > 180) throw new TimeoutException("单嘴测高超过180秒"); };
                check(); ownsVacuum = true;
                Require(xt.gpio_pzk.SetOff(), "关闭破真空");
                double[] samples = NozzleZProbe.Measure(p.Approach.Value, p.End.Value, data.Arms[arm].Step,
                    target => { check(); Require(MT.Move(ref VAR.gsys_set.bquit, ref xt.ax_z, xt.ZApproachDirection * target), "Z探测移动"); },
                    () => xt.ZApproachDirection * xt.ax_z.fenc_pos,
                    on => Require(on ? xt.cy_zk.SetOn() : xt.cy_zk.SetOff(), "真空切换"),
                    () => ReadContact(xt), check,
                    ms => { Thread.Sleep(ms); check(); });
                Require(MT.Move(ref VAR.gsys_set.bquit, ref xt.ax_z, 0), "测高抬起");
                data.Record(arm, nozzle, samples, xt.ZApproachDirection);
            }
            catch (Exception ex) { failure = ex; throw; }
            finally
            {
                // No automatic retraction following a fault/cancel: stop first, do not start another move.
                var cleanup = new List<Action>();
                if (ownsVacuum)
                {
                    cleanup.Add(() => Require(xt.ax_z.Stop(), "停止Z轴"));
                    cleanup.Add(() => Require(xt.cy_zk.SetOff(), "关闭测高真空"));
                    cleanup.Add(() => Require(xt.gpio_pzk.SetOff(), "关闭测高破真空"));
                }
                if (speedChanged && previousSpeed > 0) cleanup.Add(() => Require(xt.ax_z.SetSpeed(previousSpeed), "恢复Z速度"));
                Cleanup(failure, cleanup.ToArray());
            }
        }
    }
}

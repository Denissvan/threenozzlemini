using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MotionCtrl;

namespace UI.Compment
{
    public partial class RotAndFly
    {
        private DataGridView flyResults;
        private Button cancelCalibration;
        private readonly Dictionary<string, double> flyResiduals = new Dictionary<string, double>();
        private sealed class FlySample
        {
            public XT Nozzle;
            public WS.MdDat Fixture;
            public Cam Camera;
            public string TaskName;
            public double Y;
            public double StaticY;
            public double Offset;
            public double Residual;
            public int Identity { get { return Nozzle != null ? Nozzle.SlotIndex : Fixture.Num - 1; } }
            public string Key { get { return Nozzle != null ? "N" + Nozzle.SlotIndex : (Fixture.Num <= 8 ? "Near" : "Far"); } }
        }

        private static string NozzleCaption(XT xt)
        {
            return string.Format("{0}{1}/N{2} (ID {3})", VAR.IsChinese ? "臂" : "Arm",
                xt.Parent == null ? 0 : xt.Parent.id + 1, xt.SlotIndex + 1, xt.id);
        }

        private void InitializeCalibrationResults()
        {
            foreach (Control control in new Control[] { tb_xt1_cap, tb_xt2_cap, tb_xt1_ofs, tb_xt2_ofs,
                tb_ws_cap_before, tb_ws_cap_after, tb_ws_ofs_before, tb_ws_ofs_after,
                lbl_xt1_cap, lbl_xt2_cap, lbl_frontws, lbl_behindws, lbl_flycam_offset, lbl_flycam_test })
                control.Visible = false;
            flyResults = new DataGridView { Location = new Point(145, 126), Size = new Size(455, 108),
                ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 21, Font = new Font(Font.FontFamily, 9F), ScrollBars = ScrollBars.Vertical };
            flyResults.RowTemplate.Height = 17;
            flyResults.Columns.Add("target", VAR.IsChinese ? "测量对象" : "Target");
            flyResults.Columns.Add("offset", VAR.IsChinese ? "已存偏差(mm)" : "Saved (mm)");
            flyResults.Columns.Add("residual", VAR.IsChinese ? "复测残差(mm)" : "Residual (mm)");
            Controls.Add(flyResults);
            cancelCalibration = new Button { Text = VAR.IsChinese ? "停止标定" : "Stop", Location = new Point(677, 202), Size = new Size(106, 32), Enabled = false };
            cancelCalibration.Click += (sender, e) => NozzleZCalibrationMotion.Cancel();
            Controls.Add(cancelCalibration);
            lbl_mk.Visible = false;
        }

        private void RefreshFlyResults(UpDownLoad arm)
        {
            if (InvokeRequired) { Invoke(new Action(() => RefreshFlyResults(arm))); return; }
            if (flyResults == null) return;
            flyResults.Rows.Clear();
            if (arm == null || arm.list_xt.Count == 0) return;
            foreach (XT xt in arm.list_xt.OrderBy(x => x.SlotIndex))
                AddFlyResult(arm.id, "N" + xt.SlotIndex, "N" + (xt.SlotIndex + 1), xt.xt_cap_ofs);
            AddFlyResult(arm.id, "Near", VAR.IsChinese ? "前排" : "Near", arm.list_xt[0].ws_cap_near_ofs);
            AddFlyResult(arm.id, "Far", VAR.IsChinese ? "后排" : "Far", arm.list_xt[0].ws_cap_far_ofs);
        }

        private void AddFlyResult(int arm, string key, string caption, double offset)
        {
            double residual;
            flyResults.Rows.Add(caption, offset.ToString("F3"),
                flyResiduals.TryGetValue(arm + ":" + key, out residual) ? residual.ToString("F3") : "—");
        }

        private async Task<EM_RES> RunCalibration(Func<EM_RES> action)
        {
            if (NozzleZCalibrationMotion.IsBusy) return EM_RES.ERR;
            EM_RES result = EM_RES.ERR;
            var enabled = Controls.Cast<Control>().ToDictionary(c => c, c => c.Enabled);
            try
            {
                foreach (Control control in Controls) control.Enabled = control == cancelCalibration;
                // A worker owns the motion lease; UI reentrancy cannot impersonate the owner.
                await Task.Run(() => NozzleZCalibrationMotion.RunExclusive(() =>
                {
                    // ZupMove may retract both arms and move the opposing XY for clearance.
                    NozzleZCalibrationMotion.RegisterAxes(COM.List_UDLoad
                        .SelectMany(arm => arm.list_xt)
                        .SelectMany(xt => new[] { xt.ax_x, xt.ax_y, xt.ax_z, xt.ax_u }).ToArray());
                    result = action();
                }));
            }
            catch (Exception ex) { result = CalibrationError(ex.Message); }
            finally
            {
                foreach (var item in enabled) item.Key.Enabled = item.Value;
                cancelCalibration.Enabled = false;
            }
            return result;
        }

        private static EM_RES CalibrationError(string message)
        {
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, message);
            return EM_RES.ERR;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private List<FlySample> BuildFlySamples(UpDownLoad arm, WS ws)
        {
            if (arm == null || ws == null || arm.list_xt.Count == 0) throw new InvalidOperationException("无标定机械臂/工站 / No calibration arm/station");
            if (!arm.list_xt.Select(xt => xt.SlotIndex).OrderBy(slot => slot).SequenceEqual(Enumerable.Range(0, arm.list_xt.Count)))
                throw new InvalidOperationException("吸嘴槽位缺失或重复 / Missing or duplicate nozzle slot");
            if (arm.Demo) throw new InvalidOperationException("演示模式不能保存实测标定 / Cannot calibrate in demo mode");
            var samples = new List<FlySample>();
            foreach (XT xt in arm.list_xt.OrderBy(x => x.SlotIndex))
            {
                if (xt.SlotIndex >= CONST.ModDwFw.Length || xt.Parent != arm || !Finite(xt.st_cap_pos.y))
                    throw new InvalidOperationException("吸嘴映射或拍照位置无效 / Invalid nozzle mapping/capture position");
                if (!xt.cy_zk.isONByChkSen)
                    throw new InvalidOperationException(NozzleCaption(xt) + " 请先吸持真实标定件 / Hold a real calibration part before fly calibration");
                samples.Add(new FlySample { Nozzle = xt, Camera = arm.dwcam, TaskName = CONST.ModDwFw[xt.SlotIndex], Y = xt.st_cap_pos.y });
            }
            int[] fixtures;
            if (PT_SET.issmall || PT_SET.bitOpenMode == 2) fixtures = new[] { 7, 15 };
            else if (PT_SET.bitOpenMode == 1) fixtures = new[] { 6, 14 };
            else if (PT_SET.bitOpenMode == 3) fixtures = new[] { 15 };
            else if (PT_SET.bitOpenMode == 4) fixtures = new[] { 7 };
            else throw new InvalidOperationException("工站开位模式无效 / Invalid fixture mode");
            foreach (int index in fixtures)
            {
                if (index >= ws.list_md.Count) throw new InvalidOperationException("标定工站位置不足 / Missing fixture positions");
                WS.MdDat fixture = ws.list_md[index];
                samples.Add(new FlySample { Fixture = fixture, Camera = arm.upcam, TaskName = CONST.WsUpFw, Y = fixture.st_pos[arm.id].y });
            }
            foreach (FlySample sample in samples)
            {
                if (!Finite(sample.Y) || !sample.Camera.List_vs_task.Any(t => t.TaskName == sample.TaskName))
                    throw new InvalidOperationException("缺少有效独立模板或拍照位置 / Missing template or capture position: " + sample.TaskName);
            }
            double fixtureX = samples.First(s => s.Fixture != null).Fixture.st_pos[arm.id].x;
            if (!Finite(fixtureX) || samples.Any(s => s.Fixture != null && Math.Abs(s.Fixture.st_pos[arm.id].x - fixtureX) > 0.01))
                throw new InvalidOperationException("同次Y飞拍的工站X必须一致 / Fixture X positions must match for a Y scan");
            return samples;
        }

        private EM_RES CalibrateFly(UpDownLoad arm)
        {
            EM_RES result = EM_RES.ERR, restore = EM_RES.ERR;
            try { result = CalibrateFlyCore(arm); }
            finally { restore = arm.ax_y.SetSpeed(arm.ax_y.spd_start, arm.ax_y.spd_work, arm.ax_y.spd_stop, arm.ax_y.tacc, arm.ax_y.tdec); }
            return restore == EM_RES.OK ? result : restore;
        }

        private EM_RES CalibrateFlyCore(UpDownLoad arm)
        {
            List<FlySample> samples = BuildFlySamples(arm, Turntable.GetWSOnFeedPos);
            double fixtureX = samples.First(s => s.Fixture != null).Fixture.st_pos[arm.id].x;
            foreach (FlySample sample in samples)
                flyResiduals.Remove(arm.id + ":" + sample.Key);
            RefreshFlyResults(arm);
            foreach (Cam camera in new[] { arm.dwcam, arm.upcam })
            {
                EM_RES stopLive = (EM_RES)Invoke(new Func<EM_RES>(() => camera.StopLive()));
                if (stopLive != EM_RES.OK) return stopLive;
            }
            EM_RES disableDown = arm.ax_y.DisableHcmp(arm.dwcam.TriIO.num);
            EM_RES disableUp = arm.ax_y.DisableHcmp(arm.upcam.TriIO.num);
            if (disableDown != EM_RES.OK || disableUp != EM_RES.OK) return EM_RES.ERR;
            EM_RES speedResult = MT.SetAllAxToWorkSpd();
            if (speedResult != EM_RES.OK) return speedResult;
            if (ThreeNozzleHardwareConfig.FlyTwoSpeedEnabled)
            {
                double low = ThreeNozzleHardwareConfig.FlyLowSpeed(arm.id);
                if (!Finite(low) || low <= 0 || low > arm.ax_y.spd_work) return EM_RES.PARA_ERR;
                speedResult = arm.ax_y.SetSpeed(Math.Min(arm.ax_y.spd_start, low), low, Math.Min(arm.ax_y.spd_stop, low), arm.ax_y.tacc, arm.ax_y.tdec);
                if (speedResult != EM_RES.OK) return speedResult;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format("模块{0} 飞拍标定采用低速 {1:F1} mm/s", arm.id + 1, low));
            }
            EM_RES result = MT.ZupMove(ref VAR.gsys_set.bquit, ref arm.ax_x, fixtureX, true);
            if (result != EM_RES.OK) return result;
            foreach (XT xt in arm.list_xt)
            {
                result = MT.ZupMove(ref VAR.gsys_set.bquit, ref xt.ax_u, 0, true);
                if (result != EM_RES.OK) return result;
            }
            foreach (FlySample sample in samples)
            {
                result = MT.ZupMove(ref VAR.gsys_set.bquit, ref arm.ax_y, sample.Y, true);
                if (result != EM_RES.OK) return result;
                if (!CalibrationDelay(500)) return EM_RES.QUIT;
                result = sample.Camera.FindTaskTriAndWait(sample.TaskName);
                if (result != EM_RES.OK || !sample.Camera.curTask.ResData.bOK || !Finite(sample.Camera.curTask.ResData.PosMM.y)) return EM_RES.ERR;
                sample.StaticY = sample.Camera.curTask.ResData.PosMM.y;
            }
            result = CaptureFlyPass(arm, samples, false);
            if (result != EM_RES.OK) return result;
            result = CaptureFlyPass(arm, samples, true);
            if (result != EM_RES.OK || VAR.gsys_set.bquit) return result == EM_RES.OK ? EM_RES.QUIT : result;
            // Commit only after BOTH scans validate. Never alter XtMd, vacuum or static st_cap_pos.
            foreach (FlySample sample in samples)
            {
                if (sample.Nozzle != null) sample.Nozzle.xt_cap_ofs = sample.Offset;
                else foreach (XT xt in arm.list_xt)
                {
                    if (sample.Fixture.Num <= 8) xt.ws_cap_near_ofs = sample.Offset;
                    else xt.ws_cap_far_ofs = sample.Offset;
                }
                flyResiduals[arm.id + ":" + sample.Key] = sample.Residual;
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.NOR, string.Format("Arm{0} {1} fly offset={2:F3}, residual={3:F3} mm", arm.id + 1, sample.Key, sample.Offset, sample.Residual));
            }
            foreach (XT xt in arm.list_xt) xt.SaveHwCfg();
            RefreshFlyResults(arm);
            return EM_RES.OK;
        }

        private static bool CalibrationDelay(int milliseconds)
        {
            for (int elapsed = 0; elapsed < milliseconds; elapsed += 10)
            {
                if (VAR.gsys_set.bquit) return false;
                Thread.Sleep(10);
            }
            return !VAR.gsys_set.bquit;
        }

        private EM_RES CaptureFlyPass(UpDownLoad arm, List<FlySample> samples, bool corrected)
        {
            Func<FlySample, double> triggerY = s => s.Y - (corrected ? s.Offset : 0);
            // Keep the original 180 mm approach and 50 mm exit distances; limits remain motion-controller checked.
            double start = samples.Min(triggerY) - 180;
            double finish = samples.Max(triggerY) + 50;
            EM_RES result = MT.ZupMove(ref VAR.gsys_set.bquit, ref arm.ax_y, start, true);
            if (result != EM_RES.OK) return result;
            try
            {
                foreach (Cam camera in new[] { arm.dwcam, arm.upcam })
                {
                    var ordered = samples.Where(s => s.Camera == camera).OrderBy(triggerY).ToList();
                    var positions = ordered.Select(s => triggerY(s)).ToArray();
                    for (int i = 1; i < positions.Length; i++)
                        if (positions[i] - positions[i - 1] < 0.001) return CalibrationError("飞拍触发位置重合 / Duplicate fly trigger positions");
                    result = camera.ListTaskCfg(ordered.Select(s => s.TaskName).ToList(),
                        ordered.Select(s => new ST_XYN(0, s.Y, s.Identity)).ToList());
                    if (result != EM_RES.OK || camera.List_vs_task_cur.Count != ordered.Count) return EM_RES.ERR;
                    result = arm.ax_y.SetHcmp(positions, camera.TriIO.num);
                    if (result != EM_RES.OK) return result;
                }
                result = MT.ZupMove(ref VAR.gsys_set.bquit, ref arm.ax_y, finish, true);
                if (result != EM_RES.OK) return result;
                foreach (Cam camera in new[] { arm.dwcam, arm.upcam })
                {
                    var ordered = samples.Where(s => s.Camera == camera).OrderBy(triggerY).ToList();
                    // Check exact exposure count, task identity and completed successful finite outputs.
                    for (int wait = 0; wait < 600 && camera.List_vs_task_cur.Any(t => !t.ResData.bUpdate); wait++)
                        if (!CalibrationDelay(10)) return EM_RES.QUIT;
                    if (camera.inputImageCnt != ordered.Count || camera.List_vs_task_cur.Count != ordered.Count) return EM_RES.ERR;
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        Cam.VisionTask task = camera.List_vs_task_cur[i];
                        FlySample sample = ordered[i];
                        if (task.TaskName != sample.TaskName && task.TaskName != sample.TaskName + i.ToString()) return EM_RES.ERR;
                        if (!task.ResData.bUpdate || !task.ResData.bOK || task.TriPos.n != sample.Identity || !Finite(task.ResData.PosMM.y)) return EM_RES.ERR;
                        double delta = task.ResData.PosMM.y - sample.StaticY;
                        if (!Finite(delta)) return EM_RES.ERR;
                        if (corrected) sample.Residual = delta;
                        else sample.Offset = delta;
                    }
                }
                return VAR.gsys_set.bquit ? EM_RES.QUIT : EM_RES.OK;
            }
            finally
            {
                EM_RES disableDown = arm.ax_y.DisableHcmp(arm.dwcam.TriIO.num);
                EM_RES disableUp = arm.ax_y.DisableHcmp(arm.upcam.TriIO.num);
                if (disableDown != EM_RES.OK || disableUp != EM_RES.OK)
                    throw new InvalidOperationException("飞拍触发关闭失败，禁止保存 / Failed to disable fly triggers; calibration not saved");
            }
        }
    }
}

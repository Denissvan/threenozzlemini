using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MotionCtrl;
using System.Threading;

namespace UI.Compment
{
    public partial class RotAndFly : UserControl
    {
        public List<XT> list_xt = new List<XT>();
        public XT FlyXt = COM.xt1;

        XT mcurXt = null;
        XT mcurUdloadXt = null;
        public List<UpDownLoad> list_UDLoad = new List<UpDownLoad>();
        public UpDownLoad mcurUDLoad = new UpDownLoad();
        Cam dwcam = null;
        /// <summary>
        /// 获取当前吸头
        /// </summary>
        public XT curXt
        {
            get
            {
                if (cb_xt.SelectedIndex >= 0 && cb_xt.SelectedIndex < list_xt.Count)
                {
                    mcurXt = list_xt.ElementAt(cb_xt.SelectedIndex);
                    dwcam = mcurXt.dwcam;

                }
                return mcurXt;
            }
        }

        /// <summary>
        /// 获取当前组装模块
        /// </summary>
        public UpDownLoad curUDload
        {
            get
            {
                if (cb_UDload.SelectedIndex >= 0 && cb_UDload.SelectedIndex < list_UDLoad.Count)
                {
                    mcurUDLoad = list_UDLoad.ElementAt(cb_UDload.SelectedIndex);
                    mcurUdloadXt = mcurUDLoad.list_xt[0];
                }
                return mcurUDLoad;
            }
        }


        public RotAndFly()
        {
            InitializeComponent();
            InitializeCalibrationResults();
        }


        public void PmsEn(User.PERMISSION pms)
        {
            if (pms <= User.PERMISSION.Engineer)
            {
                this.Enabled = false;
            }
            else
            {
                this.Enabled = true;
            }
        }

        private void cb_xt_SelectedIndexChanged(object sender, EventArgs e)
        {
            mcurXt = curXt;
            if (mcurXt == null) return;
            txb_rot_x.Text = mcurXt.st_rol.x.ToString("f3");
            txb_rot_y.Text = mcurXt.st_rol.y.ToString("f3");
            txb_rot_rmse.Text = mcurXt.st_rol.z.ToString("f3");
            nud_rot_downcam_y.Value = (decimal)mcurXt.rol_cap_befrcali;
           
           
        }

        private void cb_UDload_SelectedIndexChanged(object sender, EventArgs e)
        {
            mcurUDLoad = curUDload;
            RefreshFlyResults(mcurUDLoad);
        }

        public void UpdateComBox()
        {
            string cur_sel = cb_xt.Text;
            //吸头更新
            cb_xt.Items.Clear();
            foreach (XT xt in list_xt) cb_xt.Items.Add(NozzleCaption(xt));
            if (cb_xt.Items.Count > 0)
            {
                int idx = cb_xt.Items.IndexOf(cur_sel);
                cb_xt.SelectedIndex = idx < 0 ? 0 : idx;
            }
            mcurXt = curXt;
           
            //模组更新
             cur_sel = cb_UDload.Text;
            cb_UDload.Items.Clear();

            foreach (UpDownLoad ud in list_UDLoad)
            {
                cb_UDload.Items.Add(VAR.IsChinese?ud.disc:ud.englishdisc);
            }

            if (cb_UDload.Items.Count > 0)
            {
                int idx = cb_UDload.Items.IndexOf(cur_sel);
                cb_UDload.SelectedIndex = idx < 0 ? 0 : idx;
            }
            mcurUDLoad = curUDload;


        }

        public void AddXt(XT xt)
        {
            if (xt != null)
            {
                foreach(XT Xt in list_xt)
                {
                    if (Xt.id == xt.id) return;
                }

                list_xt.Add(xt);
            }
        }

        public void AddXt(List<XT> listXt)
        {
            if (listXt != null)
            {
                foreach(XT xt in listXt)
                {
                    AddXt(xt);
                }
            }
        }

        public void AddUDLoad(UpDownLoad ud)
        {
            bool IsDif = false;
            if (ud != null)
            {
                foreach (UpDownLoad _ud in list_UDLoad)
                {
                    if (ud.id == _ud.id)
                        IsDif = true;
                }
                if (!IsDif)
                    list_UDLoad.Add(ud);
            }
        }

        public void AddUDLoad(List<UpDownLoad> list_UDLoad)
        {
            if (list_UDLoad != null)
            {
                foreach (UpDownLoad _ud in list_UDLoad)
                {
                    AddUDLoad(_ud);
                    AddXt(_ud.list_xt);
                }
                UpdateComBox();
            }
        }


        public EM_RES CalcRotCenterAction()
        {
            if (!NozzleZCalibrationMotion.IsOwnerThread) return EM_RES.ERR;
            XT selected = mcurXt;
            Cam camera = selected == null ? null : selected.dwcam;
            double captureY = (double)(decimal)Invoke(new Func<decimal>(() => nud_rot_downcam_y.Value));
            EM_RES ret;
            ST_XYZA RotCenter = new ST_XYZA();
            Cam.VisionTask task = null;
            int id = selected == null ? -1 : selected.SlotIndex;
         
            if (selected == null)
            {
                MessageBox.Show(VAR.IsChinese?"未找到可用的吸头!": "No available tips!\r\n未找到可用的吸头!");
                return EM_RES.ERR;
            }

            task = camera.List_vs_task.Find(s => s.TaskName.Equals((id + 1).ToString() + "_XtSharp"));
            if (task == null)
                return CalibrationError(NozzleCaption(selected) + " 缺少独立旋转模板 / Missing rotation template: " + (id + 1) + "_XtSharp");
            MOVE_HANDLE rotate = delegate(ref bool quit, bool x, bool y, bool z, bool angle, bool legacyU1,
                ref ST_XYZA point, string camName, int delay)
            {
                if (x || y || z || !angle) return EM_RES.PARA_ERR;
                EM_RES motion = MT.ZupMove(ref quit, ref selected.ax_u, point.a, true);
                if (motion != EM_RES.OK) return motion;
                if (!CalibrationDelay(delay)) return EM_RES.QUIT;
                return quit ? EM_RES.QUIT : EM_RES.OK;
            };

            ret = (EM_RES)Invoke(new Func<EM_RES>(() => camera.StopLive()));
            if (ret != EM_RES.OK) return ret;
            MT.SetAllAxToManualSpd();
            //if (Math.Abs(UploadModle.ax_z.fenc_pos) > 0.5)
            //{
            //    ret = UploadModle.ZHome(ref VAR.gsys_set.bquit);
            //    if (ret != EM_RES.OK) return EM_RES.ERR;
            //}
            //ret = MT.Move(ref VAR.gsys_set.bquit, ref selected.ax_z, 0);
            //if (ret != EM_RES.OK) return ret;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format("[旋转标定]{0} 第一圈：X={1:F3}（不移动） 拍照Y={2:F3} 模板={3}",
                NozzleCaption(selected), selected.ax_x.fenc_pos, captureY, task.TaskName));
            ret = MT.ZupMove(ref VAR.gsys_set.bquit, ref selected.ax_y, captureY, ref selected.ax_u, 0,true);
            if (ret != EM_RES.OK)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format("[旋转标定]{0} ✗ 移到拍照位失败（返回{1}）", NozzleCaption(selected), ret));
                return EM_RES.ERR;
            }
            camera.List_vs_task_cur.Clear();
            camera.inputImageCnt = 0;
            camera.List_vs_task_cur.Add(task);
            ret = camera.CalRotCenter(ref VAR.gsys_set.bquit, ref RotCenter, task, id == 0 ? true : false, FrMain.frrun.cogDisplayer_run.cogRecordDisplay, calibrationMove: rotate);
            if (ret != EM_RES.OK) return EM_RES.ERR;
            //again
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format("[旋转标定]{0} 第一圈旋转中心 x={1:F3} y={2:F3} RMSE={3:F3}；第二圈 Y 移到 {4:F3}（{5:+0.000;-0.000}）",
                NozzleCaption(selected), RotCenter.x, RotCenter.y, RotCenter.a, captureY + RotCenter.y, RotCenter.y));
            ret = MT.ZupMove(ref VAR.gsys_set.bquit, ref selected.ax_y, captureY + RotCenter.y, ref selected.ax_u, 0, true);
            if (ret != EM_RES.OK)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format("[旋转标定]{0} ✗ 第二圈移到拍照位失败（返回{1}）", NozzleCaption(selected), ret));
                return EM_RES.ERR;
            }

            ret = camera.CalRotCenter(ref VAR.gsys_set.bquit, ref RotCenter, task, id == 0  ? true : false, FrMain.frrun.cogDisplayer_run.cogRecordDisplay, calibrationMove: rotate);
            if (ret != EM_RES.OK) return EM_RES.ERR;

            if (VAR.gsys_set.bquit || !Finite(RotCenter.x) || !Finite(RotCenter.y) || !Finite(RotCenter.a)) return EM_RES.ERR;
            selected.st_rol.x = RotCenter.x;
            selected.st_rol.y = RotCenter.y;
            selected.st_rol.z = RotCenter.a;
            selected.st_rol_cap.x = selected.ax_x.fenc_pos;
            selected.st_rol_cap.y = selected.ax_y.fenc_pos;
            selected.st_cap_pos.y = selected.st_rol_cap.y + selected.cap_offset;
            selected.rol_cap_befrcali = selected.st_rol_cap.y;
            selected.SaveHwCfg();

            //show data
            Invoke(new Action(() =>
            {
            txb_rot_x.Text = selected.st_rol.x.ToString("f3");
            txb_rot_y.Text = selected.st_rol.y.ToString("f3");
            txb_rot_rmse.Text = selected.st_rol.z.ToString("f3");
            nud_rot_downcam_y.Value = (decimal)selected.rol_cap_befrcali;
            }));
            return EM_RES.OK;
        }
        private async void btn_rot_action_Click(object sender, EventArgs e)
        {
            EM_RES ret;
            ret = await RunCalibration(CalcRotCenterAction);
            if (ret == EM_RES.OK)
            {
                MessageBox.Show(VAR.IsChinese ? mcurXt.disc + "旋转中心校正成功!" : mcurXt.disc + "Rotation center correction is successful!\r\n旋转中心校正成功!", VAR.IsChinese ? "警告" : "Prompt", MessageBoxButtons.OK, MessageBoxIcon.Question);
            }
            else
            {
                MessageBox.Show(VAR.IsChinese?(mcurXt == null ? "" : mcurXt.disc) + "旋转中心校正失败!": (mcurXt == null ? "" : mcurXt.disc) + "Rotation center correction failed!\r\n旋转中心校正失败!", VAR.IsChinese ? "警告" : "Prompt", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            if (dwcam != null) dwcam.graphics_other.Clear();
        }

        public EM_RES XtFlyAction(ref UpDownLoad udl)
        {
            if (!NozzleZCalibrationMotion.IsOwnerThread) return EM_RES.ERR;
            return CalibrateFly(udl);
        }

        private async void btn_fly_action_Click(object sender, EventArgs e)
        {
            EM_RES ret;
            DialogResult result = MessageBox.Show(VAR.IsChinese ? "请在本臂每只吸嘴上吸持标定件，并准备当前模式的前/后排治具。确认开始静拍和两次飞拍？" : "Hold a calibration part on every nozzle and prepare fixtures for this mode. Start static capture and two fly scans?", VAR.IsChinese?"提示": "Prompt", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (result == DialogResult.No) return;
            //Task tsk = new Task(() =>
            //{
            //    for (int i = 0; i < 10; i++)
            //    {
            UpDownLoad selectedArm = mcurUDLoad;
            ret = await RunCalibration(() => CalibrateFly(selectedArm));

            //    }
            //});
            //tsk.Start();
            if (ret == EM_RES.OK)
            {
                MessageBox.Show(VAR.IsChinese?"飞拍偏差已测量并保存，请核对复测残差；生产仍保持原定拍流程。": "Fly offsets saved. Review residuals; production remains on its existing static-capture path.", VAR.IsChinese ? "提示" : "Prompt", MessageBoxButtons.OK, MessageBoxIcon.Question);
            }
            else
            {
                MessageBox.Show(VAR.IsChinese ? "吸头飞拍校正失败!": "Tip flying correction failed!\r\n吸头飞拍校正失败!", VAR.IsChinese ? "提示" : "Prompt", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            ////graphics_other.Clear();
           
        }

        private async void btn_rot_downcam_movpos_Click(object sender, EventArgs e)
        {
            if (mcurXt == null) return;
            XT selected = mcurXt;
            double captureY = (double)nud_rot_downcam_y.Value;
            await RunCalibration(() =>
            {
                MT.SetAllAxToManualSpd();
                return MT.ZupMove(ref VAR.gsys_set.bquit, ref selected.ax_y,
                    captureY, ref selected.ax_u, 0, true);
            });
        }

        private void btn_zk_ctrl_Click(object sender, EventArgs e)
        {
            if (mcurXt == null || NozzleZCalibrationMotion.IsBusy) return;
            if (mcurXt.cy_zk.isOFF)
                mcurXt.cy_zk.SetOn();
            else
                mcurXt.cy_zk.SetOff();
        }

        private void btn_cam_Click(object sender, EventArgs e)
        {
            //EM_RES res;
            //VAR.gsys_set.bquit = false;
            //for (int i = 0; i < 50 && !VAR.gsys_set.bquit; i++)
            //{
            //    res = UploadModle.test_FLyCapToTray(ref VAR.gsys_set.bquit);
            //    if (res != EM_RES.OK) return;
            //    Application.DoEvents();
            //}
                
           
          
        }

        private void btn_rot_downcam_getpos_Click(object sender, EventArgs e)
        {
            if (mcurXt == null || NozzleZCalibrationMotion.IsBusy) return;
            nud_rot_downcam_y.Value = (decimal)mcurXt.ax_y.fenc_pos;
        }
    }
}

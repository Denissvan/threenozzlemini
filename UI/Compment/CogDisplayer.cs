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
using System.IO;

namespace UI
{    
    public partial class CogDisplayer : UserControl
    {
        public List<Cam> list_cam = new List<Cam>();
        public string curImgFile = "";
        Cam mcurCam = null;
        public bool TestOrGo = false;
        /// <summary>
        /// 获取当前相机
        /// </summary>
        public Cam curCam
        {
            get
            {
                if (cb_cam.SelectedIndex >= 0 && cb_cam.SelectedIndex < list_cam.Count)
                {
                    mcurCam = list_cam.ElementAt(cb_cam.SelectedIndex);

                }
                return mcurCam;
            }
        }

        Cam.VisionTask mcurTask;
        /// <summary>
        /// 获取当前视觉任务
        /// </summary>
        public Cam.VisionTask curTask
        {
            get
            {
                mcurCam = curCam;
                if (mcurCam == null) return mcurTask = null;
                if (cb_flow.SelectedIndex >= 0 && cb_flow.SelectedIndex < mcurCam.List_vs_task.Count)
                {
                    return mcurTask = mcurCam.List_vs_task.ElementAt(cb_flow.SelectedIndex);
                }
                return mcurTask =null;
            }
        }
        
        public CogDisplayer()
        {
            InitializeComponent();
            statusbar.Display = cogRecordDisplay;
            cogRecordDisplay.PopupMenu = false;
        }

        public void UpdateComBox()
        {
            string cur_sel = cb_cam.Text;
            cb_cam.Items.Clear();
            foreach (Cam cam in list_cam) cb_cam.Items.Add(VAR.IsChinese?cam.disc:cam.englishdisc);
            if (cb_cam.Items.Count > 0)
            {
                int idx = cb_cam.Items.IndexOf(cur_sel);
                if (idx < 0) idx = 0;
                cb_cam.SelectedIndex = idx;
            }
            mcurCam = curCam;
            cb_cam_SelectedIndexChanged(null, null);
        }

        
        public void AddCam(Cam camera)
        {
            if (camera != null)
            {
                foreach (Cam cam in list_cam)
                {
                    if (cam.mName == camera.mName) return;
                }

                list_cam.Add(camera);
            }

            UpdateComBox();
        }

        /// <summary>在相机下拉框里选中指定相机（不在列表里则先加入），供其他页面联动触发/实况。</summary>
        public void SelectCam(Cam camera)
        {
            if (camera == null) return;
            int idx = list_cam.IndexOf(camera);
            if (idx < 0)
            {
                AddCam(camera);
                idx = list_cam.IndexOf(camera);
            }
            if (idx >= 0 && idx < cb_cam.Items.Count && cb_cam.SelectedIndex != idx) cb_cam.SelectedIndex = idx;
        }

        /// <summary>移除相机（删除手动添加的相机时用）。</summary>
        public void RemoveCam(Cam camera)
        {
            if (camera == null || !list_cam.Remove(camera)) return;
            UpdateComBox();
        }

        public void AddCam(List<Cam> listCam)
        {
            if (listCam != null)
            {
                foreach (Cam cam in listCam)
                {
                    AddCam(cam);
                }
            }
        }

        public void PmsEn(bool En=false)
        {
            btn_show_tool.Enabled = En;
        }

        private void cb_cam_SelectedIndexChanged(object sender, EventArgs e)
        {
            mcurCam = curCam;
            if (mcurCam == null) return;

            cb_flow.Items.Clear();
            foreach (Cam.VisionTask task in mcurCam.List_vs_task)
            {
                cb_flow.Items.Add(task.TaskName);
            }
            if (cb_flow.Items.Count > 0)
                cb_flow.SelectedIndex = 0;

            //update capcfg（拍照参数按当前选中的流程显示）
            //if (ts_param.Visible)
            {
                RefreshCapBoxes();
                tsb_continue.BackColor = curCam.bcontinue ? Color.Gold : SystemColors.ButtonFace;
                TestOrGo = false;
                tsb_TestOrGo.Text = VAR.IsChinese?"定位模式": "Positioning";
                tsb_TestOrGo.BackColor = Color.Transparent;

            }
        }

        private void btn_live_Click(object sender, EventArgs e)
        {
            tsb_continue.BackColor = curCam.bcontinue ? Color.Gold : SystemColors.ButtonFace;
            if (mcurCam == null)
            {
                MessageBox.Show(VAR.IsChinese?"相机为空，请先选择相机!": "Camera is empty, please select a camera first!\r\n相机为空，请先选择相机!");
                return;
            }
            mcurCam.LiveTask = curTask;   // 实况用当前选中流程的曝光
            mcurCam.Live(cogRecordDisplay, !curCam.isLive, true);
            if(curCam.isLive)
            {
                btn_live.BackColor = Color.Gold;
                btn_img.BackColor = SystemColors.ButtonFace;
            }
            else
            {
                btn_live.BackColor = SystemColors.ButtonFace;
            }
        }

        private void btn_triger_Click(object sender, EventArgs e)
        {
            tsb_continue.BackColor = curCam.bcontinue ? Color.Gold : SystemColors.ButtonFace;
            if (mcurCam == null)
            {
                MessageBox.Show(VAR.IsChinese?"相机为空，请先选择相机!": "Camera is empty, please select a camera first!\r\n相机为空，请先选择相机!");
                return;
            }            
            if(curTask == null)
            {
                // 没有视觉流程（如新添加的海康相机）：只取一张原图显示
                if (mcurCam.HasAcquisition && btn_img.BackColor != Color.Gold)
                {
                    mcurCam.List_vs_task_cur.Clear();
                    mcurCam.inputImageCnt = 0;
                    double exp;
                    if (double.TryParse(stb_exposure.Text, out exp) && exp > 0 && exp < 1000) mcurCam.curCapCfg.Exposure = exp;
                    mcurCam.Live(cogRecordDisplay, false, true);
                    mcurCam.mCogRecDisplay = cogRecordDisplay;
                    mcurCam.TrigerManual(mcurCam.bcontinue);   // 手动触发：海康相机用软触发
                    btn_live.BackColor = mcurCam.isLive ? Color.Gold : SystemColors.ButtonFace;
                    return;
                }
                MessageBox.Show(VAR.IsChinese?"流程为空，请先选择流程!": "The process is empty, please select the process first!\r\n流程为空，请先选择流程!");
                return;
            }
            curTask.ResData.Clear();
            mcurCam.List_vs_task_cur.Clear();
            mcurCam.inputImageCnt = 0;
            //curTask.ListCaliTool = new List<Cam.CaliTool>();
            mcurCam.List_vs_task_cur.Add(curTask);
            if (mcurCam.HasAcquisition && btn_img.BackColor != Color.Gold)
            {
                //get cap config：写到当前流程的拍照参数（只在本次运行生效，点“保存参数”才写入任务配置）
                Cam.ST_CAP_CFG taskCfg = curTask.CapCfg;
                double val = Convert.ToDouble(stb_exposure.Text);
                if (curCam != null && val > 0 && val < 1000)
                {
                    taskCfg.Exposure = val;
                }
                else MessageBox.Show(VAR.IsChinese ? "曝光参数数据异常!\r\n请检查数据格式及大小范围!": "The exposure parameter data is abnormal!\r\nPlease check the data format and size range!\r\n曝光参数数据异常!\r\n请检查数据格式及大小范围!");

                val = Convert.ToDouble(stb_brightness.Text);
                if (curCam != null && val >= 0 && val < 1)
                {
                    taskCfg.Brightness = val;
                }
                else MessageBox.Show(VAR.IsChinese ? "亮度参数数据异常!\r\n请检查数据格式及大小范围!": "The brightness parameter data is abnormal!\r\nPlease check the data format and size range!");

                val = Convert.ToDouble(stb_contrast.Text);
                if (curCam != null && val >= 0 && val < 1)
                {
                    taskCfg.Contrast = val;
                }
                else MessageBox.Show(VAR.IsChinese ? "对比参数数据异常!\r\n请检查数据格式及大小范围!": "The comparison parameter data is abnormal!\r\nPlease check the data format and size range!");
                taskCfg.HikBcValid = true;   // 操作员在界面上给定的亮度/对比度，海康也按此下发
                curTask.CapCfg = taskCfg;

                if (mcurCam.mAcqFifo != null) mcurCam.mAcqFifo.Prepare();
                mcurCam.Live(cogRecordDisplay, false, true);
                mcurCam.TrigerManual(mcurCam.bcontinue);   // 手动触发：海康相机用软触发

                btn_img.BackColor = SystemColors.ButtonFace;
                if (curCam.isLive)
                {
                    btn_live.BackColor = Color.Gold;
                }
                else
                {
                    btn_live.BackColor = SystemColors.ButtonFace;
                }
            }
            else
            {
                try
                {
                    if (curImgFile.Length < 3 || !Directory.Exists(Path.GetDirectoryName(curImgFile))) return;
                    string[] files = System.IO.Directory.GetFiles(System.IO.Path.GetDirectoryName(curImgFile), "*.bmp");
                    for (int n = 0; n < files.Count(); n++)
                    {
                        if (files[n] == curImgFile)
                        {
                            n++;
                            if (n >= files.Count()) n = 0;
                            curImgFile = files[n];
                            mcurCam.TrigerByLoadImg(curImgFile);
                            break;
                        }
                    }
                }
                catch(Exception ex)
                {
                    MessageBox.Show(VAR.IsChinese?string.Format("{0}触发{1}异常，{2}", mcurCam.disc, mcurTask.TaskName, ex.Message)
                        : string.Format("{0} trigger {2} err, {3}\r\n({1}触发{2}异常，{3})",mcurCam.englishdisc, mcurCam.disc, mcurTask.TaskName, ex.Message));
                }
            }
            //wait for done
            int timeout = 0;
            while(timeout++<20)
            {
                System.Threading.Thread.Sleep(50);
                Application.DoEvents();
                if(curTask.ResData.bUpdate)
                {
                    //string str = string.Format("{0}/{1}:{2},X:{3:F3},Y{4:F3},A{5:F3},T{6:F3}", curCam.disc,curTask.TaskName, curTask.ResData.bOK?"OK":"NG", 
                    //    curTask.ResData.PosMM.x, curTask.ResData.PosMM.y, curTask.ResData.PosMM.a, curTask.ResData.CTms);
                    //VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, str);
                    break;
                }
            }
            if(timeout >=20) VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, VAR.IsChinese?string.Format("{0}/{1} 超时！", curCam.disc, curTask.TaskName): string.Format("{0}/{2} timeout!            ({1}/{2} 超时！)",curCam.englishdisc, curCam.disc, curTask.TaskName));
        }

        private void btn_img_Click(object sender, EventArgs e)
        {
            tsb_continue.BackColor = curCam.bcontinue ? Color.Gold : SystemColors.ButtonFace;
            if (curCam == null || curTask == null) return;            
            if (((Button)sender).BackColor == SystemColors.ButtonFace)
            {               
                if (DialogResult.OK != openFileDlg.ShowDialog()) return;
                curImgFile = openFileDlg.FileName;
                ((Button)sender).BackColor = Color.Gold;
                mcurCam.TrigerByLoadImg(curImgFile);
            }
            else
            {
                ((Button)sender).BackColor = SystemColors.ButtonFace;
            }

            if (curCam.isLive)
            {
                btn_live.BackColor = Color.Gold;
            }
            else
            {
                btn_live.BackColor = SystemColors.ButtonFace;
            }
        }

        private void btn_show_tool_Click(object sender, EventArgs e)
        {
            ts_param.Visible = !ts_param.Visible;
            if (curCam != null && ts_param.Visible)
            {
                RefreshCapBoxes();
                tsb_continue.BackColor = curCam.bcontinue ? Color.Gold : SystemColors.ButtonFace;
            }
        }

        private void cogRecordDisplay_Click(object sender, EventArgs e)
        {
            ts_param.Visible = false;
           
        }

        private void tsb_block_edit_Click(object sender, EventArgs e)
        {
            if(curCam == null || curTask == null)
            {
                MessageBox.Show(VAR.IsChinese?"找不到相机或流程!\r\n请先选择相机，并选好要编辑的流程!": "Can't find the camera or process! \r\nPlease select the camera first and choose the process you want to edit!");
                return;
            }
            curTask.Load();
            using (Form f = new Form())
            { 
                CogBlockEidt fr = new CogBlockEidt();
                fr.Camera = curCam;
                fr.VsTask = curTask;
                fr.Dock = DockStyle.Fill;
                f.Controls.Add(fr);
                f.Size = new Size(800,600);
                f.Text = string.Format("{0}任务{1} {2}",curTask.Camera==null?"": curTask.Camera.disc,curTask.TaskName,curTask.path);
                f.ShowDialog();
            }
        }

        private void tsb_save_Click(object sender, EventArgs e)
        {
            if (curCam == null) return;
            if (curTask != null)
            {
                SaveTaskCapFromBoxes();
                return;
            }
            try
            {
                // 没有流程（只取原图）时才保存相机参数
                Cam.ST_CAP_CFG cap_cfg = curCam.curCapCfg;
                double val = Convert.ToDouble(stb_exposure.Text);
                if (curCam != null && val > 0 && val < 1000)
                {
                    cap_cfg.Exposure = val;
                }
                else MessageBox.Show(VAR.IsChinese?"曝光参数数据异常!\r\n请检查数据格式及大小范围!": "The exposure parameter data is abnormal! \r\nPlease check the data format and size range!");
                val = Convert.ToDouble(stb_brightness.Text);
                if (curCam != null && val >= 0 && val < 1)
                {
                    cap_cfg.Brightness = val;
                }
                else MessageBox.Show(VAR.IsChinese?"亮度参数数据异常!\r\n请检查数据格式及大小范围!": "The brightness parameter data is abnormal! \r\nPlease check the data format and size range!");

                val = Convert.ToDouble(stb_contrast.Text);
                if (curCam != null && val >= 0 && val < 1)
                {
                    cap_cfg.Contrast = val;
                }
                else MessageBox.Show(VAR.IsChinese?"对比参数数据异常!\r\n请检查数据格式及大小范围!": "The comparison parameter data is abnormal! \r\nPlease check the data format and size range!");
                //save
                if (curCam.isLive) curCam.liveCapCfg = cap_cfg;
                else curCam.curCapCfg = cap_cfg;
                curCam.SaveCfg();
                MessageBox.Show(VAR.IsChinese ? "参数保存完成！": "Parameters are saved!\r\n参数保存完成！");
            }
            catch(Exception ex)
            {
                MessageBox.Show(VAR.IsChinese?"参数数据异常!\r\n请检查数据格式及大小范围!": "Parameter data is abnormal!\r\nPlease check the data format and size range!\r\n参数数据异常!\r\n请检查数据格式及大小范围!", ex.Message);
            }
        }

        Cognex.VisionPro.CogRectangle rct = new Cognex.VisionPro.CogRectangle();
        Cognex.VisionPro.CogGraphicLabel lable = new Cognex.VisionPro.CogGraphicLabel();
        Cognex.VisionPro.ICogTransform2D mCalToPixel;
        bool bmouseup = true;
        private void cogRecordDisplay_MouseMove(object sender, MouseEventArgs e)
        {
            if (TestOrGo) return;
            if (mCalToPixel != null && bmouseup == false && e.X - rct.X > 0 && e.Y - rct.Y > 0)
            {
                rct.SetXYWidthHeight(rct.X, rct.Y, e.X - rct.X, e.Y - rct.Y);

                double x1 = 0;
                double y1 = 0;
                mCalToPixel.MapPoint(rct.X, rct.Y, out x1, out y1);
                double x2 = 0;
                double y2 = 0;
                mCalToPixel.MapPoint(rct.X + rct.Width, rct.Y + rct.Height, out x2, out y2);
                double a = Cognex.VisionPro.CogMisc.RadToDeg(Cognex.VisionPro.CogMath.AnglePointPoint(rct.X, rct.Y, rct.X + rct.Width, rct.Y + rct.Height));
                lable.Text = string.Format("W{0:F3} H{1:F3} R{2:F3}", x2 - x1, y2 - y1, a);

                try
                {
                    cogRecordDisplay.StaticGraphics.Remove("Measure");
                }
                catch
                { }
                cogRecordDisplay.StaticGraphics.Add(rct, "Measure");
                cogRecordDisplay.StaticGraphics.Add(lable, "Measure");
            }
        }

        private void cogRecordDisplay_MouseDown(object sender, MouseEventArgs e)
        {
           // double x = 0, y = 0;
            EM_RES res=EM_RES.OK;
            ST_XYZA pos=new ST_XYZA();
            if (!TestOrGo)
                //init rectangle
            {
                rct.X = e.X;
                rct.Y = e.Y;
                rct.SelectedSpaceName = "*";
                //rct.Color = Cognex.VisionPro.CogColorConstants.Orange;
                //init lable
                lable.Alignment = Cognex.VisionPro.CogGraphicLabelAlignmentConstants.BaselineLeft;
                lable.SelectedSpaceName = "*";
                lable.Font = new Font("Arial", 12);
                lable.LineWidthInScreenPixels = 3;
                lable.X = e.X;
                lable.Y = e.Y - lable.Font.SizeInPoints;
                //lable.Color = Cognex.VisionPro.CogColorConstants.Orange;
                try
                {
                    cogRecordDisplay.StaticGraphics.Remove("Measure");
                }
                catch
                {
                }
            }

            //get transform
            try
            {
                string spaceName = "#";
                                                                                                                                                                                                                                                                                     if (cogRecordDisplay.Image.SelectedSpaceName.Contains("mm")) spaceName = "mm";
                    mCalToPixel = cogRecordDisplay.GetTransform(spaceName, "*");
                if (TestOrGo)
                {
                    VAR.gsys_set.bquit = false;
                    AXIS ax_x = null;
                    AXIS ax_y = null;
                    if (mcurCam.mName == "CamUp1")
                    {
                        ax_x = COM.UDLoad1.ax_x;
                        ax_y = COM.UDLoad1.ax_y;
                    }
                    else if (mcurCam.mName == "CamUp2")
                    {
                        ax_x = COM.UDLoad2.ax_x;
                        ax_y = COM.UDLoad2.ax_y;
                    }
                     
                    mCalToPixel.MapPoint(e.X, e.Y, out pos.x, out pos.y);
                    pos.x = ax_x.fenc_pos-pos.x;
                    pos.y = ax_y.fenc_pos-pos.y;
                    //定位                  
                    res =curCam.MoveHandle(ref VAR.gsys_set.bquit, true, true, false, false, false, ref pos, curCam.mName, 0);
                    if (res != EM_RES.OK) return;
                    //拍照
                    btn_triger_Click(null, null);
                }
                    
                bmouseup = false;
            }
            catch
            {
                mCalToPixel = null;
            }            
        }

        private void cogRecordDisplay_MouseUp(object sender, MouseEventArgs e)
        {
            bmouseup = true;
        }

        private void cogRecordDisplay_DoubleClick(object sender, EventArgs e)
        {
            cogRecordDisplay.Fit();
        }

        private void cb_flow_SelectedIndexChanged(object sender, EventArgs e)
        {
            Cam cam = curCam;
            if (cam == null) return;
            RefreshCapBoxes();
            cam.LiveTask = curTask;
            // 实况中切换流程：重新开实况，立刻换成新流程的曝光
            if (cam.isLive)
            {
                cam.Live(cogRecordDisplay, false, false);
                cam.Live(cogRecordDisplay, true, false);
            }
        }

        /// <summary>参数栏显示当前流程的拍照参数；没有流程时显示相机参数。</summary>
        private void RefreshCapBoxes()
        {
            Cam cam = curCam;
            if (cam == null) return;
            Cam.VisionTask task = curTask;
            Cam.ST_CAP_CFG cfg;
            if (task != null) cfg = cam.GetTaskCapCfg(task);
            else
            {
                cfg = cam.curCapCfg;
                if (cam.isLive) cfg.Exposure = cam.liveCapCfg.Exposure;
            }
            stb_exposure.Text = cfg.Exposure.ToString("F3");
            stb_brightness.Text = cfg.Brightness.ToString("F3");
            stb_contrast.Text = cfg.Contrast.ToString("F3");
            stb_exposure.ToolTipText = task != null ? "当前流程 " + task.TaskName + " 的曝光(ms)" : "相机曝光(ms)（未选流程）";
            stb_brightness.ToolTipText = cam.IsHik ? "海康：0~1 按相机范围换算成黑电平 BlackLevel" : "亮度 0~1";
            stb_contrast.ToolTipText = cam.IsHik ? "海康：0~1 按相机范围换算成增益 Gain（会关闭自动增益）" : "对比度 0~1";
        }

        private void SaveTaskCapFromBoxes()
        {
            Cam cam = curCam;
            Cam.VisionTask task = curTask;
            if (cam == null || task == null) return;
            double exp, brt, con;
            if (!double.TryParse(stb_exposure.Text, out exp) || exp <= 0 || exp >= 1000)
            {
                MessageBox.Show(VAR.IsChinese ? "曝光参数数据异常!\r\n范围 0~1000 ms" : "Exposure out of range (0~1000 ms)");
                return;
            }
            if (!double.TryParse(stb_brightness.Text, out brt) || brt < 0 || brt >= 1) brt = task.CapCfg.Brightness;
            if (!double.TryParse(stb_contrast.Text, out con) || con < 0 || con >= 1) con = task.CapCfg.Contrast;
            Cam.ST_CAP_CFG cfg = task.CapCfg;
            cfg.Exposure = exp;
            cfg.Brightness = brt;
            cfg.Contrast = con;
            cfg.HikBcValid = true;
            task.CapCfg = cfg;
            EM_RES res = cam.SaveTaskCapCfg(task);
            VAR.msg.AddMsg(res == EM_RES.OK ? Msg.EM_MSGTYPE.NOR : Msg.EM_MSGTYPE.ERR, string.Format(
                "[任务曝光] {0}/{1} 保存拍照参数：曝光={2:F3}ms 亮度={3:F3} 对比度={4:F3}，结果={5}", cam.disc, task.TaskName, exp, brt, con, res));
            if (cam.isLive)
            {
                cam.Live(cogRecordDisplay, false, false);
                cam.Live(cogRecordDisplay, true, false);
            }
            MessageBox.Show(res == EM_RES.OK
                ? string.Format("流程【{0}】的拍照参数已保存！\r\n曝光={1:F3}ms\r\n（拍照参数只跟流程关联，其它流程不受影响）", task.TaskName, exp)
                : "保存失败，详见日志");
        }

        private void tsb_save_rawpic_Click(object sender, EventArgs e)
        {
            //if (cogRecordDisplay.Image == null) return;

            //saveFileDialog.Filter = "Jpeg|*.jpeg|Bmp|*.bmp";
            //if (DialogResult.OK != saveFileDialog.ShowDialog()) return;
            //mcurCam.SaveOriginImage(cogRecordDisplay.Image, Path.GetDirectoryName(saveFileDialog.FileName), Path.GetFileName(saveFileDialog.FileName));
            //mcurCam.ComShapeSave(curTask.ResData.ShapeOutput, "comshape.shp");
            //mcurCam.ComShapeLoad("comshape.shp");
            //mcurCam.ComShapeMove(0, 0, 0);
            
        }

        private void tsb_save_pic_Click(object sender, EventArgs e)
        {
            if (cogRecordDisplay.Image == null) return;
            DialogResult dlgres = MessageBox.Show(VAR.IsChinese?"是否要保存显示画面?\n\nYES---保存显示画面\nNO---保存原始图片": "Do you want to save the display? \n\nYES --- Save the display \nNO --- Save the original picture\r\n是否要保存显示画面?\n\nYES---保存显示画面\nNO---保存原始图片", VAR.IsChinese?"问询": "Inquire", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (dlgres == DialogResult.Cancel) return;

            saveFileDialog.Filter = "Jpeg|*.jpeg|Bmp|*.bmp";
            if (DialogResult.OK != saveFileDialog.ShowDialog()) return;

            if (dlgres == DialogResult.OK)
                mcurCam.SaveDisplay(cogRecordDisplay, Path.GetDirectoryName(saveFileDialog.FileName), Path.GetFileName(saveFileDialog.FileName));
            else
                mcurCam.SaveOriginImage(cogRecordDisplay.Image, Path.GetDirectoryName(saveFileDialog.FileName), Path.GetFileName(saveFileDialog.FileName));
        }

        private void tsb_continue_Click(object sender, EventArgs e)
        {
            if (curCam != null)
            {
                curCam.bcontinue = !curCam.bcontinue;
                ((ToolStripButton)sender).BackColor = curCam.bcontinue ? Color.Gold : SystemColors.ButtonFace;
            }
        }

        private void stb_exposure_TextChanged(object sender, EventArgs e)
        {
            //try
            //{
            //    if (curCam == null) return;
            //    double val = Convert.ToDouble(stb_exposure.Text);
            //    if(val <= 0 && val >= 1000)
            //    {
            //        MessageBox.Show("曝光值超范围!");
            //        return;
            //    }
            //    if (curCam.isLive)
            //    {
            //        curCam.liveCapCfg.Exposure = val;
            //        curCam.CapCfg(curCam.liveCapCfg);
            //    }
            //    else
            //    {
            //        curCam.curCapCfg.Exposure = val;
            //        curCam.CapCfg(curCam.curCapCfg);
            //    }
            //}
            //catch
            //{

            //}
        }

        private void tsb_TestOrGo_Click(object sender, EventArgs e)
        {
            if(mcurCam.mName== "CamDw1"|| mcurCam.mName == "CamDw2")return;
            if (TestOrGo)
            {
                TestOrGo = false;
                tsb_TestOrGo.Text = VAR.IsChinese?"定位模式": "Positioning";
                tsb_TestOrGo.BackColor = Color.Transparent;
            }
            else
            {
                TestOrGo = true;
                tsb_TestOrGo.Text = VAR.IsChinese ? "测量模式": "Measurement";
                tsb_TestOrGo.BackColor = Color.Orange;
            }

        }

        private void tsb_GoCenter_Click(object sender, EventArgs e)
        {
            if (mcurCam.mName == "CamDw1" || mcurCam.mName == "CamDw2") return;
            EM_RES res = EM_RES.OK;
            ST_XYZA CurPos = new ST_XYZA();
            VAR.gsys_set.bquit = false;
            if (DialogResult.Yes == MessageBox.Show(VAR.IsChinese?"是否画面对中?": "Is the screen aligned?\r\n是否画面对中?", VAR.IsChinese?"提示": "Prompt", MessageBoxButtons.YesNo, MessageBoxIcon.Question))
            {
                AXIS ax_x = null;
                AXIS ax_y = null;
                if (mcurCam.mName == "CamUp1")
                {
                    ax_x = COM.UDLoad1.ax_x;
                    ax_y = COM.UDLoad1.ax_y;
                }
                else if (mcurCam.mName == "CamUp2")
                {
                    ax_x = COM.UDLoad2.ax_x;
                    ax_y = COM.UDLoad2.ax_y;
                }
                CurPos.x = ax_x.fenc_pos;
                CurPos.y = ax_y.fenc_pos;
                curCam.MoveToImgCenter(ref VAR.gsys_set.bquit, ref CurPos, curTask);
            }

        }
    }
}

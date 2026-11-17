using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;

namespace MotionCtrl
{
    public partial class AxisConfig : UserControl
    {
        List<AXIS> list_ax=new List<AXIS> ();

        public AxisConfig()
        {
            InitializeComponent();
            list_ax.Clear();            
        }

        private  void FillTableWithAxisInf(AXIS ax, int row=-2)
        {
            if (ax == null || ax.mt_type == AXIS.MT_TYPE.NULL) return;
            //if empty or add mode then add
            //if (dgv.Rows.Count == 0|| row == -2) row = dgv.Rows.Add();
            if (dgv.Rows.Count == 0 || row < 0 || row >= dgv.Rows.Count) row = dgv.Rows.Add();
            //the last row
            else if (row < 0) row = dgv.Rows.Count - 1;

            dgv.Rows[row].Cells["axis"].Value = ax.disc;
            dgv.Rows[row].Cells["startspd"].Value = ax.spd_start.ToString("F0");
            dgv.Rows[row].Cells["stopspd"].Value = ax.spd_stop.ToString("F0");
            dgv.Rows[row].Cells["homespd"].Value = ax.home_spd.ToString("F0");
            dgv.Rows[row].Cells["creepspd"].Value = ax.creep_spd.ToString("F1");
            dgv.Rows[row].Cells["homemode"].Value = ax.home_mode.ToString();
            dgv.Rows[row].Cells["homedir"].Value = ax.home_dir == AXIS.AX_DIR.P ? "正向(P)" : "负向(N)";
            DataGridViewCell driveCell = dgv.Rows[row].Cells["drivehome"];
            driveCell.Value = ax.SupportsDriveHome && ax.drive_home;
            driveCell.ReadOnly = !ax.SupportsDriveHome;
            driveCell.ToolTipText = ax.SupportsDriveHome ? "" : "该轴不是总线轴，只能用控制器回零";
            dgv.Rows[row].Cells["workspd"].Value = ax.spd_work.ToString("F0");
            //dgv.Rows[row].Cells["maxspd"].Value = ax.max_spd.ToString("F0");
            dgv.Rows[row].Cells["tacc"].Value = ax.tacc.ToString("0.0#####");   // 不再按 3 位小数截断，小于 0.001 的加速时间也能显示/保存
            dgv.Rows[row].Cells["tdec"].Value = ax.tdec.ToString("0.0#####");
            dgv.Rows[row].Cells["sln"].Value = ax.sln.ToString("F0");
            dgv.Rows[row].Cells["slp"].Value = ax.slp.ToString("F0");
            dgv.Rows[row].Cells["puls_per_mm"].Value = ax.pul_per_mm.ToString("F7");
            dgv.Rows[row].Cells["offset"].Value = ax.home_offset.ToString("F3");
            dgv.Rows[row].Cells["pos0"].Value = ax.pos0.ToString("F3");
            dgv.Rows[row].Cells["pos1"].Value = ax.pos1.ToString("F3");
            dgv.Rows[row].Cells["pos2"].Value = ax.pos2.ToString("F3");

        }

        public void AddAxis(AXIS ax)
        {
            if(true)//if (ax.isInit)
            {
                if (list_ax.Contains(ax) == false)
                {
                    list_ax.Add(ax);
                    FillTableWithAxisInf(ax);
                }
            }
            else
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, String.Format("轴设置列表,{0} 未初始化！", ax.disc));
                return;
            }            
        }
        public void AddAxis(List<AXIS> list_ax)
        {
            foreach (AXIS ax in list_ax)
            {
                AddAxis(ax);
            }
        }

        public void ClearAxis()
        {
            list_ax.Clear();
        }

        public void AutoUpdate(int intv_ms=300)
        {
            if (intv_ms > 0)
            {
                tmr_update.Interval = intv_ms;
                tmr_update.Enabled = true;
                tmr_update.Start();                
            }
            else
            {
                tmr_update.Enabled = false;
                tmr_update.Stop ();
            }
        }

        public void UpdateShow()
        {
            if (dgv.Rows.Count != list_ax.Count) dgv.Rows.Clear();
            for (int r = 0; r < list_ax.Count; r++)
            {
                FillTableWithAxisInf(list_ax.ElementAt(r), r);
            }
            dgv.Update();
        }

        private void tmr_update_Tick(object sender, EventArgs e)
        {
            UpdateShow();
        }

        public bool SaveToFile(string filename = "")
        {
            //update data
            string disc = "";
            dgv.EndEdit();
            try
            {
                foreach (DataGridViewRow row in dgv.Rows)
                {
                    foreach (AXIS ax in list_ax)
                    {
                        if (ax == null) continue;
                        if (row.Cells["axis"].Value.ToString() == ax.disc)
                        {
                            disc = ax.disc;
                            ax.spd_start = Convert.ToDouble(row.Cells["startspd"].Value.ToString());
                            ax.spd_stop = Convert.ToDouble(row.Cells["stopspd"].Value.ToString());
                            ax.home_spd = Convert.ToDouble(row.Cells["homespd"].Value.ToString());
                            double creep = Convert.ToDouble(row.Cells["creepspd"].Value.ToString());
                            if (!(creep > 0)) throw new Exception("爬行速度必须大于0");
                            ax.creep_spd = creep;
                            ushort mode;
                            if (!ushort.TryParse(Convert.ToString(row.Cells["homemode"].Value).Trim(), out mode))
                                throw new Exception("回零模式必须是 0-65535 的整数");
                            bool driveHome = ax.SupportsDriveHome && Convert.ToBoolean(row.Cells["drivehome"].Value ?? false);
                            if (ax.SupportsDriveHome && mode == 0)
                                throw new Exception("ZMC432 轴的回零模式不能为 0");
                            ax.home_mode = mode;
                            ax.home_dir = Convert.ToString(row.Cells["homedir"].Value).StartsWith("正") ? AXIS.AX_DIR.P : AXIS.AX_DIR.N;
                            if (ax.SupportsDriveHome) ax.drive_home = driveHome;
                            ax.spd_work = Convert.ToDouble(row.Cells["workspd"].Value.ToString());
                            //ax.max_spd = Convert.ToDouble(row.Cells["maxspd"].Value.ToString());
                            ax.tacc = Convert.ToDouble(row.Cells["tacc"].Value.ToString());
                            ax.tdec = Convert.ToDouble(row.Cells["tdec"].Value.ToString());
                            ax.sln = Convert.ToDouble(row.Cells["sln"].Value.ToString());
                            ax.slp = Convert.ToDouble(row.Cells["slp"].Value.ToString());
                            ax.pul_per_mm = Convert.ToDouble(row.Cells["puls_per_mm"].Value.ToString());
                            ax.home_offset = Convert.ToDouble(row.Cells["offset"].Value.ToString());

                            ax.pos0 = Convert.ToDouble(row.Cells["pos0"].Value.ToString());
                            ax.pos1 = Convert.ToDouble(row.Cells["pos1"].Value.ToString());
                            ax.pos2 = Convert.ToDouble(row.Cells["pos2"].Value.ToString());
                            break;
                        }
                    }
                }
            }
            catch(Exception ex)
            {
                MessageBox.Show(VAR.IsChinese?disc + " 参数输入异常\r\n"+ex: disc + " Parameter input is abnormal \r\n        (参数输入异常\r\n)" + ex);
                return false;
            }
            //save
            foreach (AXIS ax in list_ax)
            {
                if (ax == null) continue;
                ax.SaveCfgToInf(filename);
                ax.LoadCfgFrInf(filename);
            }
            UpdateShow();
            return true;
        }

        public void LoadFrFile(string filename = "")
        {
            //save
            foreach (AXIS ax in list_ax)
            {
                if (ax == null) continue;
                ax.LoadCfgFrInf(filename);
            }
            UpdateShow();
        }

        private void dgv_CellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if ((e.RowIndex & 1) == 1) e.CellStyle.BackColor = Color.WhiteSmoke;
        }

        //axis startspd stopspd homespd workspd tacc tdec sln slp puls_per_mm offset pos0 pos1 pos2
        public void ChangeColumn()
        {
            if (VAR.IsChinese)
            {
                axis.HeaderText = "    轴";
                startspd.HeaderText = "启动速度";
                stopspd.HeaderText = "停止速度";
                homespd.HeaderText = "回零速度";
                creepspd.HeaderText = "爬行速度";
                homemode.HeaderText = "回零模式";
                homedir.HeaderText = "回零方向";
                drivehome.HeaderText = "驱动器回零";
                workspd.HeaderText = "工作速度";
                tacc.HeaderText = "加速时间";
                tdec.HeaderText = "减速时间";
                sln.HeaderText = "负限位";
                slp.HeaderText = "正限位";
                puls_per_mm.HeaderText = "脉冲/mm";
                offset.HeaderText = "偏移";
                pos0.HeaderText = "位置0";
                pos1.HeaderText = "位置1";
                pos2.HeaderText = "位置2";
            }
            else
            {
                axis.HeaderText = "Axis";
                startspd.HeaderText = "StartSpd";
                stopspd.HeaderText = "StopSpd";
                homespd.HeaderText = "HomeSpd";
                creepspd.HeaderText = "CreepSpd";
                homemode.HeaderText = "HomeMode";
                homedir.HeaderText = "HomeDir";
                drivehome.HeaderText = "DriveHome";
                workspd.HeaderText = "WorkSpd";
                tacc.HeaderText = "Tacc";
                tdec.HeaderText = "Tdec";
                sln.HeaderText = "Sln";
                slp.HeaderText = "Slp";
                puls_per_mm.HeaderText = "puls/mm";
                offset.HeaderText = "Offset";
                pos0.HeaderText = "Pos0";
                pos1.HeaderText = "Pos1";
                pos2.HeaderText = "Pos2";
            }
        }
    }
}

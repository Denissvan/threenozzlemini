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

namespace UI
{
    public partial class UpLoad : UserControl
    {
        public UpLoad()
        {
            InitializeComponent();
            lb_pos.Font = new Font("Consolas", 9F);
            lb_pos.AutoSize = false;
        }
        public void UpdateShow(UpDownLoad ud)
        {
            lb_name.Text = VAR.IsChinese?string.Format("上料[{0}]", ud.id + 1): string.Format("UpLoad[{0}]", ud.id + 1); 
            lb_sta.Text = string.Format("{0} [{1:f1}s]",Utility.GetDescription(ud.status_ud, VAR.IsChinese), ud.Ct_ud);
            StringBuilder coordinates = new StringBuilder();
            coordinates.AppendFormat("X:{0:0.000}\nY:{1:0.000}", ud.ax_x._fenc_pos, ud.ax_y._fenc_pos);
            for (int slot = 0; slot < 3; slot++)
            {
                XT nozzle = ud.list_xt != null && slot < ud.list_xt.Count ? ud.list_xt[slot] : null;
                coordinates.AppendFormat("\nN{0} Z:{1}\n   U:{2}", slot + 1,
                    nozzle == null || nozzle.ax_z == null ? "--" : nozzle.ax_z._fenc_pos.ToString("0.000"),
                    nozzle == null || nozzle.ax_u == null ? "--" : nozzle.ax_u._fenc_pos.ToString("0.000"));
            }
            lb_pos.Text = coordinates.ToString();
        }
    }
}

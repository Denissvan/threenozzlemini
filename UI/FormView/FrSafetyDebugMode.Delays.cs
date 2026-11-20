using System;
using System.Drawing;
using System.Windows.Forms;
using MotionCtrl;

namespace UI
{
    /// <summary>
    /// [R15] 调试模式窗口右侧“取放延时”：员工改完点“应用取放延时”立即生效（下一次放料起），并保存，重启后保持。
    /// 不需要开启调试模式，运行中也可以应用。
    /// </summary>
    public partial class FrSafetyDebugMode
    {
        private GroupBox grp_delays;
        private CheckBox chk_dly_pasteup;
        private NumericUpDown nud_dly_place, nud_dly_movup, nud_dly_fixture, nud_dly_tray;
        private Label lbl_dly_state;

        private void InitPickPlaceDelayGroup()
        {
            ThreeNozzleHardwareConfig.Initialize();
            ClientSize = new Size(Math.Max(ClientSize.Width, 900), ClientSize.Height);
            Font f = new Font("Microsoft YaHei UI", 10.5F);
            grp_delays = new GroupBox { Text = "取放延时（应用后立即生效并保存）", Location = new Point(572, 12), Size = new Size(316, 420), Font = f };
            Controls.Add(grp_delays);

            int y = 30;
            chk_dly_pasteup = new CheckBox { Text = "夹具放料防带起（放后先抬 1mm 再等）", Location = new Point(12, y), AutoSize = true };
            grp_delays.Controls.Add(chk_dly_pasteup);
            y += 34;
            nud_dly_place = AddDelayRow("夹具放料延时(防带起)", ref y, 40, 1000);
            nud_dly_movup = AddDelayRow("夹具提起延时(防带起)", ref y, 0, 500);
            nud_dly_fixture = AddDelayRow("夹具放料延时(不防带起)", ref y, ThreeNozzleHardwareConfig.PlaceDelayMin, ThreeNozzleHardwareConfig.PlaceDelayMax);
            nud_dly_tray = AddDelayRow("料盘放料延时", ref y, ThreeNozzleHardwareConfig.PlaceDelayMin, ThreeNozzleHardwareConfig.PlaceDelayMax);

            var note = new Label
            {
                Location = new Point(12, y), Size = new Size(296, 92), ForeColor = Color.DimGray,
                Font = new Font("Microsoft YaHei UI", 9F),
                Text = "单位 ms。防带起打开时：破真空后等待≈放料延时+10，再抬 1mm，再等提起延时。\r\n" +
                       "防带起关闭时用“不防带起”那一项。延时太小可能把料带起或放不平，改完请观察几轮。\r\n" +
                       "运行中也可应用，从下一次放料起生效。"
            };
            grp_delays.Controls.Add(note);
            y += 96;

            var btnApply = new Button { Text = "应用取放延时", Location = new Point(12, y), Size = new Size(140, 36) };
            var btnDefault = new Button { Text = "恢复默认值", Location = new Point(166, y), Size = new Size(140, 36) };
            btnApply.Click += (s, e) => ApplyPickPlaceDelays();
            btnDefault.Click += (s, e) =>
            {
                nud_dly_place.Value = 50; nud_dly_movup.Value = 50; nud_dly_fixture.Value = 60; nud_dly_tray.Value = 50;
                lbl_dly_state.Text = "已填入默认值（50/50/60/50），点“应用取放延时”才生效";
                lbl_dly_state.ForeColor = Color.DarkOrange;
            };
            grp_delays.Controls.Add(btnApply);
            grp_delays.Controls.Add(btnDefault);
            y += 44;
            lbl_dly_state = new Label { Location = new Point(12, y), Size = new Size(296, 40), Font = new Font("Microsoft YaHei UI", 9F) };
            grp_delays.Controls.Add(lbl_dly_state);

            LoadPickPlaceDelays();
        }

        private NumericUpDown AddDelayRow(string text, ref int y, int min, int max)
        {
            grp_delays.Controls.Add(new Label { Text = text + ":", Location = new Point(12, y + 4), Size = new Size(190, 24) });
            var nud = new NumericUpDown { Location = new Point(206, y), Size = new Size(96, 28), Minimum = min, Maximum = max, Increment = 5, TextAlign = HorizontalAlignment.Center };
            grp_delays.Controls.Add(nud);
            y += 36;
            return nud;
        }

        private static decimal Clamp(NumericUpDown nud, int v)
        {
            return Math.Max(nud.Minimum, Math.Min(nud.Maximum, v));
        }

        private void LoadPickPlaceDelays()
        {
            chk_dly_pasteup.Checked = PT_SET.bModPasteUp;
            nud_dly_place.Value = Clamp(nud_dly_place, PT_SET.PlaceDly);
            nud_dly_movup.Value = Clamp(nud_dly_movup, PT_SET.MovUpDly);
            nud_dly_fixture.Value = Clamp(nud_dly_fixture, ThreeNozzleHardwareConfig.FixturePlaceDelayMs);
            nud_dly_tray.Value = Clamp(nud_dly_tray, ThreeNozzleHardwareConfig.TrayPlaceDelayMs);
            lbl_dly_state.Text = DelaySummary("当前生效");
            lbl_dly_state.ForeColor = Color.Green;
        }

        private static string DelaySummary(string head)
        {
            return string.Format("{0}：防带起={1}，夹具放料={2}/提起={3}，不防带起={4}，料盘放料={5}",
                head, PT_SET.bModPasteUp ? "开" : "关", PT_SET.PlaceDly, PT_SET.MovUpDly,
                ThreeNozzleHardwareConfig.FixturePlaceDelayMs, ThreeNozzleHardwareConfig.TrayPlaceDelayMs);
        }

        private void ApplyPickPlaceDelays()
        {
            string operatorName = "未知";
            try { operatorName = FrMain.frsuser.user1.cur_user.ToString(); } catch { }
            string before = DelaySummary("修改前");

            PT_SET.bModPasteUp = chk_dly_pasteup.Checked;
            PT_SET.PlaceDly = (int)nud_dly_place.Value;
            PT_SET.MovUpDly = (int)nud_dly_movup.Value;
            string error;
            bool hwOk = ThreeNozzleHardwareConfig.SetPlaceDelays((int)nud_dly_tray.Value, (int)nud_dly_fixture.Value, out error);
            EM_RES saveRes = EM_RES.ERR;
            try { saveRes = PT_SET.SavePtCfg(VAR.gsys_set.cur_product_name); } catch (Exception ex) { error = (error ?? "") + " 产品参数保存异常:" + ex.Message; }

            try { if (FrMain.frsys != null && !FrMain.frsys.IsDisposed) FrMain.frsys.SyncPickPlaceDelays(); } catch { }

            string after = DelaySummary("修改后");
            bool ok = hwOk && saveRes == EM_RES.OK;
            VAR.msg.AddMsg(ok ? Msg.EM_MSGTYPE.WAR : Msg.EM_MSGTYPE.ERR, string.Format("[R15] 调试模式取放延时{0}，操作员={1}；{2}；{3}{4}",
                ok ? "已应用并保存" : "已生效但保存失败", operatorName, before, after,
                ok ? "" : "（" + (hwOk ? "" : "ThreeNozzleHardware.ini:" + error + " ") + (saveRes == EM_RES.OK ? "" : "ptcfg.ini:" + saveRes) + "）"));
            lbl_dly_state.Text = ok ? DelaySummary("已生效并保存") : "已生效，但保存失败，重启后会恢复，请看日志";
            lbl_dly_state.ForeColor = ok ? Color.Green : Color.Red;
        }
    }
}

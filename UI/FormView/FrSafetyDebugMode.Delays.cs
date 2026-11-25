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
        private NumericUpDown nud_dly_place, nud_dly_movup, nud_dly_fixture, nud_dly_tray, nud_pick_wait, nud_pick_hold, nud_fixture_open, nud_fixture_close;
        private Label lbl_dly_state;
        private CheckBox chk_fast_issue;

        private void InitPickPlaceDelayGroup()
        {
            ThreeNozzleHardwareConfig.Initialize();
            ClientSize = new Size(Math.Max(ClientSize.Width, 900), Math.Max(ClientSize.Height, 790));   // [R33]/[R35] 取放延时多几行
            Font f = new Font("Microsoft YaHei UI", 10.5F);
            grp_delays = new GroupBox { Text = "取放延时（应用后立即生效并保存）", Location = new Point(572, 12), Size = new Size(316, 760), Font = f };
            Controls.Add(grp_delays);

            int y = 30;
            chk_dly_pasteup = new CheckBox { Text = "夹具放料防带起（放后先抬 1mm 再等）", Location = new Point(12, y), AutoSize = true };
            grp_delays.Controls.Add(chk_dly_pasteup);
            y += 34;
            nud_dly_place = AddDelayRow("夹具放料延时(防带起)", ref y, 0, 1000);
            nud_dly_movup = AddDelayRow("夹具提起延时(防带起)", ref y, 0, 500);
            nud_dly_fixture = AddDelayRow("夹具放料延时(不防带起)", ref y, ThreeNozzleHardwareConfig.PlaceDelayMin, ThreeNozzleHardwareConfig.PlaceDelayMax);
            nud_dly_tray = AddDelayRow("料盘放料延时", ref y, ThreeNozzleHardwareConfig.PlaceDelayMin, ThreeNozzleHardwareConfig.PlaceDelayMax);
            // [R20] 取料（料盘、夹具通用）
            nud_pick_wait = AddDelayRow("取料等真空(每次最长)", ref y, ThreeNozzleHardwareConfig.PickVacuumWaitMin, ThreeNozzleHardwareConfig.PickVacuumWaitMax);
            nud_pick_hold = AddDelayRow("取料吸到后保持", ref y, 0, ThreeNozzleHardwareConfig.PickHoldMax);
            // [R28] 开夹具：气缸传感器到位后再保持
            nud_fixture_open = AddDelayRow("开夹具到位后保持", ref y, 0, ThreeNozzleHardwareConfig.FixtureOpenSettleMax);
            // [R33] 合夹具：气缸到位后再保持，然后翻转测试位
            nud_fixture_close = AddDelayRow("合夹具到位后保持", ref y, 0, ThreeNozzleHardwareConfig.FixtureCloseSettleMax);

            var note = new Label
            {
                Location = new Point(12, y), Size = new Size(296, 120), ForeColor = Color.DimGray,
                Font = new Font("Microsoft YaHei UI", 9F),
                Text = "单位 ms，放料延时可设 0。防带起开：破真空后等放料延时，抬 1mm，再等提起延时；关：等“不防带起”那项。\r\n" +
                       "取料：真空到位立即抬（可加保持）；等不到真空时 Z 再压一点再等一次。改完请观察几轮。\r\n" +
                       "开夹具：气缸传感器到位后再等这么久两臂才进夹具，原固定 300。合夹具：到位后再等这么久才翻转测试位，原固定 400。\r\n" +
                       "运行中也可应用，下一次取放起生效。"
            };
            grp_delays.Controls.Add(note);
            y += 124;

            var btnApply = new Button { Text = "应用取放延时", Location = new Point(12, y), Size = new Size(140, 36) };
            var btnDefault = new Button { Text = "恢复默认值", Location = new Point(166, y), Size = new Size(140, 36) };
            btnApply.Click += (s, e) => ApplyPickPlaceDelays();
            btnDefault.Click += (s, e) =>
            {
                nud_dly_place.Value = 50; nud_dly_movup.Value = 50; nud_dly_fixture.Value = 50; nud_dly_tray.Value = 50;
                nud_pick_wait.Value = 500; nud_pick_hold.Value = 0; nud_fixture_open.Value = 300; nud_fixture_close.Value = 400;
                lbl_dly_state.Text = "已填入默认值（放料 50/50/50/50，取料 500/0，开夹具 300/合夹具 400），点“应用取放延时”才生效";
                lbl_dly_state.ForeColor = Color.DarkOrange;
            };
            grp_delays.Controls.Add(btnApply);
            grp_delays.Controls.Add(btnDefault);
            y += 44;
            lbl_dly_state = new Label { Location = new Point(12, y), Size = new Size(296, 40), Font = new Font("Microsoft YaHei UI", 9F) };
            grp_delays.Controls.Add(lbl_dly_state);
            y += 44;

            // [R16] 运动下发快速起步：勾选即生效并保存
            chk_fast_issue = new CheckBox { Text = "运动快速起步（不固定等50ms）", Location = new Point(12, y), AutoSize = true, Checked = AXIS.FastMoveIssue };
            grp_delays.Controls.Add(chk_fast_issue);
            grp_delays.Controls.Add(new Label
            {
                Location = new Point(12, y + 26), Size = new Size(296, 36), ForeColor = Color.DimGray, Font = new Font("Microsoft YaHei UI", 9F),
                Text = "取消勾选=恢复原来每根轴下发后固定等50ms。出现到位异常、动作不对时先取消勾选再观察。"
            });
            chk_fast_issue.CheckedChanged += (s, e) =>
            {
                string err;
                bool want = chk_fast_issue.Checked;
                if (!ThreeNozzleHardwareConfig.SetFastMoveIssue(want, out err))
                {
                    AXIS.FastMoveIssue = want;   // 保存失败也先在本次运行生效
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, "[R16] 运动快速起步保存失败：" + err);
                }
                string op = "未知";
                try { op = FrMain.frsuser.user1.cur_user.ToString(); } catch { }
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("[R16] 运动快速起步已{0}，操作员={1}", want ? "开启" : "关闭（每根轴下发后固定等50ms）", op));
            };

            // [R35] Z 抬起放宽到位：勾选即生效并保存，缺省不勾
            var chk_zup_loose = new CheckBox { Text = "Z抬起放宽到位（进1mm即横移）", Location = new Point(12, y + 66), AutoSize = true, Checked = AXIS.ZeroTargetLooseSettle };
            grp_delays.Controls.Add(chk_zup_loose);
            grp_delays.Controls.Add(new Label
            {
                Location = new Point(12, y + 92), Size = new Size(296, 36), ForeColor = Color.DimGray, Font = new Font("Microsoft YaHei UI", 9F),
                Text = "原来 Z 抬起要等编码器进 0.5mm 才横移；勾选后进 1mm 就横移。出现吸嘴刮碰时取消勾选。"
            });
            chk_zup_loose.CheckedChanged += (s, e) =>
            {
                string err;
                bool want = chk_zup_loose.Checked;
                if (!ThreeNozzleHardwareConfig.SetZUpLooseSettle(want, out err))
                {
                    AXIS.ZeroTargetLooseSettle = want;
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, "[R35] Z抬起放宽到位保存失败：" + err);
                }
                string op = "未知";
                try { op = FrMain.frsuser.user1.cur_user.ToString(); } catch { }
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("[R35] Z抬起放宽到位已{0}，操作员={1}", want ? "开启（进1mm即横移）" : "关闭（进0.5mm才横移）", op));
            };

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
            nud_pick_wait.Value = Clamp(nud_pick_wait, ThreeNozzleHardwareConfig.PickVacuumWaitMs);
            nud_pick_hold.Value = Clamp(nud_pick_hold, ThreeNozzleHardwareConfig.PickHoldMs);
            nud_fixture_open.Value = Clamp(nud_fixture_open, ThreeNozzleHardwareConfig.FixtureOpenSettleMs);
            nud_fixture_close.Value = Clamp(nud_fixture_close, ThreeNozzleHardwareConfig.FixtureCloseSettleMs);
            lbl_dly_state.Text = DelaySummary("当前生效");
            lbl_dly_state.ForeColor = Color.Green;
        }

        private static string DelaySummary(string head)
        {
            return string.Format("{0}：防带起={1}，夹具放料={2}/提起={3}，不防带起={4}，料盘放料={5}，取料等真空={6}/保持={7}，开夹具保持={8}，合夹具保持={9}",
                head, PT_SET.bModPasteUp ? "开" : "关", PT_SET.PlaceDly, PT_SET.MovUpDly,
                ThreeNozzleHardwareConfig.FixturePlaceDelayMs, ThreeNozzleHardwareConfig.TrayPlaceDelayMs,
                ThreeNozzleHardwareConfig.PickVacuumWaitMs, ThreeNozzleHardwareConfig.PickHoldMs,
                ThreeNozzleHardwareConfig.FixtureOpenSettleMs, ThreeNozzleHardwareConfig.FixtureCloseSettleMs);
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
            if (hwOk) hwOk = ThreeNozzleHardwareConfig.SetPickDelays((int)nud_pick_wait.Value, (int)nud_pick_hold.Value, out error);
            if (hwOk) hwOk = ThreeNozzleHardwareConfig.SetFixtureOpenSettle((int)nud_fixture_open.Value, out error);
            if (hwOk) hwOk = ThreeNozzleHardwareConfig.SetFixtureCloseSettle((int)nud_fixture_close.Value, out error);
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

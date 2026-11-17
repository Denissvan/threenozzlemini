using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MotionCtrl;

namespace UI
{
    public partial class UpDownLoad
    {
        /// <summary>
        /// 自动对账（与原双吸嘴一致，不弹窗、不要人工确认）：以真空为准校正每个吸嘴的物料记录
        /// （记录有料但真空无料 → 清记录；真空有料但无记录 → 关真空），然后清除“待对账”锁和本臂运行计划。
        /// 记录与真空一致、仍吸着的物料保留，下次预取/换料入口先把它落盘（ThreeNozzleDisposeOrphans）。
        /// 只在所有上下料任务已停止时调用（开始键前的自动复位、上下料回零、三吸嘴复位）。返回 true 表示本臂已无锁。
        /// </summary>
        public bool ThreeNozzleAutoReconcile(string why)
        {
            bool locked = threeNozzleRecovery || threeNozzlePendingVacuum != null;
            if (Interlocked.CompareExchange(ref threeNozzleExecutor, 1, 0) != 0)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, disc + " 三吸嘴自动对账：本臂任务仍在执行，本次跳过");
                return !locked;
            }
            try
            {
                int corrected = ThreeNozzleSweepVacuumMismatch(why);
                if (!locked)
                {
                    if (corrected > 0)
                        VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 三吸嘴: {1}，按真空校正了 {2} 个吸嘴的物料记录", disc, why, corrected));
                    return true;
                }
                string held = string.Join("、", list_xt.Where(xt => xt.XtMd != null)
                    .Select(xt => xt.disc + "(" + (xt.XtMd.bardcode ?? "") + ")"));
                ThreeNozzleClearReconciledRuntime();
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format(
                    "{0} 三吸嘴: {1}，已按真空自动对账（校正 {2} 个吸嘴）并清除待对账锁定{3}",
                    disc, why, corrected, held.Length == 0 ? "" : "；吸嘴仍持料 " + held + "，启动后先落盘"));
                return true;
            }
            finally { Interlocked.Exchange(ref threeNozzleExecutor, 0); }
        }

        // 自动校正清除吸嘴记录时：模组点检样品按原人工对账的方式登记，避免点检流程一直等这颗样品。
        partial void ThreeNozzleOnRecordCleared(XT xt, object material, string where)
        {
            Product.MdDat md = material as Product.MdDat;
            if (md == null || string.IsNullOrEmpty(md.ModuleCheckSampleId)) return;
            try { UI.ModuleCheck.ModuleCheckHooks.ConfirmSampleHomedByOperator(md.ModuleCheckSampleId, disc + " " + xt.disc + " 自动对账(" + where + ")"); }
            catch (Exception ex) { VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, disc + " 自动对账登记点检样品失败：" + ex.Message); }
        }

        /// <summary>
        /// 回零前的人工对账入口（已不再被调用，保留备查）。三吸嘴异常锁只在操作员明确确认后清除，绝不自动清除。
        /// 只在 UI 线程、且所有上下料任务已停止时调用。返回 true 表示本臂无异常锁或已清除。
        /// </summary>
        public bool ThreeNozzleReconcileBeforeHome(bool homing = true)
        {
            if (!threeNozzleRecovery) return true;
            string title = disc + " 三吸嘴人工对账";
            DialogResult answer = MessageBox.Show(
                disc + " 存在三吸嘴“待人工对账”状态，自动运行被锁定。\r\n\r\n" +
                "请先确认：\r\n" +
                "1. 本臂三个吸嘴上的物料已全部人工取下；\r\n" +
                "2. 夹具穴位和料盘的物料记录已人工核对。\r\n\r\n" +
                (homing ? "“是”= 已完成对账，清除锁定后回零\r\n“否”= 保持锁定（仍会回零，但不能自动运行）"
                        : "“是”= 已完成对账，清除锁定（三吸嘴复位，不回零）\r\n“否”= 保持锁定，不能自动运行"),
                title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, disc + " 三吸嘴: 回零时操作员选择保持待对账锁定");
                return false;
            }

            XT[] vacuumHeld = list_xt.Where(xt => xt.cy_zk.isONByChkSen).ToArray();
            if (vacuumHeld.Length > 0)
            {
                MessageBox.Show(disc + " 以下吸嘴真空仍检测到有料：" + string.Join("、", vacuumHeld.Select(xt => xt.disc)) +
                    "\r\n请人工取下物料后再回零，本次不清除锁定。", title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, disc + " 三吸嘴: 对账失败，真空仍有料");
                return false;
            }

            XT[] dataHeld = list_xt.Where(xt => xt.XtMd != null).ToArray();
            if (dataHeld.Length > 0)
            {
                DialogResult clear = MessageBox.Show(disc + " 以下吸嘴真空无料，但软件里仍记录有物料：" +
                    string.Join("、", dataHeld.Select(xt => xt.disc + "(" + (xt.XtMd.bardcode ?? "") + ")")) +
                    "\r\n\r\n确认这些物料已人工取走并处理，清除吸嘴物料记录？",
                    title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (clear != DialogResult.Yes)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, disc + " 三吸嘴: 操作员未确认清除吸嘴物料记录，保持锁定");
                    return false;
                }
                foreach (XT xt in dataHeld)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 三吸嘴: 人工对账清除 {1} 物料记录 条码={2} 结果={3}",
                        disc, xt.disc, xt.XtMd.bardcode, xt.XtMd.res));
                    string sampleId = xt.XtMd.ModuleCheckSampleId;
                    xt.XtMd = null;
                    if (!string.IsNullOrEmpty(sampleId))
                        UI.ModuleCheck.ModuleCheckHooks.ConfirmSampleHomedByOperator(sampleId, disc + " " + xt.disc + " 人工对账");
                }
            }

            EM_RES result = ThreeNozzleResetAfterManualReconciliation(true);
            if (result != EM_RES.OK)
            {
                MessageBox.Show(disc + " 清除待对账锁定失败：" + result + "\r\n请查看日志。", title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, disc + " 三吸嘴: 人工对账完成，已清除待对账锁定");
            return true;
        }

        /// <summary>
        /// 开机首次回零后吸嘴仍吸着物料时，不再直接关真空让料掉落，而是由操作员确认。
        /// 在回零后台线程调用。
        /// </summary>
        private void ConfirmHeldPartsAfterFirstHome()
        {
            if (!IsFirst) return;
            XT[] held = list_xt.Where(xt => xt.cy_zk.isONByChkSen).ToArray();
            if (held.Length == 0) return;
            string names = string.Join("、", held.Select(xt => xt.disc));
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, disc + " 首次回零后吸嘴有料：" + names + "，等待人工确认");
            DialogResult answer = MessageBox.Show(
                disc + " 回零完成，以下吸嘴仍吸着物料：" + names + "\r\n\r\n" +
                "“是”= 关闭这些吸嘴的真空（请先用手托住物料，物料会落下）\r\n" +
                "“否”= 保持真空，稍后人工处理",
                disc + " 吸嘴有料确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, disc + " 首次回零：操作员选择保持吸嘴真空");
                return;
            }
            foreach (XT xt in held) xt.cy_zk.SetOff();
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, disc + " 首次回零：操作员确认后关闭真空 " + names);
        }
    }
}

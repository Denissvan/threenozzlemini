using System;
using System.Threading;
using System.Windows.Forms;
using MotionCtrl;

namespace UI
{
    // 吸嘴“物料记录 / 真空反馈”不一致的处理，与原双吸嘴（MINI3.0.0.17 UpDownLoad.cs 料盘取料前检查）保持一致：
    // 以真空为准，不一致就清除本嘴物料记录、关闭真空、记录 ERR 日志后继续，不弹窗、不要求人工对账。
    public partial class UpDownLoad
    {
        private XT threeNozzlePendingVacuum;
        private bool threeNozzlePendingVacuumExpected;
        private string threeNozzlePendingVacuumWhere;
        internal bool ThreeNozzleHasPendingVacuumIssue { get { return threeNozzlePendingVacuum != null; } }

        // 清除吸嘴物料记录后的附加处理（模组点检样品登记等），实现在 HomeRecovery.cs。
        partial void ThreeNozzleOnRecordCleared(XT xt, object material, string where);

        // 停止打断取料、当时真空读数不稳定时登记的待处理吸嘴：下次启动先按真空自动校正。
        private EM_RES ThreeNozzleCheckPendingVacuum(ref bool quit)
        {
            XT pending = threeNozzlePendingVacuum;
            string where = threeNozzlePendingVacuumWhere;
            threeNozzlePendingVacuum = null;
            threeNozzlePendingVacuumExpected = false;
            if (pending != null) ThreeNozzleAutoCorrectVacuum(pending, string.IsNullOrEmpty(where) ? "停止后真空状态未明确" : where);
            return (quit || VAR.gsys_set.bquit) ? EM_RES.QUIT : EM_RES.OK;
        }

        // 原来这里弹“吸取/真空异常”窗，必须按“重新检查”到一致才放行，记录与真空对不上就无限弹窗。
        // 现在与双吸嘴一致，直接自动校正。expected 参数保留只为不改调用方；判定只看当前记录与真空。
        private EM_RES ThreeNozzleRequireVacuumCorrection(ref bool quit, XT xt, bool expected, string where)
        {
            threeNozzlePendingVacuum = null;
            ThreeNozzleAutoCorrectVacuum(xt, where);
            return (quit || VAR.gsys_set.bquit) ? EM_RES.QUIT : EM_RES.OK;
        }

        /// <summary>
        /// 对本臂每个吸嘴做一次双吸嘴式校正，返回校正的吸嘴个数。停止后续接、自动对账时调用。
        /// </summary>
        internal int ThreeNozzleSweepVacuumMismatch(string where)
        {
            int corrected = 0;
            if (Demo || list_xt == null) return 0;
            foreach (XT xt in list_xt)
                if (xt != null && ThreeNozzleAutoCorrectVacuum(xt, where)) corrected++;
            return corrected;
        }

        /// <summary>
        /// 双吸嘴规则：记录有料但真空无料，或真空有料但没有记录 → 清除记录、关真空（未记录的料会落下）。
        /// 记录与真空一致时不动。返回 true 表示做了校正。
        /// </summary>
        private bool ThreeNozzleAutoCorrectVacuum(XT xt, string where)
        {
            if (Demo || xt == null) return false;
            bool recorded = xt.XtMd != null;
            bool vacuum = ThreeNozzleVacuumHeld(xt);
            if (recorded == vacuum) return false;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, string.Format(
                "{0} {1} {2}：检查到真空与物料记录不一致（记录有料={3} 真空有料={4}），按双吸嘴处理：清除本嘴物料记录并关闭真空后继续{5}",
                disc, xt.disc, where, recorded, vacuum,
                recorded ? "，清除的物料条码=" + (xt.XtMd.bardcode ?? "") + " 结果=" + xt.XtMd.res : "（吸嘴上未记录的物料会落下）"));
            // 只放开本嘴对物料对象的引用，不改物料对象本身（它可能仍被料盘/夹具/计划引用）
            object material = xt.XtMd;
            xt.XtMd = null;
            xt.cy_zk.SetOff();
            if (material != null) ThreeNozzleOnRecordCleared(xt, material, where);
            Thread.Sleep(100);
            return true;
        }

        // 连续采样 5 次，多数为有料才算有料，避免单次抖动把正在吸着的料关掉。
        private bool ThreeNozzleVacuumHeld(XT xt)
        {
            int on = 0;
            for (int i = 0; i < 5; i++)
            {
                if (xt.cy_zk.isONByChkSen) on++;
                if (i < 4) Thread.Sleep(20);
            }
            return on >= 3;
        }
    }
}

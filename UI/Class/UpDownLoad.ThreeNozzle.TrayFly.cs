using System;
using System.Collections.Generic;
using System.Linq;
using MotionCtrl;
using UI.Class;
using UI.ModuleCheck;

namespace UI
{
    /// <summary>
    /// 三吸嘴：供料盘取料飞拍（参照原流程 FlyCamToTray / FLyToTray）。
    /// 一组要取的两颗待测料如果属于料盘同一逻辑行（Row 与原双吸嘴 FindFdTrayPos 一致），
    /// 先让料盘 X 和臂 X 到位，臂沿 Y 走一趟、上相机按位置比较连续拍两穴（ModUp_Shp），
    /// 之后两颗都按飞拍结果直接取，不再每颗“到拍照位停下 → 拍 → 移到取料位”。
    /// 两穴共用第一穴的拍照 X 和料盘 X；各自保留 Y。缓存实际公共拍照坐标，取料时不再套用第二穴的独立料盘 X。
    /// 飞拍没拿到可用结果（无图/NG/张数不符）、两穴跨逻辑行、或条件不满足时，这颗照原来的定拍取料（XtCapMovMod）。
    /// </summary>
    public partial class UpDownLoad
    {
        public enum ThreeNozzleTrayFlyMode
        {
            /// <summary>关闭：每颗定拍取料（原流程）</summary>
            Off = 0,
            /// <summary>对比：飞拍后仍在同一拍照位定拍，用定拍结果取料；两者差值写日志和 CSV</summary>
            Compare = 1,
            /// <summary>启用：按飞拍结果直接取料</summary>
            Use = 2,
        }

        /// <summary>供料盘取料飞拍模式（默认启用）。只在本次运行有效。</summary>
        public static ThreeNozzleTrayFlyMode ThreeNozzleTrayFly = ThreeNozzleTrayFlyMode.Use;

        /// <summary>料盘飞拍的触发偏差（mm）。NaN = 用吸嘴1 的 ws_cap_near_ofs（与原流程料盘飞拍 MOD_UP 第一张相同）。</summary>
        public static double ThreeNozzleTrayFlyOffset = double.NaN;

        private sealed class ThreeNozzleTrayFlyInfo
        {
            internal Product.Tray Tray;
            internal int SourceIdx;
            internal ST_XYZA CamPos;            // 拍照位：x/y 臂（上相机）位置，a 料盘 X
            internal Cam.VisionOutPutData Result;
        }

        /// <summary>本次 PreparePair 里飞拍得到的结果，按料盘穴号。</summary>
        private readonly Dictionary<int, ThreeNozzleTrayFlyInfo> threeNozzleTrayFlyResults = new Dictionary<int, ThreeNozzleTrayFlyInfo>();

        /// <summary>沿用原双嘴的 Row 分组；返回本趟真实拍照坐标，不以逐穴 X 差值作为分组条件。</summary>
        internal static bool ThreeNozzleTrayTrySharedCam(int firstRow, ST_XYZA firstCam, int row, ST_XYZA nominalCam, out ST_XYZA actualCam)
        {
            actualCam = new ST_XYZA();
            if (firstRow != row) return false;
            if (!FlyFinite(firstCam.x) || !FlyFinite(firstCam.y) || !FlyFinite(firstCam.a)
                || !FlyFinite(nominalCam.x) || !FlyFinite(nominalCam.y) || !FlyFinite(nominalCam.a)) return false;
            actualCam = nominalCam.clone();
            actualCam.x = firstCam.x;
            actualCam.a = firstCam.a;
            return true;
        }

        /// <summary>供料盘飞拍不生效的原因；null = 生效。</summary>
        private string ThreeNozzleTrayFlyDisabledReason()
        {
            if (ThreeNozzleTrayFly == ThreeNozzleTrayFlyMode.Off) return "供料盘飞拍已关闭";
            string why = ThreeNozzleFlyDisabledReason();
            if (why != null) return why;
            if (PT_SET.bAddCapQrcode) return "启用了上相机附加扫码位（bAddCapQrcode），按定拍取料";
            if (upcam == null || !upcam.List_vs_task.Any(t => t.TaskName == CONST.ModUpFw)) return "上相机没有视觉任务 " + CONST.ModUpFw;
            return null;
        }

        /// <summary>
        /// 一组取料开始时（已持有料盘锁、料盘已就绪）调用一次：找出本组要从供料盘取的几颗，
            /// 同一逻辑行的穴走一趟飞拍，结果存到 threeNozzleTrayFlyResults。失败只影响“这颗改定拍”，不报错；
        /// 运动失败才返回非 OK。
        /// </summary>
        private EM_RES ThreeNozzleTrayFlyPrepare(ref bool quit, ThreeNozzleRuntimePair pair, int firstSlot,
            List<Product.Tray.PosInf> positions)
        {
            threeNozzleTrayFlyResults.Clear();
            string why = ThreeNozzleTrayFlyDisabledReason();
            if (why != null)
            {
                ThreeNozzleNoteTrayFlyOff(why);
                return EM_RES.OK;
            }
            if (positions == null || positions.Count == 0 || traybox_fd.tray_cur == null) return EM_RES.OK;

            // 本组还要从供料盘取几颗（点检样品不算）
            int need = 0;
            for (int s = firstSlot; s < 2; s++)
            {
                if (!pair.Incoming[s] || pair.Placed[s] || pair.Prepared[s] != null) continue;
                ModuleCheckSample sample; Product.Tray.PosInf mcSource; string mcError;
                if (ModuleCheckSourceFor(pair.Pockets[s], out sample, out mcSource, out mcError)) continue;
                need++;
            }
            // 只剩一颗时飞拍要额外助跑，不比定拍快，照定拍
            if (need < 2) return EM_RES.OK;

            WS.MdDat column = pair.Pockets[firstSlot] ?? pair.Pockets[1 - firstSlot];
            double wsX = column != null ? column.st_pos[id].x : 0;
            XT camRef = list_xt[0];
            List<ThreeNozzleTrayFlyInfo> infos = new List<ThreeNozzleTrayFlyInfo>();
            for (int i = 0; i < Math.Min(need, positions.Count); i++)
            {
                Product.Tray.PosInf src = positions[i];
                ST_XYZA nominalCam = camRef.CaliPosAt(src.Pos[id], wsX, 0);
                if (src.Row != positions[0].Row)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 供料盘飞拍: 穴{1}(Row={2}) 与穴{3}(Row={4}) 跨逻辑行，按原双嘴规则定拍取料",
                        disc, src.idx, src.Row, positions[0].idx, positions[0].Row));
                    break;
                }
                ST_XYZA cam;
                if (!ThreeNozzleTrayTrySharedCam(positions[0].Row, infos.Count == 0 ? nominalCam : infos[0].CamPos,
                    src.Row, nominalCam, out cam))
                {
                    ThreeNozzleNoteTrayFlyOff("供料盘拍照坐标无效");
                    break;
                }
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 供料盘飞拍[原双嘴分组]: 穴{1} Row={2} 独立拍照X={3:F3} 独立料盘X={4:F3} 实际公共拍照X={5:F3} 实际公共料盘X={6:F3} Y={7:F3}",
                    disc, src.idx, src.Row, nominalCam.x, nominalCam.a, cam.x, cam.a, cam.y));
                infos.Add(new ThreeNozzleTrayFlyInfo { Tray = traybox_fd.tray_cur, SourceIdx = src.idx, CamPos = cam });
            }
            if (infos.Count < 2) return EM_RES.OK;

            // 方向：从当前 Y 这一侧飞过去；助跑与进夹具飞拍相同（180mm），保证经过触发点时的速度曲线一致
            double ofs = FlyFinite(ThreeNozzleTrayFlyOffset) ? ThreeNozzleTrayFlyOffset : list_xt[0].ws_cap_near_ofs;
            if (!FlyFinite(ofs)) ofs = 0;
            double minY = infos.Min(f => f.CamPos.y), maxY = infos.Max(f => f.CamPos.y);
            double curY = ax_y.fcmd_pos;
            bool increasing = curY < (minY + maxY) / 2;
            // 短助跑：当前 Y 离第一个拍照位已有 ≥40mm 就从当前位置飞，不再先退到“触发点 − 180mm”（料盘外侧）；
            // 不够 40mm 时只退到 40mm。触发补偿按触发点处的估算速度等比缩小。
            bool shortRunUp = ThreeNozzleTrayShortRunUpEnabled && !ThreeNozzleHardwareConfig.FlyTwoSpeedEnabled;
            double plannedStart = double.NaN;
            if (shortRunUp)
            {
                double edge = increasing ? minY - ThreeNozzleTrayMinRunUp - 1.0 : maxY + ThreeNozzleTrayMinRunUp + 1.0;
                plannedStart = increasing ? Math.Min(curY, edge) : Math.Max(curY, edge);
            }
            double vWork = ax_y.spd_work;
            List<ThreeNozzleFlyShot> shots = infos.Select(f => new ThreeNozzleFlyShot
            {
                Camera = upcam,
                Task = CONST.ModUpFw,
                X = f.CamPos.x,
                NominalY = f.CamPos.y,
                TriggerY = ThreeNozzleFlyTrigger(f.CamPos.y,
                    shortRunUp && vWork > 0 ? ofs * ThreeNozzleSpeedAfterRunUp(Math.Abs(f.CamPos.y - plannedStart)) / vWork : ofs,
                    increasing),
                Identity = f.SourceIdx,
                Label = "料盘穴" + f.SourceIdx,
            }).ToList();
            double firstTrig = increasing ? shots.Min(s => s.TriggerY) : shots.Max(s => s.TriggerY);
            double lastTrig = increasing ? shots.Max(s => s.TriggerY) : shots.Min(s => s.TriggerY);
            double runUp = shortRunUp ? ThreeNozzleTrayMinRunUp : ThreeNozzleFlyRunUp;
            double startY = increasing ? firstTrig - runUp : firstTrig + runUp;
            double endY = increasing ? lastTrig + ThreeNozzleFlyRunOut : lastTrig - ThreeNozzleFlyRunOut;
            // 当前位置已经在起点之外（助跑更长）就从当前位置飞；起点超出 Y 行程则不飞
            if (increasing ? curY < startY : curY > startY) startY = curY;
            if (shortRunUp)
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format(
                    "{0} 供料盘飞拍：短助跑，从 Y={1:F3} 起飞（当前 Y={2:F3}，{3}，助跑 {4:F1}mm，第一个触发点速度约 {5:F0}mm/s，补偿按速度缩放）",
                    disc, startY, curY, increasing ? "Y 增大方向" : "Y 减小方向", Math.Abs(firstTrig - startY),
                    ThreeNozzleSpeedAfterRunUp(Math.Abs(firstTrig - startY))));
            if (startY < ax_y.sln || startY > ax_y.slp || endY < ax_y.sln || endY > ax_y.slp)
            {
                ThreeNozzleNoteTrayFlyOff(string.Format("飞拍起终点 Y {0:F1}→{1:F1} 超出 Y 行程，定拍取料", startY, endY));
                return EM_RES.OK;
            }

            double camX = infos[0].CamPos.x, trayX = infos[0].CamPos.a;
            string what = "供料盘(" + string.Join("/", infos.Select(f => "穴" + f.SourceIdx)) + ")";
            string ctPrev = CtStepLog.CurrentContext;
            CtStepLog.SetContext(disc, "取待测飞拍" + what);
            try
            {
                Camcfgset(0U);
                // 臂到起点、料盘 X 到拍照位（一起走）；之后 FlyPass 看到 X 已对齐、Y 在起点，直接配触发起飞
                EM_RES res = MT.ZupMove(ref quit, ref ax_x, camX, ref ax_y, startY, ref traybox_fd.ax_x, trayX);
                if (res != EM_RES.OK) return res;
                traybox_fd.MoveToLastPos[id] = true;
                res = ThreeNozzleFlyPass(ref quit, what, camX, startY, endY, shots);
                if (res != EM_RES.OK) return res;
                for (int i = 0; i < infos.Count; i++)
                {
                    infos[i].Result = shots[i].Result;
                    threeNozzleTrayFlyResults[infos[i].SourceIdx] = infos[i];
                }
                return EM_RES.OK;
            }
            finally { CtStepLog.SetContext(disc, ctPrev); }
        }

        /// <summary>
        /// 这颗料有可用的飞拍结果时按飞拍结果取（对比模式下先在同一拍照位定拍、用定拍结果取，并记录差值）。
        /// 返回 null：没有可用的飞拍结果，调用方照原来定拍取料。
        /// </summary>
        private EM_RES? ThreeNozzleTrayFlyPick(ref bool quit, XT xt, Product.Tray.PosInf source, out string barcode)
        {
            barcode = "";
            ThreeNozzleTrayFlyInfo info;
            if (!threeNozzleTrayFlyResults.TryGetValue(source.idx, out info)) return null;
            threeNozzleTrayFlyResults.Remove(source.idx);   // 只用一次：重取、换盘后照定拍
            if (!Object.ReferenceEquals(info.Tray, traybox_fd.tray_cur)) return null;
            Cam.VisionOutPutData fly = info.Result;
            if (fly == null || !fly.bOK || !fly.bUpdate)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 供料盘飞拍: 穴{1} {2}，改定拍取料",
                    disc, source.idx, fly == null ? "无图" : "视觉NG"));
                return null;
            }
            ST_XYA vs = fly.PosMM;
            double limit = PT_SET.Vs_XYofs;
            if (ThreeNozzleTrayFly == ThreeNozzleTrayFlyMode.Use && limit > 0 && (Math.Abs(vs.x) > limit || Math.Abs(vs.y) > limit))
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 供料盘飞拍: 穴{1} 偏移 X{2:F3} Y{3:F3} 超出 Vs_XYofs={4}，改定拍取料",
                    disc, source.idx, vs.x, vs.y, limit));
                return null;
            }
            barcode = fly.BarCode ?? "";
            if (ThreeNozzleTrayFly == ThreeNozzleTrayFlyMode.Compare)
            {
                // 同一拍照位（同一臂 X/Y、同一料盘 X）定拍，用定拍结果取料
                ST_XYA fixedVs = new ST_XYA();
                string fixedCode;
                EM_RES r = xt.UpCamMod(ref quit, out fixedCode, info.CamPos, CONST.ModUpFw, ref fixedVs, Demo);
                if (r != EM_RES.OK)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, string.Format("{0} 供料盘飞拍[对比]: 穴{1} 定拍失败（{2}），改原流程定拍取料", disc, source.idx, r));
                    return null;
                }
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} 供料盘飞拍[对比]: 穴{1} 飞拍 X{2:F3} Y{3:F3} A{4:F3}，定拍 X{5:F3} Y{6:F3} A{7:F3}，差 X{8:F3} Y{9:F3} A{10:F3}",
                    disc, source.idx, vs.x, vs.y, vs.a, fixedVs.x, fixedVs.y, fixedVs.a, vs.x - fixedVs.x, vs.y - fixedVs.y, vs.a - fixedVs.a));
                UpDownTimingLog.WritePrePlaceCompare(disc, "供料盘", source.idx, CONST.ModUpFw, "供料盘取料对比", vs, fixedVs, Demo);
                vs = fixedVs;
                barcode = fixedCode ?? "";
            }
            return ThreeNozzleTrayPickAt(ref quit, xt, source, info.CamPos, vs,
                ThreeNozzleTrayFly == ThreeNozzleTrayFlyMode.Compare ? "定拍(对比)" : "飞拍");
        }

        /// <summary>按拍照位 camPos 和上相机结果 vs 算取料位并取料（与 XtCapMovMod 拍照后的取料相同）。</summary>
        private EM_RES ThreeNozzleTrayPickAt(ref bool quit, XT xt, Product.Tray.PosInf source, ST_XYZA camPos, ST_XYA vs, string how)
        {
            ST_XYZA pickPos = xt.XtCalPos(camPos.ToXY(), vs, xt.st_vs_pos_xtshp, XT.EM_XTFLOW.PICKMOD).ToXYZA();
            try { pickPos.z = NozzleZCalibration.ToAxisZ(id, xt.SlotIndex, source.Pos[id].z, xt.ZApproachDirection); }
            catch (Exception ex)
            {
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, "吸嘴Z标定不可用：" + ex.Message);
                return EM_RES.PARA_ERR;
            }
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.DBG, string.Format("{0} {1} {2}取料 供料盘穴{3}：vs {4}，取料位 X={5:F3} Y={6:F3} Z={7:F3}，料盘X={8:F3}",
                disc, xt.disc, how, source.idx, vs.ToString(), pickPos.x, pickPos.y, pickPos.z, camPos.a));
            // 视觉结果是相对拍照时的料盘位置：取料时料盘 X 必须回到拍照位的 X
            EM_RES result = MT.ZupMove(ref quit, ref ax_x, pickPos.x, ref ax_y, pickPos.y, ref traybox_fd.ax_x, camPos.a);
            if (result != EM_RES.OK) return result;
            traybox_fd.MoveToLastPos[id] = true;
            xt.xt_pos_pick_tray_x = traybox_fd.ax_x.fenc_pos;
            xt.xt_pos_pick_mod = pickPos;
            result = xt.PickOrPlace(ref quit, pickPos, true, Demo, true);
            if (result != EM_RES.OK) return result;
            AXIS u = xt.ax_u;
            MT.Move(ref quit, ref u, 0);
            return EM_RES.OK;
        }

        /// <summary>
        /// 开关：供料盘飞拍短助跑（默认开）。关掉恢复原来先退到“第一个触发点 − 180mm”（料盘外侧）再飞。
        /// 上机初期建议把供料盘飞拍切到“对比”模式跑一段，看飞拍与定拍的差值是否和改之前一样。
        /// </summary>
        public static bool ThreeNozzleTrayShortRunUpEnabled = true;
        /// <summary>供料盘短助跑时，起点离第一个拍照位至少这么远（mm）。</summary>
        private const double ThreeNozzleTrayMinRunUp = 40.0;

        private static string lastTrayFlyOffReason;
        private void ThreeNozzleNoteTrayFlyOff(string why)
        {
            if (string.IsNullOrEmpty(why) || why == lastTrayFlyOffReason) return;
            lastTrayFlyOffReason = why;
            VAR.msg.AddMsg(Msg.EM_MSGTYPE.WAR, "三吸嘴: 供料盘不飞拍（" + why + "），按定拍取料");
        }
    }
}

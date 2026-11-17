using System;
using System.IO;
using System.Linq;
using MotionCtrl;

namespace UI
{
    /// <summary>
    /// 在「系统校准-吸嘴Z测高-相机」页按序列号手动添加的相机。
    /// 名单存在 syscfg.ini [ADDED_CAMS] LIST=HikCam1,HikCam2,...，每台相机自己的 SN/描述/触发方式存在 [HikCamN]。
    /// 添加后的相机放进 COM.ListCam，和原有四个相机一样出现在各页面的 VisionPro 显示控件里，可直接触发/实况。
    /// </summary>
    public static partial class COM
    {
        private const string AddedCamSection = "ADDED_CAMS";
        private const string AddedCamPrefix = "HikCam";
        private static bool addedCamsLoaded;
        private static readonly object addedCamSync = new object();

        private static string SyscfgFile { get { return Path.GetFullPath("..") + "\\syscfg\\syscfg.ini"; } }

        /// <summary>把 syscfg.ini 里登记的手动相机加入 ListCam（只加载一次）。CamInit 前调用。</summary>
        public static void LoadAddedCams()
        {
            lock (addedCamSync)
            {
                if (addedCamsLoaded) return;
                addedCamsLoaded = true;
                try
                {
                    if (!File.Exists(SyscfgFile)) return;
                    var inf = new IniFile(SyscfgFile);
                    string list = inf.ReadString(AddedCamSection, "LIST", "").Trim('\0', ' ');
                    foreach (string raw in list.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string name = raw.Trim();
                        if (name.Length == 0 || ListCam.Any(c => c.mName == name)) continue;
                        var cam = new Cam(name, name);
                        cam.LoadCfg();
                        cam.UserAdded = true;
                        cam.status = Cam.CAM_STA.DISCONNECT;
                        ListCam.Add(cam);
                    }
                }
                catch (Exception ex)
                {
                    VAR.msg.AddMsg(Msg.EM_MSGTYPE.ERR, "加载手动添加的相机失败：" + ex.Message);
                }
            }
        }

        /// <summary>按序列号新建一台海康相机并登记到 syscfg.ini，返回新相机（尚未连接）。</summary>
        public static Cam AddCamBySerial(string serialNumber, string description, Cam.HIK_SOURCE source, bool risingEdge, string triggerIo, string strobeLine, out string error)
        {
            error = null;
            serialNumber = (serialNumber ?? "").Trim();
            description = (description ?? "").Trim();
            if (serialNumber.Length == 0) { error = "请输入相机序列号"; return null; }
            lock (addedCamSync)
            {
                LoadAddedCams();
                Cam same = ListCam.FirstOrDefault(c => string.Equals((c.mSerialNumber ?? "").Trim(), serialNumber, StringComparison.OrdinalIgnoreCase));
                if (same != null) { error = string.Format("序列号 {0} 已被「{1}」使用", serialNumber, same.disc); return null; }

                int n = 1;
                while (ListCam.Any(c => c.mName == AddedCamPrefix + n)) n++;
                string name = AddedCamPrefix + n;
                if (description.Length == 0) description = "海康相机" + n;
                // Cam.LoadCfg 只认长度 >3 的描述，太短的补前缀
                if (description.Length <= 3) description = "海康相机-" + description;

                var cam = new Cam(name, description)
                {
                    mSerialNumber = serialNumber,
                    Backend = Cam.CAM_BACKEND.Hik,
                    HikSource = source,
                    HikRisingEdge = risingEdge,
                    HikTriggerIoName = triggerIo ?? "",
                    HikStrobeLine = string.IsNullOrEmpty(strobeLine) ? "LINE1" : strobeLine,
                    UserAdded = true,
                    mTriggerMode = Cognex.VisionPro.CogAcqTriggerModelConstants.Manual,
                };
                cam.status = Cam.CAM_STA.DISCONNECT;
                cam.curCapCfg.Exposure = 10;          // ms，之后在 VisionPro 控件参数栏里改
                cam.liveCapCfg = cam.curCapCfg;
                if (cam.SaveCfg() != EM_RES.OK) { error = "写 syscfg.ini 失败"; return null; }
                ListCam.Add(cam);
                SaveAddedCamList();
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format("添加相机 {0}（{1}，SN={2}）", cam.disc.Trim(), name, serialNumber));
                return cam;
            }
        }

        /// <summary>
        /// 删除相机。手动添加的相机从列表和 syscfg.ini 名单里移除；
        /// 原有四个相机（上相机1/2、下相机1/2）被流程代码按名字直接使用，不能真正移除，删除 = 断开并清空序列号，之后不再初始化。
        /// </summary>
        public static bool RemoveAddedCam(Cam cam, out string error)
        {
            error = null;
            if (cam == null) { error = "未选择相机"; return false; }
            if (!cam.UserAdded)
            {
                try { cam.Dispose(); } catch { }
                string oldSn = cam.mSerialNumber;
                cam.mSerialNumber = "";
                cam.bInit = false;
                cam.status = Cam.CAM_STA.DISCONNECT;
                if (cam.SaveCfg() != EM_RES.OK) { error = "写 syscfg.ini 失败"; return false; }
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format("清空相机 {0}（{1}）配置，原 SN={2}", cam.disc.Trim(), cam.mName, oldSn));
                return true;
            }
            lock (addedCamSync)
            {
                try { cam.Dispose(); } catch { }
                ListCam.Remove(cam);
                SaveAddedCamList();
                VAR.msg.AddMsg(Msg.EM_MSGTYPE.SYS, string.Format("删除相机 {0}（{1}，SN={2}）", cam.disc.Trim(), cam.mName, cam.mSerialNumber));
                return true;
            }
        }

        private static void SaveAddedCamList()
        {
            bool changed = false;
            var inf = new IniFile(SyscfgFile);
            string list = string.Join(",", ListCam.Where(c => c.UserAdded).Select(c => c.mName));
            inf.WriteString(AddedCamSection, "LIST", list, ref changed, true, SyscfgFile);
        }
    }
}

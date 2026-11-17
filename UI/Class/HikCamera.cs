using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Cognex.VisionPro;

namespace UI
{
    /// <summary>
    /// 海康 MVS 相机封装：直接 P/Invoke 调用 MVS 运行库自带的 MvCameraControl.dll（C 接口），
    /// 不需要在工程里引用 MvCamCtrl.Net.dll —— 没装 MVS 的电脑也能编译，只有真正打开海康相机时才需要机台装好 MVS 运行库。
    /// 按序列号打开 USB3/GigE 相机，软触发 / LineX 硬触发 / 连续采集（实况），每帧转成 VisionPro 的 CogImage8Grey 后通过 FrameArrived 抛出。
    /// FrameArrived、Disconnected 在 SDK 线程上触发，界面更新需要自己 BeginInvoke。
    /// </summary>
    public sealed class HikCamera : IDisposable
    {
        public sealed class DeviceEntry
        {
            public string SerialNumber;
            public string Model;
            public string UserName;
            public string Interface;
            public override string ToString()
            {
                return string.Format("{0}  {1}  {2}", SerialNumber, Model, Interface);
            }
        }

        // GenICam 枚举值（MVS CameraParams.h 中 MV_CAM_TRIGGER_SOURCE / MV_CAM_TRIGGER_MODE 的取值）
        public const uint TRIGGER_SOURCE_LINE0 = 0;
        public const uint TRIGGER_SOURCE_SOFTWARE = 7;
        private const uint TRIGGER_MODE_OFF = 0, TRIGGER_MODE_ON = 1;
        private const uint ACQ_MODE_CONTINUOUS = 2;
        private const uint EXPOSURE_AUTO_OFF = 0;
        private const uint PIXEL_MONO8 = 0x01080001;

        #region MvCameraControl.dll（C 接口）
        private const string Dll = "MvCameraControl.dll";
        private const int MV_OK = 0;
        private const uint MV_GIGE_DEVICE = 0x00000001;
        private const uint MV_USB_DEVICE = 0x00000004;
        private const uint MV_ACCESS_EXCLUSIVE = 1;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void FrameCallback(IntPtr data, IntPtr frameInfo, IntPtr user);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void ExceptionCallback(uint msgType, IntPtr user);

        [StructLayout(LayoutKind.Sequential)]
        private struct PixelConvertParam
        {
            public ushort nWidth;
            public ushort nHeight;
            public uint enSrcPixelType;
            public IntPtr pSrcData;
            public uint nSrcDataLen;
            public uint enDstPixelType;
            public IntPtr pDstBuffer;
            public uint nDstLen;
            public uint nDstBufferSize;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public uint[] nRes;
        }

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int MV_CC_EnumDevices(uint nTLayerType, IntPtr pstDevList);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int MV_CC_CreateHandle(ref IntPtr handle, IntPtr pstDevInfo);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int MV_CC_DestroyHandle(IntPtr handle);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int MV_CC_OpenDevice(IntPtr handle, uint nAccessMode, ushort nSwitchoverKey);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int MV_CC_CloseDevice(IntPtr handle);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int MV_CC_StartGrabbing(IntPtr handle);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int MV_CC_StopGrabbing(IntPtr handle);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int MV_CC_RegisterImageCallBackEx(IntPtr handle, FrameCallback cb, IntPtr user);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int MV_CC_RegisterExceptionCallBack(IntPtr handle, ExceptionCallback cb, IntPtr user);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int MV_CC_SetEnumValue(IntPtr handle, string key, uint value);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int MV_CC_SetEnumValueByString(IntPtr handle, string key, string value);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int MV_CC_SetIntValue(IntPtr handle, string key, uint value);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int MV_CC_SetFloatValue(IntPtr handle, string key, float value);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int MV_CC_SetBoolValue(IntPtr handle, string key, [MarshalAs(UnmanagedType.U1)] bool value);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int MV_CC_SetCommandValue(IntPtr handle, string key);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int MV_CC_ConvertPixelType(IntPtr handle, ref PixelConvertParam param);

        // 诊断用：读回相机当前参数
        [StructLayout(LayoutKind.Sequential)]
        private struct EnumValue
        {
            public uint nCurValue;
            public uint nSupportedNum;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public uint[] nSupportValue;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public uint[] nReserved;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct FloatValue
        {
            public float fCurValue;
            public float fMax;
            public float fMin;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public uint[] nReserved;
        }
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int MV_CC_GetEnumValue(IntPtr handle, string key, ref EnumValue value);
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int MV_CC_GetFloatValue(IntPtr handle, string key, ref FloatValue value);
        [StructLayout(LayoutKind.Sequential)]
        private struct IntValue
        {
            public uint nCurValue;
            public uint nMax;
            public uint nMin;
            public uint nInc;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public uint[] nReserved;
        }
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern int MV_CC_GetIntValue(IntPtr handle, string key, ref IntValue value);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
        private static extern void CopyMemory(IntPtr dest, IntPtr src, UIntPtr count);

        // MV_CC_DEVICE_INFO：nMajorVer(2) nMinorVer(2) nMacAddrHigh(4) nMacAddrLow(4) nTLayerType(4) nReserved[4](16)，联合体从 32 开始
        private const int DEV_TLAYER_OFFSET = 12;
        private const int DEV_SPECIAL_OFFSET = 32;
        // MV_GIGE_DEVICE_INFO：5 个 uint(20) 后 厂商[32] 型号[32] 版本[32] 厂商信息[48] 序列号[16] 自定义名[16]
        private const int GIGE_MODEL = 52, GIGE_SN = 164, GIGE_USER = 180;
        // MV_USB3_DEVICE_INFO：4 字节端点 + vid/pid(4) + nDeviceNumber(4) 后 GUID[64] 厂商[64] 型号[64] 系列[64] 版本[64] 制造商[64] 序列号[64] 自定义名[64]
        private const int USB_MODEL = 140, USB_SN = 396, USB_USER = 460;
        // MV_FRAME_OUT_INFO_EX：nWidth(2) nHeight(2) enPixelType(4) nFrameNum(4) nDevTimeStampHigh(4) nDevTimeStampLow(4) nReserved0(4) nHostTimeStamp(8) nFrameLen(4)
        private const int FI_WIDTH = 0, FI_HEIGHT = 2, FI_PIXEL = 4, FI_FRAMENUM = 8, FI_TS_HIGH = 12, FI_TS_LOW = 16, FI_FRAMELEN = 32;
        #endregion

        private static readonly object SdkSync = new object();
        private readonly object sync = new object();          // 只保护打开/关闭
        private readonly object convertSync = new object();   // 只保护像素转换缓冲区
        private IntPtr handle = IntPtr.Zero;
        private FrameCallback frameCallback;                  // 必须存在字段里，防止被 GC 回收
        private ExceptionCallback exceptionCallback;
        private IntPtr convertBuffer = IntPtr.Zero;
        private int convertBufferSize;
        private volatile bool grabbing;

        public string SerialNumber { get; private set; }
        public string Model { get; private set; }
        public bool IsOpen { get { return handle != IntPtr.Zero; } }
        /// <summary>是否处于采集状态（StartGrabbing 成功）。</summary>
        public bool IsGrabbing { get { return grabbing; } }
        private int framesReceived, framesDropped;
        // 飞拍期间的轻量观察者：仅向内存缓冲写记录，不在 SDK 回调里写盘或读控制器。
        internal Action<string> FrameDiagnostic;
        private void TraceFrame(string stage, int frame, long timestamp)
        {
            Action<string> observer = FrameDiagnostic;
            if (observer == null) return;
            try { observer(string.Format("SDK {0} 帧={1} 设备时间={2} 收帧累计={3} 转换丢弃累计={4}", stage, frame, timestamp, FramesReceived, FramesDropped)); }
            catch { } // 诊断不可影响取图。
        }
        /// <summary>SDK 回调进来的帧数（含实况帧），诊断用。</summary>
        public int FramesReceived { get { return framesReceived; } }
        /// <summary>回调进来但没能转换成图像（尺寸为 0 / 像素转换失败）的帧数，诊断用。</summary>
        public int FramesDropped { get { return framesDropped; } }

        /// <summary>读回相机当前的触发/曝光相关参数，写日志用。</summary>
        public string DumpState(bool includeTiming = false)
        {
            IntPtr h = handle;
            if (h == IntPtr.Zero) return "相机未打开";
            var sb = new StringBuilder();
            sb.Append("采集中=").Append(grabbing ? "是" : "否");
            sb.Append(" TriggerMode=").Append(ReadEnum(h, "TriggerMode", v => v == TRIGGER_MODE_ON ? "On" : v == TRIGGER_MODE_OFF ? "Off" : v.ToString()));
            sb.Append(" TriggerSource=").Append(ReadEnum(h, "TriggerSource", v => v == TRIGGER_SOURCE_SOFTWARE ? "Software" : v <= 3 ? "Line" + v : v.ToString()));
            sb.Append(" TriggerActivation=").Append(ReadEnum(h, "TriggerActivation", v => v == 0 ? "上升沿" : v == 1 ? "下降沿" : v.ToString()));
            sb.Append(" ExposureTime=").Append(ReadFloat(h, "ExposureTime", "us"));
            sb.Append(" Gain=").Append(ReadFloat(h, "Gain", "dB"));
            sb.Append(" TriggerDelay=").Append(ReadFloat(h, "TriggerDelay", "us"));
            sb.Append(" PixelFormat=").Append(ReadEnum(h, "PixelFormat", v => "0x" + v.ToString("X8")));
            sb.Append(" 收到帧=").Append(framesReceived).Append(" 丢弃帧=").Append(framesDropped);
            if (includeTiming)
            {
                sb.Append(" ExposureAuto=").Append(ReadEnum(h, "ExposureAuto", v => v.ToString()));
                sb.Append(" GainAuto=").Append(ReadEnum(h, "GainAuto", v => v.ToString()));
                sb.Append(" BlackLevel=").Append(ReadFloat(h, "BlackLevel", ""));
                sb.Append(" AcquisitionFrameRate=").Append(ReadFloat(h, "AcquisitionFrameRate", "Hz"));
            }
            return sb.ToString();
        }

        private static string ReadEnum(IntPtr h, string key, Func<uint, string> format)
        {
            var v = new EnumValue { nSupportValue = new uint[64], nReserved = new uint[4] };
            int r = MV_CC_GetEnumValue(h, key, ref v);
            return r == MV_OK ? format(v.nCurValue) : string.Format("读失败0x{0:X8}", r);
        }

        private static string ReadFloat(IntPtr h, string key, string unit)
        {
            var v = new FloatValue { nReserved = new uint[4] };
            int r = MV_CC_GetFloatValue(h, key, ref v);
            return r == MV_OK ? v.fCurValue.ToString("0.###") + unit : string.Format("读失败0x{0:X8}", r);
        }

        /// <summary>抽样计算平均灰度和最大灰度（诊断用，判断是不是全黑图）。</summary>
        public static string GrayStats(CogImage8Grey image)
        {
            if (image == null) return "图像为空";
            int w = image.Width, h = image.Height;
            ICogImage8PixelMemory mem = image.Get8GreyPixelMemory(CogImageDataModeConstants.Read, 0, 0, w, h);
            try
            {
                long sum = 0; int n = 0, max = 0;
                int stepX = Math.Max(1, w / 64), stepY = Math.Max(1, h / 64);
                for (int y = 0; y < h; y += stepY)
                    for (int x = 0; x < w; x += stepX)
                    {
                        int v = Marshal.ReadByte(mem.Scan0, y * mem.Stride + x);
                        sum += v; n++;
                        if (v > max) max = v;
                    }
                return string.Format("{0}x{1} 平均灰度={2:0.0} 最大灰度={3}", w, h, n > 0 ? (double)sum / n : 0, max);
            }
            finally
            {
                var disposable = mem as IDisposable;
                if (disposable != null) disposable.Dispose();
            }
        }
        /// <summary>最近一次设置中不影响使用的警告（如触发沿设置失败），没有为 null。</summary>
        public string LastWarning { get; private set; }

        /// <summary>新的一帧（SDK 线程）。参数：图像、帧号、设备时间戳。</summary>
        public event Action<CogImage8Grey, int, long> FrameArrived;
        /// <summary>相机掉线（SDK 线程）。</summary>
        public event Action<string> Disconnected;

        #region 枚举
        /// <summary>枚举当前电脑上能看到的海康相机（USB3 + GigE）。</summary>
        public static List<DeviceEntry> Enumerate()
        {
            var result = new List<DeviceEntry>();
            ForEachDevice((info, entry) => { result.Add(entry); return false; });
            return result;
        }

        /// <summary>枚举设备，对每台调用 visit(设备信息指针, 描述)；visit 返回 true 时停止。设备信息指针只在回调内有效。</summary>
        private static void ForEachDevice(Func<IntPtr, DeviceEntry, bool> visit)
        {
            lock (SdkSync)
            {
                // MV_CC_DEVICE_INFO_LIST：uint nDeviceNum + MV_CC_DEVICE_INFO* pDeviceInfo[256]（64 位下指针数组从偏移 8 开始）
                int ptrOffset = IntPtr.Size == 8 ? 8 : 4;
                IntPtr list = Marshal.AllocHGlobal(ptrOffset + 256 * IntPtr.Size + 64);
                try
                {
                    for (int i = 0; i < ptrOffset + 256 * IntPtr.Size + 64; i++) Marshal.WriteByte(list, i, 0);
                    int ret = MV_CC_EnumDevices(MV_GIGE_DEVICE | MV_USB_DEVICE, list);
                    if (ret != MV_OK) throw new InvalidOperationException(string.Format("枚举海康相机失败，错误码 0x{0:X8}", ret));
                    int count = Math.Min(256, Marshal.ReadInt32(list, 0));
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr info = Marshal.ReadIntPtr(list, ptrOffset + i * IntPtr.Size);
                        if (info == IntPtr.Zero) continue;
                        DeviceEntry entry = Describe(info);
                        if (entry != null && visit(info, entry)) return;
                    }
                }
                finally { Marshal.FreeHGlobal(list); }
            }
        }

        private static DeviceEntry Describe(IntPtr info)
        {
            uint layer = (uint)Marshal.ReadInt32(info, DEV_TLAYER_OFFSET);
            IntPtr special = info + DEV_SPECIAL_OFFSET;
            if (layer == MV_USB_DEVICE)
                return new DeviceEntry { SerialNumber = ReadText(special, USB_SN, 64), Model = ReadText(special, USB_MODEL, 64), UserName = ReadText(special, USB_USER, 64), Interface = "USB3" };
            if (layer == MV_GIGE_DEVICE)
                return new DeviceEntry { SerialNumber = ReadText(special, GIGE_SN, 16), Model = ReadText(special, GIGE_MODEL, 32), UserName = ReadText(special, GIGE_USER, 16), Interface = "GigE" };
            return null;
        }

        private static string ReadText(IntPtr basePtr, int offset, int length)
        {
            var bytes = new byte[length];
            Marshal.Copy(basePtr + offset, bytes, 0, length);
            int end = Array.IndexOf(bytes, (byte)0);
            if (end < 0) end = length;
            return Encoding.Default.GetString(bytes, 0, end).Trim();
        }

        private static string Clean(string s)
        {
            return (s ?? "").Trim('\0', ' ');
        }
        #endregion

        #region 打开 / 关闭
        /// <summary>按序列号打开相机，并进入“触发模式 + 已开始采集”状态（未触发不会出图）。</summary>
        public bool Open(string serialNumber, uint triggerSource, bool risingEdge, out string error)
        {
            error = null;
            serialNumber = Clean(serialNumber);
            if (serialNumber.Length == 0) { error = "序列号为空"; return false; }
            lock (sync)
            {
                if (handle != IntPtr.Zero && SerialNumber == serialNumber) return true;
                CloseCore();

                IntPtr h = IntPtr.Zero;
                string model = null, openError = null;
                ForEachDevice((info, entry) =>
                {
                    if (!string.Equals(entry.SerialNumber, serialNumber, StringComparison.OrdinalIgnoreCase)) return false;
                    int r = MV_CC_CreateHandle(ref h, info);
                    if (r != MV_OK) { openError = string.Format("创建句柄失败 0x{0:X8}", r); h = IntPtr.Zero; return true; }
                    r = MV_CC_OpenDevice(h, MV_ACCESS_EXCLUSIVE, 0);
                    if (r != MV_OK)
                    {
                        MV_CC_DestroyHandle(h);
                        h = IntPtr.Zero;
                        openError = string.Format("打开失败 0x{0:X8}（可能已被 MVS 客户端或其他程序占用）", r);
                        return true;
                    }
                    model = entry.Model;
                    return true;
                });
                if (h == IntPtr.Zero)
                {
                    error = openError ?? "未找到序列号为 " + serialNumber + " 的海康相机";
                    return false;
                }
                handle = h;
                SerialNumber = serialNumber;
                Model = model;

                frameCallback = OnFrame;
                exceptionCallback = OnException;
                LastWarning = null;
                Check(MV_CC_RegisterImageCallBackEx(handle, frameCallback, IntPtr.Zero), "注册取图回调", ref error);
                string warn = null;
                Check(MV_CC_RegisterExceptionCallBack(handle, exceptionCallback, IntPtr.Zero), "注册掉线回调", ref warn);
                if (warn != null) LastWarning = warn;
                MV_CC_SetEnumValue(handle, "PixelFormat", PIXEL_MONO8);          // 黑白相机直接出 Mono8；彩色相机不支持时在回调里转换
                MV_CC_SetEnumValue(handle, "AcquisitionMode", ACQ_MODE_CONTINUOUS);
                MV_CC_SetEnumValue(handle, "ExposureAuto", EXPOSURE_AUTO_OFF);
                lastBrightness = double.NaN;
                lastContrast = double.NaN;
                if (!SetTriggerCore(true, triggerSource, risingEdge, ref error) || error != null)
                {
                    CloseCore();
                    return false;
                }
                int ret = MV_CC_StartGrabbing(handle);
                if (ret != MV_OK)
                {
                    error = string.Format("开始采集失败 0x{0:X8}", ret);
                    CloseCore();
                    return false;
                }
                grabbing = true;
                return true;
            }
        }

        public void Close()
        {
            lock (sync) CloseCore();
        }

        private void CloseCore()
        {
            IntPtr h = handle;
            handle = IntPtr.Zero;
            if (h != IntPtr.Zero)
            {
                try
                {
                    if (grabbing) MV_CC_StopGrabbing(h);
                    MV_CC_CloseDevice(h);
                    MV_CC_DestroyHandle(h);
                }
                catch { }
            }
            grabbing = false;
            lock (convertSync)
            {
                if (convertBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(convertBuffer);
                    convertBuffer = IntPtr.Zero;
                    convertBufferSize = 0;
                }
            }
        }

        public void Dispose()
        {
            Close();
        }
        #endregion

        #region 参数 / 触发
        /// <summary>
        /// on=true：触发模式，triggerSource=TRIGGER_SOURCE_SOFTWARE 为软触发，0~3 为 Line0~Line3 硬触发；
        /// on=false：连续采集（实况用）。
        /// </summary>
        public bool SetTrigger(bool on, uint triggerSource, bool risingEdge, out string error)
        {
            error = null;
            if (handle == IntPtr.Zero) { error = "相机未打开"; return false; }
            return SetTriggerCore(on, triggerSource, risingEdge, ref error) && error == null;
        }

        // 参数设置不加 sync 锁：取图回调里可能会再触发（连续触发），而关闭时 StopGrabbing 会等回调结束，加锁会互锁。
        private bool SetTriggerCore(bool on, uint triggerSource, bool risingEdge, ref string error)
        {
            IntPtr h = handle;
            if (h == IntPtr.Zero) { if (error == null) error = "相机未打开"; return false; }
            if (!Check(MV_CC_SetEnumValue(h, "TriggerMode", on ? TRIGGER_MODE_ON : TRIGGER_MODE_OFF), "设置触发模式", ref error)) return false;
            if (!on) return true;
            if (!Check(MV_CC_SetEnumValue(h, "TriggerSource", triggerSource), "设置触发源", ref error)) return false;
            if (triggerSource != TRIGGER_SOURCE_SOFTWARE)
            {
                // 触发沿设置失败不影响连接（相机保持原触发沿），只记警告
                string warn = null;
                if (!Check(MV_CC_SetEnumValue(h, "TriggerActivation", risingEdge ? 0u : 1u), "设置触发沿", ref warn))
                    LastWarning = warn;
            }
            return true;
        }

        public bool SoftwareTrigger(out string error)
        {
            error = null;
            IntPtr h = handle;
            if (h == IntPtr.Zero) { error = "相机未打开"; return false; }
            return Check(MV_CC_SetCommandValue(h, "TriggerSoftware"), "软触发", ref error);
        }

        /// <summary>曝光，单位 ms（与 VisionPro 曝光参数一致），内部换算成 µs。</summary>
        public bool SetExposureMs(double ms, out string error)
        {
            error = null;
            IntPtr h = handle;
            if (h == IntPtr.Zero) { error = "相机未打开"; return false; }
            return Check(MV_CC_SetFloatValue(h, "ExposureTime", (float)(ms * 1000.0)), "设置曝光", ref error);
        }

        /// <summary>
        /// 曝光输出（Strobe）：曝光期间在指定输出线上给出信号，驱动光源控制器点亮光源。
        /// 等同原巴斯勒初始化里的 Out1 = ExposureActive。line 如 "Line1"（海康 USB3 光耦输出）/"Line2"。
        /// </summary>
        public bool ConfigureStrobe(string line, out string error)
        {
            error = null;
            lock (sync)
            {
                IntPtr h = handle;
                if (h == IntPtr.Zero) { error = "相机未打开"; return false; }
                // 部分 IO 参数采集中被锁定，先停采集再设置
                bool wasGrabbing = grabbing;
                if (wasGrabbing) { MV_CC_StopGrabbing(h); grabbing = false; }
                bool ok = ConfigureStrobeCore(h, line, ref error);
                if (wasGrabbing)
                {
                    int r = MV_CC_StartGrabbing(h);
                    if (r == MV_OK) grabbing = true;
                    else if (error == null) error = string.Format("重新开始采集失败 0x{0:X8}", r);
                }
                return ok && grabbing == wasGrabbing;
            }
        }

        private static bool ConfigureStrobeCore(IntPtr h, string line, ref string error)
        {
            if (!Check(MV_CC_SetEnumValueByString(h, "LineSelector", line), "选择输出线 " + line, ref error)) return false;
            // Line1 在部分型号上固定为输出（Strobe），设置 LineMode 失败不算错误；Line2 双向口必须设成 Strobe
            string modeError = null;
            Check(MV_CC_SetEnumValueByString(h, "LineMode", "Strobe"), "设置 " + line + " 为输出", ref modeError);
            MV_CC_SetBoolValue(h, "LineInverter", false);
            if (!Check(MV_CC_SetEnumValueByString(h, "LineSource", "ExposureStartActive"), "设置输出源为曝光", ref error))
            {
                if (modeError != null) error = modeError + "；" + error;
                return false;
            }
            MV_CC_SetIntValue(h, "StrobeLineDuration", 0);   // 0 = 输出宽度跟随曝光时间
            MV_CC_SetIntValue(h, "StrobeLineDelay", 0);
            MV_CC_SetIntValue(h, "StrobeLinePreDelay", 0);
            return Check(MV_CC_SetBoolValue(h, "StrobeEnable", true), "使能曝光输出", ref error);
        }

        #region 亮度/对比度（模仿 VisionPro GigE 驱动：亮度 0~1 → 黑电平 BlackLevel，对比度 0~1 → 增益 Gain，按相机取值范围线性换算）
        private double lastBrightness = double.NaN, lastContrast = double.NaN;

        /// <summary>读相机当前黑电平/增益，换算成 0~1 的亮度/对比度（迁移旧任务时用，保证切换后图像不变）。</summary>
        public bool GetBrightnessContrast(out double brightness, out double contrast, out string detail)
        {
            brightness = double.NaN; contrast = double.NaN; detail = "";
            IntPtr h = handle;
            if (h == IntPtr.Zero) { detail = "相机未打开"; return false; }
            var bl = new IntValue { nReserved = new uint[4] };
            int r1 = MV_CC_GetIntValue(h, "BlackLevel", ref bl);
            if (r1 == MV_OK && bl.nMax > bl.nMin)
                brightness = (bl.nCurValue - (double)bl.nMin) / ((double)bl.nMax - bl.nMin);
            var g = new FloatValue { nReserved = new uint[4] };
            int r2 = MV_CC_GetFloatValue(h, "Gain", ref g);
            if (r2 == MV_OK && g.fMax > g.fMin)
                contrast = (g.fCurValue - (double)g.fMin) / ((double)g.fMax - g.fMin);
            detail = string.Format("BlackLevel={0}(范围{1}~{2}){3} Gain={4:0.###}dB(范围{5:0.###}~{6:0.###}){7}",
                r1 == MV_OK ? bl.nCurValue.ToString() : "-", bl.nMin, bl.nMax, r1 == MV_OK ? "" : string.Format("读失败0x{0:X8}", r1),
                r2 == MV_OK ? g.fCurValue : float.NaN, g.fMin, g.fMax, r2 == MV_OK ? "" : string.Format("读失败0x{0:X8}", r2));
            return !double.IsNaN(brightness) || !double.IsNaN(contrast);
        }

        /// <summary>
        /// 下发亮度/对比度（0~1）。与上次相同则不重复下发。applied 返回实际设置的黑电平/增益，error 返回失败原因。
        /// </summary>
        public bool SetBrightnessContrast(double brightness, double contrast, out string applied, out string error)
        {
            applied = null; error = null;
            IntPtr h = handle;
            if (h == IntPtr.Zero) { error = "相机未打开"; return false; }
            bool ok = true;
            var sb = new StringBuilder();
            if (brightness >= 0 && brightness <= 1 && !(Math.Abs(brightness - lastBrightness) < 1e-9))
            {
                MV_CC_SetBoolValue(h, "BlackLevelEnable", true);   // 部分型号要先使能黑电平；不支持时忽略
                var bl = new IntValue { nReserved = new uint[4] };
                int r = MV_CC_GetIntValue(h, "BlackLevel", ref bl);
                if (r == MV_OK && bl.nMax >= bl.nMin)
                {
                    double v = bl.nMin + brightness * ((double)bl.nMax - bl.nMin);
                    uint inc = bl.nInc == 0 ? 1 : bl.nInc;
                    uint target = bl.nMin + (uint)Math.Round((v - bl.nMin) / inc) * inc;
                    if (target > bl.nMax) target = bl.nMax;
                    string e = null;
                    if (Check(MV_CC_SetIntValue(h, "BlackLevel", target), "设置黑电平", ref e))
                    {
                        lastBrightness = brightness;
                        sb.AppendFormat("BlackLevel={0}(范围{1}~{2}) ", target, bl.nMin, bl.nMax);
                    }
                    else { ok = false; error = e; }
                }
                else { ok = false; error = string.Format("读取黑电平范围失败0x{0:X8}", r); }
            }
            if (contrast >= 0 && contrast <= 1 && !(Math.Abs(contrast - lastContrast) < 1e-9))
            {
                MV_CC_SetEnumValue(h, "GainAuto", 0);   // 关闭自动增益，避免和软件下发的值打架
                var g = new FloatValue { nReserved = new uint[4] };
                int r = MV_CC_GetFloatValue(h, "Gain", ref g);
                if (r == MV_OK && g.fMax >= g.fMin)
                {
                    float target = (float)(g.fMin + contrast * ((double)g.fMax - g.fMin));
                    string e = null;
                    if (Check(MV_CC_SetFloatValue(h, "Gain", target), "设置增益", ref e))
                    {
                        lastContrast = contrast;
                        sb.AppendFormat("Gain={0:0.###}dB(范围{1:0.###}~{2:0.###})", target, g.fMin, g.fMax);
                    }
                    else { ok = false; error = (error == null ? "" : error + "；") + e; }
                }
                else { ok = false; error = (error == null ? "" : error + "；") + string.Format("读取增益范围失败0x{0:X8}", r); }
            }
            applied = sb.Length > 0 ? sb.ToString().Trim() : null;
            return ok;
        }
        #endregion

        public void SetReverseX(bool on)
        {
            IntPtr h = handle;
            if (h != IntPtr.Zero) MV_CC_SetBoolValue(h, "ReverseX", on);
        }

        private static bool Check(int ret, string what, ref string error)
        {
            if (ret == MV_OK) return true;
            if (error == null) error = string.Format("{0}失败 0x{1:X8}", what, ret);
            return false;
        }
        #endregion

        #region 回调
        private void OnFrame(IntPtr data, IntPtr frameInfo, IntPtr user)
        {
            try
            {
                System.Threading.Interlocked.Increment(ref framesReceived);
                Action<CogImage8Grey, int, long> handler = FrameArrived;
                if (handler == null || data == IntPtr.Zero || frameInfo == IntPtr.Zero) { System.Threading.Interlocked.Increment(ref framesDropped); TraceFrame("丢弃:空指针或无订阅", -1, 0); return; }
                int width = (ushort)Marshal.ReadInt16(frameInfo, FI_WIDTH);
                int height = (ushort)Marshal.ReadInt16(frameInfo, FI_HEIGHT);
                uint pixel = (uint)Marshal.ReadInt32(frameInfo, FI_PIXEL);
                int frameNum = Marshal.ReadInt32(frameInfo, FI_FRAMENUM);
                long ts = ((long)(uint)Marshal.ReadInt32(frameInfo, FI_TS_HIGH) << 32) | (uint)Marshal.ReadInt32(frameInfo, FI_TS_LOW);
                uint frameLen = (uint)Marshal.ReadInt32(frameInfo, FI_FRAMELEN);
                TraceFrame("进入回调", frameNum, ts);
                if (width <= 0 || height <= 0) { System.Threading.Interlocked.Increment(ref framesDropped); TraceFrame("丢弃:无效尺寸", frameNum, ts); return; }

                CogImage8Grey image;
                if (pixel == PIXEL_MONO8)
                    image = ToCogImage(data, width, height);
                else
                {
                    IntPtr h = handle;
                    if (h == IntPtr.Zero) { TraceFrame("丢弃:句柄关闭", frameNum, ts); return; }
                    lock (convertSync)
                    {
                        int need = width * height;
                        if (convertBufferSize < need)
                        {
                            if (convertBuffer != IntPtr.Zero) Marshal.FreeHGlobal(convertBuffer);
                            convertBuffer = Marshal.AllocHGlobal(need);
                            convertBufferSize = need;
                        }
                        var p = new PixelConvertParam
                        {
                            nWidth = (ushort)width,
                            nHeight = (ushort)height,
                            enSrcPixelType = pixel,
                            pSrcData = data,
                            nSrcDataLen = frameLen,
                            enDstPixelType = PIXEL_MONO8,
                            pDstBuffer = convertBuffer,
                            nDstBufferSize = (uint)convertBufferSize,
                            nRes = new uint[4],
                        };
                        int convertRc = MV_CC_ConvertPixelType(h, ref p);
                        if (convertRc != MV_OK) { System.Threading.Interlocked.Increment(ref framesDropped); TraceFrame("丢弃:像素转换 RC=" + convertRc, frameNum, ts); return; }
                        image = ToCogImage(convertBuffer, width, height);
                    }
                }
                // 在锁外回调，回调里可以再次软触发
                TraceFrame("转换完成", frameNum, ts);
                handler(image, frameNum, ts);
            }
            catch (Exception ex)
            {
                TraceFrame("回调异常:" + ex.Message, -1, 0);
                MotionCtrl.VAR.msg.AddMsg(MotionCtrl.Msg.EM_MSGTYPE.ERR, string.Format("海康相机 {0} 取图回调异常：{1}", SerialNumber, ex.Message));
            }
        }

        private static CogImage8Grey ToCogImage(IntPtr src, int width, int height)
        {
            var image = new CogImage8Grey(width, height);
            ICogImage8PixelMemory mem = image.Get8GreyPixelMemory(CogImageDataModeConstants.Write, 0, 0, width, height);
            try
            {
                int stride = mem.Stride;
                if (stride == width)
                    CopyMemory(mem.Scan0, src, (UIntPtr)(uint)(width * height));
                else
                    for (int row = 0; row < height; row++)
                        CopyMemory(mem.Scan0 + row * stride, src + row * width, (UIntPtr)(uint)width);
            }
            finally
            {
                var disposable = mem as IDisposable;
                if (disposable != null) disposable.Dispose();
            }
            return image;
        }

        private void OnException(uint msgType, IntPtr user)
        {
            // 0x00008001 = MV_EXCEPTION_DEV_DISCONNECT
            Action<string> handler = Disconnected;
            if (handler != null) handler(string.Format("海康相机 {0} 异常/掉线（0x{1:X8}）", SerialNumber, msgType));
        }
        #endregion
    }
}

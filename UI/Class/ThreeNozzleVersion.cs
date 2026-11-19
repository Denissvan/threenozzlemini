using System.Linq;

namespace UI
{
    /// <summary>
    /// 三吸嘴改动版本号。每次改动在新增日志前加“[Rn]”，开机和每次上下料线程开始时打印当前版本，
    /// 方便在日志里核对现场程序编进了哪些改动。新增改动时：Current 加 1，Changes 末尾加一行，相关日志带上同一个标记。
    /// </summary>
    public static class ThreeNozzleVersion
    {
        public const string Current = "R10";

        /// <summary>版本 → 提交号与内容。</summary>
        public static readonly string[] Changes =
        {
            "R1 a5af6e9 回检短助跑/进夹具按取料位X飞/停车列来源/取放计时",
            "R2 f2151dd 最后一组离开夹具不回X=0/料盘预定位时臂先走",
            "R3 de724cc 供料盘飞拍短助跑",
            "R4 2eeb8b5 回检直接飞到落料位/满盘立即后台换盘",
            "R5 85fb1ff 停车限位只算对臂负责的穴/X-Y合并起飞按加速段估算",
            "R6 c65b93a 飞拍丢帧不死等3秒/料盘取料收臂X",
            "R7 0fe2593 供料盘飞拍短收尾",
            "R8 版本号标记",
            "R9 模块1先行(模块2取料进度不超过模块1)/取放计时加U轴与轴参数",
            "R10 主界面版本号显示三吸嘴版本",
        };

        public static string Banner
        {
            get { return "三吸嘴程序版本 " + Current + "（" + string.Join("；", Changes) + "）"; }
        }
    }
}

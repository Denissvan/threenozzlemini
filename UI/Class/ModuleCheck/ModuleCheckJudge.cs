using System;
using System.Collections.Generic;
using System.Linq;

namespace UI.ModuleCheck
{
    /// <summary>
    /// 一轮结束后的判定。唯一依据是 dllforcomv8 sta==0 时返回的逐工位结果码：
    /// 全部为 0 → 通过；任一非 0（289 或其它 NG 码，一律同样处理）→ 换料重测；
    /// 已到总轮数上限仍有非 0 → 超过重试次数失败。
    /// 有样品的工位没有结果（-1/缺失）属于通讯/测试异常，判为“结果不完整”，不计轮次。
    /// </summary>
    public static class ModuleCheckJudge
    {
        public const int CodeNoResult = -1;
        public const int CodeEmpty = -2;

        public class JudgeOutput
        {
            public ModuleCheckVerdict Verdict = ModuleCheckVerdict.Pending;
            public string NgSummary = "";
            public List<int> MissingPockets = new List<int>();
            /// <summary>无样品的穴却收到了非空结果（可能映射错位）</summary>
            public List<int> UnexpectedPockets = new List<int>();
        }

        /// <param name="mapping">本轮 穴位→样品</param>
        /// <param name="codes">穴位(1~16)→结果码</param>
        public static JudgeOutput Judge(Dictionary<int, string> mapping, Dictionary<int, int> codes, int roundNo, int maxRounds)
        {
            var o = new JudgeOutput();
            var ng = new List<string>();
            foreach (var kv in mapping.OrderBy(k => k.Key))
            {
                int code;
                if (!codes.TryGetValue(kv.Key, out code) || code == CodeNoResult || code == CodeEmpty)
                {
                    o.MissingPockets.Add(kv.Key);
                    continue;
                }
                if (code != 0) ng.Add(string.Format("穴{0}({1}):{2}", kv.Key, kv.Value, code));
            }
            foreach (var kv in codes)
            {
                if (!mapping.ContainsKey(kv.Key) && kv.Value != CodeNoResult && kv.Value != CodeEmpty)
                    o.UnexpectedPockets.Add(kv.Key);
            }
            o.NgSummary = string.Join(",", ng);

            if (o.MissingPockets.Count > 0) o.Verdict = ModuleCheckVerdict.Incomplete;
            else if (ng.Count == 0) o.Verdict = ModuleCheckVerdict.Pass;
            else if (roundNo >= maxRounds) o.Verdict = ModuleCheckVerdict.FailOverLimit;
            else o.Verdict = ModuleCheckVerdict.Retest;
            return o;
        }
    }
}

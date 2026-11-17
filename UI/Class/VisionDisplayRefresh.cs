using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UI
{
    /// <summary>
    /// 切页 / 切标签后强制重画 VisionPro 显示控件（CogRecordDisplay，ActiveX）。
    /// 这类控件刚变为可见时经常不重画自己，窗口区域里就会留下上一个页面的图像。
    /// 做法：等本次切换的消息处理完（BeginInvoke）再重画一次，150ms 后再补一次。
    /// </summary>
    public static class VisionDisplayRefresh
    {
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;
        private const uint RDW_INVALIDATE = 0x0001, RDW_INTERNALPAINT = 0x0002, RDW_ERASE = 0x0004, RDW_ALLCHILDREN = 0x0080, RDW_UPDATENOW = 0x0100, RDW_FRAME = 0x0400;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        /// <summary>稍后重画 root 下所有可见的 VisionPro 显示控件。</summary>
        public static void RefreshLater(Control root)
        {
            if (root == null || root.IsDisposed || !root.IsHandleCreated) return;
            try
            {
                root.BeginInvoke(new Action(() => RefreshNow(root)));
                var timer = new Timer { Interval = 150 };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    RefreshNow(root);
                };
                timer.Start();
            }
            catch { }
        }

        public static void RefreshNow(Control root)
        {
            if (root == null || root.IsDisposed || !root.Visible) return;
            foreach (Control c in root.Controls)
            {
                if (!c.Visible) continue;
                if (c is Cognex.VisionPro.CogRecordDisplay || c is AxHost)
                    Repaint(c);
                else if (c.HasChildren)
                    RefreshNow(c);
            }
        }

        private static void Repaint(Control c)
        {
            try
            {
                if (!c.IsHandleCreated) return;
                SetWindowPos(c.Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                RedrawWindow(c.Handle, IntPtr.Zero, IntPtr.Zero,
                    RDW_INVALIDATE | RDW_INTERNALPAINT | RDW_ERASE | RDW_FRAME | RDW_ALLCHILDREN | RDW_UPDATENOW);
                c.Invalidate(true);
                c.Update();
            }
            catch { }
        }
    }
}

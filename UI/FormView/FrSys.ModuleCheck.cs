using System;

namespace UI
{
    public partial class FrSys
    {
        /// <summary>系统设置 → 自动点检面板 → “模组自动点检设置”按钮</summary>
        private void btnModuleCheckSet_Click(object sender, EventArgs e)
        {
            FrModuleCheckSet.ShowSingle(this);
        }
    }
}

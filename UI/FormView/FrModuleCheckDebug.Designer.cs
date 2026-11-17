namespace UI
{
    partial class FrModuleCheckDebug
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.grp_target = new System.Windows.Forms.GroupBox();
            this.btn_dsaveip = new System.Windows.Forms.Button();
            this.btn_dautoip = new System.Windows.Forms.Button();
            this.btn_dautoall = new System.Windows.Forms.Button();
            this.txt_dip = new System.Windows.Forms.TextBox();
            this.lbl_dip = new System.Windows.Forms.Label();
            this.lbl_dhint = new System.Windows.Forms.Label();
            this.cmb_dpc = new System.Windows.Forms.ComboBox();
            this.lbl_dpc = new System.Windows.Forms.Label();
            this.cmb_dstation = new System.Windows.Forms.ComboBox();
            this.lbl_dstation = new System.Windows.Forms.Label();
            this.grp_mode = new System.Windows.Forms.GroupBox();
            this.num_dwait = new System.Windows.Forms.NumericUpDown();
            this.lbl_dwait = new System.Windows.Forms.Label();
            this.btn_dsetwait = new System.Windows.Forms.Button();
            this.btn_dset = new System.Windows.Forms.Button();
            this.btn_dget = new System.Windows.Forms.Button();
            this.num_dtemp = new System.Windows.Forms.NumericUpDown();
            this.lbl_dtemp = new System.Windows.Forms.Label();
            this.cmb_dmode = new System.Windows.Forms.ComboBox();
            this.lbl_dmode = new System.Windows.Forms.Label();
            this.grp_link = new System.Windows.Forms.GroupBox();
            this.btn_dstatus = new System.Windows.Forms.Button();
            this.btn_dinfo = new System.Windows.Forms.Button();
            this.txt_dlog = new System.Windows.Forms.TextBox();
            this.btn_dclear = new System.Windows.Forms.Button();
            this.btn_dclose = new System.Windows.Forms.Button();
            this.grp_target.SuspendLayout();
            this.grp_mode.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.num_dwait)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_dtemp)).BeginInit();
            this.grp_link.SuspendLayout();
            this.SuspendLayout();
            //
            // grp_target
            //
            this.grp_target.Controls.Add(this.btn_dautoall);
            this.grp_target.Controls.Add(this.btn_dautoip);
            this.grp_target.Controls.Add(this.btn_dsaveip);
            this.grp_target.Controls.Add(this.txt_dip);
            this.grp_target.Controls.Add(this.lbl_dip);
            this.grp_target.Controls.Add(this.lbl_dhint);
            this.grp_target.Controls.Add(this.cmb_dpc);
            this.grp_target.Controls.Add(this.lbl_dpc);
            this.grp_target.Controls.Add(this.cmb_dstation);
            this.grp_target.Controls.Add(this.lbl_dstation);
            this.grp_target.Location = new System.Drawing.Point(12, 12);
            this.grp_target.Name = "grp_target";
            this.grp_target.Size = new System.Drawing.Size(736, 96);
            this.grp_target.TabIndex = 0;
            this.grp_target.TabStop = false;
            this.grp_target.Text = "目标电脑";
            //
            // lbl_dstation
            //
            this.lbl_dstation.AutoSize = true;
            this.lbl_dstation.Location = new System.Drawing.Point(16, 30);
            this.lbl_dstation.Name = "lbl_dstation";
            this.lbl_dstation.Size = new System.Drawing.Size(39, 20);
            this.lbl_dstation.TabIndex = 0;
            this.lbl_dstation.Text = "工站";
            //
            // cmb_dstation
            //
            this.cmb_dstation.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmb_dstation.FormattingEnabled = true;
            this.cmb_dstation.Items.AddRange(new object[] {
            "工站1",
            "工站2",
            "工站3",
            "工站4"});
            this.cmb_dstation.Location = new System.Drawing.Point(70, 26);
            this.cmb_dstation.Name = "cmb_dstation";
            this.cmb_dstation.Size = new System.Drawing.Size(100, 28);
            this.cmb_dstation.TabIndex = 1;
            this.cmb_dstation.SelectedIndexChanged += new System.EventHandler(this.cmb_dstation_SelectedIndexChanged);
            //
            // lbl_dpc
            //
            this.lbl_dpc.AutoSize = true;
            this.lbl_dpc.Location = new System.Drawing.Point(190, 30);
            this.lbl_dpc.Name = "lbl_dpc";
            this.lbl_dpc.Size = new System.Drawing.Size(69, 20);
            this.lbl_dpc.TabIndex = 2;
            this.lbl_dpc.Text = "测试电脑";
            //
            // cmb_dpc
            //
            this.cmb_dpc.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmb_dpc.FormattingEnabled = true;
            this.cmb_dpc.Location = new System.Drawing.Point(270, 26);
            this.cmb_dpc.Name = "cmb_dpc";
            this.cmb_dpc.Size = new System.Drawing.Size(120, 28);
            this.cmb_dpc.TabIndex = 3;
            this.cmb_dpc.SelectedIndexChanged += new System.EventHandler(this.cmb_dpc_SelectedIndexChanged);
            //
            // lbl_dip
            //
            this.lbl_dip.AutoSize = true;
            this.lbl_dip.Location = new System.Drawing.Point(410, 30);
            this.lbl_dip.Name = "lbl_dip";
            this.lbl_dip.Size = new System.Drawing.Size(22, 20);
            this.lbl_dip.TabIndex = 5;
            this.lbl_dip.Text = "IP";
            //
            // txt_dip
            //
            this.txt_dip.Location = new System.Drawing.Point(440, 26);
            this.txt_dip.Name = "txt_dip";
            this.txt_dip.Size = new System.Drawing.Size(160, 27);
            this.txt_dip.TabIndex = 6;
            //
            // btn_dsaveip
            //
            this.btn_dsaveip.Location = new System.Drawing.Point(610, 22);
            this.btn_dsaveip.Name = "btn_dsaveip";
            this.btn_dsaveip.Size = new System.Drawing.Size(110, 34);
            this.btn_dsaveip.TabIndex = 7;
            this.btn_dsaveip.Text = "保存IP";
            this.btn_dsaveip.UseVisualStyleBackColor = true;
            this.btn_dsaveip.Click += new System.EventHandler(this.btn_dsaveip_Click);
            //
            // lbl_dhint
            //
            this.lbl_dhint.Location = new System.Drawing.Point(436, 64);
            this.lbl_dhint.Name = "lbl_dhint";
            this.lbl_dhint.Size = new System.Drawing.Size(290, 24);
            this.lbl_dhint.TabIndex = 4;
            this.lbl_dhint.Text = "IP优先级：手动保存 > 连接读取 > WS*Cfg.ini";
            //
            // btn_dautoip
            //
            this.btn_dautoip.Location = new System.Drawing.Point(16, 58);
            this.btn_dautoip.Name = "btn_dautoip";
            this.btn_dautoip.Size = new System.Drawing.Size(180, 32);
            this.btn_dautoip.TabIndex = 8;
            this.btn_dautoip.Text = "从连接读取本台IP";
            this.btn_dautoip.UseVisualStyleBackColor = true;
            this.btn_dautoip.Click += new System.EventHandler(this.btn_dautoip_Click);
            //
            // btn_dautoall
            //
            this.btn_dautoall.Location = new System.Drawing.Point(206, 58);
            this.btn_dautoall.Name = "btn_dautoall";
            this.btn_dautoall.Size = new System.Drawing.Size(220, 32);
            this.btn_dautoall.TabIndex = 9;
            this.btn_dautoall.Text = "全部工站读取并保存IP";
            this.btn_dautoall.UseVisualStyleBackColor = true;
            this.btn_dautoall.Click += new System.EventHandler(this.btn_dautoall_Click);
            //
            // grp_mode
            //
            this.grp_mode.Controls.Add(this.num_dwait);
            this.grp_mode.Controls.Add(this.lbl_dwait);
            this.grp_mode.Controls.Add(this.btn_dsetwait);
            this.grp_mode.Controls.Add(this.btn_dset);
            this.grp_mode.Controls.Add(this.btn_dget);
            this.grp_mode.Controls.Add(this.num_dtemp);
            this.grp_mode.Controls.Add(this.lbl_dtemp);
            this.grp_mode.Controls.Add(this.cmb_dmode);
            this.grp_mode.Controls.Add(this.lbl_dmode);
            this.grp_mode.Location = new System.Drawing.Point(12, 114);
            this.grp_mode.Name = "grp_mode";
            this.grp_mode.Size = new System.Drawing.Size(736, 110);
            this.grp_mode.TabIndex = 1;
            this.grp_mode.TabStop = false;
            this.grp_mode.Text = "模式切换（PUCFTestServiceCall.dll）";
            //
            // lbl_dmode
            //
            this.lbl_dmode.AutoSize = true;
            this.lbl_dmode.Location = new System.Drawing.Point(16, 32);
            this.lbl_dmode.Name = "lbl_dmode";
            this.lbl_dmode.Size = new System.Drawing.Size(69, 20);
            this.lbl_dmode.TabIndex = 0;
            this.lbl_dmode.Text = "目标模式";
            //
            // cmb_dmode
            //
            this.cmb_dmode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmb_dmode.FormattingEnabled = true;
            this.cmb_dmode.Items.AddRange(new object[] {
            "生产(0)",
            "点检(1)",
            "点检生成(2)"});
            this.cmb_dmode.Location = new System.Drawing.Point(100, 28);
            this.cmb_dmode.Name = "cmb_dmode";
            this.cmb_dmode.Size = new System.Drawing.Size(160, 28);
            this.cmb_dmode.TabIndex = 1;
            //
            // lbl_dtemp
            //
            this.lbl_dtemp.AutoSize = true;
            this.lbl_dtemp.Location = new System.Drawing.Point(280, 32);
            this.lbl_dtemp.Name = "lbl_dtemp";
            this.lbl_dtemp.Size = new System.Drawing.Size(114, 20);
            this.lbl_dtemp.TabIndex = 2;
            this.lbl_dtemp.Text = "CheckModeTemp";
            //
            // num_dtemp
            //
            this.num_dtemp.Location = new System.Drawing.Point(400, 28);
            this.num_dtemp.Maximum = new decimal(new int[] {
            99999,
            0,
            0,
            0});
            this.num_dtemp.Name = "num_dtemp";
            this.num_dtemp.Size = new System.Drawing.Size(100, 27);
            this.num_dtemp.TabIndex = 3;
            this.num_dtemp.Value = new decimal(new int[] {
            3000,
            0,
            0,
            0});
            //
            // btn_dget
            //
            this.btn_dget.Location = new System.Drawing.Point(16, 64);
            this.btn_dget.Name = "btn_dget";
            this.btn_dget.Size = new System.Drawing.Size(130, 36);
            this.btn_dget.TabIndex = 4;
            this.btn_dget.Text = "读取当前模式";
            this.btn_dget.UseVisualStyleBackColor = true;
            this.btn_dget.Click += new System.EventHandler(this.btn_dget_Click);
            //
            // btn_dset
            //
            this.btn_dset.Location = new System.Drawing.Point(156, 64);
            this.btn_dset.Name = "btn_dset";
            this.btn_dset.Size = new System.Drawing.Size(130, 36);
            this.btn_dset.TabIndex = 5;
            this.btn_dset.Text = "只发送切换";
            this.btn_dset.UseVisualStyleBackColor = true;
            this.btn_dset.Click += new System.EventHandler(this.btn_dset_Click);
            //
            // btn_dsetwait
            //
            this.btn_dsetwait.Location = new System.Drawing.Point(296, 64);
            this.btn_dsetwait.Name = "btn_dsetwait";
            this.btn_dsetwait.Size = new System.Drawing.Size(170, 36);
            this.btn_dsetwait.TabIndex = 6;
            this.btn_dsetwait.Text = "发送并等待回读确认";
            this.btn_dsetwait.UseVisualStyleBackColor = true;
            this.btn_dsetwait.Click += new System.EventHandler(this.btn_dsetwait_Click);
            //
            // lbl_dwait
            //
            this.lbl_dwait.AutoSize = true;
            this.lbl_dwait.Location = new System.Drawing.Point(480, 72);
            this.lbl_dwait.Name = "lbl_dwait";
            this.lbl_dwait.Size = new System.Drawing.Size(69, 20);
            this.lbl_dwait.TabIndex = 7;
            this.lbl_dwait.Text = "等待(秒)";
            //
            // num_dwait
            //
            this.num_dwait.Location = new System.Drawing.Point(560, 68);
            this.num_dwait.Maximum = new decimal(new int[] {
            600,
            0,
            0,
            0});
            this.num_dwait.Minimum = new decimal(new int[] {
            5,
            0,
            0,
            0});
            this.num_dwait.Name = "num_dwait";
            this.num_dwait.Size = new System.Drawing.Size(80, 27);
            this.num_dwait.TabIndex = 8;
            this.num_dwait.Value = new decimal(new int[] {
            60,
            0,
            0,
            0});
            //
            // grp_link
            //
            this.grp_link.Controls.Add(this.btn_dstatus);
            this.grp_link.Controls.Add(this.btn_dinfo);
            this.grp_link.Location = new System.Drawing.Point(12, 230);
            this.grp_link.Name = "grp_link";
            this.grp_link.Size = new System.Drawing.Size(736, 70);
            this.grp_link.TabIndex = 2;
            this.grp_link.TabStop = false;
            this.grp_link.Text = "测试握手（dllforcomv8.dll）";
            //
            // btn_dinfo
            //
            this.btn_dinfo.Location = new System.Drawing.Point(16, 26);
            this.btn_dinfo.Name = "btn_dinfo";
            this.btn_dinfo.Size = new System.Drawing.Size(180, 36);
            this.btn_dinfo.TabIndex = 0;
            this.btn_dinfo.Text = "读握手状态(GetInfo)";
            this.btn_dinfo.UseVisualStyleBackColor = true;
            this.btn_dinfo.Click += new System.EventHandler(this.btn_dinfo_Click);
            //
            // btn_dstatus
            //
            this.btn_dstatus.Location = new System.Drawing.Point(206, 26);
            this.btn_dstatus.Name = "btn_dstatus";
            this.btn_dstatus.Size = new System.Drawing.Size(200, 36);
            this.btn_dstatus.TabIndex = 1;
            this.btn_dstatus.Text = "读联机心跳(GetStatus)";
            this.btn_dstatus.UseVisualStyleBackColor = true;
            this.btn_dstatus.Click += new System.EventHandler(this.btn_dstatus_Click);
            //
            // txt_dlog
            //
            this.txt_dlog.Font = new System.Drawing.Font("Consolas", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txt_dlog.Location = new System.Drawing.Point(12, 308);
            this.txt_dlog.Multiline = true;
            this.txt_dlog.Name = "txt_dlog";
            this.txt_dlog.ReadOnly = true;
            this.txt_dlog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txt_dlog.Size = new System.Drawing.Size(736, 200);
            this.txt_dlog.TabIndex = 3;
            //
            // btn_dclear
            //
            this.btn_dclear.Location = new System.Drawing.Point(538, 516);
            this.btn_dclear.Name = "btn_dclear";
            this.btn_dclear.Size = new System.Drawing.Size(100, 34);
            this.btn_dclear.TabIndex = 4;
            this.btn_dclear.Text = "清空";
            this.btn_dclear.UseVisualStyleBackColor = true;
            this.btn_dclear.Click += new System.EventHandler(this.btn_dclear_Click);
            //
            // btn_dclose
            //
            this.btn_dclose.Location = new System.Drawing.Point(648, 516);
            this.btn_dclose.Name = "btn_dclose";
            this.btn_dclose.Size = new System.Drawing.Size(100, 34);
            this.btn_dclose.TabIndex = 5;
            this.btn_dclose.Text = "关闭";
            this.btn_dclose.UseVisualStyleBackColor = true;
            this.btn_dclose.Click += new System.EventHandler(this.btn_dclose_Click);
            //
            // FrModuleCheckDebug
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(760, 560);
            this.Controls.Add(this.btn_dclose);
            this.Controls.Add(this.btn_dclear);
            this.Controls.Add(this.txt_dlog);
            this.Controls.Add(this.grp_link);
            this.Controls.Add(this.grp_mode);
            this.Controls.Add(this.grp_target);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FrModuleCheckDebug";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "模组点检联调：单台电脑";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FrModuleCheckDebug_FormClosing);
            this.Load += new System.EventHandler(this.FrModuleCheckDebug_Load);
            this.grp_target.ResumeLayout(false);
            this.grp_target.PerformLayout();
            this.grp_mode.ResumeLayout(false);
            this.grp_mode.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.num_dwait)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_dtemp)).EndInit();
            this.grp_link.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.GroupBox grp_target;
        private System.Windows.Forms.Label lbl_dstation;
        private System.Windows.Forms.ComboBox cmb_dstation;
        private System.Windows.Forms.Label lbl_dpc;
        private System.Windows.Forms.ComboBox cmb_dpc;
        private System.Windows.Forms.Label lbl_dhint;
        private System.Windows.Forms.Label lbl_dip;
        private System.Windows.Forms.TextBox txt_dip;
        private System.Windows.Forms.Button btn_dsaveip;
        private System.Windows.Forms.Button btn_dautoip;
        private System.Windows.Forms.Button btn_dautoall;
        private System.Windows.Forms.GroupBox grp_mode;
        private System.Windows.Forms.Label lbl_dmode;
        private System.Windows.Forms.ComboBox cmb_dmode;
        private System.Windows.Forms.Label lbl_dtemp;
        private System.Windows.Forms.NumericUpDown num_dtemp;
        private System.Windows.Forms.Button btn_dget;
        private System.Windows.Forms.Button btn_dset;
        private System.Windows.Forms.Button btn_dsetwait;
        private System.Windows.Forms.Label lbl_dwait;
        private System.Windows.Forms.NumericUpDown num_dwait;
        private System.Windows.Forms.GroupBox grp_link;
        private System.Windows.Forms.Button btn_dinfo;
        private System.Windows.Forms.Button btn_dstatus;
        private System.Windows.Forms.TextBox txt_dlog;
        private System.Windows.Forms.Button btn_dclear;
        private System.Windows.Forms.Button btn_dclose;
    }
}

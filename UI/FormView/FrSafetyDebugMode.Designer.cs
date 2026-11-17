namespace UI
{
    partial class FrSafetyDebugMode
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
            this.components = new System.ComponentModel.Container();
            this.lbl_note = new System.Windows.Forms.Label();
            this.grp_inputs = new System.Windows.Forms.GroupBox();
            this.chk_emg0 = new System.Windows.Forms.CheckBox();
            this.lbl_emg0 = new System.Windows.Forms.Label();
            this.chk_emg1 = new System.Windows.Forms.CheckBox();
            this.lbl_emg1 = new System.Windows.Forms.Label();
            this.chk_emg2 = new System.Windows.Forms.CheckBox();
            this.lbl_emg2 = new System.Windows.Forms.Label();
            this.chk_emg3 = new System.Windows.Forms.CheckBox();
            this.lbl_emg3 = new System.Windows.Forms.Label();
            this.grp_doors = new System.Windows.Forms.GroupBox();
            this.chk_door_ud = new System.Windows.Forms.CheckBox();
            this.chk_door_tt = new System.Windows.Forms.CheckBox();
            this.chk_door_left = new System.Windows.Forms.CheckBox();
            this.chk_door_right = new System.Windows.Forms.CheckBox();
            this.lbl_door_ud = new System.Windows.Forms.Label();
            this.lbl_door_tt = new System.Windows.Forms.Label();
            this.lbl_door_left = new System.Windows.Forms.Label();
            this.lbl_door_right = new System.Windows.Forms.Label();
            this.grp_lightbox = new System.Windows.Forms.GroupBox();
            this.chk_mask_lb = new System.Windows.Forms.CheckBox();
            this.lbl_mask_lb = new System.Windows.Forms.Label();
            this.btn_enable = new System.Windows.Forms.Button();
            this.btn_disable = new System.Windows.Forms.Button();
            this.btn_tnreset = new System.Windows.Forms.Button();
            this.lbl_demores = new System.Windows.Forms.Label();
            this.cmb_demores = new System.Windows.Forms.ComboBox();
            this.chk_blindpick = new System.Windows.Forms.CheckBox();
            this.chk_earlyprefetch = new System.Windows.Forms.CheckBox();
            this.lbl_preplacefly = new System.Windows.Forms.Label();
            this.cmb_preplacefly = new System.Windows.Forms.ComboBox();
            this.lbl_preplacetask = new System.Windows.Forms.Label();
            this.cmb_preplacetask = new System.Windows.Forms.ComboBox();
            this.lbl_mode = new System.Windows.Forms.Label();
            this.tmr_refresh = new System.Windows.Forms.Timer(this.components);
            this.grp_inputs.SuspendLayout();
            this.grp_doors.SuspendLayout();
            this.grp_lightbox.SuspendLayout();
            this.SuspendLayout();
            //
            // lbl_note
            //
            this.lbl_note.ForeColor = System.Drawing.Color.DarkRed;
            this.lbl_note.Location = new System.Drawing.Point(12, 12);
            this.lbl_note.Name = "lbl_note";
            this.lbl_note.Size = new System.Drawing.Size(536, 66);
            this.lbl_note.TabIndex = 0;
            this.lbl_note.Text = "急停屏蔽、门禁忽略与光箱屏蔽可单独选择，点击“应用调试设置”后生效。仅本次运行有效，不修改原配置；退出或重启后恢复。调试期间禁止自动运行，硬件急停回路与软限位保持有效。";
            //
            // grp_inputs
            //
            this.grp_inputs.Controls.Add(this.chk_emg0);
            this.grp_inputs.Controls.Add(this.lbl_emg0);
            this.grp_inputs.Controls.Add(this.chk_emg1);
            this.grp_inputs.Controls.Add(this.lbl_emg1);
            this.grp_inputs.Controls.Add(this.chk_emg2);
            this.grp_inputs.Controls.Add(this.lbl_emg2);
            this.grp_inputs.Controls.Add(this.chk_emg3);
            this.grp_inputs.Controls.Add(this.lbl_emg3);
            this.grp_inputs.Location = new System.Drawing.Point(12, 84);
            this.grp_inputs.Name = "grp_inputs";
            this.grp_inputs.Size = new System.Drawing.Size(536, 196);
            this.grp_inputs.TabIndex = 1;
            this.grp_inputs.TabStop = false;
            this.grp_inputs.Text = "急停输入（勾选 = 屏蔽）及当前状态";
            //
            // chk_emg0
            //
            this.chk_emg0.AutoSize = true;
            this.chk_emg0.Location = new System.Drawing.Point(20, 32);
            this.chk_emg0.Name = "chk_emg0";
            this.chk_emg0.Size = new System.Drawing.Size(150, 24);
            this.chk_emg0.TabIndex = 0;
            this.chk_emg0.Text = "急停0";
            this.chk_emg0.UseVisualStyleBackColor = true;
            //
            // lbl_emg0
            //
            this.lbl_emg0.Location = new System.Drawing.Point(260, 34);
            this.lbl_emg0.Name = "lbl_emg0";
            this.lbl_emg0.Size = new System.Drawing.Size(240, 22);
            this.lbl_emg0.TabIndex = 1;
            this.lbl_emg0.Text = "-";
            //
            // chk_emg1
            //
            this.chk_emg1.AutoSize = true;
            this.chk_emg1.Location = new System.Drawing.Point(20, 72);
            this.chk_emg1.Name = "chk_emg1";
            this.chk_emg1.Size = new System.Drawing.Size(150, 24);
            this.chk_emg1.TabIndex = 2;
            this.chk_emg1.Text = "急停1";
            this.chk_emg1.UseVisualStyleBackColor = true;
            //
            // lbl_emg1
            //
            this.lbl_emg1.Location = new System.Drawing.Point(260, 74);
            this.lbl_emg1.Name = "lbl_emg1";
            this.lbl_emg1.Size = new System.Drawing.Size(240, 22);
            this.lbl_emg1.TabIndex = 3;
            this.lbl_emg1.Text = "-";
            //
            // chk_emg2
            //
            this.chk_emg2.AutoSize = true;
            this.chk_emg2.Location = new System.Drawing.Point(20, 112);
            this.chk_emg2.Name = "chk_emg2";
            this.chk_emg2.Size = new System.Drawing.Size(150, 24);
            this.chk_emg2.TabIndex = 4;
            this.chk_emg2.Text = "急停2";
            this.chk_emg2.UseVisualStyleBackColor = true;
            //
            // lbl_emg2
            //
            this.lbl_emg2.Location = new System.Drawing.Point(260, 114);
            this.lbl_emg2.Name = "lbl_emg2";
            this.lbl_emg2.Size = new System.Drawing.Size(240, 22);
            this.lbl_emg2.TabIndex = 5;
            this.lbl_emg2.Text = "-";
            //
            // chk_emg3
            //
            this.chk_emg3.AutoSize = true;
            this.chk_emg3.Location = new System.Drawing.Point(20, 152);
            this.chk_emg3.Name = "chk_emg3";
            this.chk_emg3.Size = new System.Drawing.Size(150, 24);
            this.chk_emg3.TabIndex = 6;
            this.chk_emg3.Text = "急停3";
            this.chk_emg3.UseVisualStyleBackColor = true;
            //
            // lbl_emg3
            //
            this.lbl_emg3.Location = new System.Drawing.Point(260, 154);
            this.lbl_emg3.Name = "lbl_emg3";
            this.lbl_emg3.Size = new System.Drawing.Size(240, 22);
            this.lbl_emg3.TabIndex = 7;
            this.lbl_emg3.Text = "-";
            //
            // grp_doors
            //
            this.grp_doors.Controls.Add(this.chk_door_ud);
            this.grp_doors.Controls.Add(this.chk_door_tt);
            this.grp_doors.Controls.Add(this.chk_door_left);
            this.grp_doors.Controls.Add(this.chk_door_right);
            this.grp_doors.Controls.Add(this.lbl_door_ud);
            this.grp_doors.Controls.Add(this.lbl_door_tt);
            this.grp_doors.Controls.Add(this.lbl_door_left);
            this.grp_doors.Controls.Add(this.lbl_door_right);
            this.grp_doors.Location = new System.Drawing.Point(12, 292);
            this.grp_doors.Name = "grp_doors";
            this.grp_doors.Size = new System.Drawing.Size(536, 180);
            this.grp_doors.TabIndex = 2;
            this.grp_doors.TabStop = false;
            this.grp_doors.Text = "门禁使能（勾选 = 本次调试忽略）";
            //
            // door selections and current effective configuration
            //
            this.chk_door_ud.AutoSize = true;
            this.chk_door_ud.Location = new System.Drawing.Point(20, 32);
            this.chk_door_ud.Name = "chk_door_ud";
            this.chk_door_ud.TabIndex = 0;
            this.chk_door_ud.Text = "忽略上下料门禁使能";
            this.chk_door_ud.UseVisualStyleBackColor = true;
            this.chk_door_tt.AutoSize = true;
            this.chk_door_tt.Location = new System.Drawing.Point(20, 68);
            this.chk_door_tt.Name = "chk_door_tt";
            this.chk_door_tt.TabIndex = 1;
            this.chk_door_tt.Text = "忽略转盘门禁使能";
            this.chk_door_tt.UseVisualStyleBackColor = true;
            this.chk_door_left.AutoSize = true;
            this.chk_door_left.Location = new System.Drawing.Point(20, 104);
            this.chk_door_left.Name = "chk_door_left";
            this.chk_door_left.TabIndex = 2;
            this.chk_door_left.Text = "忽略左光箱门禁使能";
            this.chk_door_left.UseVisualStyleBackColor = true;
            this.chk_door_right.AutoSize = true;
            this.chk_door_right.Location = new System.Drawing.Point(20, 140);
            this.chk_door_right.Name = "chk_door_right";
            this.chk_door_right.TabIndex = 3;
            this.chk_door_right.Text = "忽略右光箱门禁使能";
            this.chk_door_right.UseVisualStyleBackColor = true;
            this.lbl_door_ud.Location = new System.Drawing.Point(260, 34);
            this.lbl_door_ud.Name = "lbl_door_ud";
            this.lbl_door_ud.Size = new System.Drawing.Size(260, 24);
            this.lbl_door_ud.Text = "-";
            this.lbl_door_tt.Location = new System.Drawing.Point(260, 70);
            this.lbl_door_tt.Name = "lbl_door_tt";
            this.lbl_door_tt.Size = new System.Drawing.Size(260, 24);
            this.lbl_door_tt.Text = "-";
            this.lbl_door_left.Location = new System.Drawing.Point(260, 106);
            this.lbl_door_left.Name = "lbl_door_left";
            this.lbl_door_left.Size = new System.Drawing.Size(260, 24);
            this.lbl_door_left.Text = "-";
            this.lbl_door_right.Location = new System.Drawing.Point(260, 142);
            this.lbl_door_right.Name = "lbl_door_right";
            this.lbl_door_right.Size = new System.Drawing.Size(260, 24);
            this.lbl_door_right.Text = "-";
            //
            // grp_lightbox
            //
            this.grp_lightbox.Controls.Add(this.chk_mask_lb);
            this.grp_lightbox.Controls.Add(this.lbl_mask_lb);
            this.grp_lightbox.Location = new System.Drawing.Point(12, 484);
            this.grp_lightbox.Name = "grp_lightbox";
            this.grp_lightbox.Size = new System.Drawing.Size(536, 76);
            this.grp_lightbox.TabIndex = 6;
            this.grp_lightbox.TabStop = false;
            this.grp_lightbox.Text = "光箱屏蔽（板卡未接入时勾选 = 本次运行按光箱未启用处理）";
            //
            // chk_mask_lb
            //
            this.chk_mask_lb.AutoSize = true;
            this.chk_mask_lb.Location = new System.Drawing.Point(20, 34);
            this.chk_mask_lb.Name = "chk_mask_lb";
            this.chk_mask_lb.Size = new System.Drawing.Size(150, 24);
            this.chk_mask_lb.TabIndex = 0;
            this.chk_mask_lb.Text = "屏蔽左右光箱";
            this.chk_mask_lb.UseVisualStyleBackColor = true;
            //
            // lbl_mask_lb
            //
            this.lbl_mask_lb.Location = new System.Drawing.Point(260, 36);
            this.lbl_mask_lb.Name = "lbl_mask_lb";
            this.lbl_mask_lb.Size = new System.Drawing.Size(260, 24);
            this.lbl_mask_lb.TabIndex = 1;
            this.lbl_mask_lb.Text = "-";
            //
            // btn_enable
            //
            this.btn_enable.Location = new System.Drawing.Point(12, 572);
            this.btn_enable.Name = "btn_enable";
            this.btn_enable.Size = new System.Drawing.Size(160, 44);
            this.btn_enable.TabIndex = 3;
            this.btn_enable.Text = "应用调试设置";
            this.btn_enable.UseVisualStyleBackColor = true;
            this.btn_enable.Click += new System.EventHandler(this.btn_enable_Click);
            //
            // btn_disable
            //
            this.btn_disable.Enabled = false;
            this.btn_disable.Location = new System.Drawing.Point(184, 572);
            this.btn_disable.Name = "btn_disable";
            this.btn_disable.Size = new System.Drawing.Size(160, 44);
            this.btn_disable.TabIndex = 4;
            this.btn_disable.Text = "退出调试模式";
            this.btn_disable.UseVisualStyleBackColor = true;
            this.btn_disable.Click += new System.EventHandler(this.btn_disable_Click);
            //
            // btn_tnreset
            //
            this.btn_tnreset.Location = new System.Drawing.Point(356, 572);
            this.btn_tnreset.Name = "btn_tnreset";
            this.btn_tnreset.Size = new System.Drawing.Size(192, 44);
            this.btn_tnreset.TabIndex = 6;
            this.btn_tnreset.Text = "三吸嘴复位(不回零)";
            this.btn_tnreset.UseVisualStyleBackColor = true;
            this.btn_tnreset.Click += new System.EventHandler(this.btn_tnreset_Click);
            //
            // lbl_demores
            //
            this.lbl_demores.AutoSize = true;
            this.lbl_demores.Location = new System.Drawing.Point(12, 726);
            this.lbl_demores.Name = "lbl_demores";
            this.lbl_demores.Size = new System.Drawing.Size(159, 20);
            this.lbl_demores.TabIndex = 7;
            this.lbl_demores.Text = "无料空跑模拟测试结果：";
            //
            // cmb_demores
            //
            this.cmb_demores.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmb_demores.FormattingEnabled = true;
            this.cmb_demores.Items.AddRange(new object[] {
            "一排OK一排NG（原逻辑：1-8穴NG，9-16穴OK）",
            "全部OK",
            "全部NG"});
            this.cmb_demores.Location = new System.Drawing.Point(184, 722);
            this.cmb_demores.Name = "cmb_demores";
            this.cmb_demores.Size = new System.Drawing.Size(364, 28);
            this.cmb_demores.TabIndex = 8;
            this.cmb_demores.SelectedIndexChanged += new System.EventHandler(this.cmb_demores_SelectedIndexChanged);
            //
            // chk_blindpick
            //
            this.chk_blindpick.AutoSize = true;
            this.chk_blindpick.Location = new System.Drawing.Point(16, 764);
            this.chk_blindpick.Name = "chk_blindpick";
            this.chk_blindpick.Size = new System.Drawing.Size(420, 24);
            this.chk_blindpick.TabIndex = 9;
            this.chk_blindpick.Text = "供料盘盲取（不拍上相机，按示教位置直接取料，本次运行有效）";
            this.chk_blindpick.UseVisualStyleBackColor = true;
            this.chk_blindpick.CheckedChanged += new System.EventHandler(this.chk_blindpick_CheckedChanged);
            //
            // lbl_preplacefly
            //
            this.lbl_preplacefly.AutoSize = true;
            this.lbl_preplacefly.Location = new System.Drawing.Point(12, 806);
            this.lbl_preplacefly.Name = "lbl_preplacefly";
            this.lbl_preplacefly.Size = new System.Drawing.Size(159, 20);
            this.lbl_preplacefly.TabIndex = 10;
            this.lbl_preplacefly.Text = "放料前上相机飞拍：";
            //
            // cmb_preplacefly
            //
            this.cmb_preplacefly.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmb_preplacefly.FormattingEnabled = true;
            this.cmb_preplacefly.Items.AddRange(new object[] {
            "关闭（取完旧料后退回定拍，原流程）",
            "对比（进夹具飞拍+取后定拍，用定拍放料）",
            "启用（用进夹具飞拍结果放料，不退回拍照）"});
            this.cmb_preplacefly.Location = new System.Drawing.Point(184, 802);
            this.cmb_preplacefly.Name = "cmb_preplacefly";
            this.cmb_preplacefly.Size = new System.Drawing.Size(364, 28);
            this.cmb_preplacefly.TabIndex = 11;
            this.cmb_preplacefly.SelectedIndexChanged += new System.EventHandler(this.cmb_preplacefly_SelectedIndexChanged);
            //
            // lbl_preplacetask
            //
            this.lbl_preplacetask.AutoSize = true;
            this.lbl_preplacetask.Location = new System.Drawing.Point(12, 846);
            this.lbl_preplacetask.Name = "lbl_preplacetask";
            this.lbl_preplacetask.Size = new System.Drawing.Size(159, 20);
            this.lbl_preplacetask.TabIndex = 12;
            this.lbl_preplacetask.Text = "飞拍视觉任务(有料)：";
            //
            // cmb_preplacetask
            //
            this.cmb_preplacetask.FormattingEnabled = true;
            this.cmb_preplacetask.Location = new System.Drawing.Point(184, 842);
            this.cmb_preplacetask.Name = "cmb_preplacetask";
            this.cmb_preplacetask.Size = new System.Drawing.Size(364, 28);
            this.cmb_preplacetask.TabIndex = 13;
            this.cmb_preplacetask.TextChanged += new System.EventHandler(this.cmb_preplacetask_TextChanged);
            //
            // chk_earlyprefetch
            //
            this.chk_earlyprefetch.AutoSize = true;
            this.chk_earlyprefetch.Location = new System.Drawing.Point(16, 882);
            this.chk_earlyprefetch.Name = "chk_earlyprefetch";
            this.chk_earlyprefetch.Size = new System.Drawing.Size(420, 24);
            this.chk_earlyprefetch.TabIndex = 14;
            this.chk_earlyprefetch.Text = "三吸嘴提前预取（取消勾选=关闭，本次运行有效，重启恢复开启）";
            this.chk_earlyprefetch.UseVisualStyleBackColor = true;
            this.chk_earlyprefetch.CheckedChanged += new System.EventHandler(this.chk_earlyprefetch_CheckedChanged);
            //
            // lbl_mode
            //
            this.lbl_mode.Location = new System.Drawing.Point(12, 626);
            this.lbl_mode.Name = "lbl_mode";
            this.lbl_mode.Size = new System.Drawing.Size(536, 88);
            this.lbl_mode.TabIndex = 5;
            this.lbl_mode.Text = "-";
            //
            // tmr_refresh
            //
            this.tmr_refresh.Interval = 300;
            this.tmr_refresh.Tick += new System.EventHandler(this.tmr_refresh_Tick);
            //
            // FrSafetyDebugMode
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(560, 920);
            this.Controls.Add(this.chk_earlyprefetch);
            this.Controls.Add(this.cmb_preplacetask);
            this.Controls.Add(this.lbl_preplacetask);
            this.Controls.Add(this.cmb_preplacefly);
            this.Controls.Add(this.lbl_preplacefly);
            this.Controls.Add(this.chk_blindpick);
            this.Controls.Add(this.cmb_demores);
            this.Controls.Add(this.lbl_demores);
            this.Controls.Add(this.grp_lightbox);
            this.Controls.Add(this.grp_doors);
            this.Controls.Add(this.lbl_mode);
            this.Controls.Add(this.btn_tnreset);
            this.Controls.Add(this.btn_disable);
            this.Controls.Add(this.btn_enable);
            this.Controls.Add(this.grp_inputs);
            this.Controls.Add(this.lbl_note);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.Name = "FrSafetyDebugMode";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "调试模式（急停 / 门禁 / 光箱）";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.FrSafetyDebugMode_FormClosed);
            this.Load += new System.EventHandler(this.FrSafetyDebugMode_Load);
            this.grp_inputs.ResumeLayout(false);
            this.grp_inputs.PerformLayout();
            this.grp_doors.ResumeLayout(false);
            this.grp_doors.PerformLayout();
            this.grp_lightbox.ResumeLayout(false);
            this.grp_lightbox.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Label lbl_note;
        private System.Windows.Forms.GroupBox grp_inputs;
        private System.Windows.Forms.CheckBox chk_emg0;
        private System.Windows.Forms.Label lbl_emg0;
        private System.Windows.Forms.CheckBox chk_emg1;
        private System.Windows.Forms.Label lbl_emg1;
        private System.Windows.Forms.CheckBox chk_emg2;
        private System.Windows.Forms.Label lbl_emg2;
        private System.Windows.Forms.CheckBox chk_emg3;
        private System.Windows.Forms.Label lbl_emg3;
        private System.Windows.Forms.GroupBox grp_doors;
        private System.Windows.Forms.CheckBox chk_door_ud;
        private System.Windows.Forms.CheckBox chk_door_tt;
        private System.Windows.Forms.CheckBox chk_door_left;
        private System.Windows.Forms.CheckBox chk_door_right;
        private System.Windows.Forms.Label lbl_door_ud;
        private System.Windows.Forms.Label lbl_door_tt;
        private System.Windows.Forms.Label lbl_door_left;
        private System.Windows.Forms.Label lbl_door_right;
        private System.Windows.Forms.GroupBox grp_lightbox;
        private System.Windows.Forms.CheckBox chk_mask_lb;
        private System.Windows.Forms.Label lbl_mask_lb;
        private System.Windows.Forms.Button btn_enable;
        private System.Windows.Forms.Button btn_disable;
        private System.Windows.Forms.Button btn_tnreset;
        private System.Windows.Forms.Label lbl_demores;
        private System.Windows.Forms.ComboBox cmb_demores;
        private System.Windows.Forms.CheckBox chk_blindpick;
        private System.Windows.Forms.CheckBox chk_earlyprefetch;
        private System.Windows.Forms.Label lbl_preplacefly;
        private System.Windows.Forms.ComboBox cmb_preplacefly;
        private System.Windows.Forms.Label lbl_preplacetask;
        private System.Windows.Forms.ComboBox cmb_preplacetask;
        private System.Windows.Forms.Label lbl_mode;
        private System.Windows.Forms.Timer tmr_refresh;
    }
}

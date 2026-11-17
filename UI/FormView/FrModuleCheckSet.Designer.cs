namespace UI
{
    partial class FrModuleCheckSet
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
            this.grp_basic = new System.Windows.Forms.GroupBox();
            this.chk_borrow = new System.Windows.Forms.CheckBox();
            this.num_timeout = new System.Windows.Forms.NumericUpDown();
            this.lbl_timeout = new System.Windows.Forms.Label();
            this.num_temp = new System.Windows.Forms.NumericUpDown();
            this.lbl_temp = new System.Windows.Forms.Label();
            this.num_retry = new System.Windows.Forms.NumericUpDown();
            this.lbl_retry = new System.Windows.Forms.Label();
            this.num_advance = new System.Windows.Forms.NumericUpDown();
            this.lbl_advance = new System.Windows.Forms.Label();
            this.cmb_station = new System.Windows.Forms.ComboBox();
            this.lbl_station = new System.Windows.Forms.Label();
            this.chk_enabled = new System.Windows.Forms.CheckBox();
            this.grp_plan = new System.Windows.Forms.GroupBox();
            this.lbl_next = new System.Windows.Forms.Label();
            this.btn_deltime = new System.Windows.Forms.Button();
            this.btn_manual = new System.Windows.Forms.Button();
            this.btn_addtime = new System.Windows.Forms.Button();
            this.dtp_time = new System.Windows.Forms.DateTimePicker();
            this.lst_times = new System.Windows.Forms.ListBox();
            this.grp_samples = new System.Windows.Forms.GroupBox();
            this.lbl_samplehint = new System.Windows.Forms.Label();
            this.btn_delrow = new System.Windows.Forms.Button();
            this.btn_addrow = new System.Windows.Forms.Button();
            this.btn_fill16 = new System.Windows.Forms.Button();
            this.dgv_samples = new System.Windows.Forms.DataGridView();
            this.col_id = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.col_tray = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.col_pocket = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.col_group = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.grp_status = new System.Windows.Forms.GroupBox();
            this.btn_openlog = new System.Windows.Forms.Button();
            this.btn_reconcile = new System.Windows.Forms.Button();
            this.btn_cancel = new System.Windows.Forms.Button();
            this.lbl_task = new System.Windows.Forms.Label();
            this.btn_save = new System.Windows.Forms.Button();
            this.btn_close = new System.Windows.Forms.Button();
            this.btn_debug = new System.Windows.Forms.Button();
            this.lbl_flow = new System.Windows.Forms.Label();
            this.tmr_refresh = new System.Windows.Forms.Timer(this.components);
            this.grp_basic.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.num_timeout)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_temp)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_retry)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_advance)).BeginInit();
            this.grp_plan.SuspendLayout();
            this.grp_samples.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgv_samples)).BeginInit();
            this.grp_status.SuspendLayout();
            this.SuspendLayout();
            //
            // grp_basic
            //
            this.grp_basic.Controls.Add(this.chk_borrow);
            this.grp_basic.Controls.Add(this.num_timeout);
            this.grp_basic.Controls.Add(this.lbl_timeout);
            this.grp_basic.Controls.Add(this.num_temp);
            this.grp_basic.Controls.Add(this.lbl_temp);
            this.grp_basic.Controls.Add(this.num_retry);
            this.grp_basic.Controls.Add(this.lbl_retry);
            this.grp_basic.Controls.Add(this.num_advance);
            this.grp_basic.Controls.Add(this.lbl_advance);
            this.grp_basic.Controls.Add(this.cmb_station);
            this.grp_basic.Controls.Add(this.lbl_station);
            this.grp_basic.Controls.Add(this.chk_enabled);
            this.grp_basic.Location = new System.Drawing.Point(12, 12);
            this.grp_basic.Name = "grp_basic";
            this.grp_basic.Size = new System.Drawing.Size(420, 290);
            this.grp_basic.TabIndex = 0;
            this.grp_basic.TabStop = false;
            this.grp_basic.Text = "基本设置";
            //
            // chk_enabled
            //
            this.chk_enabled.AutoSize = true;
            this.chk_enabled.Location = new System.Drawing.Point(20, 30);
            this.chk_enabled.Name = "chk_enabled";
            this.chk_enabled.Size = new System.Drawing.Size(183, 24);
            this.chk_enabled.TabIndex = 0;
            this.chk_enabled.Text = "启用定时模组自动点检";
            this.chk_enabled.UseVisualStyleBackColor = true;
            //
            // lbl_station
            //
            this.lbl_station.AutoSize = true;
            this.lbl_station.Location = new System.Drawing.Point(20, 70);
            this.lbl_station.Name = "lbl_station";
            this.lbl_station.Size = new System.Drawing.Size(69, 20);
            this.lbl_station.TabIndex = 1;
            this.lbl_station.Text = "承载工站";
            //
            // cmb_station
            //
            this.cmb_station.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmb_station.FormattingEnabled = true;
            this.cmb_station.Items.AddRange(new object[] {
            "工站1",
            "工站2",
            "工站3",
            "工站4"});
            this.cmb_station.Location = new System.Drawing.Point(230, 66);
            this.cmb_station.Name = "cmb_station";
            this.cmb_station.Size = new System.Drawing.Size(120, 28);
            this.cmb_station.TabIndex = 2;
            this.cmb_station.SelectedIndexChanged += new System.EventHandler(this.cmb_station_SelectedIndexChanged);
            //
            // lbl_advance
            //
            this.lbl_advance.AutoSize = true;
            this.lbl_advance.Location = new System.Drawing.Point(20, 106);
            this.lbl_advance.Name = "lbl_advance";
            this.lbl_advance.Size = new System.Drawing.Size(114, 20);
            this.lbl_advance.TabIndex = 3;
            this.lbl_advance.Text = "提前触发(分钟)";
            //
            // num_advance
            //
            this.num_advance.Location = new System.Drawing.Point(230, 102);
            this.num_advance.Maximum = new decimal(new int[] {
            60,
            0,
            0,
            0});
            this.num_advance.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.num_advance.Name = "num_advance";
            this.num_advance.Size = new System.Drawing.Size(120, 27);
            this.num_advance.TabIndex = 4;
            this.num_advance.Value = new decimal(new int[] {
            60,
            0,
            0,
            0});
            //
            // lbl_retry
            //
            this.lbl_retry.AutoSize = true;
            this.lbl_retry.Location = new System.Drawing.Point(20, 142);
            this.lbl_retry.Name = "lbl_retry";
            this.lbl_retry.Size = new System.Drawing.Size(129, 20);
            this.lbl_retry.TabIndex = 5;
            this.lbl_retry.Text = "自动点检重试次数";
            //
            // num_retry
            //
            this.num_retry.Location = new System.Drawing.Point(230, 138);
            this.num_retry.Maximum = new decimal(new int[] {
            15,
            0,
            0,
            0});
            this.num_retry.Name = "num_retry";
            this.num_retry.Size = new System.Drawing.Size(120, 27);
            this.num_retry.TabIndex = 6;
            this.num_retry.Value = new decimal(new int[] {
            3,
            0,
            0,
            0});
            //
            // lbl_temp
            //
            this.lbl_temp.AutoSize = true;
            this.lbl_temp.Location = new System.Drawing.Point(20, 178);
            this.lbl_temp.Name = "lbl_temp";
            this.lbl_temp.Size = new System.Drawing.Size(189, 20);
            this.lbl_temp.TabIndex = 7;
            this.lbl_temp.Text = "点检参数(CheckModeTemp)";
            //
            // num_temp
            //
            this.num_temp.Location = new System.Drawing.Point(230, 174);
            this.num_temp.Maximum = new decimal(new int[] {
            99999,
            0,
            0,
            0});
            this.num_temp.Name = "num_temp";
            this.num_temp.Size = new System.Drawing.Size(120, 27);
            this.num_temp.TabIndex = 8;
            this.num_temp.Value = new decimal(new int[] {
            3000,
            0,
            0,
            0});
            //
            // lbl_timeout
            //
            this.lbl_timeout.AutoSize = true;
            this.lbl_timeout.Location = new System.Drawing.Point(20, 214);
            this.lbl_timeout.Name = "lbl_timeout";
            this.lbl_timeout.Size = new System.Drawing.Size(144, 20);
            this.lbl_timeout.TabIndex = 9;
            this.lbl_timeout.Text = "切参数确认超时(秒)";
            //
            // num_timeout
            //
            this.num_timeout.Location = new System.Drawing.Point(230, 210);
            this.num_timeout.Maximum = new decimal(new int[] {
            600,
            0,
            0,
            0});
            this.num_timeout.Minimum = new decimal(new int[] {
            5,
            0,
            0,
            0});
            this.num_timeout.Name = "num_timeout";
            this.num_timeout.Size = new System.Drawing.Size(120, 27);
            this.num_timeout.TabIndex = 10;
            this.num_timeout.Value = new decimal(new int[] {
            60,
            0,
            0,
            0});
            //
            // chk_borrow
            //
            this.chk_borrow.AutoSize = true;
            this.chk_borrow.Location = new System.Drawing.Point(20, 250);
            this.chk_borrow.Name = "chk_borrow";
            this.chk_borrow.Size = new System.Drawing.Size(273, 24);
            this.chk_borrow.TabIndex = 11;
            this.chk_borrow.Text = "点检盘未到位，借用待测盘的一部分";
            this.chk_borrow.UseVisualStyleBackColor = true;
            //
            // grp_plan
            //
            this.grp_plan.Controls.Add(this.lbl_next);
            this.grp_plan.Controls.Add(this.btn_manual);
            this.grp_plan.Controls.Add(this.btn_deltime);
            this.grp_plan.Controls.Add(this.btn_addtime);
            this.grp_plan.Controls.Add(this.dtp_time);
            this.grp_plan.Controls.Add(this.lst_times);
            this.grp_plan.Location = new System.Drawing.Point(12, 310);
            this.grp_plan.Name = "grp_plan";
            this.grp_plan.Size = new System.Drawing.Size(420, 200);
            this.grp_plan.TabIndex = 1;
            this.grp_plan.TabStop = false;
            this.grp_plan.Text = "点检时间 T（每天）";
            //
            // lst_times
            //
            this.lst_times.FormattingEnabled = true;
            this.lst_times.ItemHeight = 20;
            this.lst_times.Location = new System.Drawing.Point(20, 28);
            this.lst_times.Name = "lst_times";
            this.lst_times.Size = new System.Drawing.Size(180, 124);
            this.lst_times.TabIndex = 0;
            //
            // dtp_time
            //
            this.dtp_time.CustomFormat = "HH:mm";
            this.dtp_time.Format = System.Windows.Forms.DateTimePickerFormat.Custom;
            this.dtp_time.Location = new System.Drawing.Point(215, 28);
            this.dtp_time.Name = "dtp_time";
            this.dtp_time.ShowUpDown = true;
            this.dtp_time.Size = new System.Drawing.Size(100, 27);
            this.dtp_time.TabIndex = 1;
            //
            // btn_addtime
            //
            this.btn_addtime.Location = new System.Drawing.Point(215, 66);
            this.btn_addtime.Name = "btn_addtime";
            this.btn_addtime.Size = new System.Drawing.Size(100, 34);
            this.btn_addtime.TabIndex = 2;
            this.btn_addtime.Text = "添加";
            this.btn_addtime.UseVisualStyleBackColor = true;
            this.btn_addtime.Click += new System.EventHandler(this.btn_addtime_Click);
            //
            // btn_deltime
            //
            this.btn_deltime.Location = new System.Drawing.Point(215, 108);
            this.btn_deltime.Name = "btn_deltime";
            this.btn_deltime.Size = new System.Drawing.Size(100, 34);
            this.btn_deltime.TabIndex = 3;
            this.btn_deltime.Text = "删除选中";
            this.btn_deltime.UseVisualStyleBackColor = true;
            this.btn_deltime.Click += new System.EventHandler(this.btn_deltime_Click);
            //
            // btn_manual
            //
            this.btn_manual.Location = new System.Drawing.Point(325, 28);
            this.btn_manual.Name = "btn_manual";
            this.btn_manual.Size = new System.Drawing.Size(85, 114);
            this.btn_manual.TabIndex = 5;
            this.btn_manual.Text = "立即点检";
            this.btn_manual.UseVisualStyleBackColor = true;
            this.btn_manual.Click += new System.EventHandler(this.btn_manual_Click);
            //
            // lbl_next
            //
            this.lbl_next.Location = new System.Drawing.Point(20, 158);
            this.lbl_next.Name = "lbl_next";
            this.lbl_next.Size = new System.Drawing.Size(390, 36);
            this.lbl_next.TabIndex = 4;
            this.lbl_next.Text = "下一次计划：-";
            //
            // grp_samples
            //
            this.grp_samples.Controls.Add(this.lbl_samplehint);
            this.grp_samples.Controls.Add(this.btn_delrow);
            this.grp_samples.Controls.Add(this.btn_addrow);
            this.grp_samples.Controls.Add(this.btn_fill16);
            this.grp_samples.Controls.Add(this.dgv_samples);
            this.grp_samples.Location = new System.Drawing.Point(444, 12);
            this.grp_samples.Name = "grp_samples";
            this.grp_samples.Size = new System.Drawing.Size(444, 498);
            this.grp_samples.TabIndex = 2;
            this.grp_samples.TabStop = false;
            this.grp_samples.Text = "点检样品（放在待测盘的哪些穴位）";
            //
            // dgv_samples
            //
            this.dgv_samples.AllowUserToAddRows = false;
            this.dgv_samples.AllowUserToDeleteRows = false;
            this.dgv_samples.AllowUserToResizeRows = false;
            this.dgv_samples.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgv_samples.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.col_id,
            this.col_tray,
            this.col_pocket,
            this.col_group});
            this.dgv_samples.Location = new System.Drawing.Point(16, 28);
            this.dgv_samples.MultiSelect = false;
            this.dgv_samples.Name = "dgv_samples";
            this.dgv_samples.RowHeadersVisible = false;
            this.dgv_samples.RowTemplate.Height = 24;
            this.dgv_samples.Size = new System.Drawing.Size(412, 380);
            this.dgv_samples.TabIndex = 0;
            this.dgv_samples.CellEndEdit += new System.Windows.Forms.DataGridViewCellEventHandler(this.dgv_samples_CellEndEdit);
            //
            // col_id
            //
            this.col_id.HeaderText = "样品";
            this.col_id.Name = "col_id";
            this.col_id.ReadOnly = true;
            this.col_id.Width = 60;
            //
            // col_tray
            //
            this.col_tray.HeaderText = "待测盘穴位编号";
            this.col_tray.Name = "col_tray";
            this.col_tray.Width = 130;
            //
            // col_pocket
            //
            this.col_pocket.HeaderText = "首轮夹具穴(自动)";
            this.col_pocket.Name = "col_pocket";
            this.col_pocket.ReadOnly = true;
            this.col_pocket.Width = 120;
            //
            // col_group
            //
            this.col_group.HeaderText = "分组";
            this.col_group.Name = "col_group";
            this.col_group.ReadOnly = true;
            this.col_group.Width = 80;
            //
            // btn_fill16
            //
            this.btn_fill16.Location = new System.Drawing.Point(16, 416);
            this.btn_fill16.Name = "btn_fill16";
            this.btn_fill16.Size = new System.Drawing.Size(140, 34);
            this.btn_fill16.TabIndex = 1;
            this.btn_fill16.Text = "读取待测盘屏蔽穴位";
            this.btn_fill16.UseVisualStyleBackColor = true;
            this.btn_fill16.Click += new System.EventHandler(this.btn_fill16_Click);
            //
            // btn_addrow
            //
            this.btn_addrow.Location = new System.Drawing.Point(166, 416);
            this.btn_addrow.Name = "btn_addrow";
            this.btn_addrow.Size = new System.Drawing.Size(90, 34);
            this.btn_addrow.TabIndex = 2;
            this.btn_addrow.Text = "增加一颗";
            this.btn_addrow.UseVisualStyleBackColor = true;
            this.btn_addrow.Click += new System.EventHandler(this.btn_addrow_Click);
            //
            // btn_delrow
            //
            this.btn_delrow.Location = new System.Drawing.Point(266, 416);
            this.btn_delrow.Name = "btn_delrow";
            this.btn_delrow.Size = new System.Drawing.Size(90, 34);
            this.btn_delrow.TabIndex = 3;
            this.btn_delrow.Text = "删除选中";
            this.btn_delrow.UseVisualStyleBackColor = true;
            this.btn_delrow.Click += new System.EventHandler(this.btn_delrow_Click);
            //
            // lbl_samplehint
            //
            this.lbl_samplehint.Location = new System.Drawing.Point(16, 456);
            this.lbl_samplehint.Name = "lbl_samplehint";
            this.lbl_samplehint.Size = new System.Drawing.Size(412, 36);
            this.lbl_samplehint.TabIndex = 4;
            this.lbl_samplehint.Text = "只需填样品在待测盘的穴位编号（与 产品-料盘 穴位表的“编号”一致），首轮夹具穴位自动平均分到两组。样品不计产量、不扫码、不传MES。";
            //
            // grp_status
            //
            this.grp_status.Controls.Add(this.btn_reconcile);
            this.grp_status.Controls.Add(this.btn_openlog);
            this.grp_status.Controls.Add(this.btn_cancel);
            this.grp_status.Controls.Add(this.lbl_task);
            this.grp_status.Location = new System.Drawing.Point(12, 518);
            this.grp_status.Name = "grp_status";
            this.grp_status.Size = new System.Drawing.Size(876, 74);
            this.grp_status.TabIndex = 3;
            this.grp_status.TabStop = false;
            this.grp_status.Text = "当前任务";
            //
            // lbl_task
            //
            this.lbl_task.Location = new System.Drawing.Point(16, 26);
            this.lbl_task.Name = "lbl_task";
            this.lbl_task.Size = new System.Drawing.Size(424, 40);
            this.lbl_task.TabIndex = 0;
            this.lbl_task.Text = "无";
            //
            // btn_reconcile
            //
            this.btn_reconcile.Location = new System.Drawing.Point(450, 26);
            this.btn_reconcile.Name = "btn_reconcile";
            this.btn_reconcile.Size = new System.Drawing.Size(130, 36);
            this.btn_reconcile.TabIndex = 3;
            this.btn_reconcile.Text = "确认已核对";
            this.btn_reconcile.UseVisualStyleBackColor = true;
            this.btn_reconcile.Click += new System.EventHandler(this.btn_reconcile_Click);
            //
            // btn_cancel
            //
            this.btn_cancel.Location = new System.Drawing.Point(590, 26);
            this.btn_cancel.Name = "btn_cancel";
            this.btn_cancel.Size = new System.Drawing.Size(130, 36);
            this.btn_cancel.TabIndex = 1;
            this.btn_cancel.Text = "取消本次点检";
            this.btn_cancel.UseVisualStyleBackColor = true;
            this.btn_cancel.Click += new System.EventHandler(this.btn_cancel_Click);
            //
            // btn_openlog
            //
            this.btn_openlog.Location = new System.Drawing.Point(730, 26);
            this.btn_openlog.Name = "btn_openlog";
            this.btn_openlog.Size = new System.Drawing.Size(130, 36);
            this.btn_openlog.TabIndex = 2;
            this.btn_openlog.Text = "打开点检日志";
            this.btn_openlog.UseVisualStyleBackColor = true;
            this.btn_openlog.Click += new System.EventHandler(this.btn_openlog_Click);
            //
            // lbl_flow
            //
            this.lbl_flow.ForeColor = System.Drawing.Color.DarkRed;
            this.lbl_flow.Location = new System.Drawing.Point(12, 602);
            this.lbl_flow.Name = "lbl_flow";
            this.lbl_flow.Size = new System.Drawing.Size(520, 34);
            this.lbl_flow.TabIndex = 4;
            this.lbl_flow.Text = "运行流程接入状态";
            //
            // btn_debug
            //
            this.btn_debug.Location = new System.Drawing.Point(548, 600);
            this.btn_debug.Name = "btn_debug";
            this.btn_debug.Size = new System.Drawing.Size(110, 36);
            this.btn_debug.TabIndex = 7;
            this.btn_debug.Text = "联调工具";
            this.btn_debug.UseVisualStyleBackColor = true;
            this.btn_debug.Click += new System.EventHandler(this.btn_debug_Click);
            //
            // btn_save
            //
            this.btn_save.Location = new System.Drawing.Point(668, 600);
            this.btn_save.Name = "btn_save";
            this.btn_save.Size = new System.Drawing.Size(100, 36);
            this.btn_save.TabIndex = 5;
            this.btn_save.Text = "保存";
            this.btn_save.UseVisualStyleBackColor = true;
            this.btn_save.Click += new System.EventHandler(this.btn_save_Click);
            //
            // btn_close
            //
            this.btn_close.Location = new System.Drawing.Point(788, 600);
            this.btn_close.Name = "btn_close";
            this.btn_close.Size = new System.Drawing.Size(100, 36);
            this.btn_close.TabIndex = 6;
            this.btn_close.Text = "关闭";
            this.btn_close.UseVisualStyleBackColor = true;
            this.btn_close.Click += new System.EventHandler(this.btn_close_Click);
            //
            // tmr_refresh
            //
            this.tmr_refresh.Interval = 1000;
            this.tmr_refresh.Tick += new System.EventHandler(this.tmr_refresh_Tick);
            //
            // FrModuleCheckSet
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(900, 646);
            this.Controls.Add(this.btn_debug);
            this.Controls.Add(this.lbl_flow);
            this.Controls.Add(this.btn_close);
            this.Controls.Add(this.btn_save);
            this.Controls.Add(this.grp_status);
            this.Controls.Add(this.grp_samples);
            this.Controls.Add(this.grp_plan);
            this.Controls.Add(this.grp_basic);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "FrModuleCheckSet";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "模组自动点检设置";
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.FrModuleCheckSet_FormClosed);
            this.Load += new System.EventHandler(this.FrModuleCheckSet_Load);
            this.grp_basic.ResumeLayout(false);
            this.grp_basic.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.num_timeout)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_temp)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_retry)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_advance)).EndInit();
            this.grp_plan.ResumeLayout(false);
            this.grp_samples.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dgv_samples)).EndInit();
            this.grp_status.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.GroupBox grp_basic;
        private System.Windows.Forms.CheckBox chk_enabled;
        private System.Windows.Forms.Label lbl_station;
        private System.Windows.Forms.ComboBox cmb_station;
        private System.Windows.Forms.Label lbl_advance;
        private System.Windows.Forms.NumericUpDown num_advance;
        private System.Windows.Forms.Label lbl_retry;
        private System.Windows.Forms.NumericUpDown num_retry;
        private System.Windows.Forms.Label lbl_temp;
        private System.Windows.Forms.NumericUpDown num_temp;
        private System.Windows.Forms.Label lbl_timeout;
        private System.Windows.Forms.NumericUpDown num_timeout;
        private System.Windows.Forms.CheckBox chk_borrow;
        private System.Windows.Forms.GroupBox grp_plan;
        private System.Windows.Forms.ListBox lst_times;
        private System.Windows.Forms.DateTimePicker dtp_time;
        private System.Windows.Forms.Button btn_addtime;
        private System.Windows.Forms.Button btn_deltime;
        private System.Windows.Forms.Button btn_manual;
        private System.Windows.Forms.Label lbl_next;
        private System.Windows.Forms.GroupBox grp_samples;
        private System.Windows.Forms.DataGridView dgv_samples;
        private System.Windows.Forms.DataGridViewTextBoxColumn col_id;
        private System.Windows.Forms.DataGridViewTextBoxColumn col_tray;
        private System.Windows.Forms.DataGridViewTextBoxColumn col_pocket;
        private System.Windows.Forms.DataGridViewTextBoxColumn col_group;
        private System.Windows.Forms.Button btn_fill16;
        private System.Windows.Forms.Button btn_addrow;
        private System.Windows.Forms.Button btn_delrow;
        private System.Windows.Forms.Label lbl_samplehint;
        private System.Windows.Forms.GroupBox grp_status;
        private System.Windows.Forms.Label lbl_task;
        private System.Windows.Forms.Button btn_cancel;
        private System.Windows.Forms.Button btn_openlog;
        private System.Windows.Forms.Button btn_reconcile;
        private System.Windows.Forms.Label lbl_flow;
        private System.Windows.Forms.Button btn_save;
        private System.Windows.Forms.Button btn_close;
        private System.Windows.Forms.Button btn_debug;
        private System.Windows.Forms.Timer tmr_refresh;
    }
}

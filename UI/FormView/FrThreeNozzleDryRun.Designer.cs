namespace UI
{
    partial class FrThreeNozzleDryRun
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
            this.grp_params = new System.Windows.Forms.GroupBox();
            this.chk_return = new System.Windows.Forms.CheckBox();
            this.chk_prefetch = new System.Windows.Forms.CheckBox();
            this.chk_campass = new System.Windows.Forms.CheckBox();
            this.chk_zdescend = new System.Windows.Forms.CheckBox();
            this.chk_nocal = new System.Windows.Forms.CheckBox();
            this.chk_fixed = new System.Windows.Forms.CheckBox();
            this.lbl_scene = new System.Windows.Forms.Label();
            this.cmb_scene = new System.Windows.Forms.ComboBox();
            this.btn_simclear = new System.Windows.Forms.Button();
            this.btn_simfill = new System.Windows.Forms.Button();
            this.num_dwell = new System.Windows.Forms.NumericUpDown();
            this.lbl_dwell = new System.Windows.Forms.Label();
            this.num_margin = new System.Windows.Forms.NumericUpDown();
            this.lbl_margin = new System.Windows.Forms.Label();
            this.num_cycles = new System.Windows.Forms.NumericUpDown();
            this.lbl_cycles = new System.Windows.Forms.Label();
            this.cmb_range = new System.Windows.Forms.ComboBox();
            this.lbl_range = new System.Windows.Forms.Label();
            this.cmb_arm = new System.Windows.Forms.ComboBox();
            this.lbl_arm = new System.Windows.Forms.Label();
            this.cmb_station = new System.Windows.Forms.ComboBox();
            this.lbl_station = new System.Windows.Forms.Label();
            this.lbl_note = new System.Windows.Forms.Label();
            this.simView = new UI.ThreeNozzleSimView();
            this.btn_start = new System.Windows.Forms.Button();
            this.btn_stop = new System.Windows.Forms.Button();
            this.btn_home = new System.Windows.Forms.Button();
            this.btn_debug = new System.Windows.Forms.Button();
            this.chk_axhome = new System.Windows.Forms.CheckBox();
            this.chk_live = new System.Windows.Forms.CheckBox();
            this.tmr_live = new System.Windows.Forms.Timer(this.components);
            this.lbl_debug = new System.Windows.Forms.Label();
            this.lbl_status = new System.Windows.Forms.Label();
            this.txt_log = new System.Windows.Forms.TextBox();
            this.grp_params.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.num_dwell)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_margin)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_cycles)).BeginInit();
            this.SuspendLayout();
            //
            // grp_params
            //
            this.grp_params.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grp_params.Controls.Add(this.btn_simfill);
            this.grp_params.Controls.Add(this.btn_simclear);
            this.grp_params.Controls.Add(this.cmb_scene);
            this.grp_params.Controls.Add(this.lbl_scene);
            this.grp_params.Controls.Add(this.chk_fixed);
            this.grp_params.Controls.Add(this.chk_nocal);
            this.grp_params.Controls.Add(this.chk_prefetch);
            this.grp_params.Controls.Add(this.chk_return);
            this.grp_params.Controls.Add(this.chk_campass);
            this.grp_params.Controls.Add(this.chk_zdescend);
            this.grp_params.Controls.Add(this.num_dwell);
            this.grp_params.Controls.Add(this.lbl_dwell);
            this.grp_params.Controls.Add(this.num_margin);
            this.grp_params.Controls.Add(this.lbl_margin);
            this.grp_params.Controls.Add(this.num_cycles);
            this.grp_params.Controls.Add(this.lbl_cycles);
            this.grp_params.Controls.Add(this.cmb_range);
            this.grp_params.Controls.Add(this.lbl_range);
            this.grp_params.Controls.Add(this.cmb_arm);
            this.grp_params.Controls.Add(this.lbl_arm);
            this.grp_params.Controls.Add(this.cmb_station);
            this.grp_params.Controls.Add(this.lbl_station);
            this.grp_params.Location = new System.Drawing.Point(12, 12);
            this.grp_params.Name = "grp_params";
            this.grp_params.Size = new System.Drawing.Size(736, 216);
            this.grp_params.TabIndex = 0;
            this.grp_params.TabStop = false;
            this.grp_params.Text = "空跑参数";
            //
            // lbl_station
            //
            this.lbl_station.AutoSize = true;
            this.lbl_station.Location = new System.Drawing.Point(16, 32);
            this.lbl_station.Name = "lbl_station";
            this.lbl_station.Size = new System.Drawing.Size(80, 20);
            this.lbl_station.TabIndex = 0;
            this.lbl_station.Text = "穴位坐标";
            //
            // cmb_station
            //
            this.cmb_station.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmb_station.FormattingEnabled = true;
            this.cmb_station.SelectedIndexChanged += new System.EventHandler(this.OnSimOptionChanged);
            this.cmb_station.Location = new System.Drawing.Point(110, 28);
            this.cmb_station.Name = "cmb_station";
            this.cmb_station.Size = new System.Drawing.Size(130, 28);
            this.cmb_station.TabIndex = 1;
            //
            // lbl_arm
            //
            this.lbl_arm.AutoSize = true;
            this.lbl_arm.Location = new System.Drawing.Point(260, 32);
            this.lbl_arm.Name = "lbl_arm";
            this.lbl_arm.Size = new System.Drawing.Size(80, 20);
            this.lbl_arm.TabIndex = 2;
            this.lbl_arm.Text = "运行模组";
            //
            // cmb_arm
            //
            this.cmb_arm.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmb_arm.FormattingEnabled = true;
            this.cmb_arm.Items.AddRange(new object[] {
            "仅臂1",
            "仅臂2",
            "臂1→臂2 依次",
            "双臂同时（协同，含防撞）"});
            this.cmb_arm.SelectedIndexChanged += new System.EventHandler(this.OnSimOptionChanged);
            this.cmb_arm.Location = new System.Drawing.Point(350, 28);
            this.cmb_arm.Name = "cmb_arm";
            this.cmb_arm.DropDownWidth = 260;
            this.cmb_arm.Size = new System.Drawing.Size(140, 28);
            this.cmb_arm.TabIndex = 3;
            //
            // lbl_range
            //
            this.lbl_range.AutoSize = true;
            this.lbl_range.Location = new System.Drawing.Point(16, 72);
            this.lbl_range.Name = "lbl_range";
            this.lbl_range.Size = new System.Drawing.Size(80, 20);
            this.lbl_range.TabIndex = 4;
            this.lbl_range.Text = "穴位范围";
            //
            // cmb_range
            //
            this.cmb_range.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmb_range.FormattingEnabled = true;
            this.cmb_range.Items.AddRange(new object[] {
            "按双臂分配(臂1:5-8/13-16, 臂2:1-4/9-12)",
            "所选臂跑全部启用穴位"});
            this.cmb_range.SelectedIndexChanged += new System.EventHandler(this.OnSimOptionChanged);
            this.cmb_range.Location = new System.Drawing.Point(110, 68);
            this.cmb_range.Name = "cmb_range";
            this.cmb_range.Size = new System.Drawing.Size(380, 28);
            this.cmb_range.TabIndex = 5;
            //
            // lbl_cycles
            //
            this.lbl_cycles.AutoSize = true;
            this.lbl_cycles.Location = new System.Drawing.Point(510, 32);
            this.lbl_cycles.Name = "lbl_cycles";
            this.lbl_cycles.Size = new System.Drawing.Size(80, 20);
            this.lbl_cycles.TabIndex = 6;
            this.lbl_cycles.Text = "循环次数";
            //
            // num_cycles
            //
            this.num_cycles.Location = new System.Drawing.Point(620, 29);
            this.num_cycles.Maximum = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.num_cycles.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.num_cycles.Name = "num_cycles";
            this.num_cycles.Size = new System.Drawing.Size(100, 27);
            this.num_cycles.TabIndex = 7;
            this.num_cycles.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            //
            // lbl_margin
            //
            this.lbl_margin.AutoSize = true;
            this.lbl_margin.Location = new System.Drawing.Point(510, 72);
            this.lbl_margin.Name = "lbl_margin";
            this.lbl_margin.Size = new System.Drawing.Size(105, 20);
            this.lbl_margin.TabIndex = 8;
            this.lbl_margin.Text = "Z预留(mm)";
            //
            // num_margin
            //
            this.num_margin.DecimalPlaces = 1;
            this.num_margin.Increment = new decimal(new int[] {
            5,
            0,
            0,
            65536});
            this.num_margin.Location = new System.Drawing.Point(620, 69);
            this.num_margin.Maximum = new decimal(new int[] {
            50,
            0,
            0,
            0});
            this.num_margin.Minimum = new decimal(new int[] {
            5,
            0,
            0,
            65536});
            this.num_margin.Name = "num_margin";
            this.num_margin.Size = new System.Drawing.Size(100, 27);
            this.num_margin.TabIndex = 9;
            this.num_margin.Value = new decimal(new int[] {
            5,
            0,
            0,
            0});
            //
            // lbl_dwell
            //
            this.lbl_dwell.AutoSize = true;
            this.lbl_dwell.Location = new System.Drawing.Point(510, 112);
            this.lbl_dwell.Name = "lbl_dwell";
            this.lbl_dwell.Size = new System.Drawing.Size(105, 20);
            this.lbl_dwell.TabIndex = 10;
            this.lbl_dwell.Text = "停留(ms)";
            //
            // num_dwell
            //
            this.num_dwell.Increment = new decimal(new int[] {
            100,
            0,
            0,
            0});
            this.num_dwell.Location = new System.Drawing.Point(620, 109);
            this.num_dwell.Maximum = new decimal(new int[] {
            10000,
            0,
            0,
            0});
            this.num_dwell.Name = "num_dwell";
            this.num_dwell.Size = new System.Drawing.Size(100, 27);
            this.num_dwell.TabIndex = 11;
            this.num_dwell.Value = new decimal(new int[] {
            300,
            0,
            0,
            0});
            //
            // chk_zdescend
            //
            this.chk_zdescend.AutoSize = true;
            this.chk_zdescend.Checked = true;
            this.chk_zdescend.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chk_zdescend.Location = new System.Drawing.Point(20, 112);
            this.chk_zdescend.Name = "chk_zdescend";
            this.chk_zdescend.Size = new System.Drawing.Size(91, 24);
            this.chk_zdescend.TabIndex = 12;
            this.chk_zdescend.Text = "Z 下探";
            this.chk_zdescend.UseVisualStyleBackColor = true;
            //
            // chk_campass
            //
            this.chk_campass.AutoSize = true;
            this.chk_campass.Location = new System.Drawing.Point(140, 112);
            this.chk_campass.Name = "chk_campass";
            this.chk_campass.Size = new System.Drawing.Size(201, 24);
            this.chk_campass.TabIndex = 13;
            this.chk_campass.Text = "经过相机位(不拍照)";
            this.chk_campass.UseVisualStyleBackColor = true;
            //
            // chk_return
            //
            this.chk_return.AutoSize = true;
            this.chk_return.Checked = true;
            this.chk_return.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chk_return.Location = new System.Drawing.Point(350, 112);
            this.chk_return.Name = "chk_return";
            this.chk_return.Size = new System.Drawing.Size(141, 24);
            this.chk_return.TabIndex = 14;
            this.chk_return.Text = "结束回待机位";
            this.chk_return.UseVisualStyleBackColor = true;
            //
            // chk_prefetch
            //
            this.chk_prefetch.AutoSize = true;
            this.chk_prefetch.Location = new System.Drawing.Point(510, 112);
            this.chk_prefetch.Name = "chk_prefetch";
            this.chk_prefetch.Size = new System.Drawing.Size(161, 24);
            this.chk_prefetch.TabIndex = 24;
            this.chk_prefetch.Text = "预取提前第一组";
            this.chk_prefetch.UseVisualStyleBackColor = true;
            // 
            // chk_nocal
            // 
            this.chk_nocal.AutoSize = true;
            this.chk_nocal.ForeColor = System.Drawing.Color.DarkRed;
            this.chk_nocal.Location = new System.Drawing.Point(20, 142);
            this.chk_nocal.Name = "chk_nocal";
            this.chk_nocal.Size = new System.Drawing.Size(361, 24);
            this.chk_nocal.TabIndex = 17;
            this.chk_nocal.Text = "不用Z测高补偿（仅验证流程，三嘴按N1坐标下探）";
            this.chk_nocal.UseVisualStyleBackColor = true;
            // 
            // chk_fixed
            // 
            this.chk_fixed.AutoSize = true;
            this.chk_fixed.ForeColor = System.Drawing.Color.DarkRed;
            this.chk_fixed.Location = new System.Drawing.Point(400, 142);
            this.chk_fixed.Name = "chk_fixed";
            this.chk_fixed.Size = new System.Drawing.Size(301, 24);
            this.chk_fixed.TabIndex = 18;
            this.chk_fixed.Text = "固定下探（从Z原点向下，不看示教高度）";
            this.chk_fixed.UseVisualStyleBackColor = true;
            this.chk_fixed.CheckedChanged += new System.EventHandler(this.chk_fixed_CheckedChanged);
            // 
            // lbl_scene
            // 
            this.lbl_scene.AutoSize = true;
            this.lbl_scene.Location = new System.Drawing.Point(16, 182);
            this.lbl_scene.Name = "lbl_scene";
            this.lbl_scene.Size = new System.Drawing.Size(39, 20);
            this.lbl_scene.TabIndex = 20;
            this.lbl_scene.Text = "场景";
            // 
            // cmb_scene
            // 
            this.cmb_scene.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmb_scene.FormattingEnabled = true;
            this.cmb_scene.Items.AddRange(new object[] {
            "上料/交换（穴上有料=取旧放新，没料=只放新）",
            "只下料（清料：有料的穴取出落OK盘）"});
            this.cmb_scene.Location = new System.Drawing.Point(110, 178);
            this.cmb_scene.Name = "cmb_scene";
            this.cmb_scene.Size = new System.Drawing.Size(300, 28);
            this.cmb_scene.TabIndex = 21;
            // 
            // btn_simclear
            // 
            this.btn_simclear.Location = new System.Drawing.Point(424, 176);
            this.btn_simclear.Name = "btn_simclear";
            this.btn_simclear.Size = new System.Drawing.Size(140, 32);
            this.btn_simclear.TabIndex = 22;
            this.btn_simclear.Text = "清空模拟夹具";
            this.btn_simclear.UseVisualStyleBackColor = true;
            this.btn_simclear.Click += new System.EventHandler(this.btn_simclear_Click);
            // 
            // btn_simfill
            // 
            this.btn_simfill.Location = new System.Drawing.Point(574, 176);
            this.btn_simfill.Name = "btn_simfill";
            this.btn_simfill.Size = new System.Drawing.Size(150, 32);
            this.btn_simfill.TabIndex = 23;
            this.btn_simfill.Text = "模拟夹具装满旧料";
            this.btn_simfill.UseVisualStyleBackColor = true;
            this.btn_simfill.Click += new System.EventHandler(this.btn_simfill_Click);
            //
            // lbl_note
            //
            this.lbl_note.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)));
            this.lbl_note.ForeColor = System.Drawing.Color.DarkRed;
            this.lbl_note.Location = new System.Drawing.Point(12, 236);
            this.lbl_note.Name = "lbl_note";
            this.lbl_note.Size = new System.Drawing.Size(524, 48);
            this.lbl_note.TabIndex = 1;
            this.lbl_note.Text = "空跑不开真空、不触发相机、不改变物料和料盘数据，料仓只移动料盘X轴。运行前请调低速度，并关闭 UD_MOV_SAFE（该项依赖转盘）。";
            //
            // btn_start
            //
            this.btn_start.Location = new System.Drawing.Point(12, 290);
            this.btn_start.Name = "btn_start";
            this.btn_start.Size = new System.Drawing.Size(140, 44);
            this.btn_start.TabIndex = 2;
            this.btn_start.Text = "开始空跑";
            this.btn_start.UseVisualStyleBackColor = true;
            this.btn_start.Click += new System.EventHandler(this.btn_start_Click);
            //
            // btn_stop
            //
            this.btn_stop.Enabled = false;
            this.btn_stop.Location = new System.Drawing.Point(164, 290);
            this.btn_stop.Name = "btn_stop";
            this.btn_stop.Size = new System.Drawing.Size(140, 44);
            this.btn_stop.TabIndex = 3;
            this.btn_stop.Text = "停止";
            this.btn_stop.UseVisualStyleBackColor = true;
            this.btn_stop.Click += new System.EventHandler(this.btn_stop_Click);
            //
            // btn_home
            //
            this.btn_home.Location = new System.Drawing.Point(316, 290);
            this.btn_home.Name = "btn_home";
            this.btn_home.Size = new System.Drawing.Size(160, 44);
            this.btn_home.TabIndex = 6;
            this.btn_home.Text = "仅上下料回零";
            this.btn_home.UseVisualStyleBackColor = true;
            this.btn_home.Click += new System.EventHandler(this.btn_home_Click);
            //
            // btn_debug
            //
            this.btn_debug.Location = new System.Drawing.Point(488, 290);
            this.btn_debug.Name = "btn_debug";
            this.btn_debug.Size = new System.Drawing.Size(160, 44);
            this.btn_debug.TabIndex = 7;
            this.btn_debug.Text = "调试模式(急停屏蔽)";
            this.btn_debug.UseVisualStyleBackColor = true;
            this.btn_debug.Click += new System.EventHandler(this.btn_debug_Click);
            //
            // chk_axhome
            //
            this.chk_axhome.AutoSize = true;
            this.chk_axhome.Location = new System.Drawing.Point(544, 262);
            this.chk_axhome.Name = "chk_axhome";
            this.chk_axhome.Size = new System.Drawing.Size(200, 24);
            this.chk_axhome.TabIndex = 20;
            this.chk_axhome.Text = "允许XY单独回零(调试)";
            this.chk_axhome.UseVisualStyleBackColor = true;
            this.chk_axhome.CheckedChanged += new System.EventHandler(this.chk_axhome_CheckedChanged);
            //
            // chk_live
            //
            this.chk_live.AutoSize = true;
            this.chk_live.Location = new System.Drawing.Point(544, 236);
            this.chk_live.Name = "chk_live";
            this.chk_live.Size = new System.Drawing.Size(200, 24);
            this.chk_live.TabIndex = 21;
            this.chk_live.Text = "跟随系统运行(实时)";
            this.chk_live.UseVisualStyleBackColor = true;
            this.chk_live.CheckedChanged += new System.EventHandler(this.chk_live_CheckedChanged);
            //
            // tmr_live
            //
            this.tmr_live.Interval = 300;
            this.tmr_live.Tick += new System.EventHandler(this.tmr_live_Tick);
            //
            // lbl_debug
            //
            this.lbl_debug.Location = new System.Drawing.Point(12, 342);
            this.lbl_debug.Name = "lbl_debug";
            this.lbl_debug.Size = new System.Drawing.Size(360, 24);
            this.lbl_debug.TabIndex = 8;
            this.lbl_debug.Text = "调试模式：未开启";
            //
            // lbl_status
            //
            this.lbl_status.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lbl_status.Location = new System.Drawing.Point(380, 342);
            this.lbl_status.Name = "lbl_status";
            this.lbl_status.Size = new System.Drawing.Size(368, 24);
            this.lbl_status.TabIndex = 4;
            this.lbl_status.Text = "就绪";
            //
            // txt_log
            //
            this.txt_log.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txt_log.Font = new System.Drawing.Font("Consolas", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txt_log.Location = new System.Drawing.Point(12, 372);
            this.txt_log.Multiline = true;
            this.txt_log.Name = "txt_log";
            this.txt_log.ReadOnly = true;
            this.txt_log.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txt_log.Size = new System.Drawing.Size(736, 304);
            this.txt_log.TabIndex = 5;
            this.txt_log.WordWrap = false;
            // 
            // simView
            // 
            this.simView.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.simView.BackColor = System.Drawing.Color.WhiteSmoke;
            this.simView.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.simView.Font = new System.Drawing.Font("Microsoft YaHei UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.simView.Location = new System.Drawing.Point(760, 12);
            this.simView.Name = "simView";
            this.simView.Size = new System.Drawing.Size(520, 664);
            this.simView.TabIndex = 19;
            //
            // FrThreeNozzleDryRun
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1292, 688);
            this.Controls.Add(this.simView);
            this.Controls.Add(this.txt_log);
            this.Controls.Add(this.lbl_status);
            this.Controls.Add(this.lbl_debug);
            this.Controls.Add(this.btn_debug);
            this.Controls.Add(this.chk_axhome);
            this.Controls.Add(this.chk_live);
            this.Controls.Add(this.btn_home);
            this.Controls.Add(this.btn_stop);
            this.Controls.Add(this.btn_start);
            this.Controls.Add(this.lbl_note);
            this.Controls.Add(this.grp_params);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.MinimumSize = new System.Drawing.Size(1310, 640);
            this.Name = "FrThreeNozzleDryRun";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "三吸嘴空跑（仅流程动作）";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.FrThreeNozzleDryRun_FormClosing);
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.FrThreeNozzleDryRun_FormClosed);
            this.Load += new System.EventHandler(this.FrThreeNozzleDryRun_Load);
            this.grp_params.ResumeLayout(false);
            this.grp_params.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.num_dwell)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_margin)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.num_cycles)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.GroupBox grp_params;
        private System.Windows.Forms.Label lbl_station;
        private System.Windows.Forms.ComboBox cmb_station;
        private System.Windows.Forms.Label lbl_arm;
        private System.Windows.Forms.ComboBox cmb_arm;
        private System.Windows.Forms.Label lbl_range;
        private System.Windows.Forms.ComboBox cmb_range;
        private System.Windows.Forms.Label lbl_cycles;
        private System.Windows.Forms.NumericUpDown num_cycles;
        private System.Windows.Forms.Label lbl_margin;
        private System.Windows.Forms.NumericUpDown num_margin;
        private System.Windows.Forms.Label lbl_dwell;
        private System.Windows.Forms.NumericUpDown num_dwell;
        private System.Windows.Forms.CheckBox chk_zdescend;
        private System.Windows.Forms.CheckBox chk_campass;
        private System.Windows.Forms.CheckBox chk_return;
        private System.Windows.Forms.CheckBox chk_prefetch;
        private System.Windows.Forms.CheckBox chk_nocal;
        private System.Windows.Forms.CheckBox chk_fixed;
        private UI.ThreeNozzleSimView simView;
        private System.Windows.Forms.Label lbl_scene;
        private System.Windows.Forms.ComboBox cmb_scene;
        private System.Windows.Forms.Button btn_simclear;
        private System.Windows.Forms.Button btn_simfill;
        private System.Windows.Forms.Label lbl_note;
        private System.Windows.Forms.Button btn_start;
        private System.Windows.Forms.Button btn_stop;
        private System.Windows.Forms.Button btn_home;
        private System.Windows.Forms.Button btn_debug;
        private System.Windows.Forms.CheckBox chk_axhome;
        private System.Windows.Forms.CheckBox chk_live;
        private System.Windows.Forms.Timer tmr_live;
        private System.Windows.Forms.Label lbl_debug;
        private System.Windows.Forms.Label lbl_status;
        private System.Windows.Forms.TextBox txt_log;
    }
}

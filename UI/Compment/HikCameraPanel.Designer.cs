namespace UI
{
    partial class HikCameraPanel
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region 组件设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            this.lbl_sn = new System.Windows.Forms.Label();
            this.cmb_sn = new System.Windows.Forms.ComboBox();
            this.lbl_disc = new System.Windows.Forms.Label();
            this.txt_disc = new System.Windows.Forms.TextBox();
            this.lbl_source = new System.Windows.Forms.Label();
            this.cmb_source = new System.Windows.Forms.ComboBox();
            this.btn_scan = new System.Windows.Forms.Button();
            this.btn_add = new System.Windows.Forms.Button();
            this.btn_apply = new System.Windows.Forms.Button();
            this.btn_connect = new System.Windows.Forms.Button();
            this.btn_remove = new System.Windows.Forms.Button();
            this.lbl_status = new System.Windows.Forms.Label();
            this.dgv_cams = new System.Windows.Forms.DataGridView();
            this.col_name = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.col_disc = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.col_sn = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.col_backend = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.col_state = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.col_online = new System.Windows.Forms.DataGridViewTextBoxColumn();
            ((System.ComponentModel.ISupportInitialize)(this.dgv_cams)).BeginInit();
            this.SuspendLayout();
            //
            // lbl_sn
            //
            this.lbl_sn.AutoSize = true;
            this.lbl_sn.Location = new System.Drawing.Point(4, 8);
            this.lbl_sn.Name = "lbl_sn";
            this.lbl_sn.Size = new System.Drawing.Size(44, 17);
            this.lbl_sn.TabIndex = 0;
            this.lbl_sn.Text = "序列号";
            //
            // cmb_sn
            //
            this.cmb_sn.DropDownWidth = 300;
            this.cmb_sn.FormattingEnabled = true;
            this.cmb_sn.Location = new System.Drawing.Point(52, 4);
            this.cmb_sn.Name = "cmb_sn";
            this.cmb_sn.Size = new System.Drawing.Size(170, 25);
            this.cmb_sn.TabIndex = 1;
            this.cmb_sn.SelectedIndexChanged += new System.EventHandler(this.cmb_sn_SelectedIndexChanged);
            //
            // lbl_disc
            //
            this.lbl_disc.AutoSize = true;
            this.lbl_disc.Location = new System.Drawing.Point(232, 8);
            this.lbl_disc.Name = "lbl_disc";
            this.lbl_disc.Size = new System.Drawing.Size(32, 17);
            this.lbl_disc.TabIndex = 2;
            this.lbl_disc.Text = "描述";
            //
            // txt_disc
            //
            this.txt_disc.Location = new System.Drawing.Point(268, 4);
            this.txt_disc.Name = "txt_disc";
            this.txt_disc.Size = new System.Drawing.Size(140, 23);
            this.txt_disc.TabIndex = 3;
            //
            // lbl_source
            //
            this.lbl_source.AutoSize = true;
            this.lbl_source.Location = new System.Drawing.Point(418, 8);
            this.lbl_source.Name = "lbl_source";
            this.lbl_source.Size = new System.Drawing.Size(56, 17);
            this.lbl_source.TabIndex = 4;
            this.lbl_source.Text = "触发方式";
            //
            // cmb_source
            //
            this.cmb_source.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmb_source.FormattingEnabled = true;
            this.cmb_source.Items.AddRange(new object[] {
            "软触发",
            "硬触发"});
            this.cmb_source.Location = new System.Drawing.Point(478, 4);
            this.cmb_source.Name = "cmb_source";
            this.cmb_source.Size = new System.Drawing.Size(100, 25);
            this.cmb_source.TabIndex = 5;
            //
            // btn_scan
            //
            this.btn_scan.Location = new System.Drawing.Point(4, 36);
            this.btn_scan.Name = "btn_scan";
            this.btn_scan.Size = new System.Drawing.Size(110, 29);
            this.btn_scan.TabIndex = 6;
            this.btn_scan.Text = "扫描在线相机";
            this.btn_scan.UseVisualStyleBackColor = true;
            this.btn_scan.Click += new System.EventHandler(this.btn_scan_Click);
            //
            // btn_add
            //
            this.btn_add.Location = new System.Drawing.Point(120, 36);
            this.btn_add.Name = "btn_add";
            this.btn_add.Size = new System.Drawing.Size(92, 29);
            this.btn_add.TabIndex = 7;
            this.btn_add.Text = "添加相机";
            this.btn_add.UseVisualStyleBackColor = true;
            this.btn_add.Click += new System.EventHandler(this.btn_add_Click);
            //
            // btn_apply
            //
            this.btn_apply.Location = new System.Drawing.Point(218, 36);
            this.btn_apply.Name = "btn_apply";
            this.btn_apply.Size = new System.Drawing.Size(100, 29);
            this.btn_apply.TabIndex = 8;
            this.btn_apply.Text = "保存到选中";
            this.btn_apply.UseVisualStyleBackColor = true;
            this.btn_apply.Click += new System.EventHandler(this.btn_apply_Click);
            //
            // btn_connect
            //
            this.btn_connect.Location = new System.Drawing.Point(324, 36);
            this.btn_connect.Name = "btn_connect";
            this.btn_connect.Size = new System.Drawing.Size(92, 29);
            this.btn_connect.TabIndex = 9;
            this.btn_connect.Text = "重新连接";
            this.btn_connect.UseVisualStyleBackColor = true;
            this.btn_connect.Click += new System.EventHandler(this.btn_connect_Click);
            //
            // btn_remove
            //
            this.btn_remove.Location = new System.Drawing.Point(422, 36);
            this.btn_remove.Name = "btn_remove";
            this.btn_remove.Size = new System.Drawing.Size(92, 29);
            this.btn_remove.TabIndex = 10;
            this.btn_remove.Text = "删除相机";
            this.btn_remove.UseVisualStyleBackColor = true;
            this.btn_remove.Click += new System.EventHandler(this.btn_remove_Click);
            //
            // lbl_status
            //
            this.lbl_status.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lbl_status.AutoEllipsis = true;
            this.lbl_status.Location = new System.Drawing.Point(4, 210);
            this.lbl_status.Name = "lbl_status";
            this.lbl_status.Size = new System.Drawing.Size(792, 20);
            this.lbl_status.TabIndex = 12;
            this.lbl_status.Text = "选中一行即在上方 VisionPro 窗口里切到该相机，用窗口自带的「触发」「实况」按钮取图。";
            this.lbl_status.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // dgv_cams
            //
            this.dgv_cams.AllowUserToAddRows = false;
            this.dgv_cams.AllowUserToDeleteRows = false;
            this.dgv_cams.AllowUserToResizeRows = false;
            this.dgv_cams.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.dgv_cams.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.dgv_cams.BackgroundColor = System.Drawing.Color.White;
            this.dgv_cams.ColumnHeadersHeight = 26;
            this.dgv_cams.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.dgv_cams.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.col_name,
            this.col_disc,
            this.col_sn,
            this.col_backend,
            this.col_state,
            this.col_online});
            dataGridViewCellStyle1.SelectionBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(43)))), ((int)(((byte)(144)))), ((int)(((byte)(217)))));
            this.dgv_cams.DefaultCellStyle = dataGridViewCellStyle1;
            this.dgv_cams.Location = new System.Drawing.Point(4, 71);
            this.dgv_cams.MultiSelect = false;
            this.dgv_cams.Name = "dgv_cams";
            this.dgv_cams.ReadOnly = true;
            this.dgv_cams.RowHeadersVisible = false;
            this.dgv_cams.RowTemplate.Height = 24;
            this.dgv_cams.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgv_cams.Size = new System.Drawing.Size(792, 135);
            this.dgv_cams.TabIndex = 11;
            this.dgv_cams.SelectionChanged += new System.EventHandler(this.dgv_cams_SelectionChanged);
            //
            // col_name
            //
            this.col_name.FillWeight = 70F;
            this.col_name.HeaderText = "名称";
            this.col_name.Name = "col_name";
            this.col_name.ReadOnly = true;
            this.col_name.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // col_disc
            //
            this.col_disc.FillWeight = 90F;
            this.col_disc.HeaderText = "描述";
            this.col_disc.Name = "col_disc";
            this.col_disc.ReadOnly = true;
            this.col_disc.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // col_sn
            //
            this.col_sn.FillWeight = 100F;
            this.col_sn.HeaderText = "序列号";
            this.col_sn.Name = "col_sn";
            this.col_sn.ReadOnly = true;
            this.col_sn.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // col_backend
            //
            this.col_backend.FillWeight = 60F;
            this.col_backend.HeaderText = "触发方式";
            this.col_backend.Name = "col_backend";
            this.col_backend.ReadOnly = true;
            this.col_backend.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // col_state
            //
            this.col_state.FillWeight = 60F;
            this.col_state.HeaderText = "状态";
            this.col_state.Name = "col_state";
            this.col_state.ReadOnly = true;
            this.col_state.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // col_online
            //
            this.col_online.FillWeight = 110F;
            this.col_online.HeaderText = "扫描结果";
            this.col_online.Name = "col_online";
            this.col_online.ReadOnly = true;
            this.col_online.SortMode = System.Windows.Forms.DataGridViewColumnSortMode.NotSortable;
            //
            // HikCameraPanel
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.lbl_status);
            this.Controls.Add(this.dgv_cams);
            this.Controls.Add(this.btn_remove);
            this.Controls.Add(this.btn_connect);
            this.Controls.Add(this.btn_apply);
            this.Controls.Add(this.btn_add);
            this.Controls.Add(this.btn_scan);
            this.Controls.Add(this.cmb_source);
            this.Controls.Add(this.lbl_source);
            this.Controls.Add(this.txt_disc);
            this.Controls.Add(this.lbl_disc);
            this.Controls.Add(this.cmb_sn);
            this.Controls.Add(this.lbl_sn);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.MinimumSize = new System.Drawing.Size(600, 180);
            this.Name = "HikCameraPanel";
            this.Size = new System.Drawing.Size(800, 232);
            ((System.ComponentModel.ISupportInitialize)(this.dgv_cams)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lbl_sn;
        private System.Windows.Forms.ComboBox cmb_sn;
        private System.Windows.Forms.Label lbl_disc;
        private System.Windows.Forms.TextBox txt_disc;
        private System.Windows.Forms.Label lbl_source;
        private System.Windows.Forms.ComboBox cmb_source;
        private System.Windows.Forms.Button btn_scan;
        private System.Windows.Forms.Button btn_add;
        private System.Windows.Forms.Button btn_apply;
        private System.Windows.Forms.Button btn_connect;
        private System.Windows.Forms.Button btn_remove;
        private System.Windows.Forms.Label lbl_status;
        private System.Windows.Forms.DataGridView dgv_cams;
        private System.Windows.Forms.DataGridViewTextBoxColumn col_name;
        private System.Windows.Forms.DataGridViewTextBoxColumn col_disc;
        private System.Windows.Forms.DataGridViewTextBoxColumn col_sn;
        private System.Windows.Forms.DataGridViewTextBoxColumn col_backend;
        private System.Windows.Forms.DataGridViewTextBoxColumn col_state;
        private System.Windows.Forms.DataGridViewTextBoxColumn col_online;
    }
}

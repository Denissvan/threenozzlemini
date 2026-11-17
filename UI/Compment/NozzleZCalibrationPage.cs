using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace UI
{
    public sealed class NozzleZCalibrationPage : UserControl
    {
        private readonly ComboBox armSelector = new ComboBox();
        private readonly DataGridView grid = new DataGridView(), points = new DataGridView(), records = new DataGridView();
        private readonly Label baseline = new Label(), status = new Label();
        private readonly NumericUpDown speed = NumberBox(0.01M, 10M, 1M), step = NumberBox(0.001M, 0.1M, 0.02M), spread = NumberBox(0.001M, 1M, 0.05M);
        private readonly Button cancel = new Button(), cancelPosition = new Button();
        private readonly FlowLayoutPanel actions = new FlowLayoutPanel(), pointActions = new FlowLayoutPanel();
        private readonly TabControl tabs = new TabControl();
        private NozzleZCalibrationData pending, active;
        private bool updating, dirty, running, loadFailed;
        private int selectedArm;
        private readonly string settingsPath;

        public NozzleZCalibrationPage(int arm, string draftPath, Func<int, int, double> unusedRead = null)
        {
            settingsPath = draftPath;
            Font = new Font("宋体", 10.5F); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96,96);
            Size = new Size(876,306); BackColor = Color.FromArgb(217,225,232); AutoScroll = true;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4), ColumnCount = 1, RowCount = 3, MinimumSize = new Size(820,296) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,34)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,26)); Controls.Add(root);
            var top = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            top.Controls.Add(new Label { Text = "机械臂", AutoSize = true, Margin = new Padding(2,7,6,0) });
            armSelector.DropDownStyle = ComboBoxStyle.DropDownList; armSelector.Width = 115; armSelector.Items.AddRange(new object[]{"上下料臂1","上下料臂2"}); top.Controls.Add(armSelector);
            baseline.AutoSize = true; baseline.Margin = new Padding(10,7,0,0); top.Controls.Add(baseline); root.Controls.Add(top,0,0);
            tabs.Name = "zCalibrationTabs"; tabs.Dock = DockStyle.Fill;
            var measureTab = new TabPage("测高与补偿"); var pointTab = new TabPage("基准点设置"); var recordTab = new TabPage("三次测量记录");
            tabs.TabPages.AddRange(new[]{measureTab,pointTab,recordTab}); root.Controls.Add(tabs,0,1);
            var measure = Layout(measureTab);
            measure.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "测高仅供记录：当前取放直接使用实际 Z 轴坐标，不使用本页补偿。", TextAlign = ContentAlignment.MiddleLeft },0,0);
            SetupGrid(grid,new[]{"吸嘴","接触均值","极差","已存补偿","候选补偿","状态"}); grid.ReadOnly = true; measure.Controls.Add(grid,0,1);
            actions.Dock = DockStyle.Fill; actions.WrapContents = false;
            actions.Controls.Add(Button("测量当前嘴",118,(s,e)=>StartMeasure(false)));
            actions.Controls.Add(Button("依次测量三嘴",128,(s,e)=>StartMeasure(true)));
            actions.Controls.Add(Button("保存本臂结果",128,SaveResult));
            actions.Controls.Add(Button("放弃本次结果",128,(s,e)=>{if(CanDiscard()) LoadArm();}));
            cancel.Text = "停止测量"; cancel.Size = new Size(110,34); cancel.Enabled = false; cancel.Click += (s,e)=>NozzleZCalibrationMotion.Cancel(); actions.Controls.Add(cancel); measure.Controls.Add(actions,0,2);
            var pointLayout = Layout(pointTab);
            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            AddOption(options,"探测速度",speed); AddOption(options,"步距",step); AddOption(options,"极差上限",spread);
            options.Controls.Add(new Label { Text = "mm/s、mm；初值须现场核对", AutoSize = true, Margin = new Padding(8,5,0,0) }); pointLayout.Controls.Add(options,0,0);
            SetupGrid(points,new[]{"吸嘴","机械轴X","机械轴Y","U角度","接近高度","探测终点"}); points.Columns[0].ReadOnly=true; pointLayout.Controls.Add(points,0,1);
            points.CellValueChanged += (s,e)=>{if(!updating && e.RowIndex>=0) dirty=true;};
            foreach(var n in new[]{speed,step,spread}) n.ValueChanged += (s,e)=>{if(!updating)dirty=true;};
            pointActions.Dock=DockStyle.Fill; pointActions.WrapContents=false;
            pointActions.Controls.Add(Button("学习当前嘴位置",150,Learn)); pointActions.Controls.Add(Button("定位到接近位",140,Position));
            pointActions.Controls.Add(Button("保存设置草稿",140,SaveSettings));
            cancelPosition.Text="停止定位";cancelPosition.Size=new Size(110,34);cancelPosition.Enabled=false;cancelPosition.Click+=(s,e)=>NozzleZCalibrationMotion.Cancel();pointActions.Controls.Add(cancelPosition);pointLayout.Controls.Add(pointActions,0,2);
            var recLayout=Layout(recordTab);
            recLayout.Controls.Add(new Label { Text="显示本次候选记录，未保存不影响已生效补偿。",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);
            SetupGrid(records,new[]{"吸嘴","第1次","第2次","第3次","测量时间","方向"});records.ReadOnly=true;records.Columns[4].FillWeight=185;recLayout.Controls.Add(records,0,1);
            recLayout.Controls.Add(new Label { Text="首次先测N1建立本臂基准；后续重标任何嘴均保持原基准。",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,2);
            status.Dock=DockStyle.Fill;status.TextAlign=ContentAlignment.MiddleLeft;root.Controls.Add(status,0,2);
            try { active=NozzleZCalibration.GetSnapshot(); }
            catch(Exception ex){loadFailed=true;active=new NozzleZCalibrationData();status.Text="正式标定读取失败，禁止覆盖："+ex.Message;}
            selectedArm=arm==1?1:0;armSelector.SelectedIndex=selectedArm;
            armSelector.SelectedIndexChanged+=(s,e)=>{
                if(updating)return;
                if(running || !CanDiscard()){updating=true;armSelector.SelectedIndex=selectedArm;updating=false;return;}
                selectedArm=armSelector.SelectedIndex;LoadArm();};
            LoadArm();
        }
        private static NumericUpDown NumberBox(decimal min,decimal max,decimal value){return new NumericUpDown{Minimum=min,Maximum=max,Value=value,DecimalPlaces=3,Increment=0.001M,Width=72};}
        private static void AddOption(FlowLayoutPanel p,string text,Control c){p.Controls.Add(new Label{Text=text,AutoSize=true,Margin=new Padding(2,5,2,0)});p.Controls.Add(c);}
        private static Button Button(string text,int width,EventHandler click){var b=new Button{Text=text,Size=new Size(width,34)};b.Click+=click;return b;}
        private static TableLayoutPanel Layout(TabPage tab){var p=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(3),ColumnCount=1,RowCount=3};p.RowStyles.Add(new RowStyle(SizeType.Absolute,30));p.RowStyles.Add(new RowStyle(SizeType.Percent,100));p.RowStyles.Add(new RowStyle(SizeType.Absolute,44));tab.Controls.Add(p);return p;}
        private static void SetupGrid(DataGridView g,string[] columns){g.Dock=DockStyle.Fill;g.AllowUserToAddRows=g.AllowUserToDeleteRows=g.AllowUserToResizeRows=false;g.RowHeadersVisible=false;g.MultiSelect=false;g.BackgroundColor=Color.White;g.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;g.ColumnHeadersHeight=28;g.RowTemplate.Height=28;foreach(var title in columns){int i=g.Columns.Add("c"+g.Columns.Count,title);g.Columns[i].SortMode=DataGridViewColumnSortMode.NotSortable;}g.Columns[0].FillWeight=50;}
        private static string Num(double? v){return v.HasValue?v.Value.ToString("F3"):"—";}
        private static string Entry(double? v){return v.HasValue?v.Value.ToString("F3"):"";}
        private bool CanDiscard(){points.EndEdit();return !dirty || MessageBox.Show(this,"本次设置或测量未保存，是否放弃？","测高",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes;}
        public void SelectArm(int arm){armSelector.SelectedIndex=arm==1?1:0;tabs.SelectedIndex=0;}
        public void ShowRecords(){tabs.SelectedIndex=2;}
        public bool HasPendingEdits{get{return dirty;}}
        private void LoadArm()
        {
            updating=true;pending=active.Copy();
            if(!loadFailed && System.IO.File.Exists(settingsPath))
            {
                try { var settings=NozzleZCalibrationDraft.Load(settingsPath).Arms[selectedArm];
                    var target=pending.Arms[selectedArm];
                    if(settings.SettingsSaved || !target.Baseline.HasValue)
                    {
                        bool changed=target.Speed!=settings.Speed || target.Step!=settings.Step || target.MaxSpread!=settings.MaxSpread;
                        for(int i=0;i<3;i++)
                        {
                            var old=target.Nozzles[i].Point;var next=settings.Points[i];
                            if(changed || old.X!=next.X || old.Y!=next.Y || old.U!=next.U || old.Approach!=next.Approach || old.End!=next.End)
                                target.Nozzles[i].Samples=new double[0];
                            target.Nozzles[i].Point=next.Copy();
                        }
                        target.Speed=settings.Speed;target.Step=settings.Step;target.MaxSpread=settings.MaxSpread;
                        pending.ValidateStructure();
                    }
                } catch(Exception ex){status.Text="设置草稿无效："+ex.Message;pending=active.Copy();}
            }
            var a=pending.Arms[selectedArm];points.Rows.Clear();
            for(int i=0;i<3;i++){var p=a.Nozzles[i].Point;points.Rows.Add("N"+(i+1),Entry(p.X),Entry(p.Y),Entry(p.U),Entry(p.Approach),Entry(p.End));}
            speed.Value=(decimal)a.Speed;step.Value=(decimal)a.Step;spread.Value=(decimal)a.MaxSpread;
            dirty=false;updating=false;RefreshResults();SetRunning(false);
        }
        private void RefreshResults()
        {
            var a=pending.Arms[selectedArm];grid.Rows.Clear();records.Rows.Clear();
            baseline.Text="固定基准 B："+Num(a.Baseline)+" mm  / "+(active.Arms[selectedArm].Baseline.HasValue?"重标，基准不变":"首次建立，待保存");
            for(int i=0;i<3;i++){
                var n=a.Nozzles[i];bool measured=n.Samples.Length==3;var old=active.Arms[selectedArm];
                grid.Rows.Add("N"+(i+1),measured?Num(n.Mean):"—",measured?Num(n.Spread):"—",old.Baseline.HasValue?Num(active.Offset(selectedArm,i)):"—",measured&&a.Baseline.HasValue?Num(pending.Offset(selectedArm,i)):"—",measured?"已测量":"待测量");
                records.Rows.Add("N"+(i+1),measured?Num(n.Samples[0]):"—",measured?Num(n.Samples[1]):"—",measured?Num(n.Samples[2]):"—",measured?n.MeasuredAt:"—",measured?n.Direction.ToString():"—");
            }
        }
        private NozzleZCalibrationData ReadSettings()
        {
            points.EndEdit();var data=pending.Copy();var a=data.Arms[selectedArm];
            bool changed=a.Speed!=(double)speed.Value || a.Step!=(double)step.Value || a.MaxSpread!=(double)spread.Value;
            a.Speed=(double)speed.Value;a.Step=(double)step.Value;a.MaxSpread=(double)spread.Value;
            for(int row=0;row<3;row++){
                var v=new double?[5];for(int c=0;c<5;c++){var text=Convert.ToString(points.Rows[row].Cells[c+1].Value).Trim();if(text.Length==0)continue;double n;if(!double.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out n)||!NozzleZCalibrationData.Finite(n))throw new ArgumentException("N"+(row+1)+" 参数格式无效");v[c]=n;}
                var old=a.Nozzles[row].Point;
                if(changed || old.X!=v[0] || old.Y!=v[1] || old.U!=v[2] || old.Approach!=v[3] || old.End!=v[4])a.Nozzles[row].Samples=new double[0];
                a.Nozzles[row].Point=new NozzleZCalibrationDraft.PointDraft{X=v[0],Y=v[1],U=v[2],Approach=v[3],End=v[4]};
            }
            data.ValidateStructure();return data;
        }
        private void SetRunning(bool value){running=value;armSelector.Enabled=!value;points.ReadOnly=value;foreach(var n in new[]{speed,step,spread})n.Enabled=!value;foreach(Control c in actions.Controls)c.Enabled=!value&&!loadFailed;foreach(Control c in pointActions.Controls)c.Enabled=!value&&!loadFailed;cancel.Enabled=value;cancelPosition.Enabled=value;}
        private async void StartMeasure(bool all)
        {
            if(running||loadFailed)return;
            int nozzle=grid.CurrentCell==null?0:grid.CurrentCell.RowIndex;
            try {
                var data=ReadSettings();for(int i=all?0:nozzle;i<=(all?2:nozzle);i++)data.ValidateProbe(selectedArm,i);
                if(!data.Arms[selectedArm].Baseline.HasValue&&!all&&nozzle!=0)throw new InvalidOperationException("首次请先测量N1");
                if(MessageBox.Show(this,"确认六嘴空嘴，基准点及探测范围已核对？将执行定位和低速真空探测。","自动测高",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
                SetRunning(true);status.Text="自动测高中，可点击停止测量。";int arm=selectedArm;
                await Task.Run(()=>NozzleZCalibrationMotion.RunExclusive(()=>{for(int i=all?0:nozzle;i<=(all?2:nozzle);i++)NozzleZCalibrationMotion.Measure(data,arm,i);}));
                pending=data;dirty=true;RefreshResults();status.Text="三次测量合格；请核对候选补偿并保存。";
            }catch(Exception ex){status.Text="测高未完成："+ex.Message;}finally{SetRunning(false);}
        }
        private void Learn(object sender,EventArgs e)
        {
            try{int i=points.CurrentCell==null?0:points.CurrentCell.RowIndex;var p=NozzleZCalibrationMotion.Learn(selectedArm,i);points.Rows[i].Cells[1].Value=Entry(p.X);points.Rows[i].Cells[2].Value=Entry(p.Y);points.Rows[i].Cells[3].Value=Entry(p.U);points.Rows[i].Cells[4].Value=Entry(p.Approach);status.Text="已学习当前嘴接近位置；请核对并填写探测终点。";}
            catch(Exception ex){status.Text="学习失败："+ex.Message;}
        }
        private async void Position(object sender,EventArgs e)
        {
            try{var data=ReadSettings();int i=points.CurrentCell==null?0:points.CurrentCell.RowIndex;data.ValidateProbe(selectedArm,i);
                if(MessageBox.Show(this,"按当前参数抬起六嘴，再定位所选嘴至接近位置？","定位",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
                SetRunning(true);int arm=selectedArm;await Task.Run(()=>NozzleZCalibrationMotion.RunExclusive(()=>NozzleZCalibrationMotion.Position(data,arm,i)));status.Text="已到接近位置。";
            }catch(Exception ex){status.Text="定位未完成："+ex.Message;}finally{SetRunning(false);}
        }
        private void SaveDraftSettings(NozzleZCalibrationData data)
        {
            var settings=NozzleZCalibrationDraft.Load(settingsPath);var a=data.Arms[selectedArm];for(int i=0;i<3;i++)settings.Arms[selectedArm].Points[i]=a.Nozzles[i].Point.Copy();settings.Arms[selectedArm].Speed=a.Speed;settings.Arms[selectedArm].Step=a.Step;settings.Arms[selectedArm].MaxSpread=a.MaxSpread;settings.Arms[selectedArm].SettingsSaved=true;settings.Save(settingsPath);
        }
        private void SaveSettings(object sender,EventArgs e)
        {
            try{var data=ReadSettings();SaveDraftSettings(data);pending=data;dirty=true;RefreshResults();status.Text="设置草稿已保存；参数改变的嘴需要重新测量，正式补偿未改变。";}
            catch(Exception ex){status.Text="设置未保存："+ex.Message;}
        }
        private void SaveResult(object sender,EventArgs e)
        {
            try{if(NozzleZCalibrationMotion.IsBusy)throw new InvalidOperationException("标定动作执行中");var data=ReadSettings();data.ValidateArm(selectedArm);
                string info="臂"+(selectedArm+1)+" 固定B="+Num(data.Arms[selectedArm].Baseline)+" mm\r\n";
                for(int i=0;i<3;i++)info+="N"+(i+1)+" 补偿 "+Num(data.Offset(selectedArm,i))+" mm\r\n";
                if(MessageBox.Show(this,info+"仅保存测高记录，不参与当前取放坐标换算，是否确认？","测高记录保存",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
                NozzleZCalibrationMotion.CheckIdle();SaveDraftSettings(data);NozzleZCalibration.SaveArm(data,selectedArm);active=NozzleZCalibration.GetSnapshot();LoadArm();status.Text="测高记录已保存；当前取放仍直接使用实际 Z 轴坐标。";
            }catch(Exception ex){status.Text="结果未保存："+ex.Message;}
        }
    }
}

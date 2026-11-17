namespace UI
{
    // Form dependencies only. The real machine application is never loaded.
    public class User
    {
        public enum PERMISSION { Engineer }
        public struct ST_USER { public PERMISSION pms; }
    }
    public static class FrMain { public static UserForm frsuser = new UserForm(); }
    public class UserForm { public UserControlStub user1 = new UserControlStub(); }
    public class UserControlStub { public User.ST_USER cur_user; }

    public static class DoorDebugUiHarness
    {
        static int checks;
        static void Expect(bool condition, string message)
        {
            checks++;
            if (!condition) throw new System.Exception(message);
        }
        static object Call(FrSafetyDebugMode form, string method, params object[] args)
        {
            return typeof(FrSafetyDebugMode).GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(form, args);
        }
        static T Control<T>(FrSafetyDebugMode form, string name) where T : System.Windows.Forms.Control
        {
            return (T)form.Controls.Find(name, true)[0];
        }
        static void CheckBounds(System.Windows.Forms.Control parent)
        {
            foreach (System.Windows.Forms.Control child in parent.Controls)
            {
                Expect(parent.ClientRectangle.Contains(child.Bounds), child.Name + " clipped");
                CheckBounds(child);
            }
        }
        public static void Run(string preview)
        {
            SafetyDebugMode.Disable("UI test reset");
            using (var form = new FrSafetyDebugMode())
            {
                form.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-32000, -32000);
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                foreach (var item in SafetyDebugMode.EmgInputs) item.Value.card.Input = MotionCtrl.GPIO.IO_STA.IN_OFF;
                Call(form, "RefreshView");
                var emg = Control<System.Windows.Forms.Label>(form, "lbl_emg0");
                Expect(emg.Text.Contains("IO:OFF") && emg.Text.Contains("正常") && emg.ForeColor == System.Drawing.Color.Green, "OFF sample is shown as normal");
                MT.GPIO_IN_EMG0.card.Input = MotionCtrl.GPIO.IO_STA.IN_ON;
                Call(form, "RefreshView");
                Expect(emg.Text.Contains("IO:ON") && emg.ForeColor == System.Drawing.Color.Red, "ON sample is shown as emergency");
                MT.GPIO_IN_EMG0.card.Input = MotionCtrl.GPIO.IO_STA.ERR;
                Call(form, "RefreshView");
                Expect(emg.Text.Contains("读取异常") && emg.ForeColor != System.Drawing.Color.Green, "read error must not display green normal");
                MT.GPIO_IN_EMG0.card.Input = MotionCtrl.GPIO.IO_STA.IN_OFF;
                foreach (string name in new[] { "chk_emg0", "chk_emg1", "chk_emg2", "chk_emg3", "chk_door_ud", "chk_door_tt", "chk_door_left", "chk_door_right" })
                    Expect(!Control<System.Windows.Forms.CheckBox>(form, name).Checked, name + " must default off even if input is triggered");
                var choice = Control<System.Windows.Forms.CheckBox>(form, "chk_door_ud");
                choice.Checked = true;
                Expect(!SafetyDebugMode.IsActive, "checkbox edit must not apply immediately");
                var selected = (SafetyDebugMode.DoorGroup)Call(form, "SelectedDoors");
                Expect(selected == SafetyDebugMode.DoorGroup.UpDownLoad, "selection mapping");
                string error;
                choice.Checked = false;
                Expect(SafetyDebugMode.Enable(SafetyDebugMode.EmgInput.None, SafetyDebugMode.DoorGroup.None, "test", out error), "plain debug apply succeeds");
                Call(form, "RefreshView");
                Expect(Control<System.Windows.Forms.Label>(form, "lbl_mode").Text.Contains("调试模式开启中"), "plain debug shown active");
                Expect(Control<System.Windows.Forms.Button>(form, "btn_disable").Enabled, "plain debug can exit");
                Expect(!Control<System.Windows.Forms.Label>(form, "lbl_door_ud").Text.Contains("调试已忽略"), "plain debug does not bypass door");
                SafetyDebugMode.Disable("plain debug UI test");
                choice.Checked = true;
                SafetyDebugMode.Enable(SafetyDebugMode.EmgInput.None, selected, "test", out error);
                Call(form, "RefreshView");
                Expect(Control<System.Windows.Forms.Label>(form, "lbl_door_ud").Text.Contains("调试已忽略"), "door status reflects applied selection");
                Expect(Control<System.Windows.Forms.Label>(form, "lbl_mode").Text.Contains("门禁忽略：上下料"), "summary includes doors");
                using (var reopened = new FrSafetyDebugMode())
                {
                    Call(reopened, "FrSafetyDebugMode_Load", null, System.EventArgs.Empty);
                    Expect(Control<System.Windows.Forms.CheckBox>(reopened, "chk_door_ud").Checked, "reopen restores applied selection");
                }
                form.PerformLayout();
                CheckBounds(form);
                using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height));
                    bitmap.Save(preview, System.Drawing.Imaging.ImageFormat.Png);
                }
                Call(form, "btn_disable_Click", null, System.EventArgs.Empty);
                Expect(!SafetyDebugMode.IsActive && !choice.Checked, "exit clears both applied and UI selection");
            }
            System.Console.WriteLine("PASS: " + checks + " actual WinForms UI checks; preview=" + preview);
        }
    }
}

namespace UI
{
    public static class PT_SET
    {
        public static bool bEnUpDownDr, bEnTrayDr, bEnRBoxDr, bEnLBoxDr;
    }

    public static class DoorDebugHarness
    {
        /*ACTUAL_DOOR_GATES*/
        static int checks;
        static void Expect(bool condition, string message)
        {
            checks++;
            if (!condition) throw new System.Exception(message);
        }

        public static void Run()
        {
            string error;
            SafetyDebugMode.Disable("reset test");
            Expect(!SafetyDebugMode.IsActive, "default debug off");
            Expect(SafetyDebugMode.Enable(SafetyDebugMode.EmgInput.None, SafetyDebugMode.DoorGroup.None, "test", out error), "allow debug without bypassing any safety input");
            Expect(SafetyDebugMode.IsActive, "debug enabled independently of bypass masks");
            Expect(SafetyDebugMode.Ignored == SafetyDebugMode.EmgInput.None && SafetyDebugMode.IgnoredDoors == SafetyDebugMode.DoorGroup.None && !SafetyDebugMode.LightBoxMasked, "empty selection must not add a bypass");
            Expect(SafetyDebugMode.ShouldCheckDoor(true, SafetyDebugMode.DoorGroup.TurnTable), "debug without bypass preserves configured door checks");
            SafetyDebugMode.Disable("plain debug test");
            Expect(!SafetyDebugMode.IsActive, "plain debug can be disabled");
            var gates = new System.Func<int, bool>[] { GateUpDownDr, GateTrayDr, GateRBoxDr, GateLBoxDr };
            for (int selected = 0; selected < 4; selected++)
            {
                var group = (SafetyDebugMode.DoorGroup)(1 << selected);
                Expect(SafetyDebugMode.Enable(SafetyDebugMode.EmgInput.None, group, "test", out error), "allow doors only");
                Expect(SafetyDebugMode.IsActive, "door-only debug must block automatic run");
                Expect(SafetyDebugMode.Ignored == SafetyDebugMode.EmgInput.None, "doors must not mask emergency inputs");
                MT.GPIO_IN_EMG3.card.Input = MotionCtrl.GPIO.IO_STA.IN_ON;
                Expect(SafetyDebugMode.IsPressed(MT.GPIO_IN_EMG3, SafetyDebugMode.EmgInput.UpDownLoad), "emergency input still effective");
                for (int enabled = 0; enabled < 2; enabled++)
                {
                    PT_SET.bEnUpDownDr = PT_SET.bEnTrayDr = PT_SET.bEnRBoxDr = PT_SET.bEnLBoxDr = enabled != 0;
                    for (int current = 0; current < 4; current++)
                    {
                        Expect(gates[current](15) == (enabled != 0 && current != selected), "only selected door group ignored");
                        Expect(!gates[current](0), "caller mask preserved");
                    }
                    SafetyDebugMode.Disable("test restore");
                    for (int current = 0; current < 4; current++)
                        Expect(gates[current](15) == (enabled != 0), "restore original configured enable");
                    Expect(PT_SET.bEnUpDownDr == (enabled != 0) && PT_SET.bEnTrayDr == (enabled != 0)
                        && PT_SET.bEnRBoxDr == (enabled != 0) && PT_SET.bEnLBoxDr == (enabled != 0), "configured values unchanged");
                    SafetyDebugMode.Enable(SafetyDebugMode.EmgInput.None, group, "test", out error);
                }
            }
            SafetyDebugMode.Disable("test");
            VAR.gsys_set.status = EM_SYS_STA.RUN;
            Expect(!SafetyDebugMode.Enable(SafetyDebugMode.EmgInput.None, SafetyDebugMode.DoorGroup.UpDownLoad, "test", out error), "reject enable during automatic run");
            Expect(!SafetyDebugMode.IsActive, "failed enable must not change mask");
            VAR.gsys_set.status = EM_SYS_STA.STANDBY;
            Expect(SafetyDebugMode.Enable(SafetyDebugMode.EmgInput.UpDownLoad, SafetyDebugMode.DoorGroup.TurnTable, "test", out error), "combined selection");
            Expect(SafetyDebugMode.Ignored == SafetyDebugMode.EmgInput.UpDownLoad && SafetyDebugMode.IgnoredDoors == SafetyDebugMode.DoorGroup.TurnTable, "independent masks");
            SafetyDebugMode.Disable("test");
            Expect(!SafetyDebugMode.IsActive && SafetyDebugMode.IgnoredDoors == SafetyDebugMode.DoorGroup.None, "exit clears both masks");
            System.Console.WriteLine("PASS: " + checks + " door debug policy and actual MT gate checks (no hardware).");
        }
    }
}

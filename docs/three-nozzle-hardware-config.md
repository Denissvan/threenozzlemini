# 三吸嘴机器接线配置（2026-09-25更新）

依据 `WLTmini IO分配表(1).xlsx` 的“技改新增轴卡”页 A16:F27，并经用户确认，**六嘴真空/破真空/真空检测全部迁移到 ZMC432，IP 192.168.0.30，程序卡编号8**。这替代此前“仅轴迁移、原IO不变”的方案；XY、料盘轴及其他IO不变。

| 吸嘴 | 真空输出 | 破真空输出 | 检测输入 |
| --- | --- | --- | --- |
| 臂1 N1 | OUT0 | OUT1 | IN0 |
| 臂1 N2 | OUT2 | OUT3 | IN1 |
| 臂1 N3 | OUT4 | OUT5 | IN2 |
| 臂2 N1 | OUT6 | OUT7 | IN3 |
| 臂2 N2 | OUT8 | OUT9 | IN4 |
| 臂2 N3 | OUT10 | OUT11 | IN5 |

软件按表中 IN0..IN11 / OUT0..OUT11 声明12输入、12输出；IN6..IN11在表中未分配用途，不创建业务信号。此数量表示本项目使用的地址范围，不断言控制器的总硬件容量。

原四嘴的稳定 GPIO 对象在 `MT.cs` 中直接绑定新卡，原 Cylinder/XT 引用保持。第三嘴仍由 `ThreeNozzleHardwareConfig.Initialize()` 在板卡初始化前验证并绑定；仅接受卡8及表中对应点位，拒绝旧卡、错位、重复、越界，全部通过后才注册六点。

`UI/ThreeNozzleHardware.ini` 已填写两臂第三嘴地址，并随编译以 PreserveNewest 复制到程序目录。现场标定值需同步维护源配置；部署时保留机台已确认的配置，避免用开发模板覆盖。

```ini
[Machine]
CalibrationConfirmed=false
[Arm1]
VacuumCard=8
VacuumPoint=4
VacuumBreakCard=8
VacuumBreakPoint=5
VacuumSenseCard=8
VacuumSensePoint=2
SafeTrayMaxY=NaN
[Arm2]
VacuumCard=8
VacuumPoint=10
VacuumBreakCard=8
VacuumBreakPoint=11
VacuumSenseCard=8
VacuumSensePoint=5
SafeTrayMaxY=NaN
```

IO表已确定接线，不能代替机械标定：`CalibrationConfirmed`仍为false；两臂安全区域Y上界仍为NaN。按实际六嘴方向/原点、偏移、高度和完整机械运动包络确认后填写，修改后重启。代码不套用表中的速度/加速度参数，也不自动启用生产。

初始化读取配置本身不发硬件指令；板卡启动时沿用既有输出默认值初始化与读回校验，GPIO状态读取使用新卡句柄。未连接设备验证，实际电平与接线仍需现场核对。

开启马达扫码时，两臂各需填写 `[Arm1]` / `[Arm2]` 的 `MotorScanY3`、`MotorScanZ3`、`MotorScanAngle3`，模板默认NaN。原N1/N2继续读取原工艺参数，N3独立标定，不能借用N2。下相机附加扫码需要第三嘴 `FindQrCode3` 任务（与 `CONST.DwFindQrCodeFw[2]` 一致），特征任务和二维码任务分别保存结果，避免二维码结果覆盖放料定位结果。

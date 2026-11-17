# 三吸嘴停止与清料实施计划

**目标：** 修复后台尾部卸料被清料结束打断和普通停止残留故障锁。

**方式：** 在现有协调器内增加完成屏障并保留停止任务，现有物理动作和提交顺序不变。

1. 扩展 `tests/three-nozzle-coordinator-regression.ps1`，先复现普通停止锁定、尾部续接和清料缺失等待的失败。
2. 修改 `UI/Class/ThreeNozzleCoordinator.cs`，根据物理状态区分普通停止，重启原任务时跳过已成功臂，提供共用清料完成条件。
3. 修改 `UI/Class/UpDownLoad.ThreeNozzle.cs`，只读验证轴安全、真空/记录一致与尾部计划清空。
4. 修改 `UI/Class/myfunc.cs`，清料提示前等待，收盘前共用检查，后台运行期间禁止重复归位。
5. 使用 `tests/three-nozzle-stop-state-regression.ps1` 验证真实状态方法；运行协调器测试和相关已有回归；VS2022 全量离线编译。

不修改相机帧率、飞拍节拍、正常生产重叠卸料及物料计数，不部署现场。

## 离线结果

- 协调器 23 个行为场景通过，实际状态方法 25 项检查通过。
- 原有动作顺序、取料恢复和启动不强制归位回归通过；取料恢复脚本的硬件替身补齐了现有轴、枚举和 UTF-8 读取。
- VS2022 MSBuild `UI/UI.csproj /t:Build /p:Configuration=Debug` 最终退出码 0，输出只到临时目录。日志：`C:\Users\gdfanyd\AppData\Local\Temp\MINI-StopDrain-e7b711c62a154c80b4ba3d9efd761224\build-final.log`。
- 本次涉及的已跟踪文件 `git diff --check` 通过。工作区其他文件已有的空白告警未改动。
- 未提交 Git、未部署；现场动作和暂停恢复仍需验收。

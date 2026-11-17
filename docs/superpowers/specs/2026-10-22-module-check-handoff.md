# 定时模组点检：新对话交接（2026-10-22）

本文件用于在新对话中继续“定时模组点检”工作。主方案仍以
`2026-09-28-module-check-scheduled-design.md` 与 `2026-09-28-module-check-risk-review.md` 为准，本文只记录 10-22 的评估结论和新确认事项。代码未改动。

## 一、10-22 新确认的现场信息

1. 点检盘将装在**待测料盘（traybox_fd）同一根 X 轴**上，具体位置未定，需要独立的阵列（示教/生成穴位）流程。
2. 测试程序 SetFactoryMode 切换参数（重启+登录）时间先按 **约 5 s** 估算。
3. 292 换位接受复用现有 GRR 夹具内直接换位方式（见第三节），不必退回点检盘再装。
4. 原方案 AFC/DCC/OTP 单面点检：现有代码对“某面没有测试项”的处理见第四节。

## 二、点检盘与待测料盘共用 X 轴的影响（代码依据）

- 取料对位机制：`XT.CaliPos(pos, ws_xpos)` 计算料盘 X 轴目标 `pos.a = -pos.x + xpos + pos.a`，即让料盘轴移动，使料穴落在“臂 X = 目标夹具穴 X（ws_xpos）”的位置，臂 X 尽量不动；`XT.UpCamMod` 用 `MT.ZupMove(ax_x, ax_y, traybox_fd.ax_x, pos.a)` 同时移动臂和料盘轴。三嘴预取 `ThreeNozzlePreparePair → xt.XtCapMovMod(..., pair.Pockets[slot].st_pos[id].x, ...)` 走这条路径。
- 旧流程提前定位 `UpDownLoad.FindNextTrayPosX1 / MoveNextTrayX1Pos` 同理。
- **只有下限保护**：`pos.a < 0` 时把差值转给臂 X（`pos.x = xpos - pos.a; pos.a = 0`），**没有上限保护**。料盘轴行程被点检盘占用/待测盘坐标偏移后，超过新行程上限的目标会直接撞软限位或报错。
- 需要改：CaliPos 与 FindNextTrayPosX1 增加上限钳位（超出部分转给臂 X）；料盘轴行程上下限做成参数；臂 X 补偿后检查双臂合计上限/安全距离（`MT.ChkSafePos`、`DescribeDualArmLimit`）。
- 点检盘需要独立 Tray 定义（穴位阵列、每臂 Pos[id] 含 a=料盘轴位置），对位逻辑与待测盘相同；点检盘位置必须落在 SafeTrayMaxY（三吸嘴下相机拍照位最大Y+3，`ThreeNozzleHardwareConfig.SafeTrayMaxY` 已自动计算）以内，否则无法在转盘转动期间预取。
- 换盘/升降：点检盘不能走 traybox_fd 的空盘换盘逻辑（`ThreeNozzleEnsureTray`），点检盘“空”可能是样品在夹具上。

## 三、现有 GRR 流程已支持夹具内直接换位（可复用于 292）

`UpDownLoad.ThreeNozzle.Modes.cs`：
- 触发：`PT_SET.bGrrFlow && ws.GrrUDLcnt > 0 && !VAR.ClearMt` → `ThreeNozzleMode.GrrReload`。
- 分组：`ThreeNozzleModeOwnsPocket` 按 FindMod，LOW = 1–4、9–12，HIGH = 5–8、13–16；GRR 按穴位升序，正好等于方案的组内循环顺序。
- 动作 `ThreeNozzleRunGrrReload`：N1 取组首件并暂存；N2 依次取下一穴 → `ThreeNozzleGrrPlace` 先 `DwCamAction` 拍下相机 → `ThreeNozzlePlaceOne(countNew:false)` 放入**最早空穴**；最后 N1 的件放入最后空穴。全满时等价于 [A,B,C,D]→[B,C,D,A] 循环移位。N3 空闲。
- 已有：条码随料走（GrrReload 分支 `md.bardcode = xt.XtMd.bardcode`）、不计产量、中断续做游标 `threeNozzleModeCursor/Stage`、清料 `ThreeNozzleDrainGrr`。
- 每搬一件都要去下相机（DwCamAction），节拍按“夹具取 → 下相机 → 上相机拍穴 → 放”估算。
- 复用时需改的点：
  1. “最早空穴”规则在组内有空穴/屏蔽穴时不是严格循环 → 改为按预先生成的映射表放料；
  2. `ThreeNozzleCanLoadPocket` 排除 `isAutoChkMode && bAutoChkOk`，点检全批换位不能被已通过标记过滤；
  3. 触发条件从 GRR 计数改为“点检任务 + 本轮存在 292”；
  4. `ThreeNozzleDrainGrr` / `ThreeNozzleUnloadCore` 会按 res 落 OK/NG 盘，点检样品必须改走点检盘原穴归还；
  5. 轮数上限与 292 汇总（两 PC 结果齐全后才换位）。

## 四、测试面无测试项时的现有行为

`WS.cs` 测试流程：每个面 `WaitTestResult` 拿到测试端请求的 sta（光箱位置ID）→ `MoveLightBoxToTestPosition → lb.MoveTo(sta)`；`LightBox.MoveTo` 中 sta 不在本光箱 `ListPos` 时返回 `EM_RES.END` → 光箱回 0、`TestStatus = NEXT`，转盘转到下一面再次 `WaitTestResult`。sta==0 为最终结果，`NextTest(0)` 后 `COMPLETED`，`pc.bCycleCompleted` 锁存，后续面跳过 WaitTestResult。
结论：测试端某面不发该面光箱的 sta，电控在该面收到下一面的 sta 即返回 END 跳过；前提是测试端在未收到 NextTest 前重复返回同一个 sta（需联调日志确认）。被跳过的面仍要转盘停靠。单面点检主要靠测试端点检参数只包含该面项目。

## 五、与上下料预取提前方案的关系（同期另一个议题）

- 现状：预取在工站到位翻转后才开始（`WS.SetupForFeed(ref bquit)` 默认 beginPrefetch=true，在 TurnToFeed 后、AllCyUp 前调用 `ThreeNozzleCoordinator.BeginPrefetch`），myfunc 2081 行又调一次 BeginPrefetch，会在 WaitPending 处阻塞到预取完成；光幕检查、PutModShowMsg 都在预取之后。
- 计划：上一工站最后一组卸完（BeginUnload）后即为下一工站预取，两臂只在料盘区（Y ≤ SafeTrayMaxY），工站到位后再进夹具。
- 点检衔接：点检批次判定必须在为该工站预取之前完成；点检工站 292 换位轮不预取；预取料源（普通盘/点检盘）由同一入口决定，两个改动需一起设计。

## 六、仍待确认

1. 点检盘尺寸/穴数/在料盘轴上的位置，料盘轴新行程上下限。
2. 测试端单面点检参数与“面→项目”映射（推测 DCC=3000/4000/5000，AFC=10001–10003，OTP=20001/20002，需核对）。
3. GetCheckModeResult 结果归属与 30 s 读取窗口。
4. 旧点检入口（279/276 清料点检、运行页勾选）在新功能启用时是否屏蔽。

## 七、建议分期

1. 定时触发 + 预取前锁定批次 + 点检盘全盘装入 + 单轮测试 + 原穴归还 + 恢复生产模式（无 292）。
2. 基于 GRR 直接换位实现 292 多轮 + 轮数上限。
3. 断电续做、细分失败处置。
UI 新增设置页按 Designer（.Designer.cs/.resx）实现。

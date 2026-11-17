> 2026-09-23 实施记录：主要上下料路径与轴/初始化已修改。实际支持范围、未适配模式及验证结果以 `../../three-nozzle-implementation.md` 为准；机器接线参考 `../../three-nozzle-hardware-config.md`。未采用截图的速度、加速度等参数。

# MINI 三吸嘴独立 Z/R 技改修改方案与分阶段计划

> **For agentic workers:** 使用 superpowers:executing-plans 按任务实施。本文件是设计方案，不是已实施代码；硬件接线与安全区域数据未确认前，不生成可上机运动参数。步骤使用 checkbox 跟踪，未经实际验证不得勾选。

**Goal:** 每臂三个独立 Z/R 吸嘴，保持两片交换、现有料盘布局和测试方式，实现开盖期间预取两片、开盖后同组交替交换，以及满足工艺和互锁条件后的末组并行下料。

**Architecture:** 保留双臂以及工站、料盘业务。新增三吸嘴专用状态机、每轮交换上下文及区域互锁；每臂同一时刻只有一个运动执行者。治具交换完成、治具区释放、允许转盘换位和成品落盘完成分别表达，禁止提前伪造旧完成状态。

**Tech Stack:** C#、WinForms、.NET Framework 4.8、既有 MotionCtrl/AXIS/CARD/Cylinder、INI、Visual Studio 2022 BuildTools MSBuild、PowerShell 回归脚本。

---

## 1. 范围与基线

- 目标目录：`D:\MyProject\自动点检验证普通版本1218\MINI三吸嘴技改`。
- 参考目录：`D:\MyProject\自动点检验证普通版本1218\MINI3.0.0.17`，本次不修改参考目录。
- 检查时分支为 master，HEAD 为 `29dd2a3c24b4b256f7e42193af35cc54682fe159`。工作区 MT.cs、UpDownLoad.cs、XT.cs 已有未提交修改，另有既有删除和未跟踪测试；不能把 HEAD 当成完整设计基线。
- 已对九个关键文件逐一计算 SHA256，与参考目录一致：UI/Class 下的 MT.cs、UpDownLoad.cs、XT.cs、myfunc.cs、WS.cs、Turntable.cs、TrayBox.cs，MotionCtrl/MyType.cs，UI/UI.csproj。
- 已确认：每臂 3 个独立 Z/R，共 6 吸嘴；每组仍交换 2 片；其他机构和测试方式不变。
- 已纠正：原双吸嘴连续运行本来就是去程送新料、在治具区放完后取下一组已测料回盘。收益不能按每两片减少一次长距离往返计算。
- 主要收益：首组的预取料与开盖重叠，并用一个空吸嘴启动满料治具交换；末组带成品离场后，尾部落盘尽可能与合盖/转盘动作重叠。独立 Z/R 速度收益单独实测。
- 本次仅新增本设计文档，不改运行代码，不提交既有改动，不宣称编译或实机验证通过。

## 2. 正常动作与首尾边界

为避免物理编号混淆，下文 N1/N2/N3 始终表示“本臂第 1/2/3 个吸嘴”，不是全机 id。

| 阶段完成后 | N1 | N2 | N3 | 穴位 A | 穴位 B |
|---|---|---|---|---|---|
| 开盖期间预取 | 空 | 新 A | 新 B | 旧 A | 旧 B |
| PickA | 旧 A | 新 A | 新 B | 空 | 旧 B |
| PlaceA | 旧 A | 空 | 新 B | 新 A | 旧 B |
| PickB | 旧 A | 旧 B | 新 B | 新 A | 空 |
| PlaceB | 旧 A | 旧 B | 空 | 新 A | 新 B |
| 成品分流落盘 | 空 | 空 | 空 | 新 A | 新 B |

满料治具必须先取 A 再放 A，不能先把新料放到仍有成品的穴位。每颗取放完成并验证后才提交物料转移。

连续多组：第 1 组交换后卸成品、补两片，再进第 2 组；中间组不会放行整副工站合盖或转盘。只有该工站本轮所有需要处理的组都完成、必要检查完成、两臂均离场，才进入末尾并行阶段。

空治具首轮：只为启用空穴备料并放入，跳过取旧料。清料：禁止新备料，只取走已测料；若已经持有待测料，先保留身份并按明确的退料/人工处置流程处理，不能当成成品落 OK 盘。单穴禁用或只剩一片：生成一片计划，仅执行有效穴位的动作，不填充虚拟第二片。启用穴位缺料且不允许缺片生产时保持等待，不发布交换完成。

## 3. 当前入口与改动位置

以下路径均相对于目标目录；行号为本次检查时位置，实施后以函数名定位。

| 文件与位置 | 已核实行为 | 必须改动 |
|---|---|---|
| UI/Class/myfunc.cs:1902–1942，COM.test_th | SetupForFeed 完成后才发 REDAYFORUPDOWNLOAD | 在实际需上下料且模式允许时创建一次备料请求；原就绪信号仍在开盖成功后发 |
| UI/Class/WS.cs:1918，SetupForFeed | 翻转至上下料位、开盖；使用旧机械臂位置限制 | 接入开盖区域占用与三吸嘴路径检查，保持气缸、位置及安全传感器检查 |
| UI/Class/UpDownLoad.cs:7702、7889 | 等工站就绪才启动双臂，随后等待任务全部完成 | 三吸嘴分支提前启动备料；改为按阶段等待，尾部任务仍被监控 |
| UI/Class/UpDownLoad.cs:7557，UdLoad | 运行时读取 GetWSOnFeedPos | 新分支使用创建任务时固定的工站/轮次，不在转盘移动后重新取工站 |
| UI/Class/UpDownLoad.cs:6979、7014 | 单一主流程；持有已测料就跳料盘放料 | 保留旧算法；新状态机按单颗物料角色决定动作，允许交换过程混合持料 |
| UI/Class/UpDownLoad.cs:640、4705 | 按两片分组，按 md.Num/9 选择旧吸嘴 | 保留穴位分组规则，移除新流程中穴位编号到吸嘴的隐式映射 |
| UI/Class/UpDownLoad.cs:6420、6451 | 取待测料依赖治具当前 EMPTY 目标 | 新备料依据本轮交换计划，不能提前清空仍有成品的治具数据 |
| UI/Class/UpDownLoad.cs:4441、5486、5832、3144 | 治具批取、批放、料盘下料包含物料与视觉记账 | 提取指定吸嘴/指定穴位的一颗动作；不直接用旧批量函数执行半交换 |
| UI/Class/myfunc.cs:2023、2191、2207 | 等旧状态，再 SetupForTest/MoveToNext | 新模式消费明确的工站换位许可；保持其他工站/测试/转盘互锁 |
| UI/Class/UpDownLoad.cs:4240、7940–8006 | 合盖回检、独立扫码、夹具检查、Hall 分支 | 保留工艺。需要机械臂相机时归还本臂执行权后检查，不同线程抢轴 |
| UI/Class/WS.cs:1978，SetupForTest | 旧待机区域检查、合盖、翻转 | 用区域许可证明料盘区活动兼容夹具运动，不能直接删除位置判断 |
| UI/Class/Turntable.cs:261、343 | ChkSafe / MoveToNext | 新模式增加换位许可与治具区排他检查，保留 OTP、传感器及全部工站就位条件 |
| UI/Class/MT.cs:54、119、962、1549、1743、1895、1995 | 轴、轴表、安全检查、抬Z、预约、轴归属和恢复门禁 | 六 Z/R 绑定、完整轴集合、明确归属、所有相关 Z 抬升和区域互锁 |
| UI/Class/XT.cs:150、883、959、1011、1220、1301、1509、1621、1695 | 轴绑定及依赖 id%2 的 Z 方向/测高 | 独立参数决定接近方向和安全高度，重查取放、偏移、补吸、测高 |
| UI/FormView/FrMain.cs:551 | 构造两臂，只接受一个 Z、两个 U | 根据机械布局初始化集合，验证数量、归属和重复轴 |
| UI/Class/XT.cs:166、317；MotionCtrl/MyType.cs:1551、1868 | xtcfg.ini、ptcfg.ini 加载保存 | 机器拓扑与产品标定分开；六吸嘴迁移和缺失值检查 |
| UI/FormView/FrSys.cs、FrProduct.cs；UI/Compment/XtOfsAdj.cs、NpointCail.cs | 四吸嘴显示、选择、测高、偏移、标定 | 六吸嘴显示及臂内序号映射；人工动作同样遵守执行权和区域互锁 |
| UI/UI.csproj | 旧式显式 Compile 项目 | 注册所有新 C# 文件，不能只将文件放进文件夹 |

## 4. 文件组织与职责

新增四个生产文件，避免把整套新流程继续堆进 8500 行的 UpDownLoad.cs：

1. `UI/Class/ThreeNozzleCycle.cs`：每轮工站上下文、两片计划、阶段和结果快照；协调信号，无直接轴命令。
2. `UI/Class/UpDownLoad.ThreeNozzle.cs`：将原 UpDownLoad 声明改为 partial；新增单臂预取、交换、退让、尾部落盘和恢复逻辑，沿用现有本臂同步任务执行。
3. `UI/Class/MT.ThreeNozzle.cs`：将原静态 MT 声明改为 partial；新增硬件集合查询、区域占用/换位许可、六 Z/R 安全检查。轴命令仍走既有 MT/AXIS 报错路径。
4. `UI/Class/ThreeNozzleHardwareConfig.cs`：机器级六吸嘴映射与配置检查，不承载产品物料状态。

保留并适配既有底层 Pick/Place、真空、视觉算法、料盘仓储和双臂预约。新流程不直接复用依赖“整臂同一种物料”的批量调度函数。单颗动作从原方法中提取，旧流程也调用同一动作基础，避免两套记账实现漂移。

## 5. 硬件、吸嘴编号与配置

### 5.1 映射方案

优先保留既有内部 id 和配置节，新增两个 id，显示号单独定义，避免将原第二臂参数无声挪给第一臂第三嘴。

| 本臂位置 | 内部 XT.id | 原对象/拟新增对象 | 操作员显示号 | 配方节 |
|---|---|---|---|---|
| 臂1 N1 | 0 | xt1 | 1 | XT0 |
| 臂1 N2 | 1 | xt2 | 2 | XT1 |
| 臂1 N3 | 4 | 新增 xt5 | 3 | XT4 |
| 臂2 N1 | 2 | xt3 | 4 | XT2 |
| 臂2 N2 | 3 | xt4 | 5 | XT3 |
| 臂2 N3 | 5 | 新增 xt6 | 6 | XT5 |

`ListXT1 = {xt1,xt2,xt5}`，`ListXT2 = {xt3,xt4,xt6}`；全局按 id 检索和按显示顺序展示分开。新增 `XT.SlotIndex`（0..2）、`XT.DisplayNumber`，臂归属以 `XT.Parent.id` 为准。禁止用 id%2、id/2、全局列表下标推断臂内位置。

保留 id 不代表旧机械标定可直接使用：ZR 改装后每个吸嘴的 Z 高度、R 零点、旋转中心及相机偏移都必须重新验证。标定版本不匹配时不能自动运行。

### 5.2 逻辑轴

每臂 `X/Y + Z1/R1 + Z2/R2 + Z3/R3`。代码层可继续使用 ax_u 表示 R，界面显示 Z/R。原双吸嘴硬件的共享 Z 模式单独保留；新硬件不能因流程开关关闭而跑旧共享 Z 算法。

每嘴配置：Z/R 对应 CARD 和轴号、真空输出、破真空输出、真空检测输入、Z 安全位、接近方向、软限位、回零顺序和物料包络参数。轴 m_id 已包含卡号，匹配时使用完整 m_id，不仅比较卡内轴号。

机器拓扑拟存 `Path.GetFullPath("..") + "\\syscfg\\three-nozzle-hardware.ini"`，与现有 `axiscfg.ini` 同目录。产品相关标定沿用 `product/<产品>/xtcfg.ini`；产品功能策略沿用 ptcfg.ini。机器拓扑不随产品切换改变。

启动检查：布局版本明确、每臂恰好 3 嘴、每嘴独立 Z/R 无重复、卡轴范围有效、必要 IO 有映射、方向有效、标定与硬件版本匹配。缺少实参返回 PARA_ERR 并给出字段名，不以 0 或默认旧轴静默代替。LegacyTwo 与 IndependentThree 的选择为机器配置；预取/尾部并行开关只能改变时序，不能改变真实机械布局。

### 5.3 已确认新增板卡与轴号（用户补充）

用户已确认新增一张板卡负责上下料轴部分。提供截图标明型号 **ZMC432**，IP **192.168.0.30**，以下十二个 Z/U 轴归属于该卡。U 即本方案中的旋转 R。用户随后明确“只有图中提到的需要迁移”：两臂 X/Y 保留原雷赛卡，已有真空、破真空和检测 IO 保持原卡接线，不迁入新卡。新增两只吸嘴的 IO 点位仍需补齐；不能据轴号空缺猜测 0～5 的用途。

| 本臂吸嘴 | Z轴号 | U/R轴号 | 对应内部XT.id |
|---|---|---|---|
| 臂1 N1 | 6 | 7 | 0 |
| 臂1 N2 | 8 | 9 | 1 |
| 臂1 N3 | 10 | 11 | 4 |
| 臂2 N1 | 12 | 13 | 2 |
| 臂2 N2 | 14 | 15 | 3 |
| 臂2 N3 | 16 | 17 | 5 |

截图给出的参数原值如下，尚未转换为软件或驱动器实参：

| 参数 | 六个Z轴 | 六个U轴 |
|---|---|---|
| 回零模式（轴卡回零） | 二次回零 | Z向回零 |
| 回零方向 | 负向 | 正向 |
| 工作速度 | 300 | 300 |
| 工作加速度 | 3000 | 3000 |
| 回零速度 | 20 | 20 |
| 回零加速度 | 200 | 200 |
| 细分 | 10000 | 36000 |
| 脉冲当量 | 10000 | 100 |

接入方案细化：

- MT.cs 新增 ZMOTION 品牌、以太网 IP 192.168.0.30 的 CARD 实例，建议命名 CARD_ZMC432_8。当前显式卡 id 使用 0～7，8 只是拟采用的软件唯一编号，不是截图轴号或控制器地址；实施前仍核对所有卡注册入口。
- 按实际运行模式加入 CardList / CardList1，使初始化、监控、停止和关闭都覆盖新卡。保留旧卡上的料盘轴或 IO，不能因为 Z/U 迁移就删除整张旧卡。
- 轴6～17绑定到上述六嘴并进入新卡 AxList、各臂轴集合。不得把“新增12个轴”直接当作 ax_num=12；卡轴容量须按实际控制器配置填写，并覆盖轴号17。输入/输出数量不由该截图推导。
- MotionCtrl/CARD.cs 已有 BRAND.ZMOTION 及 ZAux_OpenEth 连接路径，MotionCtrl.csproj 已启用 ZMOTION 编译符号，可作为接入基础；这不等于已经验证 ZMC432 对应驱动、轴类型及回零行为。
- 回零模式须核对 AXIS.Home 的正运动映射及控制器/驱动设置。“Z向回零”保留截图原文，不直接翻译成现有 ushort home_mode。回零方向也不能替代取放料Z接近方向的标定。
- AXIS.cs 使用 pul_per_mm 参与位置和速度换算，现有 SetSpeed 路径还有 Tacc/Tdec；截图的加速度数值不能直接填入加速时间字段。分别核对 Z 的长度单位、U 的角度单位、细分所在层级及控制器 UNITS，防止重复换算。
- MT.Move 当前按各 AXIS 的 card 逐轴下发 MoveTo 并等待完成，不能视作跨卡硬件同步。本次已确定 X/Y 留在旧雷赛卡，Z/U 在新正运动卡，必须验证跨卡抬Z→横移→下降、故障停止和飞拍触发；现有 X/Y 轴号和绑定不迁移。
- 新增卡未连接、初始化失败或运行中断连时，应阻止新机械臂动作，停止相关运动并撤销新动作许可，保持持料信息；不能自动回退到旧 Z/U 轴。

源图：用户提供的 `codex-clipboard-161c98bc-8864-41d2-8ea5-63efce1ac613.png`。本节仅更新设计，不写入运行配置，不连接或操作控制卡。

## 6. 每轮上下文与阶段信号

所有以下名称均为拟新增接口，不是已存在功能。

`ThreeNozzleCycle` 固定保存：单调递增 CycleId、SourceWs 引用、工站编号、启动时位置代次、配方/标定版本、当前模式、两臂分工快照、PairPlan 集合、取消/失败原因。工站旋转后，尾部成品依据吸嘴持料快照落盘，不再读取 SourceWs 当前模组结果。

`ThreeNozzlePairPlan` 保存本组 A/B 的实际 WS.MdDat 引用、有效穴位、各自旧料快照、新料来源料盘与槽位、明确吸嘴槽号，以及每个动作成功确认。穴位与吸嘴配对不能靠相机任务返回顺序。

拟定 `ThreeNozzleStep`：

```text
Idle → Prefetch → WaitFixtureReady
     → PickA → PlaceA → PickB → PlaceB
     → LeaveFixture
     → UnloadFinished → PrefetchNext → WaitFixtureReady （尚有下一组）
     → AwaitPostChecks → TailUnload → Completed         （最后一组）
任意阶段 → Paused / RecoveryRequired / Faulted / Cancelled
```

| 信号 | 含义及限制 |
|---|---|
| PrefetchRequested | 本轮确实需要新料且允许备料，不能单凭开盖请求无条件取料 |
| PrefetchReady | 所需新料均在指定吸嘴、身份/扫码/必要视觉有效；不代表允许进入治具 |
| FixtureReady | 原开盖、位置、安全和工艺前置条件均满足，仍使用真实状态 |
| ArmExchangeDone | 该臂本轮所有有效组已处理，并非仅最后一次函数返回 |
| ArmOutsideFixture | 对应臂退出已验证区域，保持禁止重新进入的许可状态 |
| StationReadyForIndex | 两臂离场、必要检查与合盖/翻转就绪，转盘其余互锁也满足后才可换位 |
| ArmTailDone | 本臂成品全部落盘且记账结束 |
| CycleCompleted | 两臂任务收尾，前述工站交接流程也结束；可回收上下文 |

快照与发布使用同一短临界区；每次发布携带 CycleId 和 armId，拒绝旧轮次信号。等待必须检查取消、失败和超时。不得持状态锁执行运动、相机等待、消息框或等待对臂。

## 7. 并行接入与执行权

### 7.1 开盖前发布备料请求

在 COM.test_th 的 `!bnotfeed && (readyByCompleted || readyByEmpty || readyByFeedStatus)` 真正进入新一轮时，校验工作模式、料盘可供量、工站计划与前一轮状态，调用拟新增 `RequestThreeNozzleCycle(WS sourceWs)`。

请求只创建一次。它先申请“夹具准备期间禁止机械臂进入”的区域占用，再由上下料协调线程启动每臂同一个任务进行 Prefetch。COM.test_th 执行 SetupForFeed；成功才发布 FixtureReady，失败则取消该轮，已取新料保留记录并进入恢复。

UpDownload 新分支必须在旧 `FeedStatus != REDAYFORUPDOWNLOAD` 的等待之前处理备料请求。不能提前把 FeedStatus 写成就绪。SetupForFeed 的旧位置判断要与新区域占用一起适配，避免备料刚开始就被旧原点条件判错。

预取可在料盘区完成取料及可用的视觉/扫码；若某个相机位置位于开盖禁入区，只预取并等待，在 FixtureReady 后补完检查再进入交换。

两臂共享现有料盘，仍使用现有流程锁、料盘运动状态及安全条件。预取不代表两臂能同时占用同一取料区；实际预取完成时间取两臂协调后的时间，不能用单臂理想值。

### 7.2 单臂只保留一个执行者

新增 `RunUdTask(ThreeNozzleCycle cycle)` 重载与 `RunThreeNozzleCycle(ref bool quit, ThreeNozzleCycle cycle)`，保留旧入口用于旧硬件。新任务从预取持续执行到 TailDone，不能在原任务未退出时重复创建任务。

开盖线程只发命令和状态，不直接操纵机械臂。手动、回检、报警恢复须先使本臂任务到达可交接点，获得同一执行权后动作；不能另开线程对同一组轴运动。保留现有 ThreadStatic 定位让位上下文的同步线程语义，首版不改成跨线程 async/await。

### 7.3 末组离场与转盘交接

UpDownload 三吸嘴分支不在合盖前等待 ArmTailDone，而是等待本轮所有参与臂 ArmExchangeDone 和 ArmOutsideFixture。随后按当前产品工艺执行合盖、扫码、回检、Hall 和点检；其余分支不得漏掉。

若 bJigDownPhoto 或其他检查需要机械臂相机，指定臂暂不 TailUnload，先由该臂执行者完成必要检查，再退出并重新发布区域释放。保守首版可让两臂都在检查完成后才开始尾部下料；关闭此检查的产品可充分重叠合盖。必须在日志里显示实际串行原因，不能悄悄省略检查。

只有全部必要检查和夹具测试姿态确认成功，才保留原语义发布 REDAYFORTEST，并发布本轮 StationReadyForIndex。COM.test_th 在新硬件分支用这个信号替代对 UpDownLoad.status 的模糊推断。Turntable.ChkSafe 在发输出前再次验证区域占用、轮次和许可，原 OTP/门禁/光幕/工站就位检查继续执行。

转盘移动期间两臂只能在已批准的料盘区动作。计划路径不能进入夹具区。尾部故障必须被协调线程持续监控，禁止下一轮备料和再次换位；转盘已启动时按现有整机故障停止策略处理，不能假定布尔许可撤销能使转盘物理瞬停。

下一轮请求可排队，但必须等待前轮相关机械臂退出 TailUnload、物料状态对账完成后才能占用执行者。不得把整机“空闲”状态提前发布来骗过旧的启动/清料判断。

## 8. 拟新增动作接口及数据契约

在 UpDownLoad.ThreeNozzle.cs 定义这些边界；参数显式指定本臂槽位，外部不得随意传全局 id：

```text
RunThreeNozzleCycle(ref bool quit, ThreeNozzleCycle cycle)
PrepareIncomingPair(ref bool quit, ThreeNozzleCycle cycle, ThreeNozzlePairPlan pair)
ExchangeFixturePair(ref bool quit, ThreeNozzleCycle cycle, ThreeNozzlePairPlan pair)
PickFinishedOne(ref bool quit, ThreeNozzleCycle cycle, ThreeNozzlePairPlan pair, int pairSlot)
PlaceIncomingOne(ref bool quit, ThreeNozzleCycle cycle, ThreeNozzlePairPlan pair, int pairSlot)
LeaveFixtureZone(ref bool quit, ThreeNozzleCycle cycle)
UnloadFinishedPair(ref bool quit, ThreeNozzleCycle cycle, ThreeNozzlePairPlan pair)
RecoverThreeNozzleCycle(ref bool quit, ThreeNozzleCycle cycle)
```

所有动作返回现有 EM_RES。pairSlot 为 0=A、1=B；PickFinishedOne 使用 N1/N2，PlaceIncomingOne 使用 N2/N3。方法执行前检查计划代次、源/目标物料、真空状态、当前步骤和区域权限。错误不自动推进步骤。

提取现有料盘取料中的“指定槽位→指定吸嘴”动作，输入计划中的待测需求，不能再依赖当前治具 EMPTY 数量。不要在旧 TrayPickMod 上临时修改 md.res 伪造空穴。

取成品成功：复制完整物料身份（条码、马达码、来源工站/穴位、PC、测试结果和其他业务字段）到吸嘴；随后将对应穴位清空。放新料成功：把新料身份转入指定穴位，设置 UNTEST，吸嘴清空，计数只加一次。成品落盘：成功并检查后再清空吸嘴并提交盘位；复位/转盘换位不能更改吸嘴上的原结果。

原 Product.MdDat.Clone 是否完整保留所需字段必须逐项核对，不能只拷贝 res 和 barcode。保留既有数字结果协议，不把业务扩展结果简单归并为 OK/NG；明确不同错误码的原有分流规则。

正常混合持料仅在有合法 PairPlan 的阶段允许。对无计划、真空有料但 XtMd 为空、计划料号不一致的状态，进入 RecoveryRequired，不放行进入治具。原 HasPendingTrayPlacement 不得在新状态机中将任何成品持料都强制转 PLACETRAY，但在末尾落盘和恢复分支继续用于分流检查。

## 9. 独立 Z/R 与区域互锁

新增硬件方法契约：`GetArmAxes(armId)` 返回 X/Y 与本臂三 Z/R；`RaiseAllNozzles(ref quit, armId)` 按机器布局处理共享Z去重或独立Z集合；`CheckArmHorizontalPath(armId, target)` 检查全臂相关嘴的抬升和完整路径区域；`AcquireFixtureAccess(cycleId, armId)`/`ReleaseFixtureAccess(...)` 协调进出；`CanIndex(cycleId)` 只读核对当前许可。

规则：

- X/Y 横移前，确认所有可能干涉的吸嘴在已标定安全位，不只检查当前操作嘴；在料盘区原地 Z 下降与夹具运动可并行的前提，是该区域及持料包络已经验证。
- 夹具准备/翻转/公转持有区域排他权期间，不允许机械臂再次进入；仅检查启动瞬间位置不够。
- 运动路径若离开料盘区、经过夹具区或经特殊避让到达禁区，拒绝；不能只检查最终目标点。
- 原双臂 X 预约、优先级、同线程主动让位与恢复门禁保留。让位路径也受新区域限制，不能为解除双臂等待而闯入正在动作的夹具区。
- 下降接触、真空转换、物料转移阶段不开放横向让位。抬升失败不发布区域释放；故障/取消使计划运动权限失效，但区域占用只能在真实位置核对后释放，不能在 finally 里盲目声明安全。
- 所有停止入口（Stop、UD1Stop、UD2Stop、LCStop、StopAllUpDownLoadMotion、整机 Stop/Home）覆盖新增轴，保持原有真空持料策略。停止不是自动清空所有物料。

## 10. 视觉与标定

- 下视觉任务绑定 CycleId、臂、吸嘴、物料身份；上视觉绑定本轮穴位与计划。禁止继续使用 `(index+1)%2`、`TriPos.n` 未转换全局/局部 id 或仅依赖任务列表顺序的对应方式。
- A 被取空后，检查现有夹具定位是否需要空穴图；需要则只在这个空穴存在的时间拍照。若使用可见的固定治具基准复用变换，先验证取放不会使基准或工装移动，不能默认有效。
- N2 放完新料后，清除该新料视觉/扫码任务绑定，再接收成品 B；保留成品原结果，不能被新料扫码回调覆盖。
- 所有回调检查轮次和物料身份。重拍生成新的动作代次，旧结果迟到只能记录丢弃。
- 六嘴分别验证九点标定关联、旋转中心、拍照偏移、Z取放高度、R基准与补偿；按明确用途核查 `[2]` 数组，双臂数组保持 2，不能一律扩为 3。

## 11. 异常、暂停和恢复契约

| 中断位置 | 必须保留 | 恢复动作 |
|---|---|---|
| 预取一片后第二片失败 | 第一片来源、条码、所在嘴 | 原位重试第二片或受控取消，不能重取第一片 |
| 开盖失败但已备两片 | N2/N3 新料及本轮关联 | 禁入治具；待工站恢复或明确退料，不能将新料当成品放盘 |
| PickA 失败 | A仍有旧料，两个新料仍持有 | 重试/人工确认，禁止 PlaceA |
| PlaceA 成功、PickB 失败 | A新料、N1旧A、N3新B | 从 PickB 恢复，不重做 A |
| 放料物理结果不确定 | 源/目标候选位置、最后动作意图 | 进入人工/视觉核对，不能盲目重复放料或回滚为空 |
| 最后交换完成、落盘失败 | 治具的新料、吸嘴成品完整快照 | 只重试对应盘位，防重复计数；若转盘已移动仍用原快照 |
| 暂停/停止/门禁 | 已提交的每颗转移与尚未完成动作 | 阻止新运动，保持持料；恢复前核对实际位置、真空和物料 |
| 进程重启/断电 | 最后可恢复快照与未确认动作 | 禁止自动续跑，进行六嘴/治具/料盘对账后才建立新轮次 |

运行中按成功步骤保存快照与动作记录；快照不能单独证明断电前动作是否真实完成。保留现有“重试/停止/人工确认”的交互，禁止将人工确认改为超时自动继续。资源释放、超时、取消时都必须记录 CycleId、臂、步骤和残留物料。

首版三吸嘴普通生产与清料必须完成；GRR、自动点检、仅单臂、禁用单嘴、特殊扫码等已有模式逐项标出可运行条件。未适配模式在启动前明确阻止并说明原因，不能自动回退到旧共享Z流程。整体验收前完成现场实际启用的全部模式，不以普通生产通过替代整机兼容完成。

## 12. 分阶段实施任务与验收

### Task 1：固定基线与配置接口

**Files:** MT.cs、myfunc.cs、FrMain.cs、XT.cs；新增 ThreeNozzleHardwareConfig.cs；UI.csproj。

- [ ] 记录当前工作区差异、九文件摘要及已有测试状态，在目标仓库建立 `codex/three-nozzle-retrofit` 开发分支；既有修改单独记录，不整仓暂存。
- [ ] 定义 LegacyTwo/IndependentThree 拓扑及表中 id/显示号映射，补充机器布局和配方版本校验。
- [ ] 编写 `tests/three-nozzle-config-regression.ps1`，加载真实配置解析/验证代码，验证重复轴、缺IO、缺标定、臂内序号错误被拒绝，以及旧XT0..XT3不串位。
- [ ] 配置导出、重新读入后每嘴使用不同数值验证一一对应；先在离线测试里记录失败，再补实现并通过。
- [ ] 仅显式暂存本任务变更，形成可回退提交；不能覆盖目标目录原有避让修改。

### Task 2：独立 Z/R 与运动保护

**Files:** MT.cs、MT.ThreeNozzle.cs、XT.cs、UpDownLoad.cs、WS.cs、Turntable.cs、myfunc.cs。

- [ ] 用假轴记录命令序列，测试任一嘴 Z 未到安全位时 X/Y 不被下发，以及每个停止入口覆盖三 Z/R。
- [ ] 将 Z 方向、补吸、偏移、测高从编号推导改为显式参数；重查 PickOrPlace/Place/Pick 的 finally 与错误优先级，抬升失败不能被覆盖成成功。
- [ ] 实现短锁区域许可及路径检查，模拟转盘持有区域时禁止入区，但允许已批准料盘区运动。
- [ ] 回归双臂预约与同线程让位，验证取消、超时、抬Z失败后无虚假安全释放。
- [ ] 低速逐轴验证每嘴点动、回零、软限位、真空及停止，然后独立提交。

### Task 3：单臂两片交换与数据转移

**Files:** ThreeNozzleCycle.cs、UpDownLoad.ThreeNozzle.cs、UpDownLoad.cs、XT.cs、Product.cs（仅Clone缺字段时修改）。

- [ ] 编写 `tests/three-nozzle-exchange-regression.ps1`，对生产状态机注入假取放执行器，而不是在测试中另写一套状态机。
- [ ] 正常用例断言次序精确为 PickA/PlaceA/PickB/PlaceB，及每步 N1/N2/N3、A/B 的身份与结果；成品总数、新料总数守恒，计数只增一次。
- [ ] 提取指定料盘位和指定吸嘴的取料、指定穴位的取旧/放新及落盘基础动作。
- [ ] 在四个交换动作之前/物理完成后/记账前后分别注入故障，验证恢复不重复取放；完成单片、空治具、清料、混合OK/NG分流用例。
- [ ] 接入上/下视觉结果的轮次与身份校验，测试旧回调迟到和N2角色切换。
- [ ] 单臂低速循环并核对每颗条码与结果，再提交。

### Task 4：开盖期间预取

**Files:** myfunc.cs:1902、UpDownLoad.cs:7679、WS.cs:1918、新增三个流程/安全文件。

- [ ] 编写 `tests/three-nozzle-prefetch-regression.ps1`，用阻塞的开盖假动作证明预取可以完成，但开盖未就绪时绝无治具取放命令。
- [ ] 接入 RequestThreeNozzleCycle 与本臂单任务入口，去重同轮请求；上一轮未退出不能启动第二执行者。
- [ ] 验证开盖失败、料盘未就绪、第二臂等待、清料和取消都不会提前写 FixtureReady/FeedStatus。
- [ ] 验证共享料盘预取互斥及等待期间可响应退出，不持流程锁等待工站开盖。
- [ ] 现场测开盖起止、两臂备料起止和第一颗交换起点，证明实际重叠后提交。

### Task 5：尾部并行与完整工艺

**Files:** UpDownLoad.cs:7889/7940/8004/8151、myfunc.cs:2023/2191、WS.cs:1978、Turntable.cs:261、新流程文件。

- [ ] 编写 `tests/three-nozzle-handoff-regression.ps1`，人为阻塞尾部落盘；在工艺检查结束且两臂释放后允许换位，未释放任一臂则禁止。
- [ ] 测试中让 GetWSOnFeedPos 切换到下一工站，验证旧成品条码/结果仍落入原计划对应的盘位，不受新工站重置影响。
- [ ] 改阶段等待，保持 REDAYFORTEST 的工艺含义；逐个审查旧 UpDownLoad.status/bfinal/brun_ud 消费者，不能以全局置READY代替适配。
- [ ] 覆盖合盖回检、Hall、夹具扫码和实际启用点检分支；需要相机者先检查再尾部下料，确保同臂无并发命令。
- [ ] 尾部落盘失败时测试故障传播、后续换位禁止、下一轮请求不能抢臂；恢复后不重复记账。
- [ ] 实机先验证合盖重叠，再验证转盘重叠和双臂料盘争用，分别记录后提交。

### Task 6：界面、模式和验收

**Files:** FrSys.cs/Designer.cs、FrProduct.cs、XtOfsAdj.cs/Designer.cs、NpointCail.cs、XT.cs、MyType.cs、UI.csproj；按实际修改范围更新相关资源文件。

- [ ] 六嘴显示和示教支持臂/槽选择，显示旧id与新显示号的对应提示；安全参数仅维护权限可改。
- [ ] 界面显示三嘴当前持料种类、条码、步骤、预取就绪、区域释放和尾部下料状态，暂停时保留，不以线程结束清空。
- [ ] 逐产品验证参数保存重载与版本迁移，禁止新增字段默认为零后直接运动。
- [ ] 覆盖实际启用的生产模式；完成完整连续生产、停止继续、清料、换盘、断点恢复与故障试验。
- [ ] 运行新语义测试和受影响现有测试，编译通过后再形成版本提交与现场版本记录。

## 13. 验证命令与证据边界

以下命令是实施阶段使用，本次方案编写没有执行编译或测试。

```powershell
$retrofitRoot = 'D:\MyProject\自动点检验证普通版本1218\MINI三吸嘴技改'
$buildExe = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
& $buildExe (Join-Path $retrofitRoot 'UI\UI.csproj') /t:Build /p:Configuration=Debug /p:Platform=x64 /m /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
```

新增测试依次为 config、exchange、prefetch、handoff；同一假硬件支撑生产逻辑，断言动作和数据，不用正则匹配源代码作为唯一证据。每个脚本失败必须退出非零；例：

```powershell
& pwsh -NoProfile -File (Join-Path $retrofitRoot 'tests\three-nozzle-exchange-regression.ps1') -RepoRoot $retrofitRoot
if ($LASTEXITCODE -ne 0) { throw 'Exchange regression failed' }
```

受影响现有测试至少包含：special-dual-arm-mutual-wait-regression.ps1、special-dual-arm-yield-workflow-lock-regression.ps1、special-dual-arm-yield-pick-lock-regression.ps1、special-dual-arm-yield-place-lock-regression.ps1、dual-arm-collision-envelope-regression.ps1、updown-safe-return-regression.ps1、tray-placement-resume-regression.ps1、station-pick-retry-keeps-position-regression.ps1、upper-backcheck-rephoto-regression.ps1、updown-hall-immediate-flip-regression.ps1。先在未改实现时记录基线；源形状契约因合理重构失效时，先补等价行为验证再调整契约，不直接删除测试。

模拟与编译只证明软件约束，不证明物理安全和节拍。独立ZR的完整动作时间、区域包络、取料可靠性及合盖/转盘并行须实机验证。

## 14. CT 记录与验收口径

每条日志含 CycleId、工站、臂、组号、步骤、开始/结束、结果及等待原因。至少记录：

```text
OpenPrepareStart / OpenReady
PrefetchStart / PrefetchReady（分臂）
PickA / PlaceA / PickB / PlaceB（分组）
ArmOutsideFixture / RequiredCheckDone
CloseStart / CloseEnd / IndexStart / IndexEnd
TailUnloadStart / TailUnloadDone
WaitFeedTray / WaitOutputTray / WaitOtherArm / WaitVision
```

保持原 Ct_ud 的旧语义或明确更名，新增治具占用时间、预取时间、尾部时间与完整周期时间，避免改了计时终点就宣称提速。最终以相邻同一转盘周期边界、实际合格产出数及相同产品/扫码/测试/换盘条件比较；首轮、稳态和清料分开。

判断收益时保留“备料与开盖实际重叠时长”和“尾部下料与后续工序实际重叠时长”，不要直接把两者之和当成整机收益，必须核对是否位于整机关键等待路径，也不能对每个中间组重复扣除一次启动等待。

## 15. 上机前需要补齐的工程输入

这些是明确的实施输入，并不影响先完成状态机和假硬件测试：

1. 已确认仅图中的十二个 Z/U 轴使用 ZMC432、IP 192.168.0.30、轴6～17（见5.3）；X/Y 与已有真空 IO 保持原卡。还需新增两只吸嘴的真空/破真空/检测 IO 点位、新卡实际配置容量与所需输入输出范围，以及截图参数的单位/回零方式映射。旧Z/U停止注册为生产运动轴，仍在用的原卡其他轴与IO保留。
2. 每臂三个吸嘴间距、Z/R方向、行程、软限位、回零、安全高度、带料最大外形。
3. 开盖、合盖、自转、公转各阶段允许的料盘区运动路径及禁止区域，不能只给一个最终安全点。
4. 当前产品实际启用的扫码、上/下视觉、Hall、合盖回检、自动点检与单臂/单嘴模式。
5. 治具是否需要取空后重新定位，以及拍照视野是否被另外两个持料吸嘴遮挡。

推荐先执行任务1至3做离线和单臂验证，再接入任务4、5的两段并行。不要先在现有主循环中堆叠“第三嘴 if 分支”再补物料与互锁。

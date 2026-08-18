using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MVolt.Rebuild
{
    internal enum VoltelleLanguage
    {
        Chinese,
        English
    }

    internal static class VoltelleLocalization
    {
        private static readonly Dictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "总览", "Overview" },
            { "调校", "Tuning" },
            { "电压与 V/F", "Voltage & V/F" },
            { "功耗通道", "Power Channels" },
            { "配置档", "Profiles" },
            { "接口状态", "Interface Status" },
            { "实时状态与硬件身份", "Live status and hardware identity" },
            { "核心、显存、功耗与锁频", "Core, memory, power and boost lock" },
            { "电压轨、Crossbar 与 V/F 曲线", "Voltage rails, Crossbar and V/F curve" },
            { "逐通道功率、电流与电压", "Per-channel power, current and voltage" },
            { "保存、预览与事务恢复", "Save, preview and transactional restore" },
            { "保存、预览与分项应用", "Save, preview and per-item apply" },
            { "接口支持与验证状态", "Interface support and validation" },
            { "运行模式", "Run mode" },
            { "初始化中", "Initializing" },
            { "正在连接 NVAPI。", "Connecting to NVAPI." },
            { "正在连接 NVAPI…", "Connecting to NVAPI…" },
            { "本软件完全免费", "Completely free software" },
            { "后台运行", "Run in background" },
            { "隐藏主窗口并继续在系统托盘采样", "Hide the main window and keep sampling in the system tray" },
            { "↻  刷新采样", "↻  Refresh" },
            { "一键复位", "Reset All" },
            { "立即把全部可调项目恢复为驱动默认值并执行 GET 回读", "Immediately restore every tunable item to its driver default and verify with GET" },
            { "刷新遥测", "Refresh telemetry" },
            { "打开 NV Voltelle", "Open NV Voltelle" },
            { "退出", "Exit" },
            { "实时遥测、硬件身份与当前调校状态", "Live telemetry, hardware identity and current tuning state" },
            { "核心、显存、功耗与 Boost Lock 的事务调校", "Transactional core, memory, power and Boost Lock tuning" },
            { "核心、显存、功耗与 Boost Lock 的分项调校", "Per-item core, memory, power and Boost Lock tuning" },
            { "Blackwell 电压轨、Crossbar 与 RTX 50 V/F 曲线", "Blackwell voltage rails, Crossbar and RTX 50 V/F curve" },
            { "Power Monitor、Power Topology 与降频原因", "Power Monitor, Power Topology and performance-limit reasons" },
            { "与 GPU/VBIOS 绑定的命名配置档和完整恢复", "Named GPU/VBIOS-bound profiles with complete restore" },
            { "与 GPU/VBIOS 绑定的命名配置档和分项应用", "Named GPU/VBIOS-bound profiles with per-item apply" },
            { "38 个 QueryInterface 入口与实机验证状态", "38 QueryInterface entries and hardware validation status" },
            { "写入可用", "Writes available" },
            { "写入不可用", "Writes unavailable" },
            { "只读模式", "Read-only mode" },
            { "管理员模式 · 常规应用确认，一键复位直接执行", "Administrator mode · confirm regular applies; Reset All runs directly" },
            { "由 --read-only 明确启用", "Explicitly enabled by --read-only" },
            { "请使用正式管理员构建", "Use the formal administrator build" },
            { "UI 验收", "UI QA" },
            { "测试构建 · 硬件 SET 已硬禁用", "Test build · hardware SET is hard-disabled" },
            { "超频有风险，调参需谨慎。", "Overclocking involves risk. Tune carefully." },
            { "使用前请确认", "Before you continue" },
            { "建议一次只调整一个变量，小步推进，并在负载下完成稳定性验证。常规调校会在应用前确认；一键复位直接执行。", "Change one variable at a time, use small steps, and validate stability under load. Regular tuning asks for confirmation; Reset All runs directly." },
            { "当前验证范围：仅限 GeForce RTX 5070 Ti 及以上的桌面端与移动端显卡。其他型号尚未验证。", "Current validation scope: desktop and mobile GeForce RTX 5070 Ti or higher only. Other models have not been validated." },
            { "整板功耗", "Board power" },
            { "温度", "Temperature" },
            { "核心时钟", "Core clock" },
            { "当前 graphics domain", "Current graphics domain" },
            { "当前性能状态", "Current performance state" },
            { "硬件身份", "Hardware identity" },
            { "原程序诊断路径使用的 PCI、总线与架构接口", "PCI, bus and architecture interfaces used by the original diagnostic path" },
            { "PCI 位置", "PCI location" },
            { "GPU 架构", "GPU architecture" },
            { "物理 framebuffer", "Physical framebuffer" },
            { "频率与显存", "Clocks and memory" },
            { "驱动实时值与当前偏移", "Live driver values and current offsets" },
            { "显存时钟", "Memory clock" },
            { "视频时钟", "Video clock" },
            { "核心偏移", "Core offset" },
            { "显存偏移", "Memory offset" },
            { "Crossbar 偏移", "Crossbar offset" },
            { "V/F 曲线", "V/F curve" },
            { "专用显存", "Dedicated memory" },
            { "当前可用", "Currently available" },
            { "可分配显存", "Allocatable memory" },
            { "电气状态", "Electrical status" },
            { "Blackwell 电压轨和驱动限制", "Blackwell voltage rails and driver limits" },
            { "NVVDD 感测", "NVVDD sensed" },
            { "MSVDD 感测", "MSVDD sensed" },
            { "功耗上限", "Power limit" },
            { "已关闭", "Off" },
            { "已开启", "On" },
            { "驱动芯片功耗", "Driver chip power" },
            { "驱动整板功耗", "Driver board power" },
            { "会话能量", "Session energy" },
            { "相对启动时累计", "Accumulated since launch" },
            { "事务写入可用", "Transactional writes available" },
            { "分项写入可用", "Per-item writes available" },
            { "当前运行保持只读", "This run is read-only" },
            { "点击应用后会先显示确认。核心、显存、功耗和 Boost Lock 分别写入并回读；某项失败不会撤销成功项，也不会阻止后续项目。", "Apply first opens a confirmation. Core, memory, power and Boost Lock are written and read back separately. A failed item does not undo successful items or block later items." },
            { "当前仅显示驱动读数，应用按钮不可用，也不会调用 SET。", "Only driver readings are shown. Apply is disabled and no SET is called." },
            { "核心频率偏移", "Core frequency offset" },
            { "显存频率偏移", "Memory frequency offset" },
            { "驱动范围不可用", "Driver range unavailable" },
            { "锁定手动 Boost 电压域", "Lock the manual Boost voltage domain" },
            { "原程序使用 PerfClientLimits domain 6；1,500,000 µV 是控制哨兵，不是工作电压。", "The original uses PerfClientLimits domain 6; 1,500,000 µV is a control sentinel, not an operating voltage." },
            { "恢复当前读数", "Restore current readings" },
            { "应用并验证", "Apply and verify" },
            { "高级写入事务", "Advanced write transactions" },
            { "高级分项写入", "Advanced per-item writes" },
            { "常规应用先确认；一键复位直接执行。所有接口均保留完整快照、回读与失败恢复", "Regular applies ask for confirmation; Reset All runs directly. Every interface retains complete snapshots, readback, and failure recovery" },
            { "常规应用先确认；一键复位直接执行。每项写入后独立回读，失败不回退成功项", "Regular applies ask for confirmation; Reset All runs directly. Each item has independent readback, and failures do not undo successful items" },
            { "目标范围 0..100% · 写后回读", "Target range 0..100% · readback after write" },
            { "独立 ClockDomains 控制与回读验证", "Independent ClockDomains control and verified readback" },
            { "V/F 点控制", "V/F point control" },
            { "单 bit mask · 逐点写入 · 失败继续", "Single-bit mask · point-by-point write · continue after failures" },
            { "Blackwell 电压轨", "Blackwell voltage rails" },
            { "Status 字段与 Control offset 分开显示", "Status fields and Control offsets are shown separately" },
            { "驱动未返回 VoltVoltRails v2 数据。", "The driver did not return VoltVoltRails v2 data." },
            { "电压轨与 Crossbar 目标", "Voltage rail and Crossbar targets" },
            { "输入只在点击应用后进入事务；每次应用前均需单独确认", "Inputs enter a transaction only after Apply; each Apply requires separate confirmation" },
            { "输入只在点击应用后写入；每次应用前均需单独确认", "Inputs are written only after Apply; each Apply requires separate confirmation" },
            { "启用 XOC 电压范围（最高 1.25 V）", "Enable XOC voltage range (up to 1.25 V)" },
            { "REL-only 电压路径", "REL-only voltage path" },
            { "NVVDD 范围", "NVVDD range" },
            { "MSVDD 范围", "MSVDD range" },
            { "当前电压轨不可用", "Current voltage rail unavailable" },
            { "应用并回读", "Apply and read back" },
            { "目标", "Target" },
            { "RTX 50 V/F 曲线编辑器", "RTX 50 V/F curve editor" },
            { "单点 mask、区域 offset、锚点拉平和逐点失败继续", "Point masks, regional offsets, anchor flattening and continue-after-failure point writes" },
            { "右键拖框多选；左键拖动任一已选点可整组上下平移。左右键单选，上下键 ±1 MHz，Shift+上下 ±15 MHz。所有改动仅进入暂存。", "Right-drag a box to select multiple points. Left-drag any selected point to move the group vertically. Left/Right selects one point; Up/Down moves by ±1 MHz and Shift+Up/Down by ±15 MHz. All edits are staged only." },
            { "点击曲线上的点开始编辑", "Click a curve point to start editing" },
            { "单点与拉平", "Point and flatten" },
            { "点索引", "Point index" },
            { "目标频率", "Target frequency" },
            { "暂存点目标", "Stage point target" },
            { "从该点向上拉平", "Flatten above this point" },
            { "区域 offset", "Regional offset" },
            { "起点", "Start" },
            { "终点", "End" },
            { "暂存区域", "Stage region" },
            { "暂存事务", "Staged transaction" },
            { "暂存修改", "Staged changes" },
            { "暂存仅修改预览。真实 SET 按单点发送；某点失败后继续处理剩余点，成功点保持生效。", "Staging changes only the preview. Real SET calls are sent one point at a time; after a point fails, remaining points continue and successful points stay applied." },
            { "暂存全曲线归零", "Stage full-curve reset" },
            { "放弃暂存", "Discard staged changes" },
            { "逐点应用并验证", "Apply and verify each point" },
            { "曲线不可用", "Curve unavailable" },
            { "ADC 校正电压", "ADC corrected voltage" },
            { "raw code 无效时仍保留驱动给出的 corrected µV", "Driver-provided corrected µV is preserved when the raw code is invalid" },
            { "Power Monitor 整板", "Power Monitor board" },
            { "驱动芯片", "Driver chip" },
            { "驱动整板", "Driver board" },
            { "Perf Decrease", "Perf Decrease" },
            { "无降频原因", "No performance-limit reason" },
            { "检测到外接供电不足", "Insufficient external power detected" },
            { "Power Monitor 通道", "Power Monitor channels" },
            { "通道可能重叠或包含汇总项，不能把各行直接相加", "Channels may overlap or include totals; do not sum the rows directly" },
            { "通道", "Channel" },
            { "功率 W", "Power W" },
            { "电流 A", "Current A" },
            { "电压 V", "Voltage V" },
            { "会话 Wh", "Session Wh" },
            { "VBIOS 范围配置档", "VBIOS-scoped profiles" },
            { "JSON schema 与原程序的 mvolt.profile.v1 字段对齐；文件只匹配当前 GPU 名称和 VBIOS。加载待应用不会执行硬件写入。", "The JSON schema matches the original mvolt.profile.v1 fields. Files are bound to the current GPU name and VBIOS. Loading a target does not write hardware." },
            { "已保存配置", "Saved profiles" },
            { "启动行为：仅 GET 当前驱动状态", "Startup behavior: GET current driver state only" },
            { "配置档名称", "Profile name" },
            { "新建", "New" },
            { "保存当前", "Save current" },
            { "载入待应用", "Load as pending" },
            { "载入并验证", "Load and verify" },
            { "删除", "Delete" },
            { "最小化或关闭主窗口时发送到系统托盘", "Send to the system tray when minimized or closed" },
            { "尚未选择配置档", "No profile selected" },
            { "保存当前读数，或从左侧选择一个配置档。", "Save the current readings, or select a profile on the left." },
            { "待应用预览", "Pending preview" },
            { "所选配置预览", "Selected profile preview" },
            { "配置文件", "Profile file" },
            { "原子替换并保留上一版 .bak；保存配置不会在启动时自动载入或写入", "Atomic replacement with the previous .bak retained; saved profiles are not automatically loaded or written at startup" },
            { "启动仅执行读取", "Startup is read-only" },
            { "程序启动时先执行 NVAPI GET，不会自动载入或写入任何保存配置。驱动若仍保留超频值，会如实显示为当前状态；恢复默认需要用户明确操作和确认。", "The app performs NVAPI GET first at startup and never automatically loads or writes a saved profile. If the driver still retains overclocked values, they are shown as the current state; restoring defaults requires an explicit user action and confirmation." },
            { "接口入口集合已完整恢复", "Interface entry set recovered" },
            { "当前驱动可用性", "Current driver availability" },
            { "可用", "Available" },
            { "实现与验证状态", "Implementation and validation" },
            { "入口 ID", "Entry IDs" },
            { "安全与布局测试", "Safety and layout tests" },
            { "SET 接口族", "SET interface families" },
            { "正式版安全边界", "Formal release safety boundary" },
            { "常规应用前显示目标和风险确认；一键复位直接执行，不显示确认弹窗。", "Regular applies show the target and risk confirmation; Reset All runs directly without a confirmation dialog." },
            { "每个事务先保存完整快照，逐项回读；失败时按逆序尝试恢复。", "Every transaction saves a complete snapshot and reads each item back; failures trigger reverse-order restore." },
            { "每个项目独立写入并回读；失败项不会撤销已成功项目，后续项目继续执行。", "Each item is written and read back independently. Failed items do not undo successful items, and later items continue." },
            { "后台托盘只维持实时遥测，不会在未确认时自动写入配置。", "Background tray mode maintains telemetry only and never writes a profile without confirmation." },
            { "写入能力不代表任意参数都安全", "Write capability does not make every parameter safe" },
            { "更大目标、边界值和负向核心仍应由用户逐步验证，不承诺稳定性。", "Larger targets, boundary values and negative core offsets still require gradual user validation; stability is not guaranteed." },
            { "空闲与 FurMark 负载下完成最小写入和恢复", "Minimum writes and restores completed at idle and under FurMark load" },
            { "缓冲区、失败继续、报告、配置档与异常路径", "Buffers, continue-after-failure behavior, reports, profiles and exception paths" },
            { "确认应用硬件修改", "Confirm hardware changes" },
            { "请核对本次目标。只有点击“确认应用”后才会向驱动发送 SET。", "Review this target. SET is sent to the driver only after Confirm Apply." },
            { "不稳定参数可能导致花屏、程序崩溃或驱动重置。请逐步调整并自行完成稳定性测试。", "Unstable parameters may cause artifacts, crashes or driver resets. Tune gradually and perform your own stability testing." },
            { "本次目标", "Target for this operation" },
            { "• 每个项目独立发送写入\n• 每次写入后执行 GET 回读\n• 失败项不会撤销成功项，后续项目继续执行", "• Write each item independently\n• Perform GET readback after each write\n• Failed items do not undo successful items; later items continue" },
            { "空闲与 FurMark 负载下完成最小写入和 GET 回读", "Minimum writes and GET readback completed at idle and under FurMark load" },
            { "我已了解风险，并确认以上目标是我希望应用的参数。", "I understand the risk and confirm that I want to apply the target above." },
            { "取消", "Cancel" },
            { "确认应用", "Confirm Apply" },
            { "确认应用硬件写入", "Confirm hardware write" },
            { "Crossbar offset 必须是整数 MHz。", "Crossbar offset must be an integer in MHz." },
            { "Crossbar offset 超出驱动报告范围。", "Crossbar offset is outside the driver-reported range." },
            { "已放弃电压、Crossbar 与 V/F 暂存，恢复当前驱动读数。", "Discarded staged voltage, Crossbar and V/F changes; restored current driver readings." },
            { "0..100%，使用 VoltRails Control boost byte", "0..100%, using the VoltRails Control boost byte" },
            { "原程序共使用 38 个唯一 QueryInterface ID；37 个已有可核对公开符号，0x527FC458 由调用点确认为 Crossbar MeasureFrequency helper。", "The original uses 38 unique QueryInterface IDs; 37 have verifiable symbols, and call-site analysis confirms 0x527FC458 as the Crossbar MeasureFrequency helper." },
            { "XOC 模式会把配置档和电压目标的上限从 1.15 V 提高到 1.25 V。当前构建仍不会写入显卡，但未来应用该配置可能增加硬件风险。是否保留此选择？", "XOC mode raises the profile and voltage target limit from 1.15 V to 1.25 V. This build still will not write immediately, but applying the setting later may increase hardware risk. Keep this choice?" },
            { "确认 XOC 范围", "Confirm XOC range" },
            { "报告已写入：\n", "Report written to:\n" },
            { "这是重构版的明文 GET-only 兼容报告；尚未复刻原版的公钥加密封装。\n\n", "This is a plaintext GET-only compatibility report from the rebuilt app; the original public-key encrypted wrapper has not been reproduced.\n\n" },
            { "NVAPI 实时采样成功", "NVAPI live sampling succeeded" },
            { "已取消应用；没有发送 SET。", "Apply canceled; no SET was sent." },
            { "UI 验收完成：确认流程可用，测试构建未发送任何 SET。", "UI acceptance passed: the confirmation flow is available and the test build sent no SET calls." },
            { "UI 验收完成：一键复位可直接触发，测试构建未发送任何 SET。", "UI acceptance passed: Reset All triggers directly and the test build sent no SET calls." },
            { "全部可调项目已恢复为驱动默认值，并通过 GET 回读。", "All tunable items were restored to driver defaults and passed GET readback." },
            { "当前运行处于只读模式，未发送任何硬件写入。", "This run is read-only; no hardware write was sent." },
            { "请输入有效的整数调校值。", "Enter valid integer tuning values." },
            { "等待第一次 NVAPI 采样…", "Waiting for the first NVAPI sample…" },
            { "启动 GET 基线已读取 · 未自动应用任何保存配置；", "Startup GET baseline read · no saved profile was applied automatically; " },
            { "等待电压轨和曲线采样…", "Waiting for voltage rail and curve samples…" },
            { "等待 Power Monitor 采样…", "Waiting for Power Monitor samples…" },
            { "等待 GPU 与 VBIOS 信息以初始化配置档…", "Waiting for GPU and VBIOS information to initialize profiles…" },
            { "未连接 NVIDIA 驱动", "NVIDIA driver not connected" },
            { "请检查驱动和 64 位 NVAPI", "Check the driver and 64-bit NVAPI" },
            { "尚未完成驱动探测。", "Driver probing is not complete." },
            { "未提供", "Not provided" }
        };

        private static readonly KeyValuePair<string, string>[] EnglishFragments = new[]
        {
            Pair("已载入配置档目标 · ", "Profile target loaded · "),
            Pair("配置档 “", "Profile “"),
            Pair("制作者 ", "Created by "),
            Pair("B站 @", "Bilibili @"),
            Pair("驱动 ", "Driver "),
            Pair("驱动范围 ", "Driver range "),
            Pair("核心频率偏移", "Core frequency offset"),
            Pair("显存频率偏移", "Memory frequency offset"),
            Pair("功耗上限", "Power limit"),
            Pair("电压轨与 Voltage Boost", "Voltage rails and Voltage Boost"),
            Pair("V/F 曲线", "V/F curve"),
            Pair("允许 ", "Allowed "),
            Pair("当前 ", "Current "),
            Pair("最后采样 ", "Last sample "),
            Pair(" · 2 秒刷新", " · refresh every 2 seconds"),
            Pair("2 秒刷新", "refresh every 2 seconds"),
            // Keep compound point phrases ahead of the generic " 点" replacement.
            Pair("已通过曲线拖动批量暂存 ", "Staged by dragging "),
            Pair("已通过键盘批量暂存 ", "Staged with the keyboard "),
            Pair("已框选 ", "Box-selected "),
            Pair("已选择 ", "Selected "),
            Pair("已通过曲线拖动暂存 V/F 点 ", "Staged by dragging V/F point "),
            Pair("已通过键盘暂存 V/F 点 ", "Staged with the keyboard V/F point "),
            Pair("V/F 曲线点目标无效：", "Invalid V/F curve point target: "),
            Pair(" 个 V/F 点 · ", " V/F points · "),
            Pair("V/F 点 ", "V/F point "),
            Pair("选中点 ", "Selected point "),
            Pair(" 个点 · ", " points · "),
            Pair(" · 主点 ", " · primary "),
            Pair(" · 批量拖动/方向键平移", " · group drag/arrow-key translation"),
            Pair(" · 点偏移 ", " · point offset "),
            Pair(" 个有效点 · ", " valid points · "),
            Pair(" 个暂存变更 · ", " staged changes · "),
            Pair(" 个点待应用", " points pending"),
            Pair(" 个 V/F 点", " V/F points"),
            Pair(" 个 · revision ", " profiles · revision "),
            Pair(" 点", " points"),
            Pair("步进", "steps"),
            Pair(" 分项写入完成。成功：", " per-item write completed. Successful: "),
            Pair("；失败：", "; failed: "),
            Pair("。未回退成功项。", ". Successful items were not rolled back."),
            Pair(" 全部项目已应用并通过回读。", " applied all items and passed readback."),
            Pair(" 无法开始分项写入：", " could not start per-item writes: "),
            Pair(" 执行或回读失败；未自动回退，当前状态以刷新后的 GET 为准：", " failed during execution or readback. No automatic rollback was performed; use the refreshed GET state: "),
            Pair("已应用并通过回读。", " applied and passed readback."),
            Pair("未应用：", " was not applied: "),
            Pair("刷新失败：", "Refresh failed: "),
            Pair("一键复位未完成：", "Reset All did not complete: "),
            Pair("一键复位无法开始：", "Reset All could not start: "),
            Pair("一键复位", "Reset All"),
            Pair("NVAPI 初始化失败：", "NVAPI initialization failed: "),
            Pair("保存配置档失败：", "Failed to save profile: "),
            Pair("删除配置档失败：", "Failed to delete profile: "),
            Pair("已保存配置档：", "Profile saved: "),
            Pair("电压轨: ", "Voltage rails: "),
            Pair("调校 GET: ", "Tuning GET: "),
            Pair("功耗遥测: ", "Power telemetry: "),
            Pair(" · 电压 ", " · voltage "),
            Pair(" · 频率 ", " · frequency "),
            Pair(" · 全局 ", " · global "),
            Pair(" · 合计 ", " · combined "),
            Pair("V/F 区域 ", "V/F region "),
            Pair("已暂存 ", "Staged "),
            Pair("；仅改变选择，尚未执行硬件写入。", "; selection only; no hardware write was performed."),
            Pair("；尚未执行硬件写入。", "; no hardware write was performed."),
            Pair("；仅改变选择，", "; selection only; "),
            Pair("尚未执行硬件写入。", "No hardware write was performed."),
            Pair("未执行硬件写入。", "No hardware write was performed."),
            Pair("关闭", "Off"),
            Pair("开启", "On")
        };

        internal static VoltelleLanguage Current { get; private set; }

        static VoltelleLocalization()
        {
            Current = Load();
        }

        internal static bool IsEnglish
        {
            get { return Current == VoltelleLanguage.English; }
        }

        internal static void Set(VoltelleLanguage language)
        {
            Current = language;
            Save(language);
        }

        internal static string T(string source)
        {
            return Translate(source, Current);
        }

        internal static string Translate(string source, VoltelleLanguage language)
        {
            if (String.IsNullOrEmpty(source) || language == VoltelleLanguage.Chinese) return source;
            string translated;
            if (English.TryGetValue(source, out translated)) return translated;
            translated = source;
            for (int index = 0; index < EnglishFragments.Length; index++)
                translated = translated.Replace(EnglishFragments[index].Key, EnglishFragments[index].Value);
            return translated;
        }

        private static KeyValuePair<string, string> Pair(string source, string target)
        {
            return new KeyValuePair<string, string>(source, target);
        }

        private static string SettingsPath()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (String.IsNullOrEmpty(local)) return null;
            return Path.Combine(local, "NV Voltelle", "language.txt");
        }

        private static VoltelleLanguage Load()
        {
            try
            {
                string path = SettingsPath();
                if (path != null && File.Exists(path))
                {
                    string value = File.ReadAllText(path, Encoding.UTF8).Trim();
                    if (String.Equals(value, "en", StringComparison.OrdinalIgnoreCase)) return VoltelleLanguage.English;
                    if (String.Equals(value, "zh", StringComparison.OrdinalIgnoreCase)) return VoltelleLanguage.Chinese;
                }
            }
            catch { }
            return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? VoltelleLanguage.Chinese
                : VoltelleLanguage.English;
        }

        private static void Save(VoltelleLanguage language)
        {
            try
            {
                string path = SettingsPath();
                if (path == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
                File.WriteAllText(temporary, language == VoltelleLanguage.English ? "en" : "zh", new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", true);
                else File.Move(temporary, path);
            }
            catch { }
        }
    }
}

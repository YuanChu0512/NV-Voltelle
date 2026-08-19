# NV Voltelle

## 作者信息

- 制作者：Mozelle
- B站 ID：Mozelle_33
- 本软件完全免费
- 许可证：MIT

## 主要功能

- 核心与显存频率偏移、功耗上限、Boost Lock。
- NVVDD/MSVDD 电压范围、Voltage Boost。
- Crossbar、SYS Clock、Video Clock 独立偏移控制与 GET 回读。
- RTX 50 V/F 127 点曲线编辑、右键框选、多点平移、区域偏移、拉平与逐点写入。
- 多路显卡风扇 Auto/Manual 控制、百分比滑动条、RPM 和写后回读。
- Power Monitor、Power Topology、ADC、温度、显存和降频原因遥测。
- GPU/VBIOS 绑定配置档、分项应用、一键复位。
- 可选的延迟开机自启与指定配置档自动应用；普通手动启动保持只读初始化。
- 中文/English、系统托盘后台运行。
- 启动时逐项探测驱动能力；不可用控制项显示灰色遮罩并禁止输入。

## 使用方法

1. 从 Releases 下载最新版，解压后启动 `NV Voltelle.exe`；程序会申请管理员权限。
2. 在调校页面修改数值，核对确认弹窗后应用；每项写入后都会执行 GET 回读。
3. 分项应用只同步本项回读，不会清空其他未应用草稿；V/F 仅移除写入成功的点。
4. 风扇页面可逐路选择 Auto 或 Manual；Manual 使用驱动实时报告的百分比范围。
5. V/F 页面支持右键框选和批量上下平移，改动先暂存，再逐点应用。
6. 配置档页面可保存当前参数；启用“延迟开机自启”后，选择配置档和 10–600 秒延迟即可安装登录计划任务。
7. 手动启动不会自动应用配置档；只有开机自启任务会在设定延迟后自动应用指定配置档。
8. 页头“一键复位”会分项恢复核心/显存、功耗、Boost Lock、电压、Crossbar、SYS、Video、V/F 和风扇 Auto；失败项不会阻止后续项。

配置档默认保存到 `%LocalAppData%\NV Voltelle\profiles`。

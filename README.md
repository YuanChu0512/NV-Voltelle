# NV Voltelle

## 作者信息

- 制作者：Mozelle
- B站 ID：`Mozelle_33`
- 当前版本：`1.3.3`
- 软件完全免费
- 开源许可证：[MIT License](./LICENSE)

## 主要功能

- 核心与显存频率偏移
- 功耗上限与 Boost Lock
- Blackwell NVVDD/MSVDD 电压轨与 Voltage Boost
- Crossbar 偏移
- RTX 50 V/F 127 点曲线编辑
- V/F 右键框选、多点整体平移、键盘微调、区域修改与拉平
- Power Monitor、Power Topology 与降频原因遥测
- 中英文界面
- 系统托盘后台运行
- GPU/VBIOS 绑定配置档
- 一键复位全部调校项目

## 使用方法

1. [下载 NV Voltelle.exe](./NV%20Voltelle.exe)。
2. 双击运行并接受 UAC 管理员权限请求。
3. 确认软件正确识别显卡、驱动和 VBIOS。
4. 调整需要的参数，点击应用并确认。
5. 使用压力测试检查稳定性。

命令行参数：

- `--read-only`：只读取，不发送硬件写入。
- `--start-in-tray`：启动后进入系统托盘。

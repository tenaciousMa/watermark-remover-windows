# 发布流程

## 版本号

版本采用语义化版本，例如 `0.4.0`。主程序、安装包和 Release 标签保持
一致。

## 发布前检查

- 主界面启动正常
- 三种处理模式分别通过 smoke test
- 失败原因可读
- 预计时间和剩余时间显示正确
- 进度条从左向右增长
- 安装、启动、卸载流程通过
- 第三方许可证完整

## GitHub Release

Release 需要上传：

- `WatermarkRemoverWindows_v0.4_Setup.exe`
- 对应的便携版压缩包
- `SHA256SUMS.txt`
- 发布说明

由于 GitHub 单个 Release asset 上限为 2GB，超过上限时使用 Inno Setup
分卷输出或拆分压缩包，并保留校验文件。

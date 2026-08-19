# SmartWinRAR

一个当前用户级的 WinRAR 右键菜单扩展：

- 压缩包根目录只有一个文件或文件夹时，直接解压到压缩包所在目录。
- 根目录有多个项目时，解压到压缩包同名文件夹。
- `name.tar.gz` 和 `name.tgz` 的多项目结果使用 `name` 作为文件夹名。

## 系统修改

安装程序只创建：

1. `%LOCALAPPDATA%\WinRARSmartExtract\WinRARSmartExtract.exe`
2. `HKCU\Software\Classes\SystemFileAssociations\<扩展名>\shell\WinRARSmartExtract`

右键菜单按扩展名注册，不依赖文件关联中的 `PerceivedType`。支持 `.zip`、`.rar`、`7z`、`.tar`、`.gz`（包括 `.tar.gz`）、`.tgz`，以及程序能够检测的 ZIP 家族和单流压缩格式。

不安装服务，不写入开机启动项，不修改 WinRAR 文件，不需要管理员权限。卸载程序删除上述文件、目录和注册项。

## 源码结构

- `src/SmartExtract.cs`：压缩包根目录判断及 WinRAR 调用。
- `src/Installer.cs`：当前用户级安装程序，内嵌助手 EXE。
- `src/Uninstaller.cs`：完整删除安装内容。
- `src/app.manifest`：`asInvoker` 运行级别及 Windows 兼容性声明。
- `build.ps1`：使用 Windows .NET Framework 自带的 `csc.exe` 构建。

## 构建要求

- Windows 10/11
- Windows PowerShell 5.1
- .NET Framework 4.x
- 构建不需要网络或第三方 SDK
- 运行时需要已安装 WinRAR

## 构建命令

双击运行`双击编译.bat`即可，之后执行`dist\安装.exe`即可完成安装

## 实现说明

助手优先使用 Windows 自带的 `tar.exe` 读取压缩包目录，并包含 RAR、ZIP 和 TAR/GZip 的兼容回退。实际解压始终由 `WinRAR.exe` 完成。加密文件头或损坏压缩包可能无法智能判断，此时程序会停止并提示改用 WinRAR 原有菜单。

## 代码签名

默认构建未包含商业代码签名，因此可能出现 SmartScreen 提示。

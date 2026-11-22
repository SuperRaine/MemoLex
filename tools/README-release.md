# 构建开箱即用发行包

需要 Windows x64、.NET 8 SDK、Python 3、WiX Toolset 5.0.2。构建工具仅用于开发，不需要安装在用户电脑。

1. 从已解压的便携包复制完整 `app/data` 文件夹到仓库根目录的 `data`，也可构建时通过 `-DataDirectory` 指定其路径。确认 `src/Bootstrapper` 中保留已校验的微软网页组件安装引导程序。
2. 执行 `powershell -File tools/build_release.ps1`，生成 `dist/MemoLex`。使用 Windows 自带的 .NET Framework 编译启动与卸载入口，将 .NET 8.0.34 和 WPF 运行组件合并到 `app/MemoLex.exe`。
3. 在仓库根目录执行 `dotnet tool install wix --version 5.0.2 --tool-path .tools/wix`，然后 `.tools/wix/wix.exe extension add WixToolset.UI.wixext/5.0.2`。
4. 执行 `powershell -File installer/build.ps1 -PayloadDirectory dist/MemoLex -OutputFile dist/MemoLex-1.12.0-Windows-x64-Setup.msi -WixCommand .tools/wix/wix.exe`。
5. 执行 `python tools/package_portable.py dist/MemoLex dist/MemoLex-1.12.0-Windows-x64.zip`，生成便携包。

安装包与便携包使用同一份文件。主目录仅保留 `MemoLex.exe`、`MemoLex.dll`、`Uninstall.exe` 和 `app` 文件夹。离线词库、助记及许可证均位于 `app`；运行组件首次启动时解压到 `%LOCALAPPDATA%/MemoLex/runtime`。

MSI 为当前 Windows 用户安装，默认目录为 `%LOCALAPPDATA%/Programs/MemoLex`。向导选择存放位置后自动创建 `MemoLex` 文件夹，桌面快捷方式可取消。两种版本均提供卸载入口，默认删除本机资料，开启「保留用户资料」可保留。正常升级始终保留资料。卸载按文件清单移除软件，保留目录中额外添加或已修改的文件。

默认使用电脑共享的网页运行环境，包内包含小型安装引导程序。若需要可选的完整网页引擎，把运行时放在 `webview2`，并在构建、MSI 验证中都传入 `-IncludeBrowserRuntime`，打包时传入 `--include-browser-runtime`。默认安装包和便携包使用相同的文件筛选规则，不包含整个 `webview2` 目录。

GitHub Releases 只上传软件包。Windows 提供 `.msi` 和 `-Windows-x64.zip`；Mac 只提供 `.dmg`。验证报告和校验清单不放入源码或发布附件。

Windows 如使用可信的代码签名证书，在构建应用文件和 MSI 时传入同一个 `-SigningThumbprint`。若 Windows SDK 的 SignTool 未加入 PATH，另传 `-SignToolCommand` 指定其路径。脚本会添加时间戳并核验签名。

助记正文和升级匹配表在 `mnemonics`，随包复制到 `data`。

公开软件包使用 MyMemory 翻译。本机构建可在 `src/Private/DeepL.key` 配置自己的 DeepL 密钥，该目录受版本控制忽略。

## 合并内置词库

从原版的完整 `app/data` 准备合并后的数据。输出目录须为新目录。

```powershell
dotnet run --project tools/MergeDictionary -c Release -- <原词库目录> <优先词库文件> <合并词库目录>
```

同名词条只采用优先词库的释义。原词库补充未收录的词条，也为同名词条保留音标、词形和词频。合并时会重建中文反查索引。Windows 构建将合并目录传给 `-DataDirectory`。Mac 构建将包含合并后 `data` 与 `ThirdParty` 的 `app` 目录传给 `MEMOLEX_DATA_ROOT`。

## Mac

在安装了 Xcode Command Line Tools、Swift 5.9 以上及 .NET 8 SDK 的 Mac 上，执行 `bash macos/build.sh`。脚本从固定版本便携包取得并校验离线数据，生成包含 Apple Silicon 和 Intel 组件的 `MemoLex.app` 和 DMG，并生成供构建校验使用的内部 ZIP；发布时只上传 DMG。也可用 `MEMOLEX_DATA_ROOT` 指定已有的 `app` 数据目录，用 `MEMOLEX_OUTPUT` 指定空的输出目录。

原生界面在 `macos/Sources`，查词与云端接口复用 `src/Core` 和 `src/Services`，由私有管道通信。GitHub Actions 的「Build macOS」可传入 Windows 便携包附件的 ID 和 SHA-256，从同一份离线数据构建 Mac 安装包。默认进行本机签名，Apple 开发者签名与公证需要维护者自己的证书。

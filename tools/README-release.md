# 构建开箱即用发行包

需要 Windows x64、.NET 8 SDK、Python 3、WiX Toolset 5.0.2。构建工具仅用于开发，不需要安装在用户电脑。

1. 从现有软件目录或便携包复制完整 `data` 文件夹到仓库根目录，也可构建时通过 `-DataDirectory` 指定其路径。确认 `src/Bootstrapper` 中保留已校验的微软网页组件安装引导程序。
2. 执行 `powershell -File tools/build_release.ps1`，生成 `dist/MemoLex`。自带 .NET 8.0.34 及 WPF 原生依赖。
3. 在仓库根目录执行 `dotnet tool install wix --version 5.0.2 --tool-path .tools/wix`，然后 `.tools/wix/wix.exe extension add WixToolset.UI.wixext/5.0.2`。
4. 执行 `powershell -File installer/build.ps1 -PayloadDirectory dist/MemoLex -OutputFile dist/MemoLex-1.5.0-Windows-x64-Setup.msi -WixCommand .tools/wix/wix.exe`。

安装包与便携包使用同一份文件。MSI 为当前 Windows 用户安装，默认目录为 `%LOCALAPPDATA%/Programs/MemoLex`，支持修改目录、开始菜单和桌面入口、修复、卸载和后续升级。卸载仅移除安装文件，不删除 `%LOCALAPPDATA%/MemoLex` 中的个人设置、助记、历史与凭据。

默认使用电脑共享的网页运行环境，包内包含小型安装引导程序。若需要可选的完整网页引擎，把运行时放在 `webview2`，并在构建、MSI 验证中都传入 `-IncludeBrowserRuntime`，打包时传入 `--include-browser-runtime`。默认安装包和便携包使用相同的文件筛选规则，不包含整个 `webview2` 目录。

GitHub Releases 只上传安装包（`.msi`）和便携包（`-Windows-x64.zip`）。验证报告和校验清单保留在本地；源码通过仓库及 GitHub 自动生成的源码下载项提供。

助记正文和升级匹配表在 `mnemonics`，随包复制到 `data`。

本仓库不包含服务私钥。自行构建时，如未在本机 `src/Private/DeepL.key` 提供自己的授权，在线翻译使用 MyMemory；发行二进制包使用维护者配置的服务。该目录受版本控制忽略。

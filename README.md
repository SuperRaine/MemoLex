# 墨墨搜词 MemoLex

蓝色猫头鹰陪你查词。面向 Windows 的离线英汉词典，连接墨墨云词本，查词、助记与真题例句放在同一页。

**版本 1.1.0** · Windows x64 · .NET 8 WPF · MIT 开源

[下载安装版与便携版](https://github.com/SuperRaine/MemoLex/releases/tag/v1.1.0) · [使用说明](使用说明.md) · [提交反馈](https://github.com/SuperRaine/MemoLex/issues/new/choose)

1.1.0 提供完整安装包和便携包，均自带运行环境、77 万余离线词条与本地助记。2,773 条旧式词族列表已逐词改写为拆词、形近词、谐音、词根或多义助记。修正换号后误报离线，查词时自动显示已学状态；今日新学添加前保留同步设置确认。

## 功能

- 77 万余离线词条、中文反查、完全匹配优先、同义扩展。
- 单页阅读释义、可编辑本地助记与真题例句，支持单词和搭配跳转、前进后退。
- 近义词、形近词、派生词、介词搭配及释义；搜词历史。
- 墨墨云词本收藏、批量编辑、分组与 Ctrl/Shift 多选；整本删除单独确认。
- 将当前单词添加到墨墨今日新学，先检查学习记录，再核对新词状态。
- 单词和段落翻译，双击 Ctrl+C 查询、可自定义全局快捷键。
- 蓝色主题、自定义圆角标题栏、置顶、托盘、实时缩放、可选开机自启动。
- 分页设置、邮箱 / QQ 群 / GitHub Issues 反馈，以及自愿赞助入口。

## 安装与运行

安装版：下载 `MemoLex-1.1.0-Windows-x64-Setup.msi`，双击后按中文向导安装，可修改目录。便携版：下载 `MemoLex-1.1.0-Windows-x64.zip`，解压后运行 `MemoLex/MemoLex.exe`。两者均适用于 Windows 10/11 x64，已包含 .NET/WPF 运行环境、全部离线词库及本地助记，无需首次启动下载数据。

离线查词无需账号。云词本和个人云端助记需要自己的墨墨开放 API Token，在设置的“墨墨账号”页填写。Token 使用 Windows DPAPI 加密保存在当前用户的本地目录，不随运行包分发。

## 从源码构建

安装 Windows x64 .NET 8 SDK，在仓库根目录执行：

```powershell
powershell -File tools/build_release.ps1
```

构建前将同版本便携包中的整个 `data` 文件夹复制到仓库根目录。脚本会构建自包含程序并复制数据。词典数据保留在 Release 附件中，不直接提交大型索引到 Git；重建步骤见 [词典工具说明](tools/README-dictionary.md)；安装包构建见 [发行包说明](tools/README-release.md)。

## 验证

```powershell
dotnet run --project tests-core/MemoLex.CoreTests.csproj -c Release
dotnet run --project tests/MemoLex.BackendTests.csproj -c Release
dotnet run --project tests-examples/MemoLex.ExamplesTests.csproj -c Release
$env:MEMOLEX_DATA_DIR = (Resolve-Path dist/MemoLex/data).Path
dotnet run --project tests-features/MemoLex.FeatureTests.csproj -c Release
dotnet run --project tests-ui/MemoLex.UITests.csproj -c Release
```

测试使用模拟 HTTP 与隔离临时配置，不修改真实墨墨账号。WPF 测试在不激活的屏幕外窗口渲染；UI 预览默认写入 `work/MemoLex/ui-review`。

## 数据与许可

应用代码采用 MIT 许可。词典、词网等数据遵循各自的上游许可，运行包 `data` 内附完整归属及许可：

- [ECDICT](https://github.com/skywind3000/ECDICT)：MIT。
- [Princeton WordNet](https://wordnet.princeton.edu/)：WordNet 许可。
- [Chinese Open Wordnet](https://github.com/bond-lab/cow)：上游数据许可见运行包。
- [MorphyNet](https://github.com/kbatsuren/MorphyNet)：部分构词资料来源，CC BY-SA 3.0。

内置助记覆盖 10,613 个目标词（含搜索别名共 10,614 键），对应此前整理的词表及公开考试主词表与经核实派生词。本次 2,773 条旧式词族列表已全部替换；全目标集累计 4,024 词按最新标准改写，其余 6,589 词仍待进一步完善。词表覆盖与内容复核进度分别记录在 `mnemonics/mnemonics.meta.json`。助记数据采用 CC BY-SA 3.0，完整归属随包附带。个人修改、凭据和历史保存在 `%LOCALAPPDATA%/MemoLex`，未打包发布。

## 赞助

在“设置 → 赞助与关于”中使用微信或支付宝自愿赞助。感谢你支持项目的开发与维护。

## 反馈

- [GitHub Issues](https://github.com/SuperRaine/MemoLex/issues/new/choose)
- 反馈邮箱：1623255609@qq.com
- QQ 交流群：1132371819

# 墨墨搜词 MemoLex

蓝色猫头鹰陪你查词。面向 Windows 的离线英汉词典，连接墨墨云词本，查词、助记与真题例句放在同一页。

**版本 1.0.0** · Windows x64 · .NET 8 WPF · MIT 开源

[下载软件](https://github.com/SuperRaine/MemoLex/releases/latest) · [使用说明](使用说明.md)

## 功能

- 77 万余离线词条、中文反查、完全匹配优先、同义扩展。
- 单页阅读释义、可编辑本地助记与真题例句，支持单词和搭配跳转、前进后退。
- 近义词、形近词、派生词、介词搭配及释义；搜词历史。
- 墨墨云词本收藏、批量编辑、分组与 Ctrl/Shift 多选；整本删除单独确认。
- 单词和段落翻译，双击 Ctrl+C 查询、可自定义全局快捷键。
- 蓝色主题、自定义圆角标题栏、置顶、托盘、实时缩放、可选开机自启动。
- 分页设置，以及自愿赞助入口。

## 安装与运行

下载 Releases 的 Windows x64 压缩包，解压后运行 `MemoLex/MemoLex.exe`。需要 [.NET 8 Desktop Runtime（Windows x64）](https://dotnet.microsoft.com/download/dotnet/8.0)。

离线查词无需账号。云词本和个人云端助记需要自己的墨墨开放 API Token，在设置的“墨墨账号”页填写。Token 使用 Windows DPAPI 加密保存在当前用户的本地目录，不随运行包分发。

## 从源码构建

安装 Windows x64 .NET 8 SDK，在仓库根目录执行：

```powershell
dotnet publish src/MemoLex.csproj -c Release -r win-x64 --self-contained false -o dist/MemoLex
```

将同版本运行包中的整个 `data` 文件夹复制到 `dist/MemoLex/data`，再运行 EXE。词典数据保留在 Release 附件中，不直接提交大型索引到 Git；重建步骤见 [词典工具说明](tools/README-dictionary.md)。

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

内置助记为已完成的部分词条，不代表覆盖全部考试词表。个人助记和查询历史保存在 `%LOCALAPPDATA%/MemoLex`，不包含在公开源码和软件压缩包中。

## 赞助

在“设置 → 赞助与关于”中使用微信或支付宝自愿赞助。感谢你支持项目的开发与维护。

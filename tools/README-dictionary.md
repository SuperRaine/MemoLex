# 离线词库与重建

源码包不重复附带约 170 MB 的生成词库或 66 MB 的 CSV。最便捷的方式是把运行包 `MemoLex/data` 整个目录复制到源码包根目录的 `data` 文件夹；发布时，把这个目录放在可执行文件旁。

如果需要从原始数据重建，请使用 Python 3.10 或更新版本，无需安装第三方库。在源码包根目录执行：

```powershell
python tools/download_ecdict.py
python tools/build_dictionary.py
```

下载脚本默认只向源码包自身的 `work/ecdict-source/` 目录写入 CSV、分段缓存及许可证；构建脚本默认只向源码包自身的 `data/` 目录写入生成词库。默认路径根据脚本所在位置解析，不依赖启动命令时的当前目录。可显式指定路径：

```powershell
python tools/download_ecdict.py --output "D:\MemoLex-build\ecdict.csv" --workers 6
python tools/build_dictionary.py --source "D:\MemoLex-build\ecdict.csv" --output "D:\MemoLex\data" --license-file "D:\MemoLex-build\ECDICT-LICENSE.txt"
```

上游来源：[skywind3000/ECDICT](https://github.com/skywind3000/ECDICT)，MIT 许可。脚本固定使用提交 `39cfb9672c0906b63569e0453c2e6abfb9ab182a`，并校验 CSV 的 SHA-256：`d1ecbdf9ee2b920afb6f430ca6b6bb9f559588cf0dff86e04bd255f9390843cc`。下载使用可重试并行 HTTP Range，随后校验整文件哈希；不会使用墨墨 Token。

生成结果为 770,611 条词目，数据格式如下：

- `dictionary.index.tsv`：UTF-8，无表头，每行 `spelling<TAB>byte_offset<TAB>byte_length`。
- `dictionary.data`：UTF-8 JSON 记录串联；偏移与长度以字节计。各字段均为字符串，缺失值为 `""`。
- 字段为 `word`、`phonetic`、`definition`、`translation`、`pos`、`collins`、`oxford`、`tag`、`bnc`、`frq`、`exchange`。
- `definition` 和 `translation` 中上游字面量 `\n` 已规范化为实际换行。
- 索引保持上游顺序；应用加载后按 `StringComparer.OrdinalIgnoreCase` 排序，用于前缀建议。
- `ECDICT-LICENSE.txt`、`README-ECDICT.txt`、`dictionary.meta.json` 随生成结果一并写入。

构建脚本在替换输出文件前会校验 `apple`、`ephemeral`、`serendipity` 的随机读取与中文释义。重建时请先关闭正在使用同一词库目录的 MemoLex。

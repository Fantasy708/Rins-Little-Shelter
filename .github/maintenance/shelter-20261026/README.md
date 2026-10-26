# Shelter 2026-10-26：已实测程序重建备选

云端正常编译字节与本地已实测EXE不同时，从已核验的旧便携程序和patch.gz重建相同的新EXE。此工具仅重建文件，不执行EXE；纯Python3标准库，无第三方依赖。

- 固定源码冻结提交：`87135ce05dacb2e0f434582187bedf48a231bef6`（Source871）。源码包仍以该提交为准，本辅助目录属于后续发行工作流，不混入冻结源码包。
- 旧EXE：200779365字节，SHA256 `2B767B17F8D945484D7930DAE1F1822BE56C9EA47FD1AC9480A0B3C02653F797`。
- 新EXE：200783461字节，SHA256 `73396FCCAFD449CD195C536BCAB555B0DF1F4582771DACB3E917A56D56ABFD78`。
- patch.gz：473619字节，SHA256 `B351BADBC642293240B33DE9F69E4E370CB531CC420E4AC1BF0B5CD12E95D65E`。
- apply_rin_patch.py：SHA256 `4AC68DECF9F1E40DFA7F6A4E396FF94AF58452DE4EDA89DB52CBC43E8AE2BE9D`。

```text
python apply_rin_patch.py old/RinsLittleShelter.exe patch.gz rebuilt/RinsLittleShelter.exe
```

调用前校验可信记录中的旧EXE、补丁与脚本SHA256，输出路径须尚不存在；重建后再次与上列新EXE SHA256核对。重建器检查旧/新哈希、最大512MiB、操作边界、输出长度、压缩CRC及无尾随数据，成功后才暴露输出文件。该备选本地重建已匹配并通过48项格式/拒绝检查，不替代源码来源、发布附件或程序运行验收。

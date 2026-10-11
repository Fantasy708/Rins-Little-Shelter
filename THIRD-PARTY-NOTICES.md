# 第三方组件与许可

| 组件 | 版本 | 来源与许可原文 |
| --- | --- | --- |
| NAudio及其模块 | 3.1.0 | [上游项目](https://github.com/naudio/NAudio)，MIT，`LICENSES/NAudio.txt` |
| Microsoft.NETCore.App Windows x64运行时 | 10.0.12 | [上游项目](https://github.com/dotnet/runtime)，`LICENSES/NET-runtime.txt`、`NET-runtime-third-party.txt` |
| Microsoft.WindowsDesktop.App Windows x64运行时 | 10.0.12 | [上游项目](https://github.com/dotnet/wpf)，`LICENSES/WindowsDesktop.txt` |
| System.Numerics.Tensors | 9.0.0 | [上游项目](https://github.com/dotnet/runtime)，`LICENSES/System.Numerics.Tensors.txt`、`System.Numerics.Tensors-third-party.txt` |

Microsoft许可与第三方声明文件直接复制自构建使用的对应NuGet包。NAudio许可复制自其上游项目。原文保留不变，并随便携程序与源码包交付。

NuGet锁定文件还记录构建工具与Windows SDK引用，它们用于构建，不等同于全部打包为运行时组件。项目专用SDK不随发布包分发。

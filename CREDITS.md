# 来源与署名

## 原作与插画

- Rin 来自 Porter Robinson、Madeon 与 A-1 Pictures 的《Shelter》，形象以 [官方设定](https://www.sheltertheanimation.com/)为参考。
- 本项目是同人桌面作品，与原作官方无隶属或背书关系。
- `assets/rin` 和 `assets/world` 的PNG图集由内置imagegen工具生成，并以已有素材保持角色一致。制作提示词见 [素材制作](docs/素材制作.md)。
- `assets/app/rin-shelter.png`为以现有角色素材作为参考、由内置imagegen生成的应用图标原稿；同目录ICO仅做Windows标准尺寸的格式转换，用于EXE、窗口与托盘。提示词及转换方式同见[素材制作](docs/素材制作.md)。
- 未附带原曲、原片音轨或官网角色原图。角色、名称及原作相关权利归相应权利人；代码许可证不授予这些权利，也不自动适用于角色和场景图片。

父女记忆以[官方短片](https://www.youtube.com/watch?v=fzQ6gRAEoy0)约3:46的秋千及4:06的整理发饰为参考。父亲和幼年Rin均为本项目重新生成的插画，不附带原片画面或音轨。提示词与选帧记录见[多场景素材制作](docs/多场景素材制作.md)。

Nurture草地与俯卧动作致敬[Porter Robinson官方商店的标准版正式封面](https://store.porterrobinson.com/products/nurture-2lp-standard-vinyl)，以自然草地和人物姿态为参考重新生成Rin插画。未附带摄影封面、歌曲或音轨；提示词和配准见[Nurture素材制作](docs/Nurture素材制作.md)。

v0.4.0-preview.1山脉与极地参考用户提供的《Shelter》原片截图：赭红砂岩台地、蓝色雪景与冰柱、绿色至黄绿色极光。场景和专用登山/接雪姿势均由内置imagegen重新绘制，极光以独立透明贴图呈现；不分发用户截图、原片或音乐。参考边界、完整提示词和资源锚点见[户外探索素材制作](docs/户外探索素材制作.md)。

## 软件组件

采用.NET 10、WPF、NAudio 3.1.0及System.Numerics.Tensors 9.0.0。依赖版本记录在 `src/packages.lock.json`；第三方许可原文见 `LICENSES`，说明见 [第三方组件](THIRD-PARTY-NOTICES.md)。

本项目代码许可证尚待维护者选择；第三方组件仍按各自许可证使用。

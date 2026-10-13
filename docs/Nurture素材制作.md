# Nurture草地与彩蛋素材制作

v0.3.0-preview.2；2026-10-13按用户反馈重绘。

## 参考

实际查看[Porter Robinson官方标准版2LP商品图](https://store.porterrobinson.com/products/nurture-2lp-standard-vinyl)，确认标准摄影封面和白色唱片。参考SHA256：`5CE64856989345176767E42F4E74BB4CCAE59B5E857AA4FB52DBB546A7EBA9F1`。官方原图仅在本机assets/reference，不随包分发，原曲同样不附带。

初稿仍像旧花环草坪，人物随方向镜像，用户指出不符合要求。本次重绘为连续细密草甸：自然绿草交错，小白花和少量小黄花均匀散布中央和边缘，取消花环、装饰叶边框与黄色空心区。保持插画，未贴摄影封面。

另核对用户真人cos的姿态关系：背向镜头，头朝右上、脚朝左下，两臂向两侧展开。只采用姿态，不复制真人身份或公开私人照片。保持项目Rin衣服、头身比例和长发，脸自然朝下，不强制露脸。

## 资源及锚点

- `assets/world/nurture-grass.png`：1536×1024，两格分别用于420×125草坡和350×75院子草地。按y=640分格，旧alpha一次性遮罩保留旧尺寸、外部透明轮廓和命中；旧Placement及其他场景不改。
- `assets/rin/nurture-life.png`：1536×1024，5列4行，20个完整连接的独立姿势；0～9下降，10～19撑起与起身，第9格为双臂展开的斜向俯卧。专用姿势固定朝右，不随步行朝向翻转。
- `assets/manifest.json`及`scripts/register-nurture.py`记录每格独立矩形、地面原点、掌/膝/凉鞋接触点。登记脚本只写JSON，不改PNG。前18格站姿参考289，第18格288，第19格297源像素，完整起止站姿约150逻辑像素，低姿势不会按包围高度放大。
- 后两格矩形局部包含上一行的鞋子。仅对Nurture图集读取启用最大连通完整身体及2像素抗锯齿边缘，排除邻格碎片，原始PNG不修改。其他图集不启用。
- 停留单帧，约0.15%呼吸，掌/脚边缘2～3逻辑像素细草轻动，不遮整段肢体；动作内部不逐帧透明叠加。

## 时序与空间

实际步行并站稳0.35秒；朝左进入时先完成已有12姿势、0.8秒真实转身，再收起平板。下降1.2秒、停留4.5秒、起身1.2秒，约120毫秒一个过渡姿势，再步行回日常位置。按实际时间推进。

净空依据展开的远侧掌、近侧掌及脚部核对原草地椭圆，横向保留220像素，障碍范围包含头发。草坡可以容纳；现有75像素深的院子草地即使清空家具，也容不下远侧手掌，本版院子入口禁用，不强缩人物、不移动或重置布置。隐藏、未解锁、靠边及家具/小路冲突同样禁用；室内、云台、家具和秋千禁用，仅手动触发。

## 内置imagegen实际采用提示词

未采用初稿。新版完整姿势两次被服务拒绝、无输出，随后先生成中性的关键姿势，再以它作为完整序列参考。最终图片保留生成原透明通道。

### 新草甸

输入：官方标准版商品图，项目持板图集只作插画风格参考。

```text
Use case: precise-object-edit. Asset type: transparent hand-painted anime meadow texture atlas. Input 1 is the official Porter Robinson Nurture standard cover product image. Use ONLY the photograph of the meadow on its square cover, not the records or package. Input 2 is our existing illustrated Rin sprite sheet, used solely for compatible anime painting finish. The grass must be HIGHLY SIMILAR to that actual cover: a continuous wild meadow carpet of narrow, fine long green grass blades criss-crossing naturally, medium and cool natural greens with many small white field flowers and a few small yellow flowers scattered THROUGHOUT the whole meadow. Soft even daylight, photographic cover composition translated into finely painted anime background art. Completely REPLACE our previous decorative garden grass look. No perimeter flower wreath, no raised border, no empty golden/yellow centre, no ornamental shrub leaves, no giant daisies, no neatly arranged flower beds, no rocks, no furniture, no trees. Flowers are very tiny relative to grass, irregularly distributed through the centre as well as the edge. Draw exactly two isolated wide shallow oval carpet cutouts stacked vertically, with a large transparent gap: top is a 420-by-125 logical pixel meadow carpet, bottom a 350-by-75 garden carpet using the same natural meadow texture. Each carpet has the same uniform natural vegetation flowing all the way to the transparent edge, NO framed border and NO yellow spotlight or vignette. This is NOT a decorative game garden island. Preserve a soft anime illustration medium that fits Rin, do not paste a photo. No human figure, no vinyl discs, no lettering. True transparent alpha outside both grass carpets. Entire objects with transparent margins.
```

### 草地图集透明整理

输入：上一张生成草地图集。

```text
Edit this meadow atlas. Keep the two natural fine-grass carpets and small scattered wildflowers exactly. Remove ALL green glow, dark background and blurred haze outside the two carpets. Output isolated clean grass cutouts with TRUE completely transparent alpha zero outside their crisp organic grassy silhouettes. Keep the gap between them transparent. No green halo or shadow. Do not change the natural continuous meadow vegetation.
```

### 右向关键姿势

输入：现有Rin持板图集。

```text
A production animation key pose of the existing illustrated character in reference image, same clothing, hair, bow and soft anime proportions. Calm fully clothed nature rest, lying on her stomach, back seen from a slightly elevated camera. Her head points toward the upper RIGHT and her feet extend to the lower LEFT. Both arms extend comfortably outward sideways, palms resting on the ground. Long hair settles across the back and hides the face naturally. White blouse, pink shorts and white sandals remain exactly as reference. Full body entirely visible within transparent margins. Ordinary relaxed rest, no exaggerated anatomy or expression. No grass, scenery, floor, props, text or shadow; genuine transparent background. Do not mirror left.
```

### 完整20姿势

输入：现有Rin持板图集、上述关键姿势。按实际5列4行输出逐格测量。

```text
Production 20-frame transparent sprite atlas of a quiet, fully clothed anime companion. Reference 1 is exact character identity, clothes and consistent 4-head proportion. Reference 2 is the final resting pose: head upper-right, feet lower-left, back towards camera, arms spread comfortably sideways. Adapt to reference 1's small body proportions. Exactly 5 columns and 4 rows, large transparent separation, consistent scale, complete bodies. Read left to right top to bottom: frames 0 standing facing RIGHT, 1 knees soften, 2 knees bend, 3 crouch, 4 hands on ground, 5 supported kneel, 6 arms bend, 7 chest lowers, 8 legs extend behind and arms spread, 9 final peaceful nature rest exactly like reference2. Then 10 hands move inward, 11 shoulders raise, 12 push up, 13 one knee under body, 14 both knees under, 15 upright kneel, 16 plant foot, 17 transfer weight, 18 rise, 19 stand facing RIGHT. Actual anatomically connected poses, natural long hair and ordinary clothes throughout. All poses use same slightly elevated view. Calm everyday activity, no exaggeration. No ground, scenery, props, lines or text. True fully transparent background, no haze or halo, transparent outer margins.
```

## 验证边界

生产WPF绘图预览与EXE实际窗口透明导出分别记录；它们不读取私人桌面，也不表示Windows原生键鼠/显示缩放或一小时稳定性通过。详见[验证记录](验证记录.md)。本轮完成本地交付后，用户授权发布GitHub，保持当前公开状态并保留旧版本。

最终素材SHA256：

- `assets/world/nurture-grass.png`：`795DB0986B771EF694976C7408CE6A248B4BB99809EB3C823E37CB5C0B9D7CA8`
- `assets/rin/nurture-life.png`：`13FF9B1BFF16BA84FD5A6DE9915121F12F9E9455FBDE82109000DA3993B5600E`

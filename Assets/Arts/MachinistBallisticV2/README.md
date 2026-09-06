# 机械师弹着特效 V3：角色比例与像素颗粒优化

本版图像使用内置 image_gen 从文字独立生成，没有输入或复用项目中的既有图像。风格参考《闪避刺客：离奇的一天》中霰弹枪的冲击感，素材为原创，并非提取游戏资源。

参考：[官方 Steam 页面](https://store.steampowered.com/app/3996620/SANABI_A_Haunted_Day/?l=schinese)。

## 效果

- 普通弹：紧凑白热闪点、向入射方向反向飞散的火星与少量碎屑，约 0.21 秒。
- 重击弹：放大喷散范围，约 0.25 秒；没有圆形爆炸叠层。
- 电击：同一套新弹着序列，火星偏冷色，烟尘保持灰色。
- 击盾和撞墙：缩小的金属弹着效果。
- 最后两帧逐渐淡出；旧近战动画仍由自己的配置控制。

## Unity 设置

配置资源：Assets/Resources/MachinistImpactLibrary.asset。

Ballistic Size 控制新版总体大小；Ballistic Frames Per Second 控制节奏；Ballistic Pixels Per Unit 控制素材与世界单位的比例。默认值分别为 1、28、256。Ballistic World Pixel Size 默认为 0.0625（1/16 世界单位），与机械师人物的像素尺寸一致；不同命中类型放大后仍使用相同的世界像素颗粒。重击播放速度为普通弹的 85%，因此持续时间略长。Size 仍为所有机械师命中特效的共同倍率。

原始 PNG 为 1536×1024、3 列×2 行，共六帧，每帧 512×512。按命中中心配置了独立锚点，Point 采样，无压缩、无 mipmaps。运行时专用着色器用固定像素采样与透明度裁切收紧边缘，消除原始图像的柔光边缘；实际效果请以 Unity 渲染为准。请勿将此图加入旋转或重排的 SpriteAtlas。

保留 48 个效果的对象池上限、场景切换清理、原有命中去重。此版本只修改视觉配置与播放，不改变伤害、击退或攻击判定。

## 最终生成提示词

模式：内置 image_gen，文字生成，无图像输入。

Generate a transparent-background PNG game sprite sheet of a realistic PIXEL ART shotgun pellet hit on metal. Real transparency, isolated sprites. 1536x1024. Six successive frames in a 3x2 grid, each cell 512x512. Clean pixel sprites composed of flat opaque square pixels only, sharp edges, NO glow. A compact short white-yellow contact flash followed by sparks flying mostly left and dark gray dust. Cell1 small bright compact impact. Cell2 spray of thin yellow-white streaks and tiny steel fragments. Cell3 orange sparks and gray dust, white core gone. Cell4 shorter orange flecks and gray dust. Cell5 sparse gray smoke fragments. Cell6 very sparse gray flecks. Keep all contact origins aligned at cell-local (320,256); adequate transparent padding. Restrained realistic gritty action-game art. Use a consistent coarse pixel grid like 96x96 sprites enlarged with nearest neighbor. Limited warm spark and gray dust palette. Nothing else. Transparent background. No floor, scenery, target, weapon, bullets, lettering, numbers, border, checker pattern, luminous halos, gradients, starbursts or circular explosions.

## V3 比例校准

对照 PlayerM_Idle 的 41 像素高、16 PPU，以及普通子弹的 16 PPU、预制体缩放 (0.3, 0.4)。弹着整体尺寸恢复为 V2 的大小，实际世界像素网格仍为 1/16 单位，保留降低后的像素精度。降低颜色细节，使用覆盖采样保留降精度后的细火星。像素网格锚定接触点，减少动画换帧时颗粒抖动。

本轮只调整运行时显示与配置，保留上一版生成的原始图像、命中判定与播放时长。

## 重击与电磁配色

在 Assets/Resources/MachinistImpactLibrary.asset 的 Inspector 中，Heavy Color 控制重击弹火星，默认亮蓝色（0.08, 0.4, 1）；Energy 控制电磁弹火星，默认青蓝色（0.15, 0.7, 1）。烟尘仍保持中性灰色。

电磁弹使用独立的 Electric 动画序列与 Energy 配色，重击弹使用 Heavy 星形序列与 Heavy Color 配色。原先撞墙等接触类型会覆盖弹种并丢失颜色；现已将弹种信息传入显示逻辑，保留墙面/盾牌的紧凑尺寸，同时保持重击和电磁弹各自的配色。

## 当前动画选择（Heavy / Electric 修正）

普通 Bullet、墙面 Surface、格挡 Shield 使用 Ballistic。Heavy 同时播放 Heavy 星形、Ring 附加层及 Ballistic 火星烟尘；Electric 使用独立 Electric 序列。Heavy 主动画不会被 Ballistic 替换。Slash 和 BlastSlash 使用 Slash 序列，不叠加星形。

Heavy 和 Electric 的播放速度使用 Frames Per Second，大小使用 Size 以及各类型既有倍率；Ballistic Size 与 Ballistic World Pixel Size 只作用于 Ballistic 路径。墙面与格挡仍保留来源弹种的颜色。上方历史 V2/V3 制作记录中的共用动画说明已由此规则替代。

## Heavy 叠加层

Heavy 的 Ballistic 位于星形后方，沿用 Heavy Color；大小和颗粒分别受 Ballistic Size、Ballistic World Pixel Size 控制，速度为 Ballistic Frames Per Second 的 85%。星形与 Ring 保持原帧率；最后两帧 Ballistic 烟尘独立淡出。三层共享同一个池对象和命中位置，播放完最长一层后回收。

## Heavy 星形与 Ring 像素匹配

Heavy 星形、Ring 和 Ballistic 叠层统一使用 Ballistic World Pixel Size 控制可见像素颗粒，当前默认为 0.0625 世界单位。按各层缩放分别计算采样间隔，网格锚定命中点；不改变现有整体大小、配色或动画播放速度。此规则替代历史记录中该参数仅作用于 Ballistic 的说明。

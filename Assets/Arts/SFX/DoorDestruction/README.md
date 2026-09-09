# 门体破坏爆炸

当前版本为内置 image_gen 新绘制的原创街机风爆炸，取代上一版复制的旧素材。仅将用户提供的 `explosion-1-g/spritesheet.png` 用作风格参考，没有使用或复制其中的动画帧。强调合金弹头一类街机效果的白黄闪爆、翻卷橙红火舌、破碎火团和暗红余烬，不再使用大块灰烟。

`DoorExplosion_01` 到 `DoorExplosion_16` 为 16 张 96×96 透明 PNG，共用固定画布和中心轴心，PPU 32、Point、无压缩、无 Mipmap。`DoorExplosion_Arcade_Sheet.png` 为与交付帧逐像素一致的 384×384 总图。生成提示词见 `ArcadeExplosionPrompt.txt`，最新白边修复记录见 `EdgeCleanupPrompt.txt`。

白边修复版：由图像工具清除原去底留下的白灰描边，修复高光内部的破洞，使用纯黑临时底色重新提取透明度。拆帧后仅对外轮廓的低饱和浅色残像素做清理；保留内部白黄高光，透明像素 RGB 清零，Alpha 仅为 0 或 255。全部 16 帧已通过外缘检查和深浅背景对照，GUID、导入参数、24fps 播放及场景引用保持不变。

`DoorDestructionEffect.asset`：24 帧/秒，每个爆点持续约 0.667 秒；沿门的较长方向按约 1.75 世界单位排列，最多八个，相邻启动间隔 0.045 秒，总时长不超过 0.982 秒。使用 Bullet 层显示在门体前方。

接入范围：Stage1 的三核心门 `Door`、Stage2 的三核心门 `Door (1)`、Stage2 可直接受击的 `BreakableDoor`。核心门仅框定实体门板，不把远处的控制节点计入爆炸范围。现有充能门、升降门、普通地面未配置此效果。

`BreakableProp` 在最后一次有效命中时触发；`AnimatedDestroy` 在破坏开始时触发，均沿用原有去重标志。爆炸先于已有事件和碰撞关闭创建，不更改命中次数、核心计数、开门时序或战斗事件。

特效只有 SpriteRenderer，没有攻击和碰撞组件。它与门处于同一场景但不挂在门下，门销毁或隐藏后仍能播完；最后一帧结束后自动销毁，切换场景也会一并清理。其他门可通过破坏组件中的 `Destruction Effect` 字段选择同一资源，`Destruction Effect Root` 指向门板范围。

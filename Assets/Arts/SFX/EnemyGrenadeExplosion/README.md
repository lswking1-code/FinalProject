# 敌人手雷爆炸 · 原创街机风

参考当前 `enemy_grenade_explosion.anim` 使用的六帧圆形爆炸重新生成，未复制旧图像像素。保持合金弹头一类街机风格的白黄高热火团、翻卷橙红火舌和破碎余烬。

- `EnemyGrenadeExplosion_01`～`06`：96×96 透明 PNG，固定中心轴心；PPU48，画布为2×2世界单位，接近原31px/16PPU的尺寸。
- `EnemyGrenadeExplosion_Sheet.png`：288×192透明总图，3列×2行，与游戏使用帧逐像素一致。
- Point、无压缩、无Mipmap，透明像素RGBA清零；浅色外缘检查六帧均为零，中心白黄高光保留。

已替换 `Animations/Enemy/enemy_grenade_explosion.anim` 的Sprite曲线及 `Prefabs/Enemy/EnemyGrenadeExplosion.prefab` 的初始Sprite。仍为12fps、6帧、总长0.5秒。Attack子节点在0秒开启、0.16666667秒关闭的两条曲线完全保留；伤害30、碰撞半径0.98、音效、自毁逻辑和控制器引用均未修改。旧共享图集保留原样。

使用内置image_gen生成，提示词和处理记录见 `GenerationPrompt.txt`。

2026-09-09 浅色修订：提亮暗红阴影与余烬，橙红色调整为较浅的金橙色。以生成的浅色版为配色参考，通过所有帧共用的单调颜色映射应用至原像素，保留内部火焰纹理和完全相同的透明轮廓。六帧尺寸、导入设置、动画、预制体及伤害判定均保持不变。最终逐帧检查未发现白灰透明外缘。提示词见 `LightPalettePrompt.txt`。

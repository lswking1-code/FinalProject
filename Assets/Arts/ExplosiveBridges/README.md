# 爆炸桥美术

Codex 内置 imagegen 生成的原创桥体与爆炸素材，使用 Unity 切片与像素材质接入。

- BridgeDecks.png：沙漠锈蚀版 DesertDeck、蓝灰工业版 IndustrialDeck。
- BridgeExplosion.png：8 帧闪光、火焰、碎片、烟尘，14 帧/秒。
- ExplosiveBridge.controller：Idle 保持桥面，Destroy 隐藏桥面并播放爆炸。
- BridgePixel.shader：保持硬像素颗粒，爆炸材质将图集黑色背景作为透明区域。
- FallingPlatform.prefab：BridgeArt/Deck 显示桥体，BridgeArt/Blast 显示爆炸；原来的两块占位图已停用。

Stage1 使用沙漠版，Stage2 使用工业版。桥面顶边与原碰撞顶边对齐；保留原倒计时、踩踏法线判定、单向平台配置以及碰撞禁用时机。爆炸属于视觉效果，不新增伤害。

桥面与爆炸均使用 Point 过滤、无 mipmap、无压缩。源 PNG 是带黑底的图集；爆炸在 Unity 中需配合 BridgeExplosion.mat 使用。

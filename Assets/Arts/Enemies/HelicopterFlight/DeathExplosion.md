# 直升机死亡爆炸

参考装甲车 tank_die.png 的金黄起爆、膨胀火团、灰烟消散表现，使用内置 image_gen 生成独立爆炸层，再与原直升机机身及机身碎片分层合成。不是直接复制装甲车外形。

资源：Helicopter_DeathExplosion.png，1024×1024，4×4排列，每帧256×256，共16帧。32 PPU、中心轴心、Point、无压缩、无Mipmap。最终一帧全透明。生成过程及提示词见 DeathExplosionPrompt.txt。

已更新实际游戏 Animations/Enemy/Helicopter/Helicopter_die.anim，保留其GUID及控制器Die引用。12fps，约1.33秒，不循环，绑定Sprite子对象；DestroyAfterAnimation位于动画末尾。直升机使用与装甲车相同的爆炸死亡音效。召唤取消和原有空中死亡下落逻辑保留。

验证：16个切片及Sprite引用有效，销毁事件时间正确，透明像素RGB清零，浅白灰外缘检查为零，运行脚本编译通过。未执行Unity场景实测。

白边修订：从生成层清除受棋盘底污染的浅色外缘和游离中性碎点，保留内部火焰高光及主要烟团；并非仅将白边染黄。深色背景逐帧目视检查完成。切片、动画和控制器内容保持不变。

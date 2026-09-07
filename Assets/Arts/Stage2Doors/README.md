# Stage2 工业像素门

原创素材使用 Codex 内置 imagegen 生成，参考 Stage2 实际场景截图的蓝灰工业墙体与黄黑警示边。没有使用网上第三方资源。

- Stage2IndustrialDoors.png：原始素材图集；Unity 切片为 LiftPanel、BreakablePanel、ControlStrip 和四块 BreakPiece。
- Stage2BreakableDoor.controller：Idle / Destroy，供 Stage2 可破坏门使用。
- Stage2DoorDestroy.anim：钢板分块散开并淡出，动画仅影响 DoorPanel 子节点。

Stage2 场景内覆盖 3 扇 OverheadDoor、1 扇 BreakableDoor，以及 4 个升降门受击控制条。素材通过新增视觉子节点安装，原门体、碰撞体、运动参数和受击阈值保持原样。共享的 Ground 与 OverheadDoor 预制体不改动。

图集使用 Point 过滤、无 mipmap、无压缩。黑色背景位于切片外，切片边缘的黑色属于门板暗色轮廓。

## 最终生成提示词

Generate a pixel art sprite sheet for the industrial side-scrolling platformer shown in reference image 1. Reference 1 is the ACTUAL GAME STYLE and palette; reference 2 is an earlier SHAPE prototype only. New sheet must match reference 1's coarse chunky pixel art, blue-gray steel, restrained yellow-black hazard stripes. Exactly three upright solid rectangular narrow panels side by side on plain black background with generous gaps. Each panel completely fills its straight-edged rectangle, no holes, no protrusions, no checkerboard, no text. LEFT: lifting shutter door, long slender rectangle width:height 1:8, blue-gray segmented steel shutter, dark outlines, worn metallic edge highlights, yellow/black hazard caps top and bottom. MIDDLE: destructible steel barricade door, width:height 1:7, same blue-gray palette with rusty brown patches, diagonal metal reinforcement, cracks and dents conveying breakability, a small orange warning marking, NOT wooden. RIGHT: hit-responsive control strip, width:height 1:7, sturdy steel casing with gold-brass inset segments and small amber luminous indicator blocks; no bloom. All three equal height, complete objects. Hard square pixels, deliberately low resolution approx 16-24 logical pixels across each panel and 128-168 high, enlarged nearest neighbor; no tiny noise, no smooth gradient, no antialiasing. View front orthographic, no floor or ground shadow, no perspective. 1024x1536 canvas. Allow rectangular cropping tightly around panels for Unity sprites. No background checkerboard.

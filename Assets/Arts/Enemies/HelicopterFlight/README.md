# 参考形象直升机飞行 / 开舱动画

用户授权使用原图分层制作像素动画。来源为 E:/Final/helicopter.png（关门）及 helicopter-export.png（开门）。未使用图像生成工具。保持关门原图机身，开门参考只用于侧门区域，避免两张源图的色差造成机身闪烁。

- Helicopter_Fly：关门持续飞行循环，16帧，8fps，2秒。
- Helicopter_OpenDoor：飞行中侧门向左滑开，16帧，8fps，2秒，不循环。
- Helicopter_DoorOpenFly：开门后的持续飞行循环，16帧，8fps，2秒，避免开门完成后旋翼静止。

每帧256×256；图集1024×1024，4列×4行，按从左到右、从上到下读取。已完成Unity切片，中心轴心，32 PPU，Point采样，无压缩、无Mipmap。双旋翼采用加快的交替相位与像素扫掠线持续转动，机身浮动幅度±2像素；浮动已包含在帧图内。

动画与独立控制器位于 Assets/Animations/Enemy/Helicopter/ReferenceFlight。默认Fly；触发OpenDoor后播放开舱，完成后自动进入DoorOpenFly循环。动画绑定同一对象上的SpriteRenderer。此控制器用于独立演示或后续接入，未替换项目现有直升机预制体和战斗控制器。

预览：Helicopter_Fly_Preview.gif；Helicopter_OpenDoor_Preview.gif（依次演示关门飞行、开门、保持开门飞行）。GIF展示放大4倍，PNG保留原始像素尺寸。

验证：48帧的尺寸、透明像素、切片与引用检查通过；旋翼和门区域以外的机身像素保持原样；开门首帧与关门飞行首帧一致，开门后段与开门飞行对应相位一致。未运行Unity场景测试。

2026-09-10修订：每段32帧减为16帧，保持2秒时长。修复开舱底图预先包含滑开门板、又叠加移动门板造成的双门；现在底图仅提供舱口，单独一扇门板移动。

旋翼提速修订：每帧角度步进从90度增至180度，并延长暖暗色扫掠线增强高速旋转观感。每段仍为16帧、8fps、2秒；机身浮动、舱门动作、切片和动画控制器均不变。

右侧旋翼修订：清理桨轴区域残留的静态桨叶，固定右桨轴投影位置，改用16个连续相位，完整循环可衔接。左侧旋翼、机身、舱门、动画时长与导入配置保持不变。

召唤流程接入：新增Helicopter_CloseDoor图集（16帧、8fps、2秒）。实际游戏控制器现已接入开舱→开门悬停刷兵→关舱流程；详见 Assets/Animations/Enemy/Helicopter/SummonAnimations.md。Helicopter_SummonCycle_Preview.gif演示完整开合过程，预览中的悬停长度固定，游戏内由本批召唤耗时决定。

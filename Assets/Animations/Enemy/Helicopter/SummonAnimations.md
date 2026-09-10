# 直升机召唤动画流程

实际游戏控制器使用 Assets/Animation/enemy_helicopter.controller，已加入 OpenDoor、HoverOpen、CloseDoor 状态。正常 Idle / Fly 使用关门飞行帧图，召唤动画绑定直升机子对象 Sprite。

1. 进入召唤状态，停止移动和单位分离推挤，播放 OpenDoor。生成器可准备本轮会话，但生成门控保持关闭。
2. OpenDoor 实际播放完成后，进入 HoverOpen 循环；确认悬停动画已生效后允许 Instantiate。生成间隔、编制和数量沿用原有配置。
3. 本批生成完毕后关闭生成门控，播放 CloseDoor。
4. CloseDoor 实际播放完成后，进入原有 Reload 状态重新判断；配置刷完离场的有限召唤则进入 Depart。

无限刷新保留原有等本批清光和刷新间隔，下一批准备就绪时重新请求完整开门流程，不会在关舱或移动时直接补兵。普通遭遇生成器不使用此门控。受击仍有闪烁/硬直，召唤期间不允许 Hit 动画覆盖舱门；死亡或外部退出会取消当前生成会话。

动画均为16帧、8fps；开门和关门各2秒且不循环，HoverOpen保持门打开循环飞行。CloseDoor仅反向移动门板，旋翼相位仍向前推进。四张帧图（含原有Fly）位于 Assets/Arts/Enemies/HelicopterFlight。

检查：运行时与编辑器脚本编译通过；实际HelicopterSpawnState源码配合隔离依赖的流程测试通过正常结束、离场、空配置、中断、死亡案例；切片、Sprite绑定、动画循环设置及控制器引用通过静态检查。未执行Unity场景实机游玩测试。

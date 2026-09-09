# 银色空中敌人动画

控制器：Assets/Animation/enemy_air_silver.controller。

四张银色图集逐项沿用原版导入参数、裁剪范围和轴心，PPU为16。待机、受击、死亡各16片；爆炸9片，保留原版特殊大帧裁剪。PNG像素未修改，原版资源未修改。

独立银色动画：FlyEnemy_idle、FlyEnemy_fly、FlyEnemy_hit、FlyEnemy_die、FlyEnemy_warning、FlyEnemy_Bomb，文件名以_silver结尾。帧时间、事件及其他曲线保持原版设置。

没有银色资源的FlyEnemy_shoot、FlyEnemy_downshoot及mobs直接引用原版动画。新控制器的状态名、参数、默认状态和过渡条件保持原版，现有场景与预制体未替换。

验证：切片设置与原版一致；所有新Sprite引用及控制器动作引用能解析到现有资源；控制器还原动画GUID及资源名后与原版一致。未运行Unity场景测试。

# 收集类点位场景归属确认记录（2026-08-30 实测扫描）
# 扫描基准：E:\fsm_full\scenes_scenes_scenes（590 场景目录，对象文件为 UTF8 序列化 txt）
# 用途：给 162 个被 ResolveRegion 丢弃的键补充"键→场景"归属
# 键=对象 SavedItem.name / PBI id 的精确特征（HotkeyHandler F4 实现同源）

## 一、heart 面具碎片（Heart Piece）：14 个场景点（每场景精确 1 个对象，对象名 "Heart Piece"）
# 全量 20 = 14 场景 + 2 商店 + 4 任务（商店/任务不可按场景归属）
bone_east_20
bone_east_lavachallenge
coral_19b
crawl_02
dock_08
library_05
peak_04c
peak_06
shadow_13
shellwood_14
slab_17
song_09
weave_05b
wisp_07

## 二、spool 丝轴碎片（Silk Spool）：13 个场景点（每场景精确 1 个对象，对象名 "Silk Spool"）
# 全量 18 = 13 场景 + 3 商店 + 2 任务（商店/任务不可按场景归属）
arborium_09
bone_11b
bone_east_13
cog_07
dock_03c
greymoor_02
hang_03_top
library_11b
peak_01
song_19_entrance
under_10
ward_01
weave_11

## 三、moss 苔莓（moss_berry_fruit 枝头实例）：4 个场景点（每场景本体+_#副本各 1，共 2 文件，按 1 点计）
# 全量 6 = 4 场景 + 2 在生物身上（生物带苔莓，文件名不含特征，扫描不到）
arborium_04
tut_01b
tut_02
weave_03

## 四、flower 花芯（shell_flower_purple）：6 个场景点，已全量确认（每场景多文件=同花芯的渲染体）
# shellwood_01/02/10/15/26 各 5 文件（_0015×1 + _0016×2 + _0018×2）
# shellwood_20 10 文件（_0015×1 + _0016×5 + _0018×4）
shellwood_01
shellwood_02
shellwood_10
shellwood_15
shellwood_20
shellwood_26

## 汇总：可归属 37 点 = heart 14 + spool 13 + moss 4 + flower 6
## 不可归属 13 点 = heart 6（商店2+任务4）+ spool 5（商店3+任务2）+ moss 2（生物）
## ============ 已落盘为机器可读表 Resources\collectible_scene_map.txt ============

## 附：分工状态
# - shell_flower_purple 不同文件名（_0015/_0016/_0018）是同一花芯的动画/状态渲染体，每场景只算 1 点
# - 苔莓 _#副本 与本体同点，moss 每场景 1 点
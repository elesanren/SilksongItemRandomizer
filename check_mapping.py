# -*- coding: utf-8 -*-
import json, sys, re
from collections import Counter

path = r"C:\Users\28267\AppData\Roaming\r2modmanPlus-local\HollowKnightSilksong\profiles\Default\BepInEx\config\SilksongItemRandomizer\global_save.json"
data = json.load(open(path, encoding="utf-8"))
m = data["PreGeneratedMappings"]
stamp = data.get("MappingsConfigStamp", "")

print(f"映射总数: {len(m)}")
print(f"stamp: {stamp}")
print()

# ===== 键类型分布 =====
by_key = Counter()
for k in m:
    if re.match(r'^[a-z]+:', k):
        p = k.split(':')[0]
    else:
        p = "pickup坐标键"
    by_key[p] += 1
print("===== 键类型分布 =====")
for k, v in by_key.most_common():
    print(f"  {k:<12} {v}")
print()

# ===== 奖励值分布 =====
vals = Counter()
for v in m.values():
    id_ = v[7:] if v.startswith("reward:") else v
    vals[id_] += 1

def categorize(id_):
    if id_.startswith("virt:Permit:"): return "权限物virt:Permit"
    if id_.startswith("virt:Map_"): return "地图virt:Map"
    if id_.startswith("virt:Station_"): return "车站virt:Station"
    if id_.startswith("virt:"): return "其他virt"
    if id_.startswith("perm:"): return "方向权限perm"
    if id_.startswith("has") or id_.startswith("Has"): return "能力has*"
    return "真实物品"

# 每个类别统计出现次数的分布
by_cat = {}
for id_, n in vals.items():
    cat = categorize(id_)
    by_cat.setdefault(cat, []).append((id_, n))

# 期望次数
expect = {
    "能力has*": 2,          # GetAbilityLimit 默认 2
    "方向权限perm": None,    # 移动方向1 / 攻击回血2
    "真实物品": 1,           # 全覆盖每物品一次
    "地图virt:Map": 1,
    "车站virt:Station": 1,
    "权限物virt:Permit": 1,
    "其他virt": None,
}

print("===== 各类别出现次数分布 =====")
violations = []
for cat, items in by_cat.items():
    ncount = Counter(n for _, n in items)
    print(f"--- {cat} ({len(items)} 种) ---")
    for n in sorted(ncount):
        print(f"    出现{n}次: {ncount[n]} 种")
    if cat == "能力has*":
        for id_, n in items:
            if n != 2: violations.append(f"能力 {id_} 出现 {n} 次（期望 2）")
    elif cat == "真实物品":
        for id_, n in items:
            if n != 1: violations.append(f"真实物品 {id_} 出现 {n} 次（期望 1）")
    elif cat in ("地图virt:Map", "车站virt:Station", "权限物virt:Permit"):
        for id_, n in items:
            if n != 1: violations.append(f"{cat} {id_} 出现 {n} 次（期望 1）")
    elif cat == "方向权限perm":
        for id_, n in items:
            # 移动方向(hasDash_L等)=1，攻击/回血(upward/left/right/heal)=2
            if re.search(r'_(L|R)$', id_):
                if n != 1: violations.append(f"移动方向 {id_} 出现 {n} 次（期望 1）")
            else:
                if n != 2: violations.append(f"攻击/回血 {id_} 出现 {n} 次（期望 2）")

print()
print("===== 违规检查 =====")
if violations:
    for v in violations: print("  ✗ " + v)
else:
    print("  无违规 ✓")
print()

# ===== 珍贵虚拟数量 =====
print("===== 珍贵虚拟奖励实际数量 =====")
for key in ["virt:HeartPiece", "virt:SpoolPart", "virt:MaxSilkRegenUp", "virt:UnlockCrestSlot"]:
    print(f"  {key}: {vals.get(key, 0)} 次（配置 20/18/2/2）")
print()

# ===== 能力清单与出现次数 =====
print("===== 能力（has*）出现次数明细 =====")
for id_, n in sorted(vals.items()):
    if id_.startswith("has") or id_.startswith("Has"):
        print(f"  {id_:<30} {n}")

# ===== 方向权限明细 =====
print("===== 方向权限（perm:）出现次数明细 =====")
for id_, n in sorted(vals.items()):
    if id_.startswith("perm:"):
        print(f"  {id_:<25} {n}")

# ===== 无限池填充统计 =====
print("===== 无限池兜底填充 =====")
for key in ["virt:RandomCoin", "virt:Silk_3", "virt:Shards_300", "virt:BlueHealth_Random", "virt:FallbackCoin"]:
    print(f"  {key}: {vals.get(key, 0)}")

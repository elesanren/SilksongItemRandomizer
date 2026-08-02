#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""合并 check_points_scan.txt 与额外货币点(Extracurrencypickup.cs), 生成全量点表 + 随机映射表。
用法: python merge_points_map.py [扫描文件] [--seed N]"""

import argparse, os, random, sys

# (scene, x, y) 与 Extracurrencypickup.cs pickupTable 一致, item=Simple Key, z=0
EXTRA_POINTS = [
    ("Tut_01", 26.0, 77.0), ("Bonetown", 224.0, 80.0), ("Bonetown", 151.0, 69.0),
    ("Bone_01c", 176.0, 58.0), ("Bone_01c", 139.0, 7.5), ("Bone_01", 118.0, 7.5),
    ("Bone_04", 204.0, 5.5), ("Mosstown_01", 48.0, 23.5), ("Mosstown_02", 154.0, 55.5),
    ("Bone_14", 129.0, 8.5), ("Bone_19", 28.0, 13.5),
    ("Belltown_basement_03", 65.5, 5.5), ("Belltown_basement_03", 71.0, 116.5),
    ("Bone_08", 30.5, 47.5), ("Bone_09", 6.5, 36.5), ("Bone_East_03", 153.0, 22.5),
    ("Ant_04_left", 7.0, 29.0), ("Ant_21", 48.5, 74.0), ("Dock_06_Church", 11.0, 21.5),
    ("Bone_10", 8.0, 43.5), ("Bone_10", 112.5, 66.5), ("Bone_10", 30.0, 45.5),
    ("Bone_11", 8.0, 9.5), ("Aspid_01", 10.5, 7.5), ("Bonegrave", 270.5, 72.5),
    ("Chapel_Wanderer", 79.5, 106.4), ("Shellwood_26", 98.5, 75.5),
    ("Belltown_04", 77.0, 48.5), ("Belltown_04", 63.0, 19.5),
    ("Bone_East_17", 7.0, 84.5), ("Bone_East_17", 42.0, 96.5), ("Bone_East_17b", 12.0, 31.5),
    ("Bone_East_16", 11.0, 16.5), ("Bone_East_08", 59.0, 21.5), ("Bone_East_14", 88.0, 40.5),
    ("Bone_East_14b", 250.0, 52.5), ("Bone_East_07", 10.0, 165.5), ("Bone_East_09b", 61.0, 141.5),
    ("Greymoor_15", 57.3, 74.5), ("Greymoor_15b", 205.0, 61.5), ("Greymoor_15b", 198.0, 41.5),
    ("Greymoor_22", 88.0, 25.5), ("Greymoor_02", 52.0, 90.5), ("Greymoor_01", 8.61, 17.5),
    ("Greymoor_04", 32.0, 36.5), ("Greymoor_05", 95.0, 62.5), ("Greymoor_05", 7.0, 19.0),
    ("Greymoor_06", 6.0, 79.5), ("Greymoor_07", 28.0, 10.5), ("Greymoor_08", 140.0, 30.5),
    ("Shellwood_11", 59.0, 21.5), ("Coral_12", 87.0, 34.5),
    ("Song_01", 19.5, 80.5), ("Song_01", 109.0, 129.5), ("Song_11", 44.0, 44.5),
    ("Song_03", 130.0, 4.5), ("Song_15", 6.5, 6.5), ("Song_17", 34.5, 100.5),
    ("Hang_08", 19.5, 195.0), ("Library_04", 51.0, 77.0), ("Library_06", 36.5, 63.0),
    ("Library_07", 39.0, 139.5), ("Dock_02", 103.0, 51.5), ("Bone_East_24", 246.0, 65.5),
    ("Bone_East_18", 139.0, 36.5), ("Bone_East_18b", 133.0, 6.5), ("Bone_01b", 7.0, 85.5),
    ("Bone_East_15", 47.0, 8.5), ("Song_09", 11.0, 56.5),
]


def parse_scan(path):
    points = []
    with open(path, "r", encoding="utf-8-sig") as f:
        for line in f:
            line = line.rstrip("\r\n")
            if not line or line.startswith("#"):
                continue
            p = line.split("|")
            if len(p) < 8:
                continue
            points.append({"type": p[0], "scene": p[1], "path": p[2], "name": p[3],
                           "detail": p[4], "key": p[5], "x": float(p[6]), "y": float(p[7]),
                           "origin": "scan"})
    return points


def build_extra():
    pts = []
    for scene, x, y in EXTRA_POINTS:
        pts.append({"type": "Pickup", "scene": scene, "path": "<extra>",
                    "name": "Collectable Item Pickup (extra)", "detail": "Simple Key",
                    "key": f"{scene}_{x:.2f}_{y:.2f}_0.00",
                    "x": x, "y": y, "origin": "extra"})
    return pts


def near(p, q, tol=2.0):
    return p["scene"] == q["scene"] and abs(p["x"] - q["x"]) <= tol and abs(p["y"] - q["y"]) <= tol


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("scan", nargs="?", default=r"C:\Users\28267\AppData\Roaming\r2modmanPlus-local\HollowKnightSilksong\profiles\Default\BepInEx\plugins\check_points_scan.txt")
    ap.add_argument("--seed", type=int, default=20260731)
    args = ap.parse_args()
    if not os.path.exists(args.scan):
        print(f"[!] 扫描文件不存在: {args.scan}", file=sys.stderr); sys.exit(1)

    script_dir = os.path.dirname(os.path.abspath(__file__))
    scan = parse_scan(args.scan)
    extra = build_extra()
    pickups = [p for p in scan if p["type"] == "Pickup"]

    # 全量点表
    merged = scan + extra
    merged.sort(key=lambda p: (p["scene"], p["key"], p["origin"]))
    with open(os.path.join(script_dir, "all_points_merged.txt"), "w", encoding="utf-8") as f:
        f.write("#type|scene|path|name|detail|key|x|y|origin\n")
        for p in merged:
            f.write(f"{p['type']}|{p['scene']}|{p['path']}|{p['name']}|{p['detail']}|{p['key']}|{p['x']:.1f}|{p['y']:.1f}|{p['origin']}\n")

    # 冲突检查(额外点 vs 扫描 Pickup, 容差 2.0)
    conflicts = []
    for e in extra:
        for s in pickups:
            if near(e, s):
                conflicts.append((e, s))
                break

    # 物品池 = 扫描 Pickup 原物品去重 + Simple Key
    pool = sorted({p["detail"] for p in pickups if p["detail"]} | {"Simple Key"})
    rng = random.Random(args.seed)
    shuffled = pool[:]
    rng.shuffle(shuffled)

    mappts = pickups + extra
    mappts.sort(key=lambda p: (p["scene"], p["key"], p["origin"]))
    with open(os.path.join(script_dir, "random_mapping.txt"), "w", encoding="utf-8") as f:
        f.write(f"#seed={args.seed} 物品池={len(pool)} 映射点={len(mappts)}\n")
        f.write("#key|item|scene|原物品|origin|x|y\n")
        for i, p in enumerate(mappts):
            f.write(f"{p['key']}|{shuffled[i % len(shuffled)]}|{p['scene']}|{p['detail']}|{p['origin']}|{p['x']:.1f}|{p['y']:.1f}\n")

    print("=" * 60)
    print(f"扫描: Pickup={len(pickups)} 额外货币点={len(extra)} 参与映射={len(mappts)}")
    print(f"物品池: {len(pool)} 种 | 种子: {args.seed}")
    if conflicts:
        print(f"[!] 坐标冲突 {len(conflicts)} 处:")
        for e, s in conflicts:
            print(f"    extra {e['scene']}({e['x']:.1f},{e['y']:.1f}) ~= scan {s['scene']}({s['x']:.1f},{s['y']:.1f}) key={s['key']}")
    else:
        print("[+] 额外货币点与扫描 Pickup 无坐标冲突")
    print(f"输出: all_points_merged.txt / random_mapping.txt")
    print("=" * 60)


if __name__ == "__main__":
    main()

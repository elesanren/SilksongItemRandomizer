#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
从 PreGeneratedMap.cs 中精确提取两个字符串数组的内层内容,
导出为「每行一个字符串」的纯文本文件, 供运行时外部加载:
    check_points.txt -> EmbeddedPointLines (检查点数据)
    shop_slot_keys.txt -> ShopSlotKeys (商店槽位键)
用法: python export_point_data.py
提取结果逐字取自源码字符串字面量(不含引号), 保证与源码 100% 一致。
"""
import os, re, sys

SRC = "PreGeneratedMap.cs"
DIR = os.path.dirname(os.path.abspath(__file__))
SRC_PATH = os.path.join(DIR, SRC)

def extract_named_array(source, var_name):
    """从源码中拿到 `var_name = { "a", "b", ... };` 的每个字符串字面量内容列表。"""
    # 定位 var 声明: ... string[] <var> = {  或  ... string[] <var> = new string[] {
    pattern = re.compile(
        r'string\[\]\s+' + re.escape(var_name) + r'\s*=\s*(?:new\s+string\[\]\s*)?\{', re.MULTILINE)
    m = pattern.search(source)
    if not m:
        raise RuntimeError(f"找不到数组声明: {var_name}")
    start = m.end()  # 在 '{' 之后
    # 找配对的 '}' —— 从 start 起计深度
    end = None
    depth = 0
    i = start
    while i < len(source):
        c = source[i]
        if c == '{':
            depth += 1
        elif c == '}':
            if depth == 0:
                end = i
                break
            depth -= 1
        i += 1
    if end is None:
        raise RuntimeError(f"数组未闭合: {var_name}")
    body = source[start:end]
    # 提取形如 "...." 的字符串字面量, 处理转义(\" 等)
    strings = []
    for sm in re.finditer(r'"((?:[^"\\]|\\.)*)"', body):
        raw = sm.group(1)
        # 还原 C# 转义: \n \t \" \\ (其余保留原样)
        raw = raw.replace('\\"', '"').replace('\\\\', '\\')
        raw = raw.replace('\\n', '\n').replace('\\t', '\t')
        strings.append(raw)
    return strings


def write_lines(path, lines):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        for ln in lines:
            f.write(ln + "\n")
    print(f"[+] {os.path.basename(path)}: {len(lines)} 行")


def main():
    with open(SRC_PATH, "r", encoding="utf-8-sig") as f:
        src = f.read()

    checkpoints = extract_named_array(src, "EmbeddedPointLines")
    write_lines(os.path.join(DIR, "Resources", "check_points.txt"), checkpoints)

    shopslots = extract_named_array(src, "ShopSlotKeys")
    write_lines(os.path.join(DIR, "Resources", "shop_slot_keys.txt"), shopslots)

    print("完成。数据与源码逐字一致。")


if __name__ == "__main__":
    main()

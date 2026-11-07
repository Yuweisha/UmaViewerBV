# -*- coding: utf-8 -*-
"""
删除眼球偏移表情中 (0,0,0) 零位移节点的脚本（仿 merge_morphs）
==============================================================
目标：匹配 形如 Eye_<数字>_[LR](XRange|YRange)[M_Face] 的 **4个** 眼球偏移表情
      —— XRange 左右各1 + YRange 左右各1 = 4个
      （中间数字不固定，以两端 EYE + LR + XRange/YRange 为匹配标准）

对每个匹配表情：删除 value==(0,0,0) 的零位移偏移节点，保留有效非零节点
安全规则：匹配到的表情数量必须恰好为 4，否则拒绝处理（防止误删/漏删）

实现策略（与 merge_morphs 相同的"原始字节保留"）：
- 除 Morph 区外，顶点/面/纹理/材质/骨骼/显示帧/刚体/约束 全部按原始字节保留
- 只完整解析并重建 Morph 区（目标4个表情删除零位移offset，其余Morph原样写回）
- 读取用 RenameReader（UTF-16LE 优先），保证 name 槽日文名不被误判
"""
import os
import re
import sys
import glob
import shutil
import datetime
from merge_morphs import PMXWriter
from rename_morphs import RenameReader

# 匹配 Eye_20_R(XRange)[M_Face] / Eye_21_L(YRange)[M_Face]，数字不固定
EYE_RANGE_RE = re.compile(r'^Eye_\d+_[LR]\((XRange|YRange)\)\[M_Face\]$')


def find_eye_range_morphs(reader):
    """返回匹配的 Morph 列表（按 name_g 匹配）"""
    hits = []
    for mo in reader.morphs:
        if EYE_RANGE_RE.match(mo['name_g'] or ''):
            hits.append(mo)
    return hits


def process_pmx(src):
    """清理单个PMX文件的四个眼球偏移表情的零位移节点，返回True表示处理成功"""
    print(f"\n{'='*60}")
    print(f"处理文件: {os.path.basename(src)}")
    print(f"{'='*60}")

    reader = RenameReader(src).parse()
    print(f"  顶点={reader.vertex_count} 面={reader.face_count} 骨骼={reader.bone_count}")
    print(f"  Morph总数={reader.morph_count} 显示帧={reader.frame_count} 刚体={reader.rigid_count} 约束={reader.joint_count}")
    print(f"  剩余字节={reader.remaining} (应为0)")

    # 防御：解析未对齐拒绝处理
    if reader.remaining != 0:
        print(f"  [ERROR] 文件解析未对齐(剩余{reader.remaining}字节)，已跳过")
        return False

    targets = find_eye_range_morphs(reader)
    print(f"  匹配到眼球偏移表情: {len(targets)} 个 (期望 4)")

    # 安全规则：必须恰好4个，超出或不足都拒绝
    if len(targets) != 4:
        print("  [ERROR] 匹配数量不是4，拒绝处理！")
        for mo in targets:
            print(f"    匹配: {mo['name_g']!r} offset={mo['offset_count']}")
        return False

    # 显示匹配详情
    for mo in targets:
        zero = sum(1 for o in mo['offsets'] if tuple(o['value']) == (0.0, 0.0, 0.0))
        print(f"    目标: {mo['name_g']!r}  name={mo['name']!r}  "
              f"offset={len(mo['offsets'])} (零位移={zero})")

    # 逐个删除零位移节点
    total_removed = 0
    for mo in targets:
        before = len(mo['offsets'])
        mo['offsets'] = [o for o in mo['offsets'] if tuple(o['value']) != (0.0, 0.0, 0.0)]
        after = len(mo['offsets'])
        removed = before - after
        total_removed += removed
        print(f"    {mo['name_g']!r}: {before} -> {after} (删除 {removed} 个零位移节点)")

    if total_removed == 0:
        print("  无零位移节点可删（该模型已是精简状态），无需写回")
        return False

    # 写回（覆盖原文件）
    writer = PMXWriter(reader)
    data = writer.build()
    with open(src, 'wb') as f:
        f.write(data)

    # 验证
    v = RenameReader(src).parse()
    assert v.remaining == 0, "验证失败：剩余字节不为0"
    assert v.morph_count == reader.morph_count, "验证失败：Morph数量变化"
    vt = find_eye_range_morphs(v)
    assert len(vt) == 4, "验证失败：目标表情数量不是4"
    for mo in vt:
        left_zero = sum(1 for o in mo['offsets'] if tuple(o['value']) == (0.0, 0.0, 0.0))
        assert left_zero == 0, f"验证失败：{mo['name_g']} 仍有零位移节点"
    print(f"  [OK] 清理成功并验证通过 (删除{total_removed}个零位移节点, 剩余字节=0)")
    return True





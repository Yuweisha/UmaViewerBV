# -*- coding: utf-8 -*-
"""
[10] 骨骼索引排序（骨骼树 DFS 先序）—— 通过 parent-child 关系排序，子骨骼始终在亲骨骼之后
================================================================
不依赖名称分区，直接遍历骨骼树：
  - 从根骨（parent=None，如「全ての親」）开始，深度优先、先序遍历；
  - 每个节点的子骨按「原始索引升序」遍历（先走最左子树，如 全ての親→下半身→左足）；
  - 走完最左子树后回退到上一个分支，逐次遍历 → 天然形成「亲骨→子树」块状分布；
  - 游离骨/环（无根路径）排在末尾。
★ 相比分区术语表版：新建骨骼（捩骨、足D/ひざD 等）会按亲骨(parent)关系跟在亲骨附近，
  不会因名称不匹配分区而落到列表尾部。

用法：
  python sort_bones_tree.py    # 批量处理当前目录所有 .pmx（自动备份到 [10]备份_时间戳/，原地覆盖）
"""
import struct
import os
import sys
import glob
import shutil
import datetime

SRC = os.path.dirname(os.path.abspath(__file__))
if SRC not in sys.path:
    sys.path.insert(0, SRC)
from sort_bones import PMXReader, _write_idx, _remap_points  # noqa: E402


def parse_parents(r):
    """解析每根骨的 parent 索引（item_raw 中 xyz(12字节) 之后）"""
    parents = []
    for it in r.bone_items:
        item_raw = it[4]
        parents.append(int.from_bytes(item_raw[12:12 + r.bidx], 'little'))
    return parents


def tree_sort(parents, none_idx):
    """DFS 先序遍历骨骼树，返回新顺序索引列表（旧索引 -> 排列后的顺序）"""
    n = len(parents)
    children = {}
    for i in range(n):
        children.setdefault(parents[i], []).append(i)
    for p in children:
        children[p].sort()               # 子骨按原始索引升序（先最左子树）

    order = []
    visited = set()

    def dfs(b):
        if b in visited:
            return
        visited.add(b)
        order.append(b)
        for c in children.get(b, []):
            dfs(c)

    # 根骨（parent=None 或 越界）
    for i in range(n):
        if parents[i] == none_idx or parents[i] >= n:
            dfs(i)
    # 游离骨 / 环
    for i in range(n):
        if i not in visited:
            dfs(i)
    return order


def sort_bones_file_tree(src):
    print(f"\n{'=' * 70}")
    print(f"骨骼树排序(DFS): {os.path.basename(src)}")
    print(f"{'=' * 70}")

    r = PMXReader(src).parse()
    print(f"  骨骼={r.bone_count} bidx={r.bidx} 剩余字节={r.remaining} (应为0)")
    if r.remaining != 0:
        print(f"  [ERROR] 解析未对齐(剩余{r.remaining}字节)，已跳过")
        return False

    names = [it[0].rstrip() for it in r.bone_items]
    none_idx = (1 << (8 * r.bidx)) - 1
    parents = parse_parents(r)

    # 1. 安全守卫：引用越界检查
    for label, pts in (('顶点权重', r.weight_points), ('骨骼Morph', r.morph_bone_points),
                       ('显示帧', r.frame_bone_points), ('刚体', r.rigid_bone_points)):
        bad = [b for _p, b in pts if b >= r.bone_count]
        if bad:
            print(f"  [ERROR] {label}含 {len(bad)} 个越界骨骼索引(首例 {bad[0]})，已跳过")
            return False

    # 2. 树遍历排序
    new_order = tree_sort(parents, none_idx)
    old_to_new = {old: new for new, old in enumerate(new_order)}

    roots = [i for i in range(r.bone_count) if parents[i] == none_idx or parents[i] >= r.bone_count]
    print(f"  根骨({len(roots)}): {[names[i] for i in roots]}")
    print(f"  排序后首 40 骨:")
    for new_pos, old in enumerate(new_order[:40]):
        print(f"    [{new_pos:>3}] {names[old]}")
    print(f"    ... 共 {len(new_order)} 骨")

    # 3. 重建骨骼区（按新顺序）
    out_bone = bytearray()
    out_bone.extend(r.bone_count.to_bytes(4, 'little'))
    item_new_off = {}
    for new_pos, old_idx in enumerate(new_order):
        name_str, name_g_str, name_raw, name_g_raw, item_raw = r.bone_items[old_idx]
        out_bone.extend(len(name_raw).to_bytes(4, 'little', signed=True))
        out_bone.extend(name_raw)
        out_bone.extend(len(name_g_raw).to_bytes(4, 'little', signed=True))
        out_bone.extend(name_g_raw)
        item_new_off[new_pos] = len(out_bone)
        out_bone.extend(item_raw)

    # 4. 骨骼区内部引用重映射（parent/tail/追加亲等）
    bone_ba = bytearray(out_bone)
    b_cnt = 0
    for new_pos, old_idx in enumerate(new_order):
        off = item_new_off[new_pos]
        for rel, old_ref in r.bone_refs[old_idx]:
            if old_ref == none_idx:
                continue
            new_ref = old_to_new.get(old_ref)
            if new_ref is None:
                new_ref = none_idx
            _write_idx(bone_ba, off + rel, new_ref, r.bidx)
            b_cnt += 1

    # 5. 其余区引用重映射
    vertex_out, v_cnt = _remap_points(r.vertex_bytes, r.weight_points, r.vertex_start,
                                      r.bidx, old_to_new)
    morph_out, m_cnt = _remap_points(r.morph_bytes, r.morph_bone_points, r.morph_start,
                                     r.bidx, old_to_new)
    frame_out, f_cnt = _remap_points(r.frame_bytes, r.frame_bone_points, r.frame_start,
                                     r.bidx, old_to_new)
    rigid_out, g_cnt = _remap_points(r.rigid_bytes, r.rigid_bone_points, r.rigid_start,
                                     r.bidx, old_to_new)
    print(f"  引用重映射: 顶点权重={v_cnt} 骨骼字段={b_cnt} Morph={m_cnt} 帧={f_cnt} 刚体={g_cnt}")

    # 6. 组装输出
    out = bytearray()
    out.extend(r.header_bytes)
    out.extend(r.info_bytes)
    out.extend(r.vertex_count.to_bytes(4, 'little'))
    out.extend(vertex_out)
    out.extend(r.face_count.to_bytes(4, 'little'))
    out.extend(r.face_bytes)
    out.extend(r.texture_count.to_bytes(4, 'little'))
    out.extend(r.tex_bytes)
    out.extend(r.material_count.to_bytes(4, 'little'))
    out.extend(r.mat_bytes)
    out.extend(bone_ba)
    out.extend(r.morph_count.to_bytes(4, 'little'))
    out.extend(morph_out)
    out.extend(r.frame_count.to_bytes(4, 'little'))
    out.extend(frame_out)
    out.extend(r.rigid_count.to_bytes(4, 'little'))
    out.extend(rigid_out)
    out.extend(r.joint_count.to_bytes(4, 'little'))
    out.extend(r.joint_bytes)
    data = bytes(out)

    # 7. 写回 + 验证
    with open(src, 'wb') as f:
        f.write(data)
    v = PMXReader(src).parse()
    assert v.remaining == 0, f"验证失败：剩余字节={v.remaining}"
    assert v.bone_count == r.bone_count, f"验证失败：骨骼数变化 {r.bone_count}->{v.bone_count}"
    new_names = [it[0].rstrip() for it in v.bone_items]
    assert sorted(new_names) == sorted(names), "验证失败：骨骼名集合变化"
    # 验证：子骨不高于亲骨
    vparents = parse_parents(v)
    bad_tree = sum(1 for i in range(v.bone_count)
                   if vparents[i] != none_idx and vparents[i] < v.bone_count
                   and vparents[i] > i)
    assert bad_tree == 0, f"验证失败：{bad_tree} 个子骨高于亲骨"
    print(f"  [OK] 骨骼树排序成功并验证通过 (剩余字节=0, 根骨在前, 子骨不高于亲骨, 骨名集合不变)")
    return True





# -*- coding: utf-8 -*-
"""
通用脚本：删除PMX模型中重复引用/重复名称的材质

规则(对同名材质分组后):
  - 若组内所有材质面数完全相同 → 保留索引最前的一个，删除索引靠后的
  - 若组内面数不相同(差距大)   → 保留面数最多的一个(并列最多取索引最前)，删除面数较少的
  - 仅有一个条目的材质 → 原样保留

实现策略(仿 merge_morphs 的"原始字节保留"):
  - 模型信息/顶点/纹理/骨骼/显示帧/刚体/约束 → 全部按原始字节保留
  - 面区/材质区 → 按"保留材质的原始字节块"重新拼接(不重编码, 保持原编码UTF-8/UTF-16)
  - Morph区      → 原始字节保留，仅对 材质型Morph(type=8) 的材质索引字段做字节级定点重映射
  - 自动识别面区单位: 标准PMX为每面3个顶点索引; 部分老文件为每面1个索引(材质面数和=fcount)
"""
import struct
import os
import sys
import glob
import shutil
import datetime
from collections import defaultdict


class PMXReader:
    def __init__(self, filepath):
        with open(filepath, 'rb') as f:
            self.data = f.read()
        self.pos = 0

    def rb(self, n):
        d = self.data[self.pos:self.pos + n]
        self.pos += n
        return d

    def ri(self, size, signed=True):
        return int.from_bytes(self.rb(size), 'little', signed=signed)

    def rf(self):
        return struct.unpack('<f', self.rb(4))[0]

    def rtext(self):
        n = self.ri(4)
        if n < 0 or self.pos + n > len(self.data):
            raise ValueError('Invalid PMX text length')
        raw = self.rb(n)
        if self.encoding not in (0, 1):
            raise ValueError('Invalid PMX encoding flag')
        return raw.decode('utf-16-le' if self.encoding == 0 else 'utf-8')

    def rvec(self, n=3):
        return [self.rf() for _ in range(n)]

    # ---------- 各区解析 ----------

    def _parse_header(self):
        self.rb(4)  # magic
        self.version = self.rf()
        header_size = self.ri(1, False)
        self.encoding = self.ri(1, False)
        self.uv_count = self.ri(1, False)
        self.vidx = self.ri(1, False)
        self.tidx = self.ri(1, False)
        self.midx = self.ri(1, False)
        self.bidx = self.ri(1, False)
        self.moidx = self.ri(1, False)
        self.ridx = self.ri(1, False)

    def _parse_info(self):
        start = self.pos
        self.model_name = self.rtext()
        self.model_name_global = self.rtext()
        self.comment = self.rtext()
        self.comment_global = self.rtext()
        self.info_bytes = self.data[start:self.pos]

    def _parse_vertices(self):
        self.vertex_count = self.ri(4, False)
        start = self.pos
        for _ in range(self.vertex_count):
            self.rvec(3); self.rvec(3); self.rvec(2)
            for _ in range(self.uv_count):
                self.rvec(4)
            dt = self.ri(1, False)
            if dt == 0:
                self.ri(self.bidx)
            elif dt == 1:
                self.ri(self.bidx); self.ri(self.bidx); self.rf()
            elif dt == 2:
                self.pos += 4 * self.bidx + 16
            elif dt == 3:
                self.pos += 2 * self.bidx + 4 + 36
            elif dt == 4:
                self.pos += 4 * self.bidx + 16
            self.rf()  # edge
        self.vertex_bytes = self.data[start:self.pos]

    def _valid_texture_parse(self, p):
        """
        尝试在p处解析纹理+材质区。
        成功返回 (纹理数, 纹理区结束位置, 材质数)；失败返回 None。
        """
        if p + 4 > len(self.data):
            return None
        tc = int.from_bytes(self.data[p:p + 4], 'little')
        if tc <= 0 or tc > 100000:
            return None
        q = p + 4
        ok = True
        for _ in range(tc):
            n = int.from_bytes(self.data[q:q + 4], 'little', signed=True)
            q += 4
            if n < 0 or n > 65536 or q + n > len(self.data):
                ok = False
                break
            q += n
        if not ok:
            return None
        if q + 4 <= len(self.data):
            mc = int.from_bytes(self.data[q:q + 4], 'little')
            if 0 < mc <= 100000:
                return (tc, q, mc)
        return None

    def _parse_faces(self):
        """读面数，并自动识别面区单位(每面1或3个顶点索引)，定位到纹理区起点"""
        self.face_count = self.ri(4, False)
        self.face_start = self.pos

        # 标准PMX: 每面3个顶点索引
        p3 = self.face_start + self.face_count * 3 * self.vidx
        if self._valid_texture_parse(p3):
            self.face_mult = 3
            self.pos = p3                        # 定位到纹理区起点
            self.face_bytes = self.data[self.face_start:p3]
            return

        # 老格式: 每面1个顶点索引
        p1 = self.face_start + self.face_count * self.vidx
        if self._valid_texture_parse(p1):
            self.face_mult = 1
            self.pos = p1
            self.face_bytes = self.data[self.face_start:p1]
            return

        raise ValueError("无法识别面区单位(1或3索引/面)")

    def _parse_textures(self):
        self.texture_count = self.ri(4, False)
        start = self.pos
        self.texture_names = []
        for _ in range(self.texture_count):
            self.texture_names.append(self.rtext())
        self.texture_bytes = self.data[start:self.pos]

    def _parse_materials(self):
        """完整解析材质(名称/面数/原始字节块)，并校验面数和"""
        self.material_count = self.ri(4, False)
        mstart = self.pos
        self.materials = []
        for _ in range(self.material_count):
            item = {}
            s = self.pos
            item['name'] = self.rtext()
            item['name_g'] = self.rtext()
            self.pos += 16 + 12 + 4 + 12          # diffuse+spec+pw+ambient
            self.ri(1, False)                     # flag
            self.pos += 16 + 4                    # edge色 + edge大小
            self.ri(self.tidx)                    # 纹理
            self.ri(self.tidx)                    # 环境
            self.ri(1, False)                     # 环境模式
            toon_flag = self.ri(1, False)
            if toon_flag == 0:
                self.ri(self.tidx)                # 个別Toon
            else:
                self.ri(1)                        # 共享Toon
            self.rtext()                          # memo
            item['faces'] = self.ri(4, False)     # 面数
            item['raw'] = self.data[s:self.pos]
            self.materials.append(item)
        self.material_bytes = self.data[mstart:self.pos]

        # 防御: 材质面数和应等于面区数
        sum_faces = sum(m['faces'] for m in self.materials)
        if sum_faces != self.face_count:
            raise ValueError(
                f"面数校验失败: 面区fcount={self.face_count} != 材质面数和={sum_faces}")

    def _parse_bones(self):
        self.bone_count = self.ri(4, False)
        start = self.pos
        for _ in range(self.bone_count):
            self.rtext(); self.rtext()
            self.rvec(3)
            self.ri(self.bidx)
            self.ri(4, False)
            flag = self.ri(2, False)
            if flag & 0x0001:
                self.ri(self.bidx)
            else:
                self.rvec(3)
            if flag & 0x0100:
                self.ri(self.bidx); self.rf()
            if flag & 0x0200:
                self.ri(self.bidx); self.rf()
            if flag & 0x0400:
                self.rvec(3)
            if flag & 0x0800:
                self.rvec(3); self.rvec(3)
            if flag & 0x2000:
                self.ri(4)
            if flag & 0x0020:
                self.ri(self.bidx); self.ri(4, False); self.rf()
                ic = self.ri(4, False)
                for _ in range(ic):
                    self.ri(self.bidx)
                    if self.ri(1, False):
                        self.rvec(3); self.rvec(3)
        self.bone_bytes = self.data[start:self.pos]

    def _parse_morphs(self):
        """完整解析Morph区，并记录 材质型Morph(type=8) 的 index 字段位置"""
        self.morph_count = self.ri(4, False)
        self.morph_start = self.pos
        self.morph_patch_points = []   # [(绝对文件偏移, 旧材质索引)]
        for _ in range(self.morph_count):
            self.rtext()   # name
            self.rtext()   # name_g
            self.ri(1, False)      # panel
            mtype = self.ri(1, False)
            oc = self.ri(4, False)
            for _ in range(oc):
                if mtype == 0 or mtype == 9:      # group/flip
                    self.ri(self.moidx); self.rf()
                elif mtype == 1:                  # 顶点
                    self.ri(self.vidx); self.rvec(3)
                elif mtype == 2:                  # 骨骼
                    self.ri(self.bidx); self.rvec(3); self.rvec(4)
                elif mtype in (3, 4, 5, 6, 7):    # UV/追加UV
                    self.ri(self.vidx); self.rvec(4)
                elif mtype == 8:                  # 材质: 材质Index + 演算(1) + 112字节
                    idx_pos = self.pos
                    old_idx = self.ri(self.midx)
                    self.morph_patch_points.append((idx_pos, old_idx))
                    self.ri(1, False)             # 演算
                    self.pos += 112
                elif mtype == 10:                 # 冲动
                    self.ri(self.ridx); self.ri(1, False)
                    self.rvec(3); self.rvec(3)
        self.morph_bytes = self.data[self.morph_start:self.pos]

    def _parse_tail(self):
        self.frame_count = self.ri(4, False)
        start = self.pos
        for _ in range(self.frame_count):
            self.rtext(); self.rtext()
            self.ri(1, False)
            ic = self.ri(4, False)
            for _ in range(ic):
                it = self.ri(1, False)
                if it == 0:
                    self.ri(self.bidx)
                else:
                    self.ri(self.moidx)
        self.frame_bytes = self.data[start:self.pos]

        self.rigid_count = self.ri(4, False)
        start = self.pos
        for _ in range(self.rigid_count):
            self.rtext(); self.rtext()
            self.ri(self.bidx)
            self.ri(1, False)
            self.ri(2, False)
            self.ri(1, False)
            self.rvec(3); self.rvec(3); self.rvec(3)
            self.pos += 20
            self.ri(1, False)
        self.rigid_bytes = self.data[start:self.pos]

        # ★ 约束区是否存在（即使 joint_count=0，文件尾部也可能有 4 字节计数）
        #   —— 例：1044 原文件含"空约束区"；若不保留，重建后 merge_morphs 等
        #      无条件读约束计数的脚本会解析越界 → remaining=-4
        self.has_joint = self.pos < len(self.data)
        if self.has_joint:
            self.joint_count = self.ri(4, False)
            start = self.pos
            for _ in range(self.joint_count):
                self.rtext(); self.rtext()
                self.ri(1, False)
                self.ri(self.ridx); self.ri(self.ridx)
                self.rvec(3); self.rvec(3)
                self.pos += 72
            self.joint_bytes = self.data[start:self.pos]
        else:
            self.joint_count = 0
            self.joint_bytes = b''

        self.remaining = len(self.data) - self.pos

    def parse(self):
        self._parse_header()
        self._parse_info()
        self._parse_vertices()
        self._parse_faces()
        self._parse_textures()
        self._parse_materials()
        self._parse_bones()
        self._parse_morphs()
        self._parse_tail()
        return self


class PMXWriter:
    def __init__(self, m):
        self.m = m
        self.out = bytearray()

    def w(self, b):
        self.out.extend(b)

    def wi(self, val, size):
        self.out.extend(int(val).to_bytes(size, 'little', signed=(val < 0)))

    def wf(self, val):
        self.out.extend(struct.pack('<f', val))

    def build(self, keep_idx):
        m = self.m
        # ---- Header ----
        self.w(b'PMX ')
        self.wf(m.version)
        self.wi(8, 1)
        self.wi(m.encoding, 1)
        self.wi(m.uv_count, 1)
        self.wi(m.vidx, 1); self.wi(m.tidx, 1); self.wi(m.midx, 1)
        self.wi(m.bidx, 1); self.wi(m.moidx, 1); self.wi(m.ridx, 1)

        # ---- 模型信息(原样) ----
        self.out.extend(m.info_bytes)

        # ---- 顶点(原样) ----
        self.wi(m.vertex_count, 4)
        self.out.extend(m.vertex_bytes)

        # ---- 面区(按保留材质重新拼接) ----
        keep_set = set(keep_idx)
        blk = m.face_mult * m.vidx
        new_faces = bytearray()
        new_face_count = 0
        # 预计算每材质在面区中的字节起点
        offsets = []
        acc = 0
        for mat in m.materials:
            offsets.append(acc * blk)
            acc += mat['faces']
        for i in keep_idx:
            n = m.materials[i]['faces']
            new_faces.extend(m.face_bytes[offsets[i]:offsets[i] + n * blk])
            new_face_count += n
        self.wi(new_face_count, 4)
        self.out.extend(new_faces)

        # ---- 纹理(原样) ----
        self.wi(m.texture_count, 4)
        self.out.extend(m.texture_bytes)

        # ---- 材质区(按保留材质拼接, 不重编码) ----
        self.wi(len(keep_idx), 4)
        for i in keep_idx:
            self.out.extend(m.materials[i]['raw'])

        # ---- 骨骼(原样) ----
        self.wi(m.bone_count, 4)
        self.out.extend(m.bone_bytes)

        # ---- Morph区(原样 + 材质索引字节级重映射) ----
        old_to_new = {}
        for new_i, old_i in enumerate(keep_idx):
            old_to_new[old_i] = new_i
        # 被删材质→同名保留材质
        for i, mat in enumerate(m.materials):
            if i not in keep_set:
                for j, mat2 in enumerate(m.materials):
                    if j in keep_set and mat2['name'] == mat['name']:
                        old_to_new[i] = old_to_new[j]
                        break
        morph_out = bytearray(m.morph_bytes)
        msize = m.midx
        for idx_pos, old_idx in m.morph_patch_points:
            new_idx = old_to_new.get(old_idx, old_idx)
            if new_idx != old_idx:
                off = idx_pos - m.morph_start
                morph_out[off:off + msize] = new_idx.to_bytes(msize, 'little')
        self.wi(m.morph_count, 4)
        self.out.extend(morph_out)

        # ---- 显示帧/刚体/约束(原样) ----
        self.wi(m.frame_count, 4)
        self.out.extend(m.frame_bytes)
        self.wi(m.rigid_count, 4)
        self.out.extend(m.rigid_bytes)
        # 按原文件是否含约束区写回（joint_count=0 但区存在时也保留 4 字节计数）
        if m.has_joint:
            self.wi(m.joint_count, 4)
            self.out.extend(m.joint_bytes)

        return bytes(self.out)


def _has_subset_sum(values, target):
    """检测 values 是否存在子集和为 target（用于识别'吞并'材质）"""
    if target <= 0:
        return False
    reachable = {0}
    for v in values:
        if v <= 0 or v > target:
            continue
        for s in list(reachable):
            ns = s + v
            if ns == target:
                return True
            if ns < target:
                reachable.add(ns)
    return target in reachable


def _detect_swallow(materials, group_idxs):
    """检测同名材质组中是否存在'吞并'错误材质：
    某材质的 face_count 恰好等于组外前驱材质(索引更小)的 face_count 子集和，
    说明它错误地吞并了其他材质的面。返回该吞并材质的索引，或 None。

    例(田径服)：mtl_bdy0001_00_2 出现两次，第二个(18672面)= waku0 + 主衣装 面数，
    是错误材质，应删除；第一个(3666面)才是正确号码牌材质。
    """
    group_set = set(group_idxs)
    for i in sorted(group_idxs):
        fc = materials[i]['faces']
        prefix = [materials[j]['faces'] for j in range(i) if j not in group_set]
        if _has_subset_sum(prefix, fc):
            return i
    return None


def select_keep_delete(materials):
    """
    返回 (keep索引列表按原顺序, delete索引列表)
    规则:
      - 同名分组
      - 组内所有面数相同 → 保留索引最前, 删除其余
      - 组内面数不同     → 保留面数最大(并列取索引最前), 删除面数较少的
    """
    groups = defaultdict(list)
    for i, mat in enumerate(materials):
        groups[mat['name']].append(i)

    keep = []
    delete = []
    for idxs in groups.values():
        if len(idxs) == 1:
            keep.append(idxs[0])
            continue
        by_faces = defaultdict(list)
        for i in idxs:
            by_faces[materials[i]['faces']].append(i)
        if len(by_faces) == 1:
            # 面数全部相同 → 保留最前, 删除靠后
            keep.append(idxs[0])
            delete.extend(idxs[1:])
        else:
            # 特殊：检测'吞并'错误材质（田径服 zekken：面数多的吞并了其他材质的面）
            swallow = _detect_swallow(materials, idxs)
            if swallow is not None:
                # 保留面数最小的（正确材质），删除其余（含吞并材质）
                min_faces = min(by_faces.keys())
                keep_local = by_faces[min_faces][0]
                keep.append(keep_local)
                for i in idxs:
                    if i != keep_local:
                        delete.append(i)
            else:
                # 一般：面数不同 → 保留面数最大(并列取索引最前)
                max_faces = max(by_faces.keys())
                keep_grp = by_faces[max_faces]
                keep.append(keep_grp[0])
                delete.extend(keep_grp[1:])
                for fi, g in by_faces.items():
                    if fi != max_faces:
                        delete.extend(g)
    keep.sort()
    return keep, delete


def process_pmx(src):
    """去重单个PMX文件，返回True表示有删除操作"""
    print(f"\n{'=' * 60}")
    print(f"处理文件: {os.path.basename(src)}")
    print(f"{'=' * 60}")

    reader = PMXReader(src).parse()
    print(f"  顶点={reader.vertex_count} 面区数={reader.face_count} "
          f"纹理={reader.texture_count} 材质={reader.material_count} 骨骼={reader.bone_count}")
    print(f"  Morph={reader.morph_count} 显示帧={reader.frame_count} "
          f"刚体={reader.rigid_count} 约束={reader.joint_count}")
    print(f"  面区单位: {reader.face_mult}索引/面  剩余字节={reader.remaining} (应为0)")

    # 同名材质分组统计
    name_count = defaultdict(int)
    for mat in reader.materials:
        name_count[mat['name']] += 1
    dup_names = {k: v for k, v in name_count.items() if v > 1}
    if not dup_names:
        print("  没有重复名称的材质，无需处理")
        return False

    print(f"  重复材质名: {len(dup_names)} 组 -> {dict(dup_names)}")

    keep, delete = select_keep_delete(reader.materials)
    if not delete:
        print("  无需删除")
        return False

    print(f"  保留材质 {len(keep)} 个, 删除材质 {len(delete)} 个")
    for i in delete:
        mat = reader.materials[i]
        print(f"    [删除] 索引{i} {mat['name']!r} 面数={mat['faces']}")

    # Morph 材质引用检查
    ref_names = set()
    for _, old_idx in reader.morph_patch_points:
        if old_idx in delete:
            ref_names.add(reader.materials[old_idx]['name'])
    if ref_names:
        print(f"  材质型Morph引用到待删材质: {sorted(ref_names)} -> 将重映射到同名保留材质")
    else:
        print("  材质型Morph无引用待删材质")

    # 写回(覆盖原文件)
    writer = PMXWriter(reader)
    data = writer.build(keep)
    with open(src, 'wb') as f:
        f.write(data)

    # 验证
    v = PMXReader(src).parse()
    assert v.remaining == 0, "验证失败: 剩余字节不为0"
    assert v.material_count == len(keep), "验证失败: 材质数量不符"
    v_name_count = defaultdict(int)
    for mat in v.materials:
        v_name_count[mat['name']] += 1
    dup_after = {k: c for k, c in v_name_count.items() if c > 1}
    assert not dup_after, f"验证失败: 仍有重复材质名 {dup_after}"
    print(f"  [OK] 去重成功并验证通过 (材质 {reader.material_count}->{v.material_count}, 剩余字节=0)")
    return True





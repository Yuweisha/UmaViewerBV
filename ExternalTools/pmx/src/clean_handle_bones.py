# -*- coding: utf-8 -*-
"""
[9] 清理末端 Handle 骨 —— 仿 merge_morphs 架构
================================================
依据 1025 -> 1025_2 实证还原的删除逻辑：
  1. 删除全部 name 以 "Handle" 结尾的骨骼（弹簧/FK 控制骨的末端尾部指针）
  2. 保留骨按原顺序"紧凑重排"（新索引 = 旧索引 - 前面被删骨数）
  3. 引用重映射：
     - 指向保留骨的引用（顶点权重/骨骼字段/骨骼Morph/显示帧/刚体）-> 紧凑后新索引
     - 指向被删 Handle 骨的引用 -> 置 0xFFFFFFFF（None）
     - 顶点权重若引用 Handle 骨（会导致权重崩坏）-> 拒绝处理
  4. 不改 flag / 父链 / 位置，不重建骨架

实现策略（最大程度保留原始字节）：
  - 顶点/面/纹理/材质/Morph/显示帧/刚体/约束：原始字节保留，
    仅对骨骼索引引用点做定点改写
  - 骨骼区：完整重建（删条目 + 内部引用重映射），名称用原始字节直接回写
  - main() 采用与 merge_morphs 相同的 备份/批处理/验证/回车退出 流程
"""
import struct
import os
import sys
import glob
import shutil
import datetime
import re

from bone_redirect import resolve_redirect  # noqa: E402


def decode_text(raw):
    """兼容"声明UTF-8、实际UTF-16LE"的文件"""
    if len(raw) == 0:
        return ''
    if len(raw) % 2 == 0:
        try:
            s = raw.decode('utf-16-le')
            if not any(0xD800 <= ord(c) <= 0xDFFF for c in s):
                return s
        except Exception:
            pass
    try:
        s = raw.decode('utf-8')
        if '\x00' in s and len(raw) % 2 == 0:
            return raw.decode('utf-16-le')
        return s
    except Exception:
        pass
    if len(raw) % 2 == 0:
        try:
            return raw.decode('utf-16-le')
        except Exception:
            pass
    return repr(raw)


class PMXReader:
    def __init__(self, filepath):
        with open(filepath, 'rb') as f:
            self.data = f.read()
        self.pos = 0

    def rb(self, n):
        d = self.data[self.pos:self.pos + n]
        self.pos += n
        return d

    def ri(self, size, signed=False):
        return int.from_bytes(self.rb(size), 'little', signed=signed)

    def rf(self):
        return struct.unpack('<f', self.rb(4))[0]

    def rvec(self, n=3):
        return [self.rf() for _ in range(n)]

    def rtext(self):
        """返回 (解码名, 含长度前缀的原始字节)"""
        n = self.ri(4, signed=True)
        raw = self.rb(n)
        return decode_text(raw), raw

    def parse(self):
        # ===== 头部（原样保留） =====
        self.rb(4)  # magic "PMX "
        self.version = self.rf()
        self.ri(1, False)   # header_size
        self.encoding = self.ri(1, False)
        self.uv_count = self.ri(1, False)
        self.vidx = self.ri(1, False)
        self.tidx = self.ri(1, False)
        self.midx = self.ri(1, False)
        self.bidx = self.ri(1, False)
        self.moidx = self.ri(1, False)
        self.ridx = self.ri(1, False)
        self.header_bytes = self.data[0:self.pos]

        # ===== 模型信息（原样保留） =====
        info_start = self.pos
        self.model_name, _ = self.rtext()
        self.model_name_g, _ = self.rtext()
        self.comment, _ = self.rtext()
        self.comment_g, _ = self.rtext()
        self.info_bytes = self.data[info_start:self.pos]

        # ===== 顶点（原样保留 + 收集权重骨骼索引） =====
        self.vertex_count = self.ri(4, False)
        self.vertex_start = self.pos
        self.weight_points = []   # [(绝对偏移, 旧骨骼索引)]
        for _ in range(self.vertex_count):
            self.pos += 32
            for _ in range(self.uv_count):
                self.pos += 16
            dt = self.ri(1, False)
            if dt == 0:                          # BDEF1
                self.weight_points.append((self.pos, self.ri(self.bidx)))
            elif dt == 1:                        # BDEF2
                p1 = self.pos; b1 = self.ri(self.bidx)
                p2 = self.pos; b2 = self.ri(self.bidx)
                self.rf()
                self.weight_points.append((p1, b1))
                self.weight_points.append((p2, b2))
            elif dt == 2:                        # BDEF4：骨骼Index×4 + float权重×4（分开，非交错）
                for _ in range(4):
                    self.weight_points.append((self.pos, self.ri(self.bidx)))
                self.pos += 16                   # 跳过 4 个 float 权重
            elif dt == 3:                        # SDEF
                p1 = self.pos; b1 = self.ri(self.bidx)
                p2 = self.pos; b2 = self.ri(self.bidx)
                self.rf()
                self.weight_points.append((p1, b1))
                self.weight_points.append((p2, b2))
                self.pos += 36
            elif dt == 4:                        # QDEF
                for _ in range(4):
                    self.weight_points.append((self.pos, self.ri(self.bidx)))
                self.pos += 16
            self.rf()                            # 边缘倍率
        self.vertex_bytes = self.data[self.vertex_start:self.pos]

        # ===== 面（原样保留；自动识别 1/3 索引每面） =====
        self.face_count = self.ri(4, False)
        self.face_start = self.pos

        def _valid_tex(p):
            if p + 4 > len(self.data):
                return None
            tc = int.from_bytes(self.data[p:p + 4], 'little')
            if tc <= 0 or tc > 100000:
                return None
            q = p + 4
            for _ in range(tc):
                n = int.from_bytes(self.data[q:q + 4], 'little', signed=True)
                q += 4
                if n < 0 or n > 65536 or q + n > len(self.data):
                    return None
                q += n
            if q + 4 <= len(self.data):
                mc = int.from_bytes(self.data[q:q + 4], 'little')
                if 0 < mc <= 100000:
                    return (tc, q)
            return None

        r3 = _valid_tex(self.face_start + self.face_count * 3 * self.vidx)
        r1 = _valid_tex(self.face_start + self.face_count * self.vidx)
        if r3:
            face_mult = 3
        elif r1:
            face_mult = 1
        else:
            raise ValueError('面区单位识别失败')
        self.pos = self.face_start + self.face_count * face_mult * self.vidx
        self.face_bytes = self.data[self.face_start:self.pos]

        # ===== 纹理（原样保留） =====
        self.texture_count = self.ri(4, False)
        tex_start = self.pos
        for _ in range(self.texture_count):
            self.rtext()
        self.tex_bytes = self.data[tex_start:self.pos]

        # ===== 材质（原样保留，按PMX规范跳过） =====
        self.material_count = self.ri(4, False)
        mat_start = self.pos
        for _ in range(self.material_count):
            self.rtext(); self.rtext()
            self.pos += 16 + 12 + 4 + 12       # Diffuse+Specular+Spec係数+Ambient
            self.ri(1, False)                  # 描画フラグ
            self.pos += 16 + 4                 # エッジ色+エッジサイズ
            self.ri(self.tidx)                 # 通常テクスチャ
            self.ri(self.tidx)                 # スフィアテクスチャ
            self.ri(1, False)                  # スフィアモード
            toon_flag = self.ri(1, False)      # 共有Toonフラグ
            if toon_flag == 0:
                self.ri(self.tidx)             # 個別Toon
            else:
                self.ri(1)                     # 共有Toon[0~9]
            self.rtext()                       # メモ
            self.ri(4, False)                  # 面数
        self.mat_bytes = self.data[mat_start:self.pos]

        # ===== 骨骼（完整解析：收集条目原始字节 + 内部引用点） =====
        self.bone_count = self.ri(4, False)
        self.bone_start = self.pos
        self.bone_items = []   # [(name_str, name_g_str, name_raw含前缀, name_g_raw含前缀, item_raw)]
        self.bone_refs = []    # [每骨: [(字段相对item起点偏移, 旧骨骼索引)]]
        for _ in range(self.bone_count):
            name_str, name_raw = self.rtext()
            name_g_str, name_g_raw = self.rtext()
            item_start = self.pos
            refs = []
            self.rvec(3)                           # 位置
            refs.append((self.pos - item_start, self.ri(self.bidx)))   # 親骨
            self.ri(4, False)                      # layer
            flag = self.ri(2, False)
            if flag & 0x0001:                      # tail 为骨
                refs.append((self.pos - item_start, self.ri(self.bidx)))
            else:
                self.rvec(3)                       # tail 为偏移
            if flag & 0x0100:                      # 追加親
                refs.append((self.pos - item_start, self.ri(self.bidx)))
                self.rf()
            if flag & 0x0200:                      # 追加
                refs.append((self.pos - item_start, self.ri(self.bidx)))
                self.rf()
            if flag & 0x0400:                      # 固定軸
                self.rvec(3)
            if flag & 0x0800:                      # ローカル軸
                self.rvec(3); self.rvec(3)
            if flag & 0x2000:                      # 外部親
                self.ri(4)
            if flag & 0x0020:                      # IK
                refs.append((self.pos - item_start, self.ri(self.bidx)))  # IK target
                self.ri(4, False); self.rf()       # 解除回数+回転限界
                ic = self.ri(4, False)
                for _ in range(ic):                # IK 子链
                    refs.append((self.pos - item_start, self.ri(self.bidx)))
                    if self.ri(1, False):
                        self.rvec(3); self.rvec(3) # 上限/下限
            item_raw = self.data[item_start:self.pos]
            self.bone_items.append((name_str, name_g_str, name_raw, name_g_raw, item_raw))
            self.bone_refs.append(refs)
        self.bone_bytes = self.data[self.bone_start:self.pos]

        # ===== Morph（原样保留 + 收集 type=2 骨骼索引） =====
        self.morph_count = self.ri(4, False)
        self.morph_start = self.pos
        self.morph_bone_points = []   # [(绝对偏移, 旧骨骼索引)]
        for _ in range(self.morph_count):
            self.rtext(); self.rtext()
            self.ri(1, False)         # panel
            mtype = self.ri(1, False)
            oc = self.ri(4, False)
            for _ in range(oc):
                t = mtype
                if t == 0 or t == 9:                    # 组/翻转
                    self.ri(self.moidx); self.rf()
                elif t == 1:                            # 顶点
                    self.ri(self.vidx); self.rvec(3)
                elif t == 2:                            # 骨骼
                    self.morph_bone_points.append((self.pos, self.ri(self.bidx)))
                    self.rvec(3); self.rvec(4)
                elif t in (3, 4, 5, 6, 7):              # UV/追加UV
                    self.ri(self.vidx); self.rvec(4)
                elif t == 8:                            # 材质
                    self.ri(self.midx); self.ri(1, False); self.pos += 112
                elif t == 10:                           # 冲动
                    self.ri(self.ridx); self.ri(1, False); self.rvec(3); self.rvec(3)
        self.morph_bytes = self.data[self.morph_start:self.pos]

        # ===== 显示帧（原样保留 + 收集骨骼条目） =====
        self.frame_count = self.ri(4, False)
        self.frame_start = self.pos
        self.frame_bone_points = []   # [(绝对偏移, 旧骨骼索引)]
        for _ in range(self.frame_count):
            self.rtext(); self.rtext()
            self.ri(1, False)         # special
            ic = self.ri(4, False)
            for _ in range(ic):
                it = self.ri(1, False)
                if it == 0:           # 骨骼
                    self.frame_bone_points.append((self.pos, self.ri(self.bidx)))
                else:                 # Morph
                    self.ri(self.moidx)
        self.frame_bytes = self.data[self.frame_start:self.pos]

        # ===== 刚体（原样保留 + 收集绑定骨骼） =====
        self.rigid_count = self.ri(4, False)
        self.rigid_start = self.pos
        self.rigid_bone_points = []   # [(绝对偏移, 旧骨骼索引)]
        for _ in range(self.rigid_count):
            self.rtext(); self.rtext()
            self.rigid_bone_points.append((self.pos, self.ri(self.bidx)))
            self.ri(1, False); self.ri(2, False); self.ri(1, False)
            self.rvec(3); self.rvec(3); self.rvec(3)
            self.pos += 20            # 質量/線形/角度/反発/摩擦
            self.ri(1, False)
        self.rigid_bytes = self.data[self.rigid_start:self.pos]

        # ===== 约束（原样保留） =====
        if self.pos < len(self.data):
            self.joint_count = self.ri(4, False)
            joint_start = self.pos
            for _ in range(self.joint_count):
                self.rtext(); self.rtext()
                self.ri(1, False)
                self.ri(self.ridx); self.ri(self.ridx)
                self.rvec(3); self.rvec(3)
                self.pos += 72
            self.joint_bytes = self.data[joint_start:self.pos]
        else:
            self.joint_count = 0
            self.joint_bytes = b''

        self.remaining = len(self.data) - self.pos
        return self

# ================= 删除处理 =================
def _write_idx(ba, off, val, size):
    """在 bytearray 的 off 处写入 size 字节的骨骼索引 val（little-endian）"""
    v = val & 0xFFFFFFFF
    for k in range(size):
        ba[off + k] = (v >> (8 * k)) & 0xFF


def _remap_points(data_bytes, points, section_start, bidx, compact, handle_set):
    """在原始区字节中，把 (abs_off, old_idx) 引用点改写为新索引。
    - 指向保留骨 -> 紧凑新索引
    - 指向被删 Handle 骨 -> None (0xFF... 按 bidx 位数)
    返回 (改写后的bytes, 统计dict)。"""
    none_idx = (1 << (8 * bidx)) - 1
    ba = bytearray(data_bytes)
    st = {'remap': 0, 'handle_to_none': 0}
    for abs_off, old in points:
        rel = abs_off - section_start
        if old == none_idx:
            continue                       # 原已是 None，无需改
        if old in handle_set:
            new = none_idx
            st['handle_to_none'] += 1
        elif old in compact:
            new = compact[old]
            st['remap'] += 1
        else:
            raise ValueError(f'无法映射骨骼索引 {old}')
        _write_idx(ba, rel, new, bidx)
    return bytes(ba), st


def find_empty_ref_bones(r):
    """识别"互相引用空骨骼"（仅链式互连的空骨，如 Sp_He_Ear0_L/R_01/02）：
    空骨 = 权重0 + 无刚体/Morph骨骼/显示帧引用 + 未被有用骨(parent/tail)引用
    互相引用 = 空骨中 parent 或 tail 指向另一个空骨（形成链/群）
    注：独立空骨（Eye_locator/Head_attach 等，动画制作用）不删除。

    ★ IK 功能骨保护：両目/両目IK/両目IK先 是无顶点权重的控件骨，均落入
    「空骨」候选集；両目IK先 的 parent=両目（両目亦空骨）会命中「互相引用」条件。
    但両目IK(flag&0x0020) 的 IK target=両目IK先 必须保留，否则両目IK target
    悬空 65535 -> MMD 解算 IK 越界崩溃（见知识库 18.3）。故无论源骨是否空骨，
    IK 骨的 IK target / IK links 一律纳入保护集。"""
    # parent/tail/IK 引用解析
    pos = 0
    base = r.bone_start
    parent = [None] * r.bone_count
    tail = [None] * r.bone_count
    add_parent = [None] * r.bone_count    # 追加親 (flag 0x0100)
    add_bone = [None] * r.bone_count      # 追加 (flag 0x0200)
    ik_flag = [0] * r.bone_count          # 是否 IK 功能骨 (flag & 0x0020)
    ik_target = [None] * r.bone_count     # IK target 索引
    ik_links = [[] for _ in range(r.bone_count)]  # IK link 链
    for i in range(r.bone_count):
        n = int.from_bytes(r.data[base+pos:base+pos+4], 'little', signed=True); pos += 4
        pos += n
        n2 = int.from_bytes(r.data[base+pos:base+pos+4], 'little', signed=True); pos += 4
        pos += n2
        pos += 12
        parent[i] = int.from_bytes(r.data[base+pos:base+pos+r.bidx], 'little'); pos += r.bidx
        pos += 4
        flag = int.from_bytes(r.data[base+pos:base+pos+2], 'little'); pos += 2
        if flag & 0x0001:
            tail[i] = int.from_bytes(r.data[base+pos:base+pos+r.bidx], 'little'); pos += r.bidx
        else:
            pos += 12
        if flag & 0x0100:
            add_parent[i] = int.from_bytes(r.data[base+pos:base+pos+r.bidx], 'little'); pos += r.bidx
            pos += 4
        if flag & 0x0200:
            add_bone[i] = int.from_bytes(r.data[base+pos:base+pos+r.bidx], 'little'); pos += r.bidx
            pos += 4
        if flag & 0x0400:
            pos += 12
        if flag & 0x0800:
            pos += 24
        if flag & 0x2000:
            pos += 4
        if flag & 0x0020:
            ik_flag[i] = 1
            ik_target[i] = int.from_bytes(r.data[base+pos:base+pos+r.bidx], 'little'); pos += r.bidx
            pos += 8
            ic = int.from_bytes(r.data[base+pos:base+pos+4], 'little'); pos += 4
            for _ in range(ic):
                lnk = int.from_bytes(r.data[base+pos:base+pos+r.bidx], 'little'); pos += r.bidx
                ik_links[i].append(lnk)
                if r.data[base+pos]:
                    pos += 24
                pos += 1

    # 权重
    wcnt = [0] * r.bone_count
    for _p, b in r.weight_points:
        if b < r.bone_count:
            wcnt[b] += 1

    # 刚体/Morph骨骼/显示帧 引用
    used = set()
    for _p, b in r.rigid_bone_points:
        used.add(b)
    for _p, b in r.morph_bone_points:
        used.add(b)
    for _p, b in r.frame_bone_points:
        used.add(b)

    # 空骨
    empty = {i for i in range(r.bone_count) if wcnt[i] == 0 and i not in used}
    # 被有用骨引用的排除（如 Ear0_L_00 是耳朵父骨 -> 保留）。
    # 保护集覆盖 5 类引用：非空骨的 parent/tail/追加親(0x0100)/追加(0x0200) +
    # 所有 IK 功能骨的 target/links（无论自身是否空骨）。
    # ★ 両目 链路实证：両目 通过追加親(倍率0.0898) 控制 Eye_L/Eye_R（权重23）；
    #   両目IK 是 IK 骨(flag&0x0020)，IK link=両目、IK target=両目IK先。
    #   这三者都无顶点权重=「空骨」，但両目IK 引用両目、両目IK先 必须保留，
    #   否则両目IK target 悬空 65535 -> MMD 解算 IK 越界崩溃（知识库 18.3）。
    useful_ref = set()
    for i in range(r.bone_count):
        if i not in empty:
            if parent[i] is not None and parent[i] < r.bone_count:
                useful_ref.add(parent[i])
            if tail[i] is not None and tail[i] < r.bone_count:
                useful_ref.add(tail[i])
            if add_parent[i] is not None and add_parent[i] < r.bone_count:
                useful_ref.add(add_parent[i])
            if add_bone[i] is not None and add_bone[i] < r.bone_count:
                useful_ref.add(add_bone[i])
        # ★ IK 功能骨引用保护：即使自身是空骨（両目IK 无顶点权重），
        #   其 IK target / IK links 也必须保留，否则 IK target 悬空 -> MMD 崩溃
        if ik_flag[i]:
            if ik_target[i] is not None and ik_target[i] < r.bone_count:
                useful_ref.add(ik_target[i])
            for lnk in ik_links[i]:
                if lnk < r.bone_count:
                    useful_ref.add(lnk)
    empty -= useful_ref
    # 互相引用：parent 或 tail 指向另一空骨（链式互连，如 Ear0_01/02）
    mutual = sorted(i for i in empty
                    if (parent[i] in empty) or (tail[i] in empty))
    return mutual


def clean_handle_bones(src):
    """处理单个PMX文件：删除 Handle 骨 + 引用重映射"""
    print(f"\n{'=' * 60}")
    print(f"处理文件: {os.path.basename(src)}")
    print(f"{'=' * 60}")

    r = PMXReader(src).parse()
    print(f"  顶点={r.vertex_count} 面={r.face_count} 纹理={r.texture_count} "
          f"材质={r.material_count} 骨骼={r.bone_count} Morph={r.morph_count} "
          f"帧={r.frame_count} 刚体={r.rigid_count} 约束={r.joint_count}")
    print(f"  剩余字节={r.remaining} (应为0)")

    if r.remaining != 0:
        print(f"  [ERROR] 解析未对齐(剩余{r.remaining}字节)，已跳过")
        return False

    # 1. 识别 Handle 骨 + 互相引用空骨骼
    handle_idx = [i for i, it in enumerate(r.bone_items) if it[0].rstrip().endswith('Handle')]
    print(f"  Handle 结尾骨骼: {len(handle_idx)} 个")
    empty_ref = find_empty_ref_bones(r)
    print(f"  互相引用空骨骼: {len(empty_ref)} 个")
    for i in empty_ref:
        print(f"    [{i}] {r.bone_items[i][0].rstrip()}  (name_g={r.bone_items[i][1].rstrip()})")
    to_delete = set(handle_idx) | set(empty_ref)

    # ★ 鞋带 _01（零长度末端节）处理：对齐 1033_2
    #   原始 1033 鞋带为 3 节：_00 → _01 → _01_Handle；删 Handle 后 _01 的 tail 置 None，
    #   变成零长度骨（视图里"接近 0,0"）。1033_2 只保留 _00，tail 指向足先EX（脚尖）。
    #   此处：删除 _01，并把 _00 的 tail 重映射到对应侧足先EX。
    tail_redirect = {}   # _00 骨索引 -> 足先EX 旧索引
    _names = [it[0].rstrip() for it in r.bone_items]
    _idx = {n: i for i, n in enumerate(_names)}
    for i, nm in enumerate(_names):
        m = re.match(r'^(Sp_An_Ribbon[01]_)([LR])_01$', nm)
        if not m:
            continue
        base = m.group(1) + m.group(2) + '_00'
        toe_name = 'Toe_offset_' + m.group(2)   # [8] 处理的是英文名原始模型（rename 前）
        if base in _idx and toe_name in _idx:
            tail_redirect[_idx[base]] = _idx[toe_name]
            to_delete.add(i)
    if tail_redirect:
        print(f"  鞋带 _01 处理: {len(tail_redirect)} 个 _00 tail 重映射到足先EX")

    if not to_delete:
        print("  无 Handle 骨且无互相引用空骨骼，无需处理")
        return False

    # 2. 紧凑映射
    compact = {}
    ni = 0
    for i in range(r.bone_count):
        if i not in to_delete:
            compact[i] = ni
            ni += 1
    print(f"  保留骨骼: {ni} 个 (删 {len(to_delete)})")

    # 3. 安全：顶点权重引用待删骨会崩坏网格 -> 拒绝处理
    w_del = [p for p in r.weight_points if p[1] in to_delete]
    if w_del:
        print(f"  [ERROR] {len(w_del)} 个顶点权重引用了待删骨，"
              f"删除会导致权重崩坏，已跳过")
        return False

    # 4. 重建骨骼区：删除 Handle 骨，保留骨按原顺序+原始字节紧凑排列
    out_bone = bytearray()
    out_bone.extend(ni.to_bytes(4, 'little'))
    item_new_off = []   # 每骨的新字段区起点（item_raw 起点，相对 out_bone 数据区）
    for i in range(r.bone_count):
        if i in to_delete:
            item_new_off.append(None)
            continue
        name_str, name_g_str, name_raw, name_g_raw, item_raw = r.bone_items[i]
        # ★ 弹簧骨(Sp_) 去移动：flag 0x001F -> 0x001B（去掉 0x0004 移动位）
        #   对齐 1033_2：弹簧骨只旋转不移动，否则套用 VMD 动画时会被移动付与/IK 拉动导致拉伸。
        if name_str.startswith('Sp_') or name_g_str.startswith('Sp_'):
            _ba = bytearray(item_raw)
            _foff = 12 + r.bidx + 4          # flag 字段相对 item_raw 起点偏移
            _f = int.from_bytes(_ba[_foff:_foff + 2], 'little')
            if _f & 0x0004:
                _f &= ~0x0004
                _ba[_foff:_foff + 2] = _f.to_bytes(2, 'little')
                item_raw = bytes(_ba)
        out_bone.extend(len(name_raw).to_bytes(4, 'little', signed=True))
        out_bone.extend(name_raw)      # 原始字节（不含长度前缀）
        out_bone.extend(len(name_g_raw).to_bytes(4, 'little', signed=True))
        out_bone.extend(name_g_raw)
        item_new_off.append(len(out_bone))   # 记录 item_raw 起点
        out_bone.extend(item_raw)

    # 5. 骨骼区内部引用重映射（保留骨字段内）
    none_idx = (1 << (8 * r.bidx)) - 1
    tail_rel = 12 + r.bidx + 6          # tail 字段相对 item 起点偏移（位置12+parent+layer4+flag2）
    bone_ba = bytearray(out_bone)
    b_remap = 0
    b_to_none = 0
    for i in range(r.bone_count):
        if i in to_delete:
            continue
        base = item_new_off[i]
        for rel, old in r.bone_refs[i]:
            if old == none_idx:
                continue
            # ★ 鞋带 _00 的 tail 字段重定向到足先EX（而不是置 None）
            if i in tail_redirect and rel == tail_rel:
                new = compact.get(tail_redirect[i], none_idx)
                b_remap += 1
            elif old in to_delete:
                new = none_idx
                b_to_none += 1
            elif old in compact:
                new = compact[old]
                b_remap += 1
            else:
                raise ValueError(f'骨骼 {i} 字段引用越界索引 {old}')
            _write_idx(bone_ba, base + rel, new, r.bidx)
    new_bone_bytes = bytes(bone_ba)

    # 6. 其余区引用点改写
    vertex_out, vs = _remap_points(r.vertex_bytes, r.weight_points, r.vertex_start,
                                   r.bidx, compact, to_delete)
    morph_out, ms = _remap_points(r.morph_bytes, r.morph_bone_points, r.morph_start,
                                  r.bidx, compact, to_delete)
    frame_out, fs = _remap_points(r.frame_bytes, r.frame_bone_points, r.frame_start,
                                  r.bidx, compact, to_delete)
    rigid_out, rs = _remap_points(r.rigid_bytes, r.rigid_bone_points, r.rigid_start,
                                  r.bidx, compact, to_delete)

    print(f"  引用重映射: 顶点权重={vs['remap']} (删骨->None={vs['handle_to_none']}) "
          f"骨骼字段={b_remap} (删骨->None={b_to_none}) "
          f"Morph={ms['remap']}({ms['handle_to_none']}) "
          f"帧={fs['remap']}({fs['handle_to_none']}) "
          f"刚体={rs['remap']}({rs['handle_to_none']})")

    # 7. 组装输出
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
    out.extend(new_bone_bytes)
    out.extend(r.morph_count.to_bytes(4, 'little'))
    out.extend(morph_out)
    out.extend(r.frame_count.to_bytes(4, 'little'))
    out.extend(frame_out)
    out.extend(r.rigid_count.to_bytes(4, 'little'))
    out.extend(rigid_out)
    out.extend(r.joint_count.to_bytes(4, 'little'))
    out.extend(r.joint_bytes)
    data = bytes(out)

    # 8. 写回 + 验证
    with open(src, 'wb') as f:
        f.write(data)
    v = PMXReader(src).parse()
    assert v.remaining == 0, f"验证失败：剩余字节={v.remaining}"
    assert v.bone_count == ni, f"验证失败：骨骼数 {v.bone_count} != {ni}"
    h_remain = [it[0] for it in v.bone_items if it[0].rstrip().endswith('Handle')]
    assert not h_remain, f"验证失败：仍有Handle骨 {h_remain}"
    v_names = [it[0].rstrip() for it in v.bone_items]
    v_name_gs = [it[1].rstrip() for it in v.bone_items]
    del_names = {r.bone_items[i][0].rstrip() for i in to_delete}
    leftover = del_names & set(v_names)
    assert not leftover, f"验证失败：仍有待删骨 {leftover}"
    print(f"  [OK] 删除成功并验证通过 (骨骼 {r.bone_count}->{ni}, "
          f"删Handle {len(handle_idx)}+空骨{len(empty_ref)}, 剩余字节=0)")
    return True





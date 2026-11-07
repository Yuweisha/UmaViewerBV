# -*- coding: utf-8 -*-
"""
[10] 骨骼索引排序 —— 仿 merge_morphs 架构（在[9]骨骼重命名之后执行，匹配日语名）
===========================================
依据 1033 -> 1033_2 的骨骼顺序变化（分区排序结构）：
  1033_2 = 前部物理/控制骨 + 腿足 + 躯干 + 耳朵 + 头发弹簧 + 手臂/手指
           + 胸口弹簧 + 尾巴 + 发带/鞋带弹簧 + 裙子弹簧 + 脸颊
           + 表情-眼眉 + 表情-嘴齿舌 + 末尾(Nose/Chin/M_Line00)

实现（用户建议）：
  - 内置分区术语表 BONE_PARTITIONS（英文为主、日文兼容）
  - 从最后一个分区到第一个分区，多次"把检测到的骨骼索引提到开头"
  - 每分区内保持骨骼原相对顺序，最终形成分区排序结构
  - Handle 骨先去除 _Handle 后缀用基名匹配，跟随主骨所在分区
  - ★ Sp_ 未命名骨：^Sp_ 兜底分区，置于末尾特殊(隐藏骨 Nose/Chin/M_Line00)之前
  - 未匹配任何分区的"多余骨骼"排到末尾（保持原相对顺序）

与 merge_morphs 相同的：
  - PMXReader/PMXWriter 式分段解析，非目标区原始字节保留
  - main(): exe-aware 工作目录 -> glob *.pmx -> [10]备份_时间戳/ 备份
            -> 逐文件处理 -> 写回验证 -> 回车退出
"""
import struct
import os
import sys
import glob
import shutil
import datetime


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
        """返回 (解码名, 原始字节)"""
        n = self.ri(4, signed=True)
        raw = self.rb(n)
        return decode_text(raw), raw

    def parse(self):
        # ===== 头部 =====
        self.rb(4)
        self.version = self.rf()
        self.ri(1, False)
        self.encoding = self.ri(1, False)
        self.uv_count = self.ri(1, False)
        self.vidx = self.ri(1, False)
        self.tidx = self.ri(1, False)
        self.midx = self.ri(1, False)
        self.bidx = self.ri(1, False)
        self.moidx = self.ri(1, False)
        self.ridx = self.ri(1, False)
        self.header_bytes = self.data[0:self.pos]

        # ===== 模型信息 =====
        info_start = self.pos
        self.model_name, _ = self.rtext()
        self.model_name_g, _ = self.rtext()
        self.comment, _ = self.rtext()
        self.comment_g, _ = self.rtext()
        self.info_bytes = self.data[info_start:self.pos]

        # ===== 顶点（收集权重骨骼索引） =====
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
            self.rf()
        self.vertex_bytes = self.data[self.vertex_start:self.pos]

        # ===== 面（自动识别 1/3 索引每面） =====
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

        # ===== 纹理 =====
        self.texture_count = self.ri(4, False)
        tex_start = self.pos
        for _ in range(self.texture_count):
            self.rtext()
        self.tex_bytes = self.data[tex_start:self.pos]

        # ===== 材质 =====
        self.material_count = self.ri(4, False)
        mat_start = self.pos
        for _ in range(self.material_count):
            self.rtext(); self.rtext()
            self.pos += 16 + 12 + 4 + 12
            self.ri(1, False)
            self.pos += 16 + 4
            self.ri(self.tidx)
            self.ri(self.tidx)
            self.ri(1, False)
            toon_flag = self.ri(1, False)
            if toon_flag == 0:
                self.ri(self.tidx)
            else:
                self.ri(1)
            self.rtext()
            self.ri(4, False)
        self.mat_bytes = self.data[mat_start:self.pos]

        # ===== 骨骼（完整解析：条目原始字节 + 内部引用点 + 名称） =====
        self.bone_count = self.ri(4, False)
        self.bone_start = self.pos
        self.bone_items = []   # [(name_str, name_g_str, name_raw, name_g_raw, item_raw)]
        self.bone_refs = []    # [每骨: [(字段相对item起点偏移, 旧骨骼索引)]]
        for _ in range(self.bone_count):
            name_str, name_raw = self.rtext()
            name_g_str, name_g_raw = self.rtext()
            item_start = self.pos
            refs = []
            self.rvec(3)
            refs.append((self.pos - item_start, self.ri(self.bidx)))   # 親骨
            self.ri(4, False)
            flag = self.ri(2, False)
            if flag & 0x0001:
                refs.append((self.pos - item_start, self.ri(self.bidx)))
            else:
                self.rvec(3)
            if flag & 0x0100:
                refs.append((self.pos - item_start, self.ri(self.bidx)))
                self.rf()
            if flag & 0x0200:
                refs.append((self.pos - item_start, self.ri(self.bidx)))
                self.rf()
            if flag & 0x0400:
                self.rvec(3)
            if flag & 0x0800:
                self.rvec(3); self.rvec(3)
            if flag & 0x2000:
                self.ri(4)
            if flag & 0x0020:
                refs.append((self.pos - item_start, self.ri(self.bidx)))  # IK target
                self.ri(4, False); self.rf()
                ic = self.ri(4, False)
                for _ in range(ic):
                    refs.append((self.pos - item_start, self.ri(self.bidx)))
                    if self.ri(1, False):
                        self.rvec(3); self.rvec(3)
            item_raw = self.data[item_start:self.pos]
            self.bone_items.append((name_str, name_g_str, name_raw, name_g_raw, item_raw))
            self.bone_refs.append(refs)
        self.bone_bytes = self.data[self.bone_start:self.pos]

        # ===== Morph（收集 type=2 骨骼索引） =====
        self.morph_count = self.ri(4, False)
        self.morph_start = self.pos
        self.morph_bone_points = []
        for _ in range(self.morph_count):
            self.rtext(); self.rtext()
            self.ri(1, False)
            mtype = self.ri(1, False)
            oc = self.ri(4, False)
            for _ in range(oc):
                t = mtype
                if t == 0 or t == 9:
                    self.ri(self.moidx); self.rf()
                elif t == 1:
                    self.ri(self.vidx); self.rvec(3)
                elif t == 2:
                    self.morph_bone_points.append((self.pos, self.ri(self.bidx)))
                    self.rvec(3); self.rvec(4)
                elif t in (3, 4, 5, 6, 7):
                    self.ri(self.vidx); self.rvec(4)
                elif t == 8:
                    self.ri(self.midx); self.ri(1, False); self.pos += 112
                elif t == 10:
                    self.ri(self.ridx); self.ri(1, False); self.rvec(3); self.rvec(3)
        self.morph_bytes = self.data[self.morph_start:self.pos]

        # ===== 显示帧（收集骨骼条目） =====
        self.frame_count = self.ri(4, False)
        self.frame_start = self.pos
        self.frame_bone_points = []
        for _ in range(self.frame_count):
            self.rtext(); self.rtext()
            self.ri(1, False)
            ic = self.ri(4, False)
            for _ in range(ic):
                it = self.ri(1, False)
                if it == 0:
                    self.frame_bone_points.append((self.pos, self.ri(self.bidx)))
                else:
                    self.ri(self.moidx)
        self.frame_bytes = self.data[self.frame_start:self.pos]

        # ===== 刚体（收集绑定骨骼） =====
        self.rigid_count = self.ri(4, False)
        self.rigid_start = self.pos
        self.rigid_bone_points = []
        for _ in range(self.rigid_count):
            self.rtext(); self.rtext()
            self.rigid_bone_points.append((self.pos, self.ri(self.bidx)))
            self.ri(1, False); self.ri(2, False); self.ri(1, False)
            self.rvec(3); self.rvec(3); self.rvec(3)
            self.pos += 20
            self.ri(1, False)
        self.rigid_bytes = self.data[self.rigid_start:self.pos]

        # ===== 约束 =====
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

# ================= 分区术语表（顺序 = 目标顺序，前 -> 后） =================
# 参照 1033_2 骨骼顺序归纳；英文为主、日文兼容（正则子串/精确匹配）
BONE_PARTITIONS = [
    ('物理根/控制', [
        r'^view cnt$', r'^操作中心$', r'^センター$', r'^グルーブ$', r'^groove$',
        r'^Position$', r'^腰$', r'^腰キャンセル', r'IKP', r'IK親', r'leg IK', r'toe IK', r'足IK',
    ]),
    ('腿足', [
        r'^Hip$', r'^下半身$', r'Thigh', r'Knee', r'Ankle', r'Toe',
        r'腿', r'膝', r'足首', r'つま先',
    ]),
    ('躯干', [
        r'^UpBody', r'^Waist$', r'^Spine$', r'^Chest$', r'^Neck$', r'^Head$',
        r'上半身', r'^首$', r'^頭$',
    ]),
    ('肩部控制', [
        r'shoulderC', r'shoulderP', r'左肩C', r'右肩C', r'^ShoulderP', r'^肩P',
    ]),
    ('耳朵', [
        r'Ear', r'耳',
    ]),
    ('头发弹簧', [
        r'^Sp_He_Hair', r'^Sp_He_头发', r'头发', r'^Sp_He_Hairband',
        r'髪', r'触覚', r'アホ毛',
    ]),
    ('手臂/手指', [
        r'Shoulder', r'Arm', r'Elbow', r'Roll', r'Wrist', r'Hand',
        r'^Index', r'^Middle', r'^Pinky', r'^Ring', r'^Thumb',
        r'Pole', r'Target', r'袖口', r'^Sp_El_', r'指',
        r'腕', r'ひじ', r'手首', r'肩', r'捩', r'ダミー',
    ]),
    ('胸口弹簧', [
        r'^Sp_Ch_Bust', r'^Sp_Ch_Chest', r'^Sp_Ch_胸口', r'^Sp_Ch_项链',
        r'^Sp_Ch_Necklace', r'^Sp_Ch_Tie', r'Bust', r'胸口', r'项链',
    ]),
    ('尾巴控制', [
        r'Tail_Ctrl', r'尻尾親',
    ]),
    ('尾巴', [
        r'Tail', r'尾巴',
    ]),
    ('发带/鞋带弹簧', [
        r'^Sp_He_发带', r'^Sp_An_鞋带', r'^Sp_An_Ribbon', r'^Sp_He_Acce',
        r'Ribbon', r'鞋带', r'发带',
    ]),
    ('裙子弹簧', [
        r'Skirt', r'裙子',
    ]),
    ('脸颊', [
        r'Cheek', r'頬',
    ]),
    ('表情-眼眉', [
        r'Eye', r'Eyebrow', r'Eyelash', r'^Head_attach', r'^Head_height',
        r'^Head_shade', r'目', r'眉', r'まつげ',
    ]),
    ('表情-嘴齿舌', [
        r'Mouth', r'Tongue', r'Tooth', r'口', r'舌', r'歯',
    ]),
    ('弹簧(Sp_未命名)', [
        r'^Sp_',
    ]),
    ('末尾特殊', [
        r'^Nose', r'^Chin', r'^M_Line', r'鼻', r'あご',
    ]),
]


def _strip_handle(name):
    """去除 Handle 后缀（含双层 _Handle_Handle），返回基名"""
    n = name
    while n.rstrip().endswith('Handle'):
        n = n[: -len('Handle')].rstrip('_')
    return n


def match_partition(name, pats):
    """匹配分区：先试原名，再试去 Handle 后缀的基名（Handle 骨跟随主骨分区）"""
    for nm in (name, _strip_handle(name)):
        for p in pats:
            try:
                if __import__('re').search(p, nm):
                    return True
            except Exception:
                if p in nm:
                    return True
    return False


def bone_matches(name, name_g, pats):
    """对 name 与 name_g 都尝试匹配（英文模型用 name，双语模型用 name_g）"""
    for nm in (name, name_g):
        if nm and match_partition(nm, pats):
            return True
    return False


def partition_sort(indices, names, name_gs):
    """核心排序算法：从最后一个分区到第一个分区，多次把匹配骨提到开头；
    分区内保持原相对顺序；未匹配的"多余骨骼"保持原顺序排末尾。
    返回 (新顺序索引列表, 各分区统计, 未匹配索引列表)。"""
    remaining = list(indices)
    stats = []
    for pname, pats in reversed(BONE_PARTITIONS):
        picked = [i for i in remaining if bone_matches(names[i], name_gs[i], pats)]
        if picked:
            remaining = picked + [i for i in remaining if i not in picked]
        stats.append((pname, len(picked)))
    stats.reverse()
    all_pats = [pats for _, pats in BONE_PARTITIONS]
    unmatched = [i for i in remaining
                 if not any(bone_matches(names[i], name_gs[i], pats) for pats in all_pats)]
    return remaining, stats, unmatched


# ================= 排序处理 =================
def _write_idx(ba, off, val, size):
    v = val & 0xFFFFFFFF
    for k in range(size):
        ba[off + k] = (v >> (8 * k)) & 0xFF


def _remap_points(data_bytes, points, section_start, bidx, old_to_new):
    """把引用点 (abs_off, old_idx) 改写为 old_to_new[old_idx]。返回 (bytes, 改写数)。"""
    none_idx = (1 << (8 * bidx)) - 1
    ba = bytearray(data_bytes)
    cnt = 0
    for abs_off, old in points:
        rel = abs_off - section_start
        if old == none_idx:
            continue
        if old not in old_to_new:
            raise ValueError(f'无法映射骨骼索引 {old}')
        _write_idx(ba, rel, old_to_new[old], bidx)
        cnt += 1
    return bytes(ba), cnt


def sort_bones_file(src):
    """处理单个PMX文件：按分区术语表重排骨骼索引 + 全量引用重映射"""
    print(f"\n{'=' * 70}")
    print(f"处理文件: {os.path.basename(src)}")
    print(f"{'=' * 70}")

    r = PMXReader(src).parse()
    print(f"  顶点={r.vertex_count} 面={r.face_count} 纹理={r.texture_count} "
          f"材质={r.material_count} 骨骼={r.bone_count} Morph={r.morph_count} "
          f"帧={r.frame_count} 刚体={r.rigid_count} 约束={r.joint_count}")
    print(f"  剩余字节={r.remaining} (应为0)")

    if r.remaining != 0:
        print(f"  [ERROR] 解析未对齐(剩余{r.remaining}字节)，已跳过")
        return False

    names = [it[0].rstrip() for it in r.bone_items]
    name_gs = [it[1].rstrip() for it in r.bone_items]
    print(f"  骨骼 {r.bone_count} 个")

    # 1. 分区排序
    new_order, stats, unmatched = partition_sort(list(range(r.bone_count)), names, name_gs)
    old_to_new = {old: new for new, old in enumerate(new_order)}   # 旧索引 -> 新索引

    # 1.5 安全守卫：引用越界检查（数据损坏模型拒绝处理）
    for label, pts in (('顶点权重', r.weight_points), ('骨骼Morph', r.morph_bone_points),
                       ('显示帧', r.frame_bone_points), ('刚体', r.rigid_bone_points)):
        bad = [b for _p, b in pts if b >= r.bone_count]
        if bad:
            print(f"  [ERROR] {label}含 {len(bad)} 个越界骨骼索引(首例 {bad[0]})，"
                  f"模型数据异常，已跳过")
            return False

    print(f"\n  --- 分区匹配统计 (目标顺序) ---")
    for pname, cnt in stats:
        print(f"    {pname:<10}: {cnt:>3} 个骨")
    print(f"    未匹配(排末尾) : {len(unmatched):>3} 个骨")

    # 2. 重建骨骼区（按新顺序）
    out_bone = bytearray()
    out_bone.extend(r.bone_count.to_bytes(4, 'little'))
    item_new_off = {}   # 新索引 -> item_raw 起点
    for new_pos, old_idx in enumerate(new_order):
        name_str, name_g_str, name_raw, name_g_raw, item_raw = r.bone_items[old_idx]
        out_bone.extend(len(name_raw).to_bytes(4, 'little', signed=True))
        out_bone.extend(name_raw)
        out_bone.extend(len(name_g_raw).to_bytes(4, 'little', signed=True))
        out_bone.extend(name_g_raw)
        item_new_off[new_pos] = len(out_bone)
        out_bone.extend(item_raw)

    # 3. 骨骼区内部引用重映射（新顺序下 patch）
    bone_ba = bytearray(out_bone)
    b_cnt = 0
    for new_pos, old_idx in enumerate(new_order):
        base = item_new_off[new_pos]
        for rel, old in r.bone_refs[old_idx]:
            none_idx = (1 << (8 * r.bidx)) - 1
            if old == none_idx:
                continue
            if old not in old_to_new:
                raise ValueError(f'骨骼 {old_idx} 字段引用越界索引 {old}')
            _write_idx(bone_ba, base + rel, old_to_new[old], r.bidx)
            b_cnt += 1
    new_bone_bytes = bytes(bone_ba)

    # 4. 其余区引用点改写
    vertex_out, v_cnt = _remap_points(r.vertex_bytes, r.weight_points, r.vertex_start,
                                      r.bidx, old_to_new)
    morph_out, m_cnt = _remap_points(r.morph_bytes, r.morph_bone_points, r.morph_start,
                                     r.bidx, old_to_new)
    frame_out, f_cnt = _remap_points(r.frame_bytes, r.frame_bone_points, r.frame_start,
                                     r.bidx, old_to_new)
    rigid_out, g_cnt = _remap_points(r.rigid_bytes, r.rigid_bone_points, r.rigid_start,
                                     r.bidx, old_to_new)
    print(f"  引用重映射: 顶点权重={v_cnt} 骨骼字段={b_cnt} Morph={m_cnt} "
          f"帧={f_cnt} 刚体={g_cnt}")

    # 5. 控制台输出索引变化细节（前60个）
    print(f"\n  --- 索引重排明细 (旧->新, 前60) ---")
    moved = 0
    shown = 0
    for new_pos, old_idx in enumerate(new_order):
        if old_idx != new_pos:
            moved += 1
        if shown < 60:
            flag = ' *' if old_idx != new_pos else ''
            print(f"    [{old_idx:>3} -> {new_pos:>3}] {names[old_idx]}{flag}")
            shown += 1
    print(f"    ... (共{len(new_order)}骨, 其中位置变化 {moved} 骨)")

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

    # 7. 写回 + 验证
    with open(src, 'wb') as f:
        f.write(data)
    v = PMXReader(src).parse()
    assert v.remaining == 0, f"验证失败：剩余字节={v.remaining}"
    assert v.bone_count == r.bone_count, f"验证失败：骨骼数变化 {r.bone_count}->{v.bone_count}"
    new_names = [it[0].rstrip() for it in v.bone_items]
    assert sorted(new_names) == sorted(names), "验证失败：骨骼名集合变化"
    print(f"  [OK] 骨骼排序成功并验证通过 (剩余字节=0, 骨骼名集合不变, 位置变化 {moved} 骨)")
    return True





# __MORE__

# __MORE__

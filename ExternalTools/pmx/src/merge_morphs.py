# -*- coding: utf-8 -*-
"""
合并PMX模型的表情(Face)与眉毛(Mayu)同名Morph
逻辑: "同一種類モーフを結合(上位优先)" —— 将 [M_Mayu] 合并进 [M_Face]

实现策略：
- 除 Morph 区外，顶点/面/纹理/材质/骨骼/显示帧/刚体/约束 全部按原始字节保留
- 只完整解析并重建 Morph 区
- 合并时：同名(去掉后缀标签)配对，重复顶点Index保留上位(Face)值，新顶点Index追加
"""
import struct
import os
import sys
import glob
import shutil
import datetime


class PMXReader:
    def __init__(self, filepath):
        with open(filepath, 'rb') as f:
            self.data = f.read()
        self.pos = 0

    def rb(self, n):
        d = self.data[self.pos:self.pos+n]
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

    def parse(self):
        # ===== Header =====
        self.rb(4)  # magic "PMX "
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

        # ===== 模型信息(原始字节保留) =====
        info_start = self.pos
        self.model_name = self.rtext()
        self.model_name_global = self.rtext()
        self.comment = self.rtext()
        self.comment_global = self.rtext()
        self.info_bytes = self.data[info_start:self.pos]

        # ===== 顶点(原始字节保留) =====
        vcount = self.ri(4, False)
        vstart = self.pos
        for i in range(vcount):
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
        self.vertex_count = vcount
        self.vertex_bytes = self.data[vstart:self.pos]

        # ===== 面(原始字节保留) =====
        fcount = self.ri(4, False)
        fstart = self.pos
        self.pos += fcount * self.vidx
        self.face_count = fcount
        self.face_bytes = self.data[fstart:self.pos]

        # ===== 纹理(原始字节保留) =====
        tcount = self.ri(4, False)
        tstart = self.pos
        for _ in range(tcount):
            self.rtext()
        self.texture_count = tcount
        self.texture_bytes = self.data[tstart:self.pos]

        # ===== 材质(按PMX规范正确跳过, 原始字节保留) =====
        mcount = self.ri(4, False)
        mstart = self.pos
        for _ in range(mcount):
            self.rtext()  # 材質名
            self.rtext()  # 材質名英
            self.pos += 16 + 12 + 4 + 12  # Diffuse+Specular+Specular係数+Ambient
            self.ri(1, False)  # 描画フラグ
            self.pos += 16 + 4  # エッジ色 + エッジサイズ
            self.ri(self.tidx)  # 通常テクスチャIndex
            self.ri(self.tidx)  # スフィアテクスチャIndex
            self.ri(1, False)   # スフィアモード
            toon_flag = self.ri(1, False)  # 共有Toonフラグ
            if toon_flag == 0:
                self.ri(self.tidx)  # 個別ToonテクスチャIndex
            else:
                self.ri(1)  # 共有Toon[0~9]
            self.rtext()  # メモ
            self.ri(4, False)  # 面数
        self.material_count = mcount
        self.material_bytes = self.data[mstart:self.pos]

        # ===== 骨骼(原始字节保留) =====
        bcount = self.ri(4, False)
        bstart = self.pos
        for _ in range(bcount):
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
        self.bone_count = bcount
        self.bone_bytes = self.data[bstart:self.pos]

        # ===== Morph(完整解析) =====
        mrcount = self.ri(4, False)
        self.morphs = []
        for i in range(mrcount):
            mo = {}
            mo['name'] = self.rtext()
            mo['name_g'] = self.rtext()
            mo['panel'] = self.ri(1, False)
            mo['type'] = self.ri(1, False)
            mo['offset_count'] = self.ri(4, False)
            mo['offsets'] = []
            for _ in range(mo['offset_count']):
                off = {}
                t = mo['type']
                if t == 0 or t == 9:  # group / flip
                    off['index'] = self.ri(self.moidx)
                    off['value'] = [self.rf()]
                elif t == 1:  # 顶点
                    off['index'] = self.ri(self.vidx)
                    off['value'] = self.rvec(3)
                elif t == 2:  # 骨骼
                    off['index'] = self.ri(self.bidx)
                    off['move'] = self.rvec(3)
                    off['rot'] = self.rvec(4)
                elif t in (3, 4, 5, 6, 7):  # UV / 追加UV1~4
                    off['index'] = self.ri(self.vidx)
                    off['value'] = self.rvec(4)
                elif t == 8:  # 材质
                    off['index'] = self.ri(self.midx)
                    off['op'] = self.ri(1, False)
                    off['diffuse'] = self.rvec(4)
                    off['specular'] = self.rvec(3)
                    off['spec_strength'] = self.rf()
                    off['ambient'] = self.rvec(3)
                    off['edge_color'] = self.rvec(4)
                    off['edge_size'] = self.rf()
                    off['tex'] = self.rvec(4)
                    off['sphere'] = self.rvec(4)
                    off['toon'] = self.rvec(4)
                elif t == 10:  # 冲动
                    off['index'] = self.ri(self.ridx)
                    off['local'] = self.ri(1, False)
                    off['velocity'] = self.rvec(3)
                    off['torque'] = self.rvec(3)
                mo['offsets'].append(off)
            self.morphs.append(mo)
        self.morph_count = mrcount

        # ===== 显示帧(原始字节保留) =====
        frcount = self.ri(4, False)
        frstart = self.pos
        for _ in range(frcount):
            self.rtext(); self.rtext()
            self.ri(1, False)
            ic = self.ri(4, False)
            for _ in range(ic):
                it = self.ri(1, False)
                if it == 0:
                    self.ri(self.bidx)
                else:
                    self.ri(self.moidx)
        self.frame_count = frcount
        self.frame_bytes = self.data[frstart:self.pos]

        # ===== 刚体(原始字节保留) =====
        rgcount = self.ri(4, False)
        rgstart = self.pos
        for _ in range(rgcount):
            self.rtext(); self.rtext()
            self.ri(self.bidx)
            self.ri(1, False)
            self.ri(2, False)
            self.ri(1, False)
            self.rvec(3); self.rvec(3); self.rvec(3)
            self.pos += 4 * 5  # 质量/线性阻尼/角阻尼/反弹/摩擦
            self.ri(1, False)
        self.rigid_count = rgcount
        self.rigid_bytes = self.data[rgstart:self.pos]

        # ===== 约束(原始字节保留) =====
        jtcount = self.ri(4, False)
        jtstart = self.pos
        for _ in range(jtcount):
            self.rtext(); self.rtext()
            self.ri(1, False)
            self.ri(self.ridx); self.ri(self.ridx)
            self.rvec(3); self.rvec(3)  # 位置 + 旋转
            self.pos += 72  # 移动/旋转上下限 + 弹簧移动/旋转 (6个float3)
        self.joint_count = jtcount
        self.joint_bytes = self.data[jtstart:self.pos]

        self.remaining = len(self.data) - self.pos
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

    def wtext(self, s):
        # Keep the original encoding declaration and text bytes consistent.
        raw = s.encode('utf-16-le' if self.m.encoding == 0 else 'utf-8')
        self.wi(len(raw), 4)
        self.out.extend(raw)

    def wvec(self, v):
        for x in v:
            self.wf(x)

    def build(self):
        m = self.m
        # Header
        self.w(b'PMX ')
        self.wf(m.version)
        self.wi(8, 1)             # header_size
        self.wi(m.encoding, 1)
        self.wi(m.uv_count, 1)
        self.wi(m.vidx, 1); self.wi(m.tidx, 1); self.wi(m.midx, 1)
        self.wi(m.bidx, 1); self.wi(m.moidx, 1); self.wi(m.ridx, 1)

        # 模型信息(原样)
        self.out.extend(m.info_bytes)

        # 顶点(原样)
        self.wi(m.vertex_count, 4)
        self.out.extend(m.vertex_bytes)

        # 面(原样)
        self.wi(m.face_count, 4)
        self.out.extend(m.face_bytes)

        # 纹理(原样)
        self.wi(m.texture_count, 4)
        self.out.extend(m.texture_bytes)

        # 材质(原样)
        self.wi(m.material_count, 4)
        self.out.extend(m.material_bytes)

        # 骨骼(原样)
        self.wi(m.bone_count, 4)
        self.out.extend(m.bone_bytes)

        # Morph(重建)
        self.wi(len(m.morphs), 4)
        for mo in m.morphs:
            self.wtext(mo['name'])
            self.wtext(mo['name_g'])
            self.wi(mo['panel'], 1)
            self.wi(mo['type'], 1)
            self.wi(len(mo['offsets']), 4)
            for off in mo['offsets']:
                t = mo['type']
                if t == 0 or t == 9:
                    self.wi(off['index'], m.moidx)
                    self.wf(off['value'][0])
                elif t == 1:
                    self.wi(off['index'], m.vidx)
                    self.wvec(off['value'])
                elif t == 2:
                    self.wi(off['index'], m.bidx)
                    self.wvec(off['move'])
                    self.wvec(off['rot'])
                elif t in (3, 4, 5, 6, 7):
                    self.wi(off['index'], m.vidx)
                    self.wvec(off['value'])
                elif t == 8:
                    self.wi(off['index'], m.midx)
                    self.wi(off['op'], 1)
                    self.wvec(off['diffuse'])
                    self.wvec(off['specular'])
                    self.wf(off['spec_strength'])
                    self.wvec(off['ambient'])
                    self.wvec(off['edge_color'])
                    self.wf(off['edge_size'])
                    self.wvec(off['tex'])
                    self.wvec(off['sphere'])
                    self.wvec(off['toon'])
                elif t == 10:
                    self.wi(off['index'], m.ridx)
                    self.wi(off['local'], 1)
                    self.wvec(off['velocity'])
                    self.wvec(off['torque'])

        # 显示帧(原样)
        self.wi(m.frame_count, 4)
        self.out.extend(m.frame_bytes)

        # 刚体(原样)
        self.wi(m.rigid_count, 4)
        self.out.extend(m.rigid_bytes)

        # 约束(原样)
        self.wi(m.joint_count, 4)
        self.out.extend(m.joint_bytes)

        return bytes(self.out)


def merge_pmx_file(src):
    """合并单个PMX文件的面部(Face)与眉毛(Mayu)同名表情"""
    print(f"\n{'='*60}")
    print(f"处理文件: {os.path.basename(src)}")
    print(f"{'='*60}")

    # 解析
    reader = PMXReader(src).parse()
    print(f"  顶点={reader.vertex_count} 面={reader.face_count} 骨骼={reader.bone_count}")
    print(f"  Morph总数={reader.morph_count} 显示帧={reader.frame_count} 刚体={reader.rigid_count} 约束={reader.joint_count}")
    print(f"  剩余字节={reader.remaining} (应为0)")

    # 分类 face / mayu
    face_morphs = {}
    mayu_morphs = {}
    for mo in reader.morphs:
        name = mo['name']
        if '[M_Mayu]' in name:
            base = name.replace('[M_Mayu]', '').strip()
            mayu_morphs[base] = mo
        elif '[M_Face]' in name:
            base = name.replace('[M_Face]', '').strip()
            face_morphs[base] = mo

    print(f"  [M_Face] 数量: {len(face_morphs)}")
    print(f"  [M_Mayu] 数量: {len(mayu_morphs)}")
    common = set(face_morphs.keys()) & set(mayu_morphs.keys())
    print(f"  同名配对数量: {len(common)}")

    if len(common) == 0:
        print("  没有同名的Face/Mayu配对，跳过")
        return False

    merge_count = 0
    total_added = 0
    for base in sorted(common):
        face = face_morphs[base]
        mayu = mayu_morphs[base]
        existing = set(off['index'] for off in face['offsets'])
        added = 0
        for off in mayu['offsets']:
            if off['index'] not in existing:
                face['offsets'].append(off)
                existing.add(off['index'])
                added += 1
        mayu['_to_delete'] = True
        total_added += added
        if added > 0:
            merge_count += 1

    # 删除被合并的 mayu
    reader.morphs = [mo for mo in reader.morphs if not mo.get('_to_delete', False)]

    print(f"  实际合并 {merge_count} 个 Mayu->Face，共追加 {total_added} 个新offset")
    print(f"  合并后 Morph: {len(reader.morphs)} (删除 {reader.morph_count - len(reader.morphs)} 个 Mayu)")

    # 写回（覆盖原文件）
    writer = PMXWriter(reader)
    data = writer.build()
    with open(src, 'wb') as f:
        f.write(data)

    # 验证
    v = PMXReader(src).parse()
    remaining_mayu = [mo for mo in v.morphs if '[M_Mayu]' in mo['name']]
    assert v.remaining == 0, "验证失败：剩余字节不为0"
    assert len(remaining_mayu) == 0, "验证失败：仍有Mayu残留"
    print("  [OK] 合并成功并验证通过 (剩余字节=0, 剩余Mayu=0)")
    return True





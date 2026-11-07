# -*- coding: utf-8 -*-
"""
PMX 一键处理工作台（单 exe 版 v3）
==================================
- 文件流：Windows API 文件/文件夹对话框选择 模型(.pmx) 与 Texture2D 贴图文件夹
- ★ 导入逻辑（v3 修复）：选择模型文件后先**复制到临时工作区**
  （%TEMP%\\pmx_workflow\\session-*\\），后续 1~19 步全部在工作区副本上执行，
  源模型文件只读、永不被修改；"导出贴图和模型"时才把处理结果写入目标目录。
- 检测：模型必须恰为 1 个（工作区恒 1 个副本）、贴图文件夹必须存在
- 失败：步骤抛异常 → 丢弃损坏的工作区副本，从源文件重新复制（源文件只读）
- 状态：未处理 / 成功 / 失败 / 已导出
- 19 个步骤按钮 + 全自动处理 + 导出贴图和模型

运行：python workflow_gui.py
"""
import os
import sys
import io
import shutil
import subprocess
import datetime
import threading
import traceback
import importlib
import gc
# 供 src 动态模块在打包环境使用（PyInstaller 只收集静态 import，显式导入确保收集）
import glob
import struct
import math
import re
import copy
import collections
import tempfile

# ================= 源码路径定位（打包后 _MEIPASS/src；未打包为项目 src/） =================

# 临时/缓存目录：统一放 appdata\Temp\pmx_workflow，不在模型/贴图目录落盘
TEMP_ROOT = os.path.join(tempfile.gettempdir(), 'pmx_workflow')

def _is_frozen():
    return getattr(sys, 'frozen', False)


def get_src_dir():
    if _is_frozen():
        return os.path.join(sys._MEIPASS, 'src')
    return os.path.join(os.path.dirname(os.path.abspath(__file__)), 'src')


SRC_DIR = get_src_dir()
if SRC_DIR and os.path.isdir(SRC_DIR) and SRC_DIR not in sys.path:
    sys.path.insert(0, SRC_DIR)

# ================= 环境检测 =================

def check_imagemagick():
    try:
        flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
        r = subprocess.run(['magick', '-version'], capture_output=True,
                           timeout=15, creationflags=flags)
        return r.returncode == 0
    except Exception:
        return False


# ================= ImageMagick 辅助 =================

def _run_cmd(cmd, log):
    log('  $ ' + ' '.join(cmd))
    flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
    r = subprocess.run(cmd, capture_output=True, text=True,
                       encoding='utf-8', errors='replace', timeout=600,
                       creationflags=flags)
    if r.returncode != 0:
        raise RuntimeError('magick 命令失败: ' + (r.stderr or r.stdout or '').strip())
    return r


def mag(log, *args):
    _run_cmd(['magick'] + list(args), log)


def rm_files(*paths):
    for p in paths:
        try:
            if p and os.path.exists(p):
                os.remove(p)
        except Exception:
            pass


# ================= 上下文（当前模型 / 贴图目录） =================

class Ctx:
    """步骤执行的输入上下文：模型路径(工作区副本) + 源模型路径 + 贴图文件夹 + 导出目标 + 处理结果(temp)"""
    def __init__(self, model_path=None, tex_dir=None):
        self.model_path = model_path
        self.src_model_path = model_path   # 源模型路径（用户选择的原始文件，只读不修改）
        self.tex_dir = tex_dir
        self.out_dir = None       # 导出目标目录（全自动选择，不得与导入贴图文件夹相同）
        self.result_tex_dir = None  # 步骤1 处理结果的 Texture2D 目录（appdata\Temp 下，导出时移动到目标）
        self.tmp_dir = None       # 步骤临时缓存目录（appdata\Temp\pmx_workflow 下）

    @property
    def base_dir(self):
        return os.path.dirname(self.model_path) if self.model_path else None


# ================= 临时工作区（选择文件后复制模型副本，源文件只读） =================

os.makedirs(TEMP_ROOT, exist_ok=True)
WORKSPACE_ROOT = tempfile.mkdtemp(prefix='session-', dir=TEMP_ROOT)


def _clean_workspace():
    """清理临时工作区目录（选择新模型 / 程序退出时调用），保证源目录零残留"""
    try:
        if os.path.isdir(WORKSPACE_ROOT):
            shutil.rmtree(WORKSPACE_ROOT, ignore_errors=True)
    except Exception:
        pass


def import_to_workspace(src):
    """把源模型复制到临时工作区，返回 (工作区目录, 工作区模型路径)。

    后续所有步骤在返回的工作区模型上就地执行（各步骤脚本本来就是就地写回），
    因此**源模型文件永不被修改**；"导出贴图和模型"时才把工作区结果写入目标目录。"""
    _clean_workspace()
    os.makedirs(WORKSPACE_ROOT, exist_ok=True)
    dst = os.path.join(WORKSPACE_ROOT, os.path.basename(src))
    shutil.copy2(src, dst)
    return WORKSPACE_ROOT, dst


def reset_workspace(ctx):
    """步骤失败时：丢弃损坏的工作区副本，从源文件重新复制（源文件只读，天然安全）。
    返回新工作区模型路径（路径不变，文件内容被重置）。"""
    if not ctx.src_model_path:
        return ctx.model_path
    _work, new_model = import_to_workspace(ctx.src_model_path)
    ctx.model_path = new_model
    return new_model


# ================= 步骤1：ImageMagick 贴图处理（ctx 版） =================

# 多层衣装特例：base 前缀 -> diff/shad_c 前缀（简单衣装由 0_2↔2_2 规则自动覆盖）
BDY_BASE_TO_DIFF = {
    'tex_bdy0001_00_00_0_2_00': 'tex_bdy0001_00_00_2_2_07',   # 田径服主衣装
    'tex_bdy0001_00_zekken0_2': 'tex_bdy0001_00_zekken1_2',   # 田径服号码牌
}


def _get_size(path):
    r = subprocess.run(['magick', 'identify', '-format', '%w %h', path],
                       capture_output=True, text=True, timeout=30,
                       creationflags=subprocess.CREATE_NO_WINDOW)
    return tuple(map(int, r.stdout.split()))


def _should_resize(path):
    """专属 2048 大贴图才缩放 50%；通用衣装(1024/512)保持原尺寸"""
    try:
        w, h = _get_size(path)
        return max(w, h) >= 2048
    except Exception:
        return True


def _resolve_diff_prefix(tex_dir, base):
    """body 的 base 前缀 -> 对应 diff/shad_c 前缀（多级 fallback）"""
    if os.path.exists(os.path.join(tex_dir, base + '_diff.png')):   # 1) 同名
        return base
    cand = base.replace('_0_2', '_2_2')                             # 2) 通用衣装 0_2→2_2
    if cand != base and os.path.exists(os.path.join(tex_dir, cand + '_diff.png')):
        return cand
    return BDY_BASE_TO_DIFF.get(base, base)                         # 3) 多层衣装特例


def _needs_decode(tex_dir, base, diff_prefix):
    """base 尺寸 != diff 尺寸 才需要 R/G/B 解码（专属编码贴图，base 2048 > diff 1024）；
    通用衣装 base 与 diff 同尺寸，diff 已就绪、无需解码。"""
    b = os.path.join(tex_dir, base + '_base.png')
    d = os.path.join(tex_dir, diff_prefix + '_diff.png')
    if not (os.path.exists(b) and os.path.exists(d)):
        return True
    try:
        bw, bh = _get_size(b)
        dw, dh = _get_size(d)
        return (bw, bh) != (dw, dh)
    except Exception:
        return True


def _find_num_overlay(tex_dir, base):
    """找同衣装的 num 数字贴图（如 tex_bdy0001_00_num16.png）"""
    m = re.match(r'(tex_bdy\d+_)', base)
    if not m:
        return None
    prefix = m.group(1).lower()
    for root, _, files in os.walk(tex_dir):
        for fn in sorted(files):
            if fn.lower().startswith(prefix) and 'num' in fn.lower() and fn.lower().endswith('.png'):
                return os.path.join(root, fn)
    return None


def _overlay_num(log, out_dir, ws, diff_prefix, num_overlay):
    """把 num 数字放大并叠加到号码牌 diff：正面 + 反面两个数字"""
    diff_file = os.path.join(out_dir, diff_prefix + '_diff.png')
    num_tmp = os.path.join(ws, diff_prefix + '_with_num.png')
    num_resized = os.path.join(ws, diff_prefix + '_num_resized.png')
    mag(log, num_overlay, '-resize', '128x128', num_resized)
    mag(log, diff_file, num_resized, '-gravity', 'Center',
        '-geometry', '+76-156', '-compose', 'Over', '-composite', num_tmp)
    mag(log, num_tmp, num_resized, '-gravity', 'Center',
        '-geometry', '+80+96', '-compose', 'Over', '-composite', num_tmp)
    shutil.move(num_tmp, diff_file)


def step_tex(ctx, log):
    """步骤1：贴图处理（等价原 [1] bat：bdy/hair/face/tail/eye 分层合成）
    输入 ctx.tex_dir（导入贴图文件夹，**只读不修改，不输出回原输入文件夹**），
    处理结果输出到 appdata\\Temp\\pmx_workflow\\step1_xxx\\Texture2D 临时目录，
    点击"导出贴图和模型"时整体移动到目标文件夹。"""
    if not check_imagemagick():
        raise RuntimeError('ImageMagick 未安装或 magick 命令无效，请先安装 ImageMagick')
    if not ctx.tex_dir:
        raise RuntimeError('未选择 Texture2D 贴图文件夹')
    tex = ctx.tex_dir
    if not os.path.isdir(tex):
        raise RuntimeError(f'未检测到 Texture2D 贴图文件夹：{tex}')

    # 清理旧的步骤1 结果临时目录，新建工作目录（appdata\\Temp\\pmx_workflow 下）
    try:
        for old in glob.glob(os.path.join(TEMP_ROOT, 'step1_*')):
            shutil.rmtree(old, ignore_errors=True)
    except Exception:
        pass
    ts = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
    work_dir = os.path.join(TEMP_ROOT, f'step1_{os.getpid()}_{ts}')
    os.makedirs(work_dir, exist_ok=True)
    out = os.path.join(work_dir, 'Texture2D')
    os.makedirs(out, exist_ok=True)

    orig_tex_dir = ctx.tex_dir
    orig_tmp_dir = ctx.tmp_dir
    ctx.tmp_dir = work_dir
    generated = []
    try:
        pngs = []
        for root, _, files in os.walk(tex):
            for fn in sorted(files):
                if fn.lower().endswith('.png'):
                    pngs.append(os.path.join(root, fn))

        # 先复制源贴图到 out（保留未处理的贴图，如通用衣装已就绪的 diff、ctrl 等）
        for fp in pngs:
            shutil.copy2(fp, os.path.join(out, os.path.basename(fp)))

        for fp in pngs:
            name = os.path.splitext(os.path.basename(fp))[0]
            if 'all' in name.lower():
                continue
            if 'bdy' in name.lower() and name.endswith('_base'):
                base = name[:-5]
                dp = _resolve_diff_prefix(tex, base)
                if _needs_decode(tex, base, dp):
                    # 专属编码贴图：R/G/B 解码 + 缩放
                    generated += _process_base(ctx, log, fp, name,
                                               resize=_should_resize(fp), diff_prefix=dp)
                else:
                    # 通用衣装：diff 已就绪（已复制到 out），仅号码牌叠加数字
                    if 'zekken' in name.lower():
                        num = _find_num_overlay(tex, base)
                        if num:
                            _overlay_num(log, out, work_dir, dp, num)
                            log(f'  号码牌叠加数字: {os.path.basename(num)}')
            elif 'hair' in name.lower() and name.endswith('_base'):
                generated += _process_base(ctx, log, fp, name, resize=False)
            elif 'face' in name.lower() and name.endswith('_base'):
                generated += _process_face(ctx, log, fp, name)
            elif 'tail' in name.lower() and name.endswith('_base'):
                generated += _process_tail(ctx, log, fp, name)
            elif name.endswith('_eye0'):
                generated += _process_eye(ctx, log, fp, name)

        for g in generated:
            if os.path.exists(g):
                shutil.move(g, os.path.join(out, os.path.basename(g)))
    except Exception:
        shutil.rmtree(work_dir, ignore_errors=True)   # 失败清理半成品
        raise
    finally:
        ctx.tex_dir = orig_tex_dir
        ctx.tmp_dir = orig_tmp_dir
    ctx.result_tex_dir = out   # 处理结果（temp 临时目录），供导出时整体移动
    log(f'  贴图处理完成，生成 {len(generated)} 个文件到临时目录 {out}\n'
        f'  （导入贴图源文件夹未改动；点"导出贴图和模型"时整体移动到目标文件夹）')



def _process_base(ctx, log, fp, name, resize, diff_prefix=None):
    """bdy(_base 缩放50%) / hair(_base 不缩放)：R反转蒙版 + G滤色 + B蒙版合成
    diff_prefix: body 贴图的 diff/shad_c 前缀（若与 base 前缀不同，如通用衣装 0_2↔2_2）"""
    base = name[:-5]
    out_base = diff_prefix if diff_prefix else base
    log(f'  处理 {("bdy缩放" if resize else "hair")}: {name}' + (f' -> {out_base}_diff' if diff_prefix else ''))
    ws = ctx.tmp_dir or ctx.base_dir     # 临时中间文件放缓存目录
    r_inv = os.path.join(ws, base + '_R_inverted.png')
    g_png = os.path.join(ws, base + '_G.png')
    b_png = os.path.join(ws, base + '_B.png')
    layered_temp = os.path.join(ws, base + '_layered_temp.png')
    shad_temp = os.path.join(ws, base + '_shad_c_temp.png')
    layered_shad = os.path.join(ws, base + '_layered_with_shad_c.png')
    layered_g = os.path.join(ws, base + '_layered_with_G.png')
    diff_out = os.path.join(ws, out_base + '_diff.png')
    tmp = [r_inv, g_png, b_png, layered_temp, shad_temp, layered_shad, layered_g]
    try:
        if resize:
            resized = os.path.join(ws, base + '_base_resized.png')
            tmp.append(resized)
            mag(log, fp, '-resize', '50%', resized)
            src = resized
        else:
            src = fp
        mag(log, src, '-channel', 'R', '-separate', '-negate', r_inv)
        mag(log, src, '-channel', 'G', '-separate', g_png)
        mag(log, src, '-channel', 'B', '-separate', b_png)
        d = os.path.join(ctx.tex_dir, out_base + '_diff.png')
        if os.path.exists(d):
            shutil.copy2(d, layered_temp)
        else:
            shutil.copy2(r_inv, layered_temp)
        s = os.path.join(ctx.tex_dir, out_base + '_shad_c.png')
        if os.path.exists(s):
            mag(log, s, r_inv, '-compose', 'CopyOpacity', '-composite', shad_temp)
            mag(log, layered_temp, shad_temp, '-composite', layered_shad)
        else:
            shutil.copy2(layered_temp, layered_shad)
        mag(log, layered_shad, g_png, '-compose', 'Screen', '-composite', layered_g)
        mag(log, layered_g, b_png, '-compose', 'CopyOpacity', '-composite', diff_out)
        log(f'  完成: {out_base}_diff.png')
        return [diff_out]
    finally:
        rm_files(*tmp)
def _process_face(ctx, log, fp, name):
    """face(_base)：R反转蒙版 + shad_c 合成"""
    base = name[:-5]
    log(f'  处理 face: {name}')
    ws = ctx.tmp_dir or ctx.base_dir     # 临时中间文件放缓存目录
    r_inv = os.path.join(ws, base + '_R_inverted.png')
    layered_temp = os.path.join(ws, base + '_layered_temp.png')
    shad_temp = os.path.join(ws, base + '_shad_c_temp.png')
    diff_out = os.path.join(ws, base + '_diff.png')
    try:
        mag(log, fp, '-channel', 'R', '-separate', '-negate', r_inv)
        d = os.path.join(ctx.tex_dir, base + '_diff.png')
        if os.path.exists(d):
            shutil.copy2(d, layered_temp)
        else:
            shutil.copy2(r_inv, layered_temp)
        s = os.path.join(ctx.tex_dir, base + '_shad_c.png')
        if os.path.exists(s):
            mag(log, s, r_inv, '-compose', 'CopyOpacity', '-composite', shad_temp)
            mag(log, layered_temp, shad_temp, '-composite', diff_out)
        else:
            shutil.copy2(layered_temp, diff_out)
        log(f'  完成: {base}_diff.png')
        return [diff_out]
    finally:
        rm_files(r_inv, shad_temp, layered_temp)


def _process_tail(ctx, log, fp, name):
    """tail(_base)：为每个 *tail*_diff.png 用 base 的 R反转作蒙版合成"""
    base = name[:-5]
    log(f'  处理 tail base: {name}')
    ws = ctx.tmp_dir or ctx.base_dir     # 临时中间文件放缓存目录
    diff_files = []
    for root, _, files in os.walk(ctx.tex_dir):
        for fn in files:
            if 'tail' in fn.lower() and fn.lower().endswith('_diff.png'):
                diff_files.append(os.path.join(root, fn))
    generated = []
    for df in sorted(diff_files):
        other = os.path.splitext(os.path.basename(df))[0][:-5]
        log(f'  对应 diff: {os.path.basename(df)}')
        r_inv = os.path.join(ws, base + '_R_inverted.png')
        layered_temp = os.path.join(ws, other + '_layered_temp.png')
        shad_temp = os.path.join(ws, base + '_shad_c_temp.png')
        diff_out = os.path.join(ws, other + '_diff.png')
        try:
            mag(log, fp, '-channel', 'R', '-separate', '-negate', r_inv)
            shutil.copy2(df, layered_temp)
            s = os.path.join(ctx.tex_dir, other + '_shad_c.png')
            if os.path.exists(s):
                mag(log, s, r_inv, '-compose', 'CopyOpacity', '-composite', shad_temp)
                mag(log, layered_temp, shad_temp, '-composite', diff_out)
            else:
                shutil.move(layered_temp, diff_out)
            log(f'  完成: {other}_diff.png')
            generated.append(diff_out)
        finally:
            rm_files(r_inv, shad_temp, layered_temp)
    if not diff_files:
        log(f'  警告: 未找到 {base} 对应的 _diff 文件')
    return generated


def _process_eye(ctx, log, fp, name):
    """eye0：叠加 eyehi00/01/02（North/South Screen）输出同名 .png"""
    log(f'  处理 eye0: {name}')
    ws = ctx.tmp_dir or ctx.base_dir     # 临时中间文件放缓存目录
    temp = os.path.join(ws, name + '_temp.png')
    out_png = os.path.join(ws, name + '.png')
    try:
        shutil.copy2(fp, temp)
        for i in ('00', '01', '02'):
            eyehi = os.path.join(ctx.tex_dir, name.replace('eye0', 'eyehi' + i) + '.png')
            if os.path.exists(eyehi):
                mag(log, temp, eyehi, '-gravity', 'North', '-compose', 'Screen', '-composite', temp)
        for i in ('00', '01', '02'):
            eyehi = os.path.join(ctx.tex_dir, name.replace('eye0', 'eyehi' + i) + '.png')
            if os.path.exists(eyehi):
                mag(log, temp, eyehi, '-gravity', 'South', '-compose', 'Screen', '-composite', temp)
        shutil.move(temp, out_png)
        log(f'  完成: {name}.png')
        return [out_png]
    finally:
        rm_files(temp)


# （已移除 _swap_texture：v3 不再交换 Texture2D↔OriginalTexture，原贴图文件夹保持只读）

# ================= 步骤2-15：模型处理（ctx 版） =================

def _import(modname):
    return importlib.import_module(modname)


def _run_one(ctx, func, log, label):
    """对当前单个模型调用 func(path)；抛异常由上层捕获并回退"""
    if not ctx.model_path:
        raise RuntimeError('未选择模型')
    m = os.path.basename(ctx.model_path)
    try:
        r = func(ctx.model_path)
        if r is False or r == 'skip':
            log(f'  [{label}] {m}: 无需处理/跳过')
        else:
            log(f'  [{label}] {m}: 成功')
    except Exception as e:
        log(f'  [ERROR] [{label}] {m}: {type(e).__name__}: {e}')
        raise


def step_dedup(ctx, log):
    mod = _import('dedupe_materials')
    _run_one(ctx, mod.process_pmx, log, '材质去重')


def step_merge(ctx, log):
    mod = _import('merge_morphs')
    _run_one(ctx, mod.merge_pmx_file, log, '合并mayu表情')


def step_rename_morphs(ctx, log):
    mod = _import('rename_morphs')
    _, glossary = mod.build_glossary(None)
    if not glossary:
        raise RuntimeError('无可用术语表(glossary_data)')
    def f(path):
        return mod.bilingualize_pmx_file(path, glossary)
    _run_one(ctx, f, log, '表情重命名')


def step_clean_eye(ctx, log):
    mod = _import('clean_eye_range')
    _run_one(ctx, mod.process_pmx, log, '清理眼球偏移零节点')


def step_fix_eye(ctx, log):
    mod = _import('fix_eye_range_sign')
    _run_one(ctx, mod.process_pmx, log, '修正并翻倍眼球控件')


def step_both_eyes(ctx, log):
    mod = _import('build_both_eyes')
    def f(path):
        return mod.build_both_eyes(path, path)
    _run_one(ctx, f, log, '双目骨生成')


def step_build_groups(ctx, log):
    mod = _import('build_groups')
    try:
        from groups_data import GROUPS
        groups = dict(GROUPS)
    except Exception:
        groups = {}
        log('  [警告] 未加载组表情术语表 groups_data')
    def f(path):
        ok = mod.build_groups_pmx(path, groups)
        try:
            mod.hide_face_bones(path)
        except Exception:
            pass
        try:
            mod.fix_eye_morph_range(path, take='large')
        except Exception:
            pass
        return ok
    _run_one(ctx, f, log, '建立表情组')


def step_clean_handle(ctx, log):
    mod = _import('clean_handle_bones')
    _run_one(ctx, mod.clean_handle_bones, log, '清理handle骨')


def step_rename_bones(ctx, log):
    mod = _import('rename_bones')
    _run_one(ctx, mod.rename_bones_file, log, '骨骼重命名')


def step_sort_bones(ctx, log):
    mod = _import('sort_bones_tree')
    _run_one(ctx, mod.sort_bones_file_tree, log, '骨骼索引排序')


def step_twist(ctx, log):
    mod = _import('gen_twist_bones')
    def f(path):
        return mod.gen_twist_bones(path, path)
    _run_one(ctx, f, log, '捩骨处理')


def step_apose(ctx, log):
    mod = _import('apose_general')
    def f(path):
        return mod.apose_apply(path, path)
    _run_one(ctx, f, log, 'APose处理')


def step_hip(ctx, log):
    mod = _import('hip_process')
    def f(path):
        return mod.hip_process(path, path)
    _run_one(ctx, f, log, 'D骨 ik骨统一处理')


def step_arm_ik(ctx, log):
    mod = _import('gen_arm_ik')
    def f(path):
        return mod.gen_arm_ik(path, path)
    _run_one(ctx, f, log, '手IK补齐')


def step_align(ctx, log):
    mod = _import('tree_align_1025_2')
    def f(path):
        return mod.align_bones(path, path)
    _run_one(ctx, f, log, '骨骼树对齐')


def step_body_physics(ctx, log):
    mod = _import('body_physics')
    def f(path):
        return mod.add_body_physics(path, path)
    _run_one(ctx, f, log, '基础人体刚体生成')


def step_hair_rigid(ctx, log):
    mod = _import('hair_rigid_1013_2_real')
    def f(path):
        return mod.add_hair_rigid(path, path)
    _run_one(ctx, f, log, '头部头发刚体生成')


def step_hammer_chain(ctx, log):
    mod = _import('hammer_chain_1013_2')
    def f(path):
        return mod.add_hammer_chain(path, path)
    _run_one(ctx, f, log, '锤悬挂链层生成')


def step_ear_rigid(ctx, log):
    mod = _import('ear_rigid_1013_2')
    def f(path):
        return mod.add_ear_rigid(path, path)
    _run_one(ctx, f, log, '耳骨刚体生成')


def step_refine_skirt(ctx, log):
    """裙骨细化：环腰/竖向数量翻倍 + 权重线性插值（先于尾巴裙子刚体）。"""
    mod = _import('skirt_refine')
    def f(path):
        return mod.refine_skirt(path, path)
    _run_one(ctx, f, log, '裙骨细化')


def step_tail_skirt(ctx, log):
    mod = _import('tail_skirt_1013_2')
    def f(path):
        return mod.add_tail_skirt(path, path)
    _run_one(ctx, f, log, '尾巴裙子刚体生成')


# ================= 末步：补全显示枠（"表情"特殊枠 + 8 组分类控制枠） =================
# 背景(已实测定位)：mini 崩 / mini+表情枠(B) 开 / 重存(表情枠) 开 / 仅改header(A) 仍崩
#   => root cause = 显示枠缺"表情"特殊枠(special=1 且 0 条目)。MMD 需要它。
# 参照完整模型 1013_2(10 个显示枠)，除"表情"枠外还有 8 个通常枠。迷你/去柄模型只留 Root，
#   缺全部通常枠——此处按英文骨名归类回填，保证 MMD 左侧"表示枠"分组可用。

# 已验证可开的"表情"特殊枠字节: name"表情"(UTF16 len4) + name_en"Exp"(UTF16 len6) + special=1 + 条目数0
EXPR_FRAME = bytes.fromhex('040000006888c560060000004500780070000100000000')

# 对照参考完整模型 1033_2 的通常显示枠命名（sp=0），第二项为英文名，第三项为内部归类键。
# note: 增加 胸/耳/髪/尻尾 四类（参考模型独立成组）；再增 顔（面部/表情骨），避免主控/表情骨被压到「その他」。
# 骨骼经 [10]重命名后为标准日文名，故归类须同时命中 英文原始名 与 处理后的日文名。
CTRL_FRAMES = (
    ('センター', 'center', 'center'),
    ('ＩＫ', 'IK', 'IK'),
    ('体（上）', 'body up', 'upper'),
    ('体（下）', 'body low', 'lower'),
    ('腕', 'arm', 'arm'),
    ('指', 'finger', 'finger'),
    ('足', 'foot', 'foot'),
    ('顔', 'face', 'face'),
    ('胸', 'chest', 'chest'),
    ('耳', 'ear', 'ear'),
    ('髪', 'hair', 'hair'),
    ('尻尾', 'tail', 'tail'),
    ('その他', 'other', 'other'),
)


def _classify_bone(name):
    """把骨名归入 13 类。同时识别英文原始名与处理后的标准日文名；
    面部/表情骨→顔，头/躯干/四肢/捩/腰臀辅助归对应组，饰品/弹簧等落『その他』。
    返回值必须属于 CTRL_FRAMES 的归类键。"""
    # --- 英文原始名（未重命名模型 / 兼容） ---
    if name.endswith('_IK_Handle'):
        return 'IK'
    if name.endswith('_Handle'):
        return 'other'
    if name in ('Position', 'Hip'):
        return 'center'
    if name.startswith('Head') or name in ('Chest', 'Neck', 'Head'):
        return 'upper'
    if name == 'Waist':
        return 'lower'
    # 面部/表情骨（英文）
    if name.startswith(('Eye', 'Eyebrow', 'Eyelash', 'Cheek', 'Chin', 'Nose',
                        'Mouth', 'Tongue', 'Tooth')) or name.startswith('M_'):
        return 'face'
    for kw in ('Index', 'Ring', 'Thumb'):
        if kw in name:
            return 'finger'
    # --- 特殊精确名（避免 足首/手首 里的『首』误判为头） ---
    if name in ('全ての親', 'センター', 'グルーブ'):
        return 'center'
    # 腰/臀辅助（腰キャンセル / 尻補助）→ 体（下）
    if name in ('腰', '下半身') or name.startswith('腰') or '尻補助' in name:
        return 'lower'
    # 上半身（含 上半身Ctrl）→ 体（上）
    if name in ('首', '頭') or name.startswith('上半身'):
        return 'upper'
    # 面部/表情骨（日文：両目/目/口/眉/歯/舌/頬/鼻/瞼 等）
    if name in ('両目', '左目', '右目'):
        return 'face'
    for kw in ('目', '眉', '口', '歯', '舌', '頬', '鼻', '瞼'):
        if kw in name:
            return 'face'
    # --- IK（腕/足 IK 分解与目标骨，优先于 腕/足/捩，避免 yz 骨进腕） ---
    for kw in ('IK', 'ＩＫ', 'yz', 'y+', 'yTgt'):
        if kw in name:
            return 'IK'
    # --- 胸 / 髪 / 耳 / 尻尾（参考模型独立成组） ---
    if '胸' in name:
        return 'chest'
    for kw in ('前髪', '後髪', '横髪', '触覚', '头发', '髪'):
        if kw in name:
            return 'hair'
    if '耳' in name:
        return 'ear'
    if '尻尾' in name:
        return 'tail'
    # --- 腕（含 捩/肩/腕/ひじ/手首/ダミー/袖口） ---
    for kw in ('肩', '腕', 'ひじ', '手首', 'ダミー', '袖口', '捩',
               'Shoulder', 'Arm_', 'Elbow', 'Wrist', 'Hand_Attach'):
        if kw in name:
            return 'arm'
    # --- 指 ---
    for kw in ('人指', '中指', '小指', '薬指', '親指'):
        if kw in name:
            return 'finger'
    # --- 足（腿链：足/膝/踝/趾） ---
    for kw in ('足', 'ひざ', '足首', 'つま先', '足先EX', 'Thigh', 'Knee', 'Ankle'):
        if kw in name:
            return 'foot'
    return 'other'




def _classify_items(r):
    """{类别: [骨骼索引]}，依据 bone_items 名称归类；丢弃无该类骨骼的类别。"""
    out = {c: [] for _, _, c in CTRL_FRAMES}
    for i, (name, _g, *_rest) in enumerate(r.bone_items):
        out[_classify_bone(name)].append(i)
    return {k: v for k, v in out.items() if v}


def _parse_frames(r, mod):
    """解析显示枠区，返回 [(name, name_en, special, item_count)]。用 decode_text 按编码正确解码。"""
    q = r.frame_start
    d = r.data

    def ri(sz, s=True):
        nonlocal q
        v = int.from_bytes(d[q:q + sz], 'little', signed=s); q += sz
        return v

    def rtext():
        nonlocal q
        nn = ri(4)
        raw = d[q:q + nn]; q += nn
        return mod.decode_text(raw)

    out = []
    for _ in range(r.frame_count):
        name = rtext(); name_en = rtext()
        sp = ri(1, False); ic = ri(4, False)
        for _ in range(ic):
            it = ri(1, False)
            ri(r.bidx) if it == 0 else ri(r.moidx)
        out.append((name, name_en, sp, ic))
    return out


def _enc_text(s):
    b = s.encode('utf-16-le')
    return len(b).to_bytes(4, 'little') + b


def _build_frame(name, name_en, sp, bids, bidx):
    """编码一个显示枠。sp==1 用『表情』枠固定字节；sp==0 编码通常枠(骨骼条目)。"""
    if sp == 1:
        return EXPR_FRAME
    out = _enc_text(name) + _enc_text(name_en)
    out += bytes([0])                              # special=0
    out += len(bids).to_bytes(4, 'little')         # item_count
    for b in bids:
        out += b'\x00' + b.to_bytes(bidx, 'little')  # 0=骨骼条目
    return out


def step_fix_frames(ctx, log):
    """[末步]补全显示枠：缺失"表情"特殊枠则补；再补全 8 组分类控制枠（按骨名归类）。"""
    mod = _import('clean_handle_bones')

    def f(path):
        r = mod.PMXReader(path).parse()
        existing = _parse_frames(r, mod)
        have = {n for (n, _ne, _sp, _ic) in existing}
        add = []  # (name, name_en, special, bids)
        # 1) "表情"特殊枠：只要已存在名为"表情"/'Exp'的枠即可（MMD 崩溃由缺该特殊枠导致，不必强制 0 条目）
        if not any((n == '表情' or ne == 'Exp') for (n, ne, sp, ic) in existing):
            add.append(('表情', 'Exp', 1, []))
        # 2) 分类控制枠：按当前骨名归类；已有同名枠则跳过；无该类别骨骼则不生成空组
        cls = _classify_items(r)
        for n, ne, c in CTRL_FRAMES:
            if n in have:
                continue
            bids = cls.get(c, [])
            if not bids:
                continue
            add.append((n, ne, 0, bids))
        if not add:
            return 'skip'
        blocks = b''.join(_build_frame(n, ne, sp, bids, r.bidx) for (n, ne, sp, bids) in add)
        data = bytearray(open(path, 'rb').read())
        fc_off = r.frame_start - 4                        # 显示枠计数(frame_count)所在偏移
        insert_at = r.frame_start + len(r.frame_bytes)    # 显示枠区末尾
        data[fc_off:fc_off + 4] = (r.frame_count + len(add)).to_bytes(4, 'little')
        data[insert_at:insert_at] = blocks
        open(path, 'wb').write(bytes(data))
        # 校验：frame_count+len(add) 且解析对齐 remaining==0
        r2 = mod.PMXReader(path).parse()
        ok = (r2.frame_count == r.frame_count + len(add)) and (r2.remaining == 0)
        if not ok:
            raise RuntimeError(f'校验失败: frame={r2.frame_count}(期望{r.frame_count + len(add)}) remain={r2.remaining}')
        return True

    _run_one(ctx, f, log, '补全显示枠(表情+分类控制枠)')


def step_mini_limb_ik(ctx, log):
    """mini 基础手足IK（仅IK）：绕开 toe/twist 依赖，为 mini 补足IK+手IK(腕YZ/ひじY)。"""
    mod = _import('mini_limb_ik')
    def f(path):
        return mod.add_mini_limb_ik(path, path)
    _run_one(ctx, f, log, '基础手足IK')


def step_body_physics_mini(ctx, log):
    """mini 基础人体刚体：不生成胸部刚体/胸 Joint；脚适配(仅足首刚体，无趾)。"""
    mod = _import('body_physics')
    def f(path):
        return mod.add_body_physics(path, path, chest=False, foot_simplified=True)
    _run_one(ctx, f, log, '基础人体刚体(mini)')


def step_tail_skirt_mini(ctx, log):
    """mini 尾巴裙子刚体：只生成尾巴，跳过裙子(Skirt_*/Sp_Hi_MSkirt*)。"""
    mod = _import('tail_skirt_1013_2')
    def f(path):
        return mod.add_tail_skirt(path, path, with_skirt=False)
    _run_one(ctx, f, log, '尾巴裙子刚体(mini-仅尾巴)')


def step_mini_torso(ctx, log):
    """mini 躯干层级修复：下半身挂到腰 + 补「上半身」骨，连通上下半身(修复 VMD 断连)。"""
    mod = _import('mini_torso_fix')
    def f(path):
        return mod.mini_torso_fix(path, path)
    _run_one(ctx, f, log, '躯干层级修复')


def step_mini_process(ctx, log):
    """[1]材质表情统一处理：工作区模型就地 UV重映射+morph；贴图复制到临时目录+腮红合成。"""
    mod = _import('mini_process')
    if not ctx.model_path:
        raise RuntimeError('未选择模型')
    log(f'  [1]材质表情统一处理：{os.path.basename(ctx.model_path)}')
    rtex = mod.process_workspace(ctx.model_path, ctx.tex_dir, log)
    ctx.result_tex_dir = rtex


def step_fixpos(ctx, log):
    """[23]模型位置修正：上移使脚底对齐地面 Y=0。"""
    mod = _import('mini_process')
    _run_one(ctx, lambda p: mod.fix_position(p, log), log, '模型位置修正')


# ================= 步骤注册表 =================

STEPS = [
    {'name': '[1]贴图处理', 'func': step_tex, 'texture': True},
    {'name': '[2]材质去重', 'func': step_dedup},
    {'name': '[3]合并mayu表情到face表情', 'func': step_merge},
    {'name': '[4]表情重命名', 'func': step_rename_morphs},
    {'name': '[5]清理眼球偏移零节点', 'func': step_clean_eye},
    {'name': '[6]修正并翻倍眼球控件', 'func': step_fix_eye},
    {'name': '[7]双目骨生成', 'func': step_both_eyes},
    {'name': '[8]建立表情组', 'func': step_build_groups},
    {'name': '[9]清理末端handle骨 互相引用耳骨', 'func': step_clean_handle},
    {'name': '[10]骨骼重命名', 'func': step_rename_bones},
    {'name': '[11]捩骨处理', 'func': step_twist},
    {'name': '[12]APose处理', 'func': step_apose},
    {'name': '[13]D骨 ik骨统一处理', 'func': step_hip},
    {'name': '[14]手IK补齐', 'func': step_arm_ik},
    {'name': '[15]骨骼树对齐', 'func': step_align},
    {'name': '[16]基础人体刚体生成', 'func': step_body_physics},
    {'name': '[17]头部头发刚体生成', 'func': step_hair_rigid},
    {'name': '[18]锤悬挂链层生成', 'func': step_hammer_chain},
    {'name': '[19]耳骨刚体生成', 'func': step_ear_rigid},
    {'name': '[20]裙骨细化', 'func': step_refine_skirt},
    {'name': '[21]尾巴裙子刚体生成', 'func': step_tail_skirt},
    {'name': '[22]骨骼索引排序', 'func': step_sort_bones},
    {'name': '[23]补全显示枠（表情+分类控制枠）', 'func': step_fix_frames},
]

# mini 模型处理（与通用模型分页，步骤序号从 [1] 重新计数）
MINI_STEPS = [
    {'name': '[1]材质表情统一处理', 'func': step_mini_process},
    {'name': '[2]模型位置修正', 'func': step_fixpos},
]
# 追加可安全执行的通用步骤(9~21)：去掉原 [n] 前缀，序号顺延从 [3] 开始。
# mini 无 Ankle_offset/twist 等：跳过通用 [12]捩骨 / [14]D骨 / [15]手IK（缺骨跳过），
# 用「基础手足IK」替换 14/15；[17]体刚体、[21]尾巴用 mini 专用(去胸/裙、脚适配)。
for _s in STEPS[8:]:
    if _s['name'].startswith(('[11]', '[13]', '[14]', '[20]', '[22]', '[23]')):   # [11]捩骨/[13]D骨/[14]手IK/[20]裙骨细化(mini无裙)/[22]排序/[23]表情枠 后置/跳过
        continue
    if _s['name'].startswith('[15]'):          # 骨骼树对齐前插入 mini 基础手足IK
        MINI_STEPS.append({'name': f'[{len(MINI_STEPS) + 1}]基础手足IK', 'func': step_mini_limb_ik})
    strip = _s['name'].split(']', 1)[-1].strip()
    if _s['name'].startswith('[16]'):            # mini：体刚体去胸部、脚适配
        MINI_STEPS.append({'name': f'[{len(MINI_STEPS) + 1}]{strip}', 'func': step_body_physics_mini})
        continue
    if _s['name'].startswith('[21]'):            # mini：尾巴，去裙子
        MINI_STEPS.append({'name': f'[{len(MINI_STEPS) + 1}]{strip}', 'func': step_tail_skirt_mini})
        continue
    MINI_STEPS.append({'name': f'[{len(MINI_STEPS) + 1}]{strip}', 'func': _s['func']})
    if _s['name'].startswith('[15]'):            # 骨骼树对齐后：躯干层级修复(连通上下半身)
        MINI_STEPS.append({'name': f'[{len(MINI_STEPS) + 1}]躯干层级修复', 'func': step_mini_torso})

# 骨骼索引排序 后置到 mini 流程末尾（保证所有新增骨也参与最终排序）
MINI_STEPS.append({'name': f'[{len(MINI_STEPS) + 1}]骨骼索引排序', 'func': step_sort_bones})
# 补全"表情"特殊枠 + 8 组分类控制枠 作为 mini 流程最后一步（修复崩溃 + 补齐显示分组）
MINI_STEPS.append({'name': f'[{len(MINI_STEPS) + 1}]补全显示枠（表情+分类控制枠）', 'func': step_fix_frames})

# ================= 文件选择 / 校验 =================

def scan_pmx(base_dir):
    """扫描目录下 pmx（排除历史产物），返回文件名列表"""
    try:
        files = [f for f in os.listdir(base_dir) if f.lower().endswith('.pmx')]
    except Exception:
        return []
    skip = ('_backup.pmx', '_merged.pmx', '_dedup.pmx', '_twist.pmx')
    return [f for f in files if f.lower() not in skip]


def validate_selection(model_path, tex_dir):
    """校验：模型恰为 1 个 + Texture2D 文件夹存在。不满足抛出异常。"""
    if not model_path:
        raise RuntimeError('未检测到 pmx 模型')
    base = os.path.dirname(model_path)
    models = scan_pmx(base)
    if not models:
        raise RuntimeError(f'未检测到 pmx 模型：{base}')
    if len(models) > 1:
        raise RuntimeError(f'检测到 {len(models)} 个模型（{"、".join(models)}）！请移除多余模型')
    if os.path.basename(model_path) not in models:
        raise RuntimeError(f'模型 {os.path.basename(model_path)} 不在可识别列表 {models}')
    if not tex_dir:
        raise RuntimeError('未选择 Texture2D 贴图文件夹')
    if not os.path.isdir(tex_dir):
        raise RuntimeError(f'未检测到 Texture2D 贴图文件夹：{tex_dir}')
    return True


# ================= 步骤执行核心（工作区副本执行 / 失败重置） =================

def run_step(ctx, idx, log, steps=None):
    """执行单个步骤（在工作区副本上就地执行；源文件只读）。
    步骤失败 -> 丢弃损坏的工作区副本，从源文件重新复制。
    返回 True/False。steps: 步骤列表（默认通用 STEPS）。"""
    steps = steps if steps is not None else STEPS
    step = steps[idx]
    log(f'\n{"=" * 60}\n步骤 {idx + 1}/{len(steps)}: {step["name"]}\n{"=" * 60}')

    if not ctx.model_path:
        log('  [ERROR] 未选择模型，跳过')
        return False

    if step.get('texture') and not ctx.tex_dir:
        log('  [ERROR] 未选择 Texture2D 贴图文件夹')
        return False

    old_cwd = os.getcwd()
    try:
        if ctx.base_dir:
            if not os.path.isdir(ctx.base_dir):
                raise FileNotFoundError(f'模型目录不存在：{ctx.base_dir}')
            os.chdir(ctx.base_dir)
        step['func'](ctx, log)
    except Exception as e:
        if ctx.base_dir:
            try:
                os.chdir(old_cwd)
            except Exception:
                pass
        hint = ''
        if isinstance(e, FileNotFoundError):
            hint = '（提示：文件/路径不存在，请检查模型、贴图、输出目录是否被移动、删除或未创建）'
        log(f'\n  [ERROR] 步骤{idx + 1} 执行失败: {type(e).__name__}: {e}{hint}')
        traceback.print_exc()
        # 步骤失败：重置工作区副本（源文件只读，丢弃损坏副本重新复制即可）
        if not step.get('texture'):
            try:
                reset_workspace(ctx)
                log('  [重置] 工作区副本已从源文件重建（源文件未被修改）')
            except Exception as e2:
                log(f'  [ERROR] 工作区重置失败: {e2}')
        return False
    if ctx.base_dir:
        os.chdir(old_cwd)
    log(f'  [OK] 步骤{idx + 1} 执行成功')
    return True

# ================= GUI =================

def _load_window_icon(root):
    """加载窗口图标（打包后取 _MEIPASS/icon.png；未打包取项目根 icon.png）"""
    try:
        import tkinter as tk
        if _is_frozen():
            icon = os.path.join(sys._MEIPASS, 'icon.png')
        else:
            icon = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'icon.png')
        if os.path.isfile(icon):
            img = tk.PhotoImage(file=icon)
            root.iconphoto(True, img)
            root._icon_img = img      # 保持引用防 GC
    except Exception:
        pass


def build_gui():
    import tkinter as tk
    from tkinter import ttk, messagebox, filedialog, scrolledtext

    class LogRedirector(io.TextIOBase):
        """把 print 重定向到 GUI 日志（工作线程写缓冲，主线程轮询），并同步写入诊断日志文件"""
        def __init__(self, sink, logfile=None):
            super().__init__()
            self.sink = sink
            self.buf = []
            self.lock = threading.Lock()
            self.f = None
            if logfile:
                try:
                    os.makedirs(os.path.dirname(logfile), exist_ok=True)
                    self.f = open(logfile, 'a', encoding='utf-8')
                except Exception:
                    self.f = None

        def write(self, s):
            if s and not s.endswith('\n'):
                s = s + '\n'   # 每条日志独立换行，避免多条 join 后接在同一行
            with self.lock:
                self.buf.append(s)
            if self.f:
                try:
                    self.f.write(s)
                    self.f.flush()
                except Exception:
                    pass
            return len(s)

        def flush(self):
            pass

        def drain(self):
            with self.lock:
                if not self.buf:
                    return ''
                s = ''.join(self.buf)
                self.buf.clear()
                return s

    class App:
        def __init__(self, root):
            self.root = root
            self.busy = False
            self.ctx = Ctx()
            self.state = 'none'          # none/success/fail/exported
            self.last_step = None
            self._color_mode = None      # 日志着色段：None/red/green
            self.log = LogRedirector(self._append_log,
                                     logfile=os.path.join(TEMP_ROOT, 'gui.log'))
            sys.stdout = self.log
            sys.stderr = self.log
            self._append_log(f'[系统] 工作台启动，诊断日志: {TEMP_ROOT}\\gui.log\n')
            self._build_ui()
            self.root.after(100, self._log_poll)
            self.refresh_info()
            self.root.protocol('WM_DELETE_WINDOW', self.on_close)

        # ---------- UI ----------
        def _build_ui(self):
            self.root.title('赛马娘提取模型处理工具')
            self.root.geometry('940x700')

            # 顶部：信息区（左）+ icon 原图（右上角）
            top = ttk.Frame(self.root)
            top.pack(fill='x', padx=8, pady=4)

            info = ttk.LabelFrame(top, text='工作区信息', padding=8)
            info.pack(side='left', fill='x', expand=True)
            self.lbl_env = ttk.Label(info, text='环境：...')
            self.lbl_env.pack(anchor='w', pady=1)
            self.lbl_model = ttk.Label(info, text='模型：未选择')
            self.lbl_model.pack(anchor='w', pady=1)
            self.lbl_tex = ttk.Label(info, text='贴图：未选择贴图文件夹')
            self.lbl_tex.pack(anchor='w', pady=1)
            self.lbl_out = ttk.Label(info, text='输出目录：未选择')
            self.lbl_out.pack(anchor='w', pady=1)
            self.lbl_state = ttk.Label(info, text='当前模型状态:未处理')
            self.lbl_state.pack(anchor='w', pady=1)

            # icon 原图（右上角，128x128）
            try:
                import tkinter as tk
                _icon_path = os.path.join(sys._MEIPASS if _is_frozen() else os.path.dirname(os.path.abspath(__file__)), 'icon.png')
                if os.path.isfile(_icon_path):
                    _img = tk.PhotoImage(file=_icon_path)
                    _icon_lbl = tk.Label(top, image=_img)
                    _icon_lbl.image = _img      # 保持引用防 GC
                    _icon_lbl.pack(side='right', anchor='ne', padx=4)
            except Exception:
                pass

            sel = ttk.LabelFrame(self.root, text='文件选择（Windows 文件/文件夹对话框）', padding=8)
            sel.pack(fill='x', padx=8, pady=4)
            ttk.Button(sel, text='选择模型(.pmx)',
                       command=self.choose_model).pack(side='left', padx=4)
            ttk.Button(sel, text='选择贴图文件夹(Texture2D)',
                       command=self.choose_texture).pack(side='left', padx=4)

            # 处理步骤：通用模型 / mini 模型 两页分开，各自从 [1] 编号
            nb = ttk.Notebook(self.root)
            nb.pack(fill='x', padx=8, pady=4)
            self.buttons = {}

            def build_grid(parent, steps, auto_cmd=None):
                cols = 5 if len(steps) > 5 else max(1, len(steps))
                for i, s in enumerate(steps):
                    b = ttk.Button(parent, text=s['name'],
                                   command=lambda i=i: self.start_step(i, steps))
                    b.grid(row=i // cols, column=i % cols, sticky='ew', padx=3, pady=3)
                    self.buttons[s['name']] = b
                if auto_cmd:
                    r = (len(steps) + cols - 1) // cols
                    ttk.Button(parent, text='全自动处理', command=auto_cmd).grid(
                        row=r, column=0, columnspan=cols, sticky='ew', padx=3, pady=3)
                for c in range(cols):
                    parent.columnconfigure(c, weight=1)

            gen = ttk.Frame(nb); nb.add(gen, text='通用模型处理')
            build_grid(gen, STEPS, self.start_auto)
            mn = ttk.Frame(nb); nb.add(mn, text='mini模型处理')
            build_grid(mn, MINI_STEPS, lambda: self.start_auto(MINI_STEPS))

            extra = ttk.Frame(self.root)
            extra.pack(fill='x', padx=8, pady=4)
            ttk.Button(extra, text='导出贴图和模型',
                       command=self.export_all).pack(side='left', padx=4)
            ttk.Button(extra, text='刷新信息', command=self.refresh_info).pack(side='left', padx=4)

            logf = ttk.LabelFrame(self.root, text='执行日志（命令窗口输出）', padding=4)
            logf.pack(fill='both', expand=True, padx=8, pady=4)
            self.text = scrolledtext.ScrolledText(logf, height=18, font=('Consolas', 9))
            self.text.tag_configure('red', foreground='#d00000')
            self.text.tag_configure('green', foreground='#1a7a1a')
            self.text.pack(fill='both', expand=True)
            # 日志只读：禁止用户输入，仅允许复制/全选
            self.text.bind('<Key>', self._block_log_input)
            self.text.bind_class('Text', '<Control-v>', lambda e: 'break')

        def _block_log_input(self, event):
            """拦截日志区所有键盘输入，仅放行复制/全选快捷键"""
            ctrl = (event.state & 0x4) != 0
            if ctrl and event.keysym.lower() in ('c', 'a'):
                return None
            return 'break'

        def _append_log(self, s):
            self._pending = getattr(self, '_pending', '') + s
            # 同步写入 LogRedirector 缓冲（_log_poll 从 self.log.drain() 读取），
            # 否则通过 log 参数调用（如步骤函数）的日志不会显示到窗口
            lg = getattr(self, 'log', None)
            if lg is not None:
                try:
                    lg.write(s)
                except Exception:
                    pass

        def _log_poll(self):
            s = self.log.drain()
            if s:
                self.text.config(state='normal')
                for line in s.splitlines(keepends=True):
                    t = line.strip()
                    if not t:
                        continue
                    if line[-1:] != '\n':
                        line = line + '\n'   # 统一补换行，避免无 \n 的日志段接在同一行
                    # 报错段（ERROR/失败/回退/异常/Traceback）→ 红色，延续到下一个恢复段
                    if ('[ERROR]' in line or 'Traceback' in line or '执行失败' in line
                            or '[回退]' in line or 'Error' in line or '异常' in line
                            or t.endswith('失败')):
                        self._color_mode = 'red'
                    elif ('[OK]' in line or '执行成功' in line or '成功' in line
                            or '完成' in line or '全部执行完毕'):
                        self._color_mode = 'green'
                    tag = self._color_mode or None
                    self.text.insert('end', line, tag)
                self.text.config(state='disabled')
                self.text.see('end')
            self.root.after(100, self._log_poll)

        # ---------- 信息刷新 ----------
        def refresh_info(self):
            im = check_imagemagick()
            self.lbl_env.config(
                text='环境：ImageMagick ' + ('已安装！' if im else '未安装，请确认magick指令是否有效！'))
            if self.ctx.src_model_path:
                display = self.ctx.src_model_path
                self.lbl_model.config(
                    text=f'模型：{display}（工作区副本处理中，源文件不会被修改）')
            else:
                self.lbl_model.config(text='检测到工作区下模型：无')
            if self.ctx.tex_dir and os.path.isdir(self.ctx.tex_dir):
                self.lbl_tex.config(text=f'贴图：检测到Texture2D!（{self.ctx.tex_dir}）')
            else:
                self.lbl_tex.config(text='贴图：未检测到贴图文件夹，跳过')
            if self.ctx.out_dir:
                self.lbl_out.config(text=f'输出目录：{self.ctx.out_dir}')
            else:
                self.lbl_out.config(text='输出目录：未选择')
            if self.state == 'success':
                self.lbl_state.config(text=f'当前模型状态:最近操作步骤{self.last_step}：成功')
            elif self.state == 'fail':
                self.lbl_state.config(text=f'当前模型状态:最近操作步骤{self.last_step}：失败')
            elif self.state == 'exported':
                self.lbl_state.config(text='当前模型状态:已导出')
            else:
                self.lbl_state.config(text='当前模型状态:未处理')

        # ---------- 文件选择 ----------
        def choose_model(self):
            if self.busy:
                return
            fp = filedialog.askopenfilename(
                title='选择模型文件(.pmx)',
                filetypes=[('PMX 模型', '*.pmx'), ('所有文件', '*.*')])
            if not fp:
                return
            try:
                if not fp.lower().endswith('.pmx'):
                    raise RuntimeError('所选文件不是 .pmx 模型')
                # ★ 复制源模型到临时工作区，源文件只读；后续步骤在工作区副本上执行
                work_dir, work_model = import_to_workspace(fp)
                self.ctx.src_model_path = fp
                self.ctx.model_path = work_model
                auto_tex = os.path.join(os.path.dirname(fp), 'Texture2D')
                if os.path.isdir(auto_tex):
                    self.ctx.tex_dir = auto_tex
                validate_selection(self.ctx.model_path, self.ctx.tex_dir)
                self.state = 'none'
                self.last_step = None
                self._append_log(f'[选择] 模型: {fp}\n'
                                 f'[工作区] 已复制到临时工作区: {work_model}\n'
                                 f'[工作区] 后续处理在工作区副本上进行，源文件不会被修改\n')
                if self.ctx.tex_dir:
                    self._append_log(f'[选择] 自动发现贴图文件夹: {self.ctx.tex_dir}\n')
            except Exception as e:
                messagebox.showerror('文件校验失败', str(e))
                self._append_log(f'[ERROR] {e}\n')
                self.state = 'fail'
            self.refresh_info()

        def choose_texture(self):
            if self.busy:
                return
            d = filedialog.askdirectory(title='选择 Texture2D 贴图文件夹')
            if not d:
                return
            try:
                cand = d
                if os.path.basename(cand) != 'Texture2D':
                    sub = os.path.join(cand, 'Texture2D')
                    if os.path.isdir(sub):
                        cand = sub
                if not os.path.isdir(cand):
                    raise RuntimeError(f'所选目录不是有效的贴图文件夹：{d}')
                self.ctx.tex_dir = cand
                if self.ctx.model_path:
                    validate_selection(self.ctx.model_path, self.ctx.tex_dir)
                self._append_log(f'[选择] 贴图文件夹: {cand}\n')
            except Exception as e:
                messagebox.showerror('文件校验失败', str(e))
                self._append_log(f'[ERROR] {e}\n')
                self.state = 'fail'
            self.refresh_info()

        # ---------- 执行控制 ----------
        def _set_busy(self, busy):
            self.busy = busy
            st = 'disabled' if busy else 'normal'
            for b in self.buttons.values():
                b.config(state=st)

        def _choose_output_dir(self):
            """弹出导出目标文件夹选择；未选择、与导入贴图文件夹相同或其下 Texture2D 相同 → 抛异常"""
            if not self.ctx.tex_dir:
                raise RuntimeError('请先选择贴图源文件夹')
            d = filedialog.askdirectory(title='请选择导出目标文件夹')
            if not d:
                raise RuntimeError('未选择导出目标文件夹，已取消')
            out = os.path.abspath(d)
            tex_n = os.path.normpath(self.ctx.tex_dir).lower()
            if os.path.normpath(out).lower() == tex_n:
                raise RuntimeError('导出目录不能与导入贴图文件夹相同，请重新选择')
            if os.path.normpath(os.path.join(out, 'Texture2D')).lower() == tex_n:
                raise RuntimeError('导出目录下的 Texture2D 与导入贴图文件夹相同，请重新选择')
            self.ctx.out_dir = out
            self._append_log(f'[输出] 导出目标: {out}')
            self.refresh_info()
            return out


        def start_step(self, idx, steps=None):
            if self.busy:
                return
            self._set_busy(True)
            threading.Thread(target=self._worker, args=(idx, steps), daemon=True).start()


        def start_auto(self, steps=None):
            if self.busy:
                return
            from bridge import is_unified
            if self.ctx.model_path and is_unified(self.ctx.model_path):
                messagebox.showinfo('已完成 MMD 适配', '此模型已由统一导出器生成参考姿势、控制骨架及物理。旧版全自动流程会重复修改它；请仅执行已检查过的单独步骤。')
                return
            try:
                out = self._choose_output_dir()
            except Exception as e:
                messagebox.showerror('请选择导出目标文件夹', str(e))
                self._append_log(f'[ERROR] {e}')
                self.state = 'fail'
                self.refresh_info()
                return
            self._set_busy(True)
            threading.Thread(target=self._worker_auto, args=(out, steps), daemon=True).start()


        def _worker(self, idx, steps=None):
            try:
                ok = run_step(self.ctx, idx, self._append_log, steps)
                self.state = 'success' if ok else 'fail'
                self.last_step = idx + 1
                self.root.after(0, self._after_step)
            except Exception as e:
                traceback.print_exc()
                self._append_log(f'\n[系统] 执行器异常: {e}\n')
                self.root.after(0, self._set_busy, False)

        def _worker_auto(self, out_dir, steps=None):
            steps = steps if steps is not None else STEPS
            try:
                failed = None
                for idx in range(len(steps)):
                    ok = run_step(self.ctx, idx, self._append_log, steps)
                    if not ok:
                        failed = idx
                        break
                if failed is None:
                    self.state, self.last_step = 'success', len(steps)
                    self._append_log(f'[系统] 全自动处理完成（1→{len(steps)} 全部执行完毕）')
                    if out_dir:
                        try:
                            self._do_export(os.path.abspath(out_dir))
                            self.state = 'exported'
                            self._append_log(f'[导出] 完成：模型 + 贴图已导出到 {os.path.abspath(out_dir)}')
                        except Exception as e:
                            self._append_log(f'[ERROR] 自动导出失败: {e}')
                            self.state = 'fail'
                else:
                    self.state, self.last_step = 'fail', failed + 1
                    self._append_log(f'[系统] 全自动处理中止于步骤{failed + 1}（工作区副本已重置，源文件未被修改）')
                self.root.after(0, self._after_step)
            except Exception as e:
                traceback.print_exc()
                self._append_log(f'[系统] 执行器异常: {e}')
                self.root.after(0, self._set_busy, False)


        def _after_step(self):
            self.refresh_info()
            self._set_busy(False)

        # ---------- 导出 ----------
        def export_all(self, dst=None):
            if self.busy:
                return
            if not self.ctx.model_path:
                messagebox.showwarning('导出', '请先选择模型文件')
                return
            if dst is None:
                dst = filedialog.askdirectory(title='选择导出目标文件夹')
                if not dst:
                    return
            try:
                self._do_export(os.path.abspath(dst))
                self.state = 'exported'
                self._append_log(f'[导出] 完成：模型 + 贴图已导出到 {os.path.abspath(dst)}')
            except Exception as e:
                messagebox.showerror('导出失败', str(e))
                self._append_log(f'[ERROR] 导出失败: {e}')
                self.state = 'fail'
            self.refresh_info()

        def _do_export(self, dst):
            """导出核心：模型 + 处理结果贴图(temp 的 Texture2D) 复制到目标目录"""
            if self.ctx.tex_dir and os.path.normpath(dst).lower() == os.path.normpath(self.ctx.tex_dir).lower():
                raise RuntimeError('导出目录不能与导入贴图文件夹相同')
            dst_model = os.path.join(dst, os.path.basename(self.ctx.model_path))
            shutil.copy2(self.ctx.model_path, dst_model)
            self._append_log(f'[导出] 模型 → {dst_model}')
            # 贴图：优先 temp 处理结果（步骤1），否则回退导入源
            tex_out = None
            if self.ctx.result_tex_dir and os.path.isdir(self.ctx.result_tex_dir):
                tex_out = self.ctx.result_tex_dir
            if tex_out is None and self.ctx.tex_dir and os.path.isdir(self.ctx.tex_dir):
                tex_out = self.ctx.tex_dir
            if tex_out is not None:
                dst_tex = os.path.join(dst, 'Texture2D')
                if os.path.normpath(dst_tex).lower() == os.path.normpath(tex_out).lower():
                    self._append_log('[导出] 贴图已位于目标目录，跳过复制')
                else:
                    if os.path.isdir(dst_tex):
                        shutil.rmtree(dst_tex)
                    shutil.copytree(tex_out, dst_tex)
                    self._append_log(f'[导出] 贴图 → {dst_tex}')


        # ---------- 关闭：清理临时工作区 ----------
        def on_close(self):
            try:
                _clean_workspace()
                gc.collect()
            except Exception:
                pass
            self.root.destroy()

    root = tk.Tk()
    _load_window_icon(root)
    app = App(root)
    initial_model = os.environ.get('PMX_INITIAL_MODEL')
    if initial_model and os.path.isfile(initial_model):
        work_dir, work_model = import_to_workspace(initial_model)
        app.ctx.src_model_path = initial_model
        app.ctx.model_path = work_model
        texture_dir = os.path.join(os.path.dirname(initial_model), 'Texture2D')
        if os.path.isdir(texture_dir): app.ctx.tex_dir = texture_dir
        app.refresh_info()
        app._append_log('[统一工程] 已导入 PMX 副本。新导出器已生成 A-pose、控制骨架及物理，请勿重复执行旧版全自动流程。\n')
    root.mainloop()


# ================= 命令行/自动化模式 =================

def _cli_run(auto):
    """环境变量 PMX_AUTOSTEP=N 执行第 N 步；PMX_MODEL=源模型路径；PMX_TEXTURE=贴图文件夹。
    源模型只读：先复制到临时工作区再处理；日志写工作区 workflow_autostep.log；
    若设置 PMX_OUTDIR，步骤成功后将工作区处理结果复制到该目录。不启动 GUI。"""
    model = os.environ.get('PMX_MODEL')
    tex = os.environ.get('PMX_TEXTURE')
    outdir = os.environ.get('PMX_OUTDIR') or None
    if not model:
        return 2
    try:
        work_dir, work_model = import_to_workspace(model)
    except Exception:
        return 1
    logpath = os.path.join(work_dir, 'workflow_autostep.log')

    def log(s):
        with open(logpath, 'a', encoding='utf-8') as f:
            f.write(s)
            f.flush()

    try:
        idx = int(auto) - 1
        if not (0 <= idx < len(STEPS)):
            log(f'[ERROR] 无效步骤号: {auto} (可用 1~{len(STEPS)})\n')
            return 1
        validate_selection(work_model, tex)
        ctx = Ctx(work_model, tex)
        ctx.src_model_path = model
        ctx.out_dir = outdir
        log(f'[CLI] 源模型: {model}\n'
            f'[CLI] 工作区副本: {work_model}（源文件不会被修改）\n')
        log(f'[CLI] 执行步骤{idx + 1}/{len(STEPS)}: {STEPS[idx]["name"]}\n')
        ok = run_step(ctx, idx, log)
        if ok and outdir:
            try:
                os.makedirs(outdir, exist_ok=True)
                dst_m = os.path.join(outdir, os.path.basename(work_model))
                shutil.copy2(work_model, dst_m)
                log(f'[CLI] 处理结果已导出: {dst_m}\n')
            except Exception as e:
                log(f'[CLI] 导出处理结果失败: {e}\n')
        elif ok:
            log(f'[CLI] 处理结果在工作区: {work_model}\n'
                f'[CLI] （设置 PMX_OUTDIR=目标目录 可自动导出处理结果）\n')
        log(f'[CLI] 步骤{idx + 1} 结果: {"成功" if ok else "失败"}\n')
        return 0 if ok else 1
    except Exception:
        with open(logpath, 'a', encoding='utf-8') as f:
            f.write(traceback.format_exc())
        return 1


def main():
    auto = os.environ.get('PMX_AUTOSTEP')
    if auto is not None:
        sys.exit(_cli_run(auto))
    try:
        build_gui()
    except Exception:
        try:
            crash = os.path.join(os.path.dirname(os.path.abspath(sys.executable)),
                                 'workflow_crash.log')
            with open(crash, 'w', encoding='utf-8') as f:
                f.write(traceback.format_exc())
        except Exception:
            pass
        raise


if __name__ == '__main__':
    main()

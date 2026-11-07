"""Transactional PMX bridge. Default profile preserves the Unity export byte-for-byte."""
import argparse
import hashlib
import json
from pathlib import Path, PureWindowsPath
import shutil
import sys
import tempfile
from pmx_validate import validate

def is_unified(path):
    data = Path(path).read_bytes()
    return any('UmaViewerUnified'.encode(enc) in data for enc in ('utf-8','utf-16-le'))

def process(model, output, steps=()):
    model, output = Path(model).resolve(), Path(output).resolve()
    if output == model.parent or model.is_relative_to(output):
        raise ValueError('Output must be a new directory separate from the source model')
    if output.exists():
        raise FileExistsError(f'Output already exists: {output}')
    if steps and is_unified(model):
        raise ValueError('Unified exports already have MMD pose/rig/physics. Preserve them; use the workbench only for individually reviewed edits.')
    initial = validate(model)
    before = hashlib.sha256(model.read_bytes()).hexdigest()
    output.parent.mkdir(parents=True,exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='pmx-bridge-',dir=output.parent) as temporary:
        stage = Path(temporary)/'result';stage.mkdir()
        work = stage/model.name
        shutil.copy2(model,work)
        # Copy only referenced textures and keep paths within the staged model directory.
        for texture in initial['textures']:
            rel = Path(texture.replace('\\','/'))
            if rel.is_absolute() or PureWindowsPath(texture).drive or '..' in rel.parts:
                raise ValueError(f'External texture path requires an explicit remap: {texture}')
            source = (model.parent/rel).resolve()
            if not source.is_relative_to(model.parent): raise ValueError(f'Texture resolves outside source directory: {texture}')
            if not source.is_file(): raise FileNotFoundError(source)
            target = (stage/rel).resolve()
            if not target.is_relative_to(stage): raise ValueError(f'Texture resolves outside output directory: {texture}')
            target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
        log=[]
        if steps:
            import workflow_gui as workflow
            ctx=workflow.Ctx(str(work),str(stage/'Texture2D'));ctx.src_model_path=str(model)
            for step in steps:
                if not 1 <= step <= len(workflow.STEPS): raise ValueError(f'Invalid step {step}')
                workflow.STEPS[step-1]['func'](ctx,log.append)
                validate(work)
        final = validate(work)
        if hashlib.sha256(model.read_bytes()).hexdigest()!=before:
            raise RuntimeError('Source model changed unexpectedly')
        manifest={'profile':'selected-legacy-steps' if steps else 'preserve', 'steps':list(steps),
                  'source':str(model),'source_sha256':before,'output_sha256':final['sha256'],
                  'validation':final,'log':log}
        (stage/'processing-report.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
        stage.rename(output)
    return output/model.name

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('model',type=Path);parser.add_argument('output',type=Path)
    parser.add_argument('--steps',type=lambda s: tuple(int(x) for x in s.split(',')),default=())
    args=parser.parse_args()
    print(process(args.model,args.output,args.steps))

if __name__=='__main__': main()

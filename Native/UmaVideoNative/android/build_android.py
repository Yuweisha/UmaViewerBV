"""Build the Android MediaCodec video encoder bridge (ARM64).

Mirrors Native/UmaSpringNative/build_android.py so the CI runner and local
builds use the same compiler flags.

Usage:
    python Native/UmaVideoNative/android/build_android.py
    python Native/UmaVideoNative/android/build_android.py --ndk <path> --output <file>
"""

import argparse
import os
import platform
import struct
import subprocess
from pathlib import Path


def resolve_ndk(explicit):
    if explicit:
        return Path(explicit)

    for variable in ('ANDROID_NDK_HOME', 'ANDROID_NDK_ROOT'):
        value = os.environ.get(variable)
        if value:
            return Path(value)

    for variable in ('ANDROID_SDK_ROOT', 'ANDROID_HOME'):
        sdk = os.environ.get(variable)
        if not sdk:
            continue
        installed = list((Path(sdk) / 'ndk').glob('*'))
        if installed:
            return max(installed, key=lambda path: tuple(
                int(part) for part in path.name.split('.') if part.isdigit()))

    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ndk', default=None, help='Android NDK root')
    parser.add_argument('--output', type=Path, default=None)
    parser.add_argument('--abi', default='arm64-v8a', choices=['arm64-v8a', 'armeabi-v7a'])
    args = parser.parse_args()

    ndk = resolve_ndk(args.ndk)

    if ndk is None:
        parser.error('Pass --ndk (or set ANDROID_NDK_HOME)')

    host = {'Windows': 'windows-x86_64', 'Linux': 'linux-x86_64', 'Darwin': 'darwin-x86_64'}[
        platform.system()]
    toolchain = ndk / 'toolchains/llvm/prebuilt' / host
    compiler = toolchain / 'bin' / ('clang++.exe' if os.name == 'nt' else 'clang++')

    if not compiler.exists():
        raise SystemExit('compiler not found: ' + str(compiler))

    root = Path(__file__).resolve().parents[3]

    if args.abi == 'arm64-v8a':
        target = 'aarch64-linux-android21'
        expected_machine = 183
        soname = 'libUmaVideoAndroid.so'
    else:
        target = 'armv7a-linux-androideabi21'
        expected_machine = 40
        soname = 'libUmaVideoAndroid.so'

    output = args.output or root / 'Assets/Plugins/Android/libs' / args.abi / soname
    output.parent.mkdir(parents=True, exist_ok=True)

    source = Path(__file__).resolve().parent / 'src/encoder_android.cpp'

    command = [
        str(compiler),
        '--target=' + target,
        '--sysroot=' + str(toolchain / 'sysroot'),
        '-std=c++17', '-O2', '-shared', '-fPIC',
        '-fvisibility=hidden', '-fno-exceptions', '-fno-rtti',
        '-fno-strict-aliasing',
        str(source),
        '-lmediandk', '-landroid', '-llog', '-ljnigraphics',
        '-static-libstdc++',
        '-Wl,--no-undefined',
        '-Wl,-z,max-page-size=16384',
        '-Wl,-soname,' + soname,
        '-o', str(output),
    ]

    print(' '.join(command))
    subprocess.run(command, check=True)

    header = output.read_bytes()[:20]

    if header[:6] != b'\x7fELF\x02\x01' and args.abi == 'arm64-v8a':
        raise RuntimeError('Expected a little-endian ELF64 shared library')

    if struct.unpack_from('<H', header, 18)[0] != expected_machine:
        raise RuntimeError('Unexpected ELF machine type for ' + args.abi)

    print('Built ' + args.abi + ': ' + str(output) + ' (' + str(output.stat().st_size) + ' bytes)')


if __name__ == '__main__':
    main()

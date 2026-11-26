# Third-party notices

The project is based on [katboi01/UmaViewer](https://github.com/katboi01/UmaViewer). Existing source attributions and copyright notices are retained.

Dependency notices are supplied alongside the corresponding code:

- VMDRecorder: `Assets/Scripts/Exporters/VMDRecorder/LICENSE`.
- UnityPMXRuntimeLoader, LibMMD and UniVRM: licenses in `Assets/Scripts/Exporters/UnityPMXRuntimeLoader`.
- RuntimeGizmo: `Assets/Scripts/Pose/RuntimeGizmo/LICENSE`.
- uGIF: `Assets/Scripts/uGIF/LICENSE.txt`.
- Lame for Unity: `Assets/Plugins/Lame-For-Unity/LICENSE`.
- NVENC API header: copyright and MIT license in `Native/UmaVideoNative/include/nvEncodeAPI.h`.
- Other bundled dependencies retain their source-file and package notices.

## CutIn Simplified Chinese resources

`Assets/Resources/Localize/gacha-zh-CN.bytes` embeds translations and 516 name textures
from [Hachimi-Hachimi/tl-zh-cn](https://github.com/Hachimi-Hachimi/tl-zh-cn), revision
`d25ece0757aaa73613c5e413f9fcfb8770c034dd`. The upstream credits yingyingyingqwq and
its [contributors](https://github.com/Hachimi-Hachimi/tl-zh-cn/graphs/contributors),
and identifies material ported from
[Trainers' Legend G](https://github.com/MinamiChiwa/Trainers-Legend-G-TRANS).

These translation assets use **Creative Commons Attribution-NonCommercial-ShareAlike
4.0 International (CC BY-NC-SA 4.0)**: 署名-非商业性使用-相同方式共享 4.0 国际.
[License](https://creativecommons.org/licenses/by-nc-sa/4.0/) ·
[Legal code](https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode).
Their license is separate from the application code. Name PNGs are unchanged;
only category 5 of `text_data_dict.json` is retained, and JSON is serialized
compactly. Upstream's README, preserved in the archive, also identifies the
original game-material rights and contributors.

The archive separately includes 252 unchanged comment PNGs supplied by the user
from `gacha_comment_png` (updated 2026-11-26). The user provided
`优俊少女各稀有度名称定制模板.7z` as source information. Its usage document prohibits
using the template for profit, describes noncommercial derivative creation under
the game's guidelines, and credits the template font to the Simplified Chinese
game publisher. It does not state a CC license or an author name; Hachimi's license
is not asserted for these images. The original usage document is preserved as
`COMMENT-TEMPLATE-USAGE.doc` inside the archive; the PSD and template font are not
bundled. The archive also contains `NOTICE.txt` and `HACHIMI-README.md`.

CutIn costume titles use the project's existing embedded NotoSansCJKsc-Bold font.
Runtime translation downloads and automatic comment-texture generation are removed.

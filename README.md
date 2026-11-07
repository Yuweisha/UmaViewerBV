# UmaViewer

Unity 资源查看与 Live 播放工程，包含 Standalone 资源下载、角色与服装选择、字幕、视频渲染和 CySpring 物理。

## 开发环境

- Unity **2022.3.62f3**，Windows x64 Build Support。
- FFmpeg：用于视频编码与音视频封装，放在程序目录或加入 `PATH`。
- 项目依赖由 `Packages/manifest.json` 和 `Packages/packages-lock.json` 管理。

用 Unity 打开工程，载入 `Assets/Scenes/Version2.unity`，进入 Play 模式。Windows 构建可使用 Build Settings，或运行：

```powershell
& '<Unity安装目录>/Editor/Unity.exe' -batchmode -quit -projectPath (Get-Location).Path -executeMethod PreviewerCompatibilityBuild.Run -previewerBuildOutput ./Build/UmaViewer.exe
```

## 使用入口

在 Settings → Other 中选择数据目录和工作模式。Default 使用游戏的 `meta`、`master/master.mdb` 与 `dat`；Standalone 可以复用已有数据库，也可以构建 `meta_umaviewer` 和 `master/master_umaviewer.mdb`，按需下载资源。

Shows 中选择歌曲与角色，使用 Subtitles 和 Recommended cast & outfits 开关调整播放设置。Render 打开分辨率、帧率、码率和编码器设置，选择输出目录后生成 MP4。

## 目录

| 目录 | 内容 |
| --- | --- |
| `Assets` | Unity 场景、界面、脚本、着色器和运行插件 |
| `Packages` | Unity 包依赖 |
| `ProjectSettings` | 项目与构建设置 |
| `Native` | CySpring 数值内核与 D3D11/NVENC 编码桥源码 |
| `ExternalTools/pmx` | PMX 工作台及其依赖模块 |

功能源码入口与实现说明见 [交接文档](交接文档.md)。项目基于 [katboi01/UmaViewer](https://github.com/katboi01/UmaViewer)，第三方声明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

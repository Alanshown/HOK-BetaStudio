<div align="center">
  <img src="docs/images/app-icon.png" width="112" alt="HOK BetaStudio 项目图标">
  <h1>HOK BetaStudio</h1>
  <p>面向王者荣耀资产的桌面工作区。</p>
  <p>
    <img alt="版本 1.4" src="https://img.shields.io/badge/version-1.4-147d72?style=flat-square">
    <img alt="Windows x64" src="https://img.shields.io/badge/platform-Windows_x64-357b9b?style=flat-square">
    <img alt="C# 与 React" src="https://img.shields.io/badge/C%23_%2B_React-desktop-667672?style=flat-square">
    <img alt="三种界面语言" src="https://img.shields.io/badge/UI-EN_%C2%B7_%E4%B8%AD%E6%96%87_%C2%B7_VI-147d72?style=flat-square">
    <img alt="实验性重建" src="https://img.shields.io/badge/DB_rebuilding-Beta-b68a42?style=flat-square">
  </p>
</div>

<!-- README-I18N:START -->

[English](./README.md) | **简体中文** | [Tiếng Việt](./README.vi.md)

<!-- README-I18N:END -->

版本 **1.4** 新增按需预览、按内存规模调度 DB 工作进程，以及基于真实引用关系的网格／动画 FBX 导出。查看[发行说明](docs/releases/1.4/RELEASE-NOTES.md)。下方下载链接在 1.4 发行附件发布后生效；源码更新不会自动发布二进制包。

**[下载 1.4 安装版](https://github.com/Alanshown/HOK-BetaStudio/releases/download/1.4/HOK-BetaStudio-1.4-win-x64-setup.exe) · [下载 1.4 便携 ZIP](https://github.com/Alanshown/HOK-BetaStudio/releases/download/1.4/HOK-BetaStudio-1.4-win-x64-portable.zip)**

Windows x64 · 源码版本 1.4 · [安装与校验](docs/INSTALL.md#简体中文) · [MIT 许可证](LICENSE)

按英雄和皮肤浏览 DB 包，检查 Unity 与非 Unity 文件，预览模型和音频，并批量导出所选资产。C# 后端将解析、预览和导出放在独立于界面的进程中运行。

**[程序截图](#export) · [版本下载](https://github.com/Alanshown/HOK-BetaStudio/releases) · [问题反馈](https://github.com/Alanshown/HOK-BetaStudio/issues)**

![桌面程序的英雄目录](docs/images/01-catalog.png)

## 目录

- [工作区与扫描](#workspace)
- [预览与导出](#export)
- [替换与重建 — Beta](#beta)
- [项目结构](#structure)
- [构建与打包](#build)
- [下载与使用](#download)
- [验证情况](#verification)
- [归属与分发](#attribution)

<a id="workspace"></a>
## 工作区与扫描

- **导入前也可浏览。** 启动时显示本地英雄与皮肤目录，头像采用斜向多米诺入场动画，并尊重系统减少动态效果设置。
- **资源同步（1.3）。** 头像通过索引中的 HTTPS 链接加载。启动时读取已生效本地索引，并静默抓取、校验官方目录；仅确认存在差异才显示动态“资源同步”按钮。点击后清空导入 DB 缓存与待重建替换记录，返回英雄首页，以斜向多米诺动画刷新头像。原始 DB 和已完成导出不动，网络失败保留原索引。
- **打开文件、文件夹或直接拖入。** 递归扫描所有普通子目录，无写死的层数限制。为避免循环，跳过目录联接与符号链接；无权限路径会报告错误。
- **依据文件名对位。** `3200010504.db` → 皮肤 `10504` → 英雄 `105`。`_0` 等分片后缀单独处理。无扩展名文件须通过 DB 签名检查。
- **原皮规则。** `10500` 等以 `00` 结尾的皮肤使用英雄头像并显示“默认皮肤”。未知英雄与皮肤 ID 仍然显示，使用按 ID 固定的占位图。
- **只显示当前导入范围。** 导入后显示匹配英雄、皮肤及其 DB 文件名。支持按名称或 ID 搜索、资产类型过滤、分页与批量选择。
- **切换工作区。** 新导入会清除上次导入缓存和待替换记录；动画扫帚按钮可清理缓存并返回初始目录，不删除原始文件。
- **三语界面：** 英文、简体中文和越南文。资产原名与目录名称保持原样。

![英雄 105 对应的两个皮肤包](docs/images/02-skins.png)

<a id="export"></a>
## 预览与导出

| 模块 | 预览／输出 |
|---|---|
| 贴图与精灵图 | 图像预览、通道开关、缩放；PNG、TGA、BMP、JPG、原始数据 |
| 网格 | 3D 旋转／缩放、线框；OBJ、FBX、JSON、原始数据。FBX 包含可解析引用中的骨骼、形变和动画；无关联的网格保持静态 |
| GameObject / Animator | 原生组件及引用完整时支持 FBX 导出 |
| AnimationClip | 关联场景播放；模型／骨架引用完整时导出 FBX，也支持 Unity YAML `.anim`、曲线 JSON、原始数据；缺少依赖会明确报告 |
| AudioClip / Wwise 音频 | 本地播放；原始媒体，以及解码器支持时的 WAV、MP3 |
| WwiseBank | 内嵌媒体树与播放器；原始 BNK、原始 WEM 文件 ZIP、转换后 MP3 文件 ZIP |
| Cubemap、字体、文本、着色器、视频 | Cubemap 六面预览／PNG ZIP；按需字体预览；按类型导出原始内容、文本及 JSON |
| 其他封包条目 | 同时列出非 Unity 文件；未知或未解码内容可保留原始数据 |

预览弹窗模糊工作区背景，可在同类资产中切换上一个／下一个。BNK 内的切换限定在当前音频包中。用户操作会打断模型自动旋转。

`WwiseAudio` 与 `WwiseBank` 表示媒体／容器类型，**不能固定理解为“聊天声音”和“技能语音”**。BNK 可以包含内嵌媒体、外部媒体引用或两者，只能提取实际存在的数据。WEM 导出保留原始字节，不支持将任意音频编码成 WEM。

预览仅在点击后加载；关闭后释放图像缓冲、模型 GPU 资源、音频源与临时字体，打开音频包不会解码全部声音。小工作区分离工作进程，大工作区复用已建立索引的进程，避免重复占用 DB 内存。批量导出逐项记录结果、警告并避免路径冲突；不支持的编解码器和缺失引用会明确报告，不伪造数据。

![资产工作区](docs/images/03-assets.png)
![模型预览](docs/images/05-model.png)
![包含六个内嵌音频条目的真实 BNK](docs/images/06-audio-bank.png)
![音频包导出选项](docs/images/07-export.png)

<a id="beta"></a>
## 替换与重建 — Beta

> [!WARNING]
> **实验性功能，不推荐常规使用，尚未验证游戏内兼容性。** 悬停或聚焦程序内 Beta 标签可查看说明。工具能够重新解析，不等于游戏能够正常加载。

1. 仅勾选一个受支持资产时显示 **替换**。
2. 选择兼容的原格式二进制。替换成功后标记，支持继续替换其他项或重复替换同一项。
3. 至少一项替换暂存成功后显示 **重建**。
4. 选择独立输出目录，将替换内容与其余全部原文件一起生成包，原包不覆盖。

当前仅支持等长二进制替换，并需适配原有压缩槽；不支持通用 PNG／OBJ／MP3 导入、新增任意资产或改变对象大小。不透明对象与 BNK 内嵌媒体不具备通用替换能力。重建包重新导入前，普通预览和导出仍显示原始数据。

修改块可能使用 Zstd 与可跳过帧填充。未知 QTS 校验字段及 `record.bytes` 映射**不会重新计算**；仅有 GUID 的外部引用尚未完整解析。零修改复制、未修改字节保留和回读检查只是测试基线，不是游戏兼容保证。

<a id="structure"></a>
## 项目结构

```text
HOK-BetaStudio/
├── backend/
│   ├── Hok.Desktop/          # C# WPF + WebView2 host
│   ├── Hok.Worker/           # parsing, preview, export
│   ├── Hok.Catalog/          # remote index validation, staging and atomic sync
│   ├── Hok.Contracts/        # filename identity and recursive scanner
│   ├── Hok.Legacy/           # adapters to Studio-HoK readers
│   ├── Hok.Rebuild/          # experimental replacement and rebuild
│   └── *.Tests/             # executable regression suites
├── frontend/                # React + TypeScript + Three.js
├── vendor/Studio-HoK/        # upstream C# source and attribution
├── assets/                  # UI assets, catalog and license notices
├── docs/                    # documentation and real screenshots
├── tooling/                 # build, packaging and verification scripts
├── README.md
├── README.zh.md
└── README.vi.md
```

依赖缓存、原生运行库包、游戏 DB、导出资产和构建产物不纳入源码仓库。自 1.3 起使用内置链接索引与在线头像，不再要求或打包静态游戏头像图库；未知 ID 和图片加载失败时使用本地占位图。

<a id="build"></a>
## 构建与打包

桌面宿主需要 Windows x64、.NET 10 SDK、带 npm 的 Node.js 与 Microsoft Edge WebView2 Runtime。安装包另需 NSIS。JavaScript 依赖版本由锁文件记录。

```powershell
git clone https://github.com/Alanshown/HOK-BetaStudio.git
cd HOK-BetaStudio
npm ci --prefix frontend
npm ci --prefix tooling
dotnet build backend/Hok.Desktop/Hok.Desktop.csproj -c Release
dotnet build backend/Hok.Worker/Hok.Worker.csproj -c Release
dotnet run --project backend/Hok.Contracts.Tests -c Release -- .
dotnet run --project backend/Hok.Catalog.Tests -c Release -- .
```

源码编译与完整桌面发行包是不同步骤。原生 FBX／FMOD／编解码组件、媒体工具、素材和打包目录说明见[构建输入](docs/BUILD.md)。不要把第三方二进制复制进 Git 仓库。

```powershell
powershell -ExecutionPolicy Bypass -File tooling/build-desktop.ps1 -OutputDirectory build/HOK-BetaStudio-1.4-win-x64
powershell -ExecutionPolicy Bypass -File tooling/package-desktop.ps1 -BuildDirectory build/HOK-BetaStudio-1.4-win-x64
```

演示页面单独维护，不纳入此仓库。上方图像均为真实桌面程序截图。

维护者操作：[PowerShell 提交代码、创建分支、推送与合并 PR 指南](docs/GIT-PR-GUIDE.zh.md)。

<a id="download"></a>
## 下载与使用

从[发行页面](https://github.com/Alanshown/HOK-BetaStudio/releases)选择实际已发布版本的 Setup 安装程序或便携 ZIP。源码更新不等于二进制附件已发布；GitHub 自动生成的 **Source code** 是源码，不是可运行程序。参阅[安装、SHA-256 校验与故障排查](docs/INSTALL.md#简体中文)。

解压**整个便携 ZIP**并运行 `HOK BetaStudio.exe`，或者使用 Windows 安装程序。不要将 EXE 与 `worker`、`ui`、`assets` 文件夹分开。自包含 .NET 包仍需要 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download)，Setup 不会自动安装它。目前程序包未进行代码签名。

打开包含主 DB、分片及相关文件的完整目录，依次选择英雄、皮肤、资产，再预览或导出。始终备份原始包，尤其在试用 Beta 重建时。

<a id="verification"></a>
## 验证情况

截图来自打包后的真实桌面程序，使用本地 `3200010500` 和 `3200010504` 测试包，不是虚构界面数据。测试包及提取的音频／模型不公开上传。

| 检查 | 已观察结果 |
|---|---|
| 网格导出（1.4） | 8 个真实网格分别导出 OBJ／FBX 并由独立读取器回读，几何一致；两组桌面样本连续导出通过 |
| 动画 FBX（1.4） | 6 个合成 FBX 样本回读，验证骨架、根曲线、覆盖控制器片段及动画采样播放 |
| 按需导入／预览 | 逐一测试 20 个 DB，704,337 条可见资产；原包哈希不变，导入阶段显式预览媒体物化量为零；[范围与限制](docs/LAZY-PREVIEW-VALIDATION.md) |
| 资源索引／桌面同步（1.3） | 41 项索引检查与 22 项桌面检查，涵盖真实 CDN 图片、静默检测、缓存清理、三语与多米诺刷新 |
| 命名识别与递归扫描 | 27 项检查；在 12 层目录中找到 14 个 DB／分片 |
| 零修改重建基线 | 两组包共 16 项检查，基线输出逐字节一致 |
| 替换工作进程 | 15 项检查，包含重复替换与未修改字节保留 |
| 替换桌面界面 | 11 项检查，包含单选操作和三语 Beta 警告 |
| BNK 工作进程／桌面 | 8／9 项检查，验证样本包中的六个内嵌媒体条目 |

这些是限定样本的回归结果，不代表兼容所有游戏版本或编解码格式。部分集成脚本需要私人测试目录和本地生成的报告；运行前参阅[构建输入](docs/BUILD.md)。

<a id="attribution"></a>
## 归属与分发

HOK BetaStudio 原创代码采用 [MIT 许可证](LICENSE)。基于 Studio-HoK／AssetStudio 读取器，保留[上游 MIT 声明](vendor/Studio-HoK/LICENSE)。本许可证不重新授权第三方代码、二进制、游戏美术或商标。见[许可范围](docs/LICENSING.md)、[素材说明](assets/licenses/SOURCES.txt)和[媒体组件说明](assets/licenses/media/NOTICE.txt)。

HOK 图形加文字的应用图标是生成的项目概念图，不是官方游戏图标。王者荣耀名称、角色、美术和商标属于相应权利人；本项目为独立项目。截图用于说明工具，不授予所展示游戏内容的再分发权。

仅处理你有权使用的素材。公开发布不代表分发审核已通过：游戏素材授权、原生 FBX／FMOD／编解码组件分发条件，以及所附 FFmpeg 构建的对应源码义务，仍在[发行检查清单](docs/RELEASE-CHECKLIST.md)中标为待核实。仓库不对这些组件作统一授权。

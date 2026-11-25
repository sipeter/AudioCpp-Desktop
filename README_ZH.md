<p align="center">
  <img src="Assets/app_icon.png" width="96" alt="AudioCpp Desktop Icon"/>
</p>

<h1 align="center">AudioCpp-Desktop</h1>

<p align="center">
  基于原生 Windows WPF (.NET 8) 的 <a href="https://github.com/0xShug0/audio.cpp">audio.cpp</a> 本地多模型语音合成与识别桌面控制台
</p>

<p align="center">
  <a href="README.md">English</a> | <strong>简体中文</strong>
</p>

---

## 核心特性

- **一键启停与系统托盘**
  - 图形化切换 CUDA 显卡加速 / CPU 兼容模式，自定义监听端口与最大常驻模型数（建议设为 `1`，切换大模型时自动释放显存），关闭窗口可最小化到系统托盘后台运行，退出时自动终止服务进程。
- **标准 OpenAI 兼容 API**
  - 提供 `/v1/audio/speech`、`/v1/audio/voices`、`/v1/audio/transcriptions` 接口，自动识别局域网 IP 并支持一键复制地址与模型 ID——可直接接入 **Open WebUI**、**SillyTavern**、**开源阅读（Legado）**、**CloneTTS** 等任意 OpenAI TTS 客户端。
- **双源 GGUF 模型可视化下载管理**
  - 内置 6 款 TTS 模型（`M5-TTS`、`Qwen3-TTS 0.6B / 1.7B`、`CosyVoice 3`、`VoxCPM 2`、`IndexTTS 2.5`）与 2 款 ASR 语音识别模型（`Fun-ASR-Nano 2512`、`Qwen3-ASR 0.6B`）。
  - 支持 **魔搭 ModelScope 国内镜像**（`ziansoft/audiocpp-tts-gguf`，免梯直连）与 **HuggingFace 官方源** 断点续传下载，下载完成自动校验 `GGUF` 文件头。
  - 支持自定义模型目录与音色目录，跨盘共享大文件无需重复拷贝。
- **参考音色库与内置 ASR 自动听写**
  - 表格化管理 `voice-library/` 中的 `.wav` 参考音频和 `prompt_text`，支持在线试听；导入 `.mp3`/`.m4a`/`.flac` 时若本机有 `ffmpeg` 会自动转码为 24 kHz 单声道 `.wav`。
  - 支持调用本地 `Fun-ASR-Nano` 或 `Qwen3-ASR` 一键自动识别参考音频文本并保存。
- **外置 JSON 多语言包**
  - 自带 `locales/zh.json` 与 `locales/en.json`（内置嵌入兜底 + 外部覆盖）。将任意新语言包 `locales/<code>.json` 放入程序同级 `locales/` 目录即可自动加载并实时切换。

## 下载使用

前往 [GitHub Releases](https://github.com/sipeter/AudioCpp-Desktop/releases) 页面下载：

- **`AudioCpp-Desktop-AllInOne-v0.1.0-win-x64-cuda12.7z`**（约 579 MB，推荐）：开箱即用一键整合包，内置 `AudioCpp-Desktop.exe`、`locales/`、`audio.cpp v0.9.6` 以及精简 CUDA 12 / CPU 运行库（无需安装 Python、.NET 运行库或 CUDA Toolkit）。
- **`AudioCpp-Desktop-v0.1.0-win-x64.zip`**（约 63 MB）：仅包含零依赖桌面控制台 EXE 与语言包（适合已有 `audio.cpp` 运行程序的用户）。

## 本地编译

需要安装 Windows [.NET 8 SDK](https://dotnet.microsoft.com/)：

```powershell
# 开发编译
dotnet build -c Release

# 发布零依赖单文件 EXE（目标电脑无需安装 .NET 桌面运行库）
dotnet publish -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish-dist
```

## 贡献多语言翻译

复制 `locales/en.json` 为 `locales/<语言代码>.json`（例如 `locales/ja.json`），修改 `_lang_name` 及对应词条后放入程序旁的 `locales/` 目录即可生效，欢迎提交 PR！

## 致谢与开源协议

- **AudioCpp-Desktop** 源码采用 **Apache License 2.0** 开源协议（详见 [LICENSE](LICENSE)）。
- 上游推理引擎：[0xShug0/audio.cpp](https://github.com/0xShug0/audio.cpp)（Apache-2.0）。
- 模型权重：[audio-cpp/audio.cpp-gguf](https://huggingface.co/audio-cpp/audio.cpp-gguf) / [ziansoft/audiocpp-tts-gguf](https://www.modelscope.cn/models/ziansoft/audiocpp-tts-gguf)（各模型分别遵循各自上游开源许可）。

using System.IO;
using AudioCppDesktop.Models;

namespace AudioCppDesktop.Services;

public static class ModelCatalog
{
    public const string ModelScopeBase = "https://www.modelscope.cn/models/ziansoft/audiocpp-tts-gguf/resolve/master";
    public const string HuggingFaceBase = "https://huggingface.co/audio-cpp/audio.cpp-gguf/resolve/main";

    public static List<ModelEntry> CreateDefaultCatalog() =>
    [
        new()
        {
            DisplayName = "M5-TTS 50M VoiceClone",
            ModelId = "m5-clone",
            FileName = "m5-tts-50m-voiceclone-q8_0.gguf",
            Family = "m5_tts",
            Task = "clon",
            HfSubDir = "M5-TTS-GGUF",
            SizeLabel = "895 MB",
            PromptTextReqKey = "models.req_exact",
            DescriptionKey = "models.desc.m5-clone",
            License = "Apache-2.0"
        },
        new()
        {
            DisplayName = "Qwen3-TTS 0.6B Base",
            ModelId = "qwen3-clone",
            FileName = "qwen3-tts-12hz-0.6b-base-q8_0.gguf",
            Family = "qwen3_tts",
            Task = "tts",
            HfSubDir = "Qwen3-TTS-12Hz-0.6B-Base-GGUF",
            SizeLabel = "1.85 GB",
            PromptTextReqKey = "models.req_exact",
            DescriptionKey = "models.desc.qwen3-clone",
            License = "Apache-2.0"
        },
        new()
        {
            DisplayName = "Qwen3-TTS 1.7B Base",
            ModelId = "qwen3-1.7b",
            FileName = "qwen3-tts-12hz-1.7b-base-q8_0_v2.gguf",
            Family = "qwen3_tts",
            Task = "tts",
            HfSubDir = "Qwen3-TTS-12Hz-1.7B-Base-GGUF",
            SizeLabel = "2.51 GB",
            PromptTextReqKey = "models.req_exact",
            DescriptionKey = "models.desc.qwen3-1.7b",
            License = "Apache-2.0"
        },
        new()
        {
            DisplayName = "CosyVoice 3",
            ModelId = "cosyvoice-3",
            FileName = "cosyvoice3-q8_0.gguf",
            Family = "cosyvoice3",
            Task = "clon",
            HfSubDir = "CosyVoice3-GGUF",
            SizeLabel = "2.10 GB",
            PromptTextReqKey = "models.req_exact",
            DescriptionKey = "models.desc.cosyvoice-3",
            License = "Apache-2.0"
        },
        new()
        {
            DisplayName = "VoxCPM 2",
            ModelId = "voxcpm-2",
            FileName = "voxcpm2-q8_0.gguf",
            Family = "voxcpm2",
            Task = "tts",
            HfSubDir = "VoxCPM2-GGUF",
            SizeLabel = "2.75 GB",
            PromptTextReqKey = "models.req_exact",
            DescriptionKey = "models.desc.voxcpm-2",
            License = "Apache-2.0"
        },
        new()
        {
            DisplayName = "IndexTTS 2.5",
            ModelId = "indextts-2_5",
            FileName = "index-tts2_5-q8_0.gguf",
            Family = "index_tts2",
            Task = "tts",
            HfSubDir = "IndexTTS2.5-GGUF",
            SizeLabel = "3.26 GB",
            PromptTextReqKey = "models.req_none",
            DescriptionKey = "models.desc.indextts-2_5",
            License = "Upstream Non-Commercial"
        },
        new()
        {
            DisplayName = "Fun-ASR-Nano 2512 (ASR)",
            ModelId = "fun-asr-nano",
            FileName = "fun-asr-nano-2512-q8_0.gguf",
            Family = "fun_asr_nano",
            Task = "asr",
            HfSubDir = "Fun-ASR-Nano-2512-GGUF",
            SizeLabel = "997 MB",
            PromptTextReqKey = "models.req_asr",
            DescriptionKey = "models.desc.fun-asr-nano",
            License = "MIT / Apache-2.0"
        },
        new()
        {
            DisplayName = "Qwen3-ASR 0.6B (ASR)",
            ModelId = "qwen3-asr",
            FileName = "qwen3-asr-0.6b-q8_0.gguf",
            Family = "qwen3_asr",
            Task = "asr",
            HfSubDir = "Qwen3-ASR-0.6B-GGUF",
            SizeLabel = "1.07 GB",
            PromptTextReqKey = "models.req_asr",
            DescriptionKey = "models.desc.qwen3-asr",
            License = "Apache-2.0"
        }
    ];

    public static string BuildDownloadUrl(ModelEntry entry, string source)
    {
        return string.Equals(source, "huggingface", StringComparison.OrdinalIgnoreCase)
            ? $"{HuggingFaceBase}/{entry.HfSubDir}/{entry.FileName}"
            : $"{ModelScopeBase}/{entry.FileName}";
    }

    public static List<InstalledModelInfo> ScanInstalledModels(string modelsDir)
    {
        var results = new List<InstalledModelInfo>();
        if (!Directory.Exists(modelsDir)) return results;

        foreach (var file in Directory.GetFiles(modelsDir, "*.gguf", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(file);
            var lower = fileName.ToLowerInvariant();
            string? id = null;
            string? family = null;
            var task = "tts";
            var opts = new Dictionary<string, object> { ["seed"] = 123 };

            if (lower.Contains("m5-tts") || lower.Contains("m5_tts"))
            {
                id = "m5-clone"; family = "m5_tts"; task = "clon";
                opts = new Dictionary<string, object> { ["language"] = "zh", ["seed"] = 2026 };
            }
            else if (lower.Contains("qwen3") && lower.Contains("voicedesign"))
            {
                id = "qwen3-design"; family = "qwen3_tts"; task = "vdes";
            }
            else if (lower.Contains("qwen3") && lower.Contains("customvoice"))
            {
                id = "qwen3-custom"; family = "qwen3_tts"; task = "tts";
            }
            else if (lower.Contains("qwen3") && lower.Contains("1.7b"))
            {
                id = "qwen3-1.7b"; family = "qwen3_tts"; task = "tts";
            }
            else if (lower.Contains("qwen3"))
            {
                id = "qwen3-clone"; family = "qwen3_tts"; task = "tts";
            }
            else if (lower.Contains("index-tts2_5") || lower.Contains("indextts2_5") || lower.Contains("indextts2.5"))
            {
                id = "indextts-2_5"; family = "index_tts2"; task = "tts";
            }
            else if (lower.Contains("index-tts2") || lower.Contains("indextts2"))
            {
                id = "indextts-2"; family = "index_tts2"; task = "tts";
            }
            else if (lower.Contains("cosyvoice3"))
            {
                id = "cosyvoice-3"; family = "cosyvoice3"; task = "clon";
            }
            else if (lower.Contains("voxcpm2"))
            {
                id = "voxcpm-2"; family = "voxcpm2"; task = "tts";
            }
            else if (lower.Contains("kokoro"))
            {
                id = "kokoro"; family = "kokoro"; task = "tts";
            }
            else if (lower.Contains("pocket"))
            {
                id = "pocket-tts"; family = "pocket_tts"; task = "tts";
            }
            else if (lower.Contains("breeze"))
            {
                id = "breeze-tts"; family = "breeze_tts"; task = "tts";
            }
            else if (lower.Contains("fun-asr-nano") || lower.Contains("fun_asr_nano"))
            {
                id = "fun-asr-nano"; family = "fun_asr_nano"; task = "asr";
                opts = new Dictionary<string, object>();
            }
            else if (lower.Contains("qwen3-asr") || lower.Contains("qwen3_asr"))
            {
                id = "qwen3-asr"; family = "qwen3_asr"; task = "asr";
                opts = new Dictionary<string, object>();
            }

            if (id is not null && family is not null)
            {
                results.Add(new InstalledModelInfo
                {
                    Id = id,
                    Family = family,
                    Task = task,
                    FileName = fileName,
                    FullPath = file,
                    DefaultRequestOptions = opts
                });
            }
        }

        return results;
    }
}

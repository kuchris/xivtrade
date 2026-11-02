using System;
using Dalamud.Configuration;

namespace XivTrade;

[Serializable]
internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool CompletionSoundEnabled { get; set; } = true;
}

namespace FalconBMS.Launcher.Models;

/// <summary>
/// Users Community Tools state layered on top of Stock\ThirdPartyTools.json
/// </summary>
public sealed class ThirdPartyToolsUserState
{
    public int Version { get; set; } = 1;

    public List<string> Order { get; set; } = new();

    public List<string> Hidden { get; set; } = new();

    public Dictionary<string, string> Overrides { get; set; } = new();

    public List<ThirdPartyCustomTool> Custom { get; set; } = new();
}

public sealed class ThirdPartyCustomTool
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string ExecutablePath { get; set; } = "";
}
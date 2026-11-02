using System;
using System.Globalization;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace XivTrade;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static ITargetManager Targets { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windows = new("xivtrade");
    private readonly MainWindow window;
    private readonly TradeClient client;
    internal TradeSession Session { get; } = new();
    internal string PartnerName { get; private set; } = "Any player";
    internal string TargetName => SelectedPlayer?.Name.TextValue ?? "Select a player";
    private IPlayerCharacter? SelectedPlayer => Targets.Target is IPlayerCharacter p && p.EntityId != Objects.LocalPlayer?.EntityId ? p : null;

    public Plugin()
    {
        client = new TradeClient(GameGui, ClientState);
        window = new MainWindow(this);
        windows.AddWindow(window);
        Framework.Update += Update;
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += Toggle;
        var command = new CommandInfo(OnCommand)
        {
            HelpMessage = "Open xivtrade: /xivtrade | send <gil> | receive | stop",
        };
        Commands.AddHandler("/xivtrade", command);
        Commands.AddHandler("/giltrade", command);
    }

    internal void Start(TradeMode mode, int amount)
    {
        var player = SelectedPlayer;
        try
        {
            if (Session.Start(mode, amount, player?.EntityId ?? 0, client.Read(), Environment.TickCount64))
                PartnerName = mode == TradeMode.Receive ? "Any player" : player?.Name.TextValue ?? "Select a player";
        }
        catch (Exception ex) { HandleError(ex); }
    }

    internal void Stop()
    {
        if (!Session.Running) return;
        Session.Stop();
        try { client.Execute(TradeStep.Cancel, Session); }
        catch (Exception ex) { Log.Error(ex, "Could not cancel the open trade."); }
    }

    private void Update(IFramework framework)
    {
        if (!Session.Running) return;
        try
        {
            // Requests use the pinned entity ID, independently of the current target.
            if (Session.Mode == TradeMode.Send && Objects.SearchByEntityId(Session.RecipientId) is not IPlayerCharacter)
            {
                Stop();
                Session.Stop("The recipient is no longer nearby. Automation stopped.");
                return;
            }
            client.Execute(Session.Tick(client.Read(), Environment.TickCount64), Session);
        }
        catch (Exception ex) { HandleError(ex); }
    }

    private void HandleError(Exception ex)
    {
        Session.Stop("Trade automation stopped after an error. Check /xllog.");
        Log.Error(ex, "xivtrade automation error");
    }

    private void OnCommand(string command, string args)
    {
        var parts = args.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) { Toggle(); return; }
        if (parts[0].Equals("stop", StringComparison.OrdinalIgnoreCase)) { Stop(); return; }
        if (parts[0].Equals("receive", StringComparison.OrdinalIgnoreCase)) Start(TradeMode.Receive, 0);
        else
        {
            var input = parts[0].Equals("send", StringComparison.OrdinalIgnoreCase) && parts.Length == 2 ? parts[1] : args;
            if (!int.TryParse(input, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var amount))
            {
                Chat.PrintError("Usage: /xivtrade send <gil> | receive | stop");
                window.IsOpen = true;
                return;
            }
            Start(TradeMode.Send, amount);
        }
        window.IsOpen = true;
        if (!Session.Running) Chat.PrintError(Session.Status);
    }

    private void Toggle() => window.Toggle();

    public void Dispose()
    {
        Stop();
        Framework.Update -= Update;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= Toggle;
        Commands.RemoveHandler("/xivtrade");
        Commands.RemoveHandler("/giltrade");
        windows.RemoveAllWindows();
    }
}

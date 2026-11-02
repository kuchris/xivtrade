using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace XivTrade;

internal sealed class MainWindow(Plugin plugin) : Window("xivtrade##Main")
{
    private int amount = 1_000_000;
    private int mode;
    private bool receiveTarget;

    public override void Draw()
    {
        var session = plugin.Session;
        if (session.Running)
        {
            mode = (int)session.Mode;
            if (mode == 1) receiveTarget = session.Total > 0;
            if (session.Total > 0) amount = session.Total;
        }
        ImGui.BeginDisabled(session.Running);
        ImGui.RadioButton("Send", ref mode, 0);
        ImGui.SameLine();
        ImGui.RadioButton("Receive", ref mode, 1);
        ImGui.Spacing();
        if (mode == 0)
        {
            ImGui.TextUnformatted($"To: {(session.Running ? plugin.PartnerName : plugin.TargetName)}");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputInt("##gil", ref amount, 100_000, 1_000_000);
            amount = Math.Clamp(amount, 1, TradeSession.MaxGil);
            ImGui.TextDisabled("gil · automatically split into trades of up to 1,000,000");
        }
        else
        {
            ImGui.TextUnformatted("From: any player");
            ImGui.Checkbox("Stop at target amount", ref receiveTarget);
            if (receiveTarget)
            {
                ImGui.SetNextItemWidth(-1);
                ImGui.InputInt("##receiveGil", ref amount, 100_000, 1_000_000);
                amount = Math.Clamp(amount, 1, TradeSession.MaxGil);
                ImGui.TextWrapped("Receive this total, then stop. Offers above the remaining target are canceled.");
            }
            else
                ImGui.TextWrapped("Accept pure gil trades automatically until you press Stop.");
        }
        ImGui.Spacing();
        var completionSound = plugin.Settings.CompletionSoundEnabled;
        if (ImGui.Checkbox("Completion sound", ref completionSound))
        {
            plugin.Settings.CompletionSoundEnabled = completionSound;
            plugin.SaveSettings();
        }
        ImGui.SameLine();
        if (ImGui.Button("Test")) plugin.PlayCompletionSound();
        ImGui.EndDisabled();
        ImGui.Spacing();
        if (session.Running)
        {
            if (ImGui.Button("Stop", new Vector2(-1, 0))) plugin.Stop();
        }
        else if (ImGui.Button(mode == 0 ? "Send gil" : "Start receiving", new Vector2(-1, 0)))
            plugin.Start((TradeMode)mode, mode == 1 && !receiveTarget ? 0 : amount);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (session.Total > 0)
            ImGui.ProgressBar((float)session.Completed / session.Total, new Vector2(-1, 0), $"{session.Completed:N0} / {session.Total:N0} gil");
        else
            ImGui.TextUnformatted($"Received: {session.Completed:N0} gil · {session.Trades:N0} trades");
        ImGui.TextWrapped(session.Status);
    }

    public override void OnOpen()
    {
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(390, 300), MaximumSize = new Vector2(600, 500) };
        Size = new Vector2(420, 330);
        SizeCondition = ImGuiCond.FirstUseEver;
    }
}

using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace XivTrade;

internal sealed unsafe class TradeClient(IGameGui gui, IClientState clientState)
{
    internal TradeSnapshot Read()
    {
        if (!clientState.IsLoggedIn) return new(false, false, 0, 0);
        var inventory = InventoryManager.Instance();
        if (inventory == null) return new(false, false, 0, 0);
        var trade = (AtkUnitBase*)gui.GetAddonByName("Trade").Address;
        var dialog = (AddonSelectYesno*)gui.GetAddonByName("SelectYesno").Address;
        var open = trade != null && trade->IsVisible;
        var dialogOpen = dialog != null && dialog->IsVisible;
        // Never confirm an arbitrary SelectYesno. Only a dialog owned by this Trade addon.
        var tradeDialog = open && dialogOpen && trade->Id != 0 &&
            (dialog->ParentId == trade->Id || dialog->HostId == trade->Id || dialog->BlockedParentId == trade->Id);
        var items = false;
        if (open)
            for (var i = 0; i < 5; i++)
                items |= inventory->TradeItemsLocal[i].ItemId != 0 || inventory->TradeItemsRemote[i].ItemId != 0;
        return new(true, open, inventory->TradePartnerEntityId, inventory->GetGil(),
            (int)inventory->TradeLocalState, (int)inventory->TradeRemoteState,
            open ? inventory->TradeItemsLocal[5].Quantity : 0,
            open ? inventory->TradeItemsRemote[5].Quantity : 0,
            items, inventory->TradeIsSyncPending, dialogOpen, tradeDialog);
    }

    internal void Execute(TradeStep step, TradeSession session)
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null) return;
        switch (step)
        {
            case TradeStep.Open:
                inventory->SendTradeRequest(session.RecipientId);
                break;
            case TradeStep.SetGil:
                inventory->SetTradeGilAmount((uint)System.Math.Min(session.Remaining, TradeSession.PerTrade));
                break;
            case TradeStep.Lock:
                AgentEvent(4);
                break;
            case TradeStep.Confirm:
                AgentEvent(0);
                break;
            case TradeStep.AcceptDialog:
                if (!Read().TradeDialog) return;
                var dialog = (AtkUnitBase*)gui.GetAddonByName("SelectYesno").Address;
                var yesno = (AddonSelectYesno*)dialog;
                if (yesno->YesButton == null || !yesno->YesButton->IsEnabled) return;
                var yes = new AtkValue { Type = AtkValueType.Int, Int = 0 };
                dialog->FireCallback(1, &yes);
                break;
            case TradeStep.Cancel:
                if (Read().TradeOpen) inventory->RefuseTrade();
                break;
        }
    }

    private void AgentEvent(int command)
    {
        var addon = (AtkUnitBase*)gui.GetAddonByName("Trade").Address;
        var agent = (AgentInterface*)gui.FindAgentInterface("Trade").Address;
        if (addon == null || !addon->IsVisible || agent == null) return;
        // The Trade agent expects two values. Allocate both, rather than reading past one value.
        var values = stackalloc AtkValue[2];
        values[0] = new AtkValue { Type = AtkValueType.Int, Int = command };
        values[1] = new AtkValue { Type = AtkValueType.Int, Int = 0 };
        var result = new AtkValue();
        agent->ReceiveEvent(&result, values, 2, 0);
    }
}

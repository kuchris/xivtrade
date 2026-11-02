using System.Globalization;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace XivTrade;

internal sealed unsafe class TradeClient(IGameGui gui, IClientState clientState, IDataManager data)
{
    // Addon row for the final "Complete trade?" prompt, in the client's language.
    private const uint CompleteTradeRow = 102223;
    // Trade window nodes that show each side's gil: our gil field (component 13, text 2) and the partner's text 31.
    private const uint LocalGilNode = 13;
    private const uint RemoteGilNode = 31;
    private string? completeTradePrompt;

    internal TradeSnapshot Read()
    {
        if (!clientState.IsLoggedIn) return new(false, false, 0, 0);
        var inventory = InventoryManager.Instance();
        if (inventory == null) return new(false, false, 0, 0);
        var trade = (AtkUnitBase*)gui.GetAddonByName("Trade").Address;
        var dialog = (AddonSelectYesno*)gui.GetAddonByName("SelectYesno").Address;
        var open = trade != null && trade->IsVisible;
        var dialogOpen = dialog != null && dialog->IsVisible;
        // Never confirm an arbitrary SelectYesno. Only the trade's own "Complete trade?" prompt.
        var tradeDialog = open && dialogOpen &&
            ((trade->Id != 0 && (dialog->ParentId == trade->Id || dialog->HostId == trade->Id || dialog->BlockedParentId == trade->Id)) ||
             IsCompleteTradePrompt(dialog));
        var items = false;
        if (open)
            for (var i = 0; i < 5; i++)
                items |= inventory->TradeItemsLocal[i].ItemId != 0 || inventory->TradeItemsRemote[i].ItemId != 0;
        // Offered gil is not reliably mirrored into the trade item data. Prefer what the window shows.
        return new(true, open, inventory->TradePartnerEntityId, inventory->GetGil(),
            (int)inventory->TradeLocalState, (int)inventory->TradeRemoteState,
            open ? ReadLocalGil(trade) ?? inventory->TradeItemsLocal[5].Quantity : 0,
            open ? ParseGil(trade->GetTextNodeById(RemoteGilNode)) ?? inventory->TradeItemsRemote[5].Quantity : 0,
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
                // Trade window callbacks: 0 = Trade, 1 = Cancel.
                var trade = (AtkUnitBase*)gui.GetAddonByName("Trade").Address;
                if (trade == null || !trade->IsVisible) return;
                trade->FireCallbackInt(0);
                break;
            case TradeStep.AcceptDialog:
                if (!Read().TradeDialog) return;
                var dialog = (AtkUnitBase*)gui.GetAddonByName("SelectYesno").Address;
                var yesno = (AddonSelectYesno*)dialog;
                if (yesno->YesButton == null || !yesno->YesButton->IsEnabled) return;
                var yes = new AtkValue { Type = AtkValueType.Int, Int = 0 };
                // Close the prompt as a button click would, or it stays on screen after the trade.
                dialog->FireCallback(1, &yes, true);
                break;
            case TradeStep.Cancel:
                if (Read().TradeOpen) inventory->RefuseTrade();
                break;
        }
    }

    private bool IsCompleteTradePrompt(AddonSelectYesno* dialog)
    {
        completeTradePrompt ??= data.GetExcelSheet<Addon>().GetRowOrDefault(CompleteTradeRow)?.Text.ExtractText().Trim() ?? string.Empty;
        var prompt = dialog->PromptText;
        return completeTradePrompt.Length > 0 && prompt != null && prompt->NodeText.ToString().Contains(completeTradePrompt);
    }

    private static int? ReadLocalGil(AtkUnitBase* trade)
    {
        var field = trade->GetNodeById(LocalGilNode);
        var component = field == null ? null : field->GetAsAtkComponentNode();
        return component == null || component->Component == null ? null : ParseGil(component->Component->GetTextNodeById(2));
    }

    private static int? ParseGil(AtkTextNode* node)
    {
        if (node == null) return null;
        var text = node->NodeText.ToString().Trim();
        // Digits with thousands separators only. Anything else is not a gil amount.
        if (text.Any(c => !char.IsAsciiDigit(c) && c is not (',' or '.') && !char.IsWhiteSpace(c))) return null;
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var gil) ? gil : null;
    }
}

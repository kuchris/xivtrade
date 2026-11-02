using System;

namespace XivTrade;

internal enum TradeMode { Send, Receive }
internal enum TradeStep { None, Open, SetGil, Lock, AcceptDialog, Cancel, Completed }

// Values match the client's TradeState enum. Kept independent for offline verification.
internal readonly record struct TradeSnapshot(
    bool LoggedIn, bool TradeOpen, uint PartnerId, long Wallet,
    int LocalState = 1, int RemoteState = 1, int LocalGil = 0, int RemoteGil = 0,
    bool HasItems = false, bool SyncPending = false,
    bool DialogOpen = false, bool TradeDialog = false);

internal sealed class TradeSession
{
    internal const int MaxGil = 999_999_999;
    internal const int PerTrade = 1_000_000;
    private long deadline;
    private long nextAction;
    private long stableSince;
    private long walletBefore;
    private long lockedAt;
    private uint currentPartner;
    private int offer;
    private int lockedOffer;
    private bool sawWindow;
    private bool lockRequested;
    private bool confirmRequested;
    private bool settling;
    private long settleDeadline;

    internal bool Running { get; private set; }
    internal TradeMode Mode { get; private set; }
    internal uint RecipientId { get; private set; }
    internal int Total { get; private set; }
    internal long Completed { get; private set; }
    internal int Trades { get; private set; }
    internal int Remaining => (int)Math.Max(0, Total - Completed);
    internal string Status { get; private set; } = "Ready.";

    internal bool Start(TradeMode mode, int amount, uint recipient, TradeSnapshot s, long now)
    {
        if (Running) return false;
        if (!s.LoggedIn) return Fail("Log in first.");
        if (s.TradeOpen || s.DialogOpen) return Fail("Close the current trade or dialog first.");
        if (mode == TradeMode.Send && (recipient == 0 || amount <= 0 || amount > MaxGil))
            return Fail("Select a player and enter a valid gil amount.");
        if (mode == TradeMode.Send && s.Wallet < amount) return Fail("Not enough gil.");
        if (mode == TradeMode.Receive && (amount < 0 || amount > MaxGil))
            return Fail("Enter a valid receive target, or zero for unlimited receiving.");
        if (mode == TradeMode.Receive && s.Wallet >= MaxGil) return Fail("Your wallet is full.");
        if (mode == TradeMode.Receive && amount > MaxGil - s.Wallet)
            return Fail("The receive target exceeds your wallet's remaining capacity.");
        Mode = mode;
        RecipientId = recipient;
        Total = amount;
        Completed = 0;
        Trades = 0;
        ResetTrade(now);
        Running = true;
        Status = mode == TradeMode.Send ? "Opening trade…" : "Waiting for gil from any player…";
        return true;
    }

    internal void Stop(string message = "Stopped. Completed trades are shown below.")
    {
        Running = false;
        Status = message;
    }

    private bool Fail(string message) { Status = message; return false; }

    private void ResetTrade(long now)
    {
        sawWindow = lockRequested = confirmRequested = settling = false;
        currentPartner = 0;
        offer = lockedOffer = 0;
        stableSince = now;
        deadline = now + 120_000;
        nextAction = now + 600;
    }

    internal TradeStep Tick(TradeSnapshot s, long now)
    {
        if (!Running) return TradeStep.None;
        if (!s.LoggedIn) { Stop("Logged out. Automation stopped."); return TradeStep.None; }

        if (!s.TradeOpen)
        {
            if (sawWindow)
            {
                if (!settling) { settling = true; settleDeadline = now + 5_000; }
                var delta = Mode == TradeMode.Send ? walletBefore - s.Wallet : s.Wallet - walletBefore;
                if (lockedOffer > 0 && delta == lockedOffer && confirmRequested)
                {
                    Completed += lockedOffer;
                    Trades++;
                    if (Total > 0 && Remaining == 0)
                    {
                        var action = Mode == TradeMode.Send ? "Sent" : "Received";
                        Stop($"{action} {Completed:N0} gil in {Trades:N0} trades.");
                        return TradeStep.Completed;
                    }
                    ResetTrade(now);
                    if (Mode == TradeMode.Receive && s.Wallet >= MaxGil)
                        Stop("Wallet full. Receiving stopped.");
                    else
                        Status = Mode == TradeMode.Send ? "Opening next trade…" : "Gil received. Waiting for the next player…";
                }
                else if (now >= settleDeadline)
                    Stop("Trade canceled or gil change could not be verified. Automation stopped.");
                return TradeStep.None;
            }
            if (Mode == TradeMode.Receive) return TradeStep.None;
            if (now >= deadline) { Stop("Trade request timed out."); return TradeStep.None; }
            if (now < nextAction || s.DialogOpen) return TradeStep.None;
            nextAction = now + 10_000;
            return TradeStep.Open;
        }

        if (settling) { Stop("A new trade opened before the last result was verified."); return TradeStep.None; }
        if (!sawWindow)
        {
            if (s.PartnerId == 0) return TradeStep.None;
            currentPartner = s.PartnerId;
            walletBefore = s.Wallet;
            sawWindow = true;
            deadline = now + 120_000;
            nextAction = now;
        }
        if (s.PartnerId != currentPartner || (Mode == TradeMode.Send && s.PartnerId != RecipientId))
            return Reject("Trade partner changed. Automation stopped.");
        if (now >= deadline) return Reject("Trade timed out. Automation stopped.");
        if (s.HasItems) return Reject("Items detected. Only pure gil trades are supported.");
        if (s.SyncPending) return TradeStep.None;

        var amount = Mode == TradeMode.Send ? Math.Min(Remaining, PerTrade) : s.RemoteGil;
        if (Mode == TradeMode.Receive && s.LocalGil != 0)
            return Reject("Outgoing gil detected while receiving. Automation stopped.");
        if (Mode == TradeMode.Send && s.RemoteGil != 0)
            return Reject("Incoming gil detected while sending. Automation stopped.");
        if (amount < 0 || amount > PerTrade || (Mode == TradeMode.Receive && s.Wallet + amount > MaxGil))
            return Reject("The offered gil exceeds the trade or wallet limit.");
        if (Mode == TradeMode.Receive && Total > 0 && amount > Remaining)
            return Reject($"Offer exceeds the remaining receive target of {Remaining:N0} gil. Automation stopped.");
        if (Mode == TradeMode.Send && s.Wallet < amount)
            return Reject("Not enough gil for the next trade.");
        if (lockRequested && (amount != lockedOffer || s.LocalGil != (Mode == TradeMode.Send ? lockedOffer : 0)))
            return Reject("The offer changed after approval. Automation stopped.");
        if (s.DialogOpen && !s.TradeDialog)
        {
            Status = "Close the unrelated dialog to continue.";
            return TradeStep.None;
        }
        if (now < nextAction) return TradeStep.None;

        if (Mode == TradeMode.Send && s.LocalState == 3 && s.LocalGil != amount)
        {
            // Each change makes the partner press Trade again, so give the window time to show the amount.
            nextAction = now + 2_000;
            Status = $"Offering {amount:N0} gil…";
            return TradeStep.SetGil;
        }
        if (amount == 0 || s.LocalState < 3)
        {
            if (Mode == TradeMode.Receive) Status = "Trade opened. Waiting for the sender's gil offer…";
            return TradeStep.None;
        }
        if (offer != amount) { offer = amount; stableSince = now; }
        if (now - stableSince < 600) return TradeStep.None;
        if (Mode == TradeMode.Receive && s.RemoteState < 4)
        {
            Status = "Waiting for the sender to lock their gil offer…";
            return TradeStep.None;
        }
        // Press Trade once the offer is stable. Press again if the first click did not register.
        if (s.LocalState == 3 && (!lockRequested || now - lockedAt >= 3_000))
        {
            lockedOffer = amount;
            lockRequested = true;
            lockedAt = now;
            nextAction = now + 600;
            Status = $"Confirming {amount:N0} gil…";
            return TradeStep.Lock;
        }
        // Once both sides pressed Trade, the game asks "Complete trade?".
        if (lockRequested && s.LocalState >= 4 && s.RemoteState >= 4 && s.TradeDialog)
        {
            confirmRequested = true;
            nextAction = now + 600;
            return TradeStep.AcceptDialog;
        }
        return TradeStep.None;
    }

    private TradeStep Reject(string message) { Stop(message); return TradeStep.Cancel; }
}

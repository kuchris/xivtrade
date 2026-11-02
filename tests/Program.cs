using XivTrade;

var count = 0;
void Check(bool value, string label)
{
    if (!value) throw new Exception(label);
    count++;
}

TradeSnapshot Idle(long wallet = 5_000_000) => new(true, false, 0, wallet);
TradeSnapshot Offer(int gil, uint partner = 42, long wallet = 5_000_000, bool receive = false) =>
    new(true, true, partner, wallet, 3, receive ? 4 : 3, receive ? 0 : gil, receive ? gil : 0);

TradeSession Start(TradeMode mode = TradeMode.Send, int? total = null, long wallet = 5_000_000)
{
    var session = new TradeSession();
    Check(session.Start(mode, total ?? (mode == TradeMode.Send ? 2_500_000 : 0), 42, Idle(wallet), 0), "start");
    return session;
}

long Finish(TradeSession s, int amount, long wallet, long now, uint partner = 42, bool completesTarget = false)
{
    var offer = Offer(amount, partner, wallet, s.Mode == TradeMode.Receive);
    Check(s.Tick(offer, now) == TradeStep.None, "wait for stable offer");
    Check(s.Tick(offer, now + 601) == TradeStep.Lock, "lock stable gil");
    var locked = offer with { LocalState = 4, RemoteState = 4 };
    Check(s.Tick(locked, now + 1202) == TradeStep.Confirm, "confirm locked trade");
    Check(s.Tick(locked with { DialogOpen = true, TradeDialog = true }, now + 1803) == TradeStep.AcceptDialog, "accept trade-owned dialog");
    var after = wallet + (s.Mode == TradeMode.Receive ? amount : -amount);
    Check(s.Tick(Idle(after), now + 2404) == (completesTarget ? TradeStep.Completed : TradeStep.None), "completion notification only at verified target");
    return now + 3005;
}

// Sending 2.5m uses three actual, verified transfers; no optimistic progress.
var send = Start();
Check(send.Tick(Idle(), 601) == TradeStep.Open, "open pinned recipient");
Check(send.RecipientId == 42, "recipient pinned");
Check(send.Tick(Offer(0), 700) == TradeStep.SetGil, "set gil after window ready");
var tick = Finish(send, 1_000_000, 5_000_000, 1400);
Check(send.Running && send.Completed == 1_000_000 && send.Remaining == 1_500_000, "first transfer only counted once verified");
Check(send.Tick(Idle(4_000_000), tick) == TradeStep.Open, "next trade opens");
tick = Finish(send, 1_000_000, 4_000_000, tick + 700);
Finish(send, 500_000, 3_000_000, tick + 700, completesTarget: true);
Check(!send.Running && send.Completed == 2_500_000 && send.Trades == 3, "send total and stop");
Check(send.Tick(Idle(2_500_000), tick + 9999) == TradeStep.None, "completion notification is not repeated");
Check(send.Completed == 2_500_000, "no double completion");

// The receiver listens indefinitely, handles different players, and never offers gil.
var receive = Start(TradeMode.Receive);
Check(receive.Tick(Idle(), 900_000) == TradeStep.None && receive.Running, "receive waits indefinitely");
tick = Finish(receive, 123, 5_000_000, 900_001, 88);
Finish(receive, 456, 5_000_123, tick, 99);
Check(receive.Running && receive.Completed == 579 && receive.Trades == 2, "different senders accepted");
receive.Stop();
Check(receive.Tick(Offer(1, receive: true), tick + 20_000) == TradeStep.None, "stop suppresses future work");
Check(receive.Completed == 579, "stop preserves totals");

foreach (var amount in new[] { 1, 999_999, 1_000_000 })
{
    var single = Start(total: amount);
    Finish(single, amount, 5_000_000, 700, completesTarget: true);
    Check(!single.Running && single.Completed == amount, "boundary transfer");
}

foreach (var bad in new[]
{
    Offer(1_000_000) with { PartnerId = 99 },
    Offer(1_000_000) with { HasItems = true },
    Offer(1_000_000) with { RemoteGil = 1 },
})
{
    var s = Start();
    Check(s.Tick(bad, 700) == TradeStep.Cancel && !s.Running && s.Completed == 0, "reject unsafe send");
}

foreach (var bad in new[]
{
    Offer(100, receive: true) with { LocalGil = 1 },
    Offer(100, receive: true) with { HasItems = true },
    Offer(1_000_001, receive: true),
    Offer(1, receive: true) with { RemoteGil = -1 },
})
{
    var s = Start(TradeMode.Receive);
    Check(s.Tick(bad, 700) == TradeStep.Cancel && !s.Running && s.Completed == 0, "reject unsafe receive");
}

var notLocked = Start(TradeMode.Receive);
var pending = Offer(100, receive: true) with { RemoteState = 3 };
notLocked.Tick(pending, 700);
Check(notLocked.Tick(pending, 1400) == TradeStep.None, "receiver waits for sender lock");
Check(notLocked.Tick(pending with { RemoteGil = 0 }, 2100) == TradeStep.None, "zero gil never locks");

var changed = Start(TradeMode.Receive);
changed.Tick(Offer(100, receive: true), 700);
Check(changed.Tick(Offer(100, receive: true), 1400) == TradeStep.Lock, "lock receive offer");
Check(changed.Tick(Offer(101, receive: true) with { LocalState = 4 }, 2100) == TradeStep.Cancel, "changed locked offer rejected");

var partnerChanged = Start(TradeMode.Receive);
partnerChanged.Tick(Offer(100, receive: true), 700);
Check(partnerChanged.Tick(Offer(100, partner: 99, receive: true), 800) == TradeStep.Cancel, "same-window partner change rejected");

var canceled = Start(total: 1_000_000);
canceled.Tick(Offer(1_000_000), 700);
canceled.Tick(Offer(1_000_000), 1400);
canceled.Tick(Idle(), 2100);
Check(canceled.Completed == 0 && canceled.Remaining == 1_000_000, "closed window is not success");
canceled.Tick(Idle(), 7200);
Check(!canceled.Running && canceled.Completed == 0, "cancellation stops after settlement grace");

var late = Start(total: 1_000_000);
late.Tick(Offer(1_000_000), 700);
late.Tick(Offer(1_000_000), 1400);
late.Tick(Offer(1_000_000) with { LocalState = 4, RemoteState = 4 }, 2100);
late.Tick(Idle(), 2800);
Check(late.Completed == 0, "await delayed wallet update");
Check(late.Tick(Idle(4_000_000), 5000) == TradeStep.Completed, "delayed wallet update triggers completion notification");
Check(late.Completed == 1_000_000 && !late.Running, "delayed wallet update verified");

var dialog = Start();
Check(dialog.Tick(Offer(1_000_000) with { DialogOpen = true }, 700) == TradeStep.None, "unrelated yesno is never accepted");
Check(dialog.Tick(Offer(1_000_000) with { SyncPending = true }, 1400) == TradeStep.None, "server sync pending waits");
dialog.Tick(Offer(1_000_000), 2100);
dialog.Tick(Offer(1_000_000), 2800);
Check(dialog.Tick(Offer(1_000_000) with { LocalState = 4, RemoteState = 4, DialogOpen = true }, 3500) == TradeStep.None, "unrelated dialog blocked after locking too");

var timeout = Start();
timeout.Tick(Idle(), 120_001);
Check(!timeout.Running, "send request timeout");
var windowTimeout = Start(TradeMode.Receive);
windowTimeout.Tick(Offer(100, receive: true), 700);
Check(windowTimeout.Tick(Offer(100, receive: true), 120_701) == TradeStep.Cancel, "open trade timeout");
var logout = Start();
logout.Tick(new(false, false, 0, 0), 700);
Check(!logout.Running, "logout stops");

var invalid = new TradeSession();
Check(!invalid.Start(TradeMode.Send, 0, 42, Idle(), 0), "reject zero send");
Check(!invalid.Start(TradeMode.Send, -1, 42, Idle(), 0), "reject negative send");
Check(!invalid.Start(TradeMode.Send, 6_000_000, 42, Idle(), 0), "reject insufficient wallet");
Check(!invalid.Start(TradeMode.Send, 1, 0, Idle(), 0), "reject missing recipient");
Check(!invalid.Start(TradeMode.Send, 1, 42, Idle() with { TradeOpen = true }, 0), "reject existing trade");
Check(!invalid.Start(TradeMode.Receive, 0, 0, Idle() with { DialogOpen = true }, 0), "reject existing dialog");
Check(!invalid.Start(TradeMode.Receive, 0, 0, Idle(TradeSession.MaxGil), 0), "reject full wallet");
Check(!invalid.Start(TradeMode.Send, 1, 42, new(false, false, 0, 0), 0), "reject logged out");
var running = Start();
Check(!running.Start(TradeMode.Receive, 0, 0, Idle(), 100), "cannot replace running session");

var full = Start(TradeMode.Receive, wallet: TradeSession.MaxGil - 100);
Finish(full, 100, TradeSession.MaxGil - 100, 700);
Check(!full.Running && full.Completed == 100, "stop when wallet reaches cap");
var overflow = Start(TradeMode.Receive, wallet: TradeSession.MaxGil - 99);
Check(overflow.Tick(Offer(100, wallet: TradeSession.MaxGil - 99, receive: true), 700) == TradeStep.Cancel, "wallet overflow rejected");

// A configured receive target completes once, across multiple trades and different senders.
var targetReceive = Start(TradeMode.Receive, total: 1_000);
tick = Finish(targetReceive, 400, 5_000_000, 700, partner: 88);
Check(targetReceive.Running && targetReceive.Completed == 400 && targetReceive.Remaining == 600, "partial receive does not complete target");
Finish(targetReceive, 600, 5_000_400, tick, partner: 99, completesTarget: true);
Check(!targetReceive.Running && targetReceive.Completed == 1_000 && targetReceive.Trades == 2, "receive target stops at verified total");
Check(targetReceive.Status.StartsWith("Received 1,000"), "receive success status is preserved");
Check(targetReceive.Tick(Idle(5_001_000), tick + 10_000) == TradeStep.None, "receive completion notification is not repeated");

// An overpayment cannot trigger completion or silently exceed the requested total.
var tooMuch = Start(TradeMode.Receive, total: 1_000);
tick = Finish(tooMuch, 400, 5_000_000, 700);
Check(tooMuch.Tick(Offer(601, wallet: 5_000_400, receive: true), tick) == TradeStep.Cancel, "overpayment beyond remaining target is canceled");
Check(!tooMuch.Running && tooMuch.Completed == 400 && tooMuch.Remaining == 600, "overpayment does not count as completed");
Check(tooMuch.Tick(Idle(5_000_400), tick + 1000) == TradeStep.None, "overpayment does not raise completion afterward");

var receiveCanceled = Start(TradeMode.Receive, total: 100);
receiveCanceled.Tick(Offer(100, receive: true), 700);
receiveCanceled.Tick(Offer(100, receive: true), 1400);
receiveCanceled.Tick(Offer(100, receive: true) with { LocalState = 4 }, 2100);
Check(receiveCanceled.Tick(Idle(), 2800) == TradeStep.None, "closed receive window without gil does not notify");
Check(receiveCanceled.Tick(Idle(), 7900) == TradeStep.None && !receiveCanceled.Running, "canceled receive target stops without completion");

var stopped = Start(total: 100);
stopped.Tick(Offer(100), 700);
stopped.Tick(Offer(100), 1400);
stopped.Stop();
Check(stopped.Tick(Idle(4_999_900), 2100) == TradeStep.None && stopped.Completed == 0, "manual stop does not trigger completion sound");

var restart = Start(total: 5);
tick = Finish(restart, 5, 5_000_000, 700, completesTarget: true);
Check(restart.Start(TradeMode.Send, 7, 42, Idle(4_999_995), tick), "start a new sequence after completion");
Finish(restart, 7, 4_999_995, tick + 700, completesTarget: true);
Check(restart.Completed == 7 && restart.Trades == 1, "new sequence gets its own completion notification");

var exactCap = Start(TradeMode.Receive, total: 100, wallet: TradeSession.MaxGil - 100);
Finish(exactCap, 100, TradeSession.MaxGil - 100, 700, completesTarget: true);
Check(!exactCap.Running && exactCap.Completed == 100, "target at wallet capacity still completes normally");
Check(!invalid.Start(TradeMode.Receive, -1, 0, Idle(), 0), "reject negative receive target");
Check(!invalid.Start(TradeMode.Receive, TradeSession.MaxGil + 1, 0, Idle(), 0), "reject excessive receive target");
Check(!invalid.Start(TradeMode.Receive, 100, 0, Idle(TradeSession.MaxGil - 99), 0), "reject receive target above available wallet capacity");

Console.WriteLine($"Passed {count} trade session checks. Native game interaction requires live validation.");

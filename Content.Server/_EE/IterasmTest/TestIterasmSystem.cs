using System.Text;
using Content.Server.DeviceLinking.Events;
using Content.Server.Popups;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceNetwork;
using Content.Shared.Paper;
using Robust.Shared.Timing;

namespace Content.Server._EE.IterasmTest;

public sealed class TestIterasmSystem : EntitySystem
{
    public const string TestIterasmSignalValueKey = "test-signal-value";

    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedDeviceLinkSystem _signal = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<TestIterasmComponent, ComponentInit>((ent, comp, ev) => comp.IterasmState = new(comp));
        SubscribeLocalEvent<TestIterasmComponent, ComponentRemove>((ent, comp, ev) => comp.IterasmState?.Dispose());
        SubscribeLocalEvent<TestIterasmComponent, SignalReceivedEvent>(OnSignalReceived);

        base.Initialize();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<TestIterasmComponent, PaperComponent>();
        while (query.MoveNext(out var ent, out var iterasm, out var paper))
        {
            HandleQueue(new(ent, iterasm));
            HandleCompilation(new(ent, iterasm, paper));
            HandleExecution(new(ent, iterasm));
        }
    }

    private void HandleQueue(Entity<TestIterasmComponent> ent)
    {
        var iterasm = ent.Comp;

        while (iterasm.OutgoingQueue.TryDequeue(out var item))
        {
            var (port, value) = item;
            _signal.InvokePort(ent.Owner, port, new NetworkPayload { [TestIterasmSignalValueKey] = value });
            _popupSystem.PopupEntity($"Signal emitted on port {port} with value {value}", ent.Owner);
        }
    }

    private void HandleCompilation(Entity<TestIterasmComponent, PaperComponent> ent)
    {
        var iterasm = ent.Comp1;
        var paper = ent.Comp2;

        if (iterasm.CurrentState == ExecutionState.Idle)
        {
            if (paper.Content != string.Empty)
            {
                Log.Debug($"TestIterasmSystem: Compiling Iterasm for {ent} with content: {paper.Content}");
                iterasm.IterasmState.Compile(paper.Content);
                iterasm.CurrentState = ExecutionState.Running;
                iterasm.NextExecution = _timing.CurTime + TimeSpan.FromMilliseconds(iterasm.ExecuteTime);
            }
        }

        if (paper.Content == string.Empty)
        {
            Log.Debug($"TestIterasmSystem: Paper content is empty, setting state to Idle for {ent}");
            iterasm.CurrentState = ExecutionState.Idle;
            return;
        }
    }

    private void HandleExecution(Entity<TestIterasmComponent> ent)
    {
        var iterasm = ent.Comp;

        if (iterasm.NextExecution > _timing.CurTime)
            return;

        iterasm.NextExecution = _timing.CurTime + TimeSpan.FromMilliseconds(iterasm.ExecuteTime);

        if (iterasm.CurrentState == ExecutionState.WaitingForSignal && iterasm.IncomingQueue.Count > 0)
        {
            var (port, value) = iterasm.IncomingQueue.Dequeue();

            TestIterasmState.PutSignal((Iterasm.State) iterasm.IterasmState.State!, port, iterasm.RequestedRegister, value);
            iterasm.CurrentState = ExecutionState.Running;
        }

        if (iterasm.CurrentState == ExecutionState.Running)
            iterasm.IterasmState.Vm?.RunStep();
    }

    private void OnSignalReceived(Entity<TestIterasmComponent> ent, ref SignalReceivedEvent args)
    {
        if (ent.Comp.CurrentState == ExecutionState.Idle)
            return;

        var value = 0L;
        args.Data?.TryGetValue(TestIterasmSignalValueKey, out value);
        ent.Comp.IncomingQueue.Enqueue((args.Port, value));
    }
}

public sealed class TestIterasmState(TestIterasmComponent comp) : Iterasm.IterasmState
{
    private readonly TestIterasmComponent _comp = comp;

    public override Func<Iterasm.State, long, bool>? CustomOps(string op) => op switch
    {
        "emit" => Emit,
        "receiveb" => TryReceive,
        "receive" => Receive,
        _ => base.CustomOps(op),
    };

    private bool Emit(Iterasm.State state, long args)
    {
        var addrReg = (ushort) (args & 0xFFFF);
        var valueReg = (ushort) ((args >> 16) & 0xFFFF);

        var value = state.Get(valueReg);

        var addr = state.Get(addrReg);
        var len = state.Get((ushort) (addrReg + 1));

        var portSlice = state.GetAddress((ulong) addr, (nuint) len);
        var port = Encoding.UTF8.GetString(portSlice.ReadOnlySpan);

        _comp.OutgoingQueue.Enqueue((port, value));

        return state.Increment();
    }

    private bool TryReceive(Iterasm.State state, long args)
    {
        var reg = (ushort) (args & 0xFFFF);

        if (_comp.IncomingQueue.Count == 0)
        {
            _comp.CurrentState = ExecutionState.WaitingForSignal;
            _comp.RequestedRegister = reg;
        }
        else
        {
            var (port, value) = _comp.IncomingQueue.Dequeue();
            PutSignal(state, port, reg, value);
        }

        return state.Increment();
    }

    private bool Receive(Iterasm.State state, long args)
    {
        var reg = (ushort) (args & 0xFFFF);

        if (_comp.IncomingQueue.Count == 0)
        {
            state.Set(reg, 0);
            state.Set((ushort) (reg + 1), 0);
        }
        else
        {
            var (port, value) = _comp.IncomingQueue.Dequeue();
            PutSignal(state, port, reg, value);
        }


        return state.Increment();
    }

    public static void PutSignal(Iterasm.State state, string port, ushort reg, long value)
    {
        var utf8Len = Encoding.UTF8.GetByteCount(port);
        var addr = state.Alloc((nuint) utf8Len);
        var slice = state.GetAddress(addr, (nuint) utf8Len);
        System.Text.Unicode.Utf8.FromUtf16(port.AsSpan(), slice.Span, out var _, out var _);

        state.Set(reg, value);
        state.Set((ushort) (reg + 1), (long) addr);
        state.Set((ushort) (reg + 2), utf8Len);
    }
}

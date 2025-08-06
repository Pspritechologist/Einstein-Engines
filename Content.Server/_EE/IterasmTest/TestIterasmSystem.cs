using Content.Server._EE.Iterasm;
using Content.Server.DeviceLinking.Events;
using Content.Server.Popups;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceNetwork;
using Content.Shared.Paper;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System.Text;

namespace Content.Server._EE.IterasmTest;

public sealed class TestIterasmSystem : EntitySystem
{
    public const string TestIterasmSignalValueKey = "test-signal-value";

    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedDeviceLinkSystem _signal = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

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

        if (iterasm.CurrentState is ExecutionState.Busted or ExecutionState.Halted)
            iterasm.OutgoingQueue.Clear();

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

        if (iterasm.CurrentState is ExecutionState.Halted && paper.Content != string.Empty)
        {
            try
            {
                iterasm.IterasmState.Compile(paper.Content);
                iterasm.CurrentState = ExecutionState.Running;
                iterasm.NextExecution = _timing.CurTime + TimeSpan.FromMilliseconds(1000.0 / iterasm.ExecutionsPerSecond);
                _audio.PlayPvs(new SoundPathSpecifier("/Audio/Items/genhit.ogg"), ent.Owner);
            }
            catch (Iterasm.Binds.CompilationException e)
            {
                _popupSystem.PopupEntity(
                    $"Compilation error: {e.Kind} at line {e.ErrorLine}",
                    ent.Owner,
                    Shared.Popups.PopupType.MediumCaution
                );
                _audio.PlayPvs(new SoundPathSpecifier("/Audio/Items/Defib/defib_failed.ogg"), ent.Owner);
                iterasm.CurrentState = ExecutionState.Busted;
            }
        }

        if (paper.Content == string.Empty)
        {
            // _audio.PlayPvs(new SoundPathSpecifier("/Audio/Weapons/Guns/MagOut/sfrifle_magout.ogg"), ent.Owner);
            iterasm.CurrentState = ExecutionState.Halted;
            return;
        }
    }

    private void HandleExecution(Entity<TestIterasmComponent> ent)
    {
        var iterasm = ent.Comp;

        if (iterasm.NextExecution > _timing.CurTime)
            return;

        iterasm.NextExecution = _timing.CurTime + TimeSpan.FromMilliseconds(1000.0 / iterasm.ExecutionsPerSecond);

        if (iterasm.CurrentState == ExecutionState.WaitingForSignal && iterasm.IncomingQueue.Count > 0)
        {
            var (port, value) = iterasm.IncomingQueue.Dequeue();

            TestIterasmState.PutSignal((VmState) iterasm.IterasmState.State, port, iterasm.RequestedRegister, value);
            iterasm.CurrentState = ExecutionState.Running;
        }

        if (iterasm.CurrentState == ExecutionState.Running)
        {
            try
            {
                var timePast = iterasm.NextExecution - _timing.CurTime;
                var stepsToRun = timePast.TotalMilliseconds / (1000.0 / iterasm.ExecutionsPerSecond);
                if (stepsToRun > 1)
                    Console.WriteLine($"Running {stepsToRun - 1} steps behind!");
                iterasm.IterasmState.Vm.RunSteps((nuint) stepsToRun);
                _audio.PlayPvs(new SoundPathSpecifier("/Audio/Weapons/Guns/Empty/empty.ogg"), ent.Owner);
            }
            catch (Iterasm.Binds.IterasmRuntimeErrorException e)
            {
                _popupSystem.PopupEntity($"Runtime error: {e.Message}", ent.Owner, Shared.Popups.PopupType.MediumCaution);
                _audio.PlayPvs(new SoundPathSpecifier("/Audio/Items/Defib/defib_failed.ogg"), ent.Owner);

                iterasm.CurrentState = ExecutionState.Busted;

                return;
            }

            iterasm.Pc = iterasm.IterasmState.Vm.IsInit ? iterasm.IterasmState.State.Pc : 0;
        }
        else
            iterasm.Pc = 0;

    }

    private void OnSignalReceived(Entity<TestIterasmComponent> ent, ref SignalReceivedEvent args)
    {
        if (ent.Comp.CurrentState is ExecutionState.Busted or ExecutionState.Halted)
            return;

        var value = 0L;
        args.Data?.TryGetValue(TestIterasmSignalValueKey, out value);
        ent.Comp.IncomingQueue.Enqueue((args.Port, value));
    }
}

public sealed class TestIterasmState(TestIterasmComponent comp) : IterasmState
{
    private readonly TestIterasmComponent _comp = comp;

    public override IterasmOp? CustomOps(string op) => op switch
    {
        "emit" => Emit,
        "receiveb" => Receive,
        "receive" => TryReceive,
        _ => base.CustomOps(op),
    };

    private bool Emit(VmState state, long args)
    {
        var addrReg = (ushort) (args & 0xFFFF);
        var valueReg = (ushort) ((args >> 16) & 0xFFFF);

        var value = state.Get(valueReg);

        var addr = state.Get(addrReg);
        var len = state.Get((ushort) (addrReg + 1));

        var portSlice = state.ReadAddr((ulong) addr, (nuint) len);
        var port = Encoding.UTF8.GetString(portSlice.ReadOnlySpan);

        _comp.OutgoingQueue.Enqueue((port, value));

        return false;
    }

    private bool Receive(VmState state, long args)
    {
        var reg = (ushort) (args & 0xFFFF);

        if (_comp.IncomingQueue.Count == 0)
        {
            _comp.CurrentState = ExecutionState.WaitingForSignal;
            _comp.RequestedRegister = reg;
            return true; // Tells execution to stop.
        }
        else
        {
            var (port, value) = _comp.IncomingQueue.Dequeue();
            PutSignal(state, port, reg, value);
        }

        return false;
    }

    private bool TryReceive(VmState state, long args)
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

        return false;
    }

    public static void PutSignal(VmState state, string port, ushort reg, long value)
    {
        var utf8Len = Encoding.UTF8.GetByteCount(port);
        var addr = state.Alloc((nuint) utf8Len);
        var slice = state.ReadAddr(addr, (nuint) utf8Len);
        System.Text.Unicode.Utf8.FromUtf16(port.AsSpan(), slice.Span, out var _, out var _);

        state.Set(reg, value);
        state.Set((ushort) (reg + 1), (long) addr);
        state.Set((ushort) (reg + 2), utf8Len);
    }
}

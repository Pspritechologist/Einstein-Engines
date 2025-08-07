
using System.Linq;
using System.Text;
using System.Text.Unicode;
using Content.Server._EE.Iterasm;
using Content.Server._EE.IterasmMachine.Libraries;
using Content.Server.DeviceLinking.Events;
using Content.Server.DeviceLinking.Systems;
using Content.Shared.Clock;
using Content.Shared.DeviceNetwork;
using Content.Shared.GameTicking;
using MathNet.Numerics.Random;
using Robust.Shared.Random;

namespace Content.Server._EE.IterasmMachine;

public sealed partial class IterasmMachineSystem : EntitySystem
{
    [Dependency] private readonly SharedGameTicker _ticker = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly DeviceLinkSystem _link = default!;

    private void InitializeLibraries()
    {
        SubscribeLocalEvent<IterasmTimingLibComponent, ComponentStartup>((uid, comp, ev) => RefreshOperations(uid));
        SubscribeLocalEvent<IterasmTimingLibComponent, ComponentShutdown>((uid, comp, ev) => RefreshOperations(uid));
        SubscribeLocalEvent<IterasmTimingLibComponent, IterasmMachineGetOpsEvent>((uid, comp, ev) => ev.RegisterOps(TimingOps()));

        SubscribeLocalEvent<IterasmLogLibComponent, ComponentStartup>((uid, comp, ev) => RefreshOperations(uid));
        SubscribeLocalEvent<IterasmLogLibComponent, ComponentShutdown>((uid, comp, ev) => RefreshOperations(uid));
        SubscribeLocalEvent<IterasmLogLibComponent, IterasmMachineGetOpsEvent>((uid, comp, ev) => ev.RegisterOps(LoggingOps((uid, comp))));

        SubscribeLocalEvent<IterasmRngLibComponent, ComponentStartup>((uid, comp, ev) => RefreshOperations(uid));
        SubscribeLocalEvent<IterasmRngLibComponent, ComponentShutdown>((uid, comp, ev) => RefreshOperations(uid));
        SubscribeLocalEvent<IterasmRngLibComponent, IterasmMachineGetOpsEvent>((uid, comp, ev) => ev.RegisterOps(RngOps()));

        SubscribeLocalEvent<IterasmSleepLibComponent, ComponentStartup>((uid, comp, ev) => RefreshOperations(uid));
        SubscribeLocalEvent<IterasmSleepLibComponent, ComponentShutdown>((uid, comp, ev) => RefreshOperations(uid));
        SubscribeLocalEvent<IterasmSleepLibComponent, IterasmMachineGetOpsEvent>((uid, comp, ev) => ev.RegisterOps(SleepOps((uid, comp))));
        SubscribeLocalEvent<IterasmSleepingComponent, IterasmMachineTryStartExecutionEvent>((uid, comp, ev) => ev.Cancelled = true);

        SubscribeLocalEvent<IterasmSignalLibComponent, ComponentStartup>((uid, comp, ev) => RefreshOperations(uid));
        SubscribeLocalEvent<IterasmSignalLibComponent, ComponentShutdown>((uid, comp, ev) => RefreshOperations(uid));
        SubscribeLocalEvent<IterasmSignalLibComponent, IterasmMachineGetOpsEvent>((uid, comp, ev) => ev.RegisterOps(SignalOps((uid, comp))));
        SubscribeLocalEvent<IterasmAwaitingSignalComponent, IterasmMachineTryStartExecutionEvent>((uid, comp, ev) => ev.Cancelled = true);
        SubscribeLocalEvent<IterasmSignalLibComponent, SignalReceivedEvent>(OnSignalReceived);
    }

    private void UpdateLibraries(float frameTime)
    {
        var query = EntityQueryEnumerator<IterasmSleepingComponent>();
        while (query.MoveNext(out var ent, out var comp))
        {
            if (comp.WakeupTime > _timing.CurTime)
                continue;

            RemComp<IterasmSleepingComponent>(ent);
            StartExecution(ent);
        }
    }

    private (string name, string docs, IterasmOp op)[] TimingOps() => [
        ("time", "Sets Reg(1) to the current time in milliseconds.", (state, args) =>
        {
            var reg = (ushort) (args & 0xFFFF);
            var time = GetGlobalTime().TotalMilliseconds;
            // This should 'wrap around' in the unlikely event we have too many milliseconds...
            // (Which would happen after over half a billion years... Not even Frontier is hitting that).
            state.Set(reg, long.CreateTruncating(time));
            return false;
        }),
        ("ftime", "Sets Reg(1) to the current time in fractional milliseconds.", (state, args) =>
        {
            var reg = (ushort) (args & 0xFFFF);
            var time = GetGlobalTime().TotalMilliseconds;
            state.Set(reg, BitConverter.DoubleToInt64Bits(time));
            return false;
        }),
    ];

    // The same thing SharedClockSystem does, but that's private for some reason...
    // I don't think it should be private, but that's not my problem. I don't like that this will be inconsistent if it changes but it should *work fine* regardless.
    private TimeSpan GetGlobalTime() => (EntityQuery<GlobalTimeManagerComponent>().FirstOrDefault()?.TimeOffset ?? TimeSpan.Zero) + _ticker.RoundDuration();

    private (string name, string docs, IterasmOp op)[] LoggingOps(Entity<IterasmLogLibComponent> machine) => [
        ("log", "Attempts to print the string at Reg(1) with a len of Reg(1 + 1r). Null strings are ignored.\nThe exact effects of this are vendor specific.", (state, args) =>
        {
            var reg = (ushort) (args & 0xFFFF);
            if (state.GetString(reg) is { } msg)
                RaiseLocalEvent(machine, new IterasmMachineLogEvent(msg));
            return false;
        }),
        ("logi", "Attempts to print the string at address Tim(1) with a len of Rim(2). Null strings are ignored.\nThe exact effects of this are vendor specific.", (state, args) =>
        {
            var addr = (ulong) (args & 0xFFFFFFFF);
            var len = (nuint) ((args >> 32) & 0xFFFF);
            if (state.GetString(addr, len) is { } msg)
                RaiseLocalEvent(machine, new IterasmMachineLogEvent(msg));
            return false;
        }),
    ];

    private (string name, string docs, IterasmOp op)[] RngOps() => [
        ("rng", "Sets Reg(1) a pseudo random value in a vendor specific range.", (state, args) =>
        {
            var reg = (ushort) (args & 0xFFFF);
            var value = _random.GetRandom().NextFullRangeInt64();
            state.Set(reg, value);
            return false;
        }),
        ("frng", "Sets Reg(1) a pseudo random floating point value in a vendor specific range.", (state, args) =>
        {
            var reg = (ushort) (args & 0xFFFF);
            var value = _random.NextDouble();
            state.Set(reg, BitConverter.DoubleToInt64Bits(value));
            return false;
        }),
    ];

    private (string name, string docs, IterasmOp op)[] SleepOps(Entity<IterasmSleepLibComponent> machine) => [
        ("sleep", "Puts the machine to sleep for the number of milliseconds specified in Reg(1).", (state, args) =>
        {
            var reg = (ushort) (args & 0xFFFF);
            var time = TimeSpan.FromMilliseconds(state.Get(reg));
            EnsureComp<IterasmSleepingComponent>(machine).WakeupTime = _timing.CurTime + time;

            return true;
        }),
        ("fsleep", "Puts the machine to sleep for the number of fractional milliseconds specified in Reg(1).", (state, args) =>
        {
            var reg = (ushort) (args & 0xFFFF);
            var time_ms = BitConverter.Int64BitsToDouble(state.Get(reg));
            var time = TimeSpan.FromMilliseconds(time_ms);
            EnsureComp<IterasmSleepingComponent>(machine).WakeupTime = _timing.CurTime + time;

            return true;
        }),
        ("sleepi", "Puts the machine to sleep for Aim(1) milliseconds.", (state, args) =>
        {
            var time = TimeSpan.FromMilliseconds(args);
            EnsureComp<IterasmSleepingComponent>(machine).WakeupTime = _timing.CurTime + time;

            return true;
        }),
        ("fsleepi", "Puts the machine to sleep for Aim(1) fractional milliseconds.", (state, args) =>
        {
            var time_ms = BitConverter.Int64BitsToDouble(args);
            var time = TimeSpan.FromMilliseconds(time_ms);
            EnsureComp<IterasmSleepingComponent>(machine).WakeupTime = _timing.CurTime + time;

            return true;
        }),
    ];

    private static readonly string ReceiveDocs = "Waits for a signal to be received. When one is, sets Reg(1) to the value, Reg(1 + 1r) to the address of the string representing the port that was invoked, and Reg(1 + 2r) to the length of that string.";
    private static readonly string TryReceiveDocs = "Checks if a received signal is queued. If one is, sets Reg(1) to the value, Reg(1 + 1r) to the address of the string representing the port that was invoked, and Reg(1 + 2r) to the length of that string.\nIf no signals are queued, sets Reg(1) to 0, Reg(1 + 1r) to 0, and Reg(1 + 2r) to 0.";

    private (string name, string docs, IterasmOp op)[] SignalOps(Entity<IterasmSignalLibComponent> machine) => [
        ("emit", "Emits the signal on the port specified by the string at Reg(1) with the value in Reg(2). If the port string is null, does nothing.", (state, args) =>
        {
            var portReg = (ushort) (args & 0xFFFF);
            var valueReg = (ushort) ((args >> 16) & 0xFFFF);
            var port = state.GetString(portReg);
            if (port is null)
                return false;

            var value = state.Get(valueReg);
            _link.InvokePort(machine, port, new() { [machine.Comp.ValueKey] = value });

            return false;
        }),
        ("emiti", "Emits the signal on the port specified by the string at address Tim(1) with a length of Rim(2), and the value in Reg(3).", (state, args) =>
        {
            var addr = (ulong) (args & 0xFFFFFFFF);
            var len = (nuint) ((args >> 32) & 0xFFFF);
            var valueReg = (ushort) ((args >> 48) & 0xFFFF);
            var port = state.GetString(addr, len);
            if (port is null)
                return false;

            var value = state.Get(valueReg);
            _link.InvokePort(machine, port, new() { [machine.Comp.ValueKey] = value });

            return false;
        }),
        ("receiveb", ReceiveDocs, (state, args) =>
        {
            var reg = (ushort) (args & 0xFFFF);

            if (!machine.Comp.IncomingQueue.TryDequeue(out var signal))
            {
                EnsureComp<IterasmAwaitingSignalComponent>(machine).DstRegister = reg;
                StopExecution(machine.Owner);
                return true; // Tells execution to stop.
            }

            PutSignal(state, reg, signal);

            return false;
        }),
        ("receive", TryReceiveDocs, (state, args) =>
        {
            var reg = (ushort) (args & 0xFFFF);

            PutSignal(state, reg, machine.Comp.IncomingQueue.TryDequeue(out var signal) ? signal : null);

            return false;
        }),
    ];

    private void OnSignalReceived(Entity<IterasmSignalLibComponent> machine, ref SignalReceivedEvent args)
    {
        var value = args.Data?.TryGetValue<long>(machine.Comp.ValueKey, out var v) ?? false ? v : 0;

        if (TryComp<IterasmAwaitingSignalComponent>(machine, out var awaiting))
        {
            var machineComp = Comp<IterasmMachineComponent>(machine);
            PutSignal(machineComp.State.State, awaiting.DstRegister, (args.Port, value));
            RemComp(machine, awaiting);
            StartExecution((machine, machineComp));
            return;
        }

        if (machine.Comp.IncomingQueue.Count >= machine.Comp.MaxQueueSize)
            machine.Comp.IncomingQueue.TryDequeue(out _);
        machine.Comp.IncomingQueue.Enqueue((args.Port, value));
    }

    public static void PutSignal(VmState state, ushort reg, (string port, long value)? signal)
    {
        if (signal is null)
        {
            state.Set(reg, 0);
            state.Set((ushort) (reg + 1), 0);
            state.Set((ushort) (reg + 2), 0);
            return;
        }

        var utf8Len = Encoding.UTF8.GetByteCount(signal.Value.port);
        var addr = state.Alloc((nuint) utf8Len);
        var slice = state.ReadAddr(addr, (nuint) utf8Len);
        Utf8.FromUtf16(signal.Value.port.AsSpan(), slice.Span, out var _, out var len);

        state.Set(reg, signal.Value.value);
        state.Set((ushort) (reg + 1), (long) addr);
        state.Set((ushort) (reg + 2), len);
    }
}

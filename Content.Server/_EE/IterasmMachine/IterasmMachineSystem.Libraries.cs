
using System.Linq;
using Content.Server._EE.Iterasm;
using Content.Server._EE.IterasmMachine.Libraries;
using Content.Shared.Clock;
using Content.Shared.GameTicking;
using MathNet.Numerics.Random;
using Robust.Shared.Random;

namespace Content.Server._EE.IterasmMachine;

public sealed partial class IterasmMachineSystem : EntitySystem
{
    [Dependency] private readonly SharedGameTicker _ticker = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

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

    // The same thing SharedClockSystem does, but that's private for some reason...
    // I don't think it should be private, but that's not my problem. I don't like that this will be inconsistent if it changes but it should *work fine* regardless.
    private TimeSpan GetGlobalTime() => (EntityQuery<GlobalTimeManagerComponent>().FirstOrDefault()?.TimeOffset ?? TimeSpan.Zero) + _ticker.RoundDuration();
}

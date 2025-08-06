using System.Linq;
using Content.Server._EE.Iterasm;
using Content.Server.CartridgeLoader;
using Content.Server.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._EE.IterasmMachine;

public sealed partial class IterasmMachineSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IterasmMachineComponent, ComponentInit>((ent, comp, ev) => comp.State = new IterasmMachineState((ent, comp)));
        SubscribeLocalEvent<IterasmMachineComponent, ComponentStartup>((ent, comp, ev) => RefreshOperations((ent, comp)));
        SubscribeLocalEvent<IterasmMachineComponent, ComponentRemove>((ent, comp, ev) => comp.Dispose());

        SubscribeLocalEvent<IterasmMachineDefaultProgrammedComponent, ComponentStartup>(OnDefaultProgrammedStartup);

        InitializeLibraries();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<IterasmMachineActiveComponent>();
        while (query.MoveNext(out var ent, out var active))
        {
            if (!TryComp(ent, out IterasmMachineComponent? iterasm))
            {
                // Assume the component was removed at some point.
                RemCompDeferred<IterasmMachineActiveComponent>(ent);
                continue;
            }

            if (_timing.CurTime < active.NextExecution)
                continue;

            var timePast = _timing.CurTime - active.NextExecution;
            var stepsToRun = iterasm.CatchupOps ? (nuint) Math.Round(timePast / iterasm.ExecutionInterval, MidpointRounding.ToPositiveInfinity) : 1u;

            try
            {
                iterasm.State.Vm.RunSteps(stepsToRun);
                for (var i = 0u; i < stepsToRun; i++)
                    _audio.PlayPvs(new SoundPathSpecifier("/Audio/Weapons/Guns/Empty/empty.ogg"), ent);
            }
            catch (Iterasm.Binds.IterasmRuntimeErrorException e)
            {
                _popupSystem.PopupEntity($"Runtime error: {e.Message}", ent, Shared.Popups.PopupType.MediumCaution);
                _audio.PlayPvs(new SoundPathSpecifier("/Audio/Items/Defib/defib_failed.ogg"), ent);

                StopExecution((ent, active));
            }

            active.Pc = iterasm.State.State?.Pc ?? 0;
            active.NextExecution = _timing.CurTime + iterasm.ExecutionInterval;
        }
    }

    public void StartExecution(Entity<IterasmMachineComponent?> ent)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        if (EnsureComp(ent.Owner, out IterasmMachineActiveComponent active))
            return; // Machine was already active.

        active.NextExecution = _timing.CurTime + ent.Comp.ExecutionInterval;
    }

    public void StopExecution(Entity<IterasmMachineActiveComponent?> ent)
    {
        if (!Resolve(ent.Owner, ref ent.Comp, false))
            return;

        RemCompDeferred<IterasmMachineActiveComponent>(ent.Owner);
    }

    public void RefreshOperations(Entity<IterasmMachineComponent?> ent)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        ent.Comp.Ops.Clear();
        RaiseLocalEvent(ent.Owner, new IterasmMachineGetOpsEvent(Log, ent.Comp.Ops));
    }

    private void OnDefaultProgrammedStartup(Entity<IterasmMachineDefaultProgrammedComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.Spent) return;
        if (!TryComp(ent.Owner, out IterasmMachineComponent? iterasm))
        {
            DebugTools.Assert($"Entity {ent.Owner} ({MetaData(ent.Owner).EntityPrototype}) has {nameof(IterasmMachineDefaultProgrammedComponent)}, but not {nameof(IterasmMachineComponent)}.");
            return;
        }

        RefreshOperations((ent, iterasm));

        try { iterasm.State.Compile(ent.Comp.Program); }
        catch (Iterasm.Binds.CompilationException e)
        {
            Log.Error($"Error while compiling source code from {nameof(IterasmMachineDefaultProgrammedComponent)} on Entity {ent.Owner} ({MetaData(ent.Owner).EntityPrototype}): {e.Message}");
        }
    }
}

public sealed class IterasmMachineState(Entity<IterasmMachineComponent> ent) : IterasmState
{
    //TODO Iterasm: This should be handled slightly lower level.
    // I probably want to hold the final callback items in this dict, not the C# Funcs.
    public override Func<VmState, long, bool>? CustomOps(string op) => ent.Comp.Ops.TryGetValue(op, out var inst) ? inst.op : null;
}

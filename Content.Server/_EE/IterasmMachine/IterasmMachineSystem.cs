using System.Diagnostics.CodeAnalysis;
using Content.Server._EE.Iterasm;
using Content.Server._EE.Iterasm.Binds;
using Content.Server.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._EE.IterasmMachine;

public sealed partial class IterasmMachineSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;

    // private static readonly TimeSpan MinTimePerTickSound = TimeSpan.FromMilliseconds(10);
    private static readonly uint MaxTickSoundsPerFrame = 32u;

    public override void Initialize()
    {
        base.Initialize();

        // Without this Components would be left in an invalid state, since the initial value is `default!`.
        SubscribeLocalEvent<IterasmMachineComponent, ComponentInit>((ent, comp, ev) => comp.Iterasm = new IterasmMachineState((ent, comp)));

        // This is necessary to dispose of the used native resources in the Iterasm state.
        SubscribeLocalEvent<IterasmMachineComponent, ComponentRemove>((ent, comp, ev) => comp.Dispose());

        SubscribeLocalEvent<IterasmMachineComponent, ComponentStartup>((ent, comp, ev) => RefreshOperations((ent, comp)));

        SubscribeLocalEvent<IterasmMachineDefaultProgrammedComponent, MapInitEvent>(OnDefaultProgrammedStartup);

        InitializeLibraries();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdateLibraries(frameTime);

        var query = EntityQueryEnumerator<IterasmMachineActiveComponent>();
        while (query.MoveNext(out var ent, out var active))
        {
            if (!TryComp(ent, out IterasmMachineComponent? iterasmComp))
            {
                // Assume the component was removed at some point.
                RemCompDeferred<IterasmMachineActiveComponent>(ent);
                continue;
            }

            if (_timing.CurTime < active.NextExecution)
                continue;

            var timePast = _timing.CurTime - active.NextExecution;
            var stepsToRun = iterasmComp.CatchupOps ? (ulong) Math.Round(timePast / iterasmComp.ExecutionInterval, MidpointRounding.ToPositiveInfinity) : 1u;

            try
            {
                if (iterasmComp.TickSound is not null)
                    for (var i = 0u; i < stepsToRun; i++)
                    {
                        if (i >= MaxTickSoundsPerFrame)
                            break;

                        _audio.PlayPvs(iterasmComp.TickSound, ent);
                    }

                var done = iterasmComp.Iterasm.RunSteps(stepsToRun, out var stepsRun);
                if (done)
                    StopExecution((ent, active));

                RaiseLocalEvent(ent, new IterasmMachineAfterExecutionEvent((ent, iterasmComp), done, stepsToRun));
            }
            catch (IterasmRuntimeErrorException e)
            {
                _popupSystem.PopupEntity($"Runtime error: {e.Message}", ent, Shared.Popups.PopupType.MediumCaution);
                _audio.PlayPvs(iterasmComp.ErrorSound, ent);

                StopExecution((ent, active));

                RaiseLocalEvent(ent, new IterasmMachineRuntimeErrorEvent((ent, iterasmComp), e.Message));
            }

            active.Pc = iterasmComp.Iterasm.TryGetState(out var state) ? state.Pc : 0;
            active.NextExecution = _timing.CurTime + iterasmComp.ExecutionInterval;
        }
    }

    public bool CompileProgram(Entity<IterasmMachineComponent?> ent, string program, [NotNullWhen(false)] out CompilationException? compError)
    {
        compError = null;

        if (!Resolve(ent.Owner, ref ent.Comp))
            return true;

        StopExecution(ent.Owner);

        try
        {
            ent.Comp.Iterasm.Compile(program);
            return true;
        }
        catch (CompilationException e)
        {
            compError = e;
            return false;
        }
    }

    public void StartExecution(Entity<IterasmMachineComponent?> ent)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        var ev = new IterasmMachineTryStartExecutionEvent(ent!);
        RaiseLocalEvent(ent, ref ev);
        if (ev.Cancelled)
            return;

        if (EnsureComp(ent.Owner, out IterasmMachineActiveComponent active))
            return; // Machine was already active.

        active.NextExecution = _timing.CurTime + ent.Comp.ExecutionInterval;
    }

    public void StopExecution(Entity<IterasmMachineActiveComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return;

        RemCompDeferred(ent, ent.Comp);
    }

    public void RefreshOperations(Entity<IterasmMachineComponent?> ent)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return;

        StopExecution(ent.Owner);
        ent.Comp.Ops.Clear();
        RaiseLocalEvent(ent.Owner, new IterasmMachineGetOpsEvent(Log, ent.Comp.Ops));
    }

    private void OnDefaultProgrammedStartup(Entity<IterasmMachineDefaultProgrammedComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Spent) return;
        if (!TryComp(ent.Owner, out IterasmMachineComponent? iterasm))
        {
            DebugTools.Assert($"Entity {ent.Owner} ({MetaData(ent.Owner).EntityPrototype}) has {nameof(IterasmMachineDefaultProgrammedComponent)}, but not {nameof(IterasmMachineComponent)}.");
            return;
        }

        try { iterasm.Iterasm.Compile(ent.Comp.Program); }
        catch (CompilationException e)
        {
            Log.Error($"Error while compiling source code from {nameof(IterasmMachineDefaultProgrammedComponent)} on Entity {ent.Owner} ({MetaData(ent.Owner).EntityPrototype}): {e.Message}");
            return;
        }

        StartExecution((ent.Owner, iterasm));
    }

    public bool TryGetVmState(Entity<IterasmMachineComponent?> ent, out IterasmState state)
    {
        state = new();

        if (!Resolve(ent.Owner, ref ent.Comp))
            return false;

        return ent.Comp.Iterasm.TryGetState(out state);
    }
}

public sealed class IterasmMachineState(Entity<IterasmMachineComponent> ent) : Iterasm.Iterasm
{
    //TODO Iterasm: This should be handled slightly lower level.
    // I probably want to hold the final callback items in this dict, not the C# Funcs.
    public override IterasmOp? CustomOps(string op) => ent.Comp.Ops.TryGetValue(op, out var inst) ? inst.op : null;

    public bool RunSteps(ulong steps, out ulong stepsRun) => Vm.RunSteps(steps, out stepsRun);
}

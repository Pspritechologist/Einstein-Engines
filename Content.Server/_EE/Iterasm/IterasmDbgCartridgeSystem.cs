using Content.Server.CartridgeLoader;
using Content.Shared.CartridgeLoader;
using Content.Shared._EE.Iterasm;
using Content.Server._EE.IterasmMachine;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Audio;
using Content.Server._EE.IterasmMachine.Libraries;

namespace Content.Server._EE.Iterasm;

public sealed class IterasmDbgCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _loader = default!;
    [Dependency] private readonly IterasmMachineSystem _iterasmMachine = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IterasmDbgCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
        SubscribeLocalEvent<IterasmDbgCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);

        SubscribeLocalEvent<IterasmDbgCartridgeComponent, CartridgeAfterInteractEvent>(OnDebuggerInteract);

        //TODO Iteasm: This should probably have its own Component.
        SubscribeLocalEvent<IterasmMachineComponent, IterasmMachineLogEvent>(OnIterasmLogEvent);
    }

    /// <summary>
    /// This gets called when the ui fragment needs to be updated for the first time after activating
    /// </summary>
    private void OnUiReady(EntityUid uid, IterasmDbgCartridgeComponent component, CartridgeUiReadyEvent args)
        => SetUiStateOk(args.Loader);

    /// <summary>
    /// The ui messages received here get wrapped by a CartridgeMessageEvent and are relayed from the <see cref="CartridgeLoaderSystem"/>
    /// </summary>
    /// <remarks>
    /// The cartridge specific ui message event needs to inherit from the CartridgeMessageEvent
    /// </remarks>
    private void OnUiMessage(Entity<IterasmDbgCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not IterasmDbgUiMessageEvent msg)
            return;

        if (ent.Comp.ProgramTarget is not { } machine)
        {
            // _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/double_beep.ogg"), ent.Owner);
            _loader.SendNotification(GetEntity(msg.LoaderUid), "IterasmDebugger", "No machine connected to program.");
            return;
        }

        try { machine.Comp.State.Compile(msg.Program); }
        catch (Binds.CompilationException e)
        {
            // _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/double_beep.ogg"), ent.Owner);
            _loader.SendNotification(GetEntity(msg.LoaderUid), "Compilation Error", e.Message);

            SetUiStateError(GetEntity(msg.LoaderUid), e.Message, (uint) e.ErrorLine);
            return;
        }

        // _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/beep_landmine.ogg"), ent.Owner);
        _loader.SendNotification(GetEntity(msg.LoaderUid), "Compilation Success", "Program compiled successfully.");
        _iterasmMachine.StartExecution(machine!);
    }

    private void OnDebuggerInteract(Entity<IterasmDbgCartridgeComponent> ent, ref CartridgeAfterInteractEvent args)
    {
        if (args.InteractEvent.Target is not { } machineUid)
            return;

        Entity<IterasmMachineComponent?> machine = machineUid;
        if (!Resolve(machineUid, ref machine.Comp, false))
            return;

        if (ent.Comp.ProgramTarget == machine!)
        {
            // _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/beep1.ogg"), ent.Owner);
            _loader.SendNotification(args.Loader, "IterasmDebugger", $"Disconnected from /dev/{machine.Owner}.");
            ent.Comp.ProgramTarget = null;
            return;
        }

        // _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/beep1.ogg"), ent.Owner);
        _loader.SendNotification(args.Loader, "IterasmDebugger", $"Debugger connected to /dev/{machine.Owner}.");
        ent.Comp.ProgramTarget = machine!;
    }

    private void OnIterasmLogEvent(Entity<IterasmMachineComponent> ent, ref IterasmMachineLogEvent args)
    {
        var query = new EntityQueryEnumerator<IterasmDbgCartridgeComponent>();

        while (query.MoveNext(out var dbgUid, out var dbgComp))
        {
            if (dbgComp.ProgramTarget?.Owner == ent.Owner)
            {
                if (Comp<CartridgeComponent>(dbgUid).LoaderUid is not { } loaderUid)
                    break;

                _loader.SendNotification(loaderUid, $"/dev/{ent.Owner}", args.Message);

                break;
            }
        }
    }

    private void SetUiStateError(EntityUid loaderUid, string msg, uint line)
        => _loader.UpdateCartridgeUiState(loaderUid, new IterasmDbgUiCompErrorState(msg, line));
    private void SetUiStateOk(EntityUid loaderUid)
        => _loader.UpdateCartridgeUiState(loaderUid, new IterasmDbgUiOkState());
}

using Content.Server.CartridgeLoader;
using Content.Shared.CartridgeLoader;
using Content.Shared._EE.Iterasm;

namespace Content.Server._EE.Iterasm;

public sealed class IterasmDbgCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem? _cartridgeLoaderSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IterasmDbgCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
        SubscribeLocalEvent<IterasmDbgCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
    }

    /// <summary>
    /// This gets called when the ui fragment needs to be updated for the first time after activating
    /// </summary>
    private void OnUiReady(EntityUid uid, IterasmDbgCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        UpdateUiState(uid, args.Loader, component);
    }

    /// <summary>
    /// The ui messages received here get wrapped by a CartridgeMessageEvent and are relayed from the <see cref="CartridgeLoaderSystem"/>
    /// </summary>
    /// <remarks>
    /// The cartridge specific ui message event needs to inherit from the CartridgeMessageEvent
    /// </remarks>
    private void OnUiMessage(EntityUid uid, IterasmDbgCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not IterasmDbgUiMessageEvent message)
            return;

        if (message.Action == IterasmDbgUiAction.Add)
        {
            component.Notes.Add(message.Note);
        }
        else
        {
            component.Notes.Remove(message.Note);
        }

        UpdateUiState(uid, GetEntity(args.LoaderUid), component);
    }


    private void UpdateUiState(EntityUid uid, EntityUid loaderUid, IterasmDbgCartridgeComponent? component)
    {
        if (!Resolve(uid, ref component))
            return;

        var state = new IterasmDbgUiOkState();
        _cartridgeLoaderSystem?.UpdateCartridgeUiState(loaderUid, state);
    }
}

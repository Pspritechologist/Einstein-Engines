using Content.Client.UserInterface.Fragments;
using Content.Shared._EE.Iterasm;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._EE.Iterasm;

public sealed partial class IterasmDbgUi : UIFragment
{
    private IterasmDbgUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new IterasmDbgUiFragment();
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not IterasmDbgUiState iterasmState)
            return;

        _fragment?.UpdateState(iterasmState);
    }

    private void SendIterasmDbgMessage(IterasmDbgUiAction action, string note, BoundUserInterface userInterface)
    {
        var iterasmMsg = new IterasmDbgUiMessageEvent(action, note);
        var uiMsg = new CartridgeUiMessage(iterasmMsg);
        userInterface.SendMessage(uiMsg);
    }
}

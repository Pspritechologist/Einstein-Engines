using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._EE.Iterasm;

[Serializable, NetSerializable]
public sealed class IterasmDbgUiMessageEvent : CartridgeMessageEvent
{
    public readonly IterasmDbgUiAction Action;
    public readonly string Note;

    public IterasmDbgUiMessageEvent(IterasmDbgUiAction action, string note)
    {
        Action = action;
        Note = note;
    }
}

[Serializable, NetSerializable]
public enum IterasmDbgUiAction
{
    Add,
    Remove
}

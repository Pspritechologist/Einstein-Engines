using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._EE.IterasmMachine;

[Serializable, NetSerializable]
public sealed class IterasmDbgUiMessageEvent(string program) : CartridgeMessageEvent
{
    public readonly string Program = program;
}

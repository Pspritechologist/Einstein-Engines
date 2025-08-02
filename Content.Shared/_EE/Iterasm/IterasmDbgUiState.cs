using Robust.Shared.Serialization;

namespace Content.Shared._EE.Iterasm;

[Serializable, NetSerializable]
public abstract class IterasmDbgUiState() : BoundUserInterfaceState
{ }

[Serializable, NetSerializable]
public sealed class IterasmDbgUiOkState() : IterasmDbgUiState()
{ }

[Serializable, NetSerializable]
public sealed class IterasmDbgUiCompErrorState(string errMsg, uint line) : IterasmDbgUiState()
{
    public string ErrorMessage = errMsg;
    public uint Line = line;
}

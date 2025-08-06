using Content.Server._EE.Iterasm;

namespace Content.Server._EE.IterasmMachine;

public readonly record struct IterasmMachineGetOpsEvent
{
    private readonly ISawmill _log;
    private readonly Dictionary<string, (string docs, IterasmOp op)> _ops;

    public IterasmMachineGetOpsEvent(ISawmill log, Dictionary<string, (string docs, IterasmOp op)> ops)
    {
        _log = log;
        _ops = ops;
    }

    public void RegisterOp(string name, string docs, IterasmOp op)
    {
        if (!_ops.TryAdd(name, (docs, op)))
            _log.Warning($"Tried to register duplicate op {name}. First instance: '{_ops[name].op.Target}.{_ops[name].op.Method}' Second instance: '{op.Target}.{op.Method}'");
    }

    public void RegisterOps((string name, string docs, IterasmOp op)[] ops)
    {
        foreach (var (name, docs, op) in ops)
            RegisterOp(name, docs, op);
    }
}

[ByRefEvent]
public record struct IterasmMachineAfterTickEvent(Entity<IterasmMachineComponent> Machine, bool StopExecution = false);

[ByRefEvent]
public record struct IterasmMachineTryStartExecutionEvent(Entity<IterasmMachineComponent> Machine, bool Cancelled = false);

public readonly record struct IterasmMachineRuntimeErrorEvent(Entity<IterasmMachineComponent> Machine, string Message);

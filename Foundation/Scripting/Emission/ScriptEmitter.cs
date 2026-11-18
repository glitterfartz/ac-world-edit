namespace ACWorldEdit.Foundation.Scripting;

/// <summary>An emitter turns the shared IR into one host language. Lua is the only one in v1.</summary>
public interface ScriptEmitter
{
    string Emit(IrModule module, ScriptHostProfile host);
}

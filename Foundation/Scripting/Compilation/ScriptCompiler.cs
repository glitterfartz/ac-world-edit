namespace ACWorldEdit.Foundation.Scripting;

/// <summary>Validate → IR → emit. The compiler is the only writer of Lua text.</summary>
public sealed class ScriptCompiler
{
    readonly ScriptHostProfile host;

    public ScriptCompiler(ScriptHostProfile host)
    {
        this.host = host;
    }

    public ScriptCompilation Compile(ScriptDocument document, string sourceName = "script.acwscript")
    {
        var diagnostics = new ScriptValidator(host).Validate(document);
        string sourceHash = ScriptDocumentJson.CanonicalHash(document);
        string catalogHash = ScriptCatalogHash.Compute(host);
        string outputName = OutputName(document);
        if (diagnostics.Any(diagnostic => diagnostic.IsError))
        {
            return new ScriptCompilation(null, diagnostics, sourceHash, catalogHash, outputName);
        }

        var module = new ScriptIrBuilder(host).Build(document, sourceName);
        string lua = new LuaEmitter().Emit(module, host);
        return new ScriptCompilation(lua, diagnostics, sourceHash, catalogHash, outputName);
    }

    /// <summary><c>acw_&lt;slug&gt;_&lt;shortid&gt;.lua</c>, the filename ALE sees inside lua_scripts.</summary>
    public static string OutputName(ScriptDocument document) =>
        $"acw_{ScriptSlug.FromName(document.Name)}_{ScriptSlug.ShortId(document.Id)}.lua";
}

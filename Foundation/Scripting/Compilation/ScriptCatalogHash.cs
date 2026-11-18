using System.Security.Cryptography;
using System.Text;

namespace ACWorldEdit.Foundation.Scripting;

/// <summary>A stable digest of the semantic catalog and the host bindings it compiles against.</summary>
public static class ScriptCatalogHash
{
    public static string Compute(ScriptHostProfile host)
    {
        var builder = new StringBuilder();
        foreach (var (id, capability) in host.Capabilities.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append("C|").Append(id).Append('|').Append(capability.Effect).Append('|');
            builder.AppendJoin(',', capability.Params.Select(parameter =>
                $"{parameter.Name}:{ScriptValueTypeNames.ToJson(parameter.Type)}:{(parameter.Optional ? "?" : "")}"));
            builder.Append('|');
            builder.AppendJoin(',', capability.Returns.Select(slot => $"{slot.Name}:{ScriptValueTypeNames.ToJson(slot.Type)}"));
            if (host.CapabilityBinding(id) is { } binding)
            {
                builder.Append("|B|").Append(binding.Call).Append('|')
                    .AppendJoin(',', binding.Args.Select(argument => argument.Param));
            }

            builder.Append('\n');
        }

        foreach (var (id, definition) in host.Events.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append("E|").Append(id).Append('|')
                .AppendJoin(',', definition.ScopeParams.Select(scope => $"{scope.Name}:{ScriptValueTypeNames.ToJson(scope.Type)}"))
                .Append('|')
                .AppendJoin(',', definition.Params.Select(parameter => $"{parameter.Name}:{ScriptValueTypeNames.ToJson(parameter.Type)}"));
            if (host.EventBinding(id) is { } binding)
            {
                builder.Append("|B|").Append(binding.Register).Append('|').Append(binding.EventId)
                    .Append('|').AppendJoin(',', binding.ScopeArgs).Append('|').Append(binding.EventName);
            }

            builder.Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }
}

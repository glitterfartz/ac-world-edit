using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ACWorldEdit.Assets;

/// <summary>
/// Per-store CPU liquid catalog/atlas cache. Identical patterns share decoded storage;
/// ConditionalWeakTable ties lifetime to the mounted client rather than a global singleton.
/// </summary>
public sealed class LiquidTextures
{
    static readonly ConditionalWeakTable<IClientFiles, LiquidTextures> stores = new();
    readonly IClientFiles store;
    readonly DbcTable types;
    readonly Dictionary<uint, BlpTexture> atlases = [];
    readonly Dictionary<string, BlpTexture> sequences = new(StringComparer.OrdinalIgnoreCase);
    LiquidTextures(IClientFiles source)
    {
        store = source;
        types = new(source.Read("DBFilesClient/LiquidType.dbc"));
    }

    /// <summary>
    /// Get the catalog for exactly this client store. Catalogs hold decoded bytes and allocate no
    /// Godot resources.
    /// </summary>
    public static LiquidTextures For(IClientFiles store) => stores.GetValue(store, s => new(s));
    /// <summary>
    /// Read the client SoundBank category used for water/ocean versus lava/slime preview blending.
    /// </summary>
    public uint Kind(uint type) => types.U(type, 3);
    /// <summary>
    /// Resolve the first native texture slot and normalize separators, retaining printf-style frame
    /// placeholders.
    /// </summary>
    public string Pattern(uint type) => types.Str(type, 15).Replace('\\', '/');
    /// <summary>
    /// Decode bounded sequences at up to 128 pixels per frame into a horizontal atlas. A lock
    /// prevents duplicate worker decodes; missing procedural assets fail explicitly.
    /// </summary>
    public BlpTexture Decode(uint type)
    {
        lock (atlases)
        {
            if (atlases.TryGetValue(type, out var cached))
            {
                return cached;
            }

            string pattern = Pattern(type);
            if (sequences.TryGetValue(pattern, out cached))
            {
                return atlases[type] = cached;
            }

            List<BlpTexture> frames = [];
            if (pattern.Contains('%'))
            {
                for (int i = 1; i <= 60; i++)
                {
                    string path = Regex.Replace(pattern, @"%0?(\d*)d", m => m.Groups[1].Value.Length > 0 ? i.ToString("D" + m.Groups[1].Value) : i.ToString());
                    if (!store.Exists(path))
                    {
                        break;
                    }

                    frames.Add(BlpTexture.Decode(store.Read(path), 128));
                }
            }
            else if (pattern.Length > 0 && store.Exists(pattern))
            {
                frames.Add(BlpTexture.Decode(store.Read(pattern), 128));
            }

            if (frames.Count == 0)
            {
                throw new InvalidDataException($"Liquid {type} has no supported native texture sequence: {pattern}");
            }

            int w = frames[0].Width, h = frames[0].Height;
            if (frames.Any(f => f.Width != w || f.Height != h))
            {
                throw new InvalidDataException("Mismatched liquid texture frames.");
            }

            byte[] rgba = new byte[checked(w * h * frames.Count * 4)];
            for (int f = 0; f < frames.Count; f++)
            {
                for (int y = 0; y < h; y++)
                {
                    frames[f].Rgba.AsSpan(y * w * 4, w * 4).CopyTo(rgba.AsSpan((y * w * frames.Count + f * w) * 4));
                }
            }

            return sequences[pattern] = atlases[type] = new(w * frames.Count, h, rgba);
        }
    }
}

using HighwayEngineer.Client.Gui.UserControls;
using System;
using System.Collections.Generic;
using System.IO;
using Acrux.Controls;
using Acrux.Core;
using Acrux.Layout;
using Acrux.Rendering;
using Acrux.ScreenSpace;
using Acrux.Styling;
using Adk.Image;
using Adk.Image.Dds;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace HighwayEngineer.Client.Gui
{
    /// <summary>
    /// Voxel material thumbnails (the voxel hand preview) as generated textures, so
    /// the planner can draw them on screen and on LCDs. Each is the centre square of
    /// the preview with rounded corners, decoded once per material; <see cref="CreateView"/>
    /// frames it with a thin rounded border.
    /// </summary>
    internal static class VoxelMaterialThumbnails
    {
        const int MaxSide = 1024;
        const int Size = 64;
        /// <summary>Corner radius, as a fraction of the side (texture and frame alike).</summary>
        internal const float CornerFraction = 0.2f;

        static readonly Dictionary<string, DynamicMapTexture> Textures =
            new Dictionary<string, DynamicMapTexture>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> Unavailable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The material's rounded thumbnail with a 2 px rounded frame, <paramref name="size"/>
        /// pixels square. Without a thumbnail only the frame is shown.
        /// </summary>
        public static VisualElement CreateView(string subtype, float size)
        {
            return new MaterialThumbnail { Size = size, Material = subtype };
        }

        /// <summary>
        /// The material's thumbnail texture; null when it has none. Draw it as the texture
        /// itself (<see cref="GeneratedTextureImage"/>), not by name: LCDs only resolve names
        /// of defined LCD sprites, so an ImageBrush naming it shows the missing-texture cross.
        /// </summary>
        public static IMyGeneratedTexture GetTexture(string subtype)
        {
            if (string.IsNullOrWhiteSpace(subtype)) return null;
            subtype = subtype.Trim();
            DynamicMapTexture texture;
            if (Textures.TryGetValue(subtype, out texture))
                return texture.IsAvailable ? texture.Texture : null;
            if (Unavailable.Contains(subtype)) return null;

            RawRgbaBitmap bitmap;
            texture = TryLoad(subtype, out bitmap) ? Upload(subtype, bitmap) : null;
            if (texture == null)
            {
                Unavailable.Add(subtype);
                return null;
            }
            Textures[subtype] = texture;
            return texture.Texture;
        }

        public static void Clear()
        {
            foreach (DynamicMapTexture texture in Textures.Values)
                texture.Dispose();
            Textures.Clear();
            Unavailable.Clear();
        }

        static bool TryLoad(string subtype, out RawRgbaBitmap bitmap)
        {
            bitmap = null;
            MyVoxelMaterialDefinition material = Find(subtype);
            if (material == null) return false;
            string path = material.VoxelHandPreview;
            if (string.IsNullOrWhiteSpace(path))
                path = "Textures/Voxels/" + material.Id.SubtypeName + "_Thumbnail.dds";
            string normalized, failure;
            if (!ContentTexturePath.TryNormalize(path, out normalized, out failure))
                return false;
            try
            {
                // A mod's material keeps its thumbnail in that mod; fall back to game content.
                if (material.Context != null && !material.Context.IsBaseGame &&
                    MyAPIGateway.Utilities.FileExistsInModLocation(normalized, material.Context.ModItem))
                {
                    using (BinaryReader reader = MyAPIGateway.Utilities.ReadBinaryFileInModLocation(
                               normalized, material.Context.ModItem))
                        if (DdsDecoder.TryDecode(reader.BaseStream, out bitmap)) return true;
                }
                if (MyAPIGateway.Utilities.FileExistsInGameContent(normalized))
                {
                    using (BinaryReader reader = MyAPIGateway.Utilities.ReadBinaryFileInGameContent(normalized))
                        return DdsDecoder.TryDecode(reader.BaseStream, out bitmap);
                }
            }
            catch (Exception)
            {
                // Unreadable or unsupported thumbnail: the planner shows the name only.
            }
            bitmap = null;
            return false;
        }

        static DynamicMapTexture Upload(string subtype, RawRgbaBitmap bitmap)
        {
            if (bitmap.Width > MaxSide || bitmap.Height > MaxSide) return null;
            var texture = new DynamicMapTexture(GeneratedTextureNames.Next("RoadMaterialThumb"),
                new Vector2I(Size, Size));
            if (!texture.IsAvailable || texture.Size.X != Size || texture.Size.Y != Size)
            {
                texture.Dispose();
                return null;
            }
            if (texture.UploadPixels(RoundedSquare(bitmap)))
            {
                // LCDs redraw only when their sprites change; mark the new pixels.
                GeneratedTextureNames.NotifyChanged(texture.Texture);
                return texture;
            }
            texture.Dispose();
            return null;
        }

        /// <summary>
        /// The bitmap's centre square, scaled to <see cref="Size"/> with bilinear sampling,
        /// outside an anti-aliased rounded-square outline made transparent. Premultiplied RGBA.
        /// </summary>
        static byte[] RoundedSquare(RawRgbaBitmap bitmap)
        {
            int side = Math.Min(bitmap.Width, bitmap.Height);
            float left = (bitmap.Width - side) * 0.5f;
            float top = (bitmap.Height - side) * 0.5f;
            float scale = side / (float)Size;
            float radius = Size * CornerFraction;
            var pixels = new byte[Size * Size * 4];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                // Inside the corner squares, coverage falls off at the arc around each corner's centre.
                float cx = Math.Min(x + 0.5f, Size - x - 0.5f);
                float cy = Math.Min(y + 0.5f, Size - y - 0.5f);
                float coverage = 1f;
                if (cx < radius && cy < radius)
                {
                    float dx = radius - cx, dy = radius - cy;
                    coverage = Math.Max(0f, Math.Min(1f, radius - (float)Math.Sqrt(dx * dx + dy * dy) + 0.5f));
                }
                if (coverage <= 0f) continue;

                float r, g, b, a;
                Sample(bitmap, left + (x + 0.5f) * scale - 0.5f, top + (y + 0.5f) * scale - 0.5f,
                    out r, out g, out b, out a);
                a *= coverage;
                int target = (y * Size + x) * 4;
                pixels[target] = (byte)(r * a + 0.5f);
                pixels[target + 1] = (byte)(g * a + 0.5f);
                pixels[target + 2] = (byte)(b * a + 0.5f);
                pixels[target + 3] = (byte)(a * 255f + 0.5f);
            }
            return pixels;
        }

        /// <summary>Bilinear sample; colour channels 0–255, alpha 0–1.</summary>
        static void Sample(RawRgbaBitmap bitmap, float u, float v, out float r, out float g, out float b, out float a)
        {
            int x0 = Math.Max(0, Math.Min(bitmap.Width - 1, (int)Math.Floor(u)));
            int y0 = Math.Max(0, Math.Min(bitmap.Height - 1, (int)Math.Floor(v)));
            int x1 = Math.Min(bitmap.Width - 1, x0 + 1);
            int y1 = Math.Min(bitmap.Height - 1, y0 + 1);
            float fx = Math.Max(0f, Math.Min(1f, u - x0));
            float fy = Math.Max(0f, Math.Min(1f, v - y0));
            r = g = b = a = 0f;
            Accumulate(bitmap, x0, y0, (1f - fx) * (1f - fy), ref r, ref g, ref b, ref a);
            Accumulate(bitmap, x1, y0, fx * (1f - fy), ref r, ref g, ref b, ref a);
            Accumulate(bitmap, x0, y1, (1f - fx) * fy, ref r, ref g, ref b, ref a);
            Accumulate(bitmap, x1, y1, fx * fy, ref r, ref g, ref b, ref a);
        }

        static void Accumulate(RawRgbaBitmap bitmap, int x, int y, float weight,
            ref float r, ref float g, ref float b, ref float a)
        {
            int index = y * bitmap.Stride + x * 4;
            r += bitmap.Pixels[index] * weight;
            g += bitmap.Pixels[index + 1] * weight;
            b += bitmap.Pixels[index + 2] * weight;
            a += bitmap.Pixels[index + 3] / 255f * weight;
        }

        static MyVoxelMaterialDefinition Find(string subtype)
        {
            if (MyDefinitionManager.Static == null) return null;
            foreach (MyVoxelMaterialDefinition definition in MyDefinitionManager.Static.GetVoxelMaterialDefinitions())
                if (definition != null &&
                    string.Equals(definition.Id.SubtypeName, subtype, StringComparison.OrdinalIgnoreCase))
                    return definition;
            return null;
        }
    }
}

using System.Collections.Generic;
using Acrux.Core.Input;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace HighwayEngineer.Client.Lcd
{
    /// <summary>
    /// The LCD mod's surface cursor: sprite per pointer cursor, drawn over the frame.
    /// </summary>
    internal static class LcdCursor
    {
        const string PREFIX = "HighwayEngineer_Cursor";

        public static void Add(List<MySprite> sprites, PointerCursor cursor, Vector2 position, float size)
        {
            string name;
            switch (cursor)
            {
                case PointerCursor.None:
                    return;
                case PointerCursor.Hand:
                    name = "Hand";
                    break;
                case PointerCursor.DragMove:
                    name = "Grab";
                    break;
                case PointerCursor.Cross:
                    name = "Cross";
                    break;
                case PointerCursor.No:
                    name = "No";
                    break;
                case PointerCursor.Wait:
                    name = "WaitCursor";
                    break;
                default:
                    name = "Default";
                    break;
            }

            float rotation = 0f;
            if (cursor == PointerCursor.Wait)
                rotation = (float)(MyAPIGateway.Session.GameplayFrameCounter / 60f % (2 * System.Math.PI));

            if (cursor == PointerCursor.Cross || cursor == PointerCursor.No || cursor == PointerCursor.Wait)
            {
                position += new Vector2(size / 4f);
                size *= 0.8f;
            }
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = PREFIX + name,
                Position = position,
                Size = new Vector2(size),
                Alignment = TextAlignment.CENTER,
                RotationOrScale = rotation
            });
        }
    }
}

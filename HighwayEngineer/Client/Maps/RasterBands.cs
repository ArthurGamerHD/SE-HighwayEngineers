using System;
using System.Collections.Generic;
using Acrux.Rendering;
using Sandbox.ModAPI;

namespace HighwayEngineer.Client.Maps
{
    /// <summary>Paints the planner's road overlays over disjoint row bands on the game's worker pool.</summary>
    internal static class RasterBands
    {
        /// <summary>
        /// Runs every command over disjoint row bands. Commands run in list order
        /// within a band, so later commands still draw over earlier ones.
        /// </summary>
        public static void Paint(PixelCanvas canvas, IList<Action<PixelCanvas, int, int>> commands)
        {
            if (commands.Count == 0)
                return;

            // More bands than workers lets the game scheduler balance the work
            // without creating threads per frame.
            const int rowsPerBand = 16;
            int height = canvas.Height;
            int bands = (height + rowsPerBand - 1) / rowsPerBand;
            Exception failure = null;
            object failureLock = new object();
            ParallelFor(0, bands, band =>
            {
                try
                {
                    int first = band * rowsPerBand;
                    int end = Math.Min(height, first + rowsPerBand);
                    for (int i = 0; i < commands.Count; i++)
                        commands[i](canvas, first, end);
                }
                catch (Exception exception)
                {
                    // Catch inside the worker: the engine may be configured to
                    // treat uncaught parallel task exceptions as fatal.
                    lock (failureLock)
                        failure = exception;
                }
            });
            if (failure != null)
                throw new Exception("Road overlay rasterization failed.", failure);
        }

        /// <summary>Blocking parallel-for on the game's worker pool; serial when unavailable.</summary>
        static void ParallelFor(int first, int end, Action<int> body)
        {
            if (MyAPIGateway.Parallel != null)
            {
                MyAPIGateway.Parallel.For(first, end, body);
                return;
            }
            for (int i = first; i < end; i++)
                body(i);
        }
    }
}

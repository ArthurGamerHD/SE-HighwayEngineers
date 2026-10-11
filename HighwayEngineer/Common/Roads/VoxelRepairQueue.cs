using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRage.Voxels;
using VRageMath;

namespace HighwayEngineer.Common.Roads
{
    /// <summary>
    /// Drops a planet's voxel edits in storage blocks, a few per frame, so holes dug into roads
    /// close and dirt piled on them goes: the octree storage falls back to the planet's generated
    /// shape there (the voxel cubemap API's terrain, roads included). The game does not sync a
    /// mod's storage writes, so the server and every client run this for the same blocks.
    /// <para>
    /// The voxel cubemap API commits a push by swapping the planet's storage for a patched copy,
    /// which carries the old edits along. A client may get the API's push after this repair, so
    /// each repair is run again when the planet's storage changes within a while of it.
    /// </para>
    /// </summary>
    public sealed class VoxelRepairQueue
    {
        const int BlocksPerFrame = 48;
        const int ReapplyWindowFrames = 60 * 180;

        sealed class Job
        {
            public long PlanetId;
            public int[] Blocks;
            public int BlockSize;
            public int Next;
            public IMyStorage Storage;
            public int ExpiresFrame;
        }

        readonly List<Job> _jobs = new List<Job>();
        int _frame;

        public void Enqueue(long planetId, int[] blocks, int blockSize)
        {
            if (blocks == null || blocks.Length < 3 || blockSize <= 0) return;
            _jobs.Add(new Job { PlanetId = planetId, Blocks = blocks, BlockSize = blockSize });
        }

        public void Update()
        {
            _frame++;
            int budget = BlocksPerFrame;
            for (int i = _jobs.Count - 1; i >= 0; i--)
            {
                Job job = _jobs[i];
                var planet = MyAPIGateway.Entities.GetEntityById(job.PlanetId) as MyPlanet;
                IMyStorage storage = planet == null || planet.Closed ? null : planet.Storage;
                if (storage == null || !storage.DeleteSupported)
                {
                    _jobs.RemoveAt(i);
                    continue;
                }
                bool done = job.Next >= job.Blocks.Length;
                if (done)
                {
                    // Finished: watch for the storage being swapped, then start over on the new one.
                    if (_frame > job.ExpiresFrame) _jobs.RemoveAt(i);
                    else if (!ReferenceEquals(storage, job.Storage)) job.Next = 0;
                    continue;
                }
                if (budget <= 0) continue;
                Vector3I size = storage.Size;
                while (budget > 0 && job.Next + 2 < job.Blocks.Length)
                {
                    var min = new Vector3I(job.Blocks[job.Next], job.Blocks[job.Next + 1], job.Blocks[job.Next + 2]) *
                              job.BlockSize;
                    Vector3I max = min + (job.BlockSize - 1);
                    job.Next += 3;
                    budget--;
                    min = Vector3I.Max(min, Vector3I.Zero);
                    max = Vector3I.Min(max, size - 1);
                    if (min.X > max.X || min.Y > max.Y || min.Z > max.Z) continue;
                    storage.DeleteRange(MyStorageDataTypeFlags.ContentAndMaterial, min, max, true);
                }
                if (job.Next + 2 >= job.Blocks.Length)
                {
                    job.Next = job.Blocks.Length;
                    job.Storage = storage;
                    job.ExpiresFrame = _frame + ReapplyWindowFrames;
                }
            }
        }

        public void Clear()
        {
            _jobs.Clear();
        }

        /// <summary>The storage block (of <paramref name="blockSize"/> voxels) holding a world position.</summary>
        public static Vector3I BlockAt(MyPlanet planet, Vector3D world, int blockSize)
        {
            Vector3D local = world - planet.PositionLeftBottomCorner;
            return new Vector3I(
                (int)System.Math.Floor(local.X / blockSize),
                (int)System.Math.Floor(local.Y / blockSize),
                (int)System.Math.Floor(local.Z / blockSize));
        }
    }
}

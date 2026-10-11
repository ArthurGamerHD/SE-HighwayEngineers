// ReSharper disable RedundantUsingDirective
using System;
using System.Collections.Generic;
using InvalidDataException = Adk.Compression.Exceptions.InvalidDataException;

namespace Adk.Compression.Zip
{
    public enum DeflateLevel
    {
        Fast,
        Default,
        Maximum
    }

    public static partial class Zlib
    {
        const int LiteralLengthSymbols = 286;
        const int DistanceSymbols = 30;
        const int CodeLengthSymbols = 19;
        const int EndOfBlockSymbol = 256;
        const int MaxLiteralLengthBits = 15;
        const int MaxDistanceBits = 15;
        const int MaxCodeLengthBits = 7;
        const int StoredBlockMaxBytes = 65535;

        const int MatchHashSize = 1 << 16;
        const int MatchHashMask = MatchHashSize - 1;
        const int ShortMatchMaxDistance = 4096;

        const int FastChain = 16;
        const int DefaultChain = 256;
        const int MaximumChain = 2048;
        const int MaximumParetoEntries = 16;
        const int OptimalWindowBytes = 1 << 18;
        const int OptimalIterations = 6;
        const int DefaultInitialBlockSymbols = 16384;
        const int MaximumInitialBlockSymbols = 2048;

        static readonly byte[] LengthSymbolByLength = CreateLengthSymbolTable();
        static readonly byte[] DistanceSymbolSmall = CreateDistanceSymbolTable(false);
        static readonly byte[] DistanceSymbolLarge = CreateDistanceSymbolTable(true);
        static readonly int[] FixedLiteralLengthBitCounts = CreateFixedLiteralLengthBitCounts();

        /// <summary>
        /// Encodes data as a raw RFC 1951 DEFLATE stream at <see cref="DeflateLevel.Default"/>.
        /// </summary>
        public static byte[] DeflateRaw(byte[] data)
        {
            return DeflateRaw(data, DeflateLevel.Default);
        }

        /// <summary>
        /// Encodes data as a raw RFC 1951 DEFLATE stream.
        /// Fast and Default use greedy and lazy LZ77 parsing; Maximum uses an
        /// iterated cost-model optimal parse. All levels split the symbol stream
        /// into blocks and pick stored, fixed, or dynamic Huffman per block.
        /// </summary>
        public static byte[] DeflateRaw(byte[] data, DeflateLevel level)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            DeflateSymbols symbols;
            int initialBlockSymbols;

            switch (level)
            {
                case DeflateLevel.Fast:
                    symbols = ParseGreedy(data, FastChain, false);
                    initialBlockSymbols = DefaultInitialBlockSymbols;
                    break;
                case DeflateLevel.Maximum:
                    symbols = ParseOptimal(data);
                    initialBlockSymbols = MaximumInitialBlockSymbols;
                    break;
                default:
                    symbols = ParseGreedy(data, DefaultChain, true);
                    initialBlockSymbols = DefaultInitialBlockSymbols;
                    break;
            }

            List<DeflateBlock> blocks = SplitBlocks(symbols, initialBlockSymbols);

            var writer = new BitWriter(Math.Max(64, data.Length / 2));
            for (int i = 0; i < blocks.Count; i++)
                WriteBlock(writer, data, symbols, blocks[i], i == blocks.Count - 1);

            return writer.ToArray();
        }

        /// <summary>
        /// Encodes data as an RFC 1950 zlib stream.
        /// </summary>
        public static byte[] DeflateZlib(byte[] data, DeflateLevel level)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            byte[] raw = DeflateRaw(data, level);
            byte[] output = new byte[checked(raw.Length + 6)];

            output[0] = 0x78;
            output[1] = 0xDA;
            Buffer.BlockCopy(raw, 0, output, 2, raw.Length);

            uint adler = ComputeAdler32(data, data.Length);
            int position = output.Length - 4;
            output[position++] = (byte)(adler >> 24);
            output[position++] = (byte)(adler >> 16);
            output[position++] = (byte)(adler >> 8);
            output[position] = (byte)adler;
            return output;
        }

        static DeflateSymbols ParseGreedy(byte[] data, int maxChain, bool lazy)
        {
            var finder = new MatchFinder(data, maxChain);
            var symbols = new DeflateSymbols(Math.Max(16, data.Length / 2));
            int position = 0;

            while (position < data.Length)
            {
                int distance;
                int length = finder.FindLongest(position, MatchLimit(data, position), out distance);
                finder.Insert(position);

                if (lazy && length >= DeflateMinMatch && length < DeflateMaxMatch)
                {
                    int nextDistance;
                    int nextLength = finder.FindLongest(position + 1, MatchLimit(data, position + 1), out nextDistance);
                    if (nextLength > length)
                    {
                        symbols.AddLiteral(data[position]);
                        position++;
                        continue;
                    }
                }

                if (length >= DeflateMinMatch)
                {
                    symbols.AddMatch(length, distance);
                    for (int i = 1; i < length; i++)
                        finder.Insert(position + i);

                    position += length;
                }
                else
                {
                    symbols.AddLiteral(data[position]);
                    position++;
                }
            }

            return symbols;
        }

        static int MatchLimit(byte[] data, int position)
        {
            return Math.Min(DeflateMaxMatch, data.Length - position);
        }

        static DeflateSymbols ParseOptimal(byte[] data)
        {
            var symbols = new DeflateSymbols(Math.Max(16, data.Length / 2));
            if (data.Length == 0)
                return symbols;

            DeflateCostModel seedModel = DeflateCostModel.FromSymbols(
                ParseGreedy(data, DefaultChain, true));

            var finder = new MatchFinder(data, MaximumChain);
            int windowCapacity = Math.Min(OptimalWindowBytes, data.Length);
            var matches = new ParetoMatches(windowCapacity);
            var costs = new float[windowCapacity + 1];
            var stepLengths = new int[windowCapacity + 1];
            var stepDistances = new int[windowCapacity + 1];

            for (int windowStart = 0; windowStart < data.Length; windowStart += OptimalWindowBytes)
            {
                int windowEnd = Math.Min(data.Length, windowStart + OptimalWindowBytes);
                matches.Collect(finder, data, windowStart, windowEnd);

                DeflateSymbols best = null;
                double bestBits = double.MaxValue;
                DeflateCostModel model = seedModel;

                for (int iteration = 0; iteration < OptimalIterations; iteration++)
                {
                    DeflateSymbols parse = RunOptimalParse(
                        data, windowStart, windowEnd, matches, model,
                        costs, stepLengths, stepDistances);

                    DeflateCostModel parseModel = DeflateCostModel.FromSymbols(parse);
                    double bits = parseModel.EstimateBits(parse);
                    if (bits < bestBits)
                    {
                        bestBits = bits;
                        best = parse;
                    }
                    else
                    {
                        break;
                    }

                    model = parseModel;
                }

                symbols.Append(best);
            }

            return symbols;
        }

        static DeflateSymbols RunOptimalParse(
            byte[] data,
            int windowStart,
            int windowEnd,
            ParetoMatches matches,
            DeflateCostModel model,
            float[] costs,
            int[] stepLengths,
            int[] stepDistances)
        {
            int count = windowEnd - windowStart;
            costs[0] = 0f;
            for (int i = 1; i <= count; i++)
                costs[i] = float.MaxValue;

            int previousLongest = 0;
            for (int i = 0; i < count; i++)
            {
                float baseCost = costs[i];

                float literalCost = baseCost + model.Literal[data[windowStart + i]];
                if (literalCost < costs[i + 1])
                {
                    costs[i + 1] = literalCost;
                    stepLengths[i + 1] = 1;
                    stepDistances[i + 1] = 0;
                }

                int first = matches.Offsets[i];
                int last = matches.Offsets[i + 1];
                if (first == last)
                {
                    previousLongest = 0;
                    continue;
                }

                int longest = matches.Lengths[last - 1];
                if (longest == DeflateMaxMatch && previousLongest == DeflateMaxMatch)
                {
                    int distance = matches.Distances[last - 1];
                    float cost = baseCost + model.Length[longest] + model.DistanceCost(distance);
                    if (cost < costs[i + longest])
                    {
                        costs[i + longest] = cost;
                        stepLengths[i + longest] = longest;
                        stepDistances[i + longest] = distance;
                    }
                }
                else
                {
                    int previousLength = DeflateMinMatch - 1;
                    for (int entry = first; entry < last; entry++)
                    {
                        int maximumLength = matches.Lengths[entry];
                        int distance = matches.Distances[entry];
                        float distanceCost = baseCost + model.DistanceCost(distance);

                        for (int length = previousLength + 1; length <= maximumLength; length++)
                        {
                            float cost = distanceCost + model.Length[length];
                            if (cost < costs[i + length])
                            {
                                costs[i + length] = cost;
                                stepLengths[i + length] = length;
                                stepDistances[i + length] = distance;
                            }
                        }

                        previousLength = maximumLength;
                    }
                }

                previousLongest = longest;
            }

            int stepCount = 0;
            for (int position = count; position > 0; position -= stepLengths[position])
                stepCount++;

            var steps = new int[stepCount];
            int index = stepCount;
            for (int position = count; position > 0; position -= stepLengths[position])
                steps[--index] = position;

            var symbols = new DeflateSymbols(stepCount);
            int start = 0;
            for (int i = 0; i < steps.Length; i++)
            {
                int end = steps[i];
                int length = end - start;
                if (length == 1)
                    symbols.AddLiteral(data[windowStart + start]);
                else
                    symbols.AddMatch(length, stepDistances[end]);

                start = end;
            }

            return symbols;
        }

        static List<DeflateBlock> SplitBlocks(DeflateSymbols symbols, int initialBlockSymbols)
        {
            var blocks = new List<DeflateBlock>();
            int byteStart = 0;

            for (int start = 0; start < symbols.Count || blocks.Count == 0; start += initialBlockSymbols)
            {
                int end = Math.Min(symbols.Count, start + initialBlockSymbols);
                var block = DeflateBlock.Create(symbols, start, end, byteStart);
                byteStart += block.ByteCount;
                blocks.Add(block);

                if (end == symbols.Count)
                    break;
            }

            if (blocks.Count < 2)
                return blocks;

            var gains = new List<double>(blocks.Count);
            for (int i = 0; i + 1 < blocks.Count; i++)
                gains.Add(MergeGain(blocks[i], blocks[i + 1]));

            while (true)
            {
                int bestIndex = -1;
                double bestGain = 0d;
                for (int i = 0; i < gains.Count; i++)
                {
                    if (gains[i] > bestGain)
                    {
                        bestGain = gains[i];
                        bestIndex = i;
                    }
                }

                if (bestIndex < 0)
                    break;

                blocks[bestIndex] = DeflateBlock.Merge(blocks[bestIndex], blocks[bestIndex + 1]);
                blocks.RemoveAt(bestIndex + 1);
                gains.RemoveAt(bestIndex);

                if (bestIndex > 0)
                    gains[bestIndex - 1] = MergeGain(blocks[bestIndex - 1], blocks[bestIndex]);
                if (bestIndex < gains.Count)
                    gains[bestIndex] = MergeGain(blocks[bestIndex], blocks[bestIndex + 1]);
            }

            return blocks;
        }

        static double MergeGain(DeflateBlock left, DeflateBlock right)
        {
            DeflateBlock merged = DeflateBlock.Merge(left, right);
            return left.Bits + right.Bits - merged.Bits;
        }

        static void WriteBlock(
            BitWriter writer,
            byte[] data,
            DeflateSymbols symbols,
            DeflateBlock block,
            bool final)
        {
            switch (block.Kind)
            {
                case DeflateBlockKind.Stored:
                    WriteStoredBlocks(writer, data, block.ByteStart, block.ByteCount, final);
                    return;

                case DeflateBlockKind.Fixed:
                    writer.WriteBits(final ? 1 : 0, 1);
                    writer.WriteBits(1, 2);
                    WriteBlockSymbols(writer, symbols, block, FixedLiteralLengthCodes, FixedDistanceCodes);
                    return;

                default:
                    DynamicHeader header = block.Header;
                    writer.WriteBits(final ? 1 : 0, 1);
                    writer.WriteBits(2, 2);
                    writer.WriteBits(header.LiteralLengthCount - 257, 5);
                    writer.WriteBits(header.DistanceCount - 1, 5);
                    writer.WriteBits(header.CodeLengthCount - 4, 4);

                    for (int i = 0; i < header.CodeLengthCount; i++)
                        writer.WriteBits(header.CodeLengthBitCounts[CodeLengthOrder[i]], 3);

                    HuffmanCode[] codeLengthCodes = CreateCanonicalCodes(header.CodeLengthBitCounts);
                    for (int i = 0; i < header.RunLengthCount; i++)
                    {
                        int symbol = header.RunLengthSymbols[i];
                        HuffmanCode code = codeLengthCodes[symbol];
                        writer.WriteBits((int)code.Bits, code.BitCount);

                        if (symbol == 16)
                            writer.WriteBits(header.RunLengthExtra[i], 2);
                        else if (symbol == 17)
                            writer.WriteBits(header.RunLengthExtra[i], 3);
                        else if (symbol == 18)
                            writer.WriteBits(header.RunLengthExtra[i], 7);
                    }

                    WriteBlockSymbols(
                        writer,
                        symbols,
                        block,
                        CreateCanonicalCodes(header.LiteralLengthBitCounts),
                        CreateCanonicalCodes(header.DistanceBitCounts));
                    return;
            }
        }

        static void WriteStoredBlocks(BitWriter writer, byte[] data, int start, int count, bool final)
        {
            int position = start;
            int end = start + count;

            do
            {
                int chunk = Math.Min(StoredBlockMaxBytes, end - position);
                bool last = final && position + chunk == end;

                writer.WriteBits(last ? 1 : 0, 1);
                writer.WriteBits(0, 2);
                writer.FlushToByte();
                writer.WriteBits(chunk, 16);
                writer.WriteBits(~chunk & 0xFFFF, 16);
                writer.WriteAlignedBytes(data, position, chunk);
                position += chunk;
            }
            while (position < end);
        }

        static void WriteBlockSymbols(
            BitWriter writer,
            DeflateSymbols symbols,
            DeflateBlock block,
            HuffmanCode[] literalLengthCodes,
            HuffmanCode[] distanceCodes)
        {
            for (int i = block.SymbolStart; i < block.SymbolEnd; i++)
            {
                int length = symbols.Lengths[i];
                if (length == 0)
                {
                    WriteCode(writer, literalLengthCodes, symbols.Values[i]);
                    continue;
                }

                int lengthSymbol = LengthSymbolByLength[length];
                WriteCode(writer, literalLengthCodes, 257 + lengthSymbol);
                int lengthExtraBits = LengthExtraBits[lengthSymbol];
                if (lengthExtraBits != 0)
                    writer.WriteBits(length - LengthBase[lengthSymbol], lengthExtraBits);

                int distance = symbols.Values[i];
                int distanceSymbol = GetDistanceSymbol(distance);
                WriteCode(writer, distanceCodes, distanceSymbol);
                int distanceExtraBits = DistanceExtraBits[distanceSymbol];
                if (distanceExtraBits != 0)
                    writer.WriteBits(distance - DistanceBase[distanceSymbol], distanceExtraBits);
            }

            WriteCode(writer, literalLengthCodes, EndOfBlockSymbol);
        }

        static void WriteCode(BitWriter writer, HuffmanCode[] codes, int symbol)
        {
            HuffmanCode code = codes[symbol];
            if (code.BitCount == 0)
                throw new InvalidDataException("Missing DEFLATE Huffman code for symbol " + symbol + ".");

            writer.WriteBits((int)code.Bits, code.BitCount);
        }

        static int GetDistanceSymbol(int distance)
        {
            return distance <= 256
                ? DistanceSymbolSmall[distance]
                : DistanceSymbolLarge[(distance - 1) >> 7];
        }

        static byte[] CreateLengthSymbolTable()
        {
            var table = new byte[DeflateMaxMatch + 1];
            for (int symbol = 0; symbol < LengthBase.Length; symbol++)
            {
                int first = LengthBase[symbol];
                int last = first + (1 << LengthExtraBits[symbol]) - 1;
                if (symbol == LengthBase.Length - 1)
                    last = first;

                for (int length = first; length <= last && length <= DeflateMaxMatch; length++)
                    table[length] = (byte)symbol;
            }

            return table;
        }

        static byte[] CreateDistanceSymbolTable(bool large)
        {
            var table = new byte[large ? 256 : 257];
            for (int symbol = 0; symbol < DistanceSymbols; symbol++)
            {
                int first = DistanceBase[symbol];
                int last = first + (1 << DistanceExtraBits[symbol]) - 1;
                for (int distance = first; distance <= last; distance++)
                {
                    if (large)
                    {
                        if (distance > 256)
                            table[(distance - 1) >> 7] = (byte)symbol;
                    }
                    else if (distance <= 256)
                    {
                        table[distance] = (byte)symbol;
                    }
                }
            }

            return table;
        }

        static int[] CreateFixedLiteralLengthBitCounts()
        {
            var lengths = new int[288];
            for (int i = 0; i <= 143; i++) lengths[i] = 8;
            for (int i = 144; i <= 255; i++) lengths[i] = 9;
            for (int i = 256; i <= 279; i++) lengths[i] = 7;
            for (int i = 280; i <= 287; i++) lengths[i] = 8;
            return lengths;
        }

        /// <summary>
        /// Builds optimal prefix code lengths limited to <paramref name="maxBits"/>.
        /// Unlimited Huffman is used when it already fits; otherwise package-merge.
        /// A single used symbol is paired with a second one so the code is complete.
        /// </summary>
        static int[] BuildCodeBitCounts(int[] frequencies, int maxBits)
        {
            var bitCounts = new int[frequencies.Length];
            int used = 0;
            for (int i = 0; i < frequencies.Length; i++)
            {
                if (frequencies[i] > 0)
                    used++;
            }

            if (used == 0)
                return bitCounts;

            var symbols = new int[used];
            var keys = new long[used];
            int index = 0;
            for (int i = 0; i < frequencies.Length; i++)
            {
                if (frequencies[i] <= 0)
                    continue;

                symbols[index] = i;
                keys[index] = ((long)frequencies[i] << 10) | (uint)i;
                index++;
            }

            Array.Sort(keys, symbols);

            if (used == 1)
            {
                bitCounts[symbols[0]] = 1;
                bitCounts[symbols[0] == 0 ? 1 : 0] = 1;
                return bitCounts;
            }

            if (!TryBuildHuffmanBitCounts(frequencies, symbols, maxBits, bitCounts))
                BuildPackageMergeBitCounts(frequencies, symbols, maxBits, bitCounts);

            return bitCounts;
        }

        static bool TryBuildHuffmanBitCounts(int[] frequencies, int[] sortedSymbols, int maxBits, int[] bitCounts)
        {
            int leafCount = sortedSymbols.Length;
            int nodeCount = leafCount * 2 - 1;
            var weights = new long[nodeCount];
            var parents = new int[nodeCount];

            for (int i = 0; i < leafCount; i++)
                weights[i] = frequencies[sortedSymbols[i]];

            int leaf = 0;
            int internalNode = leafCount;
            for (int next = leafCount; next < nodeCount; next++)
            {
                int first = TakeSmallest(weights, ref leaf, leafCount, ref internalNode, next);
                int second = TakeSmallest(weights, ref leaf, leafCount, ref internalNode, next);
                weights[next] = weights[first] + weights[second];
                parents[first] = next;
                parents[second] = next;
            }

            var depths = new int[nodeCount];
            for (int node = nodeCount - 2; node >= 0; node--)
                depths[node] = depths[parents[node]] + 1;

            for (int i = 0; i < leafCount; i++)
            {
                if (depths[i] > maxBits)
                    return false;
            }

            for (int i = 0; i < leafCount; i++)
                bitCounts[sortedSymbols[i]] = depths[i];

            return true;
        }

        static int TakeSmallest(long[] weights, ref int leaf, int leafCount, ref int internalNode, int internalEnd)
        {
            if (leaf < leafCount && (internalNode >= internalEnd || weights[leaf] <= weights[internalNode]))
                return leaf++;

            return internalNode++;
        }

        static void BuildPackageMergeBitCounts(int[] frequencies, int[] sortedSymbols, int maxBits, int[] bitCounts)
        {
            int leafCount = sortedSymbols.Length;
            var leaves = new List<PackageNode>(leafCount);
            for (int i = 0; i < leafCount; i++)
                leaves.Add(new PackageNode(frequencies[sortedSymbols[i]], sortedSymbols[i], null, null));

            List<PackageNode> current = leaves;
            for (int level = 1; level < maxBits; level++)
            {
                var packages = new List<PackageNode>(current.Count / 2);
                for (int i = 0; i + 1 < current.Count; i += 2)
                {
                    packages.Add(new PackageNode(
                        current[i].Weight + current[i + 1].Weight, -1, current[i], current[i + 1]));
                }

                var merged = new List<PackageNode>(leaves.Count + packages.Count);
                int leafIndex = 0;
                int packageIndex = 0;
                while (leafIndex < leaves.Count || packageIndex < packages.Count)
                {
                    if (packageIndex >= packages.Count ||
                        (leafIndex < leaves.Count && leaves[leafIndex].Weight <= packages[packageIndex].Weight))
                    {
                        merged.Add(leaves[leafIndex++]);
                    }
                    else
                    {
                        merged.Add(packages[packageIndex++]);
                    }
                }

                current = merged;
            }

            int selected = 2 * leafCount - 2;
            var stack = new Stack<PackageNode>();
            for (int i = 0; i < selected; i++)
            {
                stack.Push(current[i]);
                while (stack.Count > 0)
                {
                    PackageNode node = stack.Pop();
                    if (node.Symbol >= 0)
                    {
                        bitCounts[node.Symbol]++;
                        continue;
                    }

                    stack.Push(node.Left);
                    stack.Push(node.Right);
                }
            }
        }

        static DynamicHeader BuildDynamicHeader(int[] literalLengthFrequencies, int[] distanceFrequencies)
        {
            int[] literalLengthBitCounts = BuildCodeBitCounts(literalLengthFrequencies, MaxLiteralLengthBits);
            int[] distanceBitCounts = BuildCodeBitCounts(distanceFrequencies, MaxDistanceBits);

            int literalLengthCount = 257;
            for (int i = LiteralLengthSymbols - 1; i >= 257; i--)
            {
                if (literalLengthBitCounts[i] != 0)
                {
                    literalLengthCount = i + 1;
                    break;
                }
            }

            int distanceCount = 1;
            for (int i = DistanceSymbols - 1; i >= 1; i--)
            {
                if (distanceBitCounts[i] != 0)
                {
                    distanceCount = i + 1;
                    break;
                }
            }

            var combined = new int[literalLengthCount + distanceCount];
            Array.Copy(literalLengthBitCounts, 0, combined, 0, literalLengthCount);
            Array.Copy(distanceBitCounts, 0, combined, literalLengthCount, distanceCount);

            DynamicHeader best = null;
            for (int variant = 0; variant < 8; variant++)
            {
                DynamicHeader header = BuildRunLengthHeader(
                    combined, (variant & 1) != 0, (variant & 2) != 0, (variant & 4) != 0);
                if (best == null || header.HeaderBits < best.HeaderBits)
                    best = header;
            }

            best.LiteralLengthCount = literalLengthCount;
            best.DistanceCount = distanceCount;
            best.LiteralLengthBitCounts = literalLengthBitCounts;
            best.DistanceBitCounts = distanceBitCounts;
            best.DataBits =
                SumBits(literalLengthFrequencies, literalLengthBitCounts) +
                SumBits(distanceFrequencies, distanceBitCounts);
            return best;
        }

        static DynamicHeader BuildRunLengthHeader(int[] lengths, bool useRepeat, bool useShortZeros, bool useLongZeros)
        {
            var runSymbols = new int[lengths.Length];
            var runExtra = new int[lengths.Length];
            int runCount = 0;

            int index = 0;
            while (index < lengths.Length)
            {
                int value = lengths[index];
                int run = 1;
                while (index + run < lengths.Length && lengths[index + run] == value)
                    run++;

                index += run;

                if (value == 0 && (useShortZeros || useLongZeros))
                {
                    while (run >= 3)
                    {
                        if (useLongZeros && run >= 11)
                        {
                            int take = Math.Min(run, 138);
                            runSymbols[runCount] = 18;
                            runExtra[runCount++] = take - 11;
                            run -= take;
                        }
                        else if (useShortZeros)
                        {
                            int take = Math.Min(run, 10);
                            runSymbols[runCount] = 17;
                            runExtra[runCount++] = take - 3;
                            run -= take;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                else if (useRepeat && run >= 4)
                {
                    runSymbols[runCount++] = value;
                    run--;
                    while (run >= 3)
                    {
                        int take = Math.Min(run, 6);
                        runSymbols[runCount] = 16;
                        runExtra[runCount++] = take - 3;
                        run -= take;
                    }
                }

                for (; run > 0; run--)
                    runSymbols[runCount++] = value;
            }

            var frequencies = new int[CodeLengthSymbols];
            for (int i = 0; i < runCount; i++)
                frequencies[runSymbols[i]]++;

            int[] codeLengthBitCounts = BuildCodeBitCounts(frequencies, MaxCodeLengthBits);

            int codeLengthCount = CodeLengthSymbols;
            while (codeLengthCount > 4 && codeLengthBitCounts[CodeLengthOrder[codeLengthCount - 1]] == 0)
                codeLengthCount--;

            long bits = 5 + 5 + 4 + 3L * codeLengthCount;
            for (int i = 0; i < runCount; i++)
            {
                int symbol = runSymbols[i];
                bits += codeLengthBitCounts[symbol];
                if (symbol == 16) bits += 2;
                else if (symbol == 17) bits += 3;
                else if (symbol == 18) bits += 7;
            }

            return new DynamicHeader
            {
                RunLengthSymbols = runSymbols,
                RunLengthExtra = runExtra,
                RunLengthCount = runCount,
                CodeLengthBitCounts = codeLengthBitCounts,
                CodeLengthCount = codeLengthCount,
                HeaderBits = bits
            };
        }

        static long SumBits(int[] frequencies, int[] bitCounts)
        {
            long bits = 0;
            for (int i = 0; i < frequencies.Length; i++)
                bits += (long)frequencies[i] * bitCounts[i];

            return bits;
        }

        enum DeflateBlockKind
        {
            Stored,
            Fixed,
            Dynamic
        }

        sealed class DeflateBlock
        {
            public int SymbolStart;
            public int SymbolEnd;
            public int ByteStart;
            public int ByteCount;
            public int[] LiteralLengthFrequencies;
            public int[] DistanceFrequencies;
            public long ExtraBits;
            public DeflateBlockKind Kind;
            public DynamicHeader Header;
            public double Bits;

            public static DeflateBlock Create(DeflateSymbols symbols, int start, int end, int byteStart)
            {
                var block = new DeflateBlock
                {
                    SymbolStart = start,
                    SymbolEnd = end,
                    ByteStart = byteStart,
                    LiteralLengthFrequencies = new int[LiteralLengthSymbols],
                    DistanceFrequencies = new int[DistanceSymbols]
                };

                for (int i = start; i < end; i++)
                {
                    int length = symbols.Lengths[i];
                    if (length == 0)
                    {
                        block.LiteralLengthFrequencies[symbols.Values[i]]++;
                        block.ByteCount++;
                        continue;
                    }

                    int lengthSymbol = LengthSymbolByLength[length];
                    int distanceSymbol = GetDistanceSymbol(symbols.Values[i]);
                    block.LiteralLengthFrequencies[257 + lengthSymbol]++;
                    block.DistanceFrequencies[distanceSymbol]++;
                    block.ExtraBits += LengthExtraBits[lengthSymbol] + DistanceExtraBits[distanceSymbol];
                    block.ByteCount += length;
                }

                block.LiteralLengthFrequencies[EndOfBlockSymbol]++;
                block.Evaluate();
                return block;
            }

            public static DeflateBlock Merge(DeflateBlock left, DeflateBlock right)
            {
                var block = new DeflateBlock
                {
                    SymbolStart = left.SymbolStart,
                    SymbolEnd = right.SymbolEnd,
                    ByteStart = left.ByteStart,
                    ByteCount = left.ByteCount + right.ByteCount,
                    LiteralLengthFrequencies = new int[LiteralLengthSymbols],
                    DistanceFrequencies = new int[DistanceSymbols],
                    ExtraBits = left.ExtraBits + right.ExtraBits
                };

                for (int i = 0; i < LiteralLengthSymbols; i++)
                {
                    block.LiteralLengthFrequencies[i] =
                        left.LiteralLengthFrequencies[i] + right.LiteralLengthFrequencies[i];
                }

                for (int i = 0; i < DistanceSymbols; i++)
                    block.DistanceFrequencies[i] = left.DistanceFrequencies[i] + right.DistanceFrequencies[i];

                block.LiteralLengthFrequencies[EndOfBlockSymbol]--;
                block.Evaluate();
                return block;
            }

            void Evaluate()
            {
                Header = BuildDynamicHeader(LiteralLengthFrequencies, DistanceFrequencies);
                double dynamicBits = 3 + Header.HeaderBits + Header.DataBits + ExtraBits;

                double fixedBits = 3 + ExtraBits;
                for (int i = 0; i < LiteralLengthSymbols; i++)
                    fixedBits += (double)LiteralLengthFrequencies[i] * FixedLiteralLengthBitCounts[i];
                for (int i = 0; i < DistanceSymbols; i++)
                    fixedBits += DistanceFrequencies[i] * 5d;

                int storedChunks = Math.Max(1, (ByteCount + StoredBlockMaxBytes - 1) / StoredBlockMaxBytes);
                double storedBits = storedChunks * (3d + 5d + 32d) + 8d * ByteCount;

                Kind = DeflateBlockKind.Dynamic;
                Bits = dynamicBits;
                if (fixedBits < Bits)
                {
                    Kind = DeflateBlockKind.Fixed;
                    Bits = fixedBits;
                }

                if (storedBits < Bits)
                {
                    Kind = DeflateBlockKind.Stored;
                    Bits = storedBits;
                }
            }
        }

        sealed class DynamicHeader
        {
            public int LiteralLengthCount;
            public int DistanceCount;
            public int CodeLengthCount;
            public int[] LiteralLengthBitCounts;
            public int[] DistanceBitCounts;
            public int[] CodeLengthBitCounts;
            public int[] RunLengthSymbols;
            public int[] RunLengthExtra;
            public int RunLengthCount;
            public long HeaderBits;
            public long DataBits;
        }

        sealed class PackageNode
        {
            public readonly long Weight;
            public readonly int Symbol;
            public readonly PackageNode Left;
            public readonly PackageNode Right;

            public PackageNode(long weight, int symbol, PackageNode left, PackageNode right)
            {
                Weight = weight;
                Symbol = symbol;
                Left = left;
                Right = right;
            }
        }

        sealed class DeflateSymbols
        {
            public int[] Lengths;
            public int[] Values;
            public int Count;

            public DeflateSymbols(int capacity)
            {
                capacity = Math.Max(16, capacity);
                Lengths = new int[capacity];
                Values = new int[capacity];
            }

            public void AddLiteral(byte value)
            {
                EnsureCapacity(1);
                Lengths[Count] = 0;
                Values[Count++] = value;
            }

            public void AddMatch(int length, int distance)
            {
                EnsureCapacity(1);
                Lengths[Count] = length;
                Values[Count++] = distance;
            }

            public void Append(DeflateSymbols other)
            {
                EnsureCapacity(other.Count);
                Array.Copy(other.Lengths, 0, Lengths, Count, other.Count);
                Array.Copy(other.Values, 0, Values, Count, other.Count);
                Count += other.Count;
            }

            void EnsureCapacity(int additional)
            {
                int required = Count + additional;
                if (required <= Lengths.Length)
                    return;

                int capacity = Math.Max(required, Lengths.Length * 2);
                Array.Resize(ref Lengths, capacity);
                Array.Resize(ref Values, capacity);
            }
        }

        sealed class DeflateCostModel
        {
            public readonly float[] Literal = new float[256];
            public readonly float[] Length = new float[DeflateMaxMatch + 1];
            readonly float[] _distance = new float[DistanceSymbols];

            public float DistanceCost(int distance)
            {
                return _distance[GetDistanceSymbol(distance)];
            }

            public static DeflateCostModel FromSymbols(DeflateSymbols symbols)
            {
                var literalLengthCounts = new int[LiteralLengthSymbols];
                var distanceCounts = new int[DistanceSymbols];
                CountSymbols(symbols, literalLengthCounts, distanceCounts);

                float[] literalLengthBits = ToBits(literalLengthCounts);
                float[] distanceBits = ToBits(distanceCounts);

                var model = new DeflateCostModel();
                for (int i = 0; i < 256; i++)
                    model.Literal[i] = literalLengthBits[i];

                for (int length = DeflateMinMatch; length <= DeflateMaxMatch; length++)
                {
                    int symbol = LengthSymbolByLength[length];
                    model.Length[length] = literalLengthBits[257 + symbol] + LengthExtraBits[symbol];
                }

                for (int symbol = 0; symbol < DistanceSymbols; symbol++)
                    model._distance[symbol] = distanceBits[symbol] + DistanceExtraBits[symbol];

                return model;
            }

            public double EstimateBits(DeflateSymbols symbols)
            {
                double bits = 0d;
                for (int i = 0; i < symbols.Count; i++)
                {
                    int length = symbols.Lengths[i];
                    bits += length == 0
                        ? Literal[symbols.Values[i]]
                        : Length[length] + DistanceCost(symbols.Values[i]);
                }

                return bits;
            }

            static void CountSymbols(DeflateSymbols symbols, int[] literalLengthCounts, int[] distanceCounts)
            {
                for (int i = 0; i < symbols.Count; i++)
                {
                    int length = symbols.Lengths[i];
                    if (length == 0)
                    {
                        literalLengthCounts[symbols.Values[i]]++;
                        continue;
                    }

                    literalLengthCounts[257 + LengthSymbolByLength[length]]++;
                    distanceCounts[GetDistanceSymbol(symbols.Values[i])]++;
                }

                literalLengthCounts[EndOfBlockSymbol]++;
            }

            static float[] ToBits(int[] counts)
            {
                long total = 0;
                for (int i = 0; i < counts.Length; i++)
                    total += counts[i];

                var bits = new float[counts.Length];
                double logTotal = total > 0 ? Math.Log(total, 2d) : 0d;
                for (int i = 0; i < counts.Length; i++)
                {
                    bits[i] = counts[i] > 0
                        ? (float)(logTotal - Math.Log(counts[i], 2d))
                        : (float)(logTotal + 1d);
                }

                return bits;
            }
        }

        sealed class MatchFinder
        {
            readonly byte[] _data;
            readonly int[] _heads = new int[MatchHashSize];
            readonly int[] _previous = new int[DeflateWindowSize];
            readonly int _maxChain;

            public MatchFinder(byte[] data, int maxChain)
            {
                _data = data;
                _maxChain = maxChain;
                for (int i = 0; i < _heads.Length; i++)
                    _heads[i] = -1;
            }

            public void Insert(int position)
            {
                if (position > _data.Length - DeflateMinMatch)
                    return;

                int hash = Hash(position);
                _previous[position & DeflateWindowMask] = _heads[hash];
                _heads[hash] = position;
            }

            public int FindLongest(int position, int limit, out int distance)
            {
                distance = 0;
                int best = 0;
                if (limit < DeflateMinMatch)
                    return 0;

                int minimum = position - DeflateWindowSize;
                int candidate = _heads[Hash(position)];
                for (int chain = 0; candidate >= 0 && candidate >= minimum && chain < _maxChain; chain++)
                {
                    int length = MatchLength(candidate, position, limit, best);
                    if (length > best)
                    {
                        best = length;
                        distance = position - candidate;
                        if (best >= limit)
                            break;
                    }

                    int next = _previous[candidate & DeflateWindowMask];
                    if (next >= candidate)
                        break;

                    candidate = next;
                }

                if (best < DeflateMinMatch || (best == DeflateMinMatch && distance > ShortMatchMaxDistance))
                {
                    distance = 0;
                    return 0;
                }

                return best;
            }

            public int Collect(int position, int limit, int[] lengths, int[] distances)
            {
                if (limit < DeflateMinMatch)
                    return 0;

                int count = 0;
                int best = DeflateMinMatch - 1;
                int minimum = position - DeflateWindowSize;
                int candidate = _heads[Hash(position)];
                for (int chain = 0; candidate >= 0 && candidate >= minimum && chain < _maxChain; chain++)
                {
                    int length = MatchLength(candidate, position, limit, best);
                    if (length > best)
                    {
                        best = length;
                        if (count == lengths.Length)
                            count--;

                        lengths[count] = length;
                        distances[count++] = position - candidate;
                        if (best >= limit)
                            break;
                    }

                    int next = _previous[candidate & DeflateWindowMask];
                    if (next >= candidate)
                        break;

                    candidate = next;
                }

                return count;
            }

            int MatchLength(int candidate, int position, int limit, int best)
            {
                byte[] data = _data;
                if (best > 0 && best < limit && data[candidate + best] != data[position + best])
                    return 0;

                int length = 0;
                while (length < limit && data[candidate + length] == data[position + length])
                    length++;

                return length;
            }

            int Hash(int position)
            {
                uint value =
                    ((uint)_data[position] << 16) |
                    ((uint)_data[position + 1] << 8) |
                    _data[position + 2];

                value *= 0x9E3779B1u;
                return (int)((value >> 16) & MatchHashMask);
            }
        }

        sealed class ParetoMatches
        {
            public int[] Offsets;
            public int[] Lengths;
            public int[] Distances;
            readonly int[] _scratchLengths = new int[MaximumParetoEntries];
            readonly int[] _scratchDistances = new int[MaximumParetoEntries];

            public ParetoMatches(int windowCapacity)
            {
                Offsets = new int[windowCapacity + 1];
                Lengths = new int[Math.Max(16, windowCapacity)];
                Distances = new int[Lengths.Length];
            }

            public void Collect(MatchFinder finder, byte[] data, int windowStart, int windowEnd)
            {
                int total = 0;
                for (int position = windowStart; position < windowEnd; position++)
                {
                    Offsets[position - windowStart] = total;
                    int limit = Math.Min(DeflateMaxMatch, windowEnd - position);
                    int count = finder.Collect(position, limit, _scratchLengths, _scratchDistances);
                    finder.Insert(position);

                    if (count > 0 && _scratchLengths[count - 1] == DeflateMinMatch &&
                        _scratchDistances[count - 1] > ShortMatchMaxDistance)
                    {
                        count = 0;
                    }

                    if (total + count > Lengths.Length)
                    {
                        int capacity = Math.Max(total + count, Lengths.Length * 2);
                        Array.Resize(ref Lengths, capacity);
                        Array.Resize(ref Distances, capacity);
                    }

                    for (int i = 0; i < count; i++)
                    {
                        Lengths[total] = _scratchLengths[i];
                        Distances[total++] = _scratchDistances[i];
                    }
                }

                Offsets[windowEnd - windowStart] = total;
            }
        }
    }
}

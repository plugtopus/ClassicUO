#region license
//  Copyright (C) 2018 ClassicUO Development Community on Github
//
//	This project is an alternative client for the game Ultima Online.
//	The goal of this is to develop a lightweight client considering 
//	new technologies.  
//      
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program.  If not, see <https://www.gnu.org/licenses/>.
#endregion
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using ClassicUO.Configuration;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Scenes;
using ClassicUO.Interfaces;
using ClassicUO.IO;
using ClassicUO.IO.Resources;
using ClassicUO.Utility;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Multi = ClassicUO.Game.GameObjects.Multi;

namespace ClassicUO.Game.Map
{
    internal sealed class Map : IDisposable
    {
        private readonly bool[] _blockAccessList = new bool[0x1000];
        //private const int CHUNKS_NUM = 5;
        //private const int MAX_CHUNKS = CHUNKS_NUM * 2 + 1;
        private readonly List<int> _usedIndices = new List<int>();

        public Map(int index)
        {
            Index = index;
            FileManager.Map.LoadMap(index);
            MapBlockIndex = FileManager.Map.MapBlocksSize[Index, 0] * FileManager.Map.MapBlocksSize[Index, 1];
            Chunks = new Chunk[MapBlockIndex];
        }

        public int Index { get; }
    

        public Chunk[] Chunks { get; private set; }

        public int MapBlockIndex { get; set; }

        public Point Center { get; set; }

        public Chunk GetMapChunk(int rblock, int blockX, int blockY)
        {
            ref Chunk chunk = ref Chunks[rblock];
            if (chunk == null)
            {
                _usedIndices.Add(rblock);
                chunk = new Chunk((ushort)blockX, (ushort)blockY);
                chunk.Load(Index);
            }
            return chunk;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Tile GetTile(short x, short y, bool load = true)
        {
            if (x < 0 || y < 0)
                return null;
            int cellX = x >> 3;
            int cellY = y >> 3;
            int block = GetBlock(cellX, cellY);

            if (block >= Chunks.Length)
                return null;
            ref Chunk chuck = ref Chunks[block];

            if (chuck == null)
            {
                if (load)
                {
                    _usedIndices.Add(block);
                    chuck = new Chunk((ushort)cellX, (ushort)cellY);
                    chuck.Load(Index);
                }
                else
                    return null;
            }

            chuck.LastAccessTime = Engine.Ticks;
            return chuck.Tiles[x % 8, y % 8];
        }

        public Tile GetTile(int x, int y, bool load = true)
        {
            return GetTile((short)x, (short)y, load);
        }

        public sbyte GetTileZ(int x, int y)
        {
            if (x < 0 || y < 0)
                return -125;
            IndexMap blockIndex = GetIndex(x >> 3, y >> 3);

            if (blockIndex.MapAddress == 0)
                return -125;
            int mx = x % 8;
            int my = y % 8;

            unsafe
            {
                MapBlock* mp = (MapBlock*)blockIndex.MapAddress;
                MapCells* cells = (MapCells*)&mp->Cells;

                return cells[my * 8 + mx].Z;
            }
        }

        public void ClearBockAccess()
        {
            Array.Clear(_blockAccessList, 0, _blockAccessList.Length);
        }

        public sbyte CalculateNearZ(sbyte defaultZ, int x, int y, int z)
        {
            ref bool access = ref _blockAccessList[(x & 0x3F) + ((y & 0x3F) << 6)];

            if (access)
                return defaultZ;
            access = true;
            Tile tile = GetTile(x, y, false);

            if (tile != null)
            {
                GameObject obj = tile.FirstNode;

                for(; obj != null; obj = obj.Right)
                {
                    if (!(obj is Static) && !(obj is Multi))
                        continue;

                    if (obj is Mobile)
                        continue;

                    //if (obj is IDynamicItem dyn && (!TileData.IsRoof(dyn.ItemData.Flags) || Math.Abs(z - obj.Z) > 6))
                    //    continue;

                    if (GameObjectHelper.TryGetStaticData(obj, out var itemdata) && (!itemdata.IsRoof || Math.Abs(z - obj.Z) > 6))
                        continue;

                    break;
                }

                if (obj == null)
                    return defaultZ;
                sbyte tileZ = obj.Z;

                if (tileZ < defaultZ)
                    defaultZ = tileZ;
                defaultZ = CalculateNearZ(defaultZ, x - 1, y, tileZ);
                defaultZ = CalculateNearZ(defaultZ, x + 1, y, tileZ);
                defaultZ = CalculateNearZ(defaultZ, x, y - 1, tileZ);
                defaultZ = CalculateNearZ(defaultZ, x, y + 1, tileZ);
            }

            return defaultZ;
        }

        public IndexMap GetIndex(int blockX, int blockY)
        {
            int block = GetBlock(blockX, blockY);
            ref IndexMap[] list = ref FileManager.Map.BlockData[Index];

            return block >= list.Length ? IndexMap.Invalid : list[block];
        }

        private int GetBlock(int blockX, int blockY)
        {
            return blockX * FileManager.Map.MapBlocksSize[Index, 1] + blockY;
        }

        public void ClearUnusedBlocks()
        {
            int count = 0;
            long ticks = Engine.Ticks - Constants.CLEAR_TEXTURES_DELAY;

            for (int i = 0; i < _usedIndices.Count; i++)
            {
                ref Chunk block = ref Chunks[_usedIndices[i]];

                if (block.LastAccessTime < ticks && block.HasNoExternalData())
                {
                    block.Dispose();
                    block = null;
                    _usedIndices.RemoveAt(i--);

                    if (++count >= Constants.MAX_MAP_OBJECT_REMOVED_BY_GARBAGE_COLLECTOR)
                        break;
                }
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < _usedIndices.Count; i++)
            {
                ref Chunk block = ref Chunks[_usedIndices[i]];
                block.Dispose();
                block = null;
                _usedIndices.RemoveAt(i--);
            }

            FileManager.Map.UnloadMap(Index);
            Chunks = null;
        }

        public void Initialize()
        {
            const int XY_OFFSET = 30;

            int minBlockX = ((Center.X - XY_OFFSET) >> 3) - 1;
            int minBlockY = ((Center.Y - XY_OFFSET) >> 3) - 1;
            int maxBlockX = ((Center.X + XY_OFFSET) >> 3) + 1;
            int maxBlockY = ((Center.Y + XY_OFFSET) >> 3) + 1;

            if (minBlockX < 0)
                minBlockX = 0;

            if (minBlockY < 0)
                minBlockY = 0;

            if (maxBlockX >= FileManager.Map.MapBlocksSize[Index, 0])
                maxBlockX = FileManager.Map.MapBlocksSize[Index, 0] - 1;

            if (maxBlockY >= FileManager.Map.MapBlocksSize[Index, 1])
                maxBlockY = FileManager.Map.MapBlocksSize[Index, 1] - 1;
            long tick = Engine.Ticks;
            long maxDelay = Engine.FrameDelay[1] >> 1;

            for (int i = minBlockX; i <= maxBlockX; i++)
            {
                int index = i * FileManager.Map.MapBlocksSize[Index, 1];

                for (int j = minBlockY; j <= maxBlockY; j++)
                {
                    int cellindex = index + j;
                    ref Chunk chunk = ref Chunks[cellindex];

                    if (chunk == null)
                    {
                        if (Engine.Ticks - tick >= maxDelay)
                            return;
                        _usedIndices.Add(cellindex);
                        chunk = new Chunk((ushort)i, (ushort)j);
                        chunk.Load(Index);
                    }

                    chunk.LastAccessTime = Engine.Ticks;
                }
            }
        }
    }
}
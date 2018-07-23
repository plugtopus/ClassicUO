﻿using System.IO;
using System.Runtime.InteropServices;

namespace ClassicUO.AssetsLoader
{
    public static class Gumps
    {
        public const int GUMP_COUNT = 0x10000;
        private static UOFile _file;

        public static void Load()
        {
            var path = Path.Combine(FileManager.UoFolderPath, "gumpartLegacyMUL.uop");
            if (File.Exists(path))
            {
                _file = new UOFileUop(path, ".tga", GUMP_COUNT, true);
            }
            else
            {
                path = Path.Combine(FileManager.UoFolderPath, "Gumpart.mul");
                var pathidx = Path.Combine(FileManager.UoFolderPath, "Gumpidx.mul");

                if (File.Exists(path) && File.Exists(pathidx)) _file = new UOFileMul(path, pathidx, GUMP_COUNT, 12);
            }

            var pathdef = Path.Combine(FileManager.UoFolderPath, "gump.def");
            if (!File.Exists(pathdef))
                return;

            using (var reader = new StreamReader(File.OpenRead(pathdef)))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length <= 0 || line[0] == '#')
                        continue;
                    var defs = line.Replace('\t', ' ').Split(' ');
                    if (defs.Length != 3)
                        continue;

                    var ingump = int.Parse(defs[0]);
                    var outgump = int.Parse(defs[1].Replace("{", string.Empty).Replace("}", string.Empty));
                    var outhue = int.Parse(defs[2]);

                    _file.Entries[ingump] = _file.Entries[outgump];
                }
            }
        }


        public static unsafe ushort[] GetGump(int index, out int width, out int height)
        {
            var (length, extra, patcher) = _file.SeekByEntryIndex(index);

            if (extra == -1)
            {
                width = 0;
                height = 0;
                return null;
            }

            width = (extra >> 16) & 0xFFFF;
            height = extra & 0xFFFF;

            if (width <= 0 || height <= 0)
                return null;

            var pixels = new ushort[width * height];
            var lookuplist = (int*) _file.PositionAddress;

            for (var y = 0; y < height; y++)
            {
                var gsize = 0;
                if (y < height - 1)
                    gsize = lookuplist[y + 1] - lookuplist[y];
                else
                    gsize = length / 4 - lookuplist[y];

                var gmul = (GumpBlock*) (_file.PositionAddress + lookuplist[y] * 4);

                var pos = y * width;

                for (var i = 0; i < gsize; i++)
                {
                    var val = gmul[i].Value;
                    var a = (ushort) ((val > 0 ? 0x8000 : 0) | val);
                    int count = gmul[i].Run;
                    for (var j = 0; j < count; j++)
                        pixels[pos++] = a;
                }
            }

            return pixels;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct GumpBlock
        {
            public readonly ushort Value;
            public readonly ushort Run;
        }
    }
}
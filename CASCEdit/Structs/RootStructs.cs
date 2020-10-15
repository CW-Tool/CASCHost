using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CASCEdit.Handlers;
using CASCEdit.Helpers;

namespace CASCEdit.Structs
{
    [Flags]
    public enum LocaleFlags : uint
    {
        All = 0xFFFFFFFF,
        None = 0,
        enUS = 0x2,
        koKR = 0x4,
        frFR = 0x10,
        deDE = 0x20,
        zhCN = 0x40,
        esES = 0x80,
        zhTW = 0x100,
        enGB = 0x200,
        enCN = 0x400,
        enTW = 0x800,
        esMX = 0x1000,
        ruRU = 0x2000,
        ptBR = 0x4000,
        itIT = 0x8000,
        ptPT = 0x10000,
        All_WoW = enUS | koKR | frFR | deDE | zhCN | esES | zhTW | enGB | esMX | ruRU | ptBR | itIT | ptPT
    }

    [Flags]
    public enum ContentFlags : uint
    {
        //flag
        None            = 0,
        F00000001       = 0x1,
        F00000002       = 0x2,
        F00000004       = 0x4,
        F00000008       = 0x8, // added in 7.2.0.23436
        F00000010       = 0x10, // added in 7.2.0.23436
        LowViolence     = 0x80, // many models have this flag

        F00000800       = 0x0800,     // new in 9.0.1 36230?
        F00020000       = 0x20000,    // new in 9.0.1 36230?
        F00040000       = 0x40000,    // new in 9.0.1 36230?
        F00080000       = 0x80000,    // new in 9.0.1 36230?
        F00100000       = 0x100000,   // new in 9.0.1 36230?
        F00200000       = 0x200000,   // new in 9.0.1 36230?
        F00400000       = 0x400000,   // new in 9.0.1 36230?
        F00800000       = 0x800000,   // new in 9.0.1 36230?
        F02000000       = 0x2000000,  // new in 9.0.1 36230?
        F04000000       = 0x4000000,  // new in 9.0.1 36230?
        F08000000       = 0x8000000,  // new in 9.0.1 36230?

        NoNames         = 0x10000000,
        F20000000       = 0x20000000, // added in 21737
        Bundle          = 0x40000000,
        NoCompression   = 0x80000000 // sounds have this flag
    }

    public class RootChunk
    {
        public uint Count;
        public ContentFlags contentFlags;
        public LocaleFlags localeFlags;
        public List<RootEntry> Entries = new List<RootEntry>();
    }

    public class RootEntry
    {
        public MD5Hash CEKey;
        public uint FileDataIdOffset;
        public ulong NameHash;

        public uint FileDataId;
        public string Path;
    }
}

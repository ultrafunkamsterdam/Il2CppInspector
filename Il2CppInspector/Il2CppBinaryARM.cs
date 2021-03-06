﻿/*
    Copyright 2017 Perfare - https://github.com/Perfare/Il2CppDumper
    Copyright 2017 Katy Coe - http://www.hearthcode.org - http://www.djkaty.com

    All rights reserved.
*/

using System.Linq;

namespace Il2CppInspector
{
    internal class Il2CppBinaryARM : Il2CppBinary
    {
        public Il2CppBinaryARM(IFileFormatReader stream) : base(stream) { }

        public Il2CppBinaryARM(IFileFormatReader stream, uint codeRegistration, uint metadataRegistration) : base(stream, codeRegistration, metadataRegistration) { }

        protected override (uint, uint) ConsiderCode(uint loc, uint globalOffset) {
            // Assembly bytes to search for at start of each function
            uint metadataRegistration, codeRegistration;

            // ARMv7
            var bytes = new byte[] { 0x1c, 0x0, 0x9f, 0xe5, 0x1c, 0x10, 0x9f, 0xe5, 0x1c, 0x20, 0x9f, 0xe5 };
            Image.Position = loc;
            var buff = Image.ReadBytes(12);
            if (bytes.SequenceEqual(buff)) {
                Image.Position = loc + 0x2c;
                var subaddr = Image.ReadUInt32() + globalOffset;
                Image.Position = subaddr + 0x28;
                codeRegistration = Image.ReadUInt32() + globalOffset;
                Image.Position = subaddr + 0x2C;
                var ptr = Image.ReadUInt32() + globalOffset;
                Image.Position = Image.MapVATR(ptr);
                metadataRegistration = Image.ReadUInt32();
                return (codeRegistration, metadataRegistration);
            }

            // ARMv7 metadata v23
            Image.Position = loc;

            // Check for ADD Rx, PC in relevant parts of function
            var func = Image.ReadBytes(0x20);
            if (func[0x0C] == 0x79 && func[0x0D] == 0x44 && // ADD R1, PC
                func[0x16] == 0x78 && func[0x17] == 0x44 && // ADD R0, PC
                func[0x1E] == 0x7A && func[0x1F] == 0x44)   // ADD R2, PC
            {
                // Follow path to metadata pointer
                var ppMetadata = decodeMovImm32(func) + loc + 0x10;
                Image.Position = ppMetadata;
                metadataRegistration = Image.ReadUInt32();

                // Follow path to code pointer
                var pCode = decodeMovImm32(func.Skip(8).Take(4).Concat(func.Skip(14).Take(4)).ToArray());
                codeRegistration = pCode + loc + 0x1A + globalOffset;

                return (codeRegistration, metadataRegistration);
            }

            // ARMv7 Thumb (T1)
            // http://liris.cnrs.fr/~mmrissa/lib/exe/fetch.php?media=armv7-a-r-manual.pdf - A8.8.106
            // http://armconverter.com/hextoarm/
            bytes = new byte[] { 0x2d, 0xe9, 0x00, 0x48, 0xeb, 0x46 };
            Image.Position = loc;
            buff = Image.ReadBytes(6);
            if (!bytes.SequenceEqual(buff))
                return (0, 0);
            bytes = new byte[] { 0x00, 0x23, 0x00, 0x22, 0xbd, 0xe8, 0x00, 0x48 };
            Image.Position += 0x10;
            buff = Image.ReadBytes(8);
            if (!bytes.SequenceEqual(buff))
                return (0, 0);
            Image.Position = loc + 6;
            Image.Position = (Image.MapVATR(decodeMovImm32(Image.ReadBytes(8))) & 0xfffffffc) + 0x0e;
            metadataRegistration = decodeMovImm32(Image.ReadBytes(8));
            codeRegistration = decodeMovImm32(Image.ReadBytes(8));
            return (codeRegistration, metadataRegistration);
        }

        private uint decodeMovImm32(byte[] asm) {
            ushort low = (ushort) (asm[2] + ((asm[3] & 0x70) << 4) + ((asm[1] & 0x04) << 9) + ((asm[0] & 0x0f) << 12));
            ushort high = (ushort) (asm[6] + ((asm[7] & 0x70) << 4) + ((asm[5] & 0x04) << 9) + ((asm[4] & 0x0f) << 12));
            return (uint) ((high << 16) + low);
        }
    }
}

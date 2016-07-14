﻿using NesCore.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesCore.Storage
{
    class CartridgeMapAxRom : CartridgeMap
    {
        public CartridgeMapAxRom(Cartridge cartridge)
            : base(cartridge)
        {
            programBank = 0;
            programRam = new byte[0x6000 - 0x4100];
        }

        public override string Name { get { return "AxROM"; } }


        public override byte this[ushort address]
        {
            get
            {
                if (address < 0x2000)
                    return Cartridge.CharacterRom[address];

                if (address >= 0x8000)
                    return Cartridge.ProgramRom[programBank * 0x8000 + address - 0x8000];

                if (address >= 0x6000)
                    return Cartridge.SaveRam[(ushort)(address - 0x6000)];

                if (address >= 0x4100)
                    return programRam[address - 0x4100];

                throw new Exception("Unhandled " + Name + " mapper read at address: " + Hex.Format(address));
            }

            set
            {
                if (address < 0x2000)
                    Cartridge.CharacterRom[address] = value;
                else if (address >= 0x8000)
                {
                    int oldProgramBank = programBank;

                    // ---M-PPP
                    programBank = value & 7;

                    // mirror mode
                    MirrorMode = (value & 0x10) == 0x10 ? MirrorMode.Single1 : MirrorMode.Single0;

                    // invalidate address region
                    if (programBank != oldProgramBank)
                        ProgramBankSwitch?.Invoke(0x8000, 0x8000);
                }
                else if (address >= 0x6000)
                    Cartridge.SaveRam[(ushort)(address - 0x6000)] = value;
                else if (address >= 0x4100)
                    programRam[address - 0x4100] = value;
                else
                    throw new Exception("Unhandled " + Name + " mapper write at address: " + Hex.Format(address));
            }
        }

        private int programBank;
        private byte[] programRam;
    }
}

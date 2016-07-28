﻿using NesCore.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesCore.Storage
{
    class CartridgeMapKonamiVrc2 : CartridgeMap
    {
        public enum Variant
        {
            Vrc2a,
            Vrc2b
        }

        public CartridgeMapKonamiVrc2(Cartridge cartridge, Variant variant) : base(cartridge)
        {
            this.variant = variant;
            mapperName = variant == Variant.Vrc2a ? "Konami VRC2 Rev A" : "Konami VRC2 Rev B";

            programRam = new byte[0x2000];

            programBankCount = Cartridge.ProgramRom.Count / 0x2000;
            programLastTwoBanksAddress = (programBankCount - 2) * 0x2000;

            characterBankCount = Cartridge.CharacterRom.Length / 0x400;
            characterBank = new int[8];
        }

        public override string Name { get { return mapperName; } }

        public override byte this[ushort address]
        {
            get
            {
                if (address < 0x2000)
                {
                    int bankIndex = address / 0x400;
                    int bankOffset = address % 0x400;
                    int selectedCharacterBank = characterBank[bankIndex];
                    if (variant == Variant.Vrc2a)
                        selectedCharacterBank >>= 1;
                    return Cartridge.CharacterRom[selectedCharacterBank * 0x400 + bankOffset];
                }
                else if (address >= 0x6000 && address < 0x8000)
                {
                    return programRam[address % 0x2000];
                }
                else if (address >= 0x8000 && address < 0xA000)
                {
                    return Cartridge.ProgramRom[programBank0 * 0x2000 + address % 0x2000];
                }
                else if (address >= 0xA000 && address < 0xC000)
                {
                    return Cartridge.ProgramRom[programBank1 * 0x2000 + address % 0x2000];
                }
                else if (address >= 0xC000)
                {
                    return Cartridge.ProgramRom[programLastTwoBanksAddress + address % 0x4000];
                }
                else
                    return (byte)(address >> 8); // open bus
            }

            set
            {
                // for PRG ROM region, mask out all bits except for lines A12..A15, A0, A1
                if (address >= 0x8000)
                    address &= 0xF003;
                byte addressHighNybble = (byte)(address >> 12);

                if (address >= 0x6000 && address < 0x8000)
                {
                    programRam[address % 0x2000] = value;
                }
                else if (addressHighNybble == 0x8)
                {
                    programBank0 = value & 0x1F;
                    programBank0 %= programBankCount;
                }
                else if (addressHighNybble == 0x9)
                {
                    switch (value % 0x01)
                    {
                        case 0: MirrorMode = MirrorMode.Vertical; break;
                        case 1: MirrorMode = MirrorMode.Horizontal; break;
                    }
                }
                else if (addressHighNybble == 0x0A)
                {
                    programBank1 = value & 0x1F;
                    programBank1 %= programBankCount;
                }
                else if (addressHighNybble >= 0xB && addressHighNybble < 0xF)
                {
                    int low2Bits = address & 0x03;

                    // normalise Rev A to Rev B for simplicity
                    if (variant == Variant.Vrc2a)
                    {
                        if (low2Bits == 2)
                            low2Bits = 1;
                        else if (low2Bits == 1)
                            low2Bits = 2;
                    }

                    int bankIndex = (address - 0xB000) / 0x1000;
                    bankIndex *= 2;
                    bankIndex += low2Bits / 2;

                    if (low2Bits % 2 == 0)
                    {
                        // low 4 bits
                        characterBank[bankIndex] &= 0xF0;
                        characterBank[bankIndex] |= value & 0x0F;
                    }
                    else
                    {
                        // high 4 bits
                        characterBank[bankIndex] &= 0x0F;
                        characterBank[bankIndex] |= (value & 0x0F) << 4;
                    }
                    characterBank[bankIndex] %= characterBankCount;
                }
                else
                    Debug.WriteLine("VRC2: Unknown write of value " + Hex.Format(value) + " at address " + Hex.Format(address));
            }
        }

        private Variant variant;
        private string mapperName;

        private byte[] programRam;

        private int programBankCount;
        private int programBank0;
        private int programBank1;
        private int programLastTwoBanksAddress;

        private int characterBankCount;
        private int[] characterBank;
    }
}

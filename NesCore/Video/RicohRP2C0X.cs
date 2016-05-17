﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesCore.Video
{
    // delegate for reading a byte from a givne memory address
    public delegate byte ReadByte(ushort address);

    // delegate for writing a byte from a given memory address
    public delegate void WriteByte(ushort address, byte value);

    // delegate for writing pixel in a frame buffer implementation
    public delegate void WritePixel(byte x, byte y, byte paletteIndex);

    // delegate for presenting the frame on vblank
    public delegate void PresentFrame();

    public class RicohRP2C0X
    {
        public RicohRP2C0X()
        {
            Reset();
        }

        // implementation hooks
        public ReadByte ReadByte { get; set; }
        public WriteByte WriteByte { get; set; }

        public WritePixel WritePixel { get; set; }
        public PresentFrame PresentFrame { get; set; }

        /// <summary>
        /// Control register ($2000 PPUCTRL)
        /// </summary>
        public byte Control
        {
            set
            {
                registerLatch = value;
                flagNameTable = (byte)(value & 0x03);
                flagIncrement = (value & 0x04) != 0;
                flagSpriteTable = (value & 0x08) != 0;
                flagBackgroundTable = (value & 0x10) != 0;
                spriteSize = (value & 0x20) != 0 ? SpriteSize.Size8x16 : SpriteSize.Size8x8;
                flagMasterSlave = (value & 0x40) != 0;
                nmiOutput = (value & 0x80) != 0;

                NmiChange();
                // t: ....BA.. ........ = d: ......BA
                t = (ushort)((t & 0xF3FF) | ((value & 0x03) << 10));
            }
        }

        /// <summary>
        /// Mask register ($2001 PPUMASK(
        /// </summary>
        public byte Mask
        {
            set
            {
                registerLatch = value;
                flagGrayscale = (value & 0x01) != 0;
                flagShowLeftBackground = (value & 0x02) != 0;
                flagShowLeftSprites = (value & 0x04) != 0;
                flagShowBackground = (value & 0x08) != 0;
                flagShowSprites = (value & 0x10) != 0;
                flagRedTint = (value & 0x20) != 0;
                flagGreenTint = (value & 0x40) != 0;
                flagBlueTint = (value & 0x80) != 0;
            }
        }

        /// <summary>
        /// Status register ($2002 PPUSTATUS)
        /// </summary>
        public byte Status
        {
            get
            {
                byte result = (byte)(registerLatch & 0x1F);
                if (flagSpriteOverflow)
                    result |= 0x20;
                if (flagSpriteZeroHit)
                    result |= 0x40;
                if (nmiOccurred)
                    result |= 0x80;

                nmiOccurred = false;
                NmiChange();
                writeToggle = WriteToggle.First;
                return result;
            }
        }

        /// <summary>
        /// Object attribute memory address ($2003 OAMADDR)
        /// </summary>
        public byte OAMAddress
        {
            set
            {
                registerLatch = value;
                oamAddress = value;
            }
        }

        /// <summary>
        /// Object attribute memory data ($2004 OAMDATA).
        /// Reads from current OAM address, writes cause OAM address to advance
        /// </summary>
        public byte OAMData
        {
            get
            {
                return oamData[oamAddress];
            }
            set
            {
                registerLatch = value;
                oamData[oamAddress++] = value;
            }
        }

        /// <summary>
        /// Scrolling position register ($2005 PPUSCROLL)
        /// Accepts two sequential writes
        /// </summary>
        public byte Scroll
        {
            set
            {
                registerLatch = value;
                if (writeToggle == WriteToggle.First)
                {
                    // t: ........ ...HGFED = d: HGFED...
                    // x:               CBA = d: .....CBA
                    t = (ushort)((t & 0xFFE0) | (value >> 3));
                    x = (byte)(value & 0x07);
                    writeToggle = WriteToggle.Second;
                }
                else
                {
                    // t: .CBA..HG FED..... = d: HGFEDCBA
                    t = (ushort)((t & 0x8FFF) | ((value & 0x07) << 12));
                    t = (ushort)((t & 0xFC1F) | ((value & 0xF8) << 2));
                    writeToggle = WriteToggle.First;
                }
            }
        }

        /// <summary>
        /// Address register ($2006 PPUADDR)
        /// Two writes required in high-low byte order
        /// </summary>
        public byte Address
        {
            set
            {
                registerLatch = value;
                if (writeToggle == WriteToggle.First)
                {
                    // write high address byte
                    // t: ..FEDCBA ........ = d: ..FEDCBA
                    // t: .X...... ........ = 0
                    t = (ushort)((t & 0x80FF) | ((value & 0x3F) << 8));
                    writeToggle = WriteToggle.Second;
                }
                else
                {
                    // write low address byte
                    // t: ........ HGFEDCBA = d: HGFEDCBA
                    // v                    = t
                    t = (ushort)((t & 0xFF00) | value);
                    v = t;
                    writeToggle = WriteToggle.First;
                }
            }
        }

        // $2007: PPUDATA (read)
        private byte ReadData()
        {
            byte value = ReadByte(v);
            // emulate buffered reads
            if (v % 0x4000 < 0x3F00)
            {
                byte buffered = bufferedData;
                bufferedData = value;
                value = buffered;
            }
            else
            {
                bufferedData = ReadByte((ushort)(v - 0x1000));
            }

            // increment address
            if (flagIncrement)
                v += 0x20;
            else
                v += 0x01;

            return value;
        }

        // $2007: PPUDATA (write)
        private void WriteData(byte value)
        {
            registerLatch = value;
            WriteByte(v, value);

            // increment address
            if (flagIncrement)
                v += 0x20;
            else
                v += 0x01;
        }

        // $4014: OAMDMA
        private void WriteDMA(byte value)
        {
            registerLatch = value;

            ushort address = (ushort)(value << 8);

            for (int i = 0; i < 256; i++)
                oamData[oamAddress++] = ReadByte(address++);

            //TODO: stall
            /*
            cpu.stall += 513

            if cpu.Cycles % 2 == 1 {
                cpu.stall++

            }*/
            throw new NotImplementedException();
        }


        /// <summary>
        /// resets the PPU
        /// </summary>
        public void Reset()
        {
            cycle = 340;
            scanLine = 240;

            Control = 0x00;
            Mask = 0x00;
            OAMAddress = 0x00;
        }

        // Step executes a single PPU cycle
        public void Step()
        {
            Tick();

            bool renderingEnabled = flagShowBackground || flagShowSprites;
            bool preLine = scanLine == 261;
            bool visibleLine = scanLine < 240;
            // postLine := ppu.ScanLine == 240
            bool renderLine = preLine || visibleLine;

            bool preFetchCycle = cycle >= 321 && cycle <= 336;
            bool visibleCycle = cycle >= 1 && cycle <= 256;
            bool fetchCycle = preFetchCycle || visibleCycle;

            // background logic
            if (renderingEnabled)
            {
                if (visibleLine && visibleCycle)
                    RenderPixel();

                if (renderLine && fetchCycle)
                {
                    tileData <<= 4;

                    switch (cycle % 8)
                    {
                        case 1:
                            FetchNameTableByte();
                            break;
                        case 3:
                            FetchAttributeTableByte();
                            break;
                        case 5:
                            FetchLowTileByte();
                            break;
                        case 7:
                            FetchHighTileByte();
                            break;
                        case 0:
                            StoreTileData();
                            break;
                    }
                }

                if (preLine && cycle >= 280 && cycle <= 304)
                    CopyY();

                if (renderLine)
                {
                    if (fetchCycle && cycle % 8 == 0)
                        IncrementX();

                    if (cycle == 256)
                        IncrementY();

                    if (cycle == 257)
                        CopyX();
                }
            }

            // sprite logic
            if (renderingEnabled)
            {
                if (cycle == 257)
                {
                    if (visibleLine)
                        EvaluateSprites();
                    else
                        spriteCount = 0;
                }
            }

            // vblank logic
            if (scanLine == 241 && cycle == 1)
                SetVerticalBlank();

            if (preLine && cycle == 1)
            {
                ClearVerticalBlank();
                flagSpriteZeroHit = false;
                flagSpriteOverflow = false;
            }
        }

        /// <summary>
        /// Saves the state of the PPU
        /// </summary>
        /// <param name="streamWriter"></param>
        public void Save(StreamWriter streamWriter)
        {
            streamWriter.Write(cycle);
            streamWriter.Write(scanLine);
            streamWriter.Write(paletteData);
            streamWriter.Write(nameTableData);
            streamWriter.Write(oamData);
            streamWriter.Write(v);
            streamWriter.Write(t);
            streamWriter.Write(x);
            streamWriter.Write(writeToggle);
            streamWriter.Write(evenFrame);
            streamWriter.Write(nmiOccurred);
            streamWriter.Write(nmiOutput);
            streamWriter.Write(nmiPrevious);
            streamWriter.Write(nmiDelay);
            streamWriter.Write(nameTableByte);
            streamWriter.Write(attributeTableByte);
            streamWriter.Write(lowTileByte);
            streamWriter.Write(highTileByte);
            streamWriter.Write(tileData);
            streamWriter.Write(spriteCount);
            streamWriter.Write(spritePatterns);
            streamWriter.Write(spritePositions);
            streamWriter.Write(spritePriorities);
            streamWriter.Write(spriteIndexes);
            streamWriter.Write(flagNameTable);
            streamWriter.Write(flagIncrement);
            streamWriter.Write(flagSpriteTable);
            streamWriter.Write(flagBackgroundTable);
            streamWriter.Write(spriteSize);
            streamWriter.Write(flagMasterSlave);
            streamWriter.Write(flagGrayscale);
            streamWriter.Write(flagShowLeftBackground);
            streamWriter.Write(flagShowLeftSprites);
            streamWriter.Write(flagShowBackground);
            streamWriter.Write(flagShowSprites);
            streamWriter.Write(flagRedTint);
            streamWriter.Write(flagGreenTint);
            streamWriter.Write(flagBlueTint);
            streamWriter.Write(flagSpriteZeroHit);
            streamWriter.Write(flagSpriteOverflow);
	        streamWriter.Write(oamAddress);
	        streamWriter.Write(bufferedData);
        }

        /// <summary>
        /// Restores the state of the PPU
        /// </summary>
        /// <param name="streamReader"></param>
        public void Load(StreamReader streamReader)
        {
            throw new NotImplementedException();
        }

        private byte ReadPalette(ushort address)
        {
            if (address >= 16 && address % 4 == 0)
                address -= 16;
            return paletteData[address];
        }

        private void WritePalette(ushort address, byte value)
        {
            if (address >= 16 && address % 4 == 0)
                address -= 16;
            paletteData[address] = value;
        }
        
        // NTSC Timing Helper Functions
        private void IncrementX()
        {
            // increment hori(v)
            // if coarse X == 31
            if ((v & 0x001F) == 0x1F)
            {
                // coarse X = 0
                v &= 0xFFE0;
                // switch horizontal nametable
                v ^= 0x0400;
            }
            else
            {
                // increment coarse X
                ++v;
            }
        }

        private void IncrementY()
        {
            // increment vert(v)
            // if fine Y < 7
            if ((v & 0x7000) != 0x7000)
            {
                // increment fine Y
                v += 0x1000;
            }
            else
            {
                // fine Y = 0
                v &= 0x8FFF;
                // let y = coarse Y
                int y = (v & 0x03E0) >> 5;
                      
                if (y == 29)
                {
                    // coarse Y = 0
                    y = 0;
                    // switch vertical nametable
                    v ^= 0x0800;
                }
                else if (y == 31)
                {
                    // coarse Y = 0, nametable not switched
                    y = 0;
                }
                else
                {
                    // increment coarse Y
                    ++y;
                }

                // put coarse Y back into v
                v = (ushort)((v & 0xFC1F) | (y << 5));
            }
        }

        private void CopyX()
        {
            // hori(v) = hori(t)
            // v: .....F.. ...EDCBA = t: .....F.. ...EDCBA
            v = (ushort)((v & 0xFBE0) | (t & 0x041F));
        }

        private void CopyY()
        {
            // vert(v) = vert(t)
            // v: .IHGF.ED CBA..... = t: .IHGF.ED CBA.....
            v = (ushort)((v & 0x841F) | (t & 0x7BE0));
        }

        private void NmiChange()
        {
            bool nmi = nmiOutput && nmiOccurred;

            if (nmi && !nmiPrevious)
            {
                // TODO: this fixes some games but the delay shouldn't have to be so
                // long, so the timings are off somewhere
                nmiDelay = 15;
            }
            nmiPrevious = nmi;
        }

        private void SetVerticalBlank()
        {
            //ppu.front, ppu.back = ppu.back, ppu.front

            nmiOccurred = true;
            NmiChange();

            // call hook to present frame on vblank
            PresentFrame();
        }

        private void ClearVerticalBlank()
        {
            nmiOccurred = false;
            NmiChange();
        }

        private void FetchNameTableByte()
        {
            nameTableByte = ReadByte(v);
        }

        private void FetchAttributeTableByte()
        {
            ushort address = (ushort)(0x23C0 | (v & 0x0C00) | ((v >> 4) & 0x38) | ((v >> 2) & 0x07));
            int shift = ((v >> 4) & 4) | (v & 2);
            attributeTableByte = (byte)(((ReadByte(address) >> shift) & 3) << 2);
        }

        private void FetchLowTileByte()
        {
            int fineY = (v >> 12) & 7;

            ushort address = (ushort)(flagBackgroundTable ? 0x1000 : 0x0000);

            address += (ushort)(nameTableByte * 16 + fineY);

            lowTileByte = ReadByte(address);
        }

        private void FetchHighTileByte()
        {
            int fineY = (v >> 12) & 7;

            ushort address = (ushort)(flagBackgroundTable ? 0x1000 : 0x0000);

            address += (ushort)(nameTableByte * 16 + fineY + 8);

            highTileByte = ReadByte(address);
        }

        private void StoreTileData()
        {
            uint data = 0;

            for (int i = 0; i < 8; i++)
            {
                int p1 = (lowTileByte & 0x80) >> 7;
                int p2 = (highTileByte & 0x80) >> 6;

                lowTileByte <<= 1;
                highTileByte <<= 1;
                data <<= 4;

                data |= (uint)(attributeTableByte | p1 | p2);

            }

            tileData |= (ulong)(data);
        }

        private uint FetchTileData()
        {
            return (uint)(tileData >> 32);
        }

        private byte GetBackgroundPixel()
        {
	        if (flagShowBackground)
                return 0;

            uint data = FetchTileData() >> ((7 - x) * 4);
            return (byte)(data & 0x0F);
        }

        private byte GetSpritePixel(out byte spriteIndex)
        {
            spriteIndex = 0;
            if (!flagShowSprites)
                return 0;

	        for (int i = 0; i < spriteCount; i++)
            {
                int offset = (cycle - 1) - spritePositions[i];
                if (offset < 0 || offset > 7)
                    continue;

                offset = 7 - offset;
                byte colour = (byte)((spritePatterns[i] >> (offset * 4)) & 0x0F);
                if (colour % 4 == 0)
                    continue;

                spriteIndex = (byte)i;
		        return colour;
            }

            spriteIndex = 0;
            return 0;
        }

        private void RenderPixel()
        {
            x = (byte)(cycle - 1);
            byte y = (byte)scanLine;

            byte backgroundPixel = GetBackgroundPixel();

            byte spriteIndex = 0;
            byte spritePixel = GetSpritePixel(out spriteIndex);

            if (x < 8 && !flagBackgroundTable)
                backgroundPixel = 0;

            if (x < 8 && !flagShowLeftSprites)
                spritePixel = 0;

            bool opaqueBackground = backgroundPixel % 4 != 0;

            bool opaqueSprite = spritePixel % 4 != 0;

            byte paletteIndex = 0;

            if (opaqueBackground)
            {
                // opaque background pixel
                if (opaqueSprite)
                {
                    // opaque background and sprite pixels

                    // check sprite 0 hit
                    if (spriteIndexes[spriteIndex] == 0 && x < 255)
                        flagSpriteZeroHit = true;

                    // determine if sprite or backgroubnd pixel prevails
                    if (spritePriorities[spriteIndex] == 0)
                        paletteIndex = (byte)(spritePixel | 0x10);
                    else
                        paletteIndex = backgroundPixel;
                }
                else
                {
                    // opaque background and transparent sprite pixel
                    paletteIndex = backgroundPixel;
                }
            }
            else
            {
                // transparent backgorund
                if (opaqueSprite)
                {
                    // transparent background and opaque sprite pixels
                    paletteIndex = (byte)(spritePixel | 0x10);
                }
                else
                {
                    // transparent backgrounbd and sprite pixels
                    paletteIndex = 0;
                }
            }
            
            // hook to write pixel
            WritePixel(x, y, paletteIndex);
        }

        private uint FetchSpritePattern(int i, int row)
        {
            byte tile = oamData[i * 4 + 1];
            byte attributes = oamData[i * 4 + 2];
            ushort address = 0;
            if (spriteSize == SpriteSize.Size8x8)
            {
                if ((attributes & 0x80) == 0x80)
                    row = 7 - row;

                address = (ushort)(flagSpriteTable ? 0x1000 : 0x0000);
            }
            else // 8x16
            {
		        if ((attributes & 0x80) == 0x80)
                    row = 15 - row;

                tile &= 0xFE;
		        if (row > 7)
                {
                    ++tile;
                    row -= 8;
		        }
                address = (ushort)(0x1000 * (tile % 1));
            }
            address += (ushort)(tile * 16 + row);

            byte a = (byte)((attributes & 3) << 2);
            lowTileByte = ReadByte(address);
            highTileByte = ReadByte((ushort)(address + 8));

            uint data = 0;

            byte p1 = 0, p2 = 0;

		    if ((attributes & 0x40) == 0x40)
            {
                p1 = (byte)((lowTileByte & 1) << 0);
                p2 = (byte)((highTileByte & 1) << 1);
                lowTileByte >>= 1;
                highTileByte >>= 1;
		    }
            else
            {
                p1 = (byte)((lowTileByte & 0x80) >> 7);
                p2 = (byte)((highTileByte & 0x80) >> 6);
                lowTileByte <<= 1;
                highTileByte <<= 1;
		    }
            data <<= 4;
            data |= (uint)(a | p1 | p2);
	        
            return data;
        }

        private void EvaluateSprites()
        {
            int spriteHeight = spriteSize == SpriteSize.Size8x16 ? 16 : 8;

            int count = 0;
            for (int index = 0; index < 64; index++)
            {
                byte spriteY = oamData[index * 4 + 0];
                byte spriteAttributes = oamData[index * 4 + 2];
                byte spriteX = oamData[index * 4 + 3];

                int row = scanLine - spriteY;

                if (row < 0 || row >= spriteHeight)
                    continue;

                if (count < 8)
                {
                    spritePatterns[count] = FetchSpritePattern(index, row);
                    spritePositions[count] = spriteX;
                    spritePriorities[count] = (byte)((spriteAttributes >> 5) & 1);
                    spriteIndexes[count] = (byte)index;
                }
                ++count;
            }

            if (count > 8)
            {
                count = 8;
                flagSpriteOverflow = true;
            }
            spriteCount = count;
        }

        // tick updates Cycle, ScanLine and Frame counters
        private void Tick()
        {
            if (nmiDelay > 0)
            {
                --nmiDelay;
        
                if (nmiDelay == 0 && nmiOutput && nmiOccurred)
                {
                    throw new NotImplementedException();
                    /*ppu.console.CPU.triggerNMI()*/
                }
            }

            if (flagShowBackground || flagShowSprites)
            {
                if (evenFrame && scanLine == 261 && cycle == 339)
                {
                    cycle = 0;
                    scanLine = 0;
                    evenFrame = false;
                    return;
                }
            }
            ++cycle;

            if (cycle > 340)
            {
                cycle = 0;
                ++scanLine;

                if (scanLine > 261)
                {
                    scanLine = 0;
                    evenFrame = !evenFrame;
                }
            }
        }

        private int cycle; // 0-340
        private int scanLine; // 0-261, 0-239=visible, 240=post, 241-260=vblank, 261=pre

        // storage variables
        private byte[] paletteData = new byte[32];
        private byte[] nameTableData = new byte[2048];
        private byte[] oamData = new byte[256];

        // PPU registers
        private ushort v; // current vram address (15 bit)
        private ushort t; // temporary vram address (15 bit)
        private byte x;  // fine x scroll (3 bit)
        private WriteToggle writeToggle;  // write toggle (1 bit)
        private bool evenFrame; // even/odd frame
        private byte registerLatch; // status register
        
        // NMI flags
        private bool nmiOccurred;
        private bool nmiOutput;
        private bool nmiPrevious;
        private byte nmiDelay;

        // background temporary variables
        private byte nameTableByte;
        private byte attributeTableByte;
        private byte lowTileByte;
        private byte highTileByte;
        private ulong tileData;

        // sprite temporary variables
        private int spriteCount;
        private uint[] spritePatterns = new uint[8];
        private byte[] spritePositions = new byte[8];
        private byte[] spritePriorities = new byte[8];
        private byte[] spriteIndexes = new byte[8];

        // $2000 PPUCTRL
        private byte flagNameTable;        // 0: $2000; 1: $2400; 2: $2800; 3: $2C00
        private bool flagIncrement;        // 0: add 1; 1: add 32
        private bool flagSpriteTable;      // 0: $0000; 1: $1000; ignored in 8x16 mode
        private bool flagBackgroundTable;  // 0: $0000; 1: $1000
        private SpriteSize spriteSize;     // 8x8 or 8x16 pixels
        private bool flagMasterSlave;      // 0: read EXT; 1: write EXT

        // $2001 PPUMASK
        private bool flagGrayscale;          // 0: color; 1: grayscale
        private bool flagShowLeftBackground; // 0: hide; 1: show
        private bool flagShowLeftSprites;    // 0: hide; 1: show
        private bool flagShowBackground;     // 0: hide; 1: show
        private bool flagShowSprites;        // 0: hide; 1: show
        private bool flagRedTint;            // 0: normal; 1: emphasized
        private bool flagGreenTint;          // 0: normal; 1: emphasized
        private bool flagBlueTint;           // 0: normal; 1: emphasized

        // $2002 PPUSTATUS
        private bool flagSpriteZeroHit;
        private bool flagSpriteOverflow;

        // $2003 OAMADDR
        private byte oamAddress;

        // $2007 PPUDATA
        private byte bufferedData; // for buffered reads

        private enum WriteToggle
        {
            First,
            Second
        }

        private enum SpriteSize
        {
            Size8x8,
            Size8x16
        }
    }

}

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NesCore.Input
{
    public interface Controller
    {
        void Strobe();
        bool Read();
    }
}

﻿using System;
using System.Collections.Generic;
using System.Text;
using WebZen.Network;

namespace MuEmu.Network.AntiHack
{
    public interface IAntiHackMessage
    { }

    public class AntiHackMessageFactory : MessageFactory<AHOpCode, IAntiHackMessage>
    {
        public AntiHackMessageFactory()
        {
            // C2S
            Register<CAHCheck>(AHOpCode.AHCheck);

            // S2C
        }
    }
}

﻿using CSEmu.Network.Services;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using WebZen.Handlers;
using WebZen.Network;

namespace CSEmu.Network
{
    internal class WZConnectServer : WZServer
    {
        public WZConnectServer(IPEndPoint address, MessageHandler[] handler, MessageFactory[] factories, bool useRijndael)
        {
            Initialize(address, handler, new CSSessionFactory(), factories, useRijndael);
            SimpleStream = true;
            ServerManager.Initialize();
        }

        protected override void OnConnect(WZClient session)
        {
            var Session = session as CSSession;
            
            Session.SendAsync(new SConnectResult(1));
        }

        public override void OnDisconnect(WZClient session)
        {
            base.OnDisconnect(session);

            var Session = session as CSSession;
            ServerManager.Instance.Unregister(Session);
        }
    }
}

﻿using MuEmu.Events.BloodCastle;
using MuEmu.Events.ChaosCastle;
using MuEmu.Events.DevilSquare;
using MuEmu.Events.Kanturu;
using MuEmu.Events.LuckyCoins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebZen.Handlers;

namespace MuEmu.Network.Event
{
    public class EventServices : MessageHandler
    {
        [MessageHandler(typeof(CEventRemainTime))]
        public async Task CEventRemainTime(GSSession session, CEventRemainTime message)
        {
            var res = new SEventRemainTime { EventType = message.EventType };
            switch (message.EventType)
            {
                case EventEnterType.DevilSquare:
                    var evds = Program.EventManager.GetEvent<DevilSquares>();
                    res.RemainTime = evds.RemainTime;
                    res.EnteredUser = evds.Count;
                    break;
                case EventEnterType.BloodCastle:
                    var evbc = Program.EventManager.GetEvent<DevilSquares>();
                    res.RemainTime = evbc.RemainTime;
                    break;
                case EventEnterType.ChaosCastle:
                    var ev = Program.EventManager.GetEvent<ChaosCastles>();
                    res.RemainTime = ev.RemainTime;
                    res.EnteredUser = ev.Count;
                    break;
                case EventEnterType.IllusionTemple:
                    res.RemainTime = 0;
                    break;
            }

            await session.SendAsync(res);
        }

        [MessageHandler(typeof(CLuckyCoinsCount))]
        public async Task CLuckyCoinsCount(GSSession session/*, CLuckyCoinsCount message*/)
        {
            var coins = await LuckyCoins.Registered(session.Player);
            await session.SendAsync(new SLuckyCoinsCount(coins));
        }

        [MessageHandler(typeof(CLuckyCoinsRegistre))]
        public async Task CLuckyCoinsRegistre(GSSession session/*, CLuckyCoinsRegistre message*/)
        {
            var coins = await LuckyCoins.Registre(session.Player);
            await session.SendAsync(new SLuckyCoinsCount(coins));
        }

        [MessageHandler(typeof(CBloodCastleMove))]
        public async Task CBloodCastleMove(GSSession session, CBloodCastleMove message)
        {
            var plr = session.Player;
            var @char = session.Player.Character;

            var invisibleCloack = @char.Inventory.Get(message.ItemPos);
            var evbc = Program.EventManager.GetEvent<BloodCastles>();
            var itemLevel = evbc.GetEventNumber(plr);

            if(invisibleCloack.Plus != message.Bridge && invisibleCloack.Number != ItemNumber.FromTypeIndex(13,47))
            {
                await session.SendAsync(new SBloodCastleMove(1));
                return;
            }

            if(itemLevel != invisibleCloack.Plus)
            {
                await session.SendAsync(new SBloodCastleMove((byte)(itemLevel > invisibleCloack.Plus ? 4 : 3)));
                return;
            }

            if (!evbc.TryAdd(plr))
            {
                await session.SendAsync(new SBloodCastleMove(5));
                return;
            }

            await @char.Inventory.Delete(message.ItemPos);
        }

        [MessageHandler(typeof(CCrywolfBenefit))]
        public void CCrywolfBenefit(GSSession session)
        {
            session.SendAsync(new SCrywolfBenefit());
        }

        [MessageHandler(typeof(CDevilSquareMove))]
        public async Task CDevilSquareMove(GSSession session, CDevilSquareMove message)
        {
            var plr = session.Player;
            var @char = plr.Character;

            var itemPos = (byte)(message.InvitationItemPos - 12);
            var item = @char.Inventory.Get(itemPos);
            if (item.Plus != message.SquareNumber+1)
                return;

            var dsm = Program.EventManager.GetEvent<DevilSquares>();
            if (dsm.GetPlayerDS(plr) != message.SquareNumber+1)
                return;

            if (!dsm.TryAdd(plr))
                return;

            await @char.Inventory.Delete(itemPos);
        }

        [MessageHandler(typeof(CChaosCastleMove))]
        public async Task CChaosCastleMove(GSSession session, CChaosCastleMove message)
        {
            var plr = session.Player;
            var @char = plr.Character;

            var item = @char.Inventory.Get(message.InvitationItemPos);

            var dsm = Program.EventManager.GetEvent<ChaosCastles>();

            if (!dsm.TryAdd(plr))
                return;

            await @char.Inventory.Delete(item);
        }

        [MessageHandler(typeof(CKanturuStateInfo))]
        public void CKanturuStateInfo(GSSession session)
        {
            var kanturu = Program.EventManager.GetEvent<Kanturu>();
            kanturu.NPCTalk(session.Player);
        }
    }
}

﻿using MuEmu.Entity;
using MuEmu.Events.BloodCastle;
using MuEmu.Events.DevilSquare;
using MuEmu.Events.EventChips;
using MuEmu.Events.Kanturu;
using MuEmu.Monsters;
using MuEmu.Network.QuestSystem;
using MuEmu.Resources;
using MuEmu.Resources.Game;
using MuEmu.Resources.Map;
using MuEmu.Util;
using Serilog;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using WebZen.Handlers;
using WebZen.Network;
using WebZen.Util;

namespace MuEmu.Network.Game
{
    public class GameServices : MessageHandler
    {
        public static readonly ILogger Logger = Log.ForContext(Constants.SourceContextPropertyName, nameof(GameServices));

        [MessageHandler(typeof(CCheckSum))]
        public void CCheckSum(GSSession session, CCheckSum message)
        {
            //session.Player.CheckSum.IsValid(message.Key);
            Logger
                .ForAccount(session)
                .Debug("Key {0:X4}", message.Key);
        }

        [MessageHandler(typeof(CClientMessage))]
        public void CClientMessage(GSSession session, CClientMessage message)
        {
            Logger
                .ForAccount(session)
                .Information("Client Hack Check {0}", message.Flag);
        }

        [MessageHandler(typeof(CAction))]
        public void CAction(GSSession session, CAction message)
        {
            session.Player.Character.Direction = message.Dir;
            var ans = new SAction((ushort)session.ID, message.Dir, message.ActionNumber, message.Target);
            //session.SendAsync(ans).Wait();
            session.Player.SendV2Message(ans).Wait();
        }

        [MessageHandler(typeof(CMove))]
        public async Task CMove(GSSession session, CMove message)
        {
            var dirs = new List<Point>
            {
                new Point(-1,-1),
                new Point(0, -1),
                new Point(1, -1),

                new Point(1, 0),
                new Point(1, 1),
                new Point(0, 1),

                new Point(-1, 1),
                new Point(-1, 0)
            };

            var @char = session.Player.Character;
            var count = message.Path[0] & 0x0F;
            var Cpos = new Point(message.X, message.Y);

            var valid = true;
            byte ld = 0;

            for (int i = 1; i <= count; i++)
            {
                ld = (byte)((message.Path[(i + 1) / 2] >> (((i % 2) == 1) ? 4 : 0)) & 0x07);

                Cpos.Offset(dirs[ld]);
                var att = @char.Map.GetAttributes(Cpos);
                if (att.Where(y => y == MapAttributes.NoWalk || y == MapAttributes.Hide).Count() > 0)
                {
                    valid = false;
                    break;
                }
            }

            if (!valid)
            {
                var msgp = new SPositionSet((ushort)session.ID, @char.Position);
                await session.SendAsync(msgp);
                await session.Player.SendV2Message(msgp);
                Logger
                    .ForAccount(session)
                    .Error("Invalid path");
                return;
            }

            @char.Position = Cpos;

            var msg = new SMove((ushort)session.ID, (byte)Cpos.X, (byte)Cpos.Y, ld);
            await session.Player.SendV2Message(msg);
            session.Player.Character.TPosition = Cpos;
        }

        [MessageHandler(typeof(CPositionSet))]
        public async Task CPositionSet(GSSession session, CPositionSet message)
        {
            var pos = message.Position;
            Logger.ForAccount(session).Debug("Position set Recv {0}", pos);
            var @char = session.Player.Character;
            var msg = new SPositionSet((ushort)session.ID, pos);
            @char.Position = pos;
            @char.TPosition = pos;
            await session.SendAsync(msg);
            await @char.SendV2Message(msg);
        }

        #region Chat MessageHandlers
        // 0xC1 0x00
        [MessageHandler(typeof(CChatNickname))]
        public async Task CChatNickname(GSSession session, CChatNickname message)
        {
            if(!Program.Handler.ProcessCommands(session, message.Message.MakeString()))
            {
                await session.Player.SendV2Message(message);
                await session.SendAsync(message);
            }            
        }

        // 0xC1 0x01
        [MessageHandler(typeof(CChatNumber))]
        public async Task CChatNumber(GSSession session, CChatNumber message)
        {
            message.Number = (ushort)session.ID;
            await session.Player.SendV2Message(message);
            await session.SendAsync(message);
        }

        // 0xC1 0x02
        [MessageHandler(typeof(CChatWhisper))]
        public async Task CChatWhisper(GSSession session, CChatWhisper message)
        {
            var target = Program.server.Clients
                .Where(x => x.Player != null)
                .Where(x => x.Player.Character != null)
                .FirstOrDefault(x => x.Player.Character.Name.ToLower() == message.Id.ToLower());

            if(target == null)
            {
                await session.SendAsync(new SNotice(NoticeType.Blue, Program.ServerMessages.GetMessage(Resources.Game.Messages.Chat_Player_Offline, message.Id)));
                return;
            }

            await target.SendAsync(message);
        }
        #endregion

        [MessageHandler(typeof(CNewQuestInfo))]
        public void CNewQuestInfo(GSSession session, CNewQuestInfo message)
        {
            Logger
                .ForAccount(session)
                .Information("Quest S5 {0}", message.Quest);
            session.SendAsync(message);
        }

        [MessageHandler(typeof(CCloseWindow))]
        public static void CCloseWindow(GSSession session)
        {
            if (session.Player.Window == null)
                return;

            if(session.Player.Window.GetType() == typeof(Monster))
            {
                var mob = session.Player.Window as Monster;
                if (mob == null)
                    return;

                if(mob.Info.Monster == 229)
                    Marlon.RemRef();

                Logger.Debug("Player close window:{0}", mob.Info.Name);
            }else if(session.Player.Window.GetType() == typeof(Character))
            {

            }
            session.Player.Window = null;
        }

        [MessageHandler(typeof(CClientClose))]
        public async Task CClinetClose(GSSession session, CClientClose message)
        {
            Logger
                .ForAccount(session)
                .Information(Program.ServerMessages.GetMessage(Messages.Game_Close), message.Type);

            for(int i = 1; i <= 5; i++)
            {
                SubSystem.Instance.AddDelayedMessage(session.Player, TimeSpan.FromSeconds(5-i), new SNotice(NoticeType.Blue, Program.ServerMessages.GetMessage(Messages.Game_Close_Message, i)));
            }

            SubSystem.Instance.AddDelayedMessage(session.Player, TimeSpan.FromSeconds(5), new SCloseMsg { Type = message.Type });

            session.Player.Status = message.Type==ClientCloseType.SelectChar?LoginStatus.Logged:LoginStatus.NotLogged;

            using (var db = new GameContext())
            {
                await session.Player.Save(db);
                await db.SaveChangesAsync();
            }
        }

        [MessageHandler(typeof(CMoveItem))]
        public async Task CMoveItem(GSSession session, CMoveItem message)
        {
            Logger.Debug("Move item {0}:{1} to {2}:{3}", message.sFlag, message.Source, message.tFlag, message.Dest);

            if (session.Player.Character.Inventory.Move(message.sFlag, message.Source, message.tFlag, message.Dest))
            {
                await session.SendAsync(new SMoveItem
                {
                    ItemInfo = message.ItemInfo,
                    Position = message.Dest,
                    Result = (byte)message.tFlag
                });
            }
            else
            {
                await session.SendAsync(new SMoveItem
                {
                    ItemInfo = message.ItemInfo,
                    Position = message.Source,
                    Result = (byte)message.tFlag
                });
            }
        }

        [MessageHandler(typeof(CPointAdd))]
        public async Task CPointAdd(GSSession session, CPointAdd message)
        {
            var @char = session.Player.Character;
            var msg = new SPointAdd
            {
                Result = (byte)(0x10 | (byte)message.Type),
            };

            switch(message.Type)
            {
                case PointAdd.Strength:
                    if (@char.Strength + 1 <= short.MaxValue)
                    {
                        @char.LevelUpPoints--;
                        @char.Strength++;
                        msg.MaxStamina = (ushort)@char.MaxStamina;
                        msg.MaxShield = (ushort)@char.MaxShield;
                    }else
                    {
                        msg.Result = 0;
                    }
                    break;
                case PointAdd.Agility:
                    if (@char.Agility + 1 <= short.MaxValue)
                    {
                        @char.LevelUpPoints--;
                        @char.Agility++;
                        msg.MaxStamina = (ushort)@char.MaxStamina;
                        msg.MaxShield = (ushort)@char.MaxShield;
                    }
                    else
                    {
                        msg.Result = 0;
                    }
                    break;
                case PointAdd.Vitality:
                    if (@char.Vitality + 1 <= short.MaxValue)
                    {
                        @char.LevelUpPoints--;
                        @char.Vitality++;
                        msg.MaxLifeAndMana = (ushort)@char.MaxHealth;
                        msg.MaxStamina = (ushort)@char.MaxStamina;
                        msg.MaxShield = (ushort)@char.MaxShield;
                    }
                    else
                    {
                        msg.Result = 0;
                    }
                    break;
                case PointAdd.Energy:
                    if (@char.Energy + 1 <= short.MaxValue)
                    {
                        @char.LevelUpPoints--;
                        @char.Energy++;
                        msg.MaxLifeAndMana = (ushort)@char.MaxMana;
                        msg.MaxStamina = (ushort)@char.MaxStamina;
                        msg.MaxShield = (ushort)@char.MaxShield;
                    }
                    else
                    {
                        msg.Result = 0;
                    }
                    break;
                case PointAdd.Command:
                    if (@char.Command + 1 <= short.MaxValue)
                    {
                        @char.LevelUpPoints--;
                        @char.Command++;
                        msg.MaxStamina = (ushort)@char.MaxStamina;
                        msg.MaxShield = (ushort)@char.MaxShield;
                    }
                    else
                    {
                        msg.Result = 0;
                    }
                    break;
            }

            await session.SendAsync(msg);
        }

        // lacting
        [MessageHandler(typeof(CUseItem))]
        public async Task CUseItem(GSSession session, CUseItem message)
        {
            var @char = session.Player.Character;
            var inv = @char.Inventory;

            var Source = inv.Get(message.Source);

            Logger.Debug("CUseItem {0} {1} {2} {3}", message.Source, message.Dest, message.Type, Source);

            if(Source.BasicInfo.Skill != Spell.None)
            {
                if(await @char.Spells.TryAdd(Source.BasicInfo.Skill))
                {
                    await inv.Delete(message.Source);
                }
                @char.HPorSDChanged(RefillInfo.Update);
                return;
            }

            switch(Source.Number)
            {
                case 12 * 512 + 47:// Orb of Sword Slash
                    if(await @char.Spells.TryAdd(Spell.Slash))
                    {
                        await inv.Delete(message.Source);
                    }
                    @char.HPorSDChanged(RefillInfo.Update);
                    break;
                case 12 * 512 + 16:// Orb of Fire Slash
                    if (await @char.Spells.TryAdd(Spell.FireSlash))
                    {
                        await inv.Delete(message.Source);
                    }
                    @char.HPorSDChanged(RefillInfo.Update);
                    break;
                case 12 * 512 + 7:// Orb of Twisting Slash
                    if (await @char.Spells.TryAdd(Spell.TwistingSlash))
                    {
                        await inv.Delete(message.Source);
                    }
                    @char.HPorSDChanged(RefillInfo.Update);
                    break;
                case 12 * 512 + 8:// Orb of Healing
                    if (await @char.Spells.TryAdd(Spell.Heal))
                    {
                        await inv.Delete(message.Source);
                    }
                    @char.HPorSDChanged(RefillInfo.Update);
                    break;
                case 12 * 512 + 9:// Orb of Greater Defense
                    if (await @char.Spells.TryAdd(Spell.GreaterDefense))
                    {
                        await inv.Delete(message.Source);
                    }
                    @char.HPorSDChanged(RefillInfo.Update);
                    break;
                case 12 * 512 + 10:// Orb of Greater Damage
                    if (await @char.Spells.TryAdd(Spell.GreaterDamage))
                    {
                        await inv.Delete(message.Source);
                    }
                    @char.HPorSDChanged(RefillInfo.Update);
                    break;
                case 12 * 512 + 11:// Orb of Summoning
                    if (await @char.Spells.TryAdd(Spell.Summon))
                    {
                        await inv.Delete(message.Source);
                    }
                    @char.HPorSDChanged(RefillInfo.Update);
                    break;
                case 12 * 512 + 12:// Orb of Rageful Blow
                    if (await @char.Spells.TryAdd(Spell.RagefulBlow))
                    {
                        await inv.Delete(message.Source);
                    }
                    @char.HPorSDChanged(RefillInfo.Update);
                    break;
                case 12 * 512 + 13:// Orb of Impale
                    if (await @char.Spells.TryAdd(Spell.Impale))
                    {
                        await inv.Delete(message.Source);
                    }
                    @char.HPorSDChanged(RefillInfo.Update);
                    break;
                case 12 * 512 + 14:// Orb of Greater Fortitude
                    if (await @char.Spells.TryAdd(Spell.GreaterFortitude))
                    {
                        await inv.Delete(message.Source);
                    }
                    @char.HPorSDChanged(RefillInfo.Update);
                    break;
                case 14 * 512 + 0:// Apple
                case 14 * 512 + 1:// Small HP Potion
                case 14 * 512 + 2:// Medium HP Potion
                case 14 * 512 + 3:// Big HP Potion
                    var AddLife = (Source.SellPrice * 10) - (@char.Level * 2);
                    if (AddLife < 0)
                        AddLife = 0;

                    float AddLifeRate = ((Source.Number.Index+1) * 10.0f) + (Source.Plus * 5.0f);
                    AddLife += (long)(@char.MaxHealth * AddLifeRate / 100.0f);
                    if (Source.Durability == 1)
                        await inv.Delete(message.Source);
                    else
                        Source.Durability--;

                    @char.Health += AddLife;
                    break;
                case 14 * 512 + 4:// Small MP Potion
                case 14 * 512 + 5:// Medium MP Potion
                case 14 * 512 + 6:// Big MP Potion
                    var AddMana = (Source.SellPrice * 10) - @char.Level;
                    if (AddMana < 0)
                        AddMana = 0;

                    float AddManaRate = ((Source.Number.Index - 3) * 10.0f) + (Source.Plus * 5.0f);
                    AddMana += (uint)(@char.MaxMana * AddManaRate / 100.0f);
                    if (Source.Durability == 1)
                        await inv.Delete(message.Source);
                    else
                        Source.Durability--;

                    @char.Mana += AddMana;
                    break;
                case 14 * 512 + 8: // Antidote
                    if (Source.Durability == 1)
                        await inv.Delete(message.Source);
                    else
                        Source.Durability--;
                    await @char.Spells.ClearBuffByEffect(SkillStates.Ice);
                    await @char.Spells.ClearBuffByEffect(SkillStates.Poison);
                    break;
                case 14 * 512 + 46: // Haloween Scroll
                    if (Source.Durability == 1)
                        await inv.Delete(message.Source);
                    else
                        Source.Durability--;

                    @char.Spells.SetBuff(SkillStates.HAttackSpeed, TimeSpan.FromMilliseconds(1800));
                    break;
                case 14 * 512 + 47: // Haloween Scroll
                    if (Source.Durability == 1)
                        await inv.Delete(message.Source);
                    else
                        Source.Durability--;

                    @char.Spells.SetBuff(SkillStates.HAttackPower, TimeSpan.FromMilliseconds(1800));
                    break;
                case 14 * 512 + 48: // Haloween Scroll
                    if (Source.Durability == 1)
                        await inv.Delete(message.Source);
                    else
                        Source.Durability--;

                    @char.Spells.SetBuff(SkillStates.HDefensePower, TimeSpan.FromMilliseconds(1800));
                    break;
                case 14 * 512 + 49: // Haloween Scroll
                    if (Source.Durability == 1)
                        await inv.Delete(message.Source);
                    else
                        Source.Durability--;

                    @char.Spells.SetBuff(SkillStates.HMaxLife, TimeSpan.FromMilliseconds(1800));
                    break;
                case 14 * 512 + 50: // Haloween Scroll
                    if (Source.Durability == 1)
                        await inv.Delete(message.Source);
                    else
                        Source.Durability--;

                    @char.Spells.SetBuff(SkillStates.HMaxMana, TimeSpan.FromMilliseconds(1800));
                    break;
                case 14 * 512 + 10: // Town Portal Scroll
                    if (Source.Durability == 1)
                        await inv.Delete(message.Source);
                    else
                        Source.Durability--;

                    if (@char.MapID == Maps.Davias)
                    {
                        await @char.WarpTo(22);
                    }
                    else if (@char.MapID == Maps.Noria)
                    {
                        await @char.WarpTo(27);
                    }
                    else if (@char.MapID == Maps.LostTower)
                    {
                        await @char.WarpTo(42);
                    }
                    else if (@char.MapID == Maps.Atlans)
                    {
                        await @char.WarpTo(49);
                    }
                    else if (@char.MapID == Maps.Tarkan)
                    {
                        await @char.WarpTo(57);
                    }
                    else if (@char.MapID == Maps.BloodCastle1)
                    {
                        await @char.WarpTo(22);
                    }
                    else if (@char.MapID==Maps.ChaosCastle1)
                    {
                        await @char.WarpTo(22);
                    }
                    else if (@char.MapID==Maps.Kalima1)
                    {
                        await @char.WarpTo(22);
                    }
                    else if (@char.MapID == Maps.Aida)
                    {
                        await @char.WarpTo(27);
                    }
                    else if (@char.MapID == Maps.Crywolf)
                    {
                        await @char.WarpTo(27);
                    }
                    else
                    {
                        await @char.WarpTo(17);
                    }
                    break;
                case 14 * 512 + 9: // Ale
                    if (Source.Durability == 1)
                        await inv.Delete(message.Source);
                    else
                        Source.Durability--;
                    break;
                case 14 * 512 + 20: // Remedy Of Love
                    if (Source.Durability == 1)
                        await inv.Delete(message.Source);
                    else
                        Source.Durability--;

                    await session.SendAsync(new SItemUseSpecialTime { Number = 1, Time = 90 });
                    break;
                case 14 * 512 + 7: // Siege Potion
                    if (Source.Durability == 1)
                        await inv.Delete(message.Source);
                    else
                        Source.Durability--;

                    var SS = Source.Plus == 0 ? SkillStates.PotionBless : SkillStates.PotionSoul;
                    var time = TimeSpan.FromSeconds(Source.Plus == 0 ? 120 : 60);
                    @char.Spells.SetBuff(SS, time);
                    if(Source.Plus == 1)
                    {
                        await session.SendAsync(new SItemUseSpecialTime { Number = 2, Time = 60 });
                    }
                    break;
                case 14 * 512 + 63: // Fireworks
                    if (Source.Durability == 1)
                        await inv.Delete(message.Source);
                    else
                        Source.Durability--;

                    await session.SendAsync(new SCommand(ServerCommandType.Fireworks, (byte)@char.Position.X, (byte)@char.Position.Y));
                    break;
                case 14 * 512 + 35:// Small SD Potion
                case 14 * 512 + 36:// Medium SD Potion
                case 14 * 512 + 37:// Big SD Potion
                    float addSDRate = @char.MaxShield * (25.0f + (Source.Number.Index - 35) * 10.0f) / 100.0f;

                    @char.Shield += addSDRate;
                    if (Source.Durability == 1)
                        await inv.Delete(message.Source);
                    else
                        Source.Durability--;
                    await session.SendAsync(new SEffect((ushort)session.ID, ClientEffect.RecoverShield));
                    break;
                case 14 * 512 + 13: //  Jewel of Bless
                    {
                        var Target = inv.Get(message.Dest);
                        if (Target.Plus >= 7)
                            break;

                        await inv.Delete(message.Source);
                        Target.Plus++;
                    }
                    break;
                case 14 * 512 + 14: //  Jewel of Soul
                    {
                        var Target = inv.Get(message.Dest);
                        if (Target.Plus >= 9)
                            break;

                        await inv.Delete(message.Source);
                        var soulRate = 50 + (Target.Luck ? 25 : 0);
                        if (new Random().Next(100) < soulRate)
                        {
                            Target.Plus++;
                        }
                        else
                        {
                            if (Target.Plus > 7)
                                Target.Plus = 0;
                            else
                                Target.Plus--;
                        }
                    }
                    break;
                case 14 * 512 + 16: // Jewel of Life
                    {
                        var Target = inv.Get(message.Dest);
                        if (Target.Option28 >= 3)
                            break;

                        if (!Target.BasicInfo.Option)
                            break;

                        await inv.Delete(message.Source);
                        var lifeRate = 50 + (Target.Luck ? 25 : 0);
                        if (new Random().Next(100) < lifeRate)
                        {
                            Target.Option28++;
                        }
                        else
                        {
                            Target.Option28--;
                        }

                    }
                    break;
                case 13 * 512 + 66: //Invitation of the Santa Town's

                    break;
            }
        }

        [MessageHandler(typeof(CItemThrow))]
        public async Task CItemThrow(GSSession session, CItemThrow message)
        {
            var logger = Logger.ForAccount(session);
            var plr = session.Player;
            var inv = plr.Character.Inventory;
            var item = inv.Get(message.Source);
            await inv.Delete(message.Source);

            var date = plr.Character.Map.AddItem(message.MapX, message.MapY, item);
            await session.SendAsync(new SItemThrow { Source = message.Source, Result = 1 });
            logger.Information("Drop item {0} at {1},{2} in {3} deleted at {4}", item.Number, message.MapX, message.MapY, plr.Character.MapID, date);
        }

        [MessageHandler(typeof(CItemGet))]
        public async Task CItemGet(GSSession session, CItemGet message)
        {
            var @char = session.Player.Character;
            var item = (from obj in @char.Map.Items
                       where obj.Index == message.Number && obj.State == Resources.Map.ItemState.Created
                       select obj).FirstOrDefault();

            if(item == null)
            {
                Logger.ForAccount(session)
                    .Error("Item {0} no exist", message.Number);
                await session.SendAsync(new SItemGet { Result = 0xff });
                return;
            }
            
            if(item.Item.Number != ItemNumber.Zen)
            {
                var pos = @char.Inventory.Add(item.Item);
                if (pos == 0xff)
                {
                    await session.SendAsync(new SItemGet { Result = 0xff });
                    return;
                }
                await session.SendAsync(new SItemGet { ItemInfo = item.Item.GetBytes(), Result = pos });
            }
            else
            {
                session.Player.Character.Money += item.Item.BuyPrice;
            }

            item.State = ItemState.Deleted;
            var msg = new SViewPortItemDestroy { ViewPort = new Data.VPDestroyDto[] { new Data.VPDestroyDto(item.Index) } };
            await session.SendAsync(msg);
            await session.Player.SendV2Message(msg);
        }

        [MessageHandler(typeof(CEventEnterCount))]
        public async Task CEventEnterCount(GSSession session, CEventEnterCount message)
        {
            await session.SendAsync(new SEventEnterCount { Type = message.Type, Left = 10 });
        }


        [MessageHandler(typeof(CTalk))]
        public async Task CTalk(GSSession session, CTalk message)
        {
            var npcs = ResourceCache.Instance.GetNPCs();
            var ObjectIndex = message.Number.ShufleEnding();
            var obj = MonstersMng.Instance.GetMonster(ObjectIndex);
            var @char = session.Player.Character;
            if (npcs.TryGetValue(obj.Info.Monster, out var npc))
            {
                session.Player.Window = obj;
                switch (npc.Class)
                {
                    case NPCAttributeType.Shop:
                        if (npc.Data == 0xffff)
                            break;                        
                        await session.SendAsync(new STalk { Result = 0 });
                        await session.SendAsync(new SShopItemList(npc.Shop.Storage.GetInventory()) { ListType = 0 });
                        await session.SendAsync(new STax { Type = TaxType.Shop, Rate = 4 });                        
                        break;
                    case NPCAttributeType.Warehouse:
                        session.Player.Character.Inventory.Lock = true;
                        await session.SendAsync(new SNotice(NoticeType.Blue, Program.ServerMessages.GetMessage(Messages.Game_Vault_active, session.Player.Account.ActiveVault + 1)));
                        await session.SendAsync(new STalk { Result = NPCWindow.Warehouse });
                        await session.SendAsync(new SShopItemList(session.Player.Account.Vault.GetInventory()));
                        await session.SendAsync(new SWarehouseMoney(session.Player.Account.VaultMoney, session.Player.Character.Money));
                        break;
                    case NPCAttributeType.GuildMaster:
                        GuildManager.NPCTalk(session.Player, obj);
                        break;
                    case NPCAttributeType.EventChips:
                        EventChips.NPCTalk(session.Player);
                        break;
                    case NPCAttributeType.MessengerAngel:
                        Program.EventManager.GetEvent<BloodCastles>()
                            .MessengerAngelTalk(session.Player);
                        break;
                    case NPCAttributeType.KingAngel:
                        Program.EventManager.GetEvent<BloodCastles>()
                            .AngelKingTalk(session.Player);
                        break;
                    case NPCAttributeType.Kanturu:
                        Program.EventManager
                            .GetEvent<Kanturu>()
                            .NPCTalk(session.Player);
                        break;
                    case NPCAttributeType.DevilSquare:
                        Program.EventManager
                            .GetEvent<DevilSquares>()
                            .NPCTalk(session.Player);
                        break;
                    case NPCAttributeType.ServerDivision:
                        await session.SendAsync(new SCommand(ServerCommandType.ServerDivision));
                        break;
                    case NPCAttributeType.Window:
                        await session.SendAsync(new STalk { Result = (NPCWindow)npc.Data });

                        switch((NPCWindow)npc.Data)
                        {
                            case NPCWindow.ChaosMachine:
                                session.Player.Character.Inventory.Lock = true;
                                break;
                            case NPCWindow.GateKeeper:
                                DuelSystem.NPCTalk(session.Player);
                                break;
                        }
                        break;
                    case NPCAttributeType.Buff:
                        @char.Spells.SetBuff((SkillStates)npc.Data, TimeSpan.FromSeconds(120));
                        break;
                    case NPCAttributeType.Quest:
                        var quest = @char.Quests.Find(obj.Info.Monster);

                        if (quest == null)
                        {
                            await session.SendAsync(new SChatTarget(ObjectIndex, Program.ServerMessages.GetMessage(Messages.Game_NoQuestAvailable)));
                            return;
                        }

                        var details = quest.Details;
                        Logger.ForAccount(session)
                            .Information("Talk to QuestNPC: {0}, Found Quest:{1}, State:{2}", obj.Info.Name, details.Name, quest.State);
                        await session.SendAsync(new SSetQuest { Index = (byte)quest.Index, State = quest.StateByte });
                        break;
                }
            }
            else
            {
                Logger.ForAccount(session)
                    .Debug("Talk to unasigned NPC {0}", obj.Info.Monster);
                await session.SendAsync(new SChatTarget(ObjectIndex, Program.ServerMessages.GetMessage(Messages.Game_DefaultNPCMessage)));
            }
        }

        [MessageHandler(typeof(CBuy))]
        public async Task CBuy(GSSession session, CBuy message)
        {
            var plr = session.Player;
            var @char = plr.Character;

            var bResult = new SBuy
            {
                Result = 0xff,
                ItemInfo = Array.Empty<byte>(),
            };

            if (plr.Window == null)
            {
                await session.SendAsync(bResult);
                throw new ArgumentException("Player isn't in buy/trade/box/Quest", nameof(session.Player.Window));
            }

            if(plr.Window.GetType() != typeof(Monster))
            {
                await session.SendAsync(bResult);
                throw new ArgumentException("Player isn't in buy", nameof(session.Player.Window));
            }

            var npcs = ResourceCache.Instance.GetNPCs();
            var obj = plr.Window as Monster;
            if (npcs.TryGetValue(obj.Info.Monster, out var npc))
            {
                if (npc.Class != NPCAttributeType.Shop)
                {
                    await session.SendAsync(bResult);
                    throw new ArgumentException("Player isn't in buy", nameof(session.Player.Window));
                }

                var item = npc.Shop.Storage.Items[message.Position].Clone() as Item;
                bResult.ItemInfo = item.GetBytes();

                session.SendAsync(new SNotice(NoticeType.Blue, $"Item: {item.BasicInfo.Name} Price: {item.BuyPrice}zen")).Wait();

                if (item.BuyPrice > @char.Money)
                {
                    await session.SendAsync(bResult);
                    return;
                }

                bResult.Result = @char.Inventory.Add(item);
                if (bResult.Result == 0xff)
                {
                    await session.SendAsync(bResult);
                    return;
                }

                @char.Money -= item.BuyPrice;

                Logger
                    .ForAccount(session)
                    .Information("Buy {0} for {1}", item.ToString(), item.BuyPrice);

                await session.SendAsync(bResult);
            }
        }

        [MessageHandler(typeof(CSell))]
        public async Task CSell(GSSession session, CSell message)
        {
            var plr = session.Player;
            var result = new SSell { Result = 0, Money = session.Player.Character.Money };

            if (plr.Window == null)
            {
                await session.SendAsync(result);
                throw new ArgumentException("Player isn't in buy/trade/box", nameof(session.Player.Window));
            }

            if (plr.Window.GetType() != typeof(Monster))
            {
                await session.SendAsync(result);
                throw new ArgumentException("Player isn't in buy", nameof(session.Player.Window));
            }

            var npcs = ResourceCache.Instance.GetNPCs();
            var obj = plr.Window as Monster;
            if (npcs.TryGetValue(obj.Info.Monster, out var npc))
            {
                if(npc.Class == NPCAttributeType.Shop)
                {
                    result.Result = 1;
                    var inve = plr.Character.Inventory;
                    var item = inve.Get(message.Position);

                    plr.Character.Money += item.SellPrice;
                    result.Money = session.Player.Character.Money;
                    await inve.Delete(item, false);
                    session.SendAsync(new SNotice(NoticeType.Blue, $"Item: {item.BasicInfo.Name} Price: {item.SellPrice}zen")).Wait();
                }
            }
            await session.SendAsync(result);
        }

        #region Battle MessageHandlers
        [MessageHandler(typeof(CAttackS5E2))]
        public async Task CAttackS5E2(GSSession session, CAttackS5E2 message)
        {
            await CAttack(session, new CAttack { AttackAction = message.AttackAction, DirDis = message.DirDis, wzNumber = message.Number });
        }

        [MessageHandler(typeof(CAttack))]
        public async Task CAttack(GSSession session, CAttack message)
        {
            var targetId = message.Number;
            var Dir = message.DirDis & 0x0F;
            var Dis = (message.DirDis & 0xF0) >> 4;

            session.Player.Character.Direction = message.DirDis;

            if (message.Number >= MonstersMng.MonsterStartIndex) // Is Monster
            {
                try
                {
                    var monster = MonstersMng.Instance.GetMonster(targetId);

                    if (monster.Life <= 0)
                        return;

                    await session.Player.SendV2Message(new SAction((ushort)session.ID, message.DirDis, message.AttackAction, targetId));
                    if (monster.Type == ObjectType.NPC)
                    {
                        return;
                    }

                    DamageType type;
                    var attack = session.Player.Character.Attack(monster, out type);
                    await monster.GetAttacked(session.Player, attack, type);
                }
                catch(Exception ex)
                {
                    Logger.ForAccount(session)
                        .Error(ex, "Invalid monster #{0}", targetId);
                }
            }
            else
            {
                try
                {
                    var target = Program.server.Clients.First(x => x.ID == targetId);
                    if (target.Player.Character.Health <= 0.0f)
                        return;

                    /*await session.Player
                        .SendV2Message(new SAction((ushort)session.ID, message.DirDis, message.AttackAction, targetId));*/

                    var attack = session.Player.Character
                        .Attack(target.Player.Character, out DamageType type, out int reflect);

                    target.Player.Character
                        .GetAttacked((ushort)session.ID, message.DirDis, message.AttackAction, attack, type, Spell.None)
                        .Wait();

                    if (reflect != 0)
                    {
                        session.Player.Character
                            .GetAttacked((ushort)target.ID, target.Player.Character.Direction, 0, reflect, DamageType.Reflect, Spell.None)
                            .Wait();
                    }
                }
                catch(Exception ex)
                {
                    Logger.ForAccount(session)
                        .Error(ex, "Invalid player #{0}", targetId);
                }
            }
        }

        [MessageHandler(typeof(CMagicAttack))]
        public void CMagicAttack(GSSession session, CMagicAttack message)
        {
            var @char = session.Player.Character;
            var target = message.Target;
            MuEmu.Data.SpellInfo spell;
            Spells spells = null;
            Monster monster = null;
            Player player = null;
            int defense = 0;

            if (!@char.Spells.SpellDictionary.ContainsKey(message.MagicNumber))
            {
                Logger.Error("Invalid Magic, user don't own this spell {0}", message.MagicNumber);
                return;
            }

            spell = @char.Spells.SpellDictionary[message.MagicNumber];

            try
            {
                if (target >= MonstersMng.MonsterStartIndex) // Is Monster
                {
                    monster = MonstersMng.Instance.GetMonster(target);
                    spells = monster.Spells;
                    defense = monster.Defense;
                }
                else
                {
                    player = Program.server.Clients.First(x => x.ID == target).Player;
                    spells = player.Character.Spells;
                    defense = player.Character.Defense;
                }
            }catch(Exception)
            {
                Logger.Error("MagicAttack: Invalid target");
                return;
            }

            var mana = @char.Mana - spell.Mana;
            var bp = @char.Stamina;

            if (mana >= 0 && bp >= 0)
            {
                switch(spell.Number)
                {
                    case Spell.Poison:
                        @char.Spells.AttackSend(spell.Number, message.Target, false);
                        spells.SetBuff(SkillStates.Poison, TimeSpan.FromSeconds(60), @char);
                        break;
                    case Spell.Ice:
                        @char.Spells.AttackSend(spell.Number, message.Target, false);
                        spells.SetBuff(SkillStates.Ice, TimeSpan.FromSeconds(60), @char);
                        break;
                    case Spell.InfinityArrow:
                        if (@char.BaseClass != HeroClass.FaryElf)
                            return;
                        spells.SetBuff(SkillStates.InfinityArrow, TimeSpan.FromSeconds(1800), @char);
                        @char.Spells.AttackSend(spell.Number, message.Target, true);
                        break;
                    case Spell.Heal:
                        if (@char.BaseClass != HeroClass.FaryElf)
                            return;

                        var addLife = @char.EnergyTotal / 5;

                        if(spells.Character == null)
                        {
                            return;
                        }

                        spells.Character.Health += addLife;
                        @char.Spells.AttackSend(spell.Number, message.Target, true);
                        break;
                    case Spell.GreaterDefense:
                        if (@char.BaseClass != HeroClass.FaryElf)
                            return;

                        spells.SetBuff(SkillStates.Defense, TimeSpan.FromSeconds(1800), @char);
                        @char.Spells.AttackSend(spell.Number, message.Target, true);
                        break;
                    case Spell.GreaterDamage:
                        if (@char.BaseClass != HeroClass.FaryElf)
                            return;

                        spells.SetBuff(SkillStates.Attack, TimeSpan.FromSeconds(1800), @char);
                        @char.Spells.AttackSend(spell.Number, message.Target, true);
                        break;

                    case Spell.SoulBarrier:
                        spells.SetBuff(SkillStates.SoulBarrier, TimeSpan.FromSeconds(60 + @char.EnergyTotal / 40), @char);
                        @char.Spells.AttackSend(spell.Number, message.Target, true);
                        break;

                    case Spell.GreaterFortitude:
                        spells.SetBuff(SkillStates.SwellLife, TimeSpan.FromSeconds(60 + @char.EnergyTotal / 10), @char);
                        @char.Spells.AttackSend(spell.Number, message.Target, true);
                        break;
                    default:
                        @char.Spells.AttackSend(spell.Number, message.Target, false);
                        break;
                }

                @char.Mana = mana;
                DamageType type = DamageType.Regular;
                var attack = 0.0f;

                switch(spell.Number)
                {
                    case Spell.Falling_Slash:
                    case Spell.Lunge:
                    case Spell.Uppercut:
                    case Spell.Cyclone:
                    case Spell.Slash:
                    case Spell.TwistingSlash:
                    case Spell.RagefulBlow:
                    case Spell.DeathStab:
                    case Spell.CrescentMoonSlash:
                    case Spell.Impale:
                    case Spell.FireBreath:
                        attack = @char.SkillAttack(spell, defense, out type);
                        //else
                        //    @char.SkillAttack(spell, player, out type);
                        break;
                    case Spell.Heal:
                    case Spell.GreaterDamage:
                    case Spell.GreaterDefense:
                    case Spell.GreaterFortitude:
                    case Spell.SoulBarrier:
                    case Spell.Teleport:
                    case Spell.InfinityArrow:
                        return;
                    default:
                        if (spell.IsDamage == 0)
                            return;

                        if(@char.BaseClass == HeroClass.Summoner || @char.BaseClass == HeroClass.DarkWizard || @char.BaseClass == HeroClass.MagicGladiator)
                        {
                            attack = @char.MagicAttack(spell, defense, out type);
                        }
                        else
                        {
                            attack = @char.SkillAttack(spell, defense, out type);
                        }
                        break;
                }

                player?.Character.GetAttacked((ushort)@char.Player.Session.ID, @char.Direction, 0, (int)attack, type, spell.Number);
                monster?.GetAttackedDelayed(@char.Player, (int)attack, type, TimeSpan.FromMilliseconds(500));
            }
        }

        [MessageHandler(typeof(CMagicDuration))]
        public async Task CMagicDuration(GSSession session, CMagicDuration message)
        {
            var @char = session.Player.Character;

            if(!@char.Spells.SpellDictionary.ContainsKey(message.MagicNumber))
            {
                Logger.Error("Invalid Magic, user don't own this spell {0}", message.MagicNumber);
                return;
            }

            var magic = @char.Spells.SpellDictionary[message.MagicNumber];

            if (@char.Mana < magic.Mana || @char.Stamina < magic.BP)
                return;

            if((magic.Number == Spell.Triple_Shot||
                magic.Number == Spell.Penetration||
                magic.Number == Spell.IceArrow ||
                magic.Number == Spell.MultiShot))
            {
                if (@char.Inventory.Arrows == null)
                    return;

                if (!@char.Spells.BufActive(SkillStates.InfinityArrow))
                {
                    var durDown = magic.Number == Spell.Triple_Shot ? 3 : (magic.Number == Spell.MultiShot ? 5 : 0);
                    if (@char.Inventory.Arrows.Durability > durDown)
                        @char.Inventory.Arrows.Durability -= (byte)durDown;
                    else
                        @char.Inventory.Arrows.Durability--;

                    if (@char.Inventory.Arrows.Durability == 0)
                        await @char.Inventory.Delete(@char.Inventory.Arrows);
                }
            }

            @char.Mana -= magic.Mana;
            @char.Stamina -= magic.BP;

            var msgdef = new SMagicDuration(magic.Number, (ushort)session.ID, message.X, message.Y, message.Dir);
            await session.SendAsync(msgdef);
            await session.Player.SendV2Message(msgdef);

            var dir = (message.Dir & 0xF0) >> 4;
            var dis = (message.Dir & 0x0F);

            var dirs = new List<Point>
            {
                new Point(-1,-1),
                new Point(0, -1),
                new Point(1, -1),

                new Point(1, 0),
                new Point(1, 1),
                new Point(0, 1),

                new Point(-1, 1),
                new Point(-1, 0)
            };

            switch (message.MagicNumber)
            {
                case Spell.RagefulBlow:
                    {
                        var mp = new Point(message.X, message.Y);
                        var vp = @char.MonstersVP
                            .ToList() // clone for preveen collection changes
                            .Select(x => MonstersMng.Instance.GetMonster(x))
                            .Where(x => x.Position.Substract(mp).Length() <= 2.0 && x.Type == ObjectType.Monster);

                        foreach (var mob in vp)
                        {
                            DamageType type;
                            var attack = @char.SkillAttack(magic, mob.Defense, out type);
                            await mob.GetAttacked(@char.Player, attack, type);
                        }
                    }
                    break;
                case Spell.TwistingSlash:
                    {
                        var vp = @char.MonstersVP
                            .ToList() // clone for preveen collection changes
                            .Select(x => MonstersMng.Instance.GetMonster(x))
                            .Where(x => x.Position.Substract(@char.Position).Length() <= 2.0 && x.Type == ObjectType.Monster);

                        foreach (var mob in vp)
                        {
                            DamageType type;
                            var attack = @char.SkillAttack(magic, mob.Defense, out type);
                            await mob.GetAttacked(@char.Player, attack, type);
                        }
                    }
                    break;
                case Spell.Decay:
                    {
                        var mp = new Point(message.X, message.Y);
                        var vp = @char.MonstersVP
                            .ToList() // clone for preveen collection changes
                            .Select(x => MonstersMng.Instance.GetMonster(x))
                            .Where(x => x.Position.Substract(mp).Length() <= 2.0 && x.Type == ObjectType.Monster);
                        foreach (var mob in vp)
                        {
                            DamageType type;
                            var attack = @char.SkillAttack(magic, mob.Defense, out type);
                            await mob.GetAttacked(@char.Player, attack, type);
                            mob.Spells.SetBuff(SkillStates.Poison, TimeSpan.FromSeconds(60), @char);
                        }
                    }
                    break;
                case Spell.IceStorm:
                    {
                        var mp = new Point(message.X, message.Y);
                        var vp = @char.MonstersVP
                            .ToList() // clone for preveen collection changes
                            .Select(x => MonstersMng.Instance.GetMonster(x))
                            .Where(x => x.Position.Substract(mp).Length() <= 2.0 && x.Type == ObjectType.Monster);
                        foreach (var mob in vp)
                        {
                            DamageType type;
                            var attack = @char.SkillAttack(magic, mob.Defense, out type);
                            await mob.GetAttacked(@char.Player, attack, type);
                            mob.Spells.SetBuff(SkillStates.Ice, TimeSpan.FromSeconds(60), @char);
                        }
                    }
                    break;
            }
        }

        [MessageHandler(typeof(CBeattack))]
        public async Task CBeattack(GSSession session, CBeattack message)
        {
            var spell = ResourceCache.Instance.GetSkills()[message.MagicNumber];

            var mobList = from id in message.Beattack
                          select MonstersMng.Instance.GetMonster(id.Number);

            var mobInRange = from mob in mobList
                             where mob.MapID == session.Player.Character.MapID && spell.Distance >= mob.Position.Substract(message.Position).LengthSquared()
                             select mob;

            foreach(var mob in mobInRange)
            {
                DamageType dmgType;
                var dmg = session.Player.Character.MagicAttack(spell, mob.Defense, out dmgType);
                await mob.GetAttacked(session.Player, dmg, dmgType);
            }
        }
        #endregion

        [MessageHandler(typeof(CWarp))]
        public async Task CWarp(GSSession session, CWarp message)
        {
            if (session.Player.Character.Duel != null)
                return;

            var gates = ResourceCache.Instance.GetGates();

            var gate = (from g in gates
                        where /*g.Value.GateType != GateType.Exit &&*/ g.Value.Move == message.MoveNumber
                        select g.Value).FirstOrDefault();

            var log = Logger.ForAccount(session);

            if (gate == null)
            {
                log.Error("Invalid Gate {0}", message.MoveNumber);

                await session.SendAsync(new SNotice(NoticeType.Blue, "You can't go there"));
                return;
            }

            log.Information("Warp request to {0}", gate.Name);
            var @char = session.Player.Character;

            if(gate.ReqLevel > @char.Level)
            {
                await session.SendAsync(new SNotice(NoticeType.Blue, Program.ServerMessages.GetMessage(Messages.Game_Warp, gate.ReqLevel)));
                return;
            }

            if(gate.ReqZen > @char.Money)
            {
                await session.SendAsync(new SNotice(NoticeType.Blue, Program.ServerMessages.GetMessage(Messages.Game_Warp, gate.ReqZen)));
                return;
            }

            @char.Money -= gate.ReqZen;

            await @char.WarpTo(gate.Number);
        }

        [MessageHandler(typeof(CJewelMix))]
        public void CJewelMix(GSSession session, CJewelMix message)
        {
            var @char = session.Player.Character;
            var result = @char.Inventory.FindAll(new ItemNumber(14, (ushort)(13 + message.JewelType)));
            var neededJewels = new int[][] {
                new int[] { 10,  500000 },
                new int[] { 20, 1000000 },
                new int[] { 30, 1500000 } };

            if (message.JewelMix > 2)
            {
                Logger.ForAccount(session)
                    .Error("JewelMix out of bounds: {0}", message.JewelMix);
                session.SendAsync(new SJewelMix(0)).Wait();
                return;
            }
            
            if(result.Count() < neededJewels[message.JewelMix][0])
            {
                Logger.ForAccount(session)
                    .Error("JewelMix Insuficient Jewel count: {0} < {1}", result.Count(), neededJewels[message.JewelMix][0]);
                session.SendAsync(new SJewelMix(0)).Wait();
                return;
            }

            if(@char.Money < neededJewels[message.JewelMix][1])
            {
                Logger.ForAccount(session)
                    .Error("JewelMix Insuficient Money: {0} < {1}", @char.Money, neededJewels[message.JewelMix][1]);
                session.SendAsync(new SJewelMix(8)).Wait();
                return;
            }

            foreach (var i in result.Take(neededJewels[message.JewelMix][0]))
            {
                @char.Inventory.Delete(i).Wait();
            }

            @char.Inventory.Add(new Item(new ItemNumber(12, (ushort)(30 + message.JewelType)), 0, new { Plus = message.JewelMix }));
            @char.Inventory.SendInventory();
            session.SendAsync(new SJewelMix(1)).Wait();
        }

        [MessageHandler(typeof(CJewelUnMix))]
        public void CJewelUnMix(GSSession session, CJewelUnMix message)
        {
            var @char = session.Player.Character;
            var target = @char.Inventory.Get(message.JewelPos);
            var neededJewels = new int[][] {
                new int[] { 10,  500000 },
                new int[] { 20, 1000000 },
                new int[] { 30, 1500000 } };

            if (target == null)
            {
                Logger.ForAccount(session)
                    .Error("Item not found: {0}", message.JewelPos);
                session.SendAsync(new SJewelMix(4)).Wait();
                return;
            }

            if(target.Plus != message.JewelLevel)
            {
                Logger.ForAccount(session)
                    .Error("Item level no match: {0} != {1}", message.JewelLevel, target.Plus);
                session.SendAsync(new SJewelMix(3)).Wait();
                return;
            }

            if(@char.Money < 1000000)
            {
                Logger.ForAccount(session)
                    .Error("Insuficient money: {0} < 1000000", @char.Money);
                session.SendAsync(new SJewelMix(8)).Wait();
                return;
            }

            for(var i = 0; i < neededJewels[message.JewelLevel][0]; i++)
            {
                @char.Inventory.Add(new Item(new ItemNumber(14, (ushort)(13 + message.JewelType)), 0));
            }

            @char.Inventory.Delete(message.JewelPos).Wait();
            @char.Inventory.SendInventory();
            session.SendAsync(new SJewelMix(7)).Wait();
        }

        [MessageHandler(typeof(CChaosBoxItemMixButtonClick))]
        public async Task CChaosBoxItemMixButtonClick(GSSession session)
        {
            var @char = session.Player.Character;
            var cbMix = @char.Inventory.ChaosBox;
            
            var mixInfo = ResourceCache.Instance.GetChaosMixInfo();
            var mixMatched = mixInfo.FindMix(@char);

            if (mixMatched == null)
            {
                Logger.ForAccount(session)
                    .Error("Invalid MIX");

                await session.SendAsync(new SChaosBoxItemMixButtonClick { Result = ChaosBoxMixResult.Fail });
                return;
            }

            Logger.ForAccount(session)
                .Information("Mix found, match: {0}", mixMatched.Name);

            if (!@char.Inventory.TryAdd())
            {
                await session.SendAsync(new SNotice(NoticeType.Blue, Program.ServerMessages.GetMessage(Messages.Game_ChaosBoxMixError)));
                await session.SendAsync(new SChaosBoxItemMixButtonClick { Result = ChaosBoxMixResult.Fail });
                return;
            }

            var result = mixMatched.Execute(@char);

            if(result == ChaosBoxMixResult.Fail)
            {
                await session.SendAsync(new SShopItemList(cbMix.GetInventory()) { ListType = 3 });
            }

            await session.SendAsync(new SChaosBoxItemMixButtonClick { Result = result, ItemInfo = cbMix.Items.Values.FirstOrDefault()?.GetBytes()??Array.Empty<byte>() });
            Logger.ForAccount(session)
                .Information("Mix Result: {0} : {1}", mixMatched.Name, result);
        }

        [MessageHandler(typeof(CChaosBoxUseEnd))]
        public void CChaosBoxUseEnd(GSSession session)
        {
            session.Player.Character.Inventory.Lock = false;
        }

        [MessageHandler(typeof(CWarehouseUseEnd))]
        public void CWarehouseUseEnd(GSSession session)
        {
            session.Player.Character.Inventory.Lock = false;
        }

        [MessageHandler(typeof(CItemModify))]
        public async Task CItemModify(GSSession session, CItemModify message)
        {
            if(message.Position == 0xff)
            {
                return;
            }

            var it = session.Player.Character.Inventory.Get(message.Position);
            var res = new SItemModify
            {
                Money = 0,
            };

            var cost = it.RepairPrice;

            if(cost <= 0 || cost > session.Player.Character.Money)
            {
                session.SendAsync(res).Wait();
                return;
            }

            await session.SendAsync(new SNotice(NoticeType.Blue, $"Item:{it.BasicInfo.Name} Repair:{it.RepairPrice}zen"));

            it.Durability = it.BasicInfo.Durability;
            session.Player.Character.Money -= (uint)cost;
            res.Money = (int)session.Player.Character.Money;
            await session.SendAsync(res);
        }

        [MessageHandler(typeof(CTeleport))]
        public async Task CTeleport(GSSession session, CTeleport message)
        {
            var log = Logger.ForAccount(session);
            var @char = session.Player.Character;

            var gates = ResourceCache.Instance.GetGates();

            if (message.MoveNumber != 0)
            {
                var gate = gates[message.MoveNumber];

                if (gate == null)
                {
                    log.Error("Invalid source gate {0}", message.MoveNumber);
                    await @char.WarpTo(@char.MapID, @char.Position, @char.Direction);
                    return;
                }

                log.Information("Warp request to {1}:{0}", message.MoveNumber, gate.Map);
                await @char.WarpTo(message.MoveNumber);
            }
            else
            {
                var spell = ResourceCache.Instance.GetSkills()[Spell.Teleport];

                if (spell.Mana < @char.Mana && spell.BP < @char.Stamina)
                {
                    //@char.Position = new Point(message.X, message.Y);
                    var msg = new SMagicAttack(Spell.Teleport, (ushort)session.ID, (ushort)session.ID);
                    await session.SendAsync(msg);
                    await @char.SendV2Message(msg);

                    @char.Mana -= spell.Mana;
                    @char.Stamina -= spell.BP;

                    @char.PlayersVP.Clear();
                    @char.MonstersVP.Clear();
                    @char.ItemsVP.Clear();
                    @char.TeleportTo(new Point(message.X, message.Y));
                }

                
                //await @char.WarpTo(@char.MapID, @char.Position, @char.Direction);
                //@char.TeleportTo(@char.Position);
            }
        }



        #region Party MessageHandlers
        [MessageHandler(typeof(CPartyRequest))]
        public async Task CPartyRequest(GSSession session, CPartyRequest message)
        {
            var trg = Program.server.Clients.FirstOrDefault(x => x.ID == message.Number);
            if(trg == null)
            {
                await session.SendAsync(new SPartyResult(PartyResults.PlayerOffline));
                return;
            }

            if(trg.Player.Character.Party != null)
            {
                await session.SendAsync(new SPartyResult(PartyResults.InAnotherParty));
                return;
            }

            var party = session.Player.Character.Party;

            if((party != null && party.Master != session.Player) || session.Player.Window != null || trg.Player.Window != null)
            {
                await session.SendAsync(new SPartyResult(PartyResults.Fail));
                return;
            }

            if(Math.Abs(session.Player.Character.Level - trg.Player.Character.Level) > PartyManager.MaxLevelDiff)
            {
                await session.SendAsync(new SPartyResult(PartyResults.RestrictedLevel));
                return;
            }

            message.Number = (ushort)session.ID;
            await trg.SendAsync(message);
        }

        [MessageHandler(typeof(CPartyRequestResult))]
        public async Task CPartyRequestResult(GSSession session, CPartyRequestResult message)
        {
            var trg = Program.server.Clients.FirstOrDefault(x => x.ID == message.Number);
            if(trg == null)
            {
                await session.SendAsync(new SPartyResult(PartyResults.Fail));
                return;
            }
            PartyManager.CreateLink(trg.Player, session.Player);
        }

        [MessageHandler(typeof(CPartyList))]
        public async Task CPartyList(GSSession session)
        {
            var partyInfo = new SPartyList();
            var party = session.Player.Character.Party;

            if (party == null)
            {
                await session.SendAsync(partyInfo);
                return;
            }

            partyInfo.Result = PartyResults.Success;
            partyInfo.PartyMembers = party.List();
            await session.SendAsync(partyInfo);
        }

        [MessageHandler(typeof(CPartyDelUser))]
        public void CPartyDelUser(GSSession session, CPartyDelUser message)
        {
            var party = session.Player.Character.Party;
            if(party == null)
            {
                return;
            }

            var memb = party.Members.ElementAtOrDefault(message.Index);
            if(memb == null)
            {
                return;
            }

            if(memb != party.Master && memb != session.Player)
            {
                return;
            }

            PartyManager.Remove(memb);
        }
        #endregion

        #region Duel MessageHandlers
        [MessageHandler(typeof(CDuelRequest))]
        public void CDuelRequest(GSSession session, CDuelRequest message)
        {
            var targetId = message.wzNumber.ShufleEnding();
            var target = Program.server.Clients.FirstOrDefault(x => x.ID == targetId);

            if (session.Player.Character.Duel != null)
            {
                session.SendAsync(new SDuelAnsDuelInvite(DuelResults.AlreadyDuelling, 0, "")).Wait();
                return;
            }
            if(target.Player.Character.Duel != null)
            {
                session.SendAsync(new SDuelAnsDuelInvite(DuelResults.AlreadyDuelling1, 0, "")).Wait();
                return;
            }

            if(!DuelSystem.CreateDuel(session.Player, target.Player))
            {
                session.SendAsync(new SDuelAnsDuelInvite(DuelResults.DuelMax, 0, "")).Wait();
                return;
            }
            target.SendAsync(new SDuelAnswerReq((ushort)session.ID, session.Player.Character.Name)).Wait();
            //session.SendAsync(new SDuelAnsDuelInvite(DuelResults.NoError, 0, "")).Wait();
        }

        [MessageHandler(typeof(CDuelAnswer))]
        public void CDuelAnswer(GSSession session, CDuelAnswer message)
        {
            if(message.DuelOK == 0)
            {
                session.SendAsync(new SDuelAnsDuelInvite(DuelResults.RefuseInvitated, (ushort)session.ID, session.Player.Character.Name)).Wait();
                return;
            }

            session.Player.Character.Duel.Join();
        }

        [MessageHandler(typeof(CDuelLeave))]
        public void CDuelLeave(GSSession session)
        {
            try
            {
                session.Player.Character.Duel.Leave(session.Player);
            }catch(Exception)
            {
                session.SendAsync(new SDuelAnsExit(DuelResults.Failed)).Wait();
            }
        }

        [MessageHandler(typeof(CDuelJoinRoom))]
        public void CDuelJoinRoom(GSSession session, CDuelJoinRoom message)
        {
            var msg = new SDuelRoomJoin();
            msg.Results = DuelSystem.TryJoinRoom(session.Player, message.Room);
            session.SendAsync(msg).Wait();
        }

        [MessageHandler(typeof(CDuelLeaveRoom))]
        public void CDuelLeaveRoom(GSSession session, CDuelLeaveRoom message)
        {
            var msg = new SDuelRoomLeave();
            msg.Results = DuelSystem.LeaveRoom(session.Player, message.Room);
            session.SendAsync(msg).Wait();
        }
        #endregion

        #region PersonalShop MessageHandlers
        [MessageHandler(typeof(CPShopSetItemPrice))]
        public async Task CPShopSetItemPrice(GSSession session, CPShopSetItemPrice message)
        {
            var @char = session.Player.Character;
            if(@char.Level < 6)
            {
                await session.SendAsync(new SPShopSetItemPrice(PShopResult.LevelTooLow, message.Position));
                return;
            }

            if(message.Price == 0)
            {
                await session.SendAsync(new SPShopSetItemPrice(PShopResult.InvalidPrice, message.Position));
                return;
            }

            var item = @char.Inventory.ItemMoved;

            if(item == null)
            {
                await session.SendAsync(new SPShopSetItemPrice(PShopResult.InvalidItem, message.Position));
                return;
            }

            item.PShopValue = message.Price;

            await session.SendAsync(new SPShopSetItemPrice(PShopResult.Success, message.Position));
        }

        [MessageHandler(typeof(CPShopRequestOpen))]
        public async Task CPShopRequestOpen(GSSession session, CPShopRequestOpen message)
        {
            var @char = session.Player.Character;

            if(@char.Map.IsEvent)
            {
                await session.SendAsync(new SPShopRequestOpen(PShopResult.Disabled));
                return;
            }

            if (@char.Level < 6)
            {
                await session.SendAsync(new SPShopRequestOpen(PShopResult.LevelTooLow));
                return;
            }

            if (!@char.Shop.Open)
            {
                @char.Shop.Name = message.Name;
                @char.Shop.Open = true;
            }else
            {
                
            }

            await session.SendAsync(new SPShopRequestOpen(PShopResult.Success));
        }

        [MessageHandler(typeof(CPShopRequestClose))]
        public async Task CPShopRequestClose(GSSession session)
        {
            var @char = session.Player.Character;
            if (!@char.Shop.Open)
            {
                return;
            }

            @char.Shop.Open = false;
            var msg = new SPShopRequestClose(PShopResult.Success, (ushort)session.ID);
            await session.SendAsync(msg);
            await @char.SendV2Message(msg);
        }

        [MessageHandler(typeof(CPShopRequestList))]
        public async Task CPShopRequestList(GSSession session, CPShopRequestList message)
        {
            var seller = Program.server.Clients.FirstOrDefault(x => x.ID == message.Number);

            if(seller == null)
            {
                await session.SendAsync(new SPShopRequestList(PShopResult.InvalidPosition));
                return;
            }

            if(seller == session)
            {
                await session.SendAsync(new SPShopRequestList(PShopResult.Disabled));
                return;
            }

            await session.SendAsync(new SPShopRequestList(PShopResult.Success, message.Number, seller.Player.Character.Name, seller.Player.Character.Shop.Name, seller.Player.Character.Shop.Items));
            return;
        }

        [MessageHandler(typeof(CPShopRequestBuy))]
        public async Task CPShopRequestBuy(GSSession session, CPShopRequestBuy message)
        {
            var seller = Program.server.Clients.FirstOrDefault(x => x.ID == message.Number);

            if (seller == null)
            {
                await session.SendAsync(new SPShopRequestBuy(PShopResult.InvalidPosition));
                return;
            }

            if (seller == session)
            {
                await session.SendAsync(new SPShopRequestBuy(PShopResult.Disabled));
                return;
            }

            var @char = seller.Player.Character;
            if(!@char.Shop.Open)
            {
                await session.SendAsync(new SPShopRequestBuy(PShopResult.Disabled));
                return;
            }

            var item = @char.Inventory.PersonalShop.Get(message.Position);

            if (item == null)
            {
                await session.SendAsync(new SPShopRequestBuy(PShopResult.InvalidItem));
                return;
            }

            if(item.PShopValue < session.Player.Character.Money)
            {
                await session.SendAsync(new SPShopRequestBuy(PShopResult.LackOfZen));
                return;
            }

            if(@char.Money+item.PShopValue>uint.MaxValue)
            {
                await session.SendAsync(new SPShopRequestBuy(PShopResult.ExceedingZen));
                return;
            }

            @char.Money += item.PShopValue;
            session.Player.Character.Money -= item.PShopValue;
            @char.Inventory.Remove(message.Position);
            session.Player.Character.Inventory.Add(item);

            await session.SendAsync(new SPShopRequestBuy(PShopResult.Success, message.Number, item.GetBytes()));
            await @char.Player.Session.SendAsync(new SPShopRequestSold(PShopResult.Success, (ushort)session.ID, session.Player.Character.Name));
        }

        #endregion
    }
}

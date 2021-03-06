﻿using BlubLib.Serialization;
using MuEmu.Network;
using MuEmu.Network.Auth;
using MuEmu.Network.CashShop;
using MuEmu.Network.ConnectServer;
using MuEmu.Network.Game;
using MuEmu.Network.Global;
using MuEmu.Resources;
using Serilog;
using Serilog.Formatting.Json;
using System;
using System.IO;
using System.Net;
using WebZen.Handlers;
using WebZen.Network;
using WebZen.Serialization;
using MuEmu.Resources.XML;
using MuEmu.Monsters;
using MuEmu.Network.Event;
using WebZen.Util;
using MuEmu.Network.QuestSystem;
using MuEmu.Events.LuckyCoins;
using MuEmu.Events.EventChips;
using MuEmu.Events.BloodCastle;
using System.Threading.Tasks;
using MuEmu.Entity;
using System.Linq;
using MuEmu.Network.Guild;
using MuEmu.Network.AntiHack;
using MuEmu.Events.DevilSquare;
using MuEmu.Events;
using MuEmu.Util;
using MuEmu.Events.Kanturu;
using MuEmu.Events.ChaosCastle;
using MuEmu.Resources.BMD;
using Serilog.Sinks.RollingFile;
using MuEmu.Resources.Game;

namespace MuEmu
{
    class Program
    {
        public static CommandHandler<GSSession> Handler { get; } = new CommandHandler<GSSession>();
        public static WZGameServer server;
        public static CSClient client;
        public static string ConnectionString { get; set; }
        public static bool AutoRegistre { get; set; }
        public static ushort ServerCode { get; set; }
        public static float Experience { get; set; }
        public static float Zen { get; set; }
        public static int DropRate { get; set; }
        public static bool Season12 { get; set; }

        public static EventManagement EventManager;

        public static ServerMessages ServerMessages { get; private set; }
        static void Main(string[] args)
        {
            Predicate<GSSession> MustNotBeLoggedIn = session => session.Player.Status == LoginStatus.NotLogged;
            Predicate<GSSession> MustBeLoggedIn = session => session.Player.Status == LoginStatus.Logged;
            Predicate<GSSession> MustBePlaying = session => session.Player.Status == LoginStatus.Playing;
            Predicate<GSSession> MustBeLoggedOrPlaying = session => session.Player.Status == LoginStatus.Logged || session.Player.Status == LoginStatus.Playing;

            string output = "{Timestamp: HH:mm:ss} [{Level} {SourceContext}][{AID}:{AUser}] {Message}{NewLine}{Exception}";

            Log.Logger = new LoggerConfiguration()
                .Destructure.ByTransforming<IPEndPoint>(endPoint => endPoint.ToString())
                .Destructure.ByTransforming<EndPoint>(endPoint => endPoint.ToString())
                .WriteTo.RollingFile("GameServer_{Date}.txt", outputTemplate: output)
                .WriteTo.Console(outputTemplate: output)
                .MinimumLevel.Debug()
                .CreateLogger();

            ServerMessages = new ServerMessages();
            ServerMessages.LoadMessages("./Data/Lang/ServerMessages(es).xml");

            if (!File.Exists("./Server.xml"))
            {
                Log.Logger.Error(ServerMessages.GetMessage(Messages.Server_Cfg));
                ResourceLoader.XmlSaver("./Server.xml", new ServerInfoDto
                {
                    AutoRegistre = true,
                    Code = 0,
                    ConnectServerIP = "127.0.0.1",
                    DataBase = "MuOnline",
                    DBIp = "127.0.0.1",
                    BDUser = "root",
                    DBPassword = "",
                    DropRate = 60,
                    Experience = 10,
                    IP = "127.0.0.1",
                    Name = "GameServer",
                    Port = 55901,
                    Serial = "Serial",
                    Show = 1,
                    Version = "10203",
                    Zen = 10,
                });
                Task.Delay(10000);
                return;
            }

            var xml = ResourceLoader.XmlLoader<ServerInfoDto>("./Server.xml");
            ServerMessages.LoadMessages($"./Data/Lang/ServerMessages({xml.Lang}).xml");

            Console.Title = ServerMessages.GetMessage(Messages.Server_Title, xml.Code, xml.Name, xml.Version, xml.Serial, xml.DataBase);

            ConnectionString = $"Server={xml.DBIp};port=3306;Database={xml.DataBase};user={xml.BDUser};password={xml.DBPassword};Convert Zero Datetime=True;";
            
            SimpleModulus.LoadDecryptionKey("./Data/Dec1.dat");
            SimpleModulus.LoadEncryptionKey("./Data/Enc2.dat");
            byte[] key = { 0x44, 0x9D, 0x0F, 0xD0, 0x37, 0x22, 0x8F, 0xCB, 0xED, 0x0D, 0x37, 0x04, 0xDE, 0x78, 0x00, 0xE4, 0x33, 0x86, 0x20, 0xC2, 0x79, 0x35, 0x92, 0x26, 0xD4, 0x37, 0x37, 0x30, 0x98, 0xEF, 0xA4, 0xDE };
            PacketEncrypt.Initialize(key);

            var ip = new IPEndPoint(IPAddress.Parse(xml.IP), xml.Port);
            var csIP = new IPEndPoint(IPAddress.Parse(xml.ConnectServerIP), 44405);
            AutoRegistre = xml.AutoRegistre;
            ServerCode = (ushort)xml.Code;
            Experience = xml.Experience;
            Zen = xml.Zen;
            DropRate = xml.DropRate;
            Season12 = xml.Rijndael;

            var mh = new MessageHandler[] {
                new FilteredMessageHandler<GSSession>()
                    .AddHandler(new AuthServices())
                    .AddHandler(new GlobalServices())
                    .AddHandler(new GameServices())
                    .AddHandler(new CashShopServices())
                    .AddHandler(new EventServices())
                    .AddHandler(new QuestSystemServices())
                    .AddHandler(new GuildServices())
                    .AddHandler(new AntiHackServices())
                    .RegisterRule<CIDAndPass>(MustNotBeLoggedIn)
                    .RegisterRule<CCharacterList>(MustBeLoggedIn)
                    .RegisterRule<CCharacterMapJoin>(MustBeLoggedIn)
                    .RegisterRule<CCharacterMapJoin2>(MustBeLoggedIn)
                    .RegisterRule<CCloseWindow>(MustBePlaying)
                    .RegisterRule<CDataLoadOK>(MustBePlaying)
                    .RegisterRule<CAction>(MustBePlaying)
            };

            

            var mf = new MessageFactory[]
            {
                new AuthMessageFactory(),
                new GlobalMessageFactory(),
                new GameMessageFactory(),
                new CashShopMessageFactory(),
                new EventMessageFactory(),
                new QuestSystemMessageFactory(),
                new GuildMessageFactory(),
                new AntiHackMessageFactory(),
            };
            server = new WZGameServer(ip, mh, mf, xml.Rijndael);
            server.ClientVersion = xml.Version;
            server.ClientSerial = xml.Serial;

            var cmh = new MessageHandler[]
            {
                new FilteredMessageHandler<CSClient>()
                .AddHandler(new CSServices())
            };

            var cmf = new MessageFactory[]
            {
                new CSMessageFactory()
            };

            try
            {
                ResourceCache.Initialize(".\\Data");
                MonstersMng.Initialize();
                MonstersMng.Instance.LoadMonster("./Data/Monsters/Monster.txt");
                EventInitialize();

                MonstersMng.Instance.LoadSetBase("./Data/Monsters/MonsterSetBase.txt");
                GuildManager.Initialize();
                PartyManager.Initialzie(400);
                DuelSystem.Initialize();
                SubSystem.Initialize();
                Marlon.Initialize();
            }catch(MySql.Data.MySqlClient.MySqlException ex)
            {
                Log.Error(ex, ServerMessages.GetMessage(Messages.Server_MySQL_Error));
                //Migrate(null, new EventArgs());
                //Log.Information("Server needs restart to reload all changes");
                Task.Delay(10000);
            }
            catch (Exception ex)
            {
                Log.Error(ex, ServerMessages.GetMessage(Messages.Server_Error));
            }

            try
            {
                client = new CSClient(csIP, cmh, cmf, (ushort)xml.Code, server, (byte)xml.Show);
            }catch(Exception)
            {
                Log.Error(ServerMessages.GetMessage(Messages.Server_CSServer_Error));
            }

            Log.Information(ServerMessages.GetMessage(Messages.Server_Disconnecting_Accounts));
            try
            {
                using (var db = new GameContext())
                {
                    var accs = from acc in db.Accounts
                               where acc.IsConnected && acc.ServerCode == xml.Code
                               select acc;

                    foreach (var acc in accs)
                        acc.IsConnected = false;

                    db.Accounts.UpdateRange(accs);
                    db.SaveChanges();
                }
            }catch(Exception)
            {
                Log.Error("MySQL unavailable.");
                Task.Delay(15000);
                return;
            }

            Log.Information(ServerMessages.GetMessage(Messages.Server_Ready));

            Handler.AddCommand(new Command<GSSession>("exit", Close))
                .AddCommand(new Command<GSSession>("quit", Close))
                .AddCommand(new Command<GSSession>("stop", Close))
                .AddCommand(new Command<GSSession>("reload")
                    .AddCommand(new Command<GSSession>("shops", (object a, CommandEventArgs b) => ResourceCache.Instance.ReloadShops()))
                    .AddCommand(new Command<GSSession>("gates", (object a, CommandEventArgs b) => ResourceCache.Instance.ReloadGates())))
                .AddCommand(new Command<GSSession>("create")
                    .AddCommand(new Command<GSSession>("movereq", DumpMoveReq)))
                .AddCommand(new Command<GSSession>("db")
                    .AddCommand(new Command<GSSession>("migrate", Migrate))
                    .AddCommand(new Command<GSSession>("create", Create))
                    .AddCommand(new Command<GSSession>("delete", Delete)))
                .AddCommand(new Command<GSSession>("!", (object a, CommandEventArgs b) => GlobalAnoucement(b.Argument)).SetPartial())
                .AddCommand(new Command<GSSession>("/").SetPartial()
                    .AddCommand(new Command<GSSession>("add").SetPartial()
                        .AddCommand(new Command<GSSession>("str", Character.AddStr))
                        .AddCommand(new Command<GSSession>("agi", Character.AddAgi))
                        .AddCommand(new Command<GSSession>("vit", Character.AddVit))
                        .AddCommand(new Command<GSSession>("ene", Character.AddEne))
                        .AddCommand(new Command<GSSession>("cmd", Character.AddCmd)))
                    .AddCommand(new Command<GSSession>("set")
                        .AddCommand(new Command<GSSession>("hp", (object a, CommandEventArgs b) => ((GSSession)a).Player.Character.Health = float.Parse(b.Argument)))
                        .AddCommand(new Command<GSSession>("zen", (object a, CommandEventArgs b) => ((GSSession)a).Player.Character.Money = uint.Parse(b.Argument)))
                        .AddCommand(new Command<GSSession>("exp", (object a, CommandEventArgs b) => ((GSSession)a).Player.Character.Experience = uint.Parse(b.Argument))))
                    .AddCommand(new Command<GSSession>("levelup", LevelUp))
                    /*.AddCommand(new Command<GSSession>("post"))*/)
                //.AddCommand(new Command<GSSession>("~").SetPartial())
                /*.AddCommand(new Command<GSSession>("]").SetPartial())*/;

            while (true)
            {
                var input = Console.ReadLine();
                if (input == null)
                    break;

                Handler.ProcessCommands(null, input);
            }
        }

        private static void MakeXOR(byte[] data, int offset, int length)
        {
            var xor = new byte[] { 0xFC, 0xCF, 0xAB };
            for(var i = 0; i < data.Length- offset; i++)
            {
                data[i+ offset] ^= xor[(i % length) % xor.Length];
            }
        }

        private static void DumpMoveReq(object sender, CommandEventArgs e)
        {
            var gates = ResourceCache.Instance.GetGates();
            var moves = gates
                .Where(x => x.Value.Move != -1)
                .OrderBy(x => x.Value.Move)
                .Select(x => x.Value);

            using(var fs = new FileStream("./MoveReq.bmd", FileMode.Create))
            {
                var num = BitConverter.GetBytes(moves.Count());
                fs.Write(num, 0, 4);

                var data = new byte[0xFA0];
                using (var ms = new MemoryStream(data))
                {
                    foreach (var mov in moves)
                    {
                        var bmdData = new MoveReqBMD
                        {
                            Gate = mov.Number,
                            Level = mov.ReqLevel,
                            MoveNumber = mov.Move,
                            Zen = mov.ReqZen,
                            ClientName = mov.Name,
                            ServerName = mov.Name,
                        };
                        Serializer.Serialize(ms, bmdData);
                    }
                }

                MakeXOR(data, 0, 80);
                fs.Write(data, 0, data.Length);
            }
        }

        static void EventInitialize()
        {
            EventManager = new EventManagement();
            EventManager
                .AddEvent(Events.Events.BloodCastle, new BloodCastles())
                .AddEvent(Events.Events.DevilSquared, new DevilSquares())
                .AddEvent(Events.Events.Kanturu, new Kanturu())
                .AddEvent(Events.Events.ChaosCastle, new ChaosCastles());
            LuckyCoins.Initialize();
            EventChips.Initialize();
        }

        public static async Task GlobalAnoucement(string text)
        {
            await server.SendAll(new SNotice(NoticeType.Gold, text));
            Log.Information(ServerMessages.GetMessage(Messages.Server_GlobalAnnouncement, text));
        }

        public static async Task MapAnoucement(Maps map, string text)
        {
            await ResourceCache.Instance
                .GetMaps()[map]
                .SendAsync(new SNotice(NoticeType.Gold, text));
            Log.Information(ServerMessages.GetMessage(Messages.Server_MapAnnouncement) + text, map);
        }

        public static async Task NoEventMapSendAsync(object message)
        {
            await server
                .Clients
                .Where(x => x.Player.Status == LoginStatus.Playing && !x.Player.Character.Map.IsEvent)
                .SendAsync(message);
        }
        public static async Task NoEventMapAnoucement(string text)
        {
            await NoEventMapSendAsync(new SNotice(NoticeType.Gold, text));
            Log.Information(ServerMessages.GetMessage(Messages.Server_NoEventMapAnnouncement, text));
        }

        public static void Close(object a, EventArgs b)
        {
            if (a != null)
                return;

            GlobalAnoucement(ServerMessages.GetMessage(Messages.Server_Close)).Wait();

            Task.Delay(30000);

            Environment.Exit(0);
        }

        public static void Create(object a, EventArgs b)
        {
            if (a != null)
                return;

            Log.Information("Creating DB");
            using (var db = new GameContext())
                db.Database.EnsureCreated();
            Log.Information("Created DB");
        }

        public static void Migrate(object a, EventArgs b)
        {
            if (a != null)
                return;

            using (var db = new GameContext())
            {
                Log.Information("Dropping DB");
                db.Database.EnsureDeleted();
                Log.Information("Creating DB");
                db.Database.EnsureCreated();
                Log.Information("Created DB");
            }
        }

        public static void Delete(object a, EventArgs b)
        {
            if (a != null)
                return;

            Log.Information("Dropping DB");
            using (var db = new GameContext())
                db.Database.EnsureDeleted();
            Log.Information("Dropped DB");
        }
        public static void LevelUp(object a, CommandEventArgs b)
        {
            var session = a as GSSession;

            if(!string.IsNullOrWhiteSpace(b.Argument))
            {
                var lvls = uint.Parse(b.Argument);
                while(lvls-- > 0)
                {
                    session.Player.Character.Experience = session.Player.Character.NextExperience;
                }
            }else
            {
                session.Player.Character.Experience = session.Player.Character.NextExperience;
            }
        }
    }
}

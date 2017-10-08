using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;

using Meebey.SmartIrc4net;
using NHLScoreBot;

namespace NHLScoreBot.UserInteraction
{
    class IRC : IIRC
    {
        IrcClient ircClient;
        string SERVER;
        int PORT;
        string CHANNEL;
        string CHANNEL2;
        string REALNAME;
        string USERNAME;
        string NICK;
        string NICK2;
        string NICKSERVPW;

        Queue<Command> commands;

        Object runningMutex;
        bool running = true;


        public bool GetRunning()
        {
            bool r;
            lock (runningMutex)
            {
                r = running;
            }

            return r;
        }

        public void SetRunning(bool b)
        {
            lock (runningMutex)
            {
                running = b;
            }
        }

        public IRC()
        {
            // initialize IRC variables
            SERVER = ConfigurationManager.AppSettings["server"];
            PORT = Int32.Parse(ConfigurationManager.AppSettings["port"]);
            CHANNEL = ConfigurationManager.AppSettings["channel"];
            CHANNEL2 = ConfigurationManager.AppSettings["channel2"];
            REALNAME = ConfigurationManager.AppSettings["realname"];
            USERNAME = ConfigurationManager.AppSettings["username"];
            NICK = ConfigurationManager.AppSettings["nick"];
            NICK2 = ConfigurationManager.AppSettings["nick2"];
            NICKSERVPW = ConfigurationManager.AppSettings["nickpw"];
            runningMutex = new Object();
            ircClient = new IrcClient();
            ircClient.AutoReconnect = true;
            ircClient.AutoRejoinOnKick = true;
            ircClient.AutoRelogin = true;
            ircClient.AutoRejoin = true;
            ircClient.AutoRetry = true;
            ircClient.AutoRetryDelay = 20;
            ircClient.OnPing += new PingEventHandler(OnPing);
            ircClient.OnChannelMessage += new IrcEventHandler(OnChannelMessage);
            ircClient.OnQueryMessage += new IrcEventHandler(OnQueryMessage);

            commands = new Queue<Command>();
        }

        public void JoinIRC()
        {
            bool exception = false;

            for (; ; )
            {
                try
                {
                    string[] serverList;
                    serverList = new string[] { SERVER };
                    string[] nickList = new string[] { NICK, NICK2 };
                    int port = PORT;
                    System.Console.WriteLine("connecting to server " + SERVER + " and port " + PORT);
                    ircClient.Connect(serverList, port);

                    ircClient.Login(nickList, REALNAME, 0, USERNAME);
                    System.Console.WriteLine("joining channel " + CHANNEL);
                    ircClient.RfcJoin(CHANNEL);
                    if (CHANNEL2.Length > 0)
                        ircClient.RfcJoin(CHANNEL2);
                    //ircClient.SendMessage(SendType.Message, "NickServ", "IDENTIFY " + NICKSERVPW);

                    for (; ; )
                    {
                        ircClient.Listen(false);

                        lock (runningMutex)
                        {
                            if (!running)
                            {
                                LeaveIRC();
                                break;
                            }
                        }

                        System.Threading.Thread.Sleep(500);
                    }
                }
                catch (Exception ex)
                {
                    exception = true;
                }

                if (!exception)
                    break;

                System.Threading.Thread.Sleep(30000);
            }
        }

        public void ChangeNick(string newNick)
        {
            if (newNick.Length == 0)
            {
                newNick = NICK;
            }

            ircClient.RfcNick(newNick);
            if (newNick.CompareTo(NICK) == 0)
            {
                ircClient.SendMessage(SendType.Message, "NickServ", "IDENTIFY " + NICKSERVPW);
            }
        }

        public Command GetCommand()
        {
            Command result = null;
            lock (commands)
            {
                if (commands.Count > 0)
                    result = commands.Dequeue();
            }

            return result;
        }

        public void SendMessage(string text, Command originalCommand)
        {
            if (originalCommand == null)
            {
                ircClient.SendMessage(SendType.Message, CHANNEL, text);
                if (CHANNEL2.Length > 0)
                    ircClient.SendMessage(SendType.Message, CHANNEL2, text);
            }
            else
            {
                if (originalCommand.PrivateMessage)
                    ircClient.SendMessage(SendType.Message, originalCommand.UserName, text);
                else
                    ircClient.SendMessage(SendType.Message, originalCommand.Channel, text);
            }
        }

        public void Kick(string person)
        {
            ircClient.RfcKick(CHANNEL, person, this.GetQuote());
        }

        private string GetQuote()
        {
            Random random = new Random();
            int chosen = random.Next(9);
            string quote = "";

            switch (chosen)
            {
                case 0:
                    quote = "BIG BODY PRESENCE";
                    break;
                case 1:
                    quote = "ACTIVE STICK";
                    break;
                case 2:
                    quote = "MONSTER";
                    break;
                case 3:
                    quote = "WHAMMO";
                    break;
                case 4:
                    quote = "ACTIVE GLOVE";
                    break;
                case 5:
                    quote = "PUCK POISE";
                    break;
                case 6:
                    quote = "TENACITY";
                    break;
                case 7:
                    quote = "DOUBLE DION";
                    break;
                case 8:
                    quote = "FINE YOUNG MAN";
                    break;
                case 9:
                    quote = "BIG STICK";
                    break;

                default:
                    break;
            }

            return quote;

        }

        private void LeaveIRC()
        {
            ircClient.RfcQuit(GetQuote(), Priority.Critical);
            ircClient.Disconnect();
        }

        private void OnPing(object sender, PingEventArgs e)
        {
            Console.WriteLine("Responded to ping at {0}.",
            DateTime.Now.ToShortTimeString());
        }

        void OnChannelMessage(object sender, IrcEventArgs e)
        {
            if (e.Data.Message.StartsWith("!"))
            {
                lock (commands)
                {
                    commands.Enqueue(new Command(e.Data.Channel, e.Data.Nick, e.Data.Message, false));
                    System.Console.WriteLine(e.Data.Nick + "> " + e.Data.Message);
                }
            }
        }

        void OnQueryMessage(object sender, IrcEventArgs e)
        {
            lock (commands)
            {
                commands.Enqueue(new Command(e.Data.Channel, e.Data.Nick, e.Data.Message, true));
                System.Console.WriteLine("PM:" + e.Data.Nick + "> " + e.Data.Message);
            }
        }

    }
}

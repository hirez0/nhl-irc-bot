using System;
using System.Collections.Generic;
using System.Configuration;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using NHLScoreBot.UserInteraction;

using DateTimeParser;

namespace NHLScoreBot
{
    class Program
    {
        #region Static variables and methods

        private const string IRC = "irc";
        private const string TEXT = "text";
        private static Object runningMutex;
        private static bool running = true;
        public static Thread mainThread;
        public static bool announceEvents = true;
        private static Object lockObj;
        
        public static bool GetRunning()
        {
            bool r;
            lock (Program.runningMutex)
            {
                r = Program.running;
            }

            return r;
        }

        public static void SetRunning(bool b)
        {
            lock (Program.runningMutex)
            {
                running = b;
            }
        }

        static void IRCThread(object irc)
        {
            ((IIRC)irc).JoinIRC();
        }

        #endregion

        private enum GetScheduleType
        {
            GETSCHEDULE_DATE,
            GETSCHEDULE_TEAM,
            GETSCHEDULE_TEAMS,
            GETSCHEDULE_BADARGUMENT
        }

        List<INHLGameInfo> games;
        IIRC irc;
        Thread ircThread;
        DateTime lastUpdate;
        NHLStats stats;

        public Program()
        {
            runningMutex = new Object();
            lockObj = new Object();

            mainThread = Thread.CurrentThread;

            games = new List<INHLGameInfo>();

            stats = new NHLStats();            

            lastUpdate = DateTime.MinValue;
            FetchGameList();

            stats.UpdateStatsIfRequired();

            irc = GetInteractionObject();

            ircThread = new Thread(new ParameterizedThreadStart(IRCThread));
            ircThread.Start(irc);
        }

        private IIRC GetInteractionObject()
        {
            string cfg = ConfigurationManager.AppSettings["interaction"];
            if (IRC.Equals(cfg))
            {
                return new IRC();
            }
            else if (TEXT.Equals(cfg))
            {
                return new Text();
            }
            Console.WriteLine("GetInteractionObject did not get valid configuration value: " + cfg);
            return new Text();
        }

        private void FetchGameList()
        {     
            if ((DateTime.Now.Hour > 1 && DateTime.Now.Hour < 9) && games.Count > 0)
            {
                System.Console.WriteLine("Flushing games and downloading new schedule...");

                foreach (NHLGame game in games)
                {
                    game.Shutdown();
                }
                games.Clear();
                stats.UpdateSchedule();
            }
            else
            {
                NHLDotComFetch fetch = new NHLDotComFetch();

                DateTime cStart = DateTime.Now;
                List<int> gameIds = fetch.FetchGameIndex(DateTime.Now);
                TimeSpan cDiff = DateTime.Now - cStart;
                System.Console.WriteLine(String.Format("Took {0} s for FetchGameIndex download", cDiff.TotalSeconds));

                foreach (int i in gameIds)
                {
                    NHLGame game = null;
                    bool gameAlreadyExists = false;

                    foreach (NHLGame existingGame in games)
                    {
                        if (existingGame.GameId == i)
                        {
                            gameAlreadyExists = true;
                            break;
                        }
                    }

                    if (!gameAlreadyExists)
                    {
                        game = new NHLGame(i);
                        games.Add(game);
                        game.StartMonitoring(stats);
                        System.Console.WriteLine("Adding game {0}", i);
                    }
                }
            }

        }

        public void MonitorGames()
        {
            NHLGameEvent gameEvent = null;
            String buf;
            Command command;

            int count = 0;
            int longCount = 0;

            // TODO fix 2012
            //stats.UpdateSchedule();

            foreach (INHLGameInfo game in games)
            {
                while (game.GetNewEvent() != null)
                    ;
            }

            for (; ;)
            {
                DateTime cStart;
                /* Every 1 second */
                if (count == 10)
                {
                    cStart = DateTime.Now;

                    foreach (INHLGameInfo game in games)
                    {
                        DateTime dStart = DateTime.Now;

                        while ((gameEvent = game.GetNewEvent()) != null)
                        {
                            if (gameEvent.Type == NHLGameEvent.EventType.EVENT_GOAL)
                            {
                                System.Console.WriteLine("Goal event recognized " + game.AwayTeamName);

                                buf = String.Format("[{0}/{1}] {2}{3}. Score: {4}",
                                    game.HomeTeamName, game.AwayTeamName,
                                    gameEvent.Updated ? "Revised info: " : "",
                                    gameEvent.EventText, game.GetScoreString(gameEvent, false));

                                if (announceEvents) // && (game.HomeTeamName.Equals("Oilers") || game.AwayTeamName.Equals("Oilers") || game.HomeTeamName.Equals("Senators") || game.AwayTeamName.Equals("Senators") || game.HomeTeamName.Equals("Penguins") || game.AwayTeamName.Equals("Penguins")))
                                    irc.SendMessage(buf, null);
                            }
                            else if (gameEvent.Type == NHLGameEvent.EventType.EVENT_GAMEOVER)
                            {
                                buf = String.Format("[{0}/{1}] {2} {3}",
                                    game.HomeTeamName, game.AwayTeamName, gameEvent.EventText, game.GetScoreString(true));

                                if (announceEvents) // && (game.HomeTeamName.Equals("Oilers") || game.AwayTeamName.Equals("Oilers") || game.HomeTeamName.Equals("Senators") || game.AwayTeamName.Equals("Senators") || game.HomeTeamName.Equals("Penguins") || game.AwayTeamName.Equals("Penguins")))
                                    irc.SendMessage(buf, null);
                            }
                            else if (gameEvent.Type == NHLGameEvent.EventType.EVENT_DEBUG)
                            {
                                irc.SendMessage("debug event flushed", new Command(string.Empty, "sabotaged", string.Empty, true));
                            }
                        }
                    }

                    TimeSpan cDiff = DateTime.Now - cStart;
                    if (cDiff.TotalSeconds > 5)
                    {
                        System.Console.WriteLine(String.Format("Took {0} s for processing games event loop", cDiff.TotalSeconds));
                    }

                    cStart = DateTime.Now;
                    
                    count = 0;        
                    stats.UpdateStatsIfRequired();

                    cDiff = DateTime.Now - cStart;
                    if (cDiff.TotalSeconds > 5)
                    {
                        System.Console.WriteLine(String.Format("Took {0} s for UpdateStatsIfRequired", cDiff.TotalSeconds));
                    }
                }

                cStart = DateTime.Now;

                /* Every 30 minutes */
                if (longCount == 18000)
                {
                    FetchGameList();
                    longCount = 0;
                }

                TimeSpan eDiff = DateTime.Now - cStart;
                if (eDiff.TotalSeconds > 5)
                {
                    System.Console.WriteLine(String.Format("Took {0} s for FetchGameList", eDiff.TotalSeconds));
                }

                cStart = DateTime.Now;

                command = irc.GetCommand();
                try
                {
                    if (command != null)
                        HandleCommand(command);
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine("exception in handle command: " + ex.Message + " : " + ex.StackTrace);
                }

                eDiff = DateTime.Now - cStart;
                if (eDiff.TotalSeconds > 5)
                {
                    System.Console.WriteLine(String.Format("Took {0} s for HandleCommand", eDiff.TotalSeconds));
                }

                System.Threading.Thread.Sleep(100);
                count++;
                longCount++;

                cStart = DateTime.Now;

                if (!Program.GetRunning())
                    break;

                eDiff = DateTime.Now - cStart;
                if (eDiff.TotalSeconds > 5)
                {
                    System.Console.WriteLine(String.Format("Took {0} s for Program.GetRunning", eDiff.TotalSeconds));
                }
            }

            /////////// shutdown //////////

            foreach (INHLGameInfo game in games)
            {
                game.Shutdown();
            }

            stats.Shutdown();

            irc.SetRunning(false);
            ircThread.Join();
        }       

        private void HandleCommand(Command command)
        {
            String buf;

            if (command.Matches("!flushgames"))
            {
                foreach (INHLGameInfo game in games)
                {
                    game.StopMonitoring();
                }

                games.Clear();
            }
            else if (command.Matches("!xnick"))
            {
                if (command.HasArgument())
                {
                    irc.ChangeNick(command.GetArgumentOriginalCase());
                }
                else
                {
                    irc.ChangeNick("");
                }
            }
            else if (command.Matches("!announce"))
            {
                if (announceEvents == true)
                {
                    announceEvents = false;
                    irc.SendMessage("No longer announcing game events.", command);
                }
                else
                {
                    announceEvents = true;
                    irc.SendMessage("Now announcing game events.", command);
                }
            }
            else if (command.Matches("!scores"))
            {
                //List<string> homeTeamNames = new List<string>();
                bool nothingToReport = true;
                foreach (INHLGameInfo game in games)
                {
                    if (game.GetStatus() == NHLGameStatus.GAME_PLAYING)
                    {
                        buf = String.Format("[{0}/{1}] Score: {2}",
                            game.HomeTeamName, game.AwayTeamName, game.GetScoreString(true));

                        irc.SendMessage(buf, command);
                        nothingToReport = false;
                        //homeTeamNames.Add(game.HomeTeamName);
                    }
                }

                if (nothingToReport)
                    irc.SendMessage("No active games to report on", command);

            }
            else if (command.Matches("!debuggames"))
            {
                foreach (INHLGameInfo game in games)
                {
                    buf = String.Format("[{0}/{1}]",
                                        game.HomeTeamName, game.AwayTeamName);

                    irc.SendMessage(buf, command);
                }
            }
            else if (command.Matches("!allscores"))
            {
                //send this back via pm
                command.PrivateMessage = true;
                List<string> homeTeamNames = new List<string>();
                foreach (INHLGameInfo game in games)
                {
                    if (game.GetStatus() == NHLGameStatus.GAME_PLAYING ||
                        game.GetStatus() == NHLGameStatus.GAME_FINAL)
                    {
                        buf = String.Format("[{0}/{1}] Score: {2}",
                            game.HomeTeamName, game.AwayTeamName, game.GetScoreString(true));

                        irc.SendMessage(buf, command);
                        homeTeamNames.Add(game.HomeTeamName);
                    }
                }

                ScheduledGame[] otherGames = stats.GetScheduleToday(homeTeamNames);
                foreach (ScheduledGame game in otherGames)
                {
                    buf = String.Format("[{0}/{1}] Starts at: {2}",
                        game.HomeTeamName, game.AwayTeamName, game.StartTime);

                    irc.SendMessage(buf, command);
                }
            }
            else if (command.Matches("!finalscores") || command.Matches("!finals"))
            {
                bool nothingToReport = true;
                foreach (INHLGameInfo game in games)
                {
                    if (game.GetStatus() == NHLGameStatus.GAME_FINAL)
                    {
                        buf = String.Format("[{0}/{1}] Score: {2}",
                            game.HomeTeamName, game.AwayTeamName, game.GetScoreString(true));

                        irc.SendMessage(buf, command);
                        nothingToReport = false;
                    }
                }

                if (nothingToReport)
                    irc.SendMessage("No final scores to report", command);
            }
            else if (command.Matches("!schedule"))
            {
                string msg;
                DateTime? compareDate = null;
                NHLTeam team = null;

                NHLTeam teamA = null, teamB = null;

                GetScheduleType type = GetScheduleType.GETSCHEDULE_DATE;

                if (command.HasArgument())
                {
                    int numOfSpaces = Regex.Matches(command.GetArgument(), " ").Count;
                    if (numOfSpaces > 0)
                    {
                        type = GetScheduleType.GETSCHEDULE_TEAMS;

                        string[] teams = command.GetArgument().Split(' ');
                        string teamAquery = teams[0];
                        string teamBquery = teams[1];

                        teamA = stats.GetTeamFromCityOrTeam(teamAquery);
                        teamB = stats.GetTeamFromCityOrTeam(teamBquery);

                        if (teamA == null || teamB == null)
                        {
                            type = GetScheduleType.GETSCHEDULE_DATE;
                            compareDate = DateTimeEnglishParser.ParseRelative(DateTime.Now, command.GetArgument());

                            if (compareDate == null)
                            {
                                type = GetScheduleType.GETSCHEDULE_BADARGUMENT;
                            }
                            else
                            {
                                type = GetScheduleType.GETSCHEDULE_DATE;
                            }
                        }
                        else if (teamA.Equals(teamB))
                        {
                            type = GetScheduleType.GETSCHEDULE_TEAM;
                            team = teamA;
                        }
                    }
                    else
                    {
                        team = stats.GetTeamFromCityOrTeam(command.GetArgument());
                        if (team != null)
                        {
                            type = GetScheduleType.GETSCHEDULE_TEAM;
                        }
                        else
                        {
                            compareDate = DateTimeEnglishParser.ParseRelative(DateTime.Now, command.GetArgument());
                            if (compareDate == null)
                            {
                                type = GetScheduleType.GETSCHEDULE_BADARGUMENT;
                            }
                            else
                            {
                                type = GetScheduleType.GETSCHEDULE_DATE;
                            }
                        }
                    }
                }
                else
                {
                    type = GetScheduleType.GETSCHEDULE_DATE;
                    compareDate = DateTime.Now;
                }

                ScheduledGame[] result = new ScheduledGame[0];

                switch (type)
                {
                    case GetScheduleType.GETSCHEDULE_BADARGUMENT:
                        irc.SendMessage(string.Format("{0} is not recognized", command.GetArgument()), command);
                        break;

                    case GetScheduleType.GETSCHEDULE_DATE:

                        result = stats.GetSchedule((DateTime)compareDate);

                        break;

                    case GetScheduleType.GETSCHEDULE_TEAM:

                        result = stats.GetSchedule(team, 4);

                        break;

                    case GetScheduleType.GETSCHEDULE_TEAMS:

                        result = stats.GetSchedule(teamA, teamB, 3);

                        break;
                }

                switch (type)
                {
                    case GetScheduleType.GETSCHEDULE_BADARGUMENT:

                        break;

                    case GetScheduleType.GETSCHEDULE_DATE:

                        if (result.Length == 0)
                        {
                            irc.SendMessage(
                                String.Format("Didn't find any scheduled games for {0}",
                                ((DateTime)compareDate).ToShortDateString()), command);
                        }
                        else
                        {
                            irc.SendMessage(string.Format("Games for {0}:", ((DateTime)compareDate).ToShortDateString()),
                                command);

                            foreach (ScheduledGame game in result)
                            {
                                string matchup = string.Format("{0} @ {1}", game.AwayTeamName, game.HomeTeamName);
                                string date = game.StartTime + " ET";

                                msg = string.Format("{0} {1} ({2})", matchup.PadRight(25),
                                                                    date.PadRight(15),
                                                                    game.ExtraInfo);
                                irc.SendMessage(msg, command);
                            }
                        }

                        break;

                    case GetScheduleType.GETSCHEDULE_TEAMS:
                    case GetScheduleType.GETSCHEDULE_TEAM:

                        string name;

                        if (type == GetScheduleType.GETSCHEDULE_TEAM)
                            name = team.Name;
                        else
                            name = teamA.Name + " / " + teamB.Name;

                        if (result.Length == 0)
                        {
                            irc.SendMessage(
                                String.Format("Didn't find any scheduled games for {0}",
                                                name), command);
                        }
                        else
                        {
                            irc.SendMessage(string.Format("Next set of games for {0}:", name),
                                                        command);

                            foreach (ScheduledGame game in result)
                            {
                                string matchup = string.Format("{0} @ {1}", game.AwayTeamName, game.HomeTeamName);
                                string date = game.StartDateString + " " + game.StartTime + " ET";

                                msg = string.Format("{0} {1} ({2})", matchup.PadRight(25),
                                                                  date.PadRight(30),
                                                                  game.ExtraInfo);
                                irc.SendMessage(msg, command);
                            }
                        }

                        break;
                }
            }

            else if (command.Matches("!score"))
            {
                String name = command.GetArgument();
                name = stats.GetTeamNameAlias(name);
                NHLTeam team = stats.GetTeamFromCityOrTeam(name);

                if (team != null)
                {
                    name = team.Name;
                    foreach (INHLGameInfo game in games)
                    {
                        if (game.GetStatus() == NHLGameStatus.GAME_PLAYING ||
                            game.GetStatus() == NHLGameStatus.GAME_FINAL)
                        {
                            if (game.GetStatus() != NHLGameStatus.GAME_NODATA && game.HasTeam(name))
                            {
                                buf = String.Format("[{0}/{1}] Score: {2}",
                                    game.HomeTeamName, game.AwayTeamName, game.GetScoreString(true));

                                irc.SendMessage(buf, command);

                                break;
                            }
                        }
                        else if ((game.GetStatus() == NHLGameStatus.GAME_NOTSTARTED ||
                                    game.GetStatus() == NHLGameStatus.GAME_PREVIEWDATA)
                                        && game.HasTeam(name))
                        {
                            buf = String.Format("[{0}/{1}] Starting shortly",
                                game.HomeTeamName, game.AwayTeamName);

                            irc.SendMessage(buf, command);
                        }
                    }
                }
            }
            else if (command.Matches("!debug"))
            {
                String name = command.GetArgument();
                name = stats.GetTeamNameAlias(name);
                NHLTeam team = stats.GetTeamFromCityOrTeam(name);

                if (team != null)
                {
                    name = team.Name;
                    foreach (INHLGameInfo game in games)
                    {
                        if ((game.GetStatus() == NHLGameStatus.GAME_PLAYING ||
                            game.GetStatus() == NHLGameStatus.GAME_FINAL) &&
                            game.GetStatus() != NHLGameStatus.GAME_NODATA && game.HasTeam(name))
                        {
                            game.AddDebugEvent();
                            break;
                        }
                    }
                }
            }
            else if (command.Matches("!sog"))
            {
                if (command.HasArgument())
                {
                    String name = command.GetArgument();
                    name = stats.GetTeamNameAlias(name);
                    NHLTeam team = stats.GetTeamFromCityOrTeam(name);

                    if (team != null)
                    {
                        name = team.Name;
                        foreach (INHLGameInfo game in games)
                        {
                            if ((game.GetStatus() == NHLGameStatus.GAME_PLAYING ||
                                game.GetStatus() == NHLGameStatus.GAME_FINAL) && game.HasTeam(name))
                            {
                                buf = String.Format("[{0}/{1}] Shots on goal: {2} {3}, {4} {5}",
                                    game.HomeTeamName, game.AwayTeamName,
                                    game.HomeTeamName, game.HomeShots,
                                    game.AwayTeamName, game.AwayShots);

                                irc.SendMessage(buf, command);

                                break;
                            }
                        }
                    }
                }
            }
            else if (command.Matches("!hits"))
            {
                if (command.HasArgument())
                {
                    String name = command.GetArgument();
                    name = stats.GetTeamNameAlias(name);
                    NHLTeam team = stats.GetTeamFromCityOrTeam(name);

                    if (team != null)
                    {
                        name = team.Name;
                        foreach (INHLGameInfo game in games)
                        {
                            if ((game.GetStatus() == NHLGameStatus.GAME_PLAYING ||
                                game.GetStatus() == NHLGameStatus.GAME_FINAL) && game.HasTeam(name))
                            {
                                buf = String.Format("[{0}/{1}] Hits: {2} {3}, {4} {5}",
                                    game.HomeTeamName, game.AwayTeamName,
                                    game.HomeTeamName, game.NhlGameStats.GetGameTeamStat("hit", true, false),
                                    game.AwayTeamName, game.NhlGameStats.GetGameTeamStat("hit", false, false));

                                irc.SendMessage(buf, command);

                                break;
                            }
                        }
                    }
                }
            }
            else if (command.Matches("!pim"))
            {
                if (command.HasArgument())
                {
                    String name = command.GetArgument();
                    name = stats.GetTeamNameAlias(name);
                    NHLTeam team = stats.GetTeamFromCityOrTeam(name);

                    if (team != null)
                    {
                        name = team.Name;
                        foreach (INHLGameInfo game in games)
                        {
                            if ((game.GetStatus() == NHLGameStatus.GAME_PLAYING ||
                                game.GetStatus() == NHLGameStatus.GAME_FINAL) && game.HasTeam(name))
                            {
                                buf = String.Format("[{0}/{1}] Penalty Minutes: {2} {3}, {4} {5}",
                                    game.HomeTeamName, game.AwayTeamName,
                                    game.HomeTeamName, game.NhlGameStats.GetGameTeamStat("penalty", true, false),
                                    game.AwayTeamName, game.NhlGameStats.GetGameTeamStat("penalty", false, false));

                                irc.SendMessage(buf, command);

                                break;
                            }
                        }
                    }
                }
            }
            else if (command.Matches("!scratches"))
            {
                if (command.HasArgument())
                {
                    String name = command.GetArgument();
                    name = stats.GetTeamNameAlias(name);
                    NHLTeam team = stats.GetTeamFromCityOrTeam(name);

                    if (team != null)
                    {
                        name = team.Name;
                        foreach (INHLGameInfo game in games)
                        {
                            if ((game.GetStatus() == NHLGameStatus.GAME_PLAYING ||
                                game.GetStatus() == NHLGameStatus.GAME_FINAL) && game.HasTeam(name))
                            {
                                string scratches = "Scratches: " + game.NhlGameStats.GetScratches();
                                irc.SendMessage(scratches, command);

                                break;
                            }
                        }
                    }
                }
            }
            else if (command.Matches("!ptop"))
            {
                if (command.HasArgument())
                {
                    String argument = command.GetArgument();
                    String season = GetSeasonString(argument);
                    String filter = GetFilterString(argument);
                    argument = StripPrefixes(argument);

                    String[] results = stats.GetPlayers(argument, true, season, filter);
                    foreach (string s in results)
                    {
                        irc.SendMessage(s, command);
                    }
                }
            }
            else if (command.Matches("!gtop"))
            {
                if (command.HasArgument())
                {
                    String argument = command.GetArgument();
                    String season = GetSeasonString(argument);
                    String filter = GetFilterString(argument);
                    argument = StripPrefixes(argument);

                    String[] results = stats.GetGoalies(argument, true, season, filter);
                    foreach (string s in results)
                    {
                        irc.SendMessage(s, command);
                    }
                }
            }
            else if (command.Matches("!ttop"))
            {
                if (command.HasArgument())
                {
                    String argument = command.GetArgument();
                    String season = GetSeasonString(argument);
                    argument = StripPrefixes(argument);

                    String[] results = stats.GetTeams(argument, true, season);
                    foreach (string s in results)
                    {
                        irc.SendMessage(s, command);
                    }
                }
            }
            else if (command.Matches("!pbot"))
            {
                if (command.HasArgument())
                {
                    String argument = command.GetArgument();
                    String season = GetSeasonString(argument);
                    String filter = GetFilterString(argument);
                    argument = StripPrefixes(argument);

                    String[] results = stats.GetPlayers(argument, false, season, filter);
                    foreach (string s in results)
                    {
                        irc.SendMessage(s, command);
                    }
                }
            }
            else if (command.Matches("!gbot"))
            {
                if (command.HasArgument())
                {
                    String argument = command.GetArgument();
                    String season = GetSeasonString(argument);
                    String filter = GetFilterString(argument);
                    argument = StripPrefixes(argument);

                    String[] results = stats.GetGoalies(argument, false, season, filter);
                    foreach (string s in results)
                    {
                        irc.SendMessage(s, command);
                    }
                }
            }
            else if (command.Matches("!tbot"))
            {
                if (command.HasArgument())
                {
                    String argument = command.GetArgument();
                    String season = GetSeasonString(argument);
                    argument = StripPrefixes(argument);

                    String[] results = stats.GetTeams(argument, false, season);
                    foreach (string s in results)
                    {
                        irc.SendMessage(s, command);
                    }
                }
            }
            else if (command.Matches("!list"))
            {
                irc.SendMessage("http://www.sportsargumentwiki.com/index.php?title=Nhlfeed", command);
            }
            else if (command.Matches("!pstat") || command.Matches("!pstats"))
            {
                if (command.HasArgument())
                {
                    String argument = command.GetArgument();
                    String entry = String.Empty;
                    String name, season;

                    int s = argument.IndexOf('[');
                    int e = argument.IndexOf(']');
                    if (s >= 0 && e > 0)
                    {
                        entry = argument.Substring(s + 1, e - s - 1);
                    }

                    season = GetSeasonString(argument);

                    argument = StripPrefixes(argument);
                    name = argument;

                    irc.SendMessage(stats.GetIndividualPlayer(entry, name, season), command);
                }

            }
            else if (command.Matches("!gstat") || command.Matches("!gstats"))
            {
                if (command.HasArgument())
                {
                    String argument = command.GetArgument();
                    String entry = String.Empty;
                    String name, season;

                    int s = argument.IndexOf('[');
                    int e = argument.IndexOf(']');
                    if (s >= 0 && e > 0)
                    {
                        entry = argument.Substring(s + 1, e - s - 1);
                    }

                    season = GetSeasonString(argument);

                    argument = StripPrefixes(argument);
                    name = argument;

                    irc.SendMessage(stats.GetIndividualGoalie(entry, name, season), command);
                }
            }
            else if (command.Matches("!tstat") || command.Matches("!tstats"))
            {
                if (command.HasArgument())
                {
                    String argument = command.GetArgument();
                    String entry = String.Empty;
                    String name, season;

                    int s = argument.IndexOf('[');
                    int e = argument.IndexOf(']');
                    if (s >= 0 && e > 0)
                    {
                        entry = argument.Substring(s + 1, e - s - 1);
                    }

                    season = GetSeasonString(argument);
                    argument = StripPrefixes(argument);
                    name = argument;

                    irc.SendMessage(stats.GetIndividualTeam(entry, name, season), command);
                }
            }
            else if (command.Matches("!updatealiases"))
            {
                if (stats.FetchAliasesBroken)
                {
                    irc.SendMessage("Some idiot broke the formatting on the alias page, fix it first", command);
                    stats.FetchAliasesBroken = false;
                }
                else
                {
                    if (!stats.FetchAliases)
                    {
                        stats.FetchAliases = true;
                    }
                    else
                    {
                        irc.SendMessage("Already in the process of loading aliases", command);
                    }
                }

            }
            else if (command.Matches("!gamestats") || command.Matches("!gamestat"))
            {
                bool found = false;
                String entry;

                if (command.HasArgument())
                {
                    entry = command.GetArgument();
                    entry = stats.GetPlayerNameAlias(entry);
                    entry = stats.GetGoalieNameAlias(entry);
                    entry = stats.GetTeamNameAlias(entry);

                    NHLTeam team = stats.GetTeamFromCityOrTeam(entry);
                    if (team != null)
                    {
                        foreach (INHLGameInfo game in games)
                        {
                            if ((game.GetStatus() == NHLGameStatus.GAME_PLAYING ||
                                    game.GetStatus() == NHLGameStatus.GAME_FINAL) && game.HasTeam(team.Name))
                            {
                                if (game.NhlGameStats != null)
                                    irc.SendMessage(game.NhlGameStats.GetGameStats(), command);
                                else
                                    irc.SendMessage("No game stats available yet for that game", command);

                                found = true;
                                break;
                            }
                        }
                    }

                    if (!found)
                    {
                        string msg;
                        foreach (INHLGameInfo game in games)
                        {
                            if (game.GetStatus() == NHLGameStatus.GAME_PLAYING ||
                                game.GetStatus() == NHLGameStatus.GAME_FINAL)
                            {
                                if (game.NhlGameStats != null)
                                {
                                    msg = game.NhlGameStats.GetIndividualStats(entry);
                                    if (msg != null)
                                    {
                                        irc.SendMessage(msg, command);
                                        found = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (!found)
                        irc.SendMessage("Couldn't find team/player/goalie for game stats", command);
                }
            }

            else if (command.Matches("!summary"))
            {
                bool found = false;
                String entry;

                if (command.HasArgument())
                {
                    entry = command.GetArgument();
                    entry = stats.GetPlayerNameAlias(entry);
                    entry = stats.GetGoalieNameAlias(entry);
                    entry = stats.GetTeamNameAlias(entry);

                    NHLTeam team = stats.GetTeamFromCityOrTeam(entry);
                    if (team != null)
                    {
                        foreach (INHLGameInfo game in games)
                        {
                            if ((game.GetStatus() == NHLGameStatus.GAME_PLAYING ||
                                    game.GetStatus() == NHLGameStatus.GAME_FINAL) && game.HasTeam(team.Name))
                            {
                                List<string> summary = game.NhlGameStats.GetGameEvents();

                                foreach (string e in summary)
                                {
                                    irc.SendMessage(e, command);
                                }

                                found = true;
                                break;
                            }
                        }
                    }

                    if (!found)
                    {
                        foreach (INHLGameInfo game in games)
                        {
                            if (game.GetStatus() == NHLGameStatus.GAME_PLAYING ||
                                game.GetStatus() == NHLGameStatus.GAME_FINAL)
                            {
                                if (game.NhlGameStats != null)
                                {
                                    List<string> summary = game.NhlGameStats.GetPlayerEvents(entry);

                                    if (entry.Length == 1)
                                    {
                                        irc.Kick(command.UserName);
                                        break;
                                    }

                                    if (summary.Count > 25)
                                    {
                                        command.PrivateMessage = true;
                                    }

                                    if (summary.Count > 50)
                                    {
                                        break;
                                    }

                                    if (summary.Count > 0)
                                    {
                                        foreach (string e in summary)
                                        {
                                            irc.SendMessage(e, command);
                                        }
                                        found = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (!found)
                        irc.SendMessage("Couldn't find team/player for summary", command);
                }
            }
            else if (command.Matches("!getschedule"))
            {
                stats.UpdateSchedule();
            }
            else if (command.Matches("!record"))
            {
                if (command.HasArgument())
                {
                    NHLTeam team = null;

                    string argument = command.GetArgument();
                    string season = GetSeasonString(argument);
                    string seasonInfo = stats.GetPrettySeasonStringFromUserSeason(season);

                    string[] words = command.GetArgument().Split(' ');

                    foreach (string word in words)
                    {
                        team = stats.GetTeamFromCityOrTeam(word);
                        if (team != null)
                            break;
                    }

                    if (team != null)
                    {
                        bool? home = null;
                        if (command.GetArgument().ToLower().Contains("home"))
                            home = true;
                        else if (command.GetArgument().ToLower().Contains("away"))
                            home = false;

                        NHLStats.TeamRecordFilterType filterType;
                        if (home == null)
                            filterType = NHLStats.TeamRecordFilterType.EITHER;
                        else if (home == true)
                            filterType = NHLStats.TeamRecordFilterType.HOME;
                        else
                            filterType = NHLStats.TeamRecordFilterType.AWAY;

                        bool recent = false;
                        if (command.GetArgument().ToLower().Contains("recent"))
                            recent = true;

                        List<TeamRecord> conferenceStandings = stats.ConferenceStandings(team.Conference == "Western", games, season,
                                                                                         recent ? 10 : 82, filterType);
                        List<TeamRecord> leagueStandings = stats.LeagueStandings(games, season,
                                                                                 recent ? 10 : 82, filterType);

                        int conferenceRank = 1, leagueRank = 1;
                        TeamRecord record = null;
                        foreach (TeamRecord teamRecord in conferenceStandings)
                        {
                            if (teamRecord.Team.Equals(team))
                            {
                                record = teamRecord;
                                break;
                            }
                            conferenceRank++;
                        }

                        foreach (TeamRecord teamRecord in leagueStandings)
                        {
                            if (teamRecord.Team.Equals(team))
                            {
                                break;
                            }
                            leagueRank++;
                        }

                        string description = string.Empty;
                        if (home == true)
                            description = "Home Only";
                        else if (home == false)
                            description = "Away Only";

                        if (recent && home != null)
                            description += ", Last 10";
                        else if (recent)
                            description += "Last 10";

                        if (record.getGamesPlayed() == 0)
                        {                            
                            irc.SendMessage(string.Format("No game records for {0} {1} {2}", seasonInfo, team.City, team.Name), command);
                        }
                        else
                        {
                            if (record != null)
                            {
                                irc.SendMessage(string.Format("{6} {0} {1} ranked #{2} place in conference, #{3} in league {4} -- {5}",
                                                                team.City, team.Name, conferenceRank, leagueRank,
                                                                description.Length > 0 ? "(" + description + ")" : string.Empty,
                                                                stats.FormatRecord(record), seasonInfo), command);
                            }
                        }
                    }
                    else
                    {
                        irc.SendMessage("Couldn't find that team", command);
                    }
                }


            }
            else if (command.Matches("!standings"))
            {
				int maxlines = 16;

                command.PrivateMessage = true;
                if (command.HasArgument())
                {
                    StandingsType standingsType = StandingsType.LEAGUE;
                    if (command.GetArgument().ToLower().Contains("west"))
                        standingsType = StandingsType.WEST;
                    else if (command.GetArgument().ToLower().Contains("east"))
                        standingsType = StandingsType.EAST;
                    bool? home = null;
                    if (command.GetArgument().ToLower().Contains("home"))
                        home = true;
                    else if (command.GetArgument().ToLower().Contains("away"))
                        home = false;

                    bool recent = false;
                    if (command.GetArgument().ToLower().Contains("recent"))
                        recent = true;

                    NHLStats.TeamRecordFilterType filterType;
                    if (home == null)
                        filterType = NHLStats.TeamRecordFilterType.EITHER;
                    else if (home == true)
                        filterType = NHLStats.TeamRecordFilterType.HOME;
                    else
                        filterType = NHLStats.TeamRecordFilterType.AWAY;

                    string title = string.Empty;
                    if (recent)
                        title = "Standings for points from teams' last 10 games only";
                    else
                        title = "Current standings";

                    if (filterType == NHLStats.TeamRecordFilterType.AWAY)
                        title += ", for away games";
                    else if (filterType == NHLStats.TeamRecordFilterType.HOME)
                        title += ", for home games";

                    if (standingsType == StandingsType.EAST)
                        title += "    (Eastern Conference)";
                    else if (standingsType == StandingsType.WEST)
                        title += "    (Western Conference)";
                    irc.SendMessage(title, command);

                    List<TeamRecord> standings;

                    if (standingsType == StandingsType.LEAGUE)
                        standings = stats.LeagueStandings(games, string.Empty, recent ? 10 : 82, filterType);
                    else
                        standings = stats.ConferenceStandings(standingsType == StandingsType.WEST, games, string.Empty, recent ? 10 : 82, filterType);

                    int i = 1;
                    foreach (TeamRecord teamRecord in standings)
					{
						if (i > maxlines)
							break;

                        if (i == 9 && standingsType != StandingsType.LEAGUE)
                            irc.SendMessage("==============================", command);

                        string rank = Convert.ToString(i) + ".";

                        irc.SendMessage(string.Format("{0} {1} {2}", rank.PadRight(3), teamRecord.Team.Name.PadRight(16), stats.FormatRecord(teamRecord)), command);
                        i++;
                    }
                }
                else
                {
                    List<TeamRecord> standings = stats.LeagueStandings(games, string.Empty, 82, NHLStats.TeamRecordFilterType.EITHER);
                    int i = 1;
                    foreach (TeamRecord teamRecord in standings)
					{
						if (i > maxlines)
							break;

                        string rank = Convert.ToString(i) + ".";

                        irc.SendMessage(string.Format("{0} {1} {2}", rank.PadRight(3), teamRecord.Team.Name.PadRight(16), stats.FormatRecord(teamRecord)), command);
                        i++;
                    }
                }
            }
           
        }

        enum StandingsType
        {
            WEST,
            EAST,
            LEAGUE
        }

        private string StripPrefixes(string argument)
        {
            argument = Regex.Replace(argument, "\\([0-9][0-9].*?\\)", String.Empty);
            argument = Regex.Replace(argument, "\\[.*?\\]", String.Empty);
            argument = argument.Trim();

            return argument;
        }

        private string GetFilterString(string text)
        {
            string ret = "";
            int s = text.IndexOf("[");
            int e = text.IndexOf("]");

            if (s >= 0 && e > s)
            {
                ret = text.Substring(s + 1);
                ret = ret.Substring(0, ret.IndexOf("]"));                
            }
            return ret;
        }

        private string GetSeasonString(string text)
        {
            string result = string.Empty;
            string temp;
            int s = text.IndexOf("(");
            int e = text.IndexOf(")");
            bool playoffs = false;
            string firstYear = string.Empty, secondYear = string.Empty;

            try
            {                
                if (text.ToLower().Contains("(all)"))
                    result = "ALLR";
                else if (text.ToLower().Contains("(all playoffs)"))
                    result = "ALLP";
                else if (s >= 0 && e > s)
                {
                    temp = text.Substring(s + 1);
                    temp = temp.Substring(0, temp.IndexOf(")"));

                    if (temp.ToLower().Contains("playoffs") || temp.ToLower().Contains("ps"))
                        playoffs = true;

                    if (temp.Contains("-"))
                    {
                        firstYear = temp.Substring(0, temp.IndexOf("-"));
                        if (temp.Contains(" "))
                        {
                            temp = temp.Substring(temp.IndexOf("-") + 1);
                            secondYear = temp.Substring(0, temp.IndexOf(" "));
                        }
                        else
                        {
                            secondYear = temp.Substring(temp.IndexOf("-") + 1);
                        }
                    }
                    else
                    {
                        if (temp.Contains(" "))
                            secondYear = temp.Substring(0, temp.IndexOf(" "));
                        else
                            secondYear = temp;

                    }

                    if (secondYear.Length == 2)
                    {
                        if (Convert.ToInt16(secondYear) < 80)
                            secondYear = "20" + secondYear;
                        else
                            secondYear = "19" + secondYear;
                    }

                    if (firstYear != string.Empty && firstYear.Length == 2)
                    {
                        if (Convert.ToInt16(firstYear) < 80)
                            firstYear = "20" + firstYear;
                        else
                            firstYear = "19" + firstYear;
                    }
                    else if (firstYear == string.Empty)
                    {
                        firstYear = string.Format("{0}", Convert.ToInt16(secondYear) - 1);
                    }

                    result = string.Format("{0}{1}{2}", firstYear, secondYear, playoffs ? "P" : "R");
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("Crash and burn");
            }


            return result;
        }

        static void Main(string[] args)
        {
            //Console.CancelKeyPress += new ConsoleCancelEventHandler(Console_CancelKeyPress);
            HandlerRoutine quitHandler = new HandlerRoutine(ConsoleCtrlCheck);
            GC.KeepAlive(quitHandler);
            SetConsoleCtrlHandler(quitHandler, true);            

            Program program = new Program();
            program.MonitorGames();
        }

        /*
        static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Program.SetRunning(false);
            Program.mainThread.Join();

            e.Cancel = true;
        }
        */

        public static System.Xml.XmlDocument DeserializeFromJson(string text)
        {
            System.Xml.XmlDocument document = null;

            if (text != null)
            {
                string copy = text.Trim();


                if (copy.Substring(copy.Length - 1, 1) != "}")
                {
                    //System.Console.WriteLine("malformed json text!");
                }
                else
                {
                    try
                    {
                        lock (lockObj)
                        {
                            /*
                            System.IO.TextWriter tr = new System.IO.StreamWriter("date.txt");
                            tr.WriteLine(text);
                            tr.WriteLine(String.Empty);
                            tr.WriteLine(String.Empty);
                            tr.WriteLine(String.Empty);
                            tr.Close();
                            */

                            document = (System.Xml.XmlDocument)Newtonsoft.Json.JavaScriptConvert.DeserializeXmlNode(text);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine("json exception: " + ex.Message);
                    }
                }
            }

            return document;
        }

        
            [DllImport("Kernel32")]
            public static extern bool SetConsoleCtrlHandler(HandlerRoutine Handler, bool Add);


            // A delegate type to be used as the handler routine
            // for SetConsoleCtrlHandler.
            public delegate bool HandlerRoutine(CtrlTypes CtrlType);

            // An enumerated type for the control messages
            // sent to the handler routine.
            public enum CtrlTypes
            {
                CTRL_C_EVENT = 0,
                CTRL_BREAK_EVENT,
                CTRL_CLOSE_EVENT,
                CTRL_LOGOFF_EVENT = 5,
                CTRL_SHUTDOWN_EVENT
            }

            private static bool ConsoleCtrlCheck(CtrlTypes ctrlType)
            {
                Program.SetRunning(false);
                Program.mainThread.Join();

                //System.Diagnostics.Process.GetCurrentProcess().Kill();

                return true;
            } 
        
    }           
}

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Net;
using System.Threading;
using System.Text.RegularExpressions;
using System.Xml;
using Newtonsoft.Json;

namespace NHLScoreBot
{
    public class NHLGameEvent
    {
        public enum EventType
        {
            EVENT_GOAL,
            EVENT_PENALTY,
            EVENT_OTHER,
            EVENT_NOTSTARTED,
            EVENT_GAMEOVER,
            EVENT_DEBUG
        }

        private string teamThatScored;

        public string TeamThatScored
        {
            get { return teamThatScored; }
        }

        private bool scoreChanged;
        

        public bool ScoreChanged
        {
            get { return scoreChanged; }
            set { scoreChanged = value; }
        }

        private int awayScore, homeScore;

        public int HomeScore
        {
            get { return homeScore; }
            set { homeScore = value; }
        }

        public int AwayScore
        {
            get { return awayScore; }
            set { awayScore = value; }
        }

        private string eventText;
        private string timeText;
        private Boolean updated;

        public Boolean Updated
        {
            get { return updated; }
            set { updated = value; }
        }

        public string TimeText
        {
            get { return timeText; }
            set { timeText = value; }
        }
        private EventType type;

        public EventType Type
        {
            get { return type; }
            set { type = value; }
        }

        public string EventText
        {
            get { return eventText; }
            set { eventText = value; }
        }        

        public NHLGameEvent(string _eventText, EventType eventType, string _timeText)
        {
            eventText = _eventText;
            type = eventType;
            timeText = _timeText;

            if (eventType == EventType.EVENT_GOAL)
                teamThatScored = eventText.Substring("Goal: ".Length, 3);
        }
    }

    /*
    public class NHLTimeEvent
    {
        private string period;

        public string Period
        {
            get { return period; }
            set { period = value; }
        }
        private string time;

        public string Time
        {
            get { return time; }
            set { time = value; }
        }

        public NHLTimeEvent(string _period, string _time)
        {
            period = _period;
            time = _time;
        }
    }
    */

    public enum NHLGameStatus
    {
        GAME_NODATA,
        GAME_PREVIEWDATA,
        GAME_NOTSTARTED,
        GAME_PLAYING,
        GAME_FINAL
    }

    public interface INHLGameInfo
    {
        NHLGameEvent GetNewEvent();
        void StartMonitoring(NHLStats nhlStats);
        void StopMonitoring();
        string HomeTeamName { get; }
        string HomeTeamCity { get; }
        string AwayTeamName { get; }
        string AwayTeamCity { get; }
        int HomeShots { get; }
        int AwayShots { get; }
        string GetScoreString(bool showTime);
        string GetScoreString(NHLGameEvent gameEvent, bool showTime);
        NHLGameStatus GetStatus();
        string GetStartTime();
        bool HasTeam(string s);
        bool Overtime { get; }
        NHLGameStats NhlGameStats { get; }
        NHLStats NhlSeasonStats { get; }
        void Shutdown();
        string GetWinnerTeamName();
        string GetLoserTeamName();
        bool WasThreePointGame();
        void AddDebugEvent();
    }

    public class NHLGame : INHLGameInfo
    {
        public Guid guid;
        public NHLDotComFetch nhlFetch;
        private Object mutex;
        bool running = true;
        int stopMonitoringStatsDelay = 0;

        public void Shutdown()
        {
            lock (mutex)
            {
                running = false;
                if (nhlGameStats == null)
                {
                    //it will be sleeping for a long time
                    thread.Abort();
                }
            }
        }

        private bool IsRunning()
        {
            lock (mutex)
            {
                return running;
            }            
        }


        Thread thread;

        const int GAMETIME_UPDATE_DELAY = 1000;
        const int SLEEPY_UPDATE_DELAY = 60000;
        const int STOP_MONITORING_STATS_DELAY = 60 * 10;


        int gameId;

        public int GameId
        {
            get 
            {
                int result;
                lock (mutex)
                {
                    result = gameId;
                }

                return result;            
            }

        }
        
        //int homeTeamId;
        int homeScore;
        int homeShots;

        public int HomeShots
        {
            get 
            {
                int result;
                lock (mutex)
                {
                    result = homeShots;
                }
                return result; 
            }
        }

        private string homeCityShort;

        public string HomeCityShort
        {
            get { return homeCityShort; }
            set { homeCityShort = value; }
        }

        private string homeTeamName;
        private string homeTeamCityShort;

        public string HomeTeamCityShort
        {
            get { return homeTeamCityShort; }
            set { homeTeamCityShort = value; }
        }
        private string homeTeamCity;

        public string HomeTeamCity
        {
            get 
            {
                string result;
                lock (mutex)
                {
                    result = homeTeamCity;
                }       
                return result; 
            }
        }

        public string HomeTeamName
        {
            get { return homeTeamName; }
            set { homeTeamName = value; }
        }

        //int awayTeamId;
        private int awayScore;
        private int awayShots;

        public int AwayShots
        {
            get 
            {
                int result;
                lock (mutex)
                {
                    result = awayShots;
                }
                return result; 
            }
        }

        private string awayTeamCityShort;

        public string AwayTeamCityShort
        {
            get { return awayTeamCityShort; }
            set { awayTeamCityShort = value; }
        }
        private string awayTeamName;
        private string awayTeamCity;

        public string AwayTeamCity
        {
            get 
            {
                string result;
                lock (mutex)
                {
                    result = awayTeamCity;
                }
                return result; 
            }
        }

        string period;
        string time;

        public string Time
        {
            get { return time; }
            set { time = value; }
        }
        bool notStarted;
        //bool intermission;
        bool final;
        bool shootout;
        bool overtime;

        public bool Overtime
        {
            get { return overtime; }
        }

        public string AwayTeamName
        {
            get 
            {
                string result;
                lock (mutex)
                {
                    result = awayTeamName;
                }
                return result; 
            }
        }

        NHLGameStats nhlGameStats;
        NHLStats nhlSeasonStats;

        public NHLStats NhlSeasonStats
        {
            get { return nhlSeasonStats; }
        }

        public NHLGameStats NhlGameStats
        {
            get { return nhlGameStats; }
        }

        NHLGameEvent lastGoal, originalLastGoal;

        Queue<NHLGameEvent> newEvents;
        List<NHLGameEvent> oldEvents;

        bool goalDebug;
        
        public NHLGame(int _gameId)
        {
            guid = System.Guid.NewGuid();
            gameId = _gameId;
            homeScore = 0;
            homeShots = 0;
            awayScore = 0;
            awayShots = 0;

            period = String.Empty;
            //intermission = false;
            final = false;
            time = String.Empty;
            shootout = false;
            notStarted = true;
            overtime = false;

            mutex = new Object();
            thread = new Thread(new ParameterizedThreadStart(LoopThread));

            lastGoal = null;
            originalLastGoal = null;
            newEvents = new Queue<NHLGameEvent>();
            oldEvents = new List<NHLGameEvent>();

            nhlGameStats = null;
            nhlSeasonStats = null;

            goalDebug = false;
        }

        public bool HasTeam(string s)
        {
            bool result;

            s = s.ToLower();
            result = (HomeTeamName.ToLower().CompareTo(s) == 0) || (AwayTeamName.ToLower().CompareTo(s) == 0);

            return result;
        }

        public NHLGameStatus GetStatus()
        {
            NHLGameStatus status;

            lock (mutex)
            {

                if (final)
                    status = NHLGameStatus.GAME_FINAL;
                else if (period.Length > 0)
                    status = NHLGameStatus.GAME_PLAYING;
                else if (period.Length == 0 && notStarted &&
                    homeTeamCity != null && awayTeamCity != null)
                    status = NHLGameStatus.GAME_NOTSTARTED;
                else if (homeTeamName != null && awayTeamName != null)
                    status = NHLGameStatus.GAME_PREVIEWDATA;
                else
                    status = NHLGameStatus.GAME_NODATA;
            }

            return status;
        }

        public string GetStartTime()
        {

            string result = "Starting soon";

            /*
            LockMutex();

            if (notStarted)
            {
                if (jacked != null && jacked.ArenaName != null)
                    result = String.Format("{0} ({1})", time, jacked.ArenaName);
                else
                    result = time;
            }
            else
                result = String.Empty;

            UnlockMutex();
            */

            return result;
        }

        public NHLGameEvent GetNewEvent()
        {
            NHLGameEvent gameEvent = null;

            lock (mutex)
            {                
                if (newEvents.Count > 0)
                {
                    gameEvent = newEvents.Dequeue();
                    oldEvents.Add(gameEvent);
                    goalDebug = false;
                }
                else if (goalDebug)
                {
                    System.Console.WriteLine("GOAL DEBUGFFFFFFF");
                    goalDebug = false;
                }
            }

            return gameEvent;
        }

        public void StartMonitoring(NHLStats _nhlStats)
        {
            nhlSeasonStats = _nhlStats;
            thread.Start(this);           
        }

        public void StopMonitoring()
        {
            thread.Abort();            
        }

        public string GetWinnerTeamName()
        {
            string winnerTeamName = string.Empty; ;

            if (homeScore > awayScore)
                winnerTeamName = HomeTeamName;
            else if (awayScore > homeScore)
                winnerTeamName = AwayTeamName;
            else
                winnerTeamName = string.Empty;

            return winnerTeamName;
        }

        public string GetLoserTeamName()
        {
            string loserTeamName = string.Empty; ;

            if (homeScore > awayScore)
                loserTeamName = AwayTeamName;
            else if (awayScore > homeScore)
                loserTeamName = HomeTeamName;
            else
                System.Diagnostics.Debug.Assert(false);

            return loserTeamName;
        }

        public bool WasThreePointGame()
        {
            return (shootout || overtime);
        }

        public string GetScoreString(bool showTime)
        {
            return GetScoreString(homeScore, awayScore, showTime);
        }

        public string GetScoreString(NHLGameEvent gameEvent, bool showTime)
        {
            return GetScoreString(gameEvent.HomeScore, gameEvent.AwayScore, showTime);
        }

        private string GetScoreString(int _homeScore, int _awayScore, bool showTime)
        {
            string buf;
            string suffix;

            if (_homeScore > _awayScore)
                buf = String.Format("{0}-{1} {2}",
                    _homeScore, _awayScore, homeTeamName);

            else if (_homeScore < _awayScore)
                buf = String.Format("{0}-{1} {2}",
                    _awayScore, _homeScore, awayTeamName);
            else
                buf = String.Format("{0}-{1} tie", _awayScore, _homeScore);

            if (showTime)
            {
                if (final)
                {
                    String extraTime = String.Empty;
                    if (shootout)
                        extraTime = ", SO";
                    else if (overtime)
                        extraTime = ", OT";

                    buf = String.Format("{0} (Final{1})", buf, extraTime);
                }
                else if (period.Length > 0)
                {
                    if (period == "1")
                        suffix = "st";
                    else if (period == "2")
                        suffix = "nd";
                    else if (period == "3")
                        suffix = "rd";
                    else
                        suffix = string.Empty;

                    buf = String.Format("{0} ({1}{2}{3})", buf,
                        period.Length > 3 ? String.Empty : ParsePeriodTimeString(Convert.ToInt32(time)) + " ",
                        period, suffix);
                }
            }

            return buf;
        }


        public bool EventExists(NHLGameEvent newEvent)
        {
            bool exists = false;

            lock (mutex)
            {
                foreach (NHLGameEvent gameEvent in newEvents)
                {
                    if ((newEvent.Type == gameEvent.Type) && (gameEvent.Type == NHLGameEvent.EventType.EVENT_GOAL))
                    {
                        if (newEvent.AwayScore == gameEvent.AwayScore && newEvent.HomeScore == gameEvent.HomeScore)
                            exists = true;
                    }
                    else if (newEvent.EventText.CompareTo(gameEvent.EventText) == 0)
                        exists = true;
                }

                foreach (NHLGameEvent gameEvent in oldEvents)
                {
                    if ((newEvent.TimeText == gameEvent.TimeText && newEvent.Type == gameEvent.Type) ||
                        (newEvent.Type == gameEvent.Type) && (gameEvent.Type == NHLGameEvent.EventType.EVENT_GOAL) &&
                        newEvent.AwayScore == gameEvent.AwayScore && newEvent.HomeScore == gameEvent.HomeScore)
                    {
                        exists = true;
                    }
                    else if (newEvent.EventText.CompareTo(gameEvent.EventText) == 0)
                        exists = true;
                }

            }

            return exists;
        }

        public void AddDebugEvent()
        {
            AddEvent(new NHLGameEvent(string.Empty, NHLGameEvent.EventType.EVENT_DEBUG, string.Empty));
        }

        public void AddEvent(NHLGameEvent newEvent)
        {
            lock (mutex)
            {
                if (newEvent.Type == NHLGameEvent.EventType.EVENT_GOAL ||
                    newEvent.Type == NHLGameEvent.EventType.EVENT_DEBUG)
                {
                    System.Console.WriteLine("Adding goal event (" + guid + ")");
                    goalDebug = true;
                }

                newEvents.Enqueue(newEvent);
            }
        }

        public static string FixCase(string text)
        {
            char[] s = text.ToLower().ToCharArray();

            if (s.Length > 0)
            {

                s[0] = (char)(s[0] - 32);

                int i = text.IndexOf(' ');

                if (i > 0)
                    s[i + 1] = (char)(s[i + 1] - 32);
            }

            return new string(s);
        }

        NHLGameEvent ParseNHLDotComScoreString(string score)
        {
            NHLGameEvent newEvent = null;            

            String eventText, timeText;
            NHLGameEvent.EventType type = NHLGameEvent.EventType.EVENT_OTHER;
            string []elements = score.Split('|');
            int newHomeScore = -1, newAwayScore = -1;
            bool scoreChanged = false;

            if (elements.Length >= 22)
            {
                awayTeamName = FixCase(elements[5]);
                awayTeamCityShort = elements[3];
                awayTeamCity = FixCase(elements[4]);
                if (elements[7].Length > 0)
                {
                    newAwayScore = Convert.ToInt32(elements[7]);
                    if (newAwayScore != awayScore)
                    {
                        scoreChanged = true;
                        awayScore = newAwayScore;
                    }
                }
                if (elements[6].Length > 0)
                    awayShots = Convert.ToInt32(elements[6]);

                homeTeamName = FixCase(elements[15]);
                homeTeamCityShort = elements[13];
                homeTeamCity = FixCase(elements[14]);
                if (elements[17].Length > 0)
                {
                    newHomeScore = Convert.ToInt32(elements[17]);
                    if (newHomeScore != homeScore)
                    {
                        scoreChanged = true;
                        homeScore = newHomeScore;
                    }                    
                }
                if (elements[16].Length > 0)
                    homeShots = Convert.ToInt32(elements[16]);

                if (elements[1].CompareTo("SO") == 0)
                {
                    shootout = true;
                    if (elements[21].Length > 0 && elements[11].Length > 0)
                    {
                        int homeSOgoals = Convert.ToInt32(elements[21].Substring(0, 1));
                        int awaySOgoals = Convert.ToInt32(elements[11].Substring(0, 1));

                        /* Handle inconsistencey with nhl.com */
                        if (homeScore == awayScore && final)
                        {
                            if (homeSOgoals > awaySOgoals)
                                homeScore++;
                            else if (awaySOgoals > homeSOgoals)
                                awayScore++;
                        }
                    }
                }
                else if (elements[21].Length > 0 && elements[11].Length > 0)
                {
                    //Then it must have been/is only OT
                    overtime = true;
                }

                if (elements.Length >= 23)
                {
                    eventText = elements[22];

                    int s, e;
                    s = eventText.IndexOf('(');
                    e = eventText.IndexOf(')');

                    if (s != -1 && e != -1)
                    {
                        timeText = eventText.Substring(s).Substring(1, e - s - 1);
                        if (eventText.StartsWith("Goal:") || eventText.StartsWith("Empty Net Goal"))
                            type = NHLGameEvent.EventType.EVENT_GOAL;
                        else if (eventText.StartsWith("Penalty:"))
                            type = NHLGameEvent.EventType.EVENT_PENALTY;

                        newEvent = new NHLGameEvent(eventText, type, timeText);
                        newEvent.HomeScore = homeScore;
                        newEvent.AwayScore = awayScore;
                        newEvent.ScoreChanged = scoreChanged;
                    }
                }
            }
   
            return newEvent;
        }


        void ParseNHLDotComTimeString(string s)
        {
            string[] elements = s.Split('|');

            if (elements.Length > 1)
            {
                if (elements[1].Length > 0)
                {
                    if (elements[1].CompareTo(time) != 0)
                    {
                        if (lastGoal != null && lastGoal.EventText.CompareTo(originalLastGoal.EventText) != 0)
                            AddEvent(lastGoal);
                        lastGoal = null;
                        originalLastGoal = null;
                    }

                    //time = elements[1];
                    if (elements[1].Contains("PM") || elements[1].Contains("AM"))
                        notStarted = true;
                    else
                    {
                        notStarted = false;
                        time = elements[1];
                    }
                }

                if (elements[2].Length > 0)
                    period = elements[2];

                if (time.CompareTo("Final") == 0)
                    final = true;
                if (!final && String.IsNullOrEmpty(time))
                {
                    //Console.WriteLine("Time string from NHL.com is not correct, got NULL value for game (home team city or id): " + (HomeTeamCityShort == null ? Convert.ToString(gameId) : HomeTeamCityShort));
                    //final = true;
                }
            }
        }

        void ParseNHLDotComePreviewPage(string s)
        {
            string startTime = String.Empty;
            string date;
            int startIndex;
            int timeLength;
            string temp;
            
            startIndex = s.IndexOf("<div id=\"time\">");
            if (startIndex > 0)
            {
                timeLength = s.Substring(startIndex).IndexOf("</div>");
                if (timeLength < 60)
                {
                    date = s.Substring(startIndex, timeLength);
                    temp = date.Substring(date.LastIndexOf(", ") + 2);
                    startTime = temp.Substring(0, temp.IndexOf("EDT") + 3);
                }

                Time = startTime;
            }

            int descriptionLength;
            string previewText;            
            
            startIndex = s.IndexOf("<div id=\"desc\">");
            if (startIndex > 0)
            {
                descriptionLength = s.Substring(startIndex).IndexOf("</div>");
                if (descriptionLength < 60)
                {
                    previewText = s.Substring(startIndex, descriptionLength);
                    temp = previewText.Substring(0, previewText.IndexOf("-"));
                    awayTeamName = temp.Substring(temp.LastIndexOf("\t") + 1);
                    temp = previewText.Substring(previewText.IndexOf("-") + 1);
                    homeTeamName = temp.Substring(0, temp.IndexOf(" Preview"));
                }
            }
        }
        
        private string ParsePeriodTimeString(int time)
        {
            float seconds = (float)time;

            string mins = (seconds / 60) < 10 ? "0" + 
                Convert.ToString((int)(seconds / 60)) : Convert.ToString((int)(seconds / 60));
            string secs = seconds % 60 < 10 ? "0" + 
                Convert.ToString((int)(seconds % 60)) : Convert.ToString((int)(seconds % 60));

            return mins + ":" + secs;
        }

        private void HandleGameEvent(NHLGameEvent gameEvent)
        {
            if (gameEvent != null && gameEvent.Type == NHLGameEvent.EventType.EVENT_GOAL)
            {
                if (!EventExists(gameEvent))
                    System.Console.WriteLine("Goal: {0}", gameEvent.EventText);

                if (originalLastGoal == null)
                {
                    if (!EventExists(gameEvent))
                    {
                        if (!gameEvent.ScoreChanged)
                        {
                            if (gameEvent.TeamThatScored.CompareTo(HomeTeamCityShort) == 0)
                                gameEvent.HomeScore++;
                            else if (gameEvent.TeamThatScored.CompareTo(AwayTeamCityShort) == 0)
                                gameEvent.AwayScore++;
                            else
                                Console.WriteLine("Couldn't find team that scored.");
                        }

                        AddEvent(gameEvent);
                        originalLastGoal = gameEvent;
                    }
                }
                else
                {
                    if (gameEvent.ScoreChanged)
                        System.Console.WriteLine("DISALLOWED GOAL?");

                    lastGoal = gameEvent;
                    lastGoal.Updated = true;
                }
            }
            else if (gameEvent != null)
            {
                bool exists = EventExists(gameEvent);
                if (exists)
                    gameEvent.Updated = true;
                else
                    AddEvent(gameEvent);
            }
        }

        static void LoopThread(object _game)
        {
            NHLGame game = (NHLGame)_game;
            String timeString, scoreString;
            bool wasOver;

            game.nhlFetch = new NHLDotComFetch();
            NHLGameEvent gameEvent;

            for (; ; )
            {                   
                timeString = game.nhlFetch.FetchGameTimeString(game.GameId);
                scoreString = game.nhlFetch.FetchGameScoreString(game.GameId);
                int sleepTime = SLEEPY_UPDATE_DELAY;
                
                lock (game.mutex)
                {
                    if (game.stopMonitoringStatsDelay > 0)
                    {
                        game.stopMonitoringStatsDelay--;
                        if (game.stopMonitoringStatsDelay == 0)
                        {
                            System.Console.WriteLine("Stopped monitoring stats for: " + game.HomeTeamName + "/" + game.AwayTeamName);
                            game.nhlGameStats.StopMonitoring();
                            break;
                        }
                        
                    }
                    else if (scoreString != null && timeString != null)
                    {
                        wasOver = game.final;
                        game.ParseNHLDotComTimeString(timeString);
                        gameEvent = game.ParseNHLDotComScoreString(scoreString);

                        if (game.nhlGameStats == null)
                            game.nhlGameStats = new NHLGameStats(game.NhlSeasonStats, game.GameId, game.HomeTeamName, game.AwayTeamName, false);

                        if (game.final && !wasOver)
                        {
                            game.ParseNHLDotComScoreString(scoreString);
                            gameEvent = new NHLGameEvent("Game Over.", NHLGameEvent.EventType.EVENT_GAMEOVER, String.Empty);
                            game.AddEvent(gameEvent);
                            System.Console.WriteLine("Game over: " + game.HomeTeamName + "/" + game.AwayTeamName);
                            game.stopMonitoringStatsDelay = STOP_MONITORING_STATS_DELAY;
                        }
                        else
                        {
                            game.HandleGameEvent(gameEvent);
                        }
                    }

                    NHLGameStatus status = game.GetStatus();                    

                    if (status == NHLGameStatus.GAME_NODATA || status == NHLGameStatus.GAME_PREVIEWDATA)
                        sleepTime = SLEEPY_UPDATE_DELAY;
                    else
                        sleepTime = GAMETIME_UPDATE_DELAY;                    
                }

                if (game.nhlGameStats != null)
                {
                    // an attempt to get quicker goal events
                    NHLGameEvent lastGoalEventFromStats = game.nhlGameStats.getAndClearLastGoalEvent();
                    if (lastGoalEventFromStats != null)
                    {
                        game.HandleGameEvent(lastGoalEventFromStats);
                    }
                }

                Thread.Sleep(sleepTime);

                if (!game.IsRunning())
                {
                    if (game.nhlGameStats != null)
                        game.nhlGameStats.StopMonitoring();
                    break;
                }
            }

        }

    }

    public class NHLDotComFetch
    {
        // TODO: put in config file!!!!!!
        const string year = "2016";
        const string season = "20162017";

        const string scorePath = "http://live.nhl.com/data/scr";        
        //const string scorePath = "http://debian3800/otto/nhl/scr";
        const string clockPath = "http://live.nhl.com/data/clk";
        //const string clockPath = "http://debian3800/otto/nhl/clk";
        //const string playersStatsPath = "http://www.nhl.com/superstats/rdl_files/20092010/ptbyteam/rs_";
        //const string previewPath = "http://www.nhl.com/nhl/app?service=page&page=Preview&gameNumber=";
        //const string previewPathSuffix = "&season=20092010&gameType=3";
        //const int seasonIdentity = 20000;
        //const string gameScoreboardPath = "http://live.nhl.com/GameData/Scoreboard.jsonp?loadScoreboard";
		const string gameScoreboardPath = "http://live.nhl.com/GameData/Scoreboard.json?loadScoreboard";

        const string aliasPath = "http://sportsargumentwiki.com/index.php?title=Nhlfeed_Aliases";

        public string FetchWikiAliasString()
        {
            return GetPageString(aliasPath);
        }

        public string FetchGameScoreString(int gameId)
        {
            string url = scorePath + gameId + ".txt";
            string page = GetPageString(url);

            return page;                        
        }        

        public string FetchGameTimeString(int gameId)
        {
            string url = clockPath + gameId + ".txt";
            string page = GetPageString(url);

            return page;   
        }

        public List<int> FetchGameIndex(DateTime targetDate)
        {
            List<int> gameIds = new List<int>();
   
            String jsonText = GetScoreboardString();
            //XmlDocument document = (XmlDocument)JavaScriptConvert.DeserializeXmlNode(jsonText);
            XmlDocument document = Program.DeserializeFromJson(jsonText);

            XmlNodeList nodes;

            if (document != null)
            {
                nodes = document.SelectNodes("//loadScoreBoard/games");
                foreach (XmlNode node in nodes)
                {
                    string isToday = node.SelectNodes("isToday")[0].InnerXml;
                    string longGameId = node.SelectNodes("id")[0].InnerXml;
                    string gameId = longGameId.Substring(5); // {year}0{id} is the pattern, want substring from 5 to the end
                    string stringDate = node.SelectNodes("longStartTime")[0].InnerText;

                    DateTime date = DateTime.Parse(stringDate, new System.Globalization.CultureInfo("en-US"));

                    if (date.Day == targetDate.Day &&
                        date.Month == targetDate.Month &&
                        date.Year == targetDate.Year)
                    {
                        gameIds.Add(Convert.ToInt32(gameId));
                    }
                }
            }

            return gameIds;               
        }

        public static string GetGameStatsString(int gameId, int timeIndex, bool homeTeam)
        {
            string url = String.Format("http://live.nhl.com/GameData/{0}/{1}0{2}/{3}Roster.json?ts={4}",
                                        season, year, gameId, homeTeam ? "Home" : "Away", timeIndex);

            return GetPageString(url);
        }

        public static string GetScoreboardString()
        {
            string text = GetPageString(gameScoreboardPath);

			if (text != null)
            {
                //text = text.Substring("loadScoreboard(".Length);
                text = text.TrimEnd('\r');
                text = text.TrimEnd('\n');
                text = text.TrimEnd(')');
                text = "{\"loadScoreBoard\":" + text + "}";
            }

            return text;
        }

        public static string GetPlayByPlayString(int gameId, int timeIndex)
        {
            string text = GetPageString(String.Format("http://live.nhl.com/GameData/{0}/{1}0{2}/PlayByPlay.json?ts={3}",
                                                      season, year, gameId, timeIndex));

            return text;
        }

        public static string GetPageString(string url)
        {
            StringBuilder sb = new StringBuilder();
            byte[] buf = new byte[8192];
            string result = null;
            bool pageNotFound = false;
            HttpWebResponse response = null;

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                try
                {
                    response = (HttpWebResponse)request.GetResponse();
                }
                catch (System.Net.WebException)
                {
                    pageNotFound = true;
                }

                if (!pageNotFound)
                {

                    // we will read data via the response stream
                    Stream resStream = response.GetResponseStream();

                    string tempString = null;
                    int count = 0;

                    do
                    {
                        // fill the buffer with data
                        try
                        {
                            count = resStream.Read(buf, 0, buf.Length);
                        }
                        catch (Exception ex)
                        {
                            System.Console.WriteLine(ex.StackTrace);
                            break;
                        }

                        // make sure we read some data
                        if (count != 0)
                        {
                            // translate from bytes to ASCII text
                            tempString = Encoding.ASCII.GetString(buf, 0, count);

                            // continue building the string
                            sb.Append(tempString);
                        }
                    }
                    while (count > 0); // any more data to read?

                    result = sb.ToString();
                }

                if (result == string.Empty)
                    result = null;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine(ex.StackTrace);
            }

            return result;
        }
    }
}


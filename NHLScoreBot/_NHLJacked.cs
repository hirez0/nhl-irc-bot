using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Xml;
using System.Data;

namespace NHLScoreBot
{
    public class __NHLJacked
    {
        const int SLEEP_NEXTDATA_DELAY = 5000;
        const int SLEEP_ITERATION_DELAY = 100;

        public JackedStatsSet stats;
        Thread thread;
        string homeTeamName, awayTeamName;
        int? gameId;
        string arenaName;

        public string ArenaName
        {
            get { return arenaName; }
        }
        Object mutex;
        bool monitorStats;
        NHLStats nhlStats;        

        public NHLStats NhlStats
        {
            get { return nhlStats; }
        }
        
        public bool MonitorStats
        {
            get 
            {
                bool result;
                lock (mutex)
                {
                    result = monitorStats;
                }

                return result;
            }
        }        

        public const string gameListPath = "http://sports.jacked.com/jacked/dashboard/tab/getHomeTab.do?";
        //const string gameListPath = "http://debian3800/otto/nhl/jacked/eventlist.xml";
        public const string playerListPath = "http://sports.jacked.com/jacked/dashboard/tab/gameData.do?EVENT_ID=";        
        //const string playerListPath = "http://debian3800/otto/nhl/jacked/playerlist.xml";


        public __NHLJacked(string _homeTeam, string _awayTeam, NHLStats _nhlStats)
        {
            homeTeamName = _homeTeam.ToLower();
            awayTeamName = _awayTeam.ToLower();
            stats = new JackedStatsSet();
            thread = new Thread(new ParameterizedThreadStart(LoopThread));
            gameId = null;
            monitorStats = true;
            mutex = new Object();
            nhlStats = _nhlStats;
            arenaName = null;

            thread.Start(this);
            
        }

        private bool FindGameId()
        {
            NHLDotComFetch fetch = new NHLDotComFetch();
            String xml = NHLDotComFetch.GetPageString(gameListPath);

            if (xml != null && xml.IndexOf("<?xml") >= 0)
            {
                xml = xml.Substring(xml.IndexOf("<?xml"));

                XmlDocument document = new XmlDocument();
                document.LoadXml(xml);

                XmlNodeList nodes = document.SelectNodes("//home-tab/schedule/category[@type=\"NHL\"]/game");
                foreach (XmlNode node in nodes)
                {
                    string time = node.Attributes["date"].Value;
                    DateTime date = DateTime.Parse(time);

                    if ((date.Year == DateTime.Now.Year && date.Month == DateTime.Now.Month && date.Day == DateTime.Now.Day))
                    {
                        XmlNode homeTeam = node.SelectNodes("home-team")[0];
                        XmlNode awayTeam = node.SelectNodes("visiting-team")[0];

                        if (homeTeam.Attributes["name"].Value.ToLower().CompareTo(homeTeamName) == 0 &&
                            awayTeam.Attributes["name"].Value.ToLower().CompareTo(awayTeamName) == 0)
                        {
                            gameId = Convert.ToInt32(node.Attributes["eventId"].Value);
                            break;
                        }
                    }
                }
            }
            else
                System.Console.WriteLine("Couldn't find game id");

            return gameId != null;
        }

        private void ParsePlayerList()
        {
            XmlDocument document = new XmlDocument();
            try
            {
                document.Load(String.Format("{0}{1}", playerListPath, gameId));
                XmlNodeList nodes;

                nodes = document.SelectNodes("//response/game/venue");
                arenaName = nodes[0].Attributes["stadium"].Value;

                stats.Players.Clear();
                nodes = document.SelectNodes("//response/game/team/player-list/player");
                foreach (XmlNode node in nodes)
                {
                    JackedStatsSet.PlayersRow row = stats.Players.NewPlayersRow();
                    row.Name = node.Attributes["first-name"].Value + " " + node.Attributes["last-name"].Value;
                    row.JackedPlayer_ID = Convert.ToInt32(node.Attributes["id"].Value);

                    XmlNode extra = node.SelectNodes("attribute")[0];
                    row.Position = extra.Attributes["position"].Value;
                    row.Number = extra.Attributes["number"].Value;

                    lock (stats)
                    {
                        stats.Players.AddPlayersRow(row);
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }

        private int ParseStatsList(int dataIndex)
        {
            int dataTotalSize, dataEndIndex = 0;

            XmlDocument document = new XmlDocument();
            try
            {
                document.Load(String.Format("http://sports.jacked.com/jacked/widget/getWidgetData.do?WIDGET_ID=85" +
                    "&USER_WIDGET_ID=219564&EVENT_ID={0}&START_TIME=0&DATA_START_INDEX={1}", gameId, dataIndex));

                //document.Load("http://debian3800/otto/nhl/jacked/playerstats.xml");


                XmlNodeList nodes;

                nodes = document.SelectNodes("//response/paging-info");
                if (nodes.Count > 0)
                {
                    dataTotalSize = Convert.ToInt32(nodes[0].Attributes["total-size"].Value);
                    dataEndIndex = Convert.ToInt32(nodes[0].Attributes["end-index"].Value);


                    nodes = document.SelectNodes("//response/player-stats/*/player");
                    foreach (XmlNode node in nodes)
                    {
                        JackedStatsSet.PlayerStatsRow row = stats.PlayerStats.NewPlayerStatsRow();
                        row.JackedPlayer_ID = Convert.ToInt32(node.Attributes["id"].Value);

                        JackedStatsSet.PlayersRow parentRow = stats.Players.FindByJackedPlayer_ID(row.JackedPlayer_ID);
                        row.Goals = Convert.ToInt32(node.Attributes["goals"].Value);
                        row.Assists = Convert.ToInt32(node.Attributes["assists"].Value);
                        row.SOG = Convert.ToInt32(node.Attributes["shots-on-goal"].Value);
                        row.Hits = Convert.ToInt32(node.Attributes["hits"].Value);
                        row.PlusMinus = Convert.ToInt32(node.Attributes["plus-minus"].Value);
                        row.Penalties = Convert.ToInt32(node.Attributes["penalties"].Value);
                        row.PIM = Convert.ToInt32(node.Attributes["penalty-minutes"].Value);
                        row.FOW = Convert.ToInt32(node.Attributes["faceoffs-won"].Value);

                        if (parentRow != null && parentRow.Position.CompareTo("Goaltender") == 0)
                        {
                            if (node.Attributes["shots-faced"] != null)
                                row.ShotsFaced = Convert.ToInt32(node.Attributes["shots-faced"].Value);
                            if (node.Attributes["saves"] != null)
                                row.Saves = Convert.ToInt32(node.Attributes["saves"].Value);
                            if (node.Attributes["save-percent"] != null)
                                row.SavePercent = Convert.ToDecimal(node.Attributes["save-percent"].Value);
                        }

                        lock (stats)
                        {
                            JackedStatsSet.PlayerStatsRow deleteRow =
                                stats.PlayerStats.FindByJackedPlayer_ID(row.JackedPlayer_ID);

                            if (deleteRow != null)
                                stats.PlayerStats.RemovePlayerStatsRow(deleteRow);

                            stats.PlayerStats.AddPlayerStatsRow(row);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine(ex.Message);
                System.Console.WriteLine(ex.StackTrace);
            }

            return dataEndIndex;
        }
        
        private void GetInfoForStat(ref string playerName, ref int number, DataColumn column, bool desc)
        {
            JackedStatsSet.PlayerStatsRow []rows;
            rows = (JackedStatsSet.PlayerStatsRow[]) stats.PlayerStats.Select(
                String.Empty, String.Format("{0} {1}", column.ColumnName, desc ? "DESC" : "ASC"));

            foreach (JackedStatsSet.PlayerStatsRow row in rows)
            {
                JackedStatsSet.PlayersRow parentRow = 
                    ((JackedStatsSet.PlayersRow)row.GetParentRow("PlayerStats_Players"));

                if (parentRow != null)
                {
                    if (parentRow.Position.CompareTo("Goaltender") != 0)
                    {
                        number = (int)row[column];
                        playerName = parentRow.Name;
                        break;
                    }
                }
                else
                {
                    System.Console.WriteLine("ERROR: parentRow is null");
                }
            }            
        }

        public void StopMonitoring()
        {
            lock (mutex)
            {
                monitorStats = false;
                System.Console.WriteLine("Stopped monitoring {0}/{1}", homeTeamName, awayTeamName);
            }
        }

        public string GetGameStats()
        {
            string result;

            string topGoalsPlayer = String.Empty, topAssistsPlayer = String.Empty, topHitsPlayer = String.Empty,
                topPlusMinusPlayer = String.Empty, botPlusMinusPlayer = String.Empty, topPIMPlayer = String.Empty,
                topFOWPlayer = String.Empty, topSOGPlayer = String.Empty;
            int topGoals = 0, topAssists = 0, topHits = 0, topPlusMinus = 0, 
                botPlusMinus = 0, topPIM = 0, topFOW = 0, topSOG = 0;

            lock (stats)
            {
                GetInfoForStat(ref topGoalsPlayer, ref topGoals, stats.PlayerStats.GoalsColumn, true);
                GetInfoForStat(ref topAssistsPlayer, ref topAssists, stats.PlayerStats.AssistsColumn, true);
                GetInfoForStat(ref topHitsPlayer, ref topHits, stats.PlayerStats.HitsColumn, true);
                GetInfoForStat(ref topPlusMinusPlayer, ref topPlusMinus, stats.PlayerStats.PlusMinusColumn, true);
                GetInfoForStat(ref botPlusMinusPlayer, ref botPlusMinus, stats.PlayerStats.PlusMinusColumn, false);
                GetInfoForStat(ref topPIMPlayer, ref topPIM, stats.PlayerStats.PIMColumn, true);
                GetInfoForStat(ref topFOWPlayer, ref topFOW, stats.PlayerStats.FOWColumn, true);
                GetInfoForStat(ref topSOGPlayer, ref topSOG, stats.PlayerStats.SOGColumn, true);
            }

            result = String.Format("[{0}/{1}] [Most Goals: {2}] [Most Assists: {3}] [Most SOG: {9}] [Most Hits: {4}] " + 
                "[Top +/-: {5}] [Bot +/-: {6}] [Most PIM: {7}] [Most FOW: {8}]",
                NHLGame.FixCase(homeTeamName), NHLGame.FixCase(awayTeamName),
                topGoals == 0 ? "--" : String.Format("{0}, {1}", topGoalsPlayer, topGoals),
                topAssists == 0 ? "--" : String.Format("{0}, {1}", topAssistsPlayer, topAssists),
                topHits == 0 ? "--" : String.Format("{0}, {1}", topHitsPlayer, topHits),
                topPlusMinus == 0 ? "--" : String.Format("{0}, {1}", topPlusMinusPlayer, topPlusMinus),
                botPlusMinus == 0 ? "--" : String.Format("{0}, {1}", botPlusMinusPlayer, botPlusMinus),
                topPIM == 0 ? "--" : String.Format("{0}, {1}", topPIMPlayer, topPIM),
                topFOW == 0 ? "--" : String.Format("{0}, {1}", topFOWPlayer, topFOW), 
                topSOG == 0 ? "--" : String.Format("{0}, {1}", topSOGPlayer, topSOG));

            return result;
        }

        public string GetIndividualStats(string individual)
        {
            string result = null;
            if (nhlStats != null)
            {
                individual = nhlStats.GetGoalieNameAlias(individual);
                individual = nhlStats.GetPlayerNameAlias(individual);
            }

            JackedStatsSet.PlayerStatsRow statsRow;
            JackedStatsSet.PlayersRow[] rows = 
                (JackedStatsSet.PlayersRow[]) stats.Players.Select(String.Format("Name LIKE '%{0}%'", individual));
            if (rows.Length > 0)
            {
                if (rows[0].GetChildRows("PlayerStats_Players").Length > 0)
                {
                    statsRow = (JackedStatsSet.PlayerStatsRow)rows[0].GetChildRows("PlayerStats_Players")[0];
                    if (statsRow != null)
                    {
                        if (rows[0].Position.CompareTo("Goaltender") == 0)
                        {
                            if (!statsRow.IsSavePercentNull() && !statsRow.IsSavesNull() && !statsRow.IsShotsFacedNull())
                            {
                                result = string.Format("[{0}/{1}] {2} [Shots:{3}] [Saves:{4}] [Save %:{5}]",
                                    NHLGame.FixCase(homeTeamName), NHLGame.FixCase(awayTeamName), rows[0].Name,
                                    statsRow.ShotsFaced, statsRow.Saves, statsRow.SavePercent);
                            }
                            else
                            {
                                result = string.Format("No game stats available for {0}", rows[0].Name);
                            }
                        }
                        else
                        {
                            result = string.Format("[{0}/{1}] {2} [Goals:{3}] [Assists:{4}] [Shots: {9}] [Hits:{5}] " + 
                                "[+/-:{6}] [PIM:{7}] [FOW:{8}]", 
                                NHLGame.FixCase(homeTeamName), NHLGame.FixCase(awayTeamName),
                                rows[0].Name, statsRow.Goals, statsRow.Assists, statsRow.Hits, statsRow.PlusMinus,
                               statsRow.PIM, statsRow.FOW, statsRow.SOG);
                        }
                    }
                    else
                    {
                        result = string.Format("No game stats available yet for {0}", rows[0].Name);
                    }
                }
                else
                    result = string.Format("No game stats available yet for {0}", rows[0].Name);
            }

            return result;
        }


        static void LoopThread(object _jacked)
        {
            __NHLJacked jacked = (__NHLJacked)_jacked;

            bool foundInfo = false;
            int nextIndex = -1, dataIndex = 0;

            for (; ; )
            {
                if (!foundInfo || jacked.stats.Players.Rows.Count == 0)
                {
                    foundInfo = jacked.FindGameId();
                    System.Console.WriteLine(String.Format("Looking for game id..{0}/{1}", 
                        jacked.homeTeamName, jacked.awayTeamName));
                    if (foundInfo)
                    {
                        System.Console.WriteLine(
                            String.Format("Parsing {0}/{1} players", jacked.homeTeamName, jacked.awayTeamName));

                        if (jacked.stats.Players.Rows.Count == 0)
                            jacked.ParsePlayerList();

                        if (jacked.stats.Players.Rows.Count == 0)
                            Thread.Sleep(SLEEP_NEXTDATA_DELAY * 4);
                    }
                    else
                    {
                        if (!jacked.MonitorStats)
                            break;
                    }
                }

                if (foundInfo && jacked.stats.Players.Rows.Count > 0)
                {
                    /*
                    System.Console.WriteLine(
                        String.Format("Parsing {0}/{1} stats ({2}-{3})", 
                        jacked.homeTeamName, jacked.awayTeamName, jacked.gameId, dataIndex));
                    */
                    nextIndex = jacked.ParseStatsList(dataIndex);
                    if (nextIndex == dataIndex)
                    {
                        if (!jacked.MonitorStats)
                            break;
                        else
                            Thread.Sleep(SLEEP_NEXTDATA_DELAY);
                    }
                    else
                        dataIndex = nextIndex;
                }

                Thread.Sleep(SLEEP_ITERATION_DELAY);
            }
        }

    }
}

using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading;
using System.Xml;

using Newtonsoft.Json;

namespace NHLScoreBot
{
    public class NHLGameStats
    {
        const int SLEEP_NEXTDATA_DELAY = 30000;
        const int SLEEP_ITERATION_DELAY = 5000;

        JackedStatsSet stats;
        Thread thread;
        string homeTeamName, awayTeamName;
        int homeTeamId, awayTeamId;
        int gameId;
        XmlDocument playByPlay;
        bool playoffs;

        Object mutex;
        bool monitorStats;
        NHLStats nhlStats;

        NHLGameEvent lastGoalEvent;

        public NHLGameEvent getAndClearLastGoalEvent()
        {
            NHLGameEvent evt = null;
            lock (mutex)
            {
                evt = lastGoalEvent;
                lastGoalEvent = null;
            }
            return evt;
        }

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

        //const string playerListPath = "http://debian3800/otto/nhl/jacked/playerlist.xml";

        public NHLGameStats(NHLStats _nhlStats, int _gameId, string _homeTeamName, string _awayTeamName, bool _playoffs)
        {
            stats = new JackedStatsSet();
            thread = new Thread(new ParameterizedThreadStart(LoopThread));
            gameId = _gameId;
            monitorStats = true;
            mutex = new Object();
            nhlStats = _nhlStats;
            homeTeamName = _homeTeamName;
            awayTeamName = _awayTeamName;
            playByPlay = new XmlDocument();
            playoffs = _playoffs;
            lastGoalEvent = null;

            thread.Start(this);            
        }


        public bool ParsePlayerList(bool homeTeam)
        {
            bool succeed = false;

            try
            {
                string jsonText = NHLDotComFetch.GetGameStatsString(gameId, 0, homeTeam);

                XmlDocument document = Program.DeserializeFromJson(jsonText);
                XmlNodeList nodes;

                if (document != null)
                {
                    nodes = document.SelectNodes("//data/roster/*/player");
                    foreach (XmlNode node in nodes)
                    {
                        JackedStatsSet.PlayersRow row = stats.Players.NewPlayersRow();

                        row.Name = node.SelectNodes("name")[0].InnerText;
                        row.JackedPlayer_ID = Convert.ToInt32(node.SelectNodes("playerId")[0].InnerText);

                        if (row.JackedPlayer_ID == 0)
                        {
                            System.Console.WriteLine("ParsePlayerList: JackedPlayer_ID is 0");
                            continue;
                        }

                        row.Position = node.SelectNodes("pos")[0].InnerText.Trim();
                        row.Number = node.SelectNodes("num")[0].InnerText;

                        lock (stats)
                        {
                            stats.Players.AddPlayersRow(row);
                            succeed = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //throw new Exception("ParsePlayerList exception", ex);
                System.Console.WriteLine("ParsePlayerList exception" + ex.Message + "\n" + ex.StackTrace);
            }

            return succeed;
        }

        private bool IsIntegerNumeric(string val)
        {
            int result;
            return Int32.TryParse(val, out result);
        }

        private bool IsDecimal(string val)
        {
            Decimal result;
            return Decimal.TryParse(val, out result);
        }

        private int GetIntegerStat(string val)
        {
            int stat = 0;

            if (IsIntegerNumeric(val))
                stat = Convert.ToInt32(val);
 
            return stat;
        }

        public void CheckLastPlayByPlayEventForGoal()
        {
            lock (mutex)
            {
                if (lastGoalEvent != null)
                    return;

                //{"eventid":215,"period":1,"type":"Goal","localtime":"8:41 PM","time":"18:40","teamid":22,"video":
                //"2_60_mtl_edm_0910_discrete_EDM215_goal_800K_16x9.flv","pid":8467964,"ycoord":5,"xcoord":56,"playername":
                //"Mike Comrie","sweater":"91","desc":"Mike Comrie GOAL on Carey Price"}
                XmlNodeList nodes = playByPlay.SelectNodes("//data/game/plays/play");

                if (nodes.Count > 0)
                {
                    XmlNode node = nodes[nodes.Count - 1];

                    string type = node.SelectNodes("type")[0].InnerText;
                    string name = node.SelectNodes("playername")[0].InnerText;
                    string description = node.SelectNodes("desc")[0].InnerText;
                    string time = node.SelectNodes("time")[0].InnerText;
                    string period = node.SelectNodes("period")[0].InnerText;
                    string teamId = node.SelectNodes("teamid")[0].InnerText;

                    if (string.Compare(type, "Goal", true) == 0)
                    {
                        string teamShort;
                        string firstInitial, lastName;
                        string periodWithSuffix;

                        NHLTeam team = NHLStats.Instance.GetTeamFromTeamId(teamId);
                        if (team != null)
                        {
                            teamShort = team.TeamId;
                            firstInitial = name.Substring(0, 1);
                            lastName = name.Substring(name.IndexOf(' ') + 1);
                            periodWithSuffix = GetPeriodFormatted(period);

                            // Convert to description to Goal: MTL HAMRLIK,R. 65'  (16:38 3rd) or close
                            string fakedDescription = string.Format("Goal: {0} {1},{2}.  ({3} {4})", teamShort,
                                                                                                    firstInitial,
                                                                                                    lastName,
                                                                                                    time,
                                                                                                    periodWithSuffix);

                            NHLGameEvent goalEvent = new NHLGameEvent(fakedDescription, NHLGameEvent.EventType.EVENT_GOAL, time);
                            this.lastGoalEvent = goalEvent;

                            System.Console.WriteLine("Set detected goal event from PBP: " + description);
                        }
                        else
                        {
                            System.Console.WriteLine("Couldn't find team from team id " + teamId);
                        }
                    }
                }
            }
        }

        public void ParseStatsList(int timeIndex, bool homeTeam)
        {            
            try
            {
                string jsonText = NHLDotComFetch.GetGameStatsString(gameId, timeIndex, homeTeam);
                XmlDocument document = Program.DeserializeFromJson(jsonText);

                if (document != null)
                {
                    XmlNodeList nodes;

                    nodes = document.SelectNodes("//data/roster/*/player");
                    foreach (XmlNode node in nodes)
                    {
                        JackedStatsSet.PlayerStatsRow row = stats.PlayerStats.NewPlayerStatsRow();
                        row.JackedPlayer_ID = Convert.ToInt32(node.SelectNodes("playerId")[0].InnerText);

                        JackedStatsSet.PlayersRow parentRow = stats.Players.FindByJackedPlayer_ID(row.JackedPlayer_ID);

                        if (parentRow != null)
                        {
                            if (parentRow.Position.Trim() == "G")
                            {
                                //string savePercentText = node.SelectNodes("svp")[0].InnerText;
                                string savePercentText = "";
                                var list = node.SelectNodes("svp");
                                if (list.Count > 0)
                                    savePercentText = list[0].InnerText;

                                if (IsDecimal(savePercentText))
                                    row.SavePercent = Convert.ToDecimal(savePercentText);
                                else
                                    row.SetSavePercentNull();

                                parentRow.Scratch = false;
                            }
                            else
                            {
                                XmlNode playerNode = node.SelectNodes("onice")[0];
                                if (playerNode != null)
                                {
                                    string timeOnIceText = "";
                                    timeOnIceText = node.SelectNodes("toi")[0].InnerText;
                                    int timeOnIceInSeconds = 0;
                                    if (timeOnIceText != "")
                                    {
                                        timeOnIceInSeconds += Convert.ToInt32(timeOnIceText.Substring(0, timeOnIceText.IndexOf(':'))) * 60;
                                        timeOnIceInSeconds += Convert.ToInt32(timeOnIceText.Substring(timeOnIceText.IndexOf(':') + 1));
                                    }
                                    row.TOI = timeOnIceInSeconds;

                                    row.PlusMinus = GetIntegerStat(node.SelectNodes("pm")[0].InnerText);
                                    row.Goals = GetIntegerStat(node.SelectNodes("g")[0].InnerText);
                                    row.Assists = GetIntegerStat(node.SelectNodes("a")[0].InnerText);
                                    row.PIM = GetIntegerStat(node.SelectNodes("pim")[0].InnerText);
                                    parentRow.Scratch = false;
                                }
                                else
                                {
                                    parentRow.Scratch = true;
                                }
                            }
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
        }

        private void GetInfoForTeamStat(ref string teamName, ref int number, DataColumn column)
        {

        }
        
        private void GetInfoForStat(ref string playerName, ref int number, DataColumn column, bool desc)
        {
            JackedStatsSet.PlayerStatsRow []rows;
            rows = (JackedStatsSet.PlayerStatsRow[]) stats.PlayerStats.Select(
                String.Empty, String.Format("{0} {1}", column.ColumnName, desc ? "DESC" : "ASC"));

            foreach (JackedStatsSet.PlayerStatsRow row in rows)
            {
                if (row.IsTOINull())
                    continue;

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

            string topGoalsPlayer = String.Empty, topAssistsPlayer = String.Empty, topTOIPlayer = String.Empty, 
                   botTOIPlayer = String.Empty, topPlusMinusPlayer = String.Empty, botPlusMinusPlayer = String.Empty, 
                   topPIMPlayer = String.Empty;
            int topGoals = 0, topAssists = 0, topPlusMinus = 0, botPlusMinus = 0, topPIM = 0, topTOI = 0, botTOI = 0;

            lock (stats)
            {
                GetInfoForStat(ref topGoalsPlayer, ref topGoals, stats.PlayerStats.GoalsColumn, true);
                GetInfoForStat(ref topAssistsPlayer, ref topAssists, stats.PlayerStats.AssistsColumn, true);
                GetInfoForStat(ref topPlusMinusPlayer, ref topPlusMinus, stats.PlayerStats.PlusMinusColumn, true);
                GetInfoForStat(ref botPlusMinusPlayer, ref botPlusMinus, stats.PlayerStats.PlusMinusColumn, false);
                GetInfoForStat(ref topPIMPlayer, ref topPIM, stats.PlayerStats.PIMColumn, true);
                GetInfoForStat(ref topTOIPlayer, ref topTOI, stats.PlayerStats.TOIColumn, true);
                GetInfoForStat(ref botTOIPlayer, ref botTOI, stats.PlayerStats.TOIColumn, false);
            }

            result = String.Format("[{0}/{1}] [Most Goals: {2}] [Most Assists: {3}] " + 
                "[Top +/-: {4}] [Bot +/-: {5}] [Most PIM: {6}] [Most TOI: {7}] [Least TOI: {8}]",
                NHLGame.FixCase(homeTeamName), NHLGame.FixCase(awayTeamName),
                topGoals == 0 ? "--" : String.Format("{0}, {1}", topGoalsPlayer, topGoals),
                topAssists == 0 ? "--" : String.Format("{0}, {1}", topAssistsPlayer, topAssists),
                topPlusMinus == 0 ? "--" : String.Format("{0}, {1}", topPlusMinusPlayer, topPlusMinus),
                String.Format("{0}, {1}", botPlusMinusPlayer, botPlusMinus),
                String.Format("{0}, {1}", topPIMPlayer, topPIM),
                String.Format("{0}, {1}", topTOIPlayer, TOIConvert(topTOI)),
                String.Format("{0}, {1}", botTOIPlayer, TOIConvert(botTOI)));

            return result;
        }

        private string GetPeriodFormatted(string period)
        {
            string suffix = String.Empty;

            if (period == "1")
                suffix = "st";
            else if (period == "2")
                suffix = "nd";
            else if (period == "3")
                suffix = "rd";
            else
                period = "OT";

            return period + suffix;
        }

        public List<string> GetPlayerEvents(string playerName)
        {
            List<string> result = new List<string>();

            playerName = NHLStats.EscapeFilter(playerName);

            //if (System.Text.RegularExpressions.Regex.IsMatch(description.Substring(indexOfOn), string.Format("{0}.* {1}$", firstInitial, lastName)))
            //System.Text.
            // TODO



            JackedStatsSet.PlayersRow[] rows = 
                (JackedStatsSet.PlayersRow[]) stats.Players.Select(String.Format("Name LIKE '%{0}%'", playerName));
            if (rows.Length > 0)
            {
                string completeName = nhlStats.GetPlayerOrGoalieNameFromPartial(playerName);

                JackedStatsSet.PlayersRow player = rows[0];

                XmlNodeList nodes = playByPlay.SelectNodes("//data/game/plays/play");

                foreach (XmlNode node in nodes)
                {
                    string type = node.SelectNodes("type")[0].InnerText;
                    string name = node.SelectNodes("playername")[0].InnerText;
                    string description = node.SelectNodes("desc")[0].InnerText;
                    string time = node.SelectNodes("time")[0].InnerText;
                    string period = node.SelectNodes("period")[0].InnerText;
                    string entry = description + " at " + time + " of " + GetPeriodFormatted(period);

                    if /*(String.Compare(name, player.Name, true) != 0 &&*/ (!description.ToLower().Contains(completeName.ToLower()))
                        continue;
                    //if (!System.Text.RegularExpressions.Regex.IsMatch(description.Substring(indexOfOn), string.Format("{0}.* {1}$", firstInitial, lastName)))
                    //{
                    //    continue;
                    //}

                    if (!playoffs && period.CompareTo("5") == 0)
                    {
                        description = description.Replace("SHOT", "NO GOAL");
                        description = description + " (Shootout)";

                        if (!result.Contains(description))
                            result.Add(description);                        
                    }
                    else
                    {
                        if (type == "Goal" || type == "Penalty" || type == "Hit")
                        {
                            if (!result.Contains(entry))
                                result.Add(entry);
                        }
                    }
                }

                if (result.Count == 0)
                {
                    result.Add(player.Name + " played but did not have any events");
                }
            }

            return result;
        }

        public List<string> GetGameEvents()
        {
            List<string> result = new List<string>();
            XmlNodeList nodes = playByPlay.SelectNodes("//data/game/plays/play");

            bool shootOutStarted = false;

            foreach (XmlNode node in nodes)
            {
                string type = node.SelectNodes("type")[0].InnerText;
                string description = node.SelectNodes("desc")[0].InnerText;
                string time = node.SelectNodes("time")[0].InnerText;
                string period = node.SelectNodes("period")[0].InnerText;

                if (!playoffs && period.CompareTo("5") == 0)
                {
                    if (!shootOutStarted)
                    {
                        shootOutStarted = true;
                        result.Add("---SHOOTOUT---");
                    }

                    //description = description.Replace("SHOT", "NO GOAL");

                    string shooterName = node.SelectNodes("p1name")[0].InnerText;
                    string goalieName = node.SelectNodes("g_goalie")[0].InnerText;
                    string soResult = node.SelectNodes("soResult")[0].InnerText;

                    string whatHappened = "???";
                    if (soResult == "Save")
                        whatHappened = "NO GOAL";
                    else if (soResult == "Goal")
                        whatHappened = "GOAL";
                    else if (soResult == "Miss")
                        whatHappened = "MISS";

                    description = shooterName + " " + whatHappened + " on " + goalieName;

                    if (!result.Contains(description))
                        result.Add(description);
                }
                else
                {
                    if (type == "Goal")
                    {
                        result.Add(description + " at " + time + " of " + GetPeriodFormatted(period));
                    }
                }
            }

            return result;


        }

        public double GetGoalieSVP(string fullGoalieName)
        {
            double shots = 0;
            double goals = 0;
            double gaa = 0;
            string shotString = "SHOT on";
            string goalString = "GOAL on";

            XmlNodeList nodes = playByPlay.SelectNodes("//data/game/plays/play");

            foreach (XmlNode node in nodes)
            {
                string type = node.SelectNodes("type")[0].InnerText;
                string description = node.SelectNodes("desc")[0].InnerText;

                if (type == "Shot")
                {
                    System.Console.WriteLine("-s->" + description);
                    int shotStringIndex = description.IndexOf(shotString);
                    if (shotStringIndex >= 0)
                    {
                        if (String.Compare(description.Substring(shotStringIndex + shotString.Length).Trim(), fullGoalieName, true) == 0)
                        {
                            shots++;
                        }
                    }
                    else
                    {
                        //System.Console.WriteLine("Something wrong parsing pbp list");
                    }
                }
                else if (type == "Goal")
                {
                    System.Console.WriteLine("-g->" + description);
                    int goalStringIndex = description.IndexOf(goalString);
                    if (goalStringIndex >= 0)
                    {
                        if (String.Compare(description.Substring(goalStringIndex + goalString.Length).Trim(), fullGoalieName, true) == 0)
                        {
                            goals++;
                        }
                    }
                    else
                    {
                        //System.Console.WriteLine("Something wrong parsing pbp list");
                    }
                }
            }
            
            shots = shots + goals;

            double saves = shots - goals;

            if (shots == 0)
                gaa = 0;
            else
                gaa = saves / shots;

            return gaa;
        }

        public int GetGameStat(string statType, string fullPlayerName, int targetPlayerId, bool isTarget)
        {
            fullPlayerName = fullPlayerName.Replace("(A)", string.Empty);
            fullPlayerName = fullPlayerName.Replace("(C)", string.Empty);
            fullPlayerName = fullPlayerName.Trim();

            int stat = 0;
            string statString = statType + " on";

            XmlNodeList nodes = playByPlay.SelectNodes("//data/game/plays/play");
            try
            {

                foreach (XmlNode node in nodes)
                {
                    string type = node.SelectNodes("type")[0].InnerText;
                    string description = node.SelectNodes("desc")[0].InnerText;
                    string period = node.SelectNodes("period")[0].InnerText;
                    string player = node.SelectNodes("playername")[0].InnerText;

                    int playerId = 0;
                    XmlNodeList playerIdNodes = node.SelectNodes("pid");
                    if (playerIdNodes.Count > 0)
                        playerId = Convert.ToInt32(playerIdNodes[0].InnerText);
                    else
                        continue;

                    string firstInitial = fullPlayerName.Substring(0, 1);
                    string lastName = fullPlayerName.Substring(3);
                    int indexOfOn = description.IndexOf("on ") + 3;

                    if (string.Compare(type, statType, true) == 0)
                    {
                        int statStringIndex = description.ToLower().IndexOf(statString.ToLower());

                        if (String.Compare("penalty", statType, true) == 0)
                        {
                            if (String.Compare(fullPlayerName, player, true) == 0)
                            {
                                if (description.ToLower().Contains("maj"))
                                {
                                    stat += 5;
                                }
                                else if (description.ToLower().Contains("10 min"))
                                {
                                    stat += 10;
                                }
                                else
                                {
                                    stat += 2;
                                }
                            }
                        }
                        else if (statStringIndex >= 0)
                        {
                            //period 5 is the shootout in regular season
                            if (playoffs || period.CompareTo("4") != 0)
                            {
                                if (isTarget)
                                {
                                    //if (String.Compare(description.Substring(statStringIndex + statString.Length).Trim(), fullPlayerName, true) == 0)
                                    // Jarome Iginla SHOT on Roberto Luongo
                                    // need to find R. Luongo in ^^^^^^^
                                    if (fullPlayerName.Substring(1, 1).CompareTo(".") != 0)
                                    {
                                        System.Console.WriteLine("ERROR EXPANDING PLAYERNAME");
                                    }

                                    if (System.Text.RegularExpressions.Regex.IsMatch(description.Substring(indexOfOn), string.Format("{0}.* {1}$", firstInitial, lastName)))
                                    {
                                        stat++;
                                    }
                                }
                                else
                                {
                                    //if (String.Compare(description.Substring(0, statStringIndex).Trim(), fullPlayerName, true) == 0)
                                    if (playerId == targetPlayerId)
                                    {
                                        stat++;
                                    }
                                }
                            }
                        }
                        else if (playerId == targetPlayerId &&
                                 statType.ToLower() == "goal" &&
                                 statType.ToLower().Equals(type.ToLower()))
                        {
                            stat++;
                        }
                        else if (isTarget && statType.ToLower() == "goal")
                        {
                            // this is a goalie and there was a goal, need to test is this was a goal against this goalie
                            string scoringTeamId = node.SelectNodes("teamid")[0].InnerText;
                            bool homeTeamScored = scoringTeamId.Equals(playByPlay.SelectNodes("//data/game/hometeamid")[0].InnerText);
                            bool foundGoalie = false;
                            if (homeTeamScored)
                            {
                                foreach (XmlNode n in node.SelectNodes("aoi"))
                                {
                                    int awayOnIce = int.Parse(n.InnerText);
                                    if (targetPlayerId == awayOnIce)
                                    {
                                        stat++;
                                        foundGoalie = true;
                                        break;
                                    }
                                }
                            }
                            else if (!foundGoalie)
                            {
                                foreach (XmlNode n in node.SelectNodes("hoi"))
                                {
                                    int homeOnIce = int.Parse(n.InnerText);
                                    if (targetPlayerId == homeOnIce)
                                    {
                                        stat++;
                                        foundGoalie = true;
                                        break;
                                    }
                                }
                            }

                        }
                        else if ("shot".Equals(statType.ToLower()) && description.Contains("saved by") && description.Contains(lastName))
                        {
                            stat++;
                        }
                        else
                        {
                            System.Console.WriteLine("Something wrong parsing pbp list");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("GetGameStat exception: " + ex.Message + "\n\n: " + ex.StackTrace);
                return -1;
            }

            return stat;
        }


        public int GetGameTeamStat(string statType, bool homeTeam, bool isTarget)
        {
            int stat = 0;
            string targetTeamId = string.Empty;
            string statString = statType + " on";

            string teamTarget;
            if (homeTeam)
                teamTarget = "hometeamid";
            else
                teamTarget = "awayteamid";

            XmlNodeList nodes = playByPlay.SelectNodes("//data/game/" + teamTarget);
            if (nodes.Count > 0)
            {
                targetTeamId = nodes[0].InnerText;
            }                        

            nodes = playByPlay.SelectNodes("//data/game/plays/play");

            foreach (XmlNode node in nodes)
            {
                string type = node.SelectNodes("type")[0].InnerText;
                string description = node.SelectNodes("desc")[0].InnerText;
                string period = node.SelectNodes("period")[0].InnerText;
                string teamId = node.SelectNodes("teamid")[0].InnerText;

                if (string.Compare(type, statType, true) == 0)
                {
                    // period 5 is the shootout in regular season
                    if (playoffs || period.CompareTo("5") != 0)
                    {
                        if (String.Compare(targetTeamId, teamId, true) == 0)
                        {
                            if (String.Compare(type, "penalty", true) == 0)
                            {
                                if (description.ToLower().Contains("maj"))
                                {
                                    stat += 5;
                                }
                                else if (description.ToLower().Contains("10 min"))
                                {
                                    stat += 10;
                                }
                                else
                                {
                                    stat += 2;
                                }
                            }
                            else
                            {
                                stat++;
                            }
                        }
                    }
                }
            }

            return stat;
        }

        private string TOIConvert(int seconds)
        {
            int toiMinutes = seconds / 60;
            int toiSeconds = seconds % 60;

            string toi = toiMinutes + ":";

            if (toiSeconds < 10)
                toi += "0" + toiSeconds;
            else
                toi += toiSeconds;

            return toi;
        }

        public string GetScratches()
        {
            string result = string.Empty;

            foreach (JackedStatsSet.PlayersRow row in stats.Players.Rows)
            {
                if (row.Scratch)
                {
                    result += row.Name + ", ";                    
                }
            }

            if (result.Length == 0)
                result = "No scratches";
            else
                result = result.Substring(0, result.Length - 2);

            return result;
        }

        private string GetSavePercentage(int saves, int shots)
        {
            decimal svp;

            if (shots == 0)
                svp = 0;
            else
                svp = (decimal)saves / (decimal)shots;

            string result;

            result = Convert.ToString(Math.Round(svp, 3));

            if (result.Length == 1)
            {
                result = result + ".000";
            }
            else if (result.Length < 5)
            {
                //0.751
                result.PadRight(5, ' ');
            }

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

            individual = NHLStats.EscapeFilter(individual);

            string shortName = individual;
            if (individual.Length > 0 && individual.IndexOf(" ") > 0)
                shortName = individual.Substring(0, 1) + ". " + individual.Substring(individual.IndexOf(" ") + 1);

            try
            {
                JackedStatsSet.PlayerStatsRow statsRow;
                JackedStatsSet.PlayersRow[] rows =
                    (JackedStatsSet.PlayersRow[])stats.Players.Select(String.Format("Name LIKE '%{0}%' OR Name LIKE '{1}%'", individual, shortName));
                if (rows.Length > 0)
                {
                    if (rows[0].GetChildRows("PlayerStats_Players").Length > 0)
                    {
                        statsRow = (JackedStatsSet.PlayerStatsRow)rows[0].GetChildRows("PlayerStats_Players")[0];
                        if (statsRow != null)
                        {
                            if (rows[0].Position.Trim().CompareTo("G") == 0)
                            {
                                //jesus
                                int goals = GetGameStat("Goal", rows[0].Name, rows[0].JackedPlayer_ID, true);
                                int shots = GetGameStat("Shot", rows[0].Name, rows[0].JackedPlayer_ID, true) + goals;
                                int saves = shots - goals;

                                result = string.Format("[{0}/{1}] {2} [Save %:{3}] [Saves: {5}] [Shots: {4}]",
                                    NHLGame.FixCase(homeTeamName), NHLGame.FixCase(awayTeamName), rows[0].Name,
                                    GetSavePercentage(saves, shots), shots, saves);

                            }
                            else
                            {
                                if (!statsRow.IsGoalsNull() && !statsRow.IsAssistsNull() && !statsRow.IsPlusMinusNull() &&
                                    !statsRow.IsPIMNull() && !statsRow.IsTOINull())
                                {
                                    int goals = GetGameStat("Goal", rows[0].Name, rows[0].JackedPlayer_ID, false);
                                    int shots = GetGameStat("Shot", rows[0].Name, rows[0].JackedPlayer_ID, false) + goals;

                                string plusMinus = String.Format("{0}{1}", statsRow.PlusMinus > 0 ? "+" : "", statsRow.PlusMinus);

                                result = string.Format("[{0}/{1}] {2} [Shots:{9}] [Goals:{3}] [Assists:{4}] [TOI: {5}]" +
                                    " [+/-: {6}] [PIM:{7}] [Hits:{8}]",
                                    NHLGame.FixCase(homeTeamName), NHLGame.FixCase(awayTeamName),
                                    rows[0].Name, statsRow.Goals, statsRow.Assists, TOIConvert(statsRow.TOI),
                                    plusMinus, statsRow.PIM, GetGameStat("Hit", rows[0].Name, rows[0].JackedPlayer_ID, false), shots);
                                }
                                else
                                    result = String.Format("Error: Problem with stats for {0}. Try again in a few minutes", rows[0].Name);
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
            }
            catch (Exception ex)
            {
                result = "Problem looking up stats: you probably typed in something stupid";
            }

            return result;
        }


        static void LoopThread(object _gameStats)
        {
            NHLGameStats gameStats = (NHLGameStats)_gameStats;

            int timeIndex = 0;

            for (; ; )
            {                
                lock (gameStats.playByPlay)
                {
                    string jsonText = NHLDotComFetch.GetPlayByPlayString(gameStats.gameId, 0);

                    try
                    {
                        XmlDocument pbp = Program.DeserializeFromJson(jsonText);
                        if (pbp != null)
                            gameStats.playByPlay = pbp;
                    }
                    catch (Exception e)
                    {
                        System.Console.WriteLine(e.Message + " in NHLGameStats.cs:LoopThread");
                    }
                }
                if (gameStats.stats.Players.Rows.Count == 0 || gameStats.stats.Players.Rows.Count <= 28)
                {
                    //System.Console.WriteLine(
                    //    String.Format("Parsing game players for gameid {0}", gameStats.gameId));

                    gameStats.ParsePlayerList(true);
                    gameStats.ParsePlayerList(false);
            

                    if (gameStats.stats.Players.Rows.Count == 0)
                        Thread.Sleep(SLEEP_NEXTDATA_DELAY * 4);
                }

                if (gameStats.stats.Players.Rows.Count > 0)
                {
                    //System.Console.WriteLine(
                    //    String.Format("Parsing game stats for gameid {0}", gameStats.gameId));

                    gameStats.ParseStatsList(timeIndex, true);
                    gameStats.ParseStatsList(timeIndex, false);

                    //gameStats.CheckLastPlayByPlayEventForGoal();

                    timeIndex = timeIndex + 10000;
                }
                else
                {
                    Thread.Sleep(SLEEP_NEXTDATA_DELAY);
                }
                
                Thread.Sleep(SLEEP_ITERATION_DELAY);


                if (!gameStats.MonitorStats)
                    break;

            }
        }

    }
}

using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using System.Data;
using System.Xml;
using System.Threading;
using Newtonsoft.Json;

namespace NHLScoreBot
{
    public class ScheduledGame
    {
        string homeTeamName, awayTeamName;

        public string AwayTeamName
        {
            get { return awayTeamName; }
        }

        public string HomeTeamName
        {
            get { return homeTeamName; }
        }

        DateTime startDate;

        public DateTime StartDate
        {
            get { return startDate; }
        }

        public string StartTime
        {
            get { return startDate.ToShortTimeString(); }
        }

        public string StartDateString
        {
            get { return startDate.ToString("dddd MMM d"); }
        }

        string extraInfo;

        public string ExtraInfo
        {
            get { return extraInfo; }
        }


        public override bool Equals(object obj)
        {
            ScheduledGame game = (ScheduledGame)obj;

            bool equal = false;

            if (String.Compare(game.homeTeamName, homeTeamName, true) == 0 &&
                String.Compare(game.awayTeamName, awayTeamName, true) == 0 &&
                game.StartDate.Equals(StartDate) &&
                String.Compare(game.StartTime, StartTime, true) == 0)
            {
                equal = true;
            }

            return equal;              
        }

        public ScheduledGame(string _homeTeamName, string _awayTeamName, DateTime _startDate, string _extraInfo)
        {
            homeTeamName = _homeTeamName;
            awayTeamName = _awayTeamName;
            startDate = _startDate;
            extraInfo = _extraInfo;
        }

        public ScheduledGame(string _homeTeamName, string _awayTeamName, string _extraInfo)
        {
            homeTeamName = _homeTeamName;
            awayTeamName = _awayTeamName;
            startDate = DateTime.MinValue;
            extraInfo = _extraInfo;
        }
    }

    public class TeamRecord
    {
        public TeamRecord(NHLTeam team, int wins, int losses, int fakeLosses)
        {
            this.team = team;
            this.wins = wins;
            this.losses = losses;
            this.fakeLosses = fakeLosses;
        }

        NHLTeam team;

        public NHLTeam Team
        {
            get { return team; }
        }
        int wins;

        public int Wins
        {
            get { return wins; }
        }
        int losses;

        public int Losses
        {
            get { return losses; }
        }
        int fakeLosses;

        public int FakeLosses
        {
            get { return fakeLosses; }
        }

        public int getPoints()
        {
            return (wins * 2) + fakeLosses;
        }

        public int getGamesPlayed()
        {
            return wins + losses + fakeLosses;
        }
    }


    public class NHLTeam
    {
        string teamId;

        public string TeamId
        {
            get { return teamId; }
        }

        string city;

        public string City
        {
            get { return city; }
        }
        string name;

        public string Name
        {
            get { return name; }
        }

        string conference;

        public string Conference
        {
            get { return conference; }
        }

        string division;

        public string Division
        {
            get { return division; }
        }

        public NHLTeam(string _city, string _name, string _teamId, string _division, string _conference)
        {
            city = _city;
            name = _name;
            teamId = _teamId;
            conference = _conference;
            division = _division;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is NHLTeam)) 
                return false;
            else
                return (this.City == ((NHLTeam)obj).City && 
                        this.Name == ((NHLTeam)obj).Name);
        }
    }

    public class NHLStats
    {
        private Object mutex;

        // TODO: put in config file!!!!!!
        private NHLDatabaseOperator nhlDatabaseOperator;
        private const string CURRENT_YAHOO_SEASON_TEXT = "season_2017"; // "postseason_2015;
        private const int CURRENT_SEASON_ID = 1018; // 1011;
        private const int NOTFOUND_SEASON_ID = -1;
        private const int ALL_PLAYOFFS_SEASON_ID = int.MinValue;
        private const int ALL_REGULAR_SEASON_ID = int.MaxValue;

        public static NHLStats Instance;

        private DataSet nhl;

        private DataTable teamStats;
        private DataTable playerStats;
        private DataTable goalieStats;
        private DataTable schedule;

        private Object fetchingAliasesMutex;
        private Boolean fetchAliases;
        private Boolean fetchAliasesBroken;

        private XmlDocument jackedGameList;

        bool running = true;

        public Boolean FetchAliasesBroken
        {
            get 
            {                
                Boolean result;
                lock (fetchingAliasesMutex)
                {
                    result = fetchAliasesBroken;
                }
                return result;   
            }

            set
            {
                lock (fetchingAliasesMutex)
                {
                    fetchAliasesBroken = value;
                }
            }
        }
        private Thread aliasFetchThread;


        public Boolean FetchAliases
        {
            get 
            {
                Boolean result;
                lock (fetchingAliasesMutex)
                {
                    result = fetchAliases;
                }
                return result;            
            }

            set
            {
                lock (fetchingAliasesMutex)
                {
                    fetchAliases = value;
                }
            }
        }
        private Hashtable teamAliases, playerAliases, goalieAliases;

        DateTime lastUpdate;

        private NHLDotComFetch fetcher;

        private string GetPlayerStatsPath(string team, string year, bool playoffs)
        {
            return String.Format("http://www.nhl.com/superstats/rdl_files/{0}/ptbyteam/{1}_{2}.rdl",
                year, playoffs ? "po" : "rs", team);

        }

        private string GetGoalieStatsPath(string team, string year, bool playoffs)
        {
            return String.Format("http://www.nhl.com/superstats/rdl_files/{0}/gtbyteam/{1}_{2}.rdl",
                year, playoffs ? "po" : "rs", team);
        }

        private string GetTeamStatsPath(string league, string year, bool playoffs)
        {
            return String.Format("http://www.nhl.com/superstats/rdl_files/{0}/tsbyleague/{1}_{2}.rdl",
                year, playoffs ? "po" : "rs", league);
        }                

        public NHLStats()
        {
            NHLStats.Instance = this;
            mutex = new Object();

            nhlDatabaseOperator = new NHLDatabaseOperator();

            fetcher = new NHLDotComFetch();
            fetchingAliasesMutex = new Object();
            fetchAliases = true;
            fetchAliasesBroken = false;
            teamAliases = new Hashtable();
            playerAliases = new Hashtable();
            goalieAliases = new Hashtable();
            jackedGameList = new XmlDocument();
            aliasFetchThread = new Thread(new ParameterizedThreadStart(FetchAliasesLoop));
            aliasFetchThread.Start(this);           

            nhl = nhlDatabaseOperator.NhlStatsDatabase;

            playerStats = nhlDatabaseOperator.NhlStatsDatabase.playerstats;
            goalieStats = nhlDatabaseOperator.NhlStatsDatabase.goaliestats;
            teamStats = nhlDatabaseOperator.NhlStatsDatabase.teamstats;
            schedule = nhlDatabaseOperator.NhlStatsDatabase.schedule;

            lastUpdate = DateTime.MinValue;
        }

        public void Shutdown()
        {
            lock (fetchingAliasesMutex)
            {
                running = false;
            }
        }

        private void LoadTeams()
        {
            XmlDocument xmlDocument = new XmlDocument();
            xmlDocument.Load("teams.xml");
            /*
            Type stringType = System.Type.GetType("System.String");            
            
            teamNames = new DataTable("Teams");
            teamNames.Columns.Add(new DataColumn("TeamID", stringType));
            teamNames.Columns.Add(new DataColumn("TeamName", stringType));
            teamNames.Columns.Add(new DataColumn("TeamCity", stringType));
            teamNames.Columns.Add(new DataColumn("Conference", stringType));
            teamNames.Columns.Add(new DataColumn("Division", stringType));
            //nhl.Tables.Add(teamNames);
            */

            XmlNodeList nodes = xmlDocument.SelectNodes("//teams/league[@name='nhl']/teams/conference/division/team");

            foreach (XmlNode node in nodes)
            {
                lock (mutex)
                {
                    DataRow row = nhlDatabaseOperator.NhlStatsDatabase.teams.Rows.Add();
                    row["TeamID"] = node.InnerText;
                    row["TeamName"] = node.Attributes["name"].Value;
                    row["TeamCity"] = node.Attributes["city"].Value;
                    row["Division"] = node.ParentNode.Attributes["name"].Value;
                    row["Conference"] = node.ParentNode.ParentNode.Attributes["name"].Value;
                }
            }

            nodes = xmlDocument.SelectNodes("//teams/league[@name='olympics']/teams/conference/division/team");

            foreach (XmlNode node in nodes)
            {
                lock (mutex)
                {
                    DataRow row = nhlDatabaseOperator.NhlStatsDatabase.teams.Rows.Add();
                    row["TeamID"] = node.InnerText;
                    row["TeamName"] = node.Attributes["name"].Value;
                    row["TeamCity"] = node.Attributes["city"].Value;
                    row["Division"] = node.ParentNode.Attributes["name"].Value;
                    row["Conference"] = node.ParentNode.ParentNode.Attributes["name"].Value;
                }
            }
        }

        public void UpdateStatsIfRequired()
        {
            lock (mutex)
            {
                bool shouldUpdate = false;

                if ((lastUpdate.Day != DateTime.Now.Day && DateTime.Now.Hour > 3) ||
                        lastUpdate.Day == DateTime.Now.Day && lastUpdate.Hour <= 3 && DateTime.Now.Hour > 3)
                    shouldUpdate = true;

                if (shouldUpdate)
                {
                    // no point but easy
                    LoadTeams();

                    System.Console.Write("Downloading new stats..");

                    UpdatePlayerStats(CURRENT_YAHOO_SEASON_TEXT, (CURRENT_YAHOO_SEASON_TEXT.Contains("postseason")), null, CURRENT_SEASON_ID);
                    UpdateGoalieStats(CURRENT_YAHOO_SEASON_TEXT, (CURRENT_YAHOO_SEASON_TEXT.Contains("postseason")), null, CURRENT_SEASON_ID);

                    nhlDatabaseOperator.ClearSeasonStats(CURRENT_SEASON_ID);

                    UpdateAndClearCache();

                    lastUpdate = DateTime.Now;
                    System.Console.WriteLine("Done!");
                }
            }
        }

        private void UpdateAndClearCache()
        {
            nhlDatabaseOperator.CommitToDatabase();
            nhlDatabaseOperator.NhlStatsDatabase.Clear();
        }      

        private void UpdatePlayerStats(String year, bool playoffs, String []teams, int seasonId)
        {
            YahooParser parser = new YahooParser();
            string yahooHTML = NHLDotComFetch.GetPageString(String.Format("http://sports.yahoo.com/nhl/stats/byposition?pos=C,RW,LW,D&conference=NHL&year={0}&qualified=1",
                                                                          year));
            if (yahooHTML != null)
            {
                DataTable yahooTable = parser.ParseYahooStatsHTMLTable(yahooHTML);

                yahooTable.Columns.Add(new DataColumn("Season_ID", Type.GetType("System.Int32")));
                foreach (DataRow row in yahooTable.Rows)
                    row["Season_ID"] = seasonId;

                yahooTable.Columns["Name"].ColumnName = "PlayerName";
                
                yahooTable.Columns["G"].ColumnName = "Goals";                
                yahooTable.Columns["A"].ColumnName = "Assists";
                yahooTable.Columns["Pts"].ColumnName = "Points";
                yahooTable.Columns["+/-"].ColumnName = "Plus/Minus";
                yahooTable.Columns["PPG"].ColumnName = "Goals - PP";
                yahooTable.Columns["PPA"].ColumnName = "Assists - PP";
                yahooTable.Columns["SHG"].ColumnName = "Goals - SH";
                yahooTable.Columns["SHA"].ColumnName = "Assists - SH";
                yahooTable.Columns["GW"].ColumnName = "Goals - Game Winning";
                //yahooTable.Columns["GT"].ColumnName = "Goals - Game Tying";
                yahooTable.Columns["SOG"].ColumnName = "Shots";
                yahooTable.Columns["Pct"].ColumnName = "Shooting %age";

                playerStats.Merge(yahooTable, true, MissingSchemaAction.Ignore);
                               
            }
        }

        private void UpdateGoalieStats(String year, bool playoffs, String []teams, int seasonId)
        {
            YahooParser parser = new YahooParser();
            string yahooHTML = NHLDotComFetch.GetPageString(String.Format("http://sports.yahoo.com/nhl/stats/byposition?pos=G&conference=NHL&year={0}&qualified=1",
                                                                          year));
            if (yahooHTML != null)
            {
                DataTable yahooTable = parser.ParseYahooStatsHTMLTable(yahooHTML);

                yahooTable.Columns.Add(new DataColumn("Season_ID", Type.GetType("System.Int32")));
                foreach (DataRow row in yahooTable.Rows)
                    row["Season_ID"] = seasonId;

                yahooTable.Columns["Name"].ColumnName = "GoalieName";

                yahooTable.Columns["GP"].ColumnName = "GP";
                //yahooTable.Columns["GS"].ColumnName = "Games Started";
                //yahooTable.Columns["MIN"].ColumnName = "Minutes";
                yahooTable.Columns["W"].ColumnName = "Wins";
                yahooTable.Columns["L"].ColumnName = "Losses";
                yahooTable.Columns["OTL"].ColumnName = "OT Losses";
                //yahooTable.Columns["EGA"].ColumnName = "Emptynet Goals Against;
                yahooTable.Columns["GA"].ColumnName = "GA";
                yahooTable.Columns["GAA"].ColumnName = "GA Per Game";
                yahooTable.Columns["SA"].ColumnName = "SA";
                yahooTable.Columns["SV"].ColumnName = "Saves";
                yahooTable.Columns["SV%"].ColumnName = "Save %age";
                yahooTable.Columns["SO"].ColumnName = "Shutouts";

                goalieStats.Merge(yahooTable, true, MissingSchemaAction.Ignore);
            }
        }

        public void UpdateSchedule()
        {
            lock (mutex)
            {
                NHLScheduleParser parser = new NHLScheduleParser();

                // TODO: put in config file
                //  - gameType 1 = preseason
                //  - gameType 2 = regular season
                //  - gameType 3 = playoffs
				//string url = "http://www.nhl.com/ice/schedulebyseason.htm?season=20142015&gameType=3";
                string url = "http://www.nhl.com/ice/schedulebyseason.htm?season=20172018&gameType=3";
                string html = NHLDotComFetch.GetPageString(url);
                if (html != null)
                {
                    System.Data.DataTable result = parser.ParseNHLScheduleTable(html);
                    DownloadAndSaveSchedule(result);
                }
                else
                    System.Console.WriteLine("ERROR GETTIN SCHEDULE");
            }

        }

        private void DownloadAndSaveSchedule(DataTable inSchedule)
        {
            lock (mutex)
            {
                nhlDatabaseOperator.ScheduleAdapter.DeleteQuery(CURRENT_SEASON_ID);
                foreach (DataRow inRow in inSchedule.Rows)
                {
					string timeString = inRow["TIME"] as string; // meh
					if (timeString == null)
						continue;

                    DataRow newScheduleRow = schedule.NewRow();

                    newScheduleRow["Season_ID"] = CURRENT_SEASON_ID;

					DateTime date;
                    //try
                    {
                        date = (DateTime)inRow["DateTime"];
                    }
                    //catch (Exception)
                    //{
                    //    string d = (string)inRow["DateTime"];
                    //    date = new DateTime((d.Substring(0, 3) + " " + d.Substring(3));
                    //}
                    string isoDate = date.ToString("s", System.Globalization.CultureInfo.InvariantCulture);
                    newScheduleRow["Date"] = isoDate;

                    NHLTeam homeTeam = GetTeamFromCityOrTeam((string)inRow["HOME TEAM"]);
                    NHLTeam awayTeam = GetTeamFromCityOrTeam((string)inRow["VISITING TEAM"]);
                    if (homeTeam != null && awayTeam != null)
                    {
                        NHLTeam properHomeTeam = GetTeamFromTeamId(homeTeam.TeamId);
                        NHLTeam properAwayTeam = GetTeamFromTeamId(awayTeam.TeamId);
                        if (properHomeTeam != null && properAwayTeam != null)
                        {

                            if (((string)inRow["Winner"]).Length > 0)
                            {
                                if (inRow["HOME TEAM"] == inRow["Winner"])
                                {
                                    newScheduleRow["Winner"] = properHomeTeam.Name;
                                    newScheduleRow["Loser"] = properAwayTeam.Name;

                                }
                                else if (inRow["HOME TEAM"] == inRow["Loser"])
                                {
                                    newScheduleRow["Winner"] = properAwayTeam.Name;
                                    newScheduleRow["Loser"] = properHomeTeam.Name;
                                }
                                else
                                {
                                    throw new Exception("Couldn't match home team to winner or loser");
                                }
                            }

                            newScheduleRow["HomeTeam"] = properHomeTeam.Name;
                            newScheduleRow["AwayTeam"] = properAwayTeam.Name;

                            newScheduleRow["TBD"] = inRow["TBD"];
                            newScheduleRow["Info"] = inRow["NETWORK/RESULT"];


                            newScheduleRow["WinnerScore"] = inRow["WinnerScore"];
                            newScheduleRow["LoserScore"] = inRow["LoserScore"];
                            newScheduleRow["EndType"] = inRow["EndType"];

                            schedule.Rows.Add(newScheduleRow);
                        }
                        else
                        {
                            System.Console.WriteLine(String.Format("Problem adding game to schedule: {0} vs {1}", (string)inRow["HOME TEAM"], (string)inRow["VISITING TEAM"]));
                        }
                    }
                    else
                    {
                        System.Console.WriteLine("WARNING: Couldn't find teams: " + (string)inRow["HOME TEAM"] + ", " + (string)inRow["VISITING TEAM"]);
                    }
                }

                nhlDatabaseOperator.CommitToDatabase();
            }
        }

        public NHLTeam GetTeamFromCityOrTeam(string text)
        {
            lock (mutex)
            {
                NHLTeam result = null;
                text = EscapeFilter(text);

                //DataRow []rows = nhlDatabaseOperator.NhlStatsDatabase.teams.Select(String.Format("(TeamName LIKE '%{0}%' OR TeamCity LIKE '%{0}%') " + 
                //                                                "AND NOT (Conference = 'Nonexistent')" , text));

                DataRowCollection rows = nhlDatabaseOperator.GetExistantTeam(text).Rows;

                if (rows.Count > 0)
                {
                    result = new NHLTeam((String)rows[0]["TeamCity"], (String)rows[0]["TeamName"], (String)rows[0]["TeamID"],
                        (String)rows[0]["Division"], (String)rows[0]["Conference"]);
                }
                else
                {
                    string fromAlias = GetTeamNameAlias(text);
                    //rows = nhlDatabaseOperator.NhlStatsDatabase.teams.Select(String.Format("(TeamName LIKE '%{0}%') " +
                    //                                      "AND NOT (Conference = 'Nonexistent')", fromAlias));
                    rows = nhlDatabaseOperator.GetExistantTeam(fromAlias).Rows;

                    if (rows.Count > 0)
                    {
                        result = new NHLTeam((String)rows[0]["TeamCity"], (String)rows[0]["TeamName"], (String)rows[0]["TeamID"],
                            (String)rows[0]["Division"], (String)rows[0]["Conference"]);
                    }
                }

                return result;
            }
        }

        public NHLTeam GetTeamFromTeamId(string text)
        {
            lock (mutex)
            {
                NHLTeam result = null;
                text = EscapeFilter(text);

                DataRow[] rows = nhlDatabaseOperator.TeamsAdapter.GetData().Select(String.Format("TeamID = '{0}'", text));
                if (rows.Length > 0)
                {
                    result = new NHLTeam((String)rows[0]["TeamCity"], (String)rows[0]["TeamName"], (String)rows[0]["TeamID"],
                        (String)rows[0]["Division"], (String)rows[0]["Conference"]);
                }

                return result;
            }
        }

        public string EscapeColumnEntries(string text)
        {
            NhlStatsSet.playerstatsDataTable temp = new NhlStatsSet.playerstatsDataTable();
            foreach (DataColumn column in temp.Columns)
            {
                text = text.Replace(column.ColumnName.ToLower(), string.Format("[{0}]", column.ColumnName));                
            }

            return text;
        }

        public string[] GetPlayers(string entry, bool top, string season, string filter)
        {
            lock (mutex)
            {
                long seasonId = GetSeasonID(season);
                entry = GetPlayerEntryAlias(entry);
                string[] results;

                entry = EscapeFilter(entry);
                
                filter = GetPlayerEntryAlias(filter);
                filter = EscapeColumnEntries(filter);

                if (seasonId == NOTFOUND_SEASON_ID)
                {
                    results = new string[1];
                    results[0] = "Couldn't find that season.";
                }
                else
                {
                    string seasonInfo;
                    bool allSeasons = seasonId == ALL_REGULAR_SEASON_ID || seasonId == ALL_PLAYOFFS_SEASON_ID;
                    if (nhlDatabaseOperator.NhlStatsDatabase.playerstats.Columns.Contains(entry))
                    {
                        DataTable playerStats;
                        if (seasonId == ALL_PLAYOFFS_SEASON_ID)
                            playerStats = nhlDatabaseOperator.GetEntirePlayers(entry, top, true);
                        else if (seasonId == ALL_REGULAR_SEASON_ID)
                            playerStats = nhlDatabaseOperator.GetEntirePlayers(entry, top, false);
                        else
                            playerStats = nhlDatabaseOperator.PlayerStatsAdapter.GetPlayers(seasonId);

                        DataRow[] rows = null;
                        try
                        {
                            rows = playerStats.Select(
                                filter, String.Format("{0} {1}, GP DESC", entry, top ? "DESC" : "ASC"));
                        }
                        catch (Exception)
                        {
                        }

                        if (rows != null && rows.Length > 0)
                        {
                            results = new string[5];
                            for (int i = 0; i < 5 && i < rows.Length; i++)
                            {
                                seasonInfo = GetYearFromSeasonID((long)rows[i]["Season_ID"]);
                                results[i] = String.Format("{0}. {1}: {2} {3}", i + 1,
                                    NHLGame.FixCase((string)rows[i]["PlayerName"]), rows[i][entry],
                                    allSeasons ? "(" + seasonInfo + ")" : String.Empty);
                            }
                        }
                        else
                        {
                            results = new string[1];
                            results[0] = "No matching stats";
                        }
                    }
                    else
                    {
                        results = new string[1];
                        results[0] = String.Format("{0} is not a valid statistic.", entry);
                    }
                }

                return results;
            }
        }

        public string[] GetGoalies(string entry, bool top, string season, string filter)
        {
            lock (mutex)
            {
                long seasonId = GetSeasonID(season);
                entry = GetGoalieEntryAlias(entry);
                string[] results;

                entry = EscapeFilter(entry);

                filter = EscapeColumnEntries(filter);

                if (seasonId == NOTFOUND_SEASON_ID)
                {
                    results = new string[1];
                    results[0] = "Couldn't find that season.";
                }
                else
                {
                    string seasonInfo;
                    bool allSeasons = seasonId == ALL_REGULAR_SEASON_ID || seasonId == ALL_PLAYOFFS_SEASON_ID;
                    if (nhlDatabaseOperator.NhlStatsDatabase.goaliestats.Columns.Contains(entry))
                    {
                        DataTable goalieStats;
                        if (seasonId == ALL_PLAYOFFS_SEASON_ID)
                            goalieStats = nhlDatabaseOperator.GetEntireGoalies(entry, top, true);
                        else if (seasonId == ALL_REGULAR_SEASON_ID)
                            goalieStats = nhlDatabaseOperator.GetEntireGoalies(entry, top, false);
                        else
                            goalieStats = nhlDatabaseOperator.GoalieStatsAdapter.GetGoalies(seasonId);

                        DataRow[] rows = null;
                        try
                        {
                            rows = goalieStats.Select(
                                filter, String.Format("{0} {1}, GP DESC", entry, top ? "DESC" : "ASC"));
                        }
                        catch (Exception)
                        {
                        }

                        if (rows != null && rows.Length > 0)
                        {
                            results = new string[5];
                            for (int i = 0; i < 5 && i < rows.Length; i++)
                            {
                                seasonInfo = GetYearFromSeasonID((long)rows[i]["Season_ID"]);
                                results[i] = String.Format("{0}. {1}: {2} {3}", i + 1,
                                    NHLGame.FixCase((string)rows[i]["GoalieName"]), rows[i][entry],
                                    allSeasons ? "(" + seasonInfo + ")" : String.Empty);
                            }
                        }
                        else
                        {
                            results = new string[1];
                            results[0] = "No matching stats";
                        }
                    }
                    else
                    {
                        results = new string[1];
                        results[0] = String.Format("{0} is not a valid statistic.", entry);
                    }
                }
                return results;
            }
        }

        public string[] GetTeams(string entry, bool top, string season)
        {
            lock (mutex)
            {
                return new string[0];

                long seasonId = GetSeasonID(season);
                entry = GetTeamEntryAlias(entry);
                string[] results;

                entry = EscapeFilter(entry);

                if (seasonId == NOTFOUND_SEASON_ID)
                {
                    results = new string[1];
                    results[0] = "Couldn't find that season.";
                }
                else
                {
                    string seasonInfo;
                    NHLTeam team;
                    bool allSeasons = seasonId == ALL_REGULAR_SEASON_ID || seasonId == ALL_PLAYOFFS_SEASON_ID;
                    if (nhlDatabaseOperator.NhlStatsDatabase.teamstats.Columns.Contains(entry))
                    {
                        DataTable teamStats;
                        if (seasonId == ALL_PLAYOFFS_SEASON_ID)
                            teamStats = nhlDatabaseOperator.GetEntireTeams(entry, top, true);
                        else if (seasonId == ALL_REGULAR_SEASON_ID)
                            teamStats = nhlDatabaseOperator.GetEntireTeams(entry, top, false);
                        else
                            teamStats = nhlDatabaseOperator.TeamStatsAdapter.GetTeams(seasonId);

                        DataRow[] rows = teamStats.Select(
                            String.Empty, String.Format("{0} {1}, GP DESC", entry, top ? "DESC" : "ASC"));

                        results = new string[5];
                        for (int i = 0; i < 5; i++)
                        {
                            team = GetTeamFromTeamId((string)rows[i]["TeamName"]);
                            seasonInfo = GetYearFromSeasonID((long)rows[i]["Season_ID"]);
                            results[i] = String.Format("{0}. {1} {2}: {3} {4}", i + 1,
                                team.City, team.Name, rows[i][entry],
                                allSeasons ? "(" + seasonInfo + ")" : String.Empty);
                        }
                    }
                    else
                    {
                        results = new string[1];
                        results[0] = String.Format("{0} is not a valid statistic.", entry);
                    }
                }
                return results;
            }
        }
    
        private int GetStatRankingDesc(DataTable table, string keyColumn, string key, string stat)
        {
            int result = -1;
            DataRow[] rows = table.Select(String.Empty, String.Format("{0} DESC", stat));

            int i = 1;
            foreach (DataRow row in rows)
            {
                if (((String)row[keyColumn]).CompareTo(key) == 0)
                {
                    result = i;
                    break;
                }

                i++;
            }

            return result;
        }

        private int GetStatRankingAsc(DataTable table, string keyColumn, string key, string stat)
        {
            int result = -1;
            DataRow[] rows = table.Select(String.Empty, String.Format("{0} ASC", stat));

            int i = 1;
            foreach (DataRow row in rows)
            {
                if (((String)row[keyColumn]).CompareTo(key) == 0)
                {
                    result = i;
                    break;
                }

                i++;
            }

            return result;
        }

        public static String EscapeFilter(String vstrRawFilter)
        {
            String strEscapedFilter = vstrRawFilter;

			strEscapedFilter = strEscapedFilter.Replace("é", "e"); // meh
			strEscapedFilter = strEscapedFilter.Replace("É", "E"); // meh
			strEscapedFilter = strEscapedFilter.Replace("??", "e"); // meh

            string []astrSpecialCharacters = new string[] { "~", "(", ")", "#", "\\", /*"/",*/ "=", ">", 
                "<", "+", /*"-",*/ "*", /*"%",*/ "&", "|", "^", "'" };

            strEscapedFilter = strEscapedFilter.Replace("[", "");
            strEscapedFilter = strEscapedFilter.Replace("]", "");
            
            foreach (String strSpecialChar in astrSpecialCharacters)
            {
                if (strSpecialChar.CompareTo("'") == 0)
                    strEscapedFilter = strEscapedFilter.Replace(strSpecialChar, "'" + strSpecialChar);
                else
                    strEscapedFilter = strEscapedFilter.Replace(strSpecialChar, "[" + strSpecialChar + "]");
            }

            return strEscapedFilter;
        }

        public string GetPrettySeasonStringFromUserSeason(string userInput)
        {
            long seasonId = GetSeasonID(userInput);
            string seasonInfo = GetYearFromSeasonID(seasonId);

            return seasonInfo;
        }

        private long GetSeasonID(string season)
        {
                        
            long seasonId = NOTFOUND_SEASON_ID;
            
            if (season.CompareTo("ALLP") == 0)
                seasonId = ALL_PLAYOFFS_SEASON_ID;
            else if (season.CompareTo("ALLR") == 0)
                seasonId = ALL_REGULAR_SEASON_ID;
            else if (season != string.Empty && season.Length != 9)
                seasonId = NOTFOUND_SEASON_ID;
            else if (season != string.Empty)
            {
                DataTable table = (DataTable)nhlDatabaseOperator.SeasonsAdapter.GetSeason(
                    season.Substring(0, 4), season.Substring(4, 4), season.Contains("P"));

                if (table.Rows.Count > 0)
                    seasonId = (long)table.Rows[0]["Season_ID"];
            }
            else
                seasonId = CURRENT_SEASON_ID;

            return seasonId;
        }

        public string GetIndividualPlayer(string entry, string name, string season)
        {
            lock (mutex)
            {
                name = GetPlayerNameAlias(name);
                name = EscapeFilter(name);
                entry = GetPlayerEntryAlias(entry);
                String result = String.Empty;
                DataRow[] rows;

                long seasonId = GetSeasonID(season);

                if (seasonId == NOTFOUND_SEASON_ID)
                    result = "Couldn't find that season.";
                else
                {
                    DataTable playerStats = nhlDatabaseOperator.PlayerStatsAdapter.GetPlayers(seasonId);
                    String seasonInfo = GetYearFromSeasonID(seasonId);

                    if (name.StartsWith("\"") && name.EndsWith("\""))
                        rows = playerStats.Select(String.Format("PlayerName = '{0}'", name.Substring(1, name.Length - 2)), "Points DESC");
                    else
                        rows = playerStats.Select(String.Format("PlayerName LIKE '%{0}%'", name), "Points DESC");

                    if (rows.Length >= 1)
                    {
                        if (entry.Length > 0)
                        {
                            if (playerStats.Columns.Contains(entry))
                            {
                                int rank = GetStatRankingDesc(playerStats, "PlayerName", (string)rows[0]["PlayerName"], entry);
                                result = String.Format("({5}) {0} [{1}: {2:####.##} - Rank: {3}/{4}]",
                                    NHLGame.FixCase((string)rows[0]["PlayerName"]), playerStats.Columns[entry].ColumnName, rows[0][entry],
                                    rank, playerStats.Rows.Count, seasonInfo);
                            }
                            else
                                result = String.Format("{0} is not a valid statistic.", entry);
                        }
                        else
                        {
                            result = String.Format(
                                "({15}) {0} [GP:{1}] [G:{2}] [A:{3}] [P:{4}] [+/-:{5}] [SHG:{6}] [SHA:{7}] [PPG:{8}] [PPA:{9}] [GWG:{10}] [GTG:{11}] [SH:{12}] [S%:{13:0.000}] [PIM:{14}]",
                                NHLGame.FixCase((string)rows[0]["PlayerName"]), rows[0]["GP"],
                                rows[0]["Goals"], rows[0]["Assists"], rows[0]["Points"],
                                rows[0]["Plus/Minus"], rows[0]["Goals - SH"], rows[0]["Assists - SH"],
                                rows[0]["Goals - PP"], rows[0]["Assists - PP"], rows[0]["Goals - Game Winning"], rows[0]["Goals - Game Tying"],
                                rows[0]["Shots"], rows[0]["Shooting %age"], rows[0]["PIM"], seasonInfo);
                        }
                    }
                    else
                        result = String.Format("No record of that player for {0}.", seasonInfo);
                }

                return result;
            }
        }

        public string GetIndividualGoalie(string entry, string name, string season)
        {
            lock (mutex)
            {
                name = GetGoalieNameAlias(name);
                name = EscapeFilter(name);
                entry = GetGoalieEntryAlias(entry);
                String result = String.Empty;
                DataRow[] rows;

                long seasonId = GetSeasonID(season);

                if (seasonId == NOTFOUND_SEASON_ID)
                    result = "Couldn't find that season.";
                else
                {
                    String seasonInfo = GetYearFromSeasonID(seasonId);
                    DataTable goalieStats = nhlDatabaseOperator.GoalieStatsAdapter.GetGoalies(seasonId);

                    if (name.StartsWith("\"") && name.EndsWith("\""))
                        rows = goalieStats.Select(String.Format("GoalieName = '{0}'", name.Substring(1, name.Length - 2)));
                    else
                        rows = goalieStats.Select(String.Format("GoalieName LIKE '%{0}%'", name));

                    if (rows.Length >= 1)
                    {
                        if (entry.Length > 0)
                        {
                            if (goalieStats.Columns.Contains(entry))
                            {
                                int rank;
                                if (entry.CompareTo("GA Per Game") == 0)
                                    rank = GetStatRankingAsc(goalieStats, "GoalieName",
                                        NHLGame.FixCase((string)rows[0]["GoalieName"]), entry);
                                else
                                    rank = GetStatRankingDesc(goalieStats, "GoalieName", (string)rows[0]["GoalieName"], entry);
                                result = String.Format("({5}) {0} [{1}: {2:####.##} - Rank: {3}/{4}]",
                                    NHLGame.FixCase((string)rows[0]["GoalieName"]), goalieStats.Columns[entry].ColumnName, rows[0][entry],
                                    rank, goalieStats.Rows.Count, seasonInfo);
                            }
                            else
                                result = String.Format("{0} is not a valid statistic.", entry);
                        }
                        else
                        {
                            result = String.Format(
                                "({9}) {0} [GP:{1}] [W:{2}] [L:{3}] [OTL:{10}] [SA:{4}] [GA:{5}] [GAA:{6}] [SV%:{7:0.000}] [SO:{8}]",
                                NHLGame.FixCase((string)rows[0]["GoalieName"]), rows[0]["GP"], rows[0]["Wins"], rows[0]["Losses"], rows[0]["SA"],
                                rows[0]["GA"], rows[0]["GA Per Game"], rows[0]["Save %age"], rows[0]["Shutouts"],
                                seasonInfo, rows[0]["OT Losses"]);
                        }
                    }
                    else
                        result = String.Format("No record of that goalie for {0}.", seasonInfo);
                }

                return result;
            }
        }

        public string GetPlayerOrGoalieNameFromPartial(string partial)
        {
            DataRow[] rows;            
            rows = goalieStats.Select(String.Format("GoalieName LIKE '%{0}%'", partial));

            if (rows.Length == 0)
            {
                rows = playerStats.Select(String.Format("PlayerName LIKE '%{0}%'", partial));
            }

            if (rows.Length > 0)
            {
                if (rows[0].Table.Columns.Contains("GoalieName"))
                    return rows[0]["GoalieName"] as string;
                else
                    return rows[0]["PlayerName"] as string;
            }
            else
            {
                return partial;
            }
        }


        public string GetIndividualTeam(string entry, string name, string season)
        {
            lock (mutex)
            {
                name = GetTeamNameAlias(name);
                name = EscapeFilter(name);
                entry = GetTeamEntryAlias(entry);
                String result = String.Empty;

                long seasonId = GetSeasonID(season);

                if (seasonId == NOTFOUND_SEASON_ID)
                    result = "Couldn't find that season.";
                else
                {
                    String seasonInfo = GetYearFromSeasonID(seasonId);
                    NHLTeam team = GetTeamFromCityOrTeam(name);

                    if (team != null)
                    {
                        DataTable teamStats = nhlDatabaseOperator.TeamStatsAdapter.GetTeams(seasonId);
                        DataRow[] rows = teamStats.Select(String.Format("TeamName LIKE '%{0}%'", team.TeamId));

                        if (rows.Length >= 1)
                        {
                            if (entry.Length > 0)
                            {
                                if (teamStats.Columns.Contains(entry))
                                {
                                    int rank;

                                    if (entry.CompareTo("GA Per Game") == 0)
                                        rank = GetStatRankingAsc(teamStats, "TeamName", (string)rows[0]["TeamName"], entry);
                                    else
                                        rank = GetStatRankingDesc(teamStats, "TeamName", (string)rows[0]["TeamName"], entry);

                                    result = String.Format("({5}) {0} [{1}: {2:####.##} - Rank: {3}/{4}]",
                                        team.Name, teamStats.Columns[entry].ColumnName, rows[0][entry],
                                        rank, teamStats.Rows.Count, seasonInfo);
                                }
                                else
                                    result = String.Format("{0} is not a valid statistic.", entry);
                            }
                            else
                            {
                                result = String.Format(
                                    "({15}) {0} {1} [GP:{2}] [PTS:{3}] [Record:{4}-{5}] [Home:{6}-{7}] " +
                                    "[Away:{8}-{9}] [GF:{10}] [GA:{11}] [PK%:{12:0.000}] [PP%:{13:0.000}] [PIM:{14}]",
                                    team.City, team.Name, rows[0]["GP"], rows[0]["Points"], rows[0]["Wins"], rows[0]["Losses"],
                                    rows[0]["Wins - Home"], rows[0]["Losses - Home"], rows[0]["Wins - Road"],
                                    rows[0]["Losses - Road"], rows[0]["Goals"], rows[0]["GA"],
                                    ((Decimal)rows[0]["PK %age"]), ((Decimal)rows[0]["PP %age"]), rows[0]["PIM"],
                                    seasonInfo);
                            }
                        }
                        else
                            result = String.Format("No record of that team for {0}.", seasonInfo);
                    }
                    else
                        result = "No record of that team";
                }

                return result;
            }
        }

        private string GetYearFromSeasonID(long seasonId)
        {
            string result = string.Empty;
            NhlStatsSet.seasonsDataTable table = nhlDatabaseOperator.SeasonsAdapter.GetSeasonById(seasonId);

            if (table.Rows.Count > 0)
            {
                NhlStatsSet.seasonsRow row = (NhlStatsSet.seasonsRow)table.Rows[0];
                result = String.Format("{0}-{1} {2}", row.FirstYear.Substring(2, 2), row.SecondYear.Substring(2, 2),
                    (bool)row.Playoffs ? "PS" : "RS");
            }

            return result;
        }

        private string GetGoalieEntryAlias(string entry)
        {
            entry = entry.ToLower();
            string result = entry;

            if (entry.CompareTo("gaa") == 0)
                result = "GA Per Game";
            else if (entry.CompareTo("sv%") == 0 || entry.CompareTo("sv %") == 0)
                result = "Save %age";
            else if (entry.CompareTo("sv %") == 0 || entry.CompareTo("sv %") == 0)
                result = "Save %age";
            else if (entry.CompareTo("so") == 0)
                result = "Shutouts";
            else if (entry.CompareTo("otw") == 0)
                result = "OT Wins";
            else if (entry.CompareTo("otl") == 0)
                result = "OT Losses";
            else if (entry.CompareTo("w") == 0)
                result = "Wins";
            else if (entry.CompareTo("l") == 0)
                result = "Losses";

            return result;
        }

        private string GetPlayerEntryAlias(string entry)
        {
            entry = entry.ToLower();
            string result = entry;

            if (entry.CompareTo("+/-") == 0)
                result = "Plus/Minus";
            else if (entry.CompareTo("fow") == 0)
                result = "FO Won";
            else if (entry.CompareTo("sog") == 0)
                result = "Shots";
            else if (entry.CompareTo("fol") == 0)
                result = "FO Lost";
            else if (entry.CompareTo("shooting %") == 0 || entry.CompareTo("shooting%") == 0)
                result = "Shooting %age";
            else if (entry.CompareTo("fow %") == 0 || entry.CompareTo("fow%") == 0 || entry.CompareTo("fo win %") == 0)
                result = "FO Win %age";
            else if (entry.CompareTo("fo %") == 0 || entry.CompareTo("fo%") == 0)
                result = "FO %age";
            else if (entry.CompareTo("p") == 0)
                result = "Points";
            else if (entry.CompareTo("g") == 0)
                result = "Goals";
            else if (entry.CompareTo("ppg") == 0)
                result = "Goals - PP";
            else if (entry.CompareTo("ppa") == 0)
                result = "Assists - PP";
            else if (entry.CompareTo("shg") == 0)
                result = "Goals - SH";
            else if (entry.CompareTo("sha") == 0)
                result = "Assists - SH";
            else if (entry.CompareTo("sog") == 0)
                result = "Shots";
            else if (entry.CompareTo("s%") == 0)
                result = "Shooting %age";
            else if (entry.CompareTo("s %") == 0)
                result = "Shooting %age";

            return result;
        }

        private string GetTeamEntryAlias(string entry)
        {
            entry = entry.ToLower();
            string result = entry;

            if (entry.CompareTo("shooting %") == 0 || entry.CompareTo("shooting%") == 0)
                result = "Shooting %age";
            else if (entry.CompareTo("p") == 0)
                result = "Points";
            else if (entry.CompareTo("g") == 0)
                result = "Goals";
            else if (entry.CompareTo("ppg") == 0)
                result = "Goals - PP";
            else if (entry.CompareTo("shg") == 0)
                result = "Goals - SH";
            else if (entry.CompareTo("pk%") == 0 || entry.CompareTo("pk %") == 0)
                result = "PK %age";
            else if (entry.CompareTo("pp%") == 0 || entry.CompareTo("pp%") == 0)
                result = "PP %age";

            return result;
        }

        public string GetPlayerNameAlias(string name)
        {
            name = name.ToLower();
            string result;

            lock (fetchingAliasesMutex)
            {
                if (playerAliases.ContainsKey(name))
                    result = (String)playerAliases[name];
                else
                    result = name;
            }

            return result;
        }

        public string GetGoalieNameAlias(string name)
        {
            name = name.ToLower();
            string result;

            lock (fetchingAliasesMutex)
            {
                if (goalieAliases.ContainsKey(name))
                    result = (String)goalieAliases[name];
                else
                    result = name;
            }

            return result;
        }

        public string GetTeamNameAlias(string name)
        {
            name = name.ToLower();
            string result;

            lock (fetchingAliasesMutex)
            {
                if (teamAliases.ContainsKey(name))
                    result = (String)teamAliases[name];
                else
                    result = name;
            }

            return result;
        }

        
        public ScheduledGame[] GetScheduleToday(List<string> excludedHomeTeamNames)
        {
            lock (mutex)
            {
                ScheduledGame[] all = GetSchedule(DateTime.Now);
                ScheduledGame[] result;
                List<ScheduledGame> tmp = new List<ScheduledGame>();

                excludedHomeTeamNames =
                    excludedHomeTeamNames.ConvertAll(new Converter<string, string>(delegate(string s) { return s.ToLower(); }));

                foreach (ScheduledGame game in all)
                {
                    if (!excludedHomeTeamNames.Contains(game.HomeTeamName.ToLower()))
                        tmp.Add(game);
                }

                int i = 0;
                result = new ScheduledGame[tmp.Count];
                foreach (ScheduledGame game in tmp)
                {
                    result[i] = game;
                    i++;
                }

                return result;
            }
        }
        
        

        public ScheduledGame[] GetSchedule(DateTime compareDate)
        {
            lock (mutex)
            {
                ScheduledGame[] result;
                List<ScheduledGame> list = new List<ScheduledGame>();

                compareDate = compareDate.AddHours((-1) * compareDate.Hour);
                compareDate = compareDate.AddMinutes((-1) * compareDate.Minute);
                compareDate = compareDate.AddSeconds((-1) * compareDate.Second);
                compareDate = compareDate.AddSeconds(1);
                compareDate = compareDate.AddMilliseconds((-1) * compareDate.Millisecond);

                DateTime startDate = compareDate;
                DateTime endDate = compareDate.AddDays(1);

                NhlStatsSet.scheduleDataTable table = nhlDatabaseOperator.GetScheduleByDate(CURRENT_SEASON_ID, startDate, endDate);
                foreach (NhlStatsSet.scheduleRow row in table.Rows)
                {
                    if (row.TBD)
                        list.Add(new ScheduledGame(row.HomeTeam, row.AwayTeam, row.Info));
                    else
                        list.Add(new ScheduledGame(row.HomeTeam, row.AwayTeam, Convert.ToDateTime(row.Date), row.Info));
                }

                result = new ScheduledGame[list.Count];
                int i = 0;
                foreach (ScheduledGame g in list)
                {
                    result[i] = g;
                    i++;
                }

                return result;
            }
        }

        public ScheduledGame[] GetSchedule(NHLTeam team, int numberOfGames)
        {
            lock (mutex)
            {
                ScheduledGame[] result;
                List<ScheduledGame> list = new List<ScheduledGame>();


                NhlStatsSet.scheduleDataTable table = nhlDatabaseOperator.GetScheduleForTeam(team.Name, CURRENT_SEASON_ID, numberOfGames);
                foreach (NhlStatsSet.scheduleRow row in table.Rows)
                {
                    if (row.TBD)
                        list.Add(new ScheduledGame(row.HomeTeam, row.AwayTeam, row.Info));
                    else
                        list.Add(new ScheduledGame(row.HomeTeam, row.AwayTeam, Convert.ToDateTime(row.Date), row.Info));
                }

                result = new ScheduledGame[list.Count];
                int i = 0;
                foreach (ScheduledGame g in list)
                {
                    result[i] = g;
                    i++;
                }

                return result;
            }
        }

        public ScheduledGame[] GetSchedule(NHLTeam teamA, NHLTeam teamB, int numberOfGames)
        {
            lock (mutex)
            {
                ScheduledGame[] result;
                List<ScheduledGame> list = new List<ScheduledGame>();

                NhlStatsSet.scheduleDataTable table = nhlDatabaseOperator.GetScheduleForTeams(teamA.Name, teamB.Name, CURRENT_SEASON_ID, numberOfGames);
                foreach (NhlStatsSet.scheduleRow row in table.Rows)
                {
                    if (row.TBD)
                        list.Add(new ScheduledGame(row.HomeTeam, row.AwayTeam, row.Info));
                    else
                        list.Add(new ScheduledGame(row.HomeTeam, row.AwayTeam, Convert.ToDateTime(row.Date), row.Info));
                }

                result = new ScheduledGame[list.Count];
                int i = 0;
                foreach (ScheduledGame g in list)
                {
                    result[i] = g;
                    i++;
                }

                return result;
            }
        }

        public string FormatRecord(TeamRecord record)
        {
            string s;

            string name, pts, recordStr;
            name = record.Team.Name;
            pts = record.getPoints() + " pts";
            recordStr = string.Format("{0} W - {1} L - {2} OT/SO L   ({3} GP)", 
                                      record.Wins, record.Losses, record.FakeLosses, record.getGamesPlayed());

            s = string.Format("{0} {1}", pts.PadRight(8), recordStr);
            return s;
        }

        public List<TeamRecord> ConferenceStandings(bool west, List<INHLGameInfo> games, string season, int numberOfGames,
                                                    TeamRecordFilterType filterType)
        {
            lock (mutex)
            {
                List<TeamRecord> standings = LeagueStandings(games, season, numberOfGames, filterType);
                List<TeamRecord> removeList = new List<TeamRecord>();

                foreach (TeamRecord teamRecord in standings)
                {
                    if (teamRecord.Team.Conference == "Western" && !west)
                        removeList.Add(teamRecord);
                    else if (teamRecord.Team.Conference == "Eastern" && west)
                        removeList.Add(teamRecord);
                }

                foreach (TeamRecord remove in removeList)
                {
                    standings.Remove(remove);
                }

                return standings;
            }
        }

        public enum TeamRecordFilterType
        {
            HOME,
            AWAY,
            EITHER
        }

        public List<TeamRecord> LeagueStandings(List<INHLGameInfo> games, string season, int numberOfGames, 
                                                    TeamRecordFilterType filterType)
        {
            lock (mutex)
            {
                List<TeamRecord> leagueStandings = new List<TeamRecord>();

                List<TeamRecord> centralStandings = new List<TeamRecord>();
                List<TeamRecord> pacificStandings = new List<TeamRecord>();
                List<TeamRecord> atlanticStandings = new List<TeamRecord>();
                List<TeamRecord> metropolitanStandings = new List<TeamRecord>();

                centralStandings = ConferenceRecords("Central", games, season, numberOfGames, filterType);
                pacificStandings = ConferenceRecords("Pacific", games, season, numberOfGames, filterType);
                atlanticStandings = ConferenceRecords("Atlantic", games, season, numberOfGames, filterType);
                metropolitanStandings = ConferenceRecords("Metropolitan", games, season, numberOfGames, filterType);

                List<TeamRecord> conferenceLeaders = new List<TeamRecord>();
                conferenceLeaders.Add(centralStandings[0]);
                centralStandings.RemoveAt(0);
                conferenceLeaders.Add(pacificStandings[0]);
                pacificStandings.RemoveAt(0);
                conferenceLeaders.Add(atlanticStandings[0]);
                atlanticStandings.RemoveAt(0);
                conferenceLeaders.Add(metropolitanStandings[0]);
                metropolitanStandings.RemoveAt(0);
                conferenceLeaders.Sort(new StandingsSorter());

                List<TeamRecord> theRest = new List<TeamRecord>();
                theRest.AddRange(centralStandings);
                theRest.AddRange(pacificStandings);
                theRest.AddRange(atlanticStandings);
                theRest.AddRange(metropolitanStandings);
                theRest.Sort(new StandingsSorter());

                leagueStandings.AddRange(conferenceLeaders);
                leagueStandings.AddRange(theRest);

                return leagueStandings;
            }
        }

        private class StandingsSorter : IComparer<TeamRecord>
        {
            public int Compare(TeamRecord obj1, TeamRecord obj2)
            {
                if (obj1 == obj2)
                    return 0;

                int diff = obj2.getPoints() - obj1.getPoints();

                if (diff == 0)
                {
                    diff = obj1.getGamesPlayed() - obj2.getGamesPlayed();

                    if (diff == 0)
                    {
                        diff = obj2.Wins - obj1.Wins;

                        if (diff == 0)
                        {
                            int teamBPoints = (int)(long)NHLStats.Instance.nhlDatabaseOperator.ScheduleAdapter.GetWinsVsTeam(obj2.Team.Name, obj1.Team.Name) +
                                (int)(long)NHLStats.Instance.nhlDatabaseOperator.ScheduleAdapter.GetLoserPointsVsTeam(obj2.Team.Name, obj1.Team.Name);

                            int teamAPoints = (int)(long)NHLStats.Instance.nhlDatabaseOperator.ScheduleAdapter.GetWinsVsTeam(obj1.Team.Name, obj2.Team.Name) +
                                (int)(long)NHLStats.Instance.nhlDatabaseOperator.ScheduleAdapter.GetLoserPointsVsTeam(obj1.Team.Name, obj2.Team.Name);

                            diff = teamBPoints - teamAPoints;

                            // could do more..
                        }
                    }
                }

                return diff;
            }
        }

        private List<TeamRecord> ConferenceRecords(string conference, List<INHLGameInfo> games, string season, 
                                                    int numberOfGames, TeamRecordFilterType filterType)
        {
            List<TeamRecord> standings = new List<TeamRecord>();

            DataRowCollection rows = nhlDatabaseOperator.GetDivisionTeams(conference).Rows;

            foreach (DataRow row in rows)
            {
                int teamGamesPlayed = numberOfGames;
                NHLTeam team = GetTeamFromTeamId((string)row["TeamID"]);

                int wins = 0, losses = 0, fakeLosses = 0;

                bool playedToday = false;
                foreach (INHLGameInfo game in games)
                {
                    if (game.GetStatus() == NHLGameStatus.GAME_FINAL)
                    {
                        if ((String.Compare(game.GetWinnerTeamName(), team.Name, true) == 0) ||
                            (String.Compare(game.GetLoserTeamName(), team.Name, true) == 0))
                        {
                            if (String.Compare(game.GetWinnerTeamName(), team.Name, true) == 0)
                            {
                                wins++;
                            }
                            else if (game.WasThreePointGame())
                            {
                                fakeLosses++;
                            }
                            else
                            {
                                losses++;
                            }

                            playedToday = true;
                            break;
                        }
                    }
                }

                if (playedToday)
                    teamGamesPlayed = teamGamesPlayed - 1;

                wins += nhlDatabaseOperator.GetWinsForTeam(GetSeasonID(season), teamGamesPlayed, filterType, team.Name);
                losses += nhlDatabaseOperator.GetLossesForTeam(GetSeasonID(season), teamGamesPlayed, filterType, team.Name);
                fakeLosses += nhlDatabaseOperator.GetFakeLossesForTeam(GetSeasonID(season), teamGamesPlayed, filterType, team.Name);

                TeamRecord record = new TeamRecord(team, wins, losses, fakeLosses);
                standings.Add(record);
            }

            standings.Sort(new StandingsSorter());

            return standings;
        }

        public string[] GetCommands()
        {
            lock (mutex)
            {
                string[] result = new string[playerStats.Columns.Count + 1];
                result[0] = "Overall player statistics: !top and !bot for:";

                for (int i = 1; i < playerStats.Columns.Count; i++)
                    result[i] = playerStats.Columns[i].ColumnName;

                return result;
            }
        }

        public static void FetchAliasesLoop(object nhlStats)
        {
            String name, alias;
            Hashtable playerAliasesCopy, teamAliasesCopy, goalieAliasesCopy;
            NHLStats stats = (NHLStats)nhlStats;
            Boolean update = false;

            int gameListUpdate = 0;


            for (; ; )
            {
                // TODO: Move this code to someplace more appropriate                
                
                //Every 6 hours
                if (gameListUpdate >= 60 * 60 * 6)
                {  
                    gameListUpdate = 0;
                }

                if (gameListUpdate == 0)
                {
                    String jsonText = NHLDotComFetch.GetScoreboardString();

                   
                    if (stats.jackedGameList == null)
                    {
                        // to catch crash; dont know why this would be null?
                        stats.jackedGameList = Program.DeserializeFromJson(jsonText);
                    }
                    else
                    {
                        lock (stats.jackedGameList)
                        {
                            //stats.jackedGameList = (XmlDocument)JavaScriptConvert.DeserializeXmlNode(jsonText);
                            stats.jackedGameList = Program.DeserializeFromJson(jsonText);
                        }
                    }
                }
                gameListUpdate++;

                lock (stats.fetchingAliasesMutex)
                {
                    update = stats.FetchAliases;
                }

                if (update)
                {

                    teamAliasesCopy = new Hashtable();
                    playerAliasesCopy = new Hashtable();
                    goalieAliasesCopy = new Hashtable();

                    lock (stats.fetchingAliasesMutex)
                    {
                        stats.fetchAliases = true;
                    }

                    NHLDotComFetch fetch = new NHLDotComFetch();
                    String raw = fetch.FetchWikiAliasString();
                    String xml = null;
                    XmlNodeList nodes;

                    if (raw != null)
                    {

                        raw = raw.Replace("&lt;", "<");
                        raw = raw.Replace("&gt;", ">");
                        raw = raw.Replace("&quot;", "\"");

                        System.Console.Write("Fetching aliases...");

                        try
                        {
                            if (raw.Contains("<Aliases>") && raw.Contains("</Aliases>"))
                            {
                                xml = raw.Substring(raw.IndexOf("<Aliases>"),
                                    raw.IndexOf("</Aliases>") - raw.IndexOf("<Aliases>") + 10);
                                XmlDocument document = new XmlDocument();
                                document.LoadXml(xml);

                                nodes = document.SelectNodes("//Aliases/Goalies/alias");
                                foreach (XmlNode node in nodes)
                                {
                                    name = node.Attributes["name"].Value;
                                    alias = node.InnerText.ToLower();
                                    goalieAliasesCopy.Add(alias, name);
                                }

                                nodes = document.SelectNodes("//Aliases/Players/alias");
                                foreach (XmlNode node in nodes)
                                {
                                    name = node.Attributes["name"].Value;
                                    alias = node.InnerText.ToLower();
                                    playerAliasesCopy.Add(alias, name);
                                }

                                nodes = document.SelectNodes("//Aliases/Teams/alias");
                                foreach (XmlNode node in nodes)
                                {
                                    name = node.Attributes["name"].Value;
                                    alias = node.InnerText.ToLower();
                                    teamAliasesCopy.Add(alias, name);
                                }
                            }

                            lock (stats.fetchingAliasesMutex)
                            {
                                stats.goalieAliases = goalieAliasesCopy;
                                stats.playerAliases = playerAliasesCopy;
                                stats.teamAliases = teamAliasesCopy;
                                stats.fetchAliases = false;
                                stats.fetchAliasesBroken = false;
                                System.Console.WriteLine("done");
                            }
                        }
                        catch (Exception e)
                        {
                            stats.fetchAliasesBroken = true;
                            System.Console.Error.WriteLine(e.Message);
                            //System.Console.Error.WriteLine(e.StackTrace);

                            lock (stats.fetchingAliasesMutex)
                            {
                                stats.fetchAliases = false;
                            }
                        }
                    }

                }

                lock (stats.fetchingAliasesMutex)
                {
                    if (!stats.running)
                        break;
                }

                Thread.Sleep(1000);
            }
        }        

    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Data;
using System.Data.SQLite;
using System.Data.Common;

namespace NHLScoreBot
{
    class NHLDatabaseOperator
    {
        SQLiteConnection connection;
        SQLiteTransaction transaction;
        NhlStatsSet nhlStatsDatabase;

        public NhlStatsSet NhlStatsDatabase
        {
            get { return nhlStatsDatabase; }    
        }
        NhlStatsSetTableAdapters.playerstatsTableAdapter playerStatsAdapter;

        public NhlStatsSetTableAdapters.playerstatsTableAdapter PlayerStatsAdapter
        {
            get { return playerStatsAdapter; }
        }
        NhlStatsSetTableAdapters.goaliestatsTableAdapter goalieStatsAdapter;

        public NhlStatsSetTableAdapters.goaliestatsTableAdapter GoalieStatsAdapter
        {
            get { return goalieStatsAdapter; }
        }
        NhlStatsSetTableAdapters.teamstatsTableAdapter teamStatsAdapter;

        public NhlStatsSetTableAdapters.teamstatsTableAdapter TeamStatsAdapter
        {
            get { return teamStatsAdapter; }
        }
        NhlStatsSetTableAdapters.seasonsTableAdapter seasonsAdapter;

        public NhlStatsSetTableAdapters.seasonsTableAdapter SeasonsAdapter
        {
            get { return seasonsAdapter; }
        }

        NhlStatsSetTableAdapters.scheduleTableAdapter scheduleAdapter;

        public NhlStatsSetTableAdapters.scheduleTableAdapter ScheduleAdapter
        {
            get { return scheduleAdapter; }
        }

        NhlStatsSetTableAdapters.teamsTableAdapter teamsAdapter;

        public NhlStatsSetTableAdapters.teamsTableAdapter TeamsAdapter
        {
            get { return teamsAdapter; }
        }

        public NHLDatabaseOperator()
        {            
            nhlStatsDatabase = new NhlStatsSet();            

            String dbPath = "nhlstatsdb";
            connection = new SQLiteConnection("Data Source=" + dbPath);
            connection.Open();

            playerStatsAdapter = new NHLScoreBot.NhlStatsSetTableAdapters.playerstatsTableAdapter();
            goalieStatsAdapter = new NHLScoreBot.NhlStatsSetTableAdapters.goaliestatsTableAdapter();
            teamStatsAdapter = new NHLScoreBot.NhlStatsSetTableAdapters.teamstatsTableAdapter();
            seasonsAdapter = new NHLScoreBot.NhlStatsSetTableAdapters.seasonsTableAdapter();
            scheduleAdapter = new NHLScoreBot.NhlStatsSetTableAdapters.scheduleTableAdapter();
            teamsAdapter = new NHLScoreBot.NhlStatsSetTableAdapters.teamsTableAdapter();

            playerStatsAdapter.Connection = connection;
            goalieStatsAdapter.Connection = connection;
            teamStatsAdapter.Connection = connection;
            scheduleAdapter.Connection = connection;
            teamsAdapter.Connection = connection;
        }

        public void ClearAll()
        {
            playerStatsAdapter.DeleteAll();
            teamStatsAdapter.DeleteAll();
            goalieStatsAdapter.DeleteAll();            
        }

        public NhlStatsSet.playerstatsDataTable QueryPlayerStats(String query)
        {
            NhlStatsSet result = new NhlStatsSet();
            DataAdapter adapter = new SQLiteDataAdapter(query, connection);
            adapter.Fill(result);

            return result.playerstats;
        }

        public long? GetSeasonID(String firstYear, String secondYear, bool playoffs)
        {
            long? result = null;

            DataTable table = seasonsAdapter.GetSeason(firstYear, secondYear, playoffs);
            if (table.Rows.Count > 0)
                result = (long)table.Rows[0]["Season_ID"];

            return result;
        }

        public NhlStatsSet.playerstatsDataTable GetEntirePlayers(String entry, bool top, bool playoffs)
        {
            NhlStatsSet.playerstatsDataTable table = new NhlStatsSet.playerstatsDataTable();
            SQLiteCommand command = new SQLiteCommand();
            command.Connection = connection;
            command.CommandText = String.Format(
                "SELECT * FROM playerstats INNER JOIN seasons ON playerstats.Season_ID = seasons.Season_ID " + 
                "WHERE seasons.Playoffs = '{2}' ORDER BY [{0}] {1} LIMIT 5", entry,
                top ? "DESC" : "ASC", playoffs ? "1" : "0");
            
            SQLiteDataAdapter adapter = new SQLiteDataAdapter(command);
            adapter.Fill(table);

            return table;    
        }

        public NhlStatsSet.goaliestatsDataTable GetEntireGoalies(String entry, bool top, bool playoffs)
        {
            NhlStatsSet.goaliestatsDataTable table = new NhlStatsSet.goaliestatsDataTable();
            SQLiteCommand command = new SQLiteCommand();
            command.Connection = connection;
            command.CommandText = String.Format(
                "SELECT * FROM goaliestats INNER JOIN seasons ON goaliestats.Season_ID = seasons.Season_ID " +
                "WHERE seasons.Playoffs = '{2}' ORDER BY [{0}] {1} LIMIT 5", entry,
                top ? "DESC" : "ASC", playoffs ? "1" : "0");

            SQLiteDataAdapter adapter = new SQLiteDataAdapter(command);
            adapter.Fill(table);

            return table;
        }

        public NhlStatsSet.goaliestatsDataTable GetGoaliesEx(String entry, long seasonId, bool top, String filter)
        {
            NhlStatsSet.goaliestatsDataTable table = new NhlStatsSet.goaliestatsDataTable();
            SQLiteCommand command = new SQLiteCommand();
            command.Connection = connection;
            command.CommandText = String.Format(
                "SELECT * FROM goaliestats " +
                "WHERE goaliestats.Season_ID = {0} {1} ORDER BY {2} {3}",
                seasonId, filter, entry, top ? "DESC" : "ASC");

            SQLiteDataAdapter adapter = new SQLiteDataAdapter(command);
            adapter.Fill(table);

            return table;
        }

        public NhlStatsSet.teamstatsDataTable GetEntireTeams(String entry, bool top, bool playoffs)
        {
            NhlStatsSet.teamstatsDataTable table = new NhlStatsSet.teamstatsDataTable();
            SQLiteCommand command = new SQLiteCommand();
            command.Connection = connection;
            command.CommandText = String.Format(
                "SELECT * FROM teamstats INNER JOIN seasons ON teamstats.Season_ID = seasons.Season_ID " +
                "WHERE seasons.Playoffs = '{2}' ORDER BY [{0}] {1} LIMIT 5", entry,
                top ? "DESC" : "ASC", playoffs ? "1" : "0");

            SQLiteDataAdapter adapter = new SQLiteDataAdapter(command);
            adapter.Fill(table);

            return table;
        }

        public NhlStatsSet.scheduleDataTable GetScheduleForTeam(String team, int seasonId, DateTime startDate, DateTime endDate)
        {
            string startDateString = startDate.ToString("s", System.Globalization.CultureInfo.InvariantCulture);
            string endDateString = endDate.ToString("s", System.Globalization.CultureInfo.InvariantCulture);

            NhlStatsSet.scheduleDataTable table = new NhlStatsSet.scheduleDataTable();
            SQLiteCommand command = new SQLiteCommand();
            command.Connection = connection;
            command.CommandText = String.Format(
                "SELECT * FROM schedule WHERE Season_ID = {0} AND (HomeTeam = '{1}' OR AwayTeam = '{1}') " + 
                "AND datetime(Date) >= datetime('{2}') AND datetime(Date) <= datetime('{3}')",
                    seasonId, team, startDateString, endDateString);

            SQLiteDataAdapter adapter = new SQLiteDataAdapter(command);
            adapter.Fill(table);

            return table;
        }

        public NhlStatsSet.scheduleDataTable GetScheduleForTeam(String team, int seasonId, int gameCount)
        {
            string startDateString = DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture);

            NhlStatsSet.scheduleDataTable table = new NhlStatsSet.scheduleDataTable();
            SQLiteCommand command = new SQLiteCommand();
            command.Connection = connection;
            command.CommandText = String.Format(
                "SELECT * FROM schedule WHERE Season_ID = {0} AND (HomeTeam = '{1}' OR AwayTeam = '{1}') " +
                "AND datetime(Date) >= datetime('{2}') LIMIT {3}",
                    seasonId, team, startDateString, gameCount);

            SQLiteDataAdapter adapter = new SQLiteDataAdapter(command);
            adapter.Fill(table);

            return table;
        }

        public NhlStatsSet.scheduleDataTable GetScheduleForTeams(String teamA, String teamB, int seasonId, int gameCount)
        {
            string startDateString = DateTime.Now.ToString("s", System.Globalization.CultureInfo.InvariantCulture);

            NhlStatsSet.scheduleDataTable table = new NhlStatsSet.scheduleDataTable();
            SQLiteCommand command = new SQLiteCommand();
            command.Connection = connection;
            command.CommandText = String.Format(
                "SELECT * FROM schedule WHERE Season_ID = {0} AND ((HomeTeam = '{1}' AND AwayTeam = '{2}') OR (HomeTeam = '{2}' AND AwayTeam = '{1}'))" +
                "AND datetime(Date) >= datetime('{3}') LIMIT {4}",
                    seasonId, teamA, teamB, startDateString, gameCount);

            SQLiteDataAdapter adapter = new SQLiteDataAdapter(command);
            adapter.Fill(table);

            return table;
        }

        public NhlStatsSet.scheduleDataTable GetScheduleByDate(int seasonId, DateTime startDate, DateTime endDate)
        {
            string startDateString = startDate.ToString("s", System.Globalization.CultureInfo.InvariantCulture);
            string endDateString = endDate.ToString("s", System.Globalization.CultureInfo.InvariantCulture);

            NhlStatsSet.scheduleDataTable table = new NhlStatsSet.scheduleDataTable();
            SQLiteCommand command = new SQLiteCommand();
            command.Connection = connection;
            command.CommandText = String.Format(
                "SELECT * FROM schedule WHERE Season_ID = {0} " +
                "AND datetime(Date) >= datetime('{1}') AND datetime(Date) <= datetime('{2}')",
                    seasonId, startDateString, endDateString);

            SQLiteDataAdapter adapter = new SQLiteDataAdapter(command);
            adapter.Fill(table);

            return table;
        }

        public NhlStatsSet.teamsDataTable GetExistantTeam(string team)
        {
            NhlStatsSet.teamsDataTable table = new NhlStatsSet.teamsDataTable();
            SQLiteCommand command = new SQLiteCommand();
            command.Connection = connection;
            command.CommandText = String.Format(
                "SELECT TeamID, TeamName, TeamCity, Conference, Division " +
                "FROM teams " +
                "WHERE (TeamName LIKE '%{0}%') OR " +
                "(TeamCity LIKE '%{0}%') AND (NOT (Conference = 'Nonexistent'))", team);

            SQLiteDataAdapter adapter = new SQLiteDataAdapter(command);
            adapter.Fill(table);

            return table;
        }

        public NhlStatsSet.teamsDataTable GetDivisionTeams(string division)
        {
            NhlStatsSet.teamsDataTable table = new NhlStatsSet.teamsDataTable();
            SQLiteCommand command = new SQLiteCommand();
            command.Connection = connection;
            command.CommandText = String.Format("SELECT * FROM (SELECT TeamName, TeamID, Division FROM teams WHERE " + 
                                                "(Division = '{0}') GROUP BY TeamName) GROUP BY TeamID", division); 
                       
            SQLiteDataAdapter adapter = new SQLiteDataAdapter(command);
            adapter.Fill(table);

            // System.Diagnostics.Debug.Assert(table.Rows.Count == 5); // meh

            return table;
        }

        public int GetWinsForTeam(long seasonId, int lastGames, NHLStats.TeamRecordFilterType filterType, string team)
        {            
            DataTable table = new DataTable();
            SQLiteCommand command = new SQLiteCommand();
            command.Connection = connection;
            command.CommandText = string.Format(
                "SELECT Date, Winner FROM schedule WHERE Season_ID = '{0}' AND (Winner = '{1}' OR LOSER = '{1}')", seasonId, team);

            if (filterType == NHLStats.TeamRecordFilterType.HOME)
                command.CommandText += string.Format(" AND HomeTeam = '{0}'", team);
            else if (filterType == NHLStats.TeamRecordFilterType.AWAY)
                command.CommandText += string.Format(" AND AwayTeam = '{0}'", team);

            command.CommandText += string.Format(" ORDER BY datetime(Date) DESC LIMIT {0}", lastGames);

            SQLiteDataAdapter adapter = new SQLiteDataAdapter(command);
            adapter.Fill(table);

            int wins = 0;
            foreach (DataRow row in table.Rows)
            {
                // don't double add game results
                if (IsRoughlyToday(Convert.ToDateTime(row["Date"])))
                    continue;

                if ((string)row["Winner"] == team)
                    wins++;
            }

            return wins;
        }

        private bool IsRoughlyToday(DateTime gameDate)
        {
            // because the games list will hang around for about two hours the next day, eg 12-2am
            if (gameDate.DayOfYear == DateTime.Now.DayOfYear ||
                (gameDate.DayOfYear + 1 == DateTime.Now.DayOfYear && DateTime.Now.Hour <= 1))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public int GetLossesForTeam(long seasonId, int lastGames, NHLStats.TeamRecordFilterType filterType, string team)
        {
            DataTable table = new DataTable();
            SQLiteCommand command = new SQLiteCommand();
            command.Connection = connection;
            command.CommandText = string.Format(
                "SELECT Date, Loser, EndType FROM schedule WHERE Season_ID = '{0}' AND (Winner = '{1}' OR LOSER = '{1}')", seasonId, team);

            if (filterType == NHLStats.TeamRecordFilterType.HOME)
                command.CommandText += string.Format(" AND HomeTeam = '{0}'", team);
            else if (filterType == NHLStats.TeamRecordFilterType.AWAY)
                command.CommandText += string.Format(" AND AwayTeam = '{0}'", team);

            command.CommandText += string.Format(" ORDER BY datetime(Date) DESC LIMIT {0}", lastGames);

            SQLiteDataAdapter adapter = new SQLiteDataAdapter(command);
            adapter.Fill(table);

            int losses = 0;
            foreach (DataRow row in table.Rows)
            {
                // don't double add game results
                if (IsRoughlyToday(Convert.ToDateTime(row["Date"])))
                    continue;

                if ((string)row["Loser"] == team && !((string)row["EndType"] == "OT" || (string)row["EndType"] == "SO"))
                    losses++;
            }

            return losses;
        }

        public int GetFakeLossesForTeam(long seasonId, int lastGames, NHLStats.TeamRecordFilterType filterType, string team)
        {
            DataTable table = new DataTable();
            SQLiteCommand command = new SQLiteCommand();
            command.Connection = connection;
            command.CommandText = string.Format(
                "SELECT Date, Loser, EndType FROM schedule WHERE Season_ID = '{0}' AND (Winner = '{1}' OR LOSER = '{1}')", seasonId, team);

            if (filterType == NHLStats.TeamRecordFilterType.HOME)
                command.CommandText += string.Format(" AND HomeTeam = '{0}'", team);
            else if (filterType == NHLStats.TeamRecordFilterType.AWAY)
                command.CommandText += string.Format(" AND AwayTeam = '{0}'", team);

            command.CommandText += string.Format(" ORDER BY datetime(Date) DESC LIMIT {0}", lastGames);

            SQLiteDataAdapter adapter = new SQLiteDataAdapter(command);
            adapter.Fill(table);

            int fakeLosses = 0;
            foreach (DataRow row in table.Rows)
            {
                // don't double add game results
                if (IsRoughlyToday(Convert.ToDateTime(row["Date"])))
                    continue;

                if ((string)row["Loser"] == team && ((string)row["EndType"] == "OT" || (string)row["EndType"] == "SO"))
                    fakeLosses++;
            }

            return fakeLosses;
        }

        public void ClearSeasonStats(int seasonId)
        {
            playerStatsAdapter.DeleteSeason(seasonId);
            goalieStatsAdapter.DeleteSeason(seasonId);
            teamStatsAdapter.DeleteSeason(seasonId);
            teamsAdapter.DeleteAll();
        }
             
        public void CommitToDatabase()
        {
            NhlStatsSet changes = (NhlStatsSet)nhlStatsDatabase.GetChanges();

            System.Console.Write("Commiting to SQLite database...");

            transaction = connection.BeginTransaction();
            playerStatsAdapter.Update(changes.playerstats);
            goalieStatsAdapter.Update(changes.goaliestats);            
            teamStatsAdapter.Update(changes.teamstats);
            scheduleAdapter.Update(changes.schedule);
            teamsAdapter.Update(changes.teams);
            transaction.Commit();
            transaction.Dispose();
            
            nhlStatsDatabase.playerstats.AcceptChanges();
            nhlStatsDatabase.goaliestats.AcceptChanges();
            nhlStatsDatabase.teamstats.AcceptChanges();
            nhlStatsDatabase.schedule.AcceptChanges();
            nhlStatsDatabase.teams.AcceptChanges();

            System.Console.WriteLine("done!");
        }

        ~NHLDatabaseOperator()
        {
            connection.Close();
        }

    }
}

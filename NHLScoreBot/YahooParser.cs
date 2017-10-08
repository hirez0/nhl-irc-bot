using System;
using System.Collections.Generic;
using System.Text;
using System.Data;

namespace NHLScoreBot
{
	public class ParserError : Exception
	{
		public ParserError(string message)
			: base(message)
		{
		}
	}

	public class TableParser
	{
		protected string TR_OPEN = "<tr";
		protected string TR_CLOSE = "</tr>";
		protected string TD_OPEN = "<td";
		protected string TD_CLOSE = "</td>";
		protected string TABLE_OPEN = "</table>\n</form>";
		protected string TABLE_CLOSE = "</table>";

		private static int SEARCH_LIMIT = 1024; // meh

		private int FindIndex(int startIndex, ref string hayStack, string needle)
		{
			if (hayStack.Length - startIndex > SEARCH_LIMIT)
				return hayStack.IndexOf(needle, startIndex, SEARCH_LIMIT, StringComparison.CurrentCultureIgnoreCase);
			else
				return hayStack.IndexOf(needle, startIndex, StringComparison.CurrentCultureIgnoreCase);
		}

		private bool ShouldAddColumn(string name)
		{
			bool result = true;

			if (name.Trim() == "&nbsp;")
				result = false;

			return result;
		}

		protected string StripAllButTable(string html)
		{
			string result = "";
            int tableClose, tableOpen = html.IndexOf(TABLE_OPEN);
            if (tableOpen != -1)
            {
                tableClose = html.IndexOf(TABLE_CLOSE, tableOpen + TABLE_OPEN.Length);
                if (tableClose != -1)
                {
                    result = html.Substring(tableOpen, (tableClose - tableOpen) + TABLE_CLOSE.Length);
                }
            }
			return result;
		}

		protected DataTable ParseHTMLTable(string htmlTable, Type defaultType)
		{
			DataTable result = null;
			string table = htmlTable;
			bool firstRow = true;
			int index = 0;
			List<int> badColumnIndex = new List<int>();
			int badCount = 0;

			if (table.Length > 0)
			{
				result = new DataTable();

				//todo: get rid of <table ...>

				int tableOpen = FindIndex(index, ref table, "<table ");
				if (tableOpen >= 0)
				{
					index = tableOpen;
					int tableClose = FindIndex(index, ref table, ">");
					if (tableClose >= 0)
					{
						//table = table.Substring(tableClose + 1);
						index = tableClose + 1;
					}
					else
					{
						throw new ParserError("Table tag malformed");
					}
				}
				else
				{
					throw new ParserError("Table tag missing");
				}

				for (; ; )
				{
					int trOpenStart = FindIndex(index, ref table, TR_OPEN);
					if (trOpenStart >= 0)
					{
						int trOpenEnd = FindIndex(trOpenStart, ref table, ">");  // meh
						if (trOpenEnd < 0)
							throw new ParserError("tr tag malformed");
						index = trOpenEnd + 1;

						DataRow row = result.NewRow();
						int trClose;
						badCount = 0;
						for (int columnIndex = 0; ; columnIndex++)
						{
							// System.Diagnostics.Debug.Print("index {0}: {1}", index, table.Substring(index, Math.Min(50, table.Length - index))); // meh
							int tdOpenStart = FindIndex(index, ref table, TD_OPEN);

							if (FindIndex(index, ref table, TD_CLOSE) < tdOpenStart)
								throw new ParserError(string.Format("Mismatching td close at {0}", index));

							trClose = FindIndex(index, ref table, TR_CLOSE);

							if (tdOpenStart >= 0)
							{
								if (trClose >= 0 && tdOpenStart > trClose)
									break;

								int tdOpenEnd = FindIndex(index, ref table, ">");
								if (tdOpenEnd < 0)
									throw new ParserError(string.Format("td tag malformed at {0}", index));

								index = tdOpenEnd + 1;

								int tdClose = FindIndex(index, ref table, TD_CLOSE);
								if (tdClose < 0)
									throw new ParserError(string.Format("</td> tag not found at {0}", index));

								string tdData = table.Substring(index, tdClose - index);
								string strippedData = System.Text.RegularExpressions.Regex.Replace(tdData, "<(.|\n)+?>", string.Empty);
								strippedData = strippedData.Replace("&nbsp;", string.Empty);

								if (firstRow)
								{
									if (ShouldAddColumn(tdData))
									{
										DataColumn column = new DataColumn(strippedData);
										if (strippedData.ToLower() != "name" && strippedData.ToLower() != "team")
											column.DataType = defaultType;
										//column.DataType = Type.GetType("System.Decimal");
										// System.Diagnostics.Debug.Print("added column #{0} {1} at {2}", columnIndex, column, index); // meh
										result.Columns.Add(column);
									}
									else
										badColumnIndex.Add(columnIndex);
								}
								else
								{
									if (badColumnIndex.Contains(columnIndex))
										badCount++;
									else
									{
										if (string.Compare(strippedData, "n/a", true) != 0)
											row[columnIndex - badCount] = strippedData;
										else
											row[columnIndex - badCount] = 0;

										//System.Diagnostics.Debug.Print("added data #{0} {1} at {2}", columnIndex, strippedData, index); // meh
									}
								}

								index = tdClose + TD_CLOSE.Length;
							}
							else
							{
								break;
							}
						}

						if (trClose >= 0)
						{
							index = trClose + TR_CLOSE.Length;
						}
						else
						{
							//throw new ParserError("</tr> tag not found");
						}

						if (firstRow)
							firstRow = false;
						else
							result.Rows.Add(row);
					}
					else
					{
						break;
					}
				}

			}

			return result;
		}
	}

	public class NHLScheduleParser : TableParser
	{
		public NHLScheduleParser()
		{
			TABLE_OPEN = "<table class=\"data schedTbl\">";
		}

		public DataTable ParseNHLScheduleTable(string html)
		{
			DataTable result = null;

			string table = StripAllButTable(html);
			table = table.Replace("<th", "<td");
			table = table.Replace("</th", "</td");
			if (table.Length > 0)
				result = ParseHTMLTable(table, Type.GetType("System.String"));

            // tt jan 2014 - nhl.com schedule page has two separate tables for upcoming and completed games
            //  add new processing that gets an additional set of results from the html string based on the
            //  location of the second table open tag
            DataTable result2 = null;
            string table2 = StripAllButTable(html.Substring(html.LastIndexOf(TABLE_OPEN)));
            table2 = table2.Replace("<th", "<td");
			table2 = table2.Replace("</th", "</td");
			if (table2.Length > 0)
				result2 = ParseHTMLTable(table2, Type.GetType("System.String"));

            // merge the results from the second table into the original results
            // the first table is upcoming games, the second table is completed games
            result.Merge(result2);

			result.Columns.Add("DateTime", Type.GetType("System.DateTime"));
			result.Columns.Add("TBD", Type.GetType("System.Boolean"));
			result.Columns.Add("Winner", Type.GetType("System.String"));
			result.Columns.Add("Loser", Type.GetType("System.String"));
			result.Columns.Add("WinnerScore", Type.GetType("System.Int16"));
			result.Columns.Add("LoserScore", Type.GetType("System.Int16"));
			result.Columns.Add("EndType", Type.GetType("System.String"));

			foreach (DataRow row in result.Rows)
			{
				bool toBeDetermined = false;

				// string time = (string)row["TIME"];
				string time = row["TIME"] as string; // meh
				if (time == null)
					continue;

				time = time.Replace("\t", string.Empty);
				time = time.Replace("\n", string.Empty);

				if (string.Compare("TBD", time, true) == 0 || string.Compare("12:00 AM ET", time) == 0 || time.Contains("PPD") || time.Contains("TBD"))
				{
					toBeDetermined = true;
				}
				else
				{
                    if (time.IndexOf("*") > 0)
                    {
                        time = time.Replace("*", "");
                    }
					time = time.Substring(0, time.IndexOf("ET") + 2);
					time = time.Replace("ET", string.Empty);
				}

				string date = (string)row["DATE"];
				date = date.Replace("\t", string.Empty);
				date = date.Replace("\n", string.Empty);
				//Tue Oct 1, 2013Tue Oct 1, 2013
				//SatOct 4, 2008
				//date = date.Substring(0, 3) + " " + date.Substring(3);
				date = date.Substring(0, date.Length / 2); // meh

				string info = (string)row["NETWORK/RESULT"];
				info = info.Replace("\t", string.Empty);
				info = info.Replace("\n", string.Empty);

				string winner = string.Empty, loser = string.Empty;
				int winnerScore = 0, loserScore = 0;
				string endType = string.Empty;

				string homeTeamTemp = ((string)row["HOME TEAM"]).Trim();
				string awayTeamTemp = ((string)row["VISITING TEAM"]).Trim();

				if (homeTeamTemp.IndexOf('(') > 0)
				{
					homeTeamTemp = homeTeamTemp.Substring(0, homeTeamTemp.IndexOf('(')).Trim();
				}

				if (awayTeamTemp.IndexOf('(') > 0)
				{
					awayTeamTemp = awayTeamTemp.Substring(0, awayTeamTemp.IndexOf('(')).Trim();
				}

				row["HOME TEAM"] = homeTeamTemp;
				row["VISITING TEAM"] = awayTeamTemp;

				if (info.StartsWith("FINAL"))
				{
					string endTemp = info.TrimEnd().Substring(info.Length - 2);
					if (endTemp == "OT")
						endType = "OT";
					else if (endTemp == "/O") // tt jan 2014: current NHL page now uses S/O for shootouts instead of SO
						endType = "SO";
					else
						endType = string.Empty;


					int aScore = Convert.ToInt16(info.Substring(info.IndexOf('(') + 1, info.IndexOf(')') - info.IndexOf('(') - 1));
					int bScore = Convert.ToInt16(info.Substring(info.LastIndexOf('(') + 1, info.LastIndexOf(')') - info.LastIndexOf('(') - 1));

					if (aScore > bScore)
					{
						winner = (string)row["VISITING TEAM"];
						loser = (string)row["HOME TEAM"];

						winnerScore = aScore;
						loserScore = bScore;

					}
					else if (bScore > aScore)
					{
						loser = (string)row["VISITING TEAM"];
						winner = (string)row["HOME TEAM"];

						loserScore = aScore;
						winnerScore = bScore;
					}
					else
					{
						throw new Exception("scores equal");
					}
				}

				DateTime dateTime = DateTime.MinValue;

				try
				{
					if (!toBeDetermined)
					{
						dateTime = DateTime.Parse(time + " " + date);
					}

					row["DateTime"] = dateTime;
					row["TBD"] = toBeDetermined;
					row["NETWORK/RESULT"] = info;
					row["Winner"] = winner;
					row["Loser"] = loser;
					row["WinnerScore"] = winnerScore;
					row["LoserScore"] = loserScore;
					row["EndType"] = endType;
				}
				catch (Exception ex)
				{
					//throw;
					System.Console.WriteLine("Exception: " + ex.Message + ex.StackTrace);
				}
			}

			return result;
		}
	}

	public class YahooParser : TableParser
	{
		public YahooParser()
		{
			TABLE_OPEN = "</table>\n</form>";
		}

		public DataTable ParseYahooStatsHTMLTable(string html)
		{
			DataTable result = null;

			string table = StripAllButTable(html);
			if (table.Length > 0)
				result = ParseHTMLTable(table, Type.GetType("System.Decimal"));

			return result;
		}

	}
}
